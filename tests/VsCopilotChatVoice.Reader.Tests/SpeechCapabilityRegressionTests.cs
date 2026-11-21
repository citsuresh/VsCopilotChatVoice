#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReadAlongTrial;
using VsCopilotChatVoice.Speech;

internal static class SpeechCapabilityRegressionTests
{
    private static int assertions;
    private static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(label);
    }
    private static void Reject(Action action, string label)
    {
        try { action(); }
        catch (InvalidDataException) { Check(true, label); return; }
        throw new InvalidOperationException("Accepted " + label);
    }
    private static void Expect<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, label); return; }
        throw new InvalidOperationException("Accepted " + label);
    }
    private static SpeechVoiceCapability Voice(string name = "Fixture voice", string culture = "en-US", bool enabled = true) =>
        new SpeechVoiceCapability { Name = name, Culture = culture, Enabled = enabled };
    private static SpeechCapabilityReport Fixture() => new SpeechCapabilityReport
    { Voices = new[] { Voice(), Voice("Disabled fixture", "fr-FR", false) }, DefaultVoice = Voice() };
    private static string Json(SpeechCapabilityReport? report = null) => SpeechCapabilities.Utf8.GetString(SpeechCapabilities.Serialize(report ?? Fixture()));

    internal static int Run(string[] args)
    {
        if (args.Length > 2) { Console.Error.WriteLine("Use --speech-capabilities-probe [VSIX-DLL]."); return 2; }
        string root = Path.Combine(Environment.CurrentDirectory, ".speech-capability-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".isolated"), "speech-capability-isolated");
        try
        {
            Contract();
            NoSpeechImplementation();
            Processes(root).GetAwaiter().GetResult();
            byte[] real = RealExternal().GetAwaiter().GetResult();
            if (args.Length == 2)
            {
                string dll = Path.GetFullPath(args[1]);
                Dll(dll, real);
                Starvation(root, dll);
            }
            Console.WriteLine("PASS: speech capabilities, " + assertions + " assertions; external no-speech report verified (voice identifiers withheld).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: speech capabilities: " + ex); return 1; }
        finally { Directory.Delete(root, true); }
    }

    private static void Contract()
    {
        string json = Json();
        SpeechCapabilityReport parsed = SpeechCapabilities.Parse(SpeechCapabilities.Utf8.GetBytes(json));
        Check(parsed.Schema == SpeechCapabilities.Schema && parsed.Version == 1 && parsed.Engine == "System.Speech", "Versioned schema roundtrip");
        Check(parsed.Voices.Length == 2 && !parsed.Voices[1].Enabled && parsed.DefaultVoice!.Name == parsed.Voices[0].Name, "Default and disabled state roundtrip");
        Check(parsed.RateMin == -10 && parsed.RateMax == 10 && parsed.VolumeMin == 0 && parsed.VolumeMax == 100 && !parsed.PitchSupported, "Supported bounds without pitch");
        Check(parsed.AssessRememberedVoice(null) == RememberedVoiceAvailability.NotSelected, "No remembered selection");
        Check(parsed.AssessRememberedVoice("absent") == RememberedVoiceAvailability.Missing, "Missing selection explicitly reported");
        Check(parsed.AssessRememberedVoice("Disabled fixture") == RememberedVoiceAvailability.Disabled, "Disabled selection explicitly reported");
        Check(parsed.AssessRememberedVoice("Fixture voice") == RememberedVoiceAvailability.Available, "Available selection explicitly reported");
        parsed.Voices = new[] { Voice(), Voice(enabled: false) };
        Check(parsed.AssessRememberedVoice("Fixture voice") == RememberedVoiceAvailability.Ambiguous, "Ambiguous selection is not substituted");
        Check(SpeechCapabilities.Parse(SpeechCapabilities.Serialize(parsed)).Voices.Length == 2, "Duplicate names retained for ambiguity");
        foreach (string malformed in new[]
        {
            "", "[]", "null", "{}", "not JSON", json + "{}", json + "x", json + "\u00a0",
            json.Replace("\"version\":1", "\"version\":2"), json.Replace("\"version\":1", "\"version\":1,\"version\":1"),
            json.Replace("\"version\":1", "\"version\":01"), json.Replace("\"version\":1", "\"version\":1.0"),
            json.Replace("\"version\":1", "\"version\":1e0"), json.Replace("\"version\":1", "\"version\":2147483648"),
            json.Replace("\"version\":1,", ""), json.Replace("\"engine\":\"System.Speech\"", "\"engine\":\"other\""),
            json.Replace("\"schema\":", "\"unknown\":"), json.Replace("\"rateMin\":-10", "\"rateMin\":-11"),
            json.Replace("\"rateMax\":10", "\"rateMax\":11"), json.Replace("\"volumeMin\":0", "\"volumeMin\":-1"),
            json.Replace("\"volumeMax\":100", "\"volumeMax\":101"), json.Replace("\"pitchSupported\":false", "\"pitchSupported\":true"),
            json.Replace("\"enabled\":true", "\"enabled\":1"), json.Replace("\"culture\":\"en-US\"", "\"culture\":\"invalid-culture-xyz\""),
            json.Replace("\"name\":\"Fixture voice\"", "\"name\":\"Fixture voice\",\"name\":\"Fixture voice\""),
            json.Replace("\"name\":\"Fixture voice\"", "\"unknown\":\"Fixture voice\""),
            json.Replace("Fixture voice", "bad\\ud800"), json.Replace("Fixture voice", "bad\\u000a"),
            json.Replace("Fixture voice", "bad\\q"), json.Replace("Fixture voice", "bad\n"),
            json.TrimEnd().TrimEnd('}') + ",}", json.Replace("\"voices\":[", "\"voices\":[null,")
        }) Reject(() => SpeechCapabilities.Parse(SpeechCapabilities.Utf8.GetBytes(malformed)), "Malformed/schema/unknown/duplicate/trailing contract");
        Reject(() => SpeechCapabilities.Parse(new byte[] { 0xff }), "Invalid UTF8");
        Reject(() => SpeechCapabilities.Parse(new byte[] { 0xef, 0xbb, 0xbf }.Concat(SpeechCapabilities.Utf8.GetBytes(json)).ToArray()), "UTF8 BOM is not JSON whitespace");
        Reject(() => SpeechCapabilities.Parse(new byte[SpeechCapabilities.MaxBytes + 1]), "Input byte bound");
        var unicode = new SpeechCapabilityReport { Voices = new[] { Voice("Voix é 日本 \" \\ 😀", "fr-FR") } };
        Check(SpeechCapabilities.Parse(SpeechCapabilities.Serialize(unicode)).Voices[0].Name == unicode.Voices[0].Name, "Unicode/escaped identifiers preserved internally");
        var max = new SpeechCapabilityReport { Voices = Enumerable.Range(0, SpeechCapabilities.MaxVoices).Select(i => Voice(new string('v', SpeechCapabilities.MaxNameChars))).ToArray() };
        Check(SpeechCapabilities.Parse(SpeechCapabilities.Serialize(max)).Voices.Length == SpeechCapabilities.MaxVoices, "Maximum voice and name capacity");
        max.Voices = max.Voices.Concat(new[] { Voice() }).ToArray();
        Reject(() => SpeechCapabilities.Serialize(max), "Voice count limit");
        string tooMany = json.Replace("\"voices\":[", "\"voices\":[" + string.Join(",", Enumerable.Repeat("{\"name\":\"bounded\",\"culture\":\"en-US\",\"enabled\":true}", 65)) + ",");
        Reject(() => SpeechCapabilities.Parse(SpeechCapabilities.Utf8.GetBytes(tooMany)), "Parser voice count limit");
        foreach (SpeechVoiceCapability invalid in new[] { Voice(""), Voice(new string('x', 257)), Voice(culture: new string('a', 86)), Voice("bad\n"), Voice(culture: "en-us"), Voice("bad\ud800") })
            Reject(() => SpeechCapabilities.Serialize(new SpeechCapabilityReport { Voices = new[] { invalid } }), "Voice metadata bounds");
        Check(SpeechCapabilities.Serialize(new SpeechCapabilityReport { Voices = Enumerable.Repeat(Voice(new string('日', 256)), 64).ToArray() }).Length <= SpeechCapabilities.MaxBytes, "Maximum UTF8 metadata fits the output byte bound");
        Reject(() => SpeechCapabilities.Serialize(new SpeechCapabilityReport { DefaultVoice = Voice("missing") }), "Default must be installed");
        Reject(() => SpeechCapabilities.Serialize(new SpeechCapabilityReport { Voices = new[] { Voice(enabled: false) }, DefaultVoice = Voice() }), "Default must be enabled");
        Check(SpeechCapabilities.Parse(SpeechCapabilities.Serialize(new SpeechCapabilityReport())).Voices.Length == 0, "Explicit empty availability supported");
        byte[] padded = SpeechCapabilities.Utf8.GetBytes(json.PadRight(SpeechCapabilities.MaxBytes));
        Check(SpeechCapabilities.Parse(padded).Version == 1, "Exact input byte bound");
    }

    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null) yield break;
        var codes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public).Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(c => unchecked((ushort)c.Value));
        for (int p = 0; p < il.Length;)
        {
            ushort code = il[p++]; if (code == 0xfe) code = (ushort)(0xfe00 | il[p++]);
            OpCode op = codes[code];
            if (op.OperandType == OperandType.InlineMethod)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(il, p));
            int size;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(il, p); break;
                default: size = 4; break;
            }
            p += size;
        }
    }

    private static void NoSpeechImplementation()
    {
        Assembly reader = typeof(Program).Assembly;
        Type discovery = reader.GetType("ReadAlongTrial.SpeechCapabilityDiscovery", true)!;
        Type[] types = new[] { discovery }.Concat(discovery.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)).ToArray();
        var calls = types.SelectMany(t => t.GetMethods(Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)).SelectMany(Calls).ToArray();
        string[] allowed = { ".ctor", "GetInstalledVoices", "get_Voice", "get_VoiceInfo", "get_Name", "get_Culture", "get_Enabled" };
        foreach (MethodBase call in calls.Where(m => m.DeclaringType!.Assembly.GetName().Name == "System.Speech"))
            Check(allowed.Contains(call.Name), "Discovery calls only read-only speech APIs; no Speak/select/output/rate/volume mutation");
        Check(calls.Any(m => m.DeclaringType!.FullName == "System.IDisposable" && m.Name == "Dispose"), "Owned speech instance is disposed");
        Check(!calls.Any(m => m.DeclaringType!.FullName!.StartsWith("System.Windows.") || m.DeclaringType == typeof(File)), "Discovery has no UI or preference IO");
        MethodBase[] main = Calls(typeof(Program).GetMethod("Main", Static)!).ToArray();
        Check(Array.FindIndex(main, m => m.DeclaringType == discovery && m.Name == "Run") < Array.FindIndex(main, m => m.DeclaringType == typeof(ReaderWindow)), "Headless route precedes ReaderWindow construction");
        Check(typeof(System.Speech.Synthesis.SpeechSynthesizer).GetProperty("Pitch") == null, "No direct pitch property claimed");
    }

    private static async Task Processes(string root)
    {
        string harness = Assembly.GetExecutingAssembly().Location;
        var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        int launched = 0;
        Expect<OperationCanceledException>(() => SpeechCapabilityQuery.QueryOwnedAsync(new ProcessStartInfo(harness), TimeSpan.FromSeconds(1), cancelled.Token, _ => launched++).GetAwaiter().GetResult(), "Cancellation before startup");
        Check(launched == 0, "Cancellation before startup creates no child");
        Expect<OperationCanceledException>(() => SpeechCapabilityQuery.QueryAsync(harness, TimeSpan.FromSeconds(1), cancelled.Token).GetAwaiter().GetResult(), "Production query pre-start cancellation");
        Expect<ArgumentException>(() => SpeechCapabilityQuery.QueryAsync("relative.exe", TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(), "Canonical executable required");
        Expect<ArgumentOutOfRangeException>(() => SpeechCapabilityQuery.QueryAsync(Path.Combine(root, "missing.exe"), TimeSpan.Zero).GetAwaiter().GetResult(), "Zero deadline refused");
        Expect<ArgumentOutOfRangeException>(() => SpeechCapabilityQuery.QueryAsync(Path.Combine(root, "missing.exe"), TimeSpan.FromSeconds(31)).GetAwaiter().GetResult(), "Deadline maximum enforced");
        Expect<System.ComponentModel.Win32Exception>(() => SpeechCapabilityQuery.QueryAsync(Path.Combine(root, "missing.exe"), TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(), "Launch failure remains explicit");
        foreach (string mode in new[] { "good", "backpressure", "stdout-overflow", "stderr-overflow", "utf8", "stderr-utf8", "empty", "trailing", "diagnostic", "error", "timeout", "closed-streams", "cancel" })
        {
            Process? owned = null;
            using var cancellation = new CancellationTokenSource();
            var watch = Stopwatch.StartNew();
            Exception? failure = null;
            SpeechCapabilityReport? report = null;
            try
            {
                report = await SpeechCapabilityQuery.QueryOwnedAsync(new ProcessStartInfo(harness, "--speech-capability-child " + mode + " \"" + root + "\""),
                    TimeSpan.FromMilliseconds(mode == "timeout" || mode == "closed-streams" ? 350 : 5000), cancellation.Token, pid =>
                    {
                        owned = Process.GetProcessById(pid);
                        if (mode == "cancel") cancellation.CancelAfter(200);
                    });
            }
            catch (Exception ex) { failure = ex; }
            try
            {
                Check(owned != null && owned.HasExited, "Exact owned fake child exited: " + mode);
                Check(watch.ElapsedMilliseconds < 7500, "Bounded query and cleanup: " + mode);
                if (mode == "good" || mode == "backpressure") Check(failure == null && report!.Voices[0].Name == "Fixture voice", "Explicit JSON success: " + mode);
                else if (mode == "cancel") Check(failure is OperationCanceledException && watch.ElapsedMilliseconds < 2600, "Cancellation after startup cleans child");
                else if (mode == "timeout" || mode == "closed-streams") Check(failure is TimeoutException && watch.ElapsedMilliseconds >= 300 && watch.ElapsedMilliseconds < 2600, "Deadline includes exit and stream completion");
                else if (mode == "error") Check(failure is InvalidOperationException && failure.Message.Contains("7") && failure.Message.Contains("fixture failure"), "Meaningful nonzero stderr retained");
                else Check(failure is InvalidDataException, "Strict output/overflow failure: " + mode);
            }
            finally { owned?.Dispose(); }
        }
        cancelled.Dispose();
    }

    private static async Task<byte[]> RealExternal()
    {
        string reader = Path.Combine(AppContext.BaseDirectory, "VsCopilotChatVoice.Reader.exe");
        SpeechCapabilityReport report = await SpeechCapabilityQuery.QueryAsync(reader, TimeSpan.FromSeconds(10));
        Check(report.Voices.Length > 0 && report.DefaultVoice != null, "Real external accessible/default voices discovered");
        byte[] bytes = SpeechCapabilities.Serialize(report);
        SpeechCapabilityReport roundtrip = SpeechCapabilities.Parse(bytes);
        for (int i = 0; i < report.Voices.Length; i++)
            Check(roundtrip.Voices[i].Name == report.Voices[i].Name && roundtrip.Voices[i].Culture == report.Voices[i].Culture && roundtrip.Voices[i].Enabled == report.Voices[i].Enabled, "Actual identifiers/cultures roundtrip internally");
        Check(roundtrip.DefaultVoice!.Name == report.DefaultVoice!.Name && roundtrip.DefaultVoice.Culture == report.DefaultVoice.Culture, "Actual default roundtrip internally");
        Check(report.AssessRememberedVoice("missing fixture selection") == RememberedVoiceAvailability.Missing, "Real report does not silently replace missing selection");
        Process? owned = null;
        await SpeechCapabilityQuery.QueryOwnedAsync(new ProcessStartInfo(reader, "--speech-capabilities"), TimeSpan.FromSeconds(10), CancellationToken.None, pid =>
        { owned = Process.GetProcessById(pid); Check(owned.MainWindowHandle == IntPtr.Zero, "Real report child is headless"); });
        try { Check(owned != null && owned.HasExited, "Real owned child cleanup"); } finally { owned?.Dispose(); }
        Process? invalid = null;
        try
        {
            await SpeechCapabilityQuery.QueryOwnedAsync(new ProcessStartInfo(reader, "--speech-capabilities --silent"), TimeSpan.FromSeconds(5), CancellationToken.None, pid => invalid = Process.GetProcessById(pid));
            throw new InvalidOperationException("Capability mode accepted extra CLI arguments");
        }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains("exited 2"), "Capability CLI refuses extra args without constructing UI"); }
        finally { if (invalid != null) { Check(invalid.HasExited, "Invalid CLI child cleanup"); invalid.Dispose(); } }
        return bytes;
    }

    private static void Dll(string path, byte[] real)
    {
        Assembly dll = Assembly.LoadFrom(path);
        Check(!dll.GetReferencedAssemblies().Any(a => a.Name == "System.Speech" || a.Name == "VsCopilotChatVoice.Reader"), "VSIX has no speech or Reader reference");
        Check(dll.GetType("ReadAlongTrial.SpeechCapabilityDiscovery") == null, "Speech-bearing collector not compile-linked into VSIX");
        Type contract = dll.GetType("VsCopilotChatVoice.Speech.SpeechCapabilities", true)!;
        MethodInfo parse = contract.GetMethod("Parse", Static)!;
        object report = parse.Invoke(null, new object[] { real })!;
        byte[] serialized = (byte[])contract.GetMethod("Serialize", Static)!.Invoke(null, new[] { report })!;
        Check(serialized.SequenceEqual(real), "Exact DLL complete capability contract parity with actual identifiers withheld");
        Type query = dll.GetType("VsCopilotChatVoice.Speech.SpeechCapabilityQuery", true)!;
        object task = query.GetMethod("QueryAsync", Static)!.Invoke(null, new object[] { Path.Combine(AppContext.BaseDirectory, "VsCopilotChatVoice.Reader.exe"), TimeSpan.FromSeconds(10), CancellationToken.None })!;
        ((Task)task).GetAwaiter().GetResult();
        object external = task.GetType().GetProperty("Result")!.GetValue(task)!;
        Check(SpeechCapabilities.Parse((byte[])contract.GetMethod("Serialize", Static)!.Invoke(null, new[] { external })!).Voices.Length > 0, "Exact DLL owned external query works without speech dependency");
        try { parse.Invoke(null, new object[] { SpeechCapabilities.Utf8.GetBytes(Json() + "{}") }); throw new InvalidOperationException("DLL accepted trailing JSON"); }
        catch (TargetInvocationException ex) { Check(ex.InnerException is InvalidDataException, "Exact DLL strict parser rejection"); }
    }

    private static void Starvation(string root, string dll)
    {
        Process? owned = null;
        try
        {
            SpeechCapabilityQuery.QueryOwnedAsync(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                "--speech-capability-starvation-child \"" + root + "\" \"" + dll + "\""), TimeSpan.FromSeconds(15),
                CancellationToken.None, pid => owned = Process.GetProcessById(pid)).GetAwaiter().GetResult();
            Check(owned != null && owned.HasExited, "Exact isolated starvation child exited");
            string evidence = File.ReadAllText(Path.Combine(root, "starvation-evidence.txt"));
            Check(evidence.Length < 2048 && evidence.Contains("restored=True") && evidence.Contains("expired_starts=0"), "Isolated pool restored and expired launch suppressed");
            string count = evidence.Split(';').Single(v => v.StartsWith("assertions=", StringComparison.Ordinal));
            assertions += int.Parse(count.Substring("assertions=".Length), System.Globalization.CultureInfo.InvariantCulture);
            Console.WriteLine("STARVATION: " + evidence);
        }
        finally { owned?.Dispose(); }
    }

    internal static int StarvationChild(string[] args)
    {
        if (args.Length != 3 || !IsIsolatedRoot(args[1])) return 2;
        int oldMin, oldMinIo, oldMax, oldMaxIo;
        ThreadPool.GetMinThreads(out oldMin, out oldMinIo);
        ThreadPool.GetMaxThreads(out oldMax, out oldMaxIo);
        using var entered = new ManualResetEvent(false);
        using var release = new ManualResetEvent(false);
        using var finished = new ManualResetEvent(false);
        using var queued = new ManualResetEvent(false);
        using var timerFired = new ManualResetEvent(false);
        Timer? timer = null;
        Process? timedChild = null, cancelledChild = null;
        int expiredStarts = 0, cancelledStarts = 0;
        bool restored = false;
        long missingMs = -1, timeoutMs = -1, cancelMs = -1, expiredMs = -1;
        Exception? failure = null;
        var held = Stopwatch.StartNew();
        try
        {
            Assembly dll = Assembly.LoadFrom(Path.GetFullPath(args[2]));
            Type query = dll.GetType("VsCopilotChatVoice.Speech.SpeechCapabilityQuery", true)!;
            Task Invoke(string method, params object[] values)
            {
                try { return (Task)query.GetMethod(method, Static)!.Invoke(null, values)!; }
                catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
            }
            Exception? Outcome(Task task, int milliseconds)
            {
                // Task.WaitAny can itself queue a completion action on the deliberately starved pool.
                var wait = Stopwatch.StartNew();
                while (!task.IsCompleted && wait.ElapsedMilliseconds < milliseconds) Thread.Sleep(1);
                Check(task.IsCompleted, "Actual DLL task completes without worker-pool scheduling");
                try { task.GetAwaiter().GetResult(); return null; } catch (Exception ex) { return ex; }
            }
            Check(ThreadPool.SetMinThreads(1, oldMinIo) && ThreadPool.SetMaxThreads(1, oldMaxIo), "Isolated sole-worker pool configured");
            ThreadPool.QueueUserWorkItem(_ => { entered.Set(); try { release.WaitOne(10000); } finally { finished.Set(); } });
            Check(entered.WaitOne(2000), "Sole worker is occupied");
            held.Restart();
            ThreadPool.QueueUserWorkItem(_ => queued.Set());
            timer = new Timer(_ => timerFired.Set(), null, 50, Timeout.Infinite);
            Thread.Sleep(150);
            Check(!queued.WaitOne(0) && !timerFired.WaitOne(0) && !finished.WaitOne(0), "Queued callbacks and timer callbacks are genuinely starved");

            var watch = Stopwatch.StartNew();
            Task missing = Invoke("QueryAsync", Path.Combine(args[1], "missing.exe"), TimeSpan.FromMilliseconds(100), CancellationToken.None);
            Exception? missingError = Outcome(missing, 750);
            missingMs = watch.ElapsedMilliseconds;
            Check(missingError is System.ComponentModel.Win32Exception && missingMs < 750, "Missing-path startup failure occurs promptly before expiry, not after queued delay");

            string harness = Assembly.GetExecutingAssembly().Location;
            var fake = new ProcessStartInfo(harness, "--speech-capability-child timeout \"" + args[1] + "\"");
            watch.Restart();
            Task timed = Invoke("QueryOwnedAsync", fake, TimeSpan.FromMilliseconds(100), CancellationToken.None,
                (Action<int>)(pid => timedChild = Process.GetProcessById(pid)));
            Exception? timeoutError = Outcome(timed, 2600);
            timeoutMs = watch.ElapsedMilliseconds;
            Check(timeoutError is TimeoutException && timedChild != null && timedChild.HasExited, "Starved-pool deadline kills exact owned child");

            using var cancellation = new CancellationTokenSource();
            watch.Restart();
            Task cancelled = Invoke("QueryOwnedAsync", new ProcessStartInfo(harness, "--speech-capability-child cancel \"" + args[1] + "\""),
                TimeSpan.FromSeconds(5), cancellation.Token, (Action<int>)(pid => { cancelledStarts++; cancelledChild = Process.GetProcessById(pid); }));
            Thread.Sleep(100); cancellation.Cancel();
            Exception? cancelError = Outcome(cancelled, 2500);
            cancelMs = watch.ElapsedMilliseconds;
            Check(cancelError is OperationCanceledException && cancelled.IsCanceled && (cancelledChild == null || cancelledChild.HasExited), "Starved-pool cancellation completes with exact owned cleanup");

            watch.Restart();
            Task expired = Invoke("QueryOwnedAsync", new ProcessStartInfo(harness, "--speech-capability-child good \"" + args[1] + "\""),
                TimeSpan.FromTicks(1), CancellationToken.None, (Action<int>)(_ => expiredStarts++));
            Exception? expiredError = Outcome(expired, 750);
            expiredMs = watch.ElapsedMilliseconds;
            Check(expiredError is TimeoutException && expiredStarts == 0, "Entry-expired deadline creates no child");
            Expect<OperationCanceledException>(() => Invoke("QueryOwnedAsync", new ProcessStartInfo(harness), TimeSpan.FromSeconds(1),
                cancellation.Token, (Action<int>)(_ => expiredStarts++)), "Already cancelled work creates no queued launch");
            Check(!queued.WaitOne(0) && !timerFired.WaitOne(0) && !finished.WaitOne(0), "All lifetime results precede release of sole worker and timer callbacks");
            int remaining = Math.Max(0, 3500 - (int)held.ElapsedMilliseconds);
            Thread.Sleep(remaining);
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            release.Set();
            restored = ThreadPool.SetMaxThreads(oldMax, oldMaxIo) && ThreadPool.SetMinThreads(oldMin, oldMinIo);
            if (!finished.WaitOne(2000)) failure = new InvalidOperationException("Isolated blocked worker did not finish.", failure);
            if (!queued.WaitOne(2000)) failure = new InvalidOperationException("Isolated late queued callback did not finish.", failure);
            // Timer disposal with a completion handle fences callbacks before their event is disposed.
            if (timer != null)
            {
                using var retired = new ManualResetEvent(false);
                if (!timer.Dispose(retired) || !retired.WaitOne(2000)) failure = new InvalidOperationException("Isolated timer callback did not retire.", failure);
            }
            timedChild?.Dispose(); cancelledChild?.Dispose();
        }
        if (failure != null) { Console.Error.WriteLine("Starvation fixture failed: " + failure); return 1; }
        try
        {
            Check(restored && expiredStarts == 0 && cancelledStarts <= 1, "Restored pool/late callback cannot launch expired or cancelled work");
            File.WriteAllText(Path.Combine(args[1], "starvation-evidence.txt"), "missing_ms=" + missingMs + ";timeout_ms=" + timeoutMs +
                ";cancel_ms=" + cancelMs + ";expired_ms=" + expiredMs + ";held_ms=" + held.ElapsedMilliseconds +
                ";expired_starts=" + expiredStarts + ";cancelled_starts=" + cancelledStarts + ";restored=" + restored + ";assertions=" + assertions);
            byte[] report = SpeechCapabilities.Serialize(Fixture());
            using Stream output = Console.OpenStandardOutput(); output.Write(report, 0, report.Length);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("Starvation fixture failed: " + ex); return 1; }
    }

    private static bool IsIsolatedRoot(string root) => Path.IsPathRooted(root) && Path.GetFullPath(root) == root &&
        Path.GetDirectoryName(root) == Environment.CurrentDirectory && Path.GetFileName(root).StartsWith(".speech-capability-tests-", StringComparison.Ordinal) &&
        File.Exists(Path.Combine(root, ".isolated")) && File.ReadAllText(Path.Combine(root, ".isolated")) == "speech-capability-isolated";

    internal static int Child(string[] args)
    {
        if (args.Length != 3) return 2;
        string root = args[2];
        if (!IsIsolatedRoot(root)) return 2;
        using Stream output = Console.OpenStandardOutput();
        using Stream error = Console.OpenStandardError();
        byte[] valid = SpeechCapabilities.Utf8.GetBytes(Json());
        switch (args[1])
        {
            case "good": output.Write(valid, 0, valid.Length); return 0;
            case "backpressure":
                byte[] padded = SpeechCapabilities.Utf8.GetBytes(Json().PadRight(SpeechCapabilities.MaxBytes));
                byte[] whitespace = Encoding.ASCII.GetBytes(new string(' ', SpeechCapabilityQuery.MaxErrorBytes));
                Task.WaitAll(Task.Run(() => output.Write(padded, 0, padded.Length)), Task.Run(() => error.Write(whitespace, 0, whitespace.Length))); return 0;
            case "stdout-overflow": output.Write(new byte[SpeechCapabilities.MaxBytes + 1], 0, SpeechCapabilities.MaxBytes + 1); Thread.Sleep(30000); return 0;
            case "stderr-overflow": error.Write(new byte[SpeechCapabilityQuery.MaxErrorBytes + 1], 0, SpeechCapabilityQuery.MaxErrorBytes + 1); Thread.Sleep(30000); return 0;
            case "utf8": output.WriteByte(255); return 0;
            case "stderr-utf8": output.Write(valid, 0, valid.Length); error.WriteByte(255); return 0;
            case "empty": return 0;
            case "diagnostic": output.Write(valid, 0, valid.Length); error.WriteByte((byte)'x'); return 0;
            case "trailing": output.Write(valid, 0, valid.Length); output.WriteByte((byte)'x'); return 0;
            case "error": byte[] diagnostic = Encoding.UTF8.GetBytes("fixture failure"); error.Write(diagnostic, 0, diagnostic.Length); return 7;
            case "timeout": output.Write(valid, 0, valid.Length); output.Flush(); Thread.Sleep(30000); return 0;
            case "closed-streams": output.Dispose(); error.Dispose(); Thread.Sleep(30000); return 0;
            case "cancel": Thread.Sleep(30000); return 0;
            default: return 2;
        }
    }
}
