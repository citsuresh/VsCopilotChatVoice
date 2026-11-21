using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VsCopilotChatVoice.Bridge;
using VsCopilotChatVoice.Ipc;
using VsCopilotChatVoice.Speech;

internal static class ContractRegressionTests
{
    private static int assertions;
    private static readonly Guid SessionId = Guid.Parse("639db5ed-7451-4dc8-86aa-5c44abbd9890");
    private static string Name() => "VsCopilotChatVoice.Metadata.Test." + Guid.NewGuid().ToString("N");
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static BridgeTarget Target() => new BridgeTarget
    {
        SessionId = SessionId, ClientId = "test-client", Epoch = Guid.NewGuid(), Status = "Observed", Trusted = null, Responding = null
    };

    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length > 2) throw new ArgumentException("Use --contracts-probe [built-VSIX-DLL].");
            Protocol(); Connections(); CaptureFailure(); CapacityBoundary(); Acknowledgements(); Cancellation(); StatusInspector(); Processes();
            if (args.Length == 2) ActualDll(args[1]);
            Console.WriteLine("PASS: contracts/metadata checks, " + assertions + " assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
    }

    private static string Json<T>(T value) => Encoding.UTF8.GetString(BridgeProtocol.Serialize(value)).TrimEnd('\n');
    private static void Invalid(Action action, string message)
    {
        bool invalid = false;
        try { action(); }
        catch (InvalidDataException) { invalid = true; }
        Check(invalid, message);
    }

    private static void Protocol()
    {
        var request = new BridgeRequest { Operation = "hello", Sequence = 1, ClientPid = Process.GetCurrentProcess().Id };
        Check(BridgeProtocol.ParseRequest(Json(request)).RequestId == request.RequestId, "Request ID changed.");
        foreach (string json in new[] { "", "{}", "[]", "null", Json(request) + "{}", Json(request).Replace("\"operation\":", "\"text\":\"PRIVATE\",\"operation\":"),
            Json(request).Replace("\"operation\":", "\"operation\":\"hello\",\"operation\":") })
            Invalid(() => BridgeProtocol.ParseRequest(json), "Malformed/unknown/duplicate fields accepted.");
        request.RequestId = Guid.Empty;
        Invalid(() => BridgeProtocol.ParseRequest(Json(request)), "Empty correlation accepted.");
        var target = Target();
        target.Validate();
        Check(target.Trusted == null && target.Responding == null, "Unknown readiness was changed.");
        foreach (string bad in new[] { "", " ", "line\nfeed", new string('x', 257) })
        {
            target.ClientId = bad;
            Invalid(target.Validate, "Invalid native client identity accepted.");
        }
        var response = new BridgeResponse { InstanceId = Guid.NewGuid(), RequestId = Guid.NewGuid(), Sequence = 1, Code = "Ok", Targets = new[] { Target() } };
        Check(BridgeProtocol.ParseResponse(Json(response)).Targets.Length == 1, "Target roundtrip failed.");
        response.AtomicEdit = true;
        Invalid(() => BridgeProtocol.ParseResponse(Json(response)), "Unproved atomic editing capability accepted.");
        response.AtomicEdit = false; response.ReplyFeed = true;
        Invalid(() => BridgeProtocol.ParseResponse(Json(response)), "Unproved reply feed accepted.");
        using (var bytes = new MemoryStream(new byte[] { 0xff, 10 }))
        {
            bool invalidUtf8 = false;
            try { new BoundedLineReader(bytes, 8, true).ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (DecoderFallbackException) { invalidUtf8 = true; }
            Check(invalidUtf8, "Invalid UTF8 was repaired silently.");
        }
        using (var bytes = new MemoryStream(Encoding.UTF8.GetBytes("123456789\n")))
            Invalid(() => new BoundedLineReader(bytes, 8, true).ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult(), "Framing exceeded its bound.");
    }

    private sealed class Connection : IDisposable
    {
        internal readonly NamedPipeClientStream Pipe;
        internal readonly BoundedLineReader Reader;
        internal readonly Guid HelloId;
        internal readonly Guid Instance;
        private int sequence = 1;
        internal Connection(string name, bool hello = true)
        {
            Pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            Pipe.Connect(3000);
            Reader = new BoundedLineReader(Pipe, BridgeProtocol.MaxBytes, true);
            if (hello)
            {
                BridgeRequest request = Request("hello");
                HelloId = request.RequestId;
                BridgeResponse reply = Send(request);
                Check(reply.Code == "Ok", "Handshake failed.");
                Instance = reply.InstanceId;
            }
        }
        internal BridgeRequest Request(string operation) => new BridgeRequest
        {
            Operation = operation, Sequence = sequence++, ClientPid = Process.GetCurrentProcess().Id,
            InstanceId = operation == "hello" ? Guid.Empty : Instance
        };
        internal BridgeResponse Send(BridgeRequest request) => ChatMetadataClient.ExchangeAsync(Pipe, Reader, request, CancellationToken.None).GetAwaiter().GetResult();
        public void Dispose() { Pipe.Dispose(); }
    }

    private static void Connections()
    {
        string name = Name();
        BridgeTarget[] current = { Target(), Target() };
        var errors = new List<Exception>();
        using (var server = new ChatMetadataServer(name, _ => Task.FromResult(current), ex => { lock (errors) errors.Add(ex); }))
        {
            using (var c = new Connection(name))
            {
                Check(c.Instance == server.InstanceId && PipePeerIdentity.Server(c.Pipe) == Process.GetCurrentProcess().Id, "Wrong server lifetime/process.");
                BridgeResponse list = c.Send(c.Request("list"));
                Check(list.Code == "Ok" && list.Targets.Length == 2, "Multiple chat bindings were collapsed.");
                BridgeRequest status = c.Request("status");
                status.SessionId = current[0].SessionId; status.ClientId = current[0].ClientId; status.Epoch = current[0].Epoch;
                BridgeResponse exact = c.Send(status);
                Check(exact.Code == "Ok" && exact.Targets.Length == 1 && exact.Targets[0].Trusted == null, "Exact origin/unknown state failed.");
                Guid oldEpoch = current[0].Epoch;
                current[0] = Target();
                status = c.Request("status"); status.SessionId = SessionId; status.ClientId = "test-client"; status.Epoch = oldEpoch;
                Check(c.Send(status).Code == "UnknownTarget", "Replaced epoch was still reachable.");
                BridgeRequest wrong = c.Request("list"); wrong.InstanceId = Guid.NewGuid();
                Check(c.Send(wrong).Code == "WrongInstance", "Cross-instance request accepted.");
                Check(c.Send(c.Request("speak")).Code == "UnknownOperation", "Speech command reached metadata endpoint.");
                current = Enumerable.Range(0, BridgeProtocol.MaxTargets + 1).Select(_ => Target()).ToArray();
                Check(c.Send(c.Request("list")).Code == "CapacityExceeded", "Capacity silently truncated.");
                current = new[] { Target() };
                Check(c.Send(c.Request("list")).Code == "Ok", "Valid request did not recover.");
                BridgeRequest duplicate = c.Request("list"); duplicate.RequestId = c.HelloId;
                Check(c.Send(duplicate).Code == "DuplicateRequest", "Duplicate request accepted.");
            }
            using (var c = new Connection(name))
            {
                BridgeRequest wrong = c.Request("list"); wrong.Sequence++;
                Check(c.Send(wrong).Code == "OutOfOrder", "Out-of-order request accepted.");
            }
            using (var c = new Connection(name, false))
            {
                BridgeRequest wrong = c.Request("hello"); wrong.ClientPid++;
                Check(c.Send(wrong).Code == "PeerMismatch", "Declared PID bypassed kernel identity.");
            }
            using (var c = new Connection(name, false))
            {
                BridgeRequest wrong = c.Request("hello"); wrong.Version++;
                Check(c.Send(wrong).Code == "UnsupportedVersion", "Unsupported version accepted.");
            }
            using (var c = new Connection(name, false))
                Check(c.Send(c.Request("list")).Code == "HelloRequired", "Handshake bypassed.");
            using (var c = new Connection(name, false))
            {
                byte[] bytes = Encoding.UTF8.GetBytes("{\"text\":\"PRIVATE\"}\n");
                c.Pipe.Write(bytes, 0, bytes.Length);
                Check(BridgeProtocol.ParseResponse(c.Reader.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult()).Code == "Malformed", "Malformed rejection missing.");
            }
            Check(ChatMetadataClient.DiscoverAsync(name, Process.GetCurrentProcess().Id, server.InstanceId, CancellationToken.None).GetAwaiter().GetResult().Code == "Ok", "Next client failed after rejection.");
            bool refused = false;
            try { ChatMetadataClient.DiscoverAsync(name, Process.GetCurrentProcess().Id, Guid.NewGuid(), CancellationToken.None).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { refused = true; }
            Check(refused, "Stale lifetime handshake accepted.");
            server.Dispose();
            Check(server.Completion.Wait(5000), "Server shutdown did not finish.");
        }
        Check(errors.Count >= 8, "Rejections were silent.");
    }

    private static void CaptureFailure()
    {
        string name = Name();
        int calls = 0;
        var errors = new List<Exception>();
        using (var server = new ChatMetadataServer(name, _ => Interlocked.Increment(ref calls) == 1 ?
            Task.FromException<BridgeTarget[]>(new AmbiguousMatchException("isolated binding fixture")) :
            Task.FromResult(new[] { Target() }), ex => { lock (errors) errors.Add(ex); }))
        {
            BridgeResponse failed = ChatMetadataClient.DiscoverAsync(name, Process.GetCurrentProcess().Id,
                server.InstanceId, CancellationToken.None).GetAwaiter().GetResult();
            Check(failed.Code == "TargetUnavailable" && failed.Targets.Length == 0, "Binding ambiguity was not explicitly refused.");
            Check(!server.Completion.IsCompleted, "Binding ambiguity terminated the listener.");
            BridgeResponse recovered = ChatMetadataClient.DiscoverAsync(name, Process.GetCurrentProcess().Id,
                server.InstanceId, CancellationToken.None).GetAwaiter().GetResult();
            Check(recovered.Code == "Ok" && recovered.Targets.Length == 1, "Next client did not recover after binding ambiguity.");
            lock (errors) Check(errors.Any(ex => ex is AmbiguousMatchException), "Binding ambiguity was not reported.");
            server.Dispose();
            Check(server.Completion.Wait(5000), "Recovered listener did not stop normally.");
        }
    }

    private static void CapacityBoundary()
    {
        string name = Name();
        int captures = 0;
        using (var server = new ChatMetadataServer(name, _ =>
        {
            Interlocked.Increment(ref captures);
            return Task.FromResult(new[] { Target() });
        }, _ => { }))
        {
            using (var c = new Connection(name))
            {
                for (int sequence = 2; sequence <= BridgeProtocol.MaxRequests; sequence++)
                    Check(c.Send(c.Request("list")).Code == "Ok", "A request within the connection limit was refused.");
                Check(c.Send(c.Request("list")).Code == "CapacityExceeded", "Request 65 exceeded the connection limit without refusal.");
                Check(captures == BridgeProtocol.MaxRequests - 1, "Rejected capacity request reached the provider.");
                bool closed;
                try { closed = c.Reader.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult() == null; }
                catch (IOException) { closed = true; }
                Check(closed, "Capacity-exhausted connection stayed open.");
            }
            Check(ChatMetadataClient.DiscoverAsync(name, Process.GetCurrentProcess().Id, server.InstanceId,
                CancellationToken.None).GetAwaiter().GetResult().Code == "Ok", "Next client did not recover after capacity exhaustion.");
        }
    }

    private sealed class ScriptedPeer : Stream
    {
        private readonly MemoryStream reply;
        internal readonly MemoryStream Written = new MemoryStream();
        internal ScriptedPeer(BridgeResponse response) { reply = new MemoryStream(BridgeProtocol.Serialize(response)); }
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] bytes, int offset, int count) => reply.Read(bytes, offset, count);
        public override void Write(byte[] bytes, int offset, int count) => Written.Write(bytes, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { reply.Dispose(); Written.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private static void Acknowledgements()
    {
        foreach (bool wrongId in new[] { true, false })
        {
            var request = new BridgeRequest { Operation = "hello", Sequence = 1, ClientPid = Process.GetCurrentProcess().Id };
            var response = new BridgeResponse
            {
                InstanceId = Guid.NewGuid(), Code = "Ok", RequestId = wrongId ? Guid.NewGuid() : request.RequestId,
                Sequence = wrongId ? request.Sequence : request.Sequence + 1
            };
            using (var peer = new ScriptedPeer(response))
            {
                Invalid(() => ChatMetadataClient.ExchangeAsync(peer, new BoundedLineReader(peer, BridgeProtocol.MaxBytes, true),
                    request, CancellationToken.None).GetAwaiter().GetResult(), "Uncorrelated acknowledgement accepted.");
                Check(peer.Written.Length > 0, "Correlation fixture did not exchange a request.");
            }
        }
    }

    private static void Cancellation()
    {
        string name = Name();
        var late = new TaskCompletionSource<BridgeTarget[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using (var server = new ChatMetadataServer(name, _ => Interlocked.Increment(ref calls) == 1 ? late.Task : Task.FromResult(new[] { Target() }), _ => { }, TimeSpan.FromMilliseconds(300)))
        {
            using (var c = new Connection(name))
            {
                bool disconnected = false;
                try { c.Send(c.Request("list")); }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException) { disconnected = true; }
                Check(disconnected, "Ignored provider cancellation outlived deadline.");
            }
            late.SetResult(new[] { Target() });
            Check(ChatMetadataClient.DiscoverAsync(name, Process.GetCurrentProcess().Id, server.InstanceId, CancellationToken.None).GetAwaiter().GetResult().Code == "Ok", "Late callback poisoned next connection.");
            using (var c = new Connection(name))
            {
                server.Dispose();
                Check(server.Completion.Wait(5000), "Idle client blocked disposal.");
            }
        }
    }

    private static void Processes()
    {
        Process a = Child(Name()), b = Child(Name());
        try
        {
            string first = Ready(a), second = Ready(b);
            string[] x = first.Split('|'), y = second.Split('|');
            var one = ChatMetadataClient.DiscoverAsync(x[0], a.Id, Guid.Parse(x[1]), CancellationToken.None).GetAwaiter().GetResult();
            var two = ChatMetadataClient.DiscoverAsync(y[0], b.Id, Guid.Parse(y[1]), CancellationToken.None).GetAwaiter().GetResult();
            Check(one.InstanceId != two.InstanceId && one.Targets[0].SessionId == two.Targets[0].SessionId, "Equal conversation IDs collapsed process lifetimes.");
            bool wrongPeer = false;
            try { ChatMetadataClient.DiscoverAsync(x[0], b.Id, null, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (UnauthorizedAccessException) { wrongPeer = true; }
            Check(wrongPeer, "Wrong live server PID accepted.");
            bool crossed = false;
            try { ChatMetadataClient.DiscoverAsync(y[0], b.Id, one.InstanceId, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { crossed = true; }
            Check(crossed, "Cross-process lifetime token accepted.");
        }
        finally { Finish(a); Finish(b); }
    }

    private static void StatusInspector()
    {
        string name = Name();
        BridgeTarget target = Target();
        int pid = Process.GetCurrentProcess().Id;
        using (var server = new ChatMetadataServer(name, _ => Task.FromResult(new[] { target }), _ => { }))
        {
            BridgeResponse result = QueryStatus(name, pid, server.InstanceId, target.SessionId, target.ClientId, target.Epoch);
            Check(result.Code == "Ok" && result.Targets.Length == 1 && result.Targets[0].Epoch == target.Epoch,
                "Status inspector did not return the exact target.");
            Check(QueryStatus(name, pid, server.InstanceId, target.SessionId, target.ClientId, Guid.NewGuid()).Code == "UnknownTarget",
                "Status inspector accepted a stale epoch.");
            Check(QueryStatus(name, pid, server.InstanceId, Guid.NewGuid(), target.ClientId, target.Epoch).Code == "UnknownTarget",
                "Status inspector accepted another session.");
            Check(QueryStatus(name, pid, server.InstanceId, target.SessionId, target.ClientId, target.Epoch).Code == "Ok",
                "Status inspector did not recover after refusals.");
            Invalid(() => QueryStatus(name, pid, Guid.NewGuid(), target.SessionId, target.ClientId, target.Epoch),
                "Status inspector accepted another instance lifetime.");
        }
    }

    private static BridgeResponse QueryStatus(string name, int pid, Guid instance, Guid session, string client, Guid epoch)
    {
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        using (deadline.Token.Register(pipe.Dispose))
        {
            pipe.ConnectAsync(deadline.Token).GetAwaiter().GetResult();
            if (PipePeerIdentity.Server(pipe) != pid) throw new UnauthorizedAccessException("Unexpected metadata server process.");
            var reader = new BoundedLineReader(pipe, BridgeProtocol.MaxBytes, true);
            var hello = new BridgeRequest { Operation = "hello", Sequence = 1, ClientPid = Process.GetCurrentProcess().Id };
            BridgeResponse connected = ChatMetadataClient.ExchangeAsync(pipe, reader, hello, deadline.Token).GetAwaiter().GetResult();
            if (connected.Code != "Ok" || connected.InstanceId != instance)
                throw new InvalidDataException("Metadata instance handshake was refused.");
            var request = new BridgeRequest
            {
                Operation = "status", Sequence = 2, ClientPid = hello.ClientPid, InstanceId = instance,
                SessionId = session, ClientId = client, Epoch = epoch
            };
            BridgeResponse result = ChatMetadataClient.ExchangeAsync(pipe, reader, request, deadline.Token).GetAwaiter().GetResult();
            if (result.InstanceId != instance) throw new InvalidDataException("Metadata instance changed.");
            return result;
        }
    }

    private static Process Child(string name) => Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
        "--metadata-owner-child " + name) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true });
    private static string Ready(Process process)
    {
        Task<string> read = process.StandardOutput.ReadLineAsync();
        if (!read.Wait(5000)) throw new TimeoutException("Metadata child did not start.");
        return read.Result ?? throw new InvalidOperationException("Metadata child failed: " + process.StandardError.ReadToEnd());
    }
    private static void Finish(Process child)
    {
        if (!child.HasExited)
        {
            child.StandardInput.WriteLine("stop"); child.StandardInput.Flush();
            if (!child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(5000); }
        }
        Check(child.ExitCode == 0, "Owned metadata child failed.");
        child.Dispose();
    }
    internal static int ChildMain(string name)
    {
        try
        {
            using (var server = new ChatMetadataServer(name, _ => Task.FromResult(new[] { Target() }), ex => Console.Error.WriteLine(ex.GetType().Name)))
            {
                Console.WriteLine(name + "|" + server.InstanceId); Console.Out.Flush(); Console.ReadLine();
                server.Dispose(); if (!server.Completion.Wait(5000)) throw new TimeoutException("Metadata child did not stop.");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    internal static int Inspect(string[] args)
    {
        try
        {
            bool status = args[0] == "--metadata-status";
            if (status ? args.Length != 6 : args.Length < 2 || args.Length > 3)
                throw new ArgumentException("Use --metadata-inspect <Exp-PID> [instance-guid] or --metadata-status <Exp-PID> <instance-guid> <session-guid> <client-id> <epoch-guid>.");
            int pid = int.Parse(args[1]);
            Guid? expected = args.Length >= 3 ? Guid.Parse(args[2]) : (Guid?)null;
            BridgeResponse result = status
                ? QueryStatus(BridgeProtocol.PipeName(pid), pid, expected.Value, Guid.Parse(args[3]), args[4], Guid.Parse(args[5]))
                : ChatMetadataClient.DiscoverAsync(BridgeProtocol.PipeName(pid), pid, expected, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine(Json(result));
            return result.Code == "Ok" ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine("Metadata inspection failed: " + ex.GetType().Name); return 1; }
    }

    private static void ActualDll(string path)
    {
        Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(path));
        const BindingFlags methods = BindingFlags.NonPublic | BindingFlags.Static;
        Type bridge = assembly.GetType("VsCopilotChatVoice.VisualStudio.ChatMetadataBridge", true);
        MethodInfo enabled = bridge.GetMethod("IsEnabled", methods);
        Check(!(bool)enabled.Invoke(null, new object[] { new[] { "devenv" }, "1" }), "Main metadata gate accepted.");
        Check(!(bool)enabled.Invoke(null, new object[] { new[] { "devenv", "/RootSuffix", "ExpOther" }, "1" }), "Wrong suffix accepted.");
        Check(!(bool)enabled.Invoke(null, new object[] { new[] { "devenv", "/RootSuffix", "Exp" }, "0" }), "Opt-out metadata gate accepted.");
        Check((bool)enabled.Invoke(null, new object[] { new[] { "devenv", "/rootsuffix", "exp" }, "1" }), "Exp metadata gate refused.");
        Type protocol = assembly.GetType("VsCopilotChatVoice.Bridge.BridgeProtocol", true);
        var request = new BridgeRequest { Operation = "hello", Sequence = 1, ClientPid = Process.GetCurrentProcess().Id };
        object parsed = protocol.GetMethod("ParseRequest", methods).Invoke(null, new object[] { Json(request) });
        Check((Guid)parsed.GetType().GetProperty("RequestId").GetValue(parsed) == request.RequestId, "Actual DLL contract differs.");
    }
}