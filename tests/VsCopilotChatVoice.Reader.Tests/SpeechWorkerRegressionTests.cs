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
            StateCommands();
            StateAcknowledgementTimeout();
            CancelledStartup(worker);
            CancelQueuedReplacement();
            LegacyCancellationTimeout();
            StalledAsyncWrite();
            RejectedConnections();
            RealWorker(worker);
            RawPausedWorker(worker);
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
        foreach (string type in new[] { WorkerProtocol.Pause, WorkerProtocol.Resume, WorkerProtocol.Paused, WorkerProtocol.Resumed })
        {
            Check(WorkerProtocol.TryParse("{\"type\":\"" + type + "\",\"gen\":1,\"command\":2}") != null, "A valid state command was refused.");
            Check(WorkerProtocol.TryParse("{\"type\":\"" + type + "\",\"gen\":1}") == null, "An uncorrelated state command was accepted.");
        }
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
            host.StateChanged += (g, p) => Events.Enqueue("S:" + g + ":" + p);
        }
    }

    private static void Send(Stream client, WorkerMessage message)
    {
        byte[] bytes = WorkerProtocol.Serialize(message);
        client.Write(bytes, 0, bytes.Length);
        client.Flush();
    }

    private static void StateCommands()
    {
        string name = WorkerProtocol.NewPipeName();
        using (var host = new SpeechWorkerHost(name, null))
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            var capture = new Capture();
            capture.Attach(host);
            Task connect = host.ConnectAsync(TimeSpan.FromSeconds(5));
            client.Connect(5000); connect.GetAwaiter().GetResult();
            var reader = new BoundedLineReader(client);
            long gen = host.SpeakAsync("one two three four").GetAwaiter().GetResult();
            Receive(reader);
            bool bounded = false;
            try { host.SpeakAsync(new string('\u4E00', WorkerProtocol.MaxSpeakChars)).GetAwaiter().GetResult(); }
            catch (ArgumentException) { bounded = true; }
            Check(bounded && host.CurrentGeneration == gen && host.IsActive, "Rejected frame changed active speech.");
            Task pause = host.PauseAsync(gen);
            WorkerMessage request = Receive(reader);
            Check(request.Type == WorkerProtocol.Pause && request.Command > 0 && !pause.IsCompleted && !host.IsPaused,
                "Pause was reported before its correlated acknowledgement.");
            Send(client, new WorkerMessage { Type = WorkerProtocol.Paused, Generation = gen + 1, Command = request.Command });
            Send(client, new WorkerMessage { Type = WorkerProtocol.Paused, Generation = gen, Command = request.Command + 1 });
            Send(client, new WorkerMessage { Type = WorkerProtocol.Resumed, Generation = gen, Command = request.Command });
            Send(client, new WorkerMessage { Type = WorkerProtocol.Error, Generation = gen, Command = request.Command + 1, Message = "Stale state error." });
            Thread.Sleep(150);
            Check(!pause.IsCompleted && !host.IsPaused && !capture.Has("F:"), "A foreign state acknowledgement/error changed playback.");
            Send(client, new WorkerMessage { Type = WorkerProtocol.Paused, Generation = gen, Command = request.Command });
            Check(pause.Wait(2000) && host.IsPaused && host.CurrentGeneration == gen, "Pause did not preserve the active prompt generation.");
            Check(WaitFor(() => capture.Has("S:" + gen + ":True"), 2000), "Confirmed pause state was not published.");
            int count = capture.Count;
            Send(client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = gen, Offset = 4 });
            Send(client, new WorkerMessage { Type = WorkerProtocol.Paused, Generation = gen, Command = request.Command });
            Thread.Sleep(150);
            Check(capture.Count == count, "Paused/duplicate events leaked into presentation.");
            Task resume = host.ResumeAsync(gen);
            WorkerMessage resuming = Receive(reader);
            Check(resuming.Type == WorkerProtocol.Resume && resuming.Command > request.Command && host.IsPaused,
                "Resume did not use a fresh command or waited state.");
            Send(client, new WorkerMessage { Type = WorkerProtocol.Paused, Generation = gen, Command = request.Command });
            Thread.Sleep(100);
            Check(!resume.IsCompleted && host.IsPaused, "An old pause acknowledgement completed resume.");
            Send(client, new WorkerMessage { Type = WorkerProtocol.Resumed, Generation = gen, Command = resuming.Command });
            Check(resume.Wait(2000) && !host.IsPaused && host.IsActive, "Resume did not restore the same generation.");
            Send(client, new WorkerMessage { Type = WorkerProtocol.Progress, Generation = gen, Offset = 4 });
            Check(WaitFor(() => capture.Has("P:" + gen + ":4"), 2000), "Resumed progress was not delivered.");
            Task cancelledPause = host.PauseAsync(gen);
            WorkerMessage obsolete = Receive(reader);
            Check(host.CancelAsync().GetAwaiter().GetResult(), "Async cancel did not stop active speech.");
            Check(Receive(reader).Type == WorkerProtocol.Cancel, "Cancel was blocked by an unacknowledged pause.");
            Check(WaitFor(() => cancelledPause.IsCanceled, 2000), "Cancel left the pause acknowledgement pending.");
            Send(client, new WorkerMessage { Type = WorkerProtocol.Paused, Generation = gen, Command = obsolete.Command });
            Thread.Sleep(100);
            Check(!host.IsActive && !host.IsPaused, "A late acknowledgement revived cancelled speech.");
            long replacement = host.SpeakAsync("fresh words").GetAwaiter().GetResult(); Receive(reader);
            Task completingPause = host.PauseAsync(replacement); Receive(reader);
            Send(client, new WorkerMessage { Type = WorkerProtocol.Completed, Generation = replacement });
            Check(WaitFor(() => completingPause.IsCanceled, 2000) && !host.IsActive, "Completion left a state request pending.");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                Task<long> skipped = host.SpeakAsync("must not start", cancelled.Token);
                Check(skipped.IsCanceled && host.CurrentGeneration == replacement, "A pre-cancelled start allocated a generation.");
            }
            long final = host.SpeakAsync("final words").GetAwaiter().GetResult(); Receive(reader);
            Task disposedPause = host.PauseAsync(final); Receive(reader);
            host.Dispose();
            Check(WaitFor(() => disposedPause.IsCanceled, 2000), "Disposal left a state acknowledgement waiting.");
        }
    }

    private static void StalledAsyncWrite()
    {
        string name = WorkerProtocol.NewPipeName();
        using (var host = new SpeechWorkerHost(name, null))
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            Task connect = host.ConnectAsync(TimeSpan.FromSeconds(5));
            client.Connect(5000); connect.GetAwaiter().GetResult();
            Task<long> pending = null;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            bool responsive = true;
            for (int i = 0; i < 10; i++)
            {
                var call = System.Diagnostics.Stopwatch.StartNew();
                pending = host.SpeakAsync(new string('x', WorkerProtocol.MaxSpeakChars));
                responsive &= call.ElapsedMilliseconds < 500;
                if (!pending.Wait(150)) break;
                pending.GetAwaiter().GetResult();
            }
            Check(responsive, "An asynchronous write blocked the caller.");
            Check(pending != null && !pending.IsCompleted, "The non-reading peer did not exercise backpressure.");
            bool timedOut = false;
            try { pending.GetAwaiter().GetResult(); } catch (TimeoutException) { timedOut = true; }
            Check(timedOut && elapsed.ElapsedMilliseconds >= 4500 && elapsed.ElapsedMilliseconds < 8000 && !host.IsActive,
                "A stalled write did not enforce the actual five-second deadline and deactivate speech: timeout=" + timedOut +
                ", elapsed=" + elapsed.ElapsedMilliseconds + ", active=" + host.IsActive);
        }
    }

    private static void CancelQueuedReplacement()
    {
        string name = WorkerProtocol.NewPipeName();
        using (var host = new SpeechWorkerHost(name, null))
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            Task connect = host.ConnectAsync(TimeSpan.FromSeconds(5));
            client.Connect(5000); connect.GetAwaiter().GetResult();
            var reader = new BoundedLineReader(client);
            long first = host.SpeakAsync("currently active words").GetAwaiter().GetResult(); Receive(reader);
            var gate = (SemaphoreSlim)typeof(SpeechWorkerHost).GetField("sendGate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(host);
            Check(gate.Wait(2000), "Could not hold the serialization gate for the queued-start fixture.");
            Task<long> queued;
            Task<bool> cancel;
            try
            {
                queued = host.SpeakAsync("replacement queued before stop");
                cancel = host.CancelAsync();
                Check(!queued.IsCompleted && !cancel.IsCompleted && !host.IsActive, "Cancel did not immediately fence active progress.");
            }
            finally { gate.Release(); }
            Check(cancel.GetAwaiter().GetResult(), "Cancel did not stop the active generation.");
            Check(WaitFor(() => queued.IsCanceled, 2000), "An earlier queued replacement survived Cancel.");
            WorkerMessage stopped = Receive(reader);
            Check(stopped.Type == WorkerProtocol.Cancel && stopped.Generation == first && host.CurrentGeneration == first && !host.IsActive,
                "Cancel sent an obsolete generation after queued speech reactivated.");
            Check(gate.Wait(2000), "Could not hold the gate for a first-start cancellation.");
            Task<long> firstStart;
            try
            {
                firstStart = host.SpeakAsync("queued while nothing active");
                Check(!host.CancelAsync().GetAwaiter().GetResult(), "Idle cancellation claimed active speech.");
            }
            finally { gate.Release(); }
            Check(WaitFor(() => firstStart.IsCanceled, 2000) && host.CurrentGeneration == first && !host.IsActive,
                "An idle Stop allowed an earlier queued first start to speak.");
            long deliberate = host.SpeakAsync("deliberate play after stop").GetAwaiter().GetResult();
            Check(Receive(reader).Type == WorkerProtocol.Speak && deliberate == first + 1 && host.IsActive,
                "Cancelling earlier starts prevented a later deliberate Play.");
        }
    }

    private static void LegacyCancellationTimeout()
    {
        string name = WorkerProtocol.NewPipeName();
        using (var host = new SpeechWorkerHost(name, null))
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            Task connect = host.ConnectAsync(TimeSpan.FromSeconds(5));
            client.Connect(5000); connect.GetAwaiter().GetResult();
            foreach (int length in new[] { 100000, 100000, 61925 })
                host.SpeakAsync(new string('x', length)).GetAwaiter().GetResult();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            bool compatible = false;
            try { host.Cancel(); }
            catch (InvalidOperationException ex) { compatible = ex.InnerException is TimeoutException; }
            Check(compatible, "Legacy Cancel exposed a timeout outside its preserved UI exception contract.");
            Check(elapsed.ElapsedMilliseconds >= 4500 && elapsed.ElapsedMilliseconds < 8000 && !host.IsActive,
                "Legacy cancellation did not bound actual backpressure and stop presentation.");
        }
    }

    private static void StateAcknowledgementTimeout()
    {
        string name = WorkerProtocol.NewPipeName();
        using (var host = new SpeechWorkerHost(name, null))
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            var capture = new Capture();
            capture.Attach(host);
            Task connect = host.ConnectAsync(TimeSpan.FromSeconds(5));
            client.Connect(5000); connect.GetAwaiter().GetResult();
            var reader = new BoundedLineReader(client);
            long gen = host.SpeakAsync("unacknowledged state words").GetAwaiter().GetResult(); Receive(reader);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            Task pause = host.PauseAsync(gen);
            Check(Receive(reader).Type == WorkerProtocol.Pause, "The timeout fixture did not receive pause.");
            bool timedOut = false;
            try { pause.GetAwaiter().GetResult(); } catch (TimeoutException) { timedOut = true; }
            Check(timedOut && elapsed.ElapsedMilliseconds >= 4500 && elapsed.ElapsedMilliseconds < 8000,
                "A missing state acknowledgement did not enforce the five-second deadline.");
            Check(!host.IsActive && !host.IsPaused && capture.Has("F:"), "Uncertain external state was not explicitly faulted.");
            bool refused = false;
            try { host.SpeakAsync("must require a new host").GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused && host.CurrentGeneration == gen, "A faulted connection silently restarted speech.");
        }
    }

    private static void CancelledStartup(string executable)
    {
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            Task<SpeechWorkerHost> skipped = SpeechWorkerHost.LaunchAsync(executable, true, TimeSpan.FromSeconds(5), cancelled.Token);
            Check(skipped.IsCanceled, "A pre-cancelled launch did not stop before starting a worker.");
        }
        string name = WorkerProtocol.NewPipeName();
        using (var host = new SpeechWorkerHost(name, null))
        using (var cancelled = new CancellationTokenSource())
        {
            Task connect = host.ConnectAsync(TimeSpan.FromSeconds(10), cancelled.Token);
            cancelled.Cancel();
            Check(WaitFor(() => connect.IsCompleted, 2000) && connect.IsCanceled, "Cancellation left startup waiting on the actual pipe.");
            Check(!host.IsActive && host.CurrentGeneration == 0, "Cancelled startup allocated speech state.");
            using (var late = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                bool refused = false;
                try { late.Connect(200); } catch (TimeoutException) { refused = true; }
                Check(refused, "Cancelled startup accepted a late worker connection.");
            }
        }
    }

    private static void RawPausedWorker(string executable)
    {
        string name = WorkerProtocol.NewPipeName();
        using (var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 262144, VsCopilotChatVoice.Ipc.PipePeerIdentity.UserOnlySecurity()))
        using (var deadline = new CancellationTokenSource(15000))
        using (deadline.Token.Register(() => server.Dispose()))
        {
            Task connect = server.WaitForConnectionAsync(deadline.Token);
            using (var worker = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable, "--speech-worker " + name + " --silent")
                { UseShellExecute = false, CreateNoWindow = true }))
            {
                try
                {
                    connect.GetAwaiter().GetResult();
                    Check(VsCopilotChatVoice.Ipc.PipePeerIdentity.Client(server) == worker.Id, "The raw fixture connected to an unexpected worker.");
                    var reader = new BoundedLineReader(server);
                    string text = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 900));
                    Send(server, new WorkerMessage { Type = WorkerProtocol.Speak, Generation = 1, Text = text });
                    Send(server, new WorkerMessage { Type = WorkerProtocol.Pause, Generation = 1, Command = 1 });
                    WorkerMessage paused;
                    do
                    {
                        paused = WorkerProtocol.TryParse(reader.ReadLineAsync(deadline.Token).GetAwaiter().GetResult());
                        if (paused == null || paused.Generation != 1 || (paused.Type != WorkerProtocol.Progress && paused.Type != WorkerProtocol.Paused))
                            throw new InvalidOperationException("The real worker failed before acknowledging pause.");
                    } while (paused.Type != WorkerProtocol.Paused);
                    Check(paused.Command == 1, "The real worker did not correlate pause.");
                    Task<string> quiet = reader.ReadLineAsync(deadline.Token);
                    Thread.Sleep(300);
                    Check(!quiet.IsCompleted, "The actual worker emitted progress/completion while paused (without host filtering).");
                    Send(server, new WorkerMessage { Type = WorkerProtocol.Resume, Generation = 1, Command = 2 });
                    WorkerMessage resumed = WorkerProtocol.TryParse(quiet.GetAwaiter().GetResult());
                    bool resumeSeen = false, progressSeen = false;
                    while (!resumeSeen || !progressSeen)
                    {
                        if (resumed == null || resumed.Generation != 1 || (resumed.Type != WorkerProtocol.Resumed && resumed.Type != WorkerProtocol.Progress))
                            throw new InvalidOperationException("The real worker replaced or failed the paused prompt.");
                        if (resumed.Type == WorkerProtocol.Resumed) { Check(resumed.Command == 2, "The real worker did not correlate resume."); resumeSeen = true; }
                        if (resumed.Type == WorkerProtocol.Progress) progressSeen = true;
                        if (!resumeSeen || !progressSeen) resumed = WorkerProtocol.TryParse(reader.ReadLineAsync(deadline.Token).GetAwaiter().GetResult());
                    }
                    Check(resumeSeen && progressSeen, "The same real prompt did not resume progress.");
                    Send(server, new WorkerMessage { Type = WorkerProtocol.Cancel, Generation = 1 });
                    Send(server, new WorkerMessage { Type = WorkerProtocol.Shutdown });
                    Check(worker.WaitForExit(5000) && worker.ExitCode == 0, "Paused/resumed worker did not shut down cleanly.");
                }
                finally
                {
                    server.Dispose();
                    if (!worker.HasExited && !worker.WaitForExit(3000)) worker.Kill();
                }
            }
        }
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
