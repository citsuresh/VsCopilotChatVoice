#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VsCopilotChatVoice.Speech;

internal static class SpeechOptionsRegressionTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static int assertions;
    private static void Check(bool value, string label) { assertions++; if (!value) throw new InvalidOperationException(label); }
    private static Exception Unwrap(Exception ex) => ex is TargetInvocationException invocation && invocation.InnerException != null ? Unwrap(invocation.InnerException) : ex;
    private static void Reject(Action action, string label, Type? expected = null)
    {
        try { action(); }
        catch (Exception ex) { Check(expected == null || expected.IsInstanceOfType(Unwrap(ex)), label + " exception boundary"); return; }
        throw new InvalidOperationException("Accepted " + label);
    }
    private static bool Wait(Func<bool> condition, int timeout = 3000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < timeout) Thread.Sleep(10);
        return condition();
    }

    internal static int Run(string[] args)
    {
        if (args.Length > 2) { Console.Error.WriteLine("Use --speech-options-probe [VSIX-DLL]."); return 2; }
        try
        {
            string worker = Path.Combine(AppContext.BaseDirectory, "VsCopilotChatVoice.Reader.exe");
            var linked = new Boundary(typeof(SpeechOptionsSnapshot).Assembly);
            Contract(linked);
            Fake(linked);
            FailureBoundaries(linked);
            RealHost(linked, worker);
            if (args.Length == 2)
            {
                var actual = new Boundary(Assembly.LoadFrom(Path.GetFullPath(args[1])));
                Contract(actual);
                Fake(actual);
                FailureBoundaries(actual);
                RealHost(actual, worker);
                Check(!actual.Assembly.GetReferencedAssemblies().Any(a => a.Name == "System.Speech" || a.Name == "VsCopilotChatVoice.Reader"), "VSIX has no speech or Reader assembly dependency");
            }
            RawWorker(worker);
            InvalidUtf8Worker(worker);
            Check(!typeof(SpeechOptionsRegressionTests).Assembly.GetTypes().Any(t => t.FullName == "ReadAlongTrial.SpeechWorker"), "Worker is not Compile-linked into test process");
            Console.WriteLine("PASS: speech options, " + assertions + " assertions; linked/actual boundary and real external null-output getters verified (voice identifiers withheld).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: speech options: " + Unwrap(ex)); return 1; }
    }

    private sealed class Boundary
    {
        internal readonly Assembly Assembly;
        internal readonly Type Snapshot, Host, Protocol, Message;
        internal Boundary(Assembly assembly)
        {
            Assembly = assembly;
            Snapshot = assembly.GetType("VsCopilotChatVoice.Speech.SpeechOptionsSnapshot", true)!;
            Host = assembly.GetType("VsCopilotChatVoice.Speech.SpeechWorkerHost", true)!;
            Protocol = assembly.GetType("VsCopilotChatVoice.Speech.WorkerProtocol", true)!;
            Message = assembly.GetType("VsCopilotChatVoice.Speech.WorkerMessage", true)!;
        }
        internal object Options(string voice = "Fixture", int rate = 0, int volume = 100) => Activator.CreateInstance(Snapshot, voice, rate, volume)!;
        internal string Voice(object snapshot) => (string)Snapshot.GetProperty("Voice")!.GetValue(snapshot)!;
        internal int Rate(object snapshot) => (int)Snapshot.GetProperty("Rate")!.GetValue(snapshot)!;
        internal int Volume(object snapshot) => (int)Snapshot.GetProperty("Volume")!.GetValue(snapshot)!;
        internal object? Parse(string json) => Protocol.GetMethod("TryParse", All)!.Invoke(null, new object[] { json });
        internal byte[] Serialize(string type, long gen, string? text, object? options, long command = 0)
        {
            object message = Activator.CreateInstance(Message, true)!;
            Message.GetField("Type")!.SetValue(message, type);
            Message.GetField("Generation")!.SetValue(message, gen);
            Message.GetField("Text")!.SetValue(message, text);
            Message.GetField("Options")!.SetValue(message, options);
            Message.GetField("Command")!.SetValue(message, command);
            return (byte[])Protocol.GetMethod("Serialize", All)!.Invoke(null, new[] { message })!;
        }
        internal Task<long> Start(object host, string text, object options, CancellationToken token = default) =>
            (Task<long>)Host.GetMethod("SpeakWithOptionsAsync")!.Invoke(host, new[] { (object)text, options, token })!;
        internal Task<long> Speak(object host, string text) => (Task<long>)Host.GetMethod("SpeakAsync")!.Invoke(host, new object[] { text, CancellationToken.None })!;
        internal Task<bool> Cancel(object host) => (Task<bool>)Host.GetMethod("CancelAsync")!.Invoke(host, new object[] { CancellationToken.None })!;
        internal Task<bool> State(object host, long gen, bool pause) => (Task<bool>)Host.GetMethod(pause ? "PauseAsync" : "ResumeAsync")!.Invoke(host, new object[] { gen, CancellationToken.None })!;
        internal long Generation(object host) => (long)Host.GetProperty("CurrentGeneration")!.GetValue(host)!;
        internal bool Active(object host) => (bool)Host.GetProperty("IsActive")!.GetValue(host)!;
        internal bool Paused(object host) => (bool)Host.GetProperty("IsPaused")!.GetValue(host)!;
        internal SemaphoreSlim Gate(object host) => (SemaphoreSlim)Host.GetField("sendGate", All)!.GetValue(host)!;
        internal object Launch(string worker, CancellationToken token = default)
        {
            var method = Host.GetMethod("LaunchAsync", new[] { typeof(string), typeof(bool), typeof(TimeSpan), typeof(CancellationToken) })!;
            Task task = (Task)method.Invoke(null, new object[] { worker, true, TimeSpan.FromSeconds(15), token })!;
            task.GetAwaiter().GetResult();
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }
        internal string Assess(object snapshot, params Tuple<string, bool>[] catalog)
        {
            Type voice = Assembly.GetType("VsCopilotChatVoice.Speech.SpeechVoiceCapability", true)!;
            Array array = Array.CreateInstance(voice, catalog.Length);
            for (int i = 0; i < catalog.Length; i++)
            {
                object item = Activator.CreateInstance(voice, true)!;
                voice.GetField("Name")!.SetValue(item, catalog[i].Item1);
                voice.GetField("Culture")!.SetValue(item, "en-US");
                voice.GetField("Enabled")!.SetValue(item, catalog[i].Item2);
                array.SetValue(item, i);
            }
            return Snapshot.GetMethod("AssessVoice", All)!.Invoke(snapshot, new object[] { array })!.ToString();
        }
    }

    private sealed class Capture
    {
        internal readonly ConcurrentQueue<string> Events = new ConcurrentQueue<string>();
        internal readonly ConcurrentQueue<Tuple<long, object>> Applied = new ConcurrentQueue<Tuple<long, object>>();
        internal Capture(Boundary b, object host)
        {
            b.Host.GetEvent("Progress")!.AddEventHandler(host, new Action<long, int>((g, o) => Events.Enqueue("P:" + g + ":" + o)));
            b.Host.GetEvent("Completed")!.AddEventHandler(host, new Action<long>(g => Events.Enqueue("C:" + g)));
            b.Host.GetEvent("Faulted")!.AddEventHandler(host, new Action<string>(_ => Events.Enqueue("F")));
            b.Host.GetEvent("StateChanged")!.AddEventHandler(host, new Action<long, bool>((g, p) => Events.Enqueue("S:" + g + ":" + p)));
            var gen = Expression.Parameter(typeof(long)); var options = Expression.Parameter(b.Snapshot);
            Action<long, object> callback = (g, o) => { Applied.Enqueue(Tuple.Create(g, o)); Events.Enqueue("A:" + g); };
            Delegate handler = Expression.Lambda(b.Host.GetEvent("OptionsApplied")!.EventHandlerType!,
                Expression.Invoke(Expression.Constant(callback), gen, Expression.Convert(options, typeof(object))), gen, options).Compile();
            b.Host.GetEvent("OptionsApplied")!.AddEventHandler(host, handler);
        }
        internal bool Has(string prefix) => Events.Any(e => e.StartsWith(prefix, StringComparison.Ordinal));
    }

    private sealed class FakePeer : IDisposable
    {
        internal readonly object Host;
        internal readonly NamedPipeClientStream Client;
        internal readonly BoundedLineReader Reader;
        internal readonly Capture Capture;
        internal FakePeer(Boundary b)
        {
            string name = WorkerProtocol.NewPipeName();
            Host = Activator.CreateInstance(b.Host, All, null, new object?[] { name, null }, null)!;
            Client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            Task connect = (Task)b.Host.GetMethod("ConnectAsync", new[] { typeof(TimeSpan) })!.Invoke(Host, new object[] { TimeSpan.FromSeconds(5) })!;
            Client.Connect(5000); connect.GetAwaiter().GetResult();
            Reader = new BoundedLineReader(Client);
            Capture = new Capture(b, Host);
        }
        public void Dispose() { ((IDisposable)Host).Dispose(); Client.Dispose(); }
    }
    private static WorkerMessage Receive(BoundedLineReader reader, int milliseconds = 5000)
    {
        using var deadline = new CancellationTokenSource(milliseconds);
        string? line = reader.ReadLineAsync(deadline.Token).GetAwaiter().GetResult();
        return line == null ? throw new InvalidOperationException("Unexpected worker EOF.") :
            WorkerProtocol.TryParse(line) ?? throw new InvalidOperationException("Invalid worker frame.");
    }
    private static void Send(Stream pipe, WorkerMessage message)
    {
        byte[] bytes = WorkerProtocol.Serialize(message); pipe.Write(bytes, 0, bytes.Length); pipe.Flush();
    }
    private static void Ack(Stream pipe, long gen, string voice = "Fixture", int rate = 0, int volume = 100, long command = 0) =>
        Send(pipe, new WorkerMessage { Type = WorkerProtocol.OptionsApplied, Generation = gen, Command = command, Options = new SpeechOptionsSnapshot(voice, rate, volume) });

    private static void Contract(Boundary b)
    {
        Check(b.Snapshot.IsSealed && b.Snapshot.GetProperties().All(p => p.SetMethod == null), "Snapshot is sealed and getter-only");
        Check(b.Snapshot.GetProperty("Pitch") == null && b.Snapshot.GetProperties().Length == 3, "Snapshot has only voice/rate/volume");
        Check(b.Host.GetMethods().Count(m => m.Name == "SpeakAsync") == 1 && b.Host.GetMethod("SpeakAsync")!.ReturnType == typeof(Task<long>), "Legacy reflection stays unambiguous");
        Check(b.Host.GetMethod("Speak")!.ReturnType == typeof(long) && b.Host.GetMethod("Cancel")!.ReturnType == typeof(bool), "Legacy sync signatures preserved");
        foreach (int rate in new[] { -10, 0, 10 }) foreach (int volume in new[] { 0, 37, 100 })
        {
            object options = b.Options("", rate, volume);
            Check(b.Voice(options) == "" && b.Rate(options) == rate && b.Volume(options) == volume, "Explicit zero/default/extreme getters");
            byte[] bytes = b.Serialize(WorkerProtocol.SpeakWithOptions, 7, "Hello", options);
            object parsed = b.Parse(Encoding.UTF8.GetString(bytes))!;
            object roundtrip = b.Message.GetField("Options")!.GetValue(parsed)!;
            Check(b.Voice(roundtrip) == "" && b.Rate(roundtrip) == rate && b.Volume(roundtrip) == volume, "Profile roundtrip exact");
        }
        foreach (int rate in new[] { -11, 11, int.MinValue, int.MaxValue }) Reject(() => b.Options(rate: rate), "Rate bound", typeof(ArgumentOutOfRangeException));
        foreach (int volume in new[] { -1, 101, int.MinValue, int.MaxValue }) Reject(() => b.Options(volume: volume), "Volume bound", typeof(ArgumentOutOfRangeException));
        foreach (string invalid in new[] { " ", "\n", "bad\t", "bad\ud800", new string('x', 257) }) Reject(() => b.Options(invalid), "Name structural bound", typeof(ArgumentException));
        Reject(() => b.Options(null!), "Null is not default", typeof(ArgumentException));
        Check(b.Voice(b.Options(new string('x', 256))).Length == 256, "Maximum name bound");
        string unicode = "Fixture é 日本 \" \\ 😀";
        object unicodeParsed = b.Parse(Encoding.UTF8.GetString(b.Serialize(WorkerProtocol.SpeakWithOptions, 1, "Hello", b.Options(unicode))))!;
        Check(b.Voice(b.Message.GetField("Options")!.GetValue(unicodeParsed)!) == unicode, "Valid Unicode/name escapes preserved");
        string json = Encoding.UTF8.GetString(b.Serialize(WorkerProtocol.SpeakWithOptions, 7, "Hello", b.Options()));
        string evidence = Encoding.UTF8.GetString(b.Serialize(WorkerProtocol.OptionsApplied, 7, null, b.Options()));
        string query = Encoding.UTF8.GetString(b.Serialize(WorkerProtocol.QueryOptions, 7, null, null, 3));
        Check(b.Parse(evidence) != null && b.Parse(query) != null, "Distinct applied/query schema roundtrips");
        foreach (string malformed in new[]
        {
            evidence.Replace("\"command\":0,", ""), evidence.Replace("\"command\":0", "\"command\":-1"),
            evidence.Replace("\"command\":0", "\"command\":0,\"command\":0"),
            evidence.Replace("\"options\":", "\"unknown\":"), evidence.Replace("\"options\":{", "\"text\":\"wrong\",\"options\":{"),
            query.Replace("\"command\":3", "\"command\":0"), query.Replace("\"command\":3", "\"command\":3.0"),
            query.Replace("\"command\":3", "\"command\":3,\"options\":null"), query.Replace("\"command\":3", "\"command\":3,\"unknown\":0")
        }) Check(b.Parse(malformed) == null, "Strict query/evidence fields rejected");
        foreach (string malformed in new[]
        {
            "", "{}", "null", "[]", "not JSON", json + "{}", json + "x", json + "\u00a0", json.TrimEnd().TrimEnd('}') + ",}",
            json.Replace("\"type\":\"speak-options\"", "\"type\":\"other\""),
            json.Replace("\"gen\":7", "\"gen\":0"), json.Replace("\"gen\":7", "\"gen\":-1"),
            json.Replace("\"gen\":7", "\"gen\":7,\"gen\":8"), json.Replace("\"gen\":7", "\"gen\":9223372036854775808"),
            json.Replace("\"gen\":7", "\"gen\":7.0"), json.Replace("\"gen\":7", "\"gen\":7e0"), json.Replace("\"gen\":7", "\"gen\":07"),
            json.Replace("\"gen\":7,", ""), json.Replace("\"text\":\"Hello\"", "\"text\":\" \""),
            json.Replace("\"text\":\"Hello\",", ""), json.Replace("\"text\":", "\"unknown\":"),
            json.Replace("\"kind\":\"System.Speech\"", "\"kind\":\"other\""), json.Replace("\"kind\":\"System.Speech\",", ""),
            json.Replace("\"version\":1", "\"version\":2"), json.Replace("\"version\":1", "\"version\":1,\"version\":1"),
            json.Replace("\"version\":1", "\"version\":1.0"), json.Replace("\"version\":1", "\"version\":1e0"),
            json.Replace("\"version\":1", "\"version\":01"), json.Replace("\"version\":1", "\"version\":true"),
            json.Replace("\"version\":1,", ""), json.Replace("\"voice\":\"Fixture\"", "\"voice\":null"),
            json.Replace("\"voice\":\"Fixture\"", "\"voice\":\"Fixture\",\"voice\":\"Fixture\""),
            json.Replace("\"voice\":\"Fixture\",", ""), json.Replace("\"voice\":", "\"pitch\":"),
            json.Replace("Fixture", "bad\\ud800"), json.Replace("Fixture", "bad\\u000a"), json.Replace("Fixture", "bad\\q"),
            json.Replace("\"rate\":0", "\"rate\":-11"), json.Replace("\"rate\":0", "\"rate\":11"),
            json.Replace("\"rate\":0", "\"rate\":\"0\""), json.Replace("\"rate\":0,", ""),
            json.Replace("\"volume\":100", "\"volume\":-1"), json.Replace("\"volume\":100", "\"volume\":101"),
            json.Replace("\"volume\":100", "\"volume\":100,\"pitch\":0"), json.Replace("\"volume\":100", "\"volume\":100,\"volume\":0"),
            json.Replace("\"options\":", "\"profile\":"), json.Replace("\"options\":{", "\"command\":1,\"options\":{")
        }) Check(b.Parse(malformed) == null, "Strict configured schema mutation rejected");
        Check(b.Parse(json.PadRight(WorkerProtocol.MaxLineBytes)) != null, "Exact byte framing bound");
        Check(b.Parse(json.PadRight(WorkerProtocol.MaxLineBytes + 1)) == null, "Over-size frame rejected");
        Reject(() => b.Serialize(WorkerProtocol.SpeakWithOptions, 1, new string('日', WorkerProtocol.MaxSpeakChars), b.Options()), "UTF8 frame bound");
        Check(b.Parse(Encoding.UTF8.GetString(b.Serialize(WorkerProtocol.SpeakWithOptions, 1, new string('x', WorkerProtocol.MaxSpeakChars), b.Options()))) != null, "Maximum ASCII speech text");
        Reject(() => b.Serialize(WorkerProtocol.SpeakWithOptions, 1, "bad\ud800", b.Options()), "Text Unicode bound");
        string legacy = Encoding.UTF8.GetString(b.Serialize(WorkerProtocol.Speak, 3, "Hello", null));
        Check(legacy == "{\"command\":0,\"gen\":3,\"message\":null,\"offset\":0,\"text\":\"Hello\",\"type\":\"speak\"}\n", "Exact legacy speech bytes unchanged");
        Check(b.Parse("{\"type\":\"speak\",\"gen\":1,\"text\":\"Hello\",\"unknown\":true}") != null, "No unrelated legacy strictness rewrite");
        Check(b.Assess(b.Options("")) == "NotSelected", "Empty voice denotes engine default");
        Check(b.Assess(b.Options()) == "Missing", "Missing catalogue refuses");
        Check(b.Assess(b.Options(), Tuple.Create("Fixture", false)) == "Disabled", "Disabled catalogue refuses");
        Check(b.Assess(b.Options(), Tuple.Create("Fixture", true), Tuple.Create("Fixture", false)) == "Ambiguous", "Ambiguous catalogue refuses even with one enabled");
        Check(b.Assess(b.Options(), Tuple.Create("Fixture", true)) == "Available", "Unique enabled catalogue accepted");
        Check(b.Assess(b.Options("fixture"), Tuple.Create("Fixture", true)) == "Missing", "Ordinal actual name required");
    }

    private static void Fake(Boundary b)
    {
        using var peer = new FakePeer(b);
        object host = peer.Host;
        Task<long> first = b.Start(host, "one two three", b.Options());
        WorkerMessage request = Receive(peer.Reader);
        Check(request.Type == WorkerProtocol.SpeakWithOptions && request.Options!.Voice == "Fixture" && !first.IsCompleted, "Queued write is not application success");
        long gen = request.Generation;
        Ack(peer.Client, gen + 1);
        Ack(peer.Client, gen, command: 1);
        Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Error, Generation = gen + 1, Message = "late" });
        Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = gen, Offset = 0 });
        Thread.Sleep(80);
        Check(!first.IsCompleted && peer.Capture.Events.IsEmpty, "Foreign options/error and premature progress ignored");
        Ack(peer.Client, gen);
        Check(first.GetAwaiter().GetResult() == gen && Wait(() => peer.Capture.Has("A:" + gen)), "Correlated applied acknowledgement completes start");
        Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = gen, Offset = 4 });
        Check(Wait(() => peer.Capture.Has("P:" + gen + ":4")), "Configured offsets remain prompt-relative");
        Task<bool> pause = b.State(host, gen, true); WorkerMessage state = Receive(peer.Reader);
        Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Paused, Generation = gen, Command = state.Command });
        Check(pause.GetAwaiter().GetResult() && b.Paused(host), "Configured true pause correlated");
        Task<long> replacement = b.Start(host, "new prompt", b.Options(rate: 10, volume: 0));
        WorkerMessage next = Receive(peer.Reader);
        Ack(peer.Client, gen); Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Error, Generation = gen, Message = "obsolete" });
        Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Completed, Generation = gen });
        Thread.Sleep(80);
        Check(!replacement.IsCompleted && b.Active(host) && !b.Paused(host) && !peer.Capture.Has("F"), "Late options/error/completion cannot advance replacement");
        Ack(peer.Client, next.Generation, rate: 10, volume: 0);
        Check(replacement.GetAwaiter().GetResult() == next.Generation, "Replacement acknowledges exact options");
        Task<bool> completingPause = b.State(host, next.Generation, true); Receive(peer.Reader);
        Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Completed, Generation = next.Generation });
        Check(!completingPause.GetAwaiter().GetResult() && !b.Active(host), "Natural completion is distinct from pause acknowledgement");
        Check(!b.State(host, next.Generation, false).GetAwaiter().GetResult(), "Completed exact generation has natural state outcome");
        Task<long> pending = b.Start(host, "stop before ack", b.Options()); WorkerMessage stopping = Receive(peer.Reader);
        Check(b.Cancel(host).GetAwaiter().GetResult() && Receive(peer.Reader).Type == WorkerProtocol.Cancel, "Cancel crosses unacknowledged options");
        Reject(() => pending.GetAwaiter().GetResult(), "Pending options cancelled", typeof(OperationCanceledException));
        long legacy = b.Speak(host, "legacy after cancel").GetAwaiter().GetResult();
        Check(Receive(peer.Reader).Type == WorkerProtocol.Speak, "Legacy start still uses original command");
        Ack(peer.Client, stopping.Generation); Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Error, Generation = stopping.Generation, Message = "late" });
        Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = legacy, Offset = 0 });
        Check(Wait(() => peer.Capture.Has("P:" + legacy + ":0")) && b.Active(host) && !peer.Capture.Has("F"), "Late pending-option callbacks do not fault newer legacy operation");
        var gate = b.Gate(host); Check(gate.Wait(1000), "Acquire queued snapshot fixture gate");
        Task<long> queued; object original = b.Options(rate: -10, volume: 37);
        try
        {
            queued = b.Start(host, "immutable queued", original);
            b.Snapshot.GetField("<Voice>k__BackingField", All)!.SetValue(original, "mutated caller");
            b.Snapshot.GetField("<Rate>k__BackingField", All)!.SetValue(original, 10);
            Check(!queued.IsCompleted && b.Generation(host) == legacy, "No generation allocated before gate");
        }
        finally { gate.Release(); }
        WorkerMessage immutable = Receive(peer.Reader);
        Check(immutable.Options!.Voice == "Fixture" && immutable.Options.Rate == -10 && immutable.Options.Volume == 37, "Queued start keeps defensive entry snapshot");
        Ack(peer.Client, immutable.Generation, rate: -10, volume: 37); queued.GetAwaiter().GetResult();
        Check(gate.Wait(1000), "Acquire cancel-epoch fixture gate");
        Task<long> fenced; Task<bool> cancel;
        try { fenced = b.Start(host, "fenced replacement", b.Options()); cancel = b.Cancel(host); }
        finally { gate.Release(); }
        Check(cancel.GetAwaiter().GetResult() && Receive(peer.Reader).Type == WorkerProtocol.Cancel, "Cancel keeps exact active generation");
        Reject(() => fenced.GetAwaiter().GetResult(), "Queued configured start epoch fenced", typeof(OperationCanceledException));
        Check(gate.Wait(1000), "Acquire idle epoch fixture gate");
        Task<long> idle;
        try { idle = b.Start(host, "idle queued", b.Options()); Check(!b.Cancel(host).GetAwaiter().GetResult(), "Idle cancel returns false"); }
        finally { gate.Release(); }
        Reject(() => idle.GetAwaiter().GetResult(), "Idle configured start epoch fenced", typeof(OperationCanceledException));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject(() => b.Start(host, "pre cancelled", b.Options(), cancelled.Token).GetAwaiter().GetResult(), "Pre-cancelled configured start", typeof(OperationCanceledException));
        long before = b.Generation(host);
        Reject(() => b.Start(host, new string('日', WorkerProtocol.MaxSpeakChars), b.Options()), "Structural size validation before enqueue");
        Reject(() => b.Start(host, "bad", null!), "Null structural options before enqueue", typeof(ArgumentNullException));
        Check(b.Generation(host) == before && !b.Active(host), "Structural refusals preserve generation");
        using (var obsoleteCancellation = new CancellationTokenSource())
        {
            Task<long> obsoleteStart = b.Start(host, "obsolete cancelled callback", b.Options(), obsoleteCancellation.Token);
            WorkerMessage obsolete = Receive(peer.Reader);
            Task<long> current = b.Start(host, "new current generation", b.Options()); WorkerMessage newer = Receive(peer.Reader);
            obsoleteCancellation.Cancel();
            Reject(() => obsoleteStart.GetAwaiter().GetResult(), "Replaced waiter cancellation", typeof(OperationCanceledException));
            Ack(peer.Client, obsolete.Generation); Ack(peer.Client, newer.Generation);
            Check(current.GetAwaiter().GetResult() == newer.Generation && b.Active(host) && !peer.Capture.Has("F"),
                "Late cancelled option waiter cannot fault newer generation");
            Task<bool> actualPause = b.State(host, newer.Generation, true); WorkerMessage pauseRequest = Receive(peer.Reader);
            Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Paused, Generation = newer.Generation, Command = pauseRequest.Command });
            Check(actualPause.GetAwaiter().GetResult(), "Configured pause remains acknowledged");
            Task<bool> completingResume = b.State(host, newer.Generation, false); Receive(peer.Reader);
            Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Completed, Generation = newer.Generation });
            Check(!completingResume.GetAwaiter().GetResult() && !b.Active(host) && !b.Paused(host), "Configured completion during resume is terminal, not a state acknowledgement");
        }
        Task<long> final = b.Start(host, "dispose pending", b.Options()); Receive(peer.Reader);
        ((IDisposable)host).Dispose();
        Reject(() => final.GetAwaiter().GetResult(), "Dispose ends pending options", typeof(OperationCanceledException));
    }

    private static void FailureBoundaries(Boundary b)
    {
        using (var peer = new FakePeer(b))
        {
            Task<long> start = b.Start(peer.Host, "voice failure", b.Options()); WorkerMessage request = Receive(peer.Reader);
            Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Error, Generation = request.Generation, Message = "Missing voice." });
            Reject(() => start.GetAwaiter().GetResult(), "Current configured failure surfaces", typeof(InvalidOperationException));
            Check(Wait(() => peer.Capture.Has("F")) && !b.Active(peer.Host), "Current failed generation becomes inactive");
            Task<long> next = b.Start(peer.Host, "new explicit", b.Options()); WorkerMessage newer = Receive(peer.Reader);
            Ack(peer.Client, request.Generation); Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Error, Generation = request.Generation, Message = "late" });
            Ack(peer.Client, newer.Generation); Check(next.GetAwaiter().GetResult() == newer.Generation, "Failure does not poison later deliberate start");
        }
        foreach (string mode in new[] { "mismatch", "empty-applied", "complete-before-applied", "unknown-old-worker", "timeout", "cancel" })
        {
            using var peer = new FakePeer(b);
            using var cancellation = new CancellationTokenSource();
            Task<long> start = b.Start(peer.Host, "pending application", b.Options(), cancellation.Token); WorkerMessage request = Receive(peer.Reader);
            var clock = Stopwatch.StartNew();
            if (mode == "mismatch") Ack(peer.Client, request.Generation, rate: 1);
            if (mode == "empty-applied") Ack(peer.Client, request.Generation, voice: "");
            if (mode == "complete-before-applied") Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Completed, Generation = request.Generation });
            if (mode == "unknown-old-worker") Send(peer.Client, new WorkerMessage { Type = WorkerProtocol.Error, Generation = 0, Message = "Invalid host message." });
            if (mode == "cancel") cancellation.Cancel();
            Reject(() => start.GetAwaiter().GetResult(), mode + " cannot mean configured success", mode == "timeout" ? typeof(TimeoutException) : null);
            Check(!b.Active(peer.Host), mode + " deactivates generation");
            if (mode == "timeout") Check(clock.ElapsedMilliseconds >= 4500 && clock.ElapsedMilliseconds < 8000, "Actual applied-ack five-second deadline");
        }
        using (var peer = new FakePeer(b))
        {
            long legacy = b.Speak(peer.Host, "unrelated active").GetAwaiter().GetResult(); Receive(peer.Reader);
            SemaphoreSlim gate = b.Gate(peer.Host); gate.Wait();
            Task<long> queued;
            try
            {
                queued = b.Start(peer.Host, "queue timeout", b.Options());
                Reject(() => queued.GetAwaiter().GetResult(), "Queued configured timeout", typeof(TimeoutException));
                Check(b.Active(peer.Host) && b.Generation(peer.Host) == legacy, "A never-written timeout cannot fault existing generation");
            }
            finally { gate.Release(); }
        }
        using (var peer = new FakePeer(b))
        {
            Task<long>? pending = null;
            var clock = Stopwatch.StartNew();
            bool responsive = true;
            for (int i = 0; i < 4; i++)
            {
                var call = Stopwatch.StartNew();
                pending = b.Start(peer.Host, new string('x', WorkerProtocol.MaxSpeakChars), b.Options());
                responsive &= call.ElapsedMilliseconds < 500;
                Thread.Sleep(60);
                if (b.Gate(peer.Host).CurrentCount == 0) break;
            }
            Check(responsive && pending != null && !pending.IsCompleted && b.Gate(peer.Host).CurrentCount == 0,
                "Configured non-reading peer exercises asynchronous backpressure and send gate");
            Reject(() => pending!.GetAwaiter().GetResult(), "Configured stalled write bounded", typeof(TimeoutException));
            Check(!b.Active(peer.Host) && clock.ElapsedMilliseconds >= 4500 && clock.ElapsedMilliseconds < 8000,
                "Configured write failure retires active generation and closes transport");
        }
        string name = WorkerProtocol.NewPipeName();
        object host = Activator.CreateInstance(b.Host, All, null, new object?[] { name, null }, null)!;
        using (var cancellation = new CancellationTokenSource())
        {
            try
            {
                Task connect = (Task)b.Host.GetMethod("ConnectAsync", new[] { typeof(TimeSpan), typeof(CancellationToken) })!.Invoke(host, new object[] { TimeSpan.FromSeconds(5), cancellation.Token })!;
                cancellation.Cancel(); Reject(() => connect.GetAwaiter().GetResult(), "Cancelled startup", typeof(OperationCanceledException));
                using var late = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                Reject(() => late.Connect(100), "Late worker cannot connect", typeof(TimeoutException));
                Check(b.Generation(host) == 0 && !b.Active(host), "Cancelled startup cannot allocate options generation");
            }
            finally { ((IDisposable)host).Dispose(); }
        }
    }

    private static void RealHost(Boundary b, string worker)
    {
        SpeechCapabilityReport report = SpeechCapabilityQuery.QueryAsync(Path.GetFullPath(worker), TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        string voice = report.Voices.Where(v => v.Enabled && report.Voices.Count(other => other.Name == v.Name) == 1)
            .OrderBy(v => v.Name == report.DefaultVoice?.Name ? 1 : 0).First().Name;
        object host = b.Launch(worker); int pid = (int)b.Host.GetProperty("WorkerProcessId", All)!.GetValue(host)!;
        try
        {
            var capture = new Capture(b, host);
            foreach (Tuple<string, int, int> options in new[] { Tuple.Create(voice, -10, 0), Tuple.Create(voice, 10, 100), Tuple.Create("", 0, 100) })
            {
                long gen = b.Start(host, "one two three", b.Options(options.Item1, options.Item2, options.Item3)).GetAwaiter().GetResult();
                Check(Wait(() => capture.Applied.Any(v => v.Item1 == gen)), "Real applied event is generation correlated");
                object actual = capture.Applied.Single(v => v.Item1 == gen).Item2;
                Check(b.Voice(actual) == (options.Item1.Length == 0 ? report.DefaultVoice!.Name : voice) &&
                    b.Rate(actual) == options.Item2 && b.Volume(actual) == options.Item3, "Real external actual getter proof exact");
                Check(Wait(() => capture.Has("C:" + gen), 15000) && !capture.Has("F"), "Configured natural completion succeeds");
                string[] events = capture.Events.ToArray();
                Check(Array.IndexOf(events, "A:" + gen) < Array.IndexOf(events, "C:" + gen), "Applied proof precedes natural completion");
                Check(!b.State(host, gen, true).GetAwaiter().GetResult(), "Real natural completion/state outcome retained");
            }
            string longText = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 900));
            long older = b.Start(host, longText, b.Options(voice, -10, 0)).GetAwaiter().GetResult();
            Check(b.State(host, older, true).GetAwaiter().GetResult() && b.Paused(host), "Real configured prompt actually pauses");
            string missing = "Missing isolated speech option " + Guid.NewGuid().ToString("N");
            Reject(() => b.Start(host, "must refuse", b.Options(missing)).GetAwaiter().GetResult(), "Real missing name refuses without fallback", typeof(InvalidOperationException));
            long failed = b.Generation(host);
            Check(failed == older + 1 && !b.Active(host) && Wait(() => capture.Has("F")), "Real missing error belongs to replacement generation");
            long later = b.Start(host, "deliberate recovery", b.Options("", 0, 100)).GetAwaiter().GetResult();
            Check(Wait(() => capture.Has("C:" + later), 15000), "Real failure leaves no old paused sound and allows explicit recovery");
            long cancelling = b.Start(host, longText, b.Options(voice, 10, 37)).GetAwaiter().GetResult();
            Check(b.Cancel(host).GetAwaiter().GetResult() && !b.Active(host), "Real configured cancel retires prompt");
            int completed = capture.Events.Count(e => e == "C:" + cancelling);
            Thread.Sleep(100);
            Check(capture.Events.Count(e => e == "C:" + cancelling) == completed, "Late configured completion does not revive cancelled operation");
        }
        finally { ((IDisposable)host).Dispose(); Check(Wait(() => !Exists(pid), 6000), "Exact owned real Host worker cleanup"); }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject(() => b.Launch(worker, cancelled.Token), "Pre-cancelled real launch", typeof(OperationCanceledException));
    }

    private sealed class RawPeer : IDisposable
    {
        internal readonly NamedPipeServerStream Pipe;
        internal readonly Process Worker;
        internal readonly BoundedLineReader Reader;
        internal RawPeer(string worker)
        {
            string name = WorkerProtocol.NewPipeName();
            Pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                4096, 262144, VsCopilotChatVoice.Ipc.PipePeerIdentity.UserOnlySecurity());
            Task connect = Pipe.WaitForConnectionAsync();
            Worker = Process.Start(new ProcessStartInfo(worker, "--speech-worker " + name + " --silent") { UseShellExecute = false, CreateNoWindow = true })!;
            if (!connect.Wait(15000)) { Dispose(); throw new TimeoutException("Real raw worker startup."); }
            Check(VsCopilotChatVoice.Ipc.PipePeerIdentity.Client(Pipe) == Worker.Id, "Raw peer is exact launched worker");
            Reader = new BoundedLineReader(Pipe);
        }
        public void Dispose()
        {
            Pipe.Dispose();
            if (!Worker.HasExited && !Worker.WaitForExit(5000)) { Worker.Kill(); Worker.WaitForExit(3000); }
            Check(Worker.HasExited, "Exact raw child cleanup"); Worker.Dispose();
        }
    }
    private static WorkerMessage Until(RawPeer peer, string type, long gen, long command = 0)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 15000)
        {
            WorkerMessage message = Receive(peer.Reader, 15000);
            if (message.Generation < gen) continue;
            if (message.Generation != gen) throw new InvalidOperationException("Raw response has a future generation.");
            if (message.Type == type)
            {
                Check(message.Command == command, "Raw command correlation exact"); return message;
            }
            if (message.Type != WorkerProtocol.Progress)
                throw new InvalidOperationException("Unexpected raw response: " + message.Type + ", generation=" + message.Generation);
        }
        throw new TimeoutException("Raw speech option response.");
    }
    private static WorkerMessage Current(RawPeer peer, long gen)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 15000)
        {
            WorkerMessage message = Receive(peer.Reader);
            if (message.Generation < gen) continue;
            return message;
        }
        throw new TimeoutException("Raw current-generation response.");
    }
    private static SpeechOptionsSnapshot Query(RawPeer peer, long gen, long command)
    {
        Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.QueryOptions, Generation = gen, Command = command });
        return Until(peer, WorkerProtocol.OptionsApplied, gen, command).Options!;
    }

    private static void RawWorker(string worker)
    {
        using var peer = new RawPeer(worker);
        string text = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 900));
        Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.Speak, Generation = 1, Text = text });
        Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.Pause, Generation = 1, Command = 1 });
        Until(peer, WorkerProtocol.Paused, 1, 1);
        SpeechOptionsSnapshot baseline = Query(peer, 1, 2);
        Check(baseline.Voice.Length > 0, "Initial worker default identity captured");
        long gen = 1;
        foreach (Tuple<int, int> scalars in new[] { Tuple.Create(-10, 0), Tuple.Create(10, 100), Tuple.Create(0, 37) })
        {
            gen++;
            Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.SpeakWithOptions, Generation = gen, Text = text,
                Options = new SpeechOptionsSnapshot(baseline.Voice, scalars.Item1, scalars.Item2) });
            WorkerMessage applied = Current(peer, gen);
            Check(applied.Type == WorkerProtocol.OptionsApplied && applied.Generation == gen && applied.Command == 0,
                "Configured metadata must precede current progress: type=" + applied.Type + ", generation=" + applied.Generation);
            Check(applied.Options!.Voice == baseline.Voice && applied.Options.Rate == scalars.Item1 && applied.Options.Volume == scalars.Item2,
                "Actual getters include zero and extreme values");
            Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.Pause, Generation = gen, Command = 10 });
            Until(peer, WorkerProtocol.Paused, gen, 10);
            SpeechOptionsSnapshot paused = Query(peer, gen, 11);
            Check(paused.Voice == applied.Options.Voice && paused.Rate == applied.Options.Rate && paused.Volume == applied.Options.Volume,
                "Pause does not mutate options");
            using var resumeDeadline = new CancellationTokenSource(15000);
            Task<string?> quiet = peer.Reader.ReadLineAsync(resumeDeadline.Token);
            Thread.Sleep(100); Check(!quiet.IsCompleted, "Actual paused worker emits no progress/completion");
            Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.Resume, Generation = gen, Command = 12 });
            WorkerMessage resumed = WorkerProtocol.TryParse(quiet.GetAwaiter().GetResult() ?? throw new InvalidOperationException("Raw resume EOF."))!;
            bool stateSeen = resumed.Type == WorkerProtocol.Resumed, progressSeen = resumed.Type == WorkerProtocol.Progress;
            bool correlation = resumed.Type != WorkerProtocol.Resumed || resumed.Command == 12;
            bool offsets = resumed.Type != WorkerProtocol.Progress || (resumed.Offset >= 0 && resumed.Offset < text.Length);
            while (!stateSeen || !progressSeen)
            {
                if (resumed.Generation != gen || (resumed.Type != WorkerProtocol.Resumed && resumed.Type != WorkerProtocol.Progress))
                    throw new InvalidOperationException("Resume replaced or failed the original configured prompt.");
                if (resumed.Type == WorkerProtocol.Resumed) { stateSeen = true; correlation &= resumed.Command == 12; }
                if (resumed.Type == WorkerProtocol.Progress) { progressSeen = true; offsets &= resumed.Offset >= 0 && resumed.Offset < text.Length; }
                if (!stateSeen || !progressSeen) resumed = Receive(peer.Reader);
            }
            Check(correlation && offsets, "Resume keeps original configured prompt/command/progress offsets");
            Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.Pause, Generation = gen, Command = 13 }); Until(peer, WorkerProtocol.Paused, gen, 13);
            SpeechOptionsSnapshot still = Query(peer, gen, 14);
            Check(still.Voice == applied.Options.Voice && still.Rate == scalars.Item1 && still.Volume == scalars.Item2, "Resume/pause preserve configured getters");
        }
        gen++;
        Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.SpeakWithOptions, Generation = gen, Text = text, Options = new SpeechOptionsSnapshot("", baseline.Rate, baseline.Volume) });
        WorkerMessage reset = Current(peer, gen);
        Check(reset.Options!.Voice == baseline.Voice && reset.Options.Rate == baseline.Rate && reset.Options.Volume == baseline.Volume, "Explicit engine-default reset restores identity/rate/volume snapshot");
        Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.Pause, Generation = gen, Command = 20 }); Until(peer, WorkerProtocol.Paused, gen, 20);
        gen++;
        Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.SpeakWithOptions, Generation = gen, Text = text, Options = new SpeechOptionsSnapshot(baseline.Voice, 10, 0) });
        Current(peer, gen); Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.Pause, Generation = gen, Command = 21 }); Until(peer, WorkerProtocol.Paused, gen, 21);
        gen++;
        Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.Speak, Generation = gen, Text = text });
        Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.Pause, Generation = gen, Command = 22 });
        Until(peer, WorkerProtocol.Paused, gen, 22);
        SpeechOptionsSnapshot legacy = Query(peer, gen, 23);
        Check(legacy.Voice == baseline.Voice && legacy.Rate == baseline.Rate && legacy.Volume == baseline.Volume,
            "Legacy prompt after configured restores worker-initial defaults without extra legacy frames");
        gen++;
        Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.SpeakWithOptions, Generation = gen, Text = "refuse", Options = new SpeechOptionsSnapshot("Missing isolated " + Guid.NewGuid().ToString("N"), 0, 100) });
        WorkerMessage missing = Current(peer, gen);
        Check(missing.Type == WorkerProtocol.Error && missing.Generation == gen && missing.Message!.Contains("Missing"), "Real missing selection surfaces tagged reason without fallback");
        Task<string?> stopped = peer.Reader.ReadLineAsync(CancellationToken.None); Thread.Sleep(100);
        Check(!stopped.IsCompleted, "Failed replacement leaves no old sound/progress/completion behind inactive generation");
        Send(peer.Pipe, new WorkerMessage { Type = WorkerProtocol.Shutdown });
        Check(peer.Worker.WaitForExit(5000) && peer.Worker.ExitCode == 0, "Raw configured worker graceful shutdown exit zero");
        Check(stopped.GetAwaiter().GetResult() == null, "Shutdown closes outstanding raw read");
    }

    private static void InvalidUtf8Worker(string worker)
    {
        using var peer = new RawPeer(worker);
        byte[] bytes = WorkerProtocol.Serialize(new WorkerMessage { Type = WorkerProtocol.SpeakWithOptions, Generation = 1, Text = "Hello", Options = new SpeechOptionsSnapshot("Fixture", 0, 100) });
        int index = Encoding.UTF8.GetString(bytes).IndexOf("Fixture", StringComparison.Ordinal); bytes[index] = 0xff;
        peer.Pipe.Write(bytes, 0, bytes.Length); peer.Pipe.Flush();
        WorkerMessage failure = Receive(peer.Reader);
        Check(failure.Type == WorkerProtocol.Error && peer.Worker.WaitForExit(5000) && peer.Worker.ExitCode == 0, "Invalid configured UTF8 refuses without substituted default");
    }
    private static bool Exists(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
