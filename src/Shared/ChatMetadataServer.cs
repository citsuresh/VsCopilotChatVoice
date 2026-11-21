#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VsCopilotChatVoice.Ipc;
using VsCopilotChatVoice.Speech;

namespace VsCopilotChatVoice.Bridge;

internal sealed class ChatMetadataServer : IDisposable
{
    private readonly object sync = new object();
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private readonly string pipeName;
    private readonly TimeSpan timeout;
    private readonly Func<CancellationToken, Task<BridgeTarget[]>> capture;
    private readonly Action<Exception> reportError;
    private NamedPipeServerStream? activePipe;
    private bool disposed;

    internal Guid InstanceId { get; } = Guid.NewGuid();
    internal Task Completion { get; }

    internal ChatMetadataServer(string pipeName, Func<CancellationToken, Task<BridgeTarget[]>> capture,
        Action<Exception> reportError, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || !pipeName.StartsWith("VsCopilotChatVoice.Metadata.", StringComparison.Ordinal) ||
            pipeName.Length > 200 || pipeName.Any(c => c == '\\' || char.IsControl(c)))
            throw new ArgumentException("Invalid metadata pipe name.", nameof(pipeName));
        this.pipeName = pipeName;
        this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        this.reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        this.timeout = timeout ?? TimeSpan.FromSeconds(15);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(timeout));
        activePipe = CreatePipe();
        Completion = Task.Run(RunAsync);
    }

    private NamedPipeServerStream CreatePipe() => new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, BridgeProtocol.MaxBytes, PipePeerIdentity.UserOnlySecurity());

    private async Task RunAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                NamedPipeServerStream pipe;
                lock (sync)
                {
                    if (disposed) return;
                    pipe = activePipe ?? (activePipe = CreatePipe());
                }
                using (pipe)
                {
                    try
                    {
                        await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        deadline.CancelAfter(timeout);
                        // net48 pipe reads may ignore cancellation until the owned stream is closed.
                        using (deadline.Token.Register(pipe.Dispose))
                            await ServeAsync(pipe, deadline.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is OperationCanceledException ||
                        ex is System.ComponentModel.Win32Exception || ex is InvalidDataException || ex is DecoderFallbackException)
                    {
                        if (!lifetime.IsCancellationRequested) reportError(ex);
                    }
                    finally { lock (sync) { if (ReferenceEquals(activePipe, pipe)) activePipe = null; } }
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
        {
            reportError(ex);
            throw;
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellation)
    {
        uint peer = PipePeerIdentity.Client(pipe);
        var reader = new BoundedLineReader(pipe, BridgeProtocol.MaxBytes, true);
        var seen = new HashSet<Guid>();
        int sequence = 0;
        bool hello = false;
        while (!cancellation.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellation).ConfigureAwait(false);
            if (line == null) return;
            BridgeRequest request;
            try { request = BridgeProtocol.ParseRequest(line); }
            catch (InvalidDataException ex)
            {
                reportError(ex);
                await WriteAsync(pipe, Reply(null, "Malformed"), cancellation).ConfigureAwait(false);
                return;
            }
            string? failure = request.Version != BridgeProtocol.Version ? "UnsupportedVersion" :
                request.ClientPid != peer ? "PeerMismatch" :
                seen.Contains(request.RequestId) ? "DuplicateRequest" :
                request.Sequence != sequence + 1 ? "OutOfOrder" :
                seen.Count >= BridgeProtocol.MaxRequests ? "CapacityExceeded" : null;
            if (failure != null)
            {
                reportError(new InvalidDataException("Metadata request rejected: " + failure));
                await WriteAsync(pipe, Reply(request, failure), cancellation).ConfigureAwait(false);
                return;
            }
            seen.Add(request.RequestId);
            sequence = request.Sequence;
            BridgeResponse response;
            if (request.Operation == "hello")
            {
                response = Reply(request, !hello && request.InstanceId == Guid.Empty && request.SessionId == Guid.Empty &&
                    request.Epoch == Guid.Empty && request.ClientId == null ? "Ok" : "HelloRequired");
                hello = response.Code == "Ok";
            }
            else if (!hello) response = Reply(request, "HelloRequired");
            else if (request.InstanceId != InstanceId) response = Reply(request, "WrongInstance");
            else if (request.Operation != "list" && request.Operation != "status") response = Reply(request, "UnknownOperation");
            else if (request.Operation == "list" && (request.SessionId != Guid.Empty || request.Epoch != Guid.Empty || request.ClientId != null))
                response = Reply(request, "UnknownTarget");
            else if (request.Operation == "status" && (request.SessionId == Guid.Empty || request.Epoch == Guid.Empty || !BridgeProtocol.ValidClientId(request.ClientId)))
                response = Reply(request, "UnknownTarget");
            else
            {
                try
                {
                    BridgeTarget[] targets = await CaptureAsync(cancellation).ConfigureAwait(false);
                    if (targets.Length > BridgeProtocol.MaxTargets) response = Reply(request, "CapacityExceeded");
                    else
                    {
                        foreach (BridgeTarget target in targets) (target ?? throw new InvalidDataException("Null target metadata.")).Validate();
                        BridgeTarget[] selected = request.Operation == "list" ? targets : targets.Where(t => t.Matches(request)).ToArray();
                        response = Reply(request, request.Operation == "status" && selected.Length != 1 ?
                            selected.Length == 0 ? "UnknownTarget" : "AmbiguousTarget" : "Ok");
                        if (response.Code == "Ok") response.Targets = selected;
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException || ex is ArgumentException ||
                    ex is System.Reflection.AmbiguousMatchException || ex is System.Reflection.TargetInvocationException)
                {
                    reportError(ex);
                    response = Reply(request, "TargetUnavailable");
                }
            }
            if (response.Code != "Ok") reportError(new InvalidDataException("Metadata request rejected: " + response.Code));
            cancellation.ThrowIfCancellationRequested();
            await WriteAsync(pipe, response, cancellation).ConfigureAwait(false);
            if (!hello) return;
        }
    }

    private async Task<BridgeTarget[]> CaptureAsync(CancellationToken cancellation)
    {
        Task<BridgeTarget[]> pending = capture(cancellation);
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellation.Register(() => cancelled.TrySetResult(true)))
        {
            if (await Task.WhenAny(pending, cancelled.Task).ConfigureAwait(false) != pending)
            {
                _ = pending.ContinueWith(t => reportError(t.Exception!.GetBaseException()), CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                cancellation.ThrowIfCancellationRequested();
            }
            BridgeTarget[] result = await pending.ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return result ?? throw new InvalidDataException("Missing target metadata.");
        }
    }

    private BridgeResponse Reply(BridgeRequest? request, string code) => new BridgeResponse
    {
        InstanceId = InstanceId, RequestId = request?.RequestId ?? Guid.Empty, Sequence = request?.Sequence ?? 0, Code = code
    };

    private static async Task WriteAsync(Stream pipe, BridgeResponse response, CancellationToken cancellation)
    {
        byte[] bytes = BridgeProtocol.Serialize(response);
        await pipe.WriteAsync(bytes, 0, bytes.Length, cancellation).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            activePipe?.Dispose();
        }
    }
}

