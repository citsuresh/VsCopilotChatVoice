#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using VsCopilotChatVoice.Ipc;

// Only the legacy preview/probe uses synchronous wrappers. Migrated presentation uses asynchronous commands.
#pragma warning disable VSTHRD002

namespace VsCopilotChatVoice.Speech;

// Host side of the speech worker pipe. The IDE only presents text and progress; the launched worker process
// owns synthesis. Events carry a generation, and anything that is not the one active generation is dropped,
// so a late worker message can never revive cancelled, completed or replaced speech.
internal sealed class SpeechWorkerHost : IDisposable
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    private readonly NamedPipeServerStream pipe;
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private readonly object sync = new object();
    private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
    private int? expectedClientProcessId;
    private Process? process;
    private long generation;
    private long cancellationEpoch;
    private bool active;
    private int activeLength;
    private bool disposed;
    private bool faulted;
    private bool connected;
    private bool paused;
    private long command;
    private StateRequest? pendingState;

    private sealed class StateRequest
    {
        public long Generation;
        public long Command;
        public bool Paused;
        public readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public event Action<long, int>? Progress;
    public event Action<long>? Completed;
    public event Action<string>? Faulted;
    public event Action<long, bool>? StateChanged;

    // Tests pass null to drive the pipe with a fake worker; production always supplies the launched process id.
    internal SpeechWorkerHost(string pipeName, int? expectedClientProcessId)
    {
        if (!WorkerProtocol.IsValidPipeName(pipeName)) throw new ArgumentException("Invalid speech pipe name.", nameof(pipeName));
        this.expectedClientProcessId = expectedClientProcessId;
        pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 262144, PipePeerIdentity.UserOnlySecurity());
    }

    public bool IsActive { get { lock (sync) return active && !disposed && !faulted; } }
    public bool IsPaused { get { lock (sync) return active && paused && !disposed && !faulted; } }

    public long CurrentGeneration { get { lock (sync) return generation; } }

    public static Task<SpeechWorkerHost> LaunchAsync(string workerExecutable, bool silent, TimeSpan connectTimeout) =>
        LaunchAsync(workerExecutable, silent, connectTimeout, CancellationToken.None);

    public static async Task<SpeechWorkerHost> LaunchAsync(string workerExecutable, bool silent, TimeSpan connectTimeout, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!File.Exists(workerExecutable)) throw new FileNotFoundException("The speech worker executable was not found.", workerExecutable);
        string name = WorkerProtocol.NewPipeName();
        var start = new ProcessStartInfo(workerExecutable, "--speech-worker " + name + (silent ? " --silent" : ""))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // The server pipe exists before the worker starts so the worker cannot connect to a squatter's name first.
        var host = new SpeechWorkerHost(name, null);
        try
        {
            host.Attach(Process.Start(start) ?? throw new InvalidOperationException("The speech worker did not start."));
            await host.ConnectAsync(connectTimeout, cancellation).ConfigureAwait(false);
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    public Task ConnectAsync(TimeSpan timeout) => ConnectAsync(timeout, CancellationToken.None);

    public async Task ConnectAsync(TimeSpan timeout, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellation);
        deadline.CancelAfter(timeout);
        using var closeOnCancellation = deadline.Token.Register(() => pipe.Dispose());
        try { await pipe.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException || (deadline.IsCancellationRequested &&
            (ex is IOException || ex is ObjectDisposedException)))
        {
            Dispose();
            if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
            throw new TimeoutException("The speech worker did not connect in time.");
        }
        deadline.Token.ThrowIfCancellationRequested();
        if (expectedClientProcessId.HasValue)
        {
            bool expected;
            try { expected = PipePeerIdentity.Client(pipe) == (uint)expectedClientProcessId.Value; }
            catch (System.ComponentModel.Win32Exception) { expected = false; }
            if (!expected)
            {
                Dispose();
                throw new UnauthorizedAccessException("An unexpected process connected to the speech pipe.");
            }
        }
        lock (sync) { if (disposed) throw new ObjectDisposedException(nameof(SpeechWorkerHost)); connected = true; }
        _ = Task.Run(ReadLoopAsync);
    }

    internal void Attach(Process worker)
    {
        process = worker;
        WorkerProcessId = worker.Id;
        expectedClientProcessId = worker.Id;
    }

    internal int? WorkerProcessId { get; private set; }

    public long Speak(string text) => SpeakAsync(text).GetAwaiter().GetResult();

    public async Task<long> SpeakAsync(string text, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > WorkerProtocol.MaxSpeakChars)
            throw new ArgumentException("Readable text of bounded length is required.", nameof(text));
        cancellation.ThrowIfCancellationRequested();
        long startEpoch;
        lock (sync) { EnsureUsable(); startEpoch = cancellationEpoch; }
        using var timeout = new CancellationTokenSource(WriteTimeout);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellation, timeout.Token);
        bool acquired = false;
        try
        {
            await sendGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            acquired = true;
            long gen;
            lock (sync) { EnsureUsable(); gen = generation + 1; }
            byte[] bytes = WorkerProtocol.Serialize(new WorkerMessage { Type = WorkerProtocol.Speak, Generation = gen, Text = text });
            lock (sync)
            {
                EnsureUsable();
                deadline.Token.ThrowIfCancellationRequested();
                if (startEpoch != cancellationEpoch) throw new OperationCanceledException("The queued speech start was cancelled.");
                generation = gen;
                CancelPendingState();
                active = true;
                paused = false;
                activeLength = text.Length;
            }
            await WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
            return gen;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellation.IsCancellationRequested)
        {
            Fault("The speech command timed out.");
            throw new TimeoutException("The speech command timed out.");
        }
        finally { if (acquired) sendGate.Release(); }
    }

    // Returns false when nothing was active. Events for the cancelled generation are dropped immediately,
    // before the worker confirms, so the window can show Cancelled without waiting for the process.
    public bool Cancel()
    {
        try { return CancelAsync().GetAwaiter().GetResult(); }
        catch (TimeoutException ex) { throw new InvalidOperationException("The speech worker could not be reached.", ex); }
    }

    public async Task<bool> CancelAsync(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        long gen;
        lock (sync)
        {
            if (disposed || faulted) return false;
            cancellationEpoch++;
            if (!active) return false;
            active = false;
            paused = false;
            gen = generation;
            CancelPendingState();
        }
        using var timeout = new CancellationTokenSource(WriteTimeout);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellation, timeout.Token);
        bool acquired = false;
        try
        {
            await sendGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            acquired = true;
            await WriteAsync(WorkerProtocol.Serialize(new WorkerMessage { Type = WorkerProtocol.Cancel, Generation = gen }), deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellation.IsCancellationRequested)
        {
            Fault("The speech cancellation timed out.");
            throw new TimeoutException("The speech cancellation timed out.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Fault("The speech cancellation was interrupted; the worker connection was closed.");
            throw;
        }
        finally { if (acquired) sendGate.Release(); }
    }

    public Task PauseAsync(long gen, CancellationToken cancellation = default) => ChangeStateAsync(gen, true, cancellation);
    public Task ResumeAsync(long gen, CancellationToken cancellation = default) => ChangeStateAsync(gen, false, cancellation);

    private async Task ChangeStateAsync(long gen, bool pause, CancellationToken cancellation)
    {
        if (gen < 1) throw new ArgumentOutOfRangeException(nameof(gen));
        cancellation.ThrowIfCancellationRequested();
        lock (sync) { EnsureUsable(); }
        using var timeout = new CancellationTokenSource(WriteTimeout);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellation, timeout.Token);
        bool acquired = false;
        StateRequest? request = null;
        try
        {
            await sendGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            acquired = true;
            lock (sync)
            {
                EnsureUsable();
                if (!active || gen != generation) throw new InvalidOperationException("The speech generation is no longer active.");
                if (pendingState != null) throw new InvalidOperationException("A speech state change is already pending.");
                if (paused == pause) return;
                request = new StateRequest { Generation = gen, Command = ++command, Paused = pause };
                pendingState = request;
            }
            await WriteAsync(WorkerProtocol.Serialize(new WorkerMessage
            {
                Type = pause ? WorkerProtocol.Pause : WorkerProtocol.Resume,
                Generation = gen, Command = request.Command,
            }), deadline.Token).ConfigureAwait(false);
            sendGate.Release();
            acquired = false;
            using var cancelWait = deadline.Token.Register(() => request.Completion.TrySetCanceled());
#pragma warning disable VSTHRD003 // The pipe reader completes this deadline-bounded task without UI work.
            await request.Completion.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested || cancellation.IsCancellationRequested)
        {
            Fault("The speech state acknowledgement timed out or was cancelled.");
            if (cancellation.IsCancellationRequested) throw;
            throw new TimeoutException("The speech state acknowledgement timed out.");
        }
        finally
        {
            if (acquired) sendGate.Release();
            lock (sync) { if (pendingState == request) pendingState = null; }
        }
    }

    private void CancelPendingState()
    {
        pendingState?.Completion.TrySetCanceled();
        pendingState = null;
    }

    private void EnsureUsable()
    {
        if (disposed) throw new ObjectDisposedException(nameof(SpeechWorkerHost));
        if (faulted) throw new InvalidOperationException("The speech worker is no longer available.");
        if (!connected) throw new InvalidOperationException("The speech worker is not connected.");
    }

    private async Task WriteAsync(byte[] bytes, CancellationToken cancellation)
    {
        using var closeOnCancellation = cancellation.Register(() => pipe.Dispose());
        try
        {
            await pipe.WriteAsync(bytes, 0, bytes.Length, cancellation).ConfigureAwait(false);
            await pipe.FlushAsync(cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is TimeoutException ||
            ex is AggregateException || ex is OperationCanceledException)
        {
            bool cancelled = cancellation.IsCancellationRequested;
            Fault("The speech worker could not be reached.");
            if (cancelled) throw new OperationCanceledException(cancellation);
            throw new InvalidOperationException("The speech worker could not be reached.", ex);
        }
    }

    private async Task ReadLoopAsync()
    {
        var reader = new BoundedLineReader(pipe);
        try
        {
            while (true)
            {
                string? line = await reader.ReadLineAsync(lifetime.Token).ConfigureAwait(false);
                if (line == null) { Fault("The speech worker disconnected."); return; }
                WorkerMessage? message = WorkerProtocol.TryParse(line);
                if (message == null) { Fault("The speech worker sent an invalid message."); return; }
                if (!Handle(message)) return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidDataException)
        {
            Fault("The speech worker connection failed.");
        }
    }

    // Returns false when the connection must end. Handlers run outside the lock.
    private bool Handle(WorkerMessage message)
    {
        Action? raise = null;
        bool violation = false;
        lock (sync)
        {
            if (disposed || faulted) return false;
            switch (message.Type)
            {
                case WorkerProtocol.Progress:
                    if (active && !paused && message.Generation == generation && message.Offset < activeLength)
                    {
                        long gen = message.Generation; int offset = message.Offset;
                        raise = () => Progress?.Invoke(gen, offset);
                    }
                    break;
                case WorkerProtocol.Completed:
                    if (active && message.Generation == generation)
                    {
                        active = false;
                        paused = false;
                        CancelPendingState();
                        long gen = message.Generation;
                        raise = () => Completed?.Invoke(gen);
                    }
                    break;
                case WorkerProtocol.Paused:
                case WorkerProtocol.Resumed:
                    StateRequest? request = pendingState;
                    if (active && request != null && message.Generation == generation && message.Generation == request.Generation &&
                        message.Command == request.Command && (message.Type == WorkerProtocol.Paused) == request.Paused)
                    {
                        paused = request.Paused;
                        pendingState = null;
                        bool value = paused;
                        long gen = generation;
                        request.Completion.TrySetResult(true);
                        raise = () => StateChanged?.Invoke(gen, value);
                    }
                    break;
                case WorkerProtocol.Cancelled:
                    // A confirmation of our own cancel is expected and ignored; an unrequested stop is a fault.
                    if (active && message.Generation == generation) { active = false; paused = false; CancelPendingState(); raise = () => Faulted?.Invoke("The speech worker stopped speech unexpectedly."); }
                    break;
                case WorkerProtocol.Error:
                    if ((message.Command == 0 || (pendingState != null && message.Command == pendingState.Command)) &&
                        (message.Generation == 0 || (active && message.Generation == generation)))
                    {
                        active = false;
                        paused = false;
                        CancelPendingState();
                        string text = message.Message ?? "Speech failed.";
                        raise = () => Faulted?.Invoke(text);
                    }
                    break;
                default:
                    // Speak, cancel and shutdown only flow from the host to the worker.
                    violation = true;
                    break;
            }
        }
        if (violation) { Fault("The speech worker sent an unexpected message."); return false; }
        raise?.Invoke();
        return true;
    }

    private void Fault(string reason)
    {
        lock (sync)
        {
            if (disposed || faulted) return;
            faulted = true;
            active = false;
            paused = false;
            CancelPendingState();
        }
        Faulted?.Invoke(reason);
        Teardown(false);
    }

    public void Dispose()
    {
        bool wasConnected;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            active = false;
            paused = false;
            CancelPendingState();
            wasConnected = connected && !faulted;
        }
        // The shutdown handshake and process wait can take seconds, so they never run on the caller (UI) thread.
        // If this never completes, the worker still exits when it sees the pipe close.
        _ = Task.Run(() =>
        {
            if (wasConnected)
            {
                try
                {
                    byte[] shutdown = WorkerProtocol.Serialize(new WorkerMessage { Type = WorkerProtocol.Shutdown });
                    pipe.WriteAsync(shutdown, 0, shutdown.Length).Wait(500);
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is AggregateException) { }
            }
            Teardown(true);
        });
    }

    private void Teardown(bool graceful)
    {
        lifetime.Cancel();
        pipe.Dispose();
        Process? worker = process;
        if (worker == null) return;
        try
        {
            if (!worker.HasExited && !(graceful && worker.WaitForExit(2000))) worker.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception) { }
        worker.Dispose();
    }

}
