#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using VsCopilotChatVoice.Ipc;

// Pipe writes are tiny and bounded by WriteTimeout; the other waits run on background threads.
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
    private readonly object sendLock = new object();
    private int? expectedClientProcessId;
    private Process? process;
    private long generation;
    private bool active;
    private int activeLength;
    private bool disposed;
    private bool faulted;
    private bool connected;

    public event Action<long, int>? Progress;
    public event Action<long>? Completed;
    public event Action<string>? Faulted;

    // Tests pass null to drive the pipe with a fake worker; production always supplies the launched process id.
    internal SpeechWorkerHost(string pipeName, int? expectedClientProcessId)
    {
        if (!WorkerProtocol.IsValidPipeName(pipeName)) throw new ArgumentException("Invalid speech pipe name.", nameof(pipeName));
        this.expectedClientProcessId = expectedClientProcessId;
        pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 262144, PipePeerIdentity.UserOnlySecurity());
    }

    public bool IsActive { get { lock (sync) return active && !disposed && !faulted; } }

    public long CurrentGeneration { get { lock (sync) return generation; } }

    public static async Task<SpeechWorkerHost> LaunchAsync(string workerExecutable, bool silent, TimeSpan connectTimeout)
    {
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
            await host.ConnectAsync(connectTimeout).ConfigureAwait(false);
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    public async Task ConnectAsync(TimeSpan timeout)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(timeout);
        try { await pipe.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            Dispose();
            throw new TimeoutException("The speech worker did not connect in time.");
        }
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

    public long Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > WorkerProtocol.MaxSpeakChars)
            throw new ArgumentException("Readable text of bounded length is required.", nameof(text));
        lock (sendLock)
        {
            long gen;
            lock (sync)
            {
                EnsureUsable();
                gen = ++generation;
                active = true;
                activeLength = text.Length;
            }
            Write(new WorkerMessage { Type = WorkerProtocol.Speak, Generation = gen, Text = text });
            return gen;
        }
    }

    // Returns false when nothing was active. Events for the cancelled generation are dropped immediately,
    // before the worker confirms, so the window can show Cancelled without waiting for the process.
    public bool Cancel()
    {
        lock (sendLock)
        {
            long gen;
            lock (sync)
            {
                if (disposed || faulted || !active) return false;
                active = false;
                gen = generation;
            }
            Write(new WorkerMessage { Type = WorkerProtocol.Cancel, Generation = gen });
            return true;
        }
    }

    private void EnsureUsable()
    {
        if (disposed) throw new ObjectDisposedException(nameof(SpeechWorkerHost));
        if (faulted) throw new InvalidOperationException("The speech worker is no longer available.");
        if (!connected) throw new InvalidOperationException("The speech worker is not connected.");
    }

    private void Write(WorkerMessage message)
    {
        byte[] bytes = WorkerProtocol.Serialize(message);
        try
        {
            Task write = pipe.WriteAsync(bytes, 0, bytes.Length, lifetime.Token);
            if (!write.Wait(WriteTimeout)) throw new TimeoutException("The speech worker stopped reading.");
            pipe.Flush();
        }
        catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is TimeoutException ||
            ex is AggregateException || ex is OperationCanceledException)
        {
            Fault("The speech worker could not be reached.");
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
                    if (active && message.Generation == generation && message.Offset < activeLength)
                    {
                        long gen = message.Generation; int offset = message.Offset;
                        raise = () => Progress?.Invoke(gen, offset);
                    }
                    break;
                case WorkerProtocol.Completed:
                    if (active && message.Generation == generation)
                    {
                        active = false;
                        long gen = message.Generation;
                        raise = () => Completed?.Invoke(gen);
                    }
                    break;
                case WorkerProtocol.Cancelled:
                    // A confirmation of our own cancel is expected and ignored; an unrequested stop is a fault.
                    if (active && message.Generation == generation) { active = false; raise = () => Faulted?.Invoke("The speech worker stopped speech unexpectedly."); }
                    break;
                case WorkerProtocol.Error:
                    if (message.Generation == 0 || (active && message.Generation == generation))
                    {
                        active = false;
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