internal static class ChatMetadataClient
{
    internal static async Task<BridgeResponse> DiscoverAsync(string name, int serverPid, Guid? expectedInstance,
        CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var close = deadline.Token.Register(pipe.Dispose);
        await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
        if (PipePeerIdentity.Server(pipe) != serverPid) throw new UnauthorizedAccessException("Unexpected metadata server process.");
        var reader = new BoundedLineReader(pipe, BridgeProtocol.MaxBytes, true);
        var hello = new BridgeRequest { Operation = "hello", Sequence = 1, ClientPid = Process.GetCurrentProcess().Id };
        BridgeResponse connected = await ExchangeAsync(pipe, reader, hello, deadline.Token).ConfigureAwait(false);
        if (connected.Code != "Ok" || (expectedInstance.HasValue && connected.InstanceId != expectedInstance.Value))
            throw new InvalidDataException("Metadata instance handshake was refused.");
        var list = new BridgeRequest { Operation = "list", Sequence = 2, ClientPid = hello.ClientPid, InstanceId = connected.InstanceId };
        BridgeResponse result = await ExchangeAsync(pipe, reader, list, deadline.Token).ConfigureAwait(false);
        if (result.InstanceId != connected.InstanceId) throw new InvalidDataException("Metadata instance changed.");
        return result;
    }

    internal static async Task<BridgeResponse> ExchangeAsync(Stream pipe, BoundedLineReader reader, BridgeRequest request,
        CancellationToken cancellation)
    {
        byte[] bytes = BridgeProtocol.Serialize(request);
        await pipe.WriteAsync(bytes, 0, bytes.Length, cancellation).ConfigureAwait(false);
        BridgeResponse reply = BridgeProtocol.ParseResponse(await reader.ReadLineAsync(cancellation).ConfigureAwait(false) ??
            throw new EndOfStreamException("Metadata server disconnected."));
        if (reply.RequestId != request.RequestId || reply.Sequence != request.Sequence)
            throw new InvalidDataException("Uncorrelated metadata acknowledgement.");
        return reply;
    }
}