using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VsCopilotChatVoice.Speech;

// Checks the shared pipe protocol, the host's generation guard and the real silent speech worker.
// No audio is played and no Visual Studio instance, preference file or draft is touched.
internal static class SpeechWorkerRegressionTests
{
    private static int assertions;

    internal static int Run()
    {
        string worker = Path.Combine(AppContext.BaseDirectory, "VsCopilotChatVoice.Reader.exe");
        try
        {
            Protocol();
            WordModel();
            FakeWorker();
            RejectedConnections();
            RealWorker(worker);
            Console.WriteLine("PASS: speech worker checks, " + assertions + " assertions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void Check(bool condition, string description)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(description);
    }

    private static bool WaitFor(Func<bool> condition, int milliseconds)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    private static void Protocol()
    {
        string line = Encoding.UTF8.GetString(WorkerProtocol.Serialize(new WorkerMessage { Type = WorkerProtocol.Speak, Generation = 3, Text = "Hello there" }));
        Check(line.EndsWith("\n") && line.IndexOf('\n') == line.Length - 1, "A message must be exactly one line.");
        WorkerMessage parsed = WorkerProtocol.TryParse(line.TrimEnd('\n'));
        Check(parsed != null && parsed.Generation == 3 && parsed.Text == "Hello there", "Speak did not round-trip.");
        Check(WorkerProtocol.TryParse("{\"type\":\"explode\",\"gen\":1}") == null, "An unknown type was accepted.");
        Check(WorkerProtocol.TryParse("{\"type\":\"speak\",\"gen\":1}") == null, "Speak without text was accepted.");
        Check(WorkerProtocol.TryParse("{\"type\":\"speak\",\"gen\":0,\"text\":\"x\"}") == null, "Generation zero was accepted for speak.");
        Check(WorkerProtocol.TryParse("{\"type\":\"progress\",\"gen\":1,\"offset\":-1}") == null, "A negative offset was accepted.");
        Check(WorkerProtocol.TryParse("not json") == null && WorkerProtocol.TryParse("") == null, "Malformed input was accepted.");
        Check(WorkerProtocol.TryParse("{\"type\":\"speak\",\"gen\":1,\"text\":\"" + new string('a', WorkerProtocol.MaxSpeakChars + 1) + "\"}") == null,
            "Over-long speech text was accepted.");
        Check(WorkerProtocol.TryParse("{\"type\":\"error\",\"gen\":0,\"message\":\"" + new string('m', 500) + "\"}").Message.Length == WorkerProtocol.MaxMessageChars,
            "An error message was not bounded.");
        Check(WorkerProtocol.IsValidPipeName(WorkerProtocol.NewPipeName()), "A generated pipe name was rejected.");
        Check(!WorkerProtocol.IsValidPipeName("..\\evil") && !WorkerProtocol.IsValidPipeName("VsCopilotChatVoice.Speech.xyz") &&
            !WorkerProtocol.IsValidPipeName(null), "An invalid pipe name was accepted.");
        bool tooLong = false;
        try
        {
            var stream = new MemoryStream(Enumerable.Repeat((byte)'a', WorkerProtocol.MaxLineBytes + 10).ToArray());
            new BoundedLineReader(stream).ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (InvalidDataException) { tooLong = true; }
        Check(tooLong, "An over-long line was not rejected.");
        var lines = new BoundedLineReader(new MemoryStream(Encoding.UTF8.GetBytes("one\r\ntwo\n")));
        Check(lines.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult() == "one" &&
            lines.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult() == "two" &&
            lines.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult() == null, "Line reading changed lines or EOF.");
    }

    private static void WordModel()
    {
        var words = ReplyText.Tokenize("Alpha  beta\r\n\r\ngamma");
        Check(words.Count == 3 && words[0].Start == 0 && words[1].Start == 7 && words[2].Start == 15, "Words mapped to wrong offsets.");
        Check(ReplyText.FindWord(words, 0) == 0 && ReplyText.FindWord(words, 5) == 0 && ReplyText.FindWord(words, 7) == 1 &&
            ReplyText.FindWord(words, 100) == 2, "Offset lookup chose the wrong word.");
        Check(ReplyText.FindWord(ReplyText.Tokenize("   "), 0) == -1, "An empty text matched a word.");
    }

    private sealed class Capture
    {
        public readonly ConcurrentQueue<string> Events = new ConcurrentQueue<string>();
        public int Count { get { return Events.Count; } }
        public bool Has(string prefix) { return Events.Any(e => e.StartsWith(prefix, StringComparison.Ordinal)); }
        public void Attach(SpeechWorkerHost host)
        {
            host.Progress += (g, o) => Events.Enqueue("P:" + g + ":" + o);
            host.Completed += g => Events.Enqueue("C:" + g);
            host.Faulted += m => Events.Enqueue("F:" + m);
        }
    }

    private static void Send(NamedPipeClientStream client, WorkerMessage message)
    {
        byte[] bytes = WorkerProtocol.Serialize(message);
        client.Write(bytes, 0, bytes.Length);
        client.Flush();
    }

    private static WorkerMessage Receive(BoundedLineReader reader)
    {
        string line = reader.ReadLineAsync(new CancellationTokenSource(5000).Token).GetAwaiter().GetResult();
        return line == null ? null : WorkerProtocol.TryParse(line);
    }

    private static void FakeWorker()
    {
        string name = WorkerProtocol.NewPipeName();
        using (var host = new SpeechWorkerHost(name, null))
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            var capture = new Capture();
            capture.Attach(host);
            Task connect = host.ConnectAsync(TimeSpan.FromSeconds(5));
            client.Connect(5000);
            connect.GetAwaiter().GetResult();
            var reader = new BoundedLineReader(client);

            Send(client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = 1, Offset = 0 });
            Thread.Sleep(150);
            Check(capture.Count == 0 && !host.IsActive, "Progress before any speech was delivered.");

            long first = host.Speak("one two three four");
            WorkerMessage heard = Receive(reader);
            Check(first == 1 && heard != null && heard.Type == WorkerProtocol.Speak && heard.Generation == 1 && heard.Text == "one two three four" && host.IsActive,
                "The worker did not receive the first speak request.");
            Send(client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = 1, Offset = 5000 });
            Send(client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = 9, Offset = 3 });
            Send(client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = 1, Offset = 4 });
            Check(WaitFor(() => capture.Has("P:1:4"), 3000), "A valid progress event was not delivered.");
            Check(capture.Count == 1, "An out-of-range or wrong-generation progress event was delivered.");
            Send(client, new WorkerMessage { Type = WorkerProtocol.Completed, Generation = 1 });
            Check(WaitFor(() => capture.Has("C:1"), 3000) && !host.IsActive, "Completion was not delivered.");
            Send(client, new WorkerMessage { Type = WorkerProtocol.Completed, Generation = 1 });
            Send(client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = 1, Offset = 8 });
            Thread.Sleep(200);
            Check(capture.Count == 2, "Late events revived completed speech.");

            Check(!host.Cancel(), "Cancel reported work although nothing was active.");
            long second = host.Speak("five six seven eight");
            Receive(reader);
            Check(second == 2, "The second request did not get a new generation.");
            Check(host.Cancel() && !host.IsActive, "Cancel did not stop the active request.");
            heard = Receive(reader);
            Check(heard != null && heard.Type == WorkerProtocol.Cancel && heard.Generation == 2, "The worker did not receive the cancel request.");
            Send(client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = 2, Offset = 5 });
            Send(client, new WorkerMessage { Type = WorkerProtocol.Cancelled, Generation = 2 });
            Send(client, new WorkerMessage { Type = WorkerProtocol.Completed, Generation = 2 });
            Thread.Sleep(200);
            Check(capture.Count == 2, "Late events revived cancelled speech.");

            long third = host.Speak("nine ten");
            Receive(reader);
            Send(client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = 2, Offset = 1 });
            Send(client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = 3, Offset = 5 });
            Check(third == 3 && WaitFor(() => capture.Has("P:3:5"), 3000), "The replacement request did not deliver its own progress.");
            Check(!capture.Has("P:2:1"), "A stale generation leaked into the replacement request.");

            Send(client, new WorkerMessage { Type = WorkerProtocol.Speak, Generation = 4, Text = "wrong direction" });
            Check(WaitFor(() => capture.Has("F:"), 3000), "A host-bound message from the worker was not treated as a fault.");
            bool refused = false;
            try { host.Speak("after fault"); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "Speech started after the worker faulted.");
        }

        name = WorkerProtocol.NewPipeName();
        using (var host = new SpeechWorkerHost(name, null))
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            var capture = new Capture();
            capture.Attach(host);
            Task connect = host.ConnectAsync(TimeSpan.FromSeconds(5));
            client.Connect(5000);
            connect.GetAwaiter().GetResult();
            byte[] garbage = Encoding.UTF8.GetBytes("this is not json\n");
            client.Write(garbage, 0, garbage.Length);
            Check(WaitFor(() => capture.Has("F:"), 3000), "A malformed line did not fault the host.");
        }

        name = WorkerProtocol.NewPipeName();
        using (var host = new SpeechWorkerHost(name, null))
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            var capture = new Capture();
            capture.Attach(host);
            Task connect = host.ConnectAsync(TimeSpan.FromSeconds(5));
            client.Connect(5000);
            connect.GetAwaiter().GetResult();
            byte[] huge = Enumerable.Repeat((byte)'a', WorkerProtocol.MaxLineBytes + 100).ToArray();
            try { client.Write(huge, 0, huge.Length); } catch (IOException) { }
            Check(WaitFor(() => capture.Has("F:"), 5000), "An over-long line did not fault the host.");
        }
    }

    private static void RejectedConnections()
    {
        string name = WorkerProtocol.NewPipeName();
        using (var host = new SpeechWorkerHost(name, null))
        {
            bool timedOut = false;
            try { host.ConnectAsync(TimeSpan.FromMilliseconds(200)).GetAwaiter().GetResult(); } catch (TimeoutException) { timedOut = true; }
            Check(timedOut, "A missing worker did not time out.");
        }

        name = WorkerProtocol.NewPipeName();
        var impostor = new SpeechWorkerHost(name, 4);
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            Task connect = impostor.ConnectAsync(TimeSpan.FromSeconds(5));
            client.Connect(5000);
            bool refused = false;
            try { connect.GetAwaiter().GetResult(); } catch (UnauthorizedAccessException) { refused = true; }
            Check(refused, "A process other than the launched worker was accepted.");
        }
    }

    private static void RealWorker(string workerExecutable)
    {
        Check(File.Exists(workerExecutable), "The reader executable was not next to the test harness.");
        string text = "One two three four five six seven eight nine ten. Eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty.";
        int workerPid;
        using (SpeechWorkerHost host = SpeechWorkerHost.LaunchAsync(workerExecutable, true, TimeSpan.FromSeconds(15)).GetAwaiter().GetResult())
        {
            var capture = new Capture();
            capture.Attach(host);
            workerPid = host.WorkerProcessId.Value;
            Check(host.Speak(text) == 1, "The real worker request did not get generation 1.");
            Check(WaitFor(() => capture.Has("C:1"), 30000), "The real worker did not complete a silent request.");
            var progress = capture.Events.Where(e => e.StartsWith("P:", StringComparison.Ordinal)).Select(e => e.Split(':')).ToList();
            Check(progress.Count > 3 && progress.All(p => p[1] == "1" && int.Parse(p[2]) < text.Length), "Real progress was missing or out of range.");
            Check(progress.Select(p => int.Parse(p[2])).SequenceEqual(progress.Select(p => int.Parse(p[2])).OrderBy(v => v)), "Real progress moved backwards.");
            Check(!capture.Has("F:") && !host.IsActive, "The real worker faulted or stayed active after completing.");

            string longText = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 40));
            Check(host.Speak(longText) == 2, "The second real request did not get generation 2.");
            Check(WaitFor(() => capture.Has("P:2:"), 15000), "The second real request delivered no progress.");
            Check(host.Cancel() && !host.IsActive, "Cancel did not stop the real request.");
            int afterCancel = capture.Count;
            Thread.Sleep(1500);
            Check(capture.Count == afterCancel && !capture.Has("C:2"), "The real worker's late events revived cancelled speech.");

            Check(host.Speak("Three words here.") == 3, "A request after cancel did not get generation 3.");
            Check(WaitFor(() => capture.Has("C:3"), 30000), "A request after cancel did not complete.");
            Check(!capture.Events.Any(e => e.StartsWith("P:2:", StringComparison.Ordinal) && capture.Events.ToList().IndexOf(e) >= afterCancel),
                "Cancelled generation progress arrived after cancel.");
        }
        Check(WaitFor(() => !ProcessExists(workerPid), 5000), "Disposing the host did not stop the worker.");

        int killedPid;
        using (SpeechWorkerHost host = SpeechWorkerHost.LaunchAsync(workerExecutable, true, TimeSpan.FromSeconds(15)).GetAwaiter().GetResult())
        {
            var capture = new Capture();
            capture.Attach(host);
            killedPid = host.WorkerProcessId.Value;
            host.Speak(text);
            System.Diagnostics.Process.GetProcessById(killedPid).Kill();
            Check(WaitFor(() => capture.Has("F:"), 10000) && !host.IsActive, "A killed worker was not reported as a fault.");
            bool refused = false;
            try { host.Speak("after the worker died"); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "Speech started after the worker died.");
        }
    }

    private static bool ProcessExists(int pid)
    {
        try { using (var p = System.Diagnostics.Process.GetProcessById(pid)) return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
