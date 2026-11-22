#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VsCopilotChatVoice.Settings;

internal static class SettingsRegressionTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int assertions;

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Refused<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { assertions++; return; }
        throw new InvalidOperationException(message);
    }

    private static object? Invoke(MethodBase method, object? target, params object?[] args)
    {
        try { return method is ConstructorInfo ctor ? ctor.Invoke(args) : ((MethodInfo)method).Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static T Property<T>(object target, string name) =>
        (T)(target.GetType().GetProperty(name, Members) ?? throw new MissingMemberException(target.GetType().FullName, name)).GetValue(target)!;

    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "VsCopilotSettings-" + Guid.NewGuid().ToString("N"));
        internal string Legacy => Path.Combine(Root, "legacy-reader.txt");
        internal string Global => Path.Combine(Root, "global.xml");
        internal Fixture() { Directory.CreateDirectory(Root); File.WriteAllText(Legacy, "Concise", new UTF8Encoding(false)); }
        public void Dispose() { ValidateRoot(Root); Directory.Delete(Root, true); }
    }

    private sealed class Context
    {
        private readonly Assembly assembly;
        private readonly Type identity, values, effective, patch, field, store;
        internal readonly string? Dll;
        internal Context(string? dll)
        {
            Dll = dll;
            assembly = dll == null ? typeof(ScopedSettingsStore).Assembly : Assembly.LoadFrom(Path.GetFullPath(dll));
            identity = Type("SettingsIdentity"); values = Type("VoiceOverrides"); effective = Type("EffectiveVoiceSettings");
            patch = Type("VoiceSettingsPatch"); field = Type("VoiceSetting"); store = Type("ScopedSettingsStore");
        }
        private Type Type(string name) => assembly.GetType("VsCopilotChatVoice.Settings." + name, true)!;
        private static object Construct(Type type, params object?[] args) =>
            Invoke(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(), null, args) ?? throw new InvalidOperationException("Missing settings instance.");
        internal string Key(string path) => (string)Invoke(identity.GetMethod("SolutionKey", Members)!, null, path)!;
        internal object Values(bool? speaker = null, bool? concise = null, string? voice = null, int? rate = null, int? volume = null,
            int? audioPolicy = null, int? duckPercent = null, int? theme = null, int? fontSize = null) =>
            Construct(values, speaker, concise, voice, rate, volume, audioPolicy, duckPercent, theme, fontSize);
        internal object Effective(object global, object solution, object window) => Construct(effective, global, solution, window);
        internal object Patch(object desired, params string[] names)
        {
            Array fields = Array.CreateInstance(field, names.Length);
            for (int i = 0; i < names.Length; i++) fields.SetValue(Enum.Parse(field, names[i]), i);
            return Construct(patch, desired, fields);
        }
        internal object Store(string root, string legacy) => Construct(store, root, legacy);
        internal object Call(object target, string name, params object?[] args) =>
            Invoke(target.GetType().GetMethod(name, Members) ?? throw new MissingMethodException(name), target, args) ??
            throw new InvalidOperationException("Missing settings result.");
        internal static T Value<T>(object snapshot, string name) => Property<T>(Property<object>(snapshot, "Values"), name);
        internal static T EffectiveValue<T>(object settings, string name) => Property<T>(Property<object>(settings, name), "Value");
        internal static string Origin(object settings, string name) => Property<object>(Property<object>(settings, name), "Origin").ToString()!;
        internal void Dependencies()
        {
            if (Dll != null) Check(!assembly.GetReferencedAssemblies().Any(a => a.Name == "System.Speech" || a.Name == "VsCopilotChatVoice.Reader"), "Settings pulled speech/Reader into VSIX.");
        }
    }

    private static void VerifyIdentity(Context c)
    {
        string key = c.Key(@"C:\Examples\Agent\Workspace.slnx");
        Check(key.Length == 64 && key.All(ch => (ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'F')), "Invalid solution key shape.");
        foreach (string alias in new[] { @"c:\examples\agent\WORKSPACE.SLNX", @"C:\Examples\Agent\Child\..\Workspace.slnx", @"C:\Examples\Agent\.\Workspace.slnx", @"\\?\C:\Examples\Agent\Workspace.slnx" })
            Check(c.Key(alias) == key, "Solution alias changed its scope.");
        Check(c.Key(@"C:\Examples\Other\Workspace.slnx") != key, "Different solutions share settings.");
        Check(c.Key(@"C:\Examples\Agent\Workspace.sln") != key, "Distinct solution files share settings.");
        string unc = c.Key(@"\\Server\Share\Agent\Workspace.slnx");
        Check(unc == c.Key(@"\\server\share\agent\WORKSPACE.SLNX"), "UNC case alias changed scope.");
        Check(unc == c.Key(@"\\?\UNC\Server\Share\Agent\Workspace.slnx"), "Extended UNC alias changed scope.");
        Check(unc != key, "UNC and drive paths share scope.");
        foreach (string invalid in new[] { "", "   ", @"Workspace.slnx", @"C:Workspace.slnx", @"\Examples\Workspace.slnx", @"C:\Examples\settings.json", @"\\.\pipe\Workspace.slnx", @"\\Server\Workspace.slnx", @"\\?\UNC\Server\Workspace.slnx", "C:\\Examples\\bad\0Workspace.slnx" })
            Refused<ArgumentException>(() => c.Key(invalid), "Invalid solution identity accepted.");
    }

    private static void VerifyValues(Context c)
    {
        object empty = c.Values(), global = c.Values(true, true, "Voice-A", 10, 90), solution = c.Values(false, false, "Voice-B", -10, 0);
        object baseline = c.Effective(empty, empty, empty);
        Check(!Context.EffectiveValue<bool>(baseline, "Concise") && Context.Origin(baseline, "Concise") == "BuiltIn", "Detailed default changed.");
        Check(!Context.EffectiveValue<bool>(baseline, "SpeakerEnabled"), "Defaults activate Speaker.");
        Check(Context.EffectiveValue<string>(baseline, "VoiceName") == "", "Default voice is not engine-selected.");
        Check(Context.EffectiveValue<int>(baseline, "Rate") == 0 && Context.EffectiveValue<int>(baseline, "Volume") == 100, "Speech defaults changed.");
        object first = c.Effective(global, solution, c.Values(true, true, "Voice-C", 0, 100));
        object second = c.Effective(global, solution, empty);
        foreach (string name in new[] { "SpeakerEnabled", "Concise", "VoiceName", "Rate", "Volume" }) Check(Context.Origin(first, name) == "Window", "Window inheritance origin lost.");
        Check(Context.EffectiveValue<int>(first, "Rate") == 0 && Context.EffectiveValue<int>(second, "Volume") == 0, "Explicit zero treated as inheritance.");
        Check(!Context.EffectiveValue<bool>(second, "Concise") && !Context.EffectiveValue<bool>(second, "SpeakerEnabled"), "Explicit false treated as inheritance.");
        Check(Context.EffectiveValue<string>(first, "VoiceName") == "Voice-C" && Context.EffectiveValue<string>(second, "VoiceName") == "Voice-B", "Same-solution window voices are not isolated.");
        object reset = c.Effective(global, solution, c.Values(voice: ""));
        Check(Context.EffectiveValue<string>(reset, "VoiceName") == "" && Context.Origin(reset, "VoiceName") == "Window", "Engine-default reset lost its origin.");
        object inherited = c.Effective(global, empty, empty);
        Check(Context.EffectiveValue<string>(inherited, "VoiceName") == "Voice-A" && Context.Origin(inherited, "VoiceName") == "Global", "Global voice did not inherit.");
        object partial = c.Effective(global, c.Values(volume: 50), empty);
        Check(Context.EffectiveValue<int>(partial, "Volume") == 50 && Context.EffectiveValue<int>(partial, "Rate") == 10, "Partial overrides discard inherited values.");
        object extendedGlobal = c.Values(audioPolicy: 2, duckPercent: 40, theme: 2, fontSize: 20);
        object extendedSolution = c.Values(duckPercent: 60);
        object extended = c.Effective(extendedGlobal, extendedSolution, empty);
        Check(Context.EffectiveValue<int>(extended, "AudioPolicy") == 2 && Context.Origin(extended, "AudioPolicy") == "Global", "Audio policy did not inherit from global scope.");
        Check(Context.EffectiveValue<int>(extended, "DuckPercent") == 60 && Context.Origin(extended, "DuckPercent") == "Solution", "Duck percentage did not override its inherited value.");
        Check(Context.EffectiveValue<int>(extended, "Theme") == 2 && Context.EffectiveValue<int>(extended, "FontSize") == 20, "Appearance values did not inherit.");
        foreach (int invalid in new[] { -11, 11 }) Refused<ArgumentException>(() => c.Values(rate: invalid), "Invalid rate accepted.");
        foreach (int invalid in new[] { -1, 101 }) Refused<ArgumentException>(() => c.Values(duckPercent: invalid), "Invalid duck percentage accepted.");
        foreach (int invalid in new[] { -1, 4 }) Refused<ArgumentException>(() => c.Values(theme: invalid), "Invalid theme accepted.");
        foreach (int invalid in new[] { 9, 29 }) Refused<ArgumentException>(() => c.Values(fontSize: invalid), "Invalid font size accepted.");
        foreach (int invalid in new[] { -1, 101 }) Refused<ArgumentException>(() => c.Values(volume: invalid), "Invalid volume accepted.");
        foreach (string invalid in new[] { " Voice-A", "Voice-A ", "Voice\nA", new string('X', 257) }) Refused<ArgumentException>(() => c.Values(voice: invalid), "Invalid voice name accepted.");
        Refused<ArgumentException>(() => c.Patch(empty, "Rate", "Rate"), "Duplicate patch field accepted.");
        Refused<ArgumentException>(() => c.Patch(empty, "999"), "Unknown patch field accepted.");
        object updated = c.Call(c.Patch(c.Values(volume: 30), "Volume"), "Apply", global);
        Check(Property<int?>(updated, "Volume") == 30 && Property<string>(updated, "VoiceName") == "Voice-A", "Patch overwrites another field.");
        Check(Property<int?>(global, "Volume") == 90, "Patch mutates previous snapshot.");
        object cleared = c.Call(c.Patch(empty, "VoiceName"), "Apply", updated);
        Check(Property<string?>(cleared, "VoiceName") == null && Property<int?>(cleared, "Volume") == 30, "Use inherited value clears other fields.");
        c.Dependencies();
    }

    private static void VerifyStore(Context c)
    {
        using var f = new Fixture();
        object store = c.Store(f.Root, f.Legacy);
        object global = c.Call(store, "ReadGlobal");
        Check(Context.Value<bool?>(global, "Concise") == true && Property<long>(global, "Revision") == 1, "Legacy Concise was not imported.");
        Check(File.ReadAllText(f.Legacy) == "Concise", "Migration changed legacy preference.");
        File.WriteAllText(f.Legacy, "Detailed");
        Check(Context.Value<bool?>(c.Call(store, "ReadGlobal"), "Concise") == true, "Legacy migration repeated.");
        global = c.Call(store, "UpdateGlobal", c.Patch(c.Values(voice: "Voice-A", rate: 5), "VoiceName", "Rate"));
        Check(Context.Value<bool?>(global, "Concise") == true && Context.Value<int?>(global, "Rate") == 5, "Global patch reset mode.");
        string solution = @"C:\Examples\Agent\Workspace.slnx", other = @"C:\Examples\Other\Workspace.slnx";
        object missing = c.Call(store, "ReadSolution", solution);
        Check(Property<long>(missing, "Revision") == 0 && Context.Value<string?>(missing, "VoiceName") == null, "Missing solution is not inherited.");
        c.Call(store, "UpdateSolution", solution, c.Patch(c.Values(voice: "Voice-B", volume: 40), "VoiceName", "Volume"));
        object loaded = c.Call(store, "Load", @"c:\examples\agent\WORKSPACE.SLNX", c.Values(rate: 0));
        Check(Context.EffectiveValue<string>(loaded, "VoiceName") == "Voice-B" && Context.Origin(loaded, "VoiceName") == "Solution", "Solution scope not restored.");
        Check(Context.EffectiveValue<int>(loaded, "Rate") == 0 && Context.Origin(loaded, "Rate") == "Window", "Window override not applied.");
        Check(Context.EffectiveValue<bool>(loaded, "Concise") && Context.Origin(loaded, "Concise") == "Global", "Global mode did not inherit.");
        Check(Context.EffectiveValue<string>(c.Call(store, "Load", other, c.Values()), "VoiceName") == "Voice-A", "Different solution inherited another override.");
        c.Call(store, "UpdateSolution", solution, c.Patch(c.Values(), "VoiceName"));
        Check(Context.EffectiveValue<string>(c.Call(store, "Load", solution, c.Values()), "VoiceName") == "Voice-A", "Removing override did not restore inheritance.");
        Check(Context.Value<int?>(c.Call(store, "ReadSolution", solution), "Volume") == 40, "Clearing voice discarded solution volume.");
        Check(Context.EffectiveValue<int>(c.Call(c.Store(f.Root, f.Legacy), "Load", solution, c.Values()), "Rate") == 5, "Window override persisted after reopening.");
        string good = File.ReadAllText(f.Global);
        string[] bad = { "", "<other/>", "<voiceSettings version='2' revision='1'/>", "<voiceSettings version='1'/>", "<voiceSettings version='1' revision='0'/>", "<voiceSettings version='1' revision='1' unknown='x'/>", "<voiceSettings version='1' revision='1' concise='perhaps'/>", "<voiceSettings version='1' revision='1' rate='11'/>", "<voiceSettings version='1' revision='1' volume='101'/>", "<voiceSettings version='1' revision='1'><extra/></voiceSettings>", "<voiceSettings version='1' revision='1'/><extra/>", "<!DOCTYPE voiceSettings [<!ENTITY external SYSTEM 'file:///nonexistent'>]><voiceSettings version='1' revision='1' voice='&external;'/>", new string('X', 16385) };
        foreach (string invalid in bad)
        {
            File.WriteAllText(f.Global, invalid);
            Refused<InvalidDataException>(() => c.Call(store, "ReadGlobal"), "Invalid settings silently reset.");
            Refused<InvalidDataException>(() => c.Call(store, "UpdateGlobal", c.Patch(c.Values(rate: 1), "Rate")), "Update overwrote corrupt settings.");
            Check(File.ReadAllText(f.Global) == invalid, "Rejected settings were changed.");
        }
        File.WriteAllText(f.Global, good);
        using (var blocked = new FileStream(f.Global, FileMode.Open, FileAccess.Read, FileShare.Read))
            Refused<IOException>(() => c.Call(store, "UpdateGlobal", c.Patch(c.Values(rate: 6), "Rate")), "Uncooperative delete lock not surfaced.");
        Check(File.ReadAllText(f.Global) == good && Directory.GetFiles(f.Root, "*.tmp").Length == 0, "Failed atomic save changed file or left pending data.");
        using (var blocked = new FileStream(f.Global, FileMode.Open, FileAccess.Read, FileShare.None))
            Refused<IOException>(() => c.Call(store, "ReadGlobal"), "Read access failure silently reset settings.");
        using (var held = new FileStream(f.Global + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var elapsed = Stopwatch.StartNew();
            Refused<TimeoutException>(() => c.Call(store, "UpdateGlobal", c.Patch(c.Values(rate: 6), "Rate")), "Competing lock did not time out.");
            Check(elapsed.Elapsed >= TimeSpan.FromSeconds(5) && elapsed.Elapsed < TimeSpan.FromSeconds(8), "Settings lock deadline outside five/eight-second bounds.");
        }
        Check(File.ReadAllText(f.Global) == good, "Timed-out update modified settings.");
        File.Delete(f.Global);
        File.WriteAllText(f.Legacy, "invalid");
        Refused<InvalidDataException>(() => c.Call(store, "ReadGlobal"), "Invalid legacy preference reset during import.");
        Check(!File.Exists(f.Global) && File.ReadAllText(f.Legacy) == "invalid", "Failed import persisted or changed data.");
        File.Delete(f.Legacy);
        Check(Context.Value<bool?>(c.Call(store, "ReadGlobal"), "Concise") == false, "Missing legacy did not default Detailed.");
    }

    private static void VerifyWatch(Context c)
    {
        using var f = new Fixture();
        object store = c.Store(f.Root, f.Legacy);
        c.Call(store, "ReadGlobal");
        using var global = new ManualResetEvent(false);
        using var solution = new ManualResetEvent(false);
        var errors = new ConcurrentQueue<Exception>();
        object monitor = c.Call(store, "Watch", @"C:\Examples\Workspace.slnx", new Action<bool>(isGlobal => { if (isGlobal) global.Set(); else solution.Set(); }), new Action<Exception>(errors.Enqueue));
        using ((IDisposable)monitor)
        {
            c.Call(store, "UpdateGlobal", c.Patch(c.Values(volume: 45), "Volume"));
            Check(global.WaitOne(5000), "Atomic global rename produced no invalidation.");
            c.Call(store, "UpdateSolution", @"C:\Examples\Workspace.slnx", c.Patch(c.Values(rate: 3), "Rate"));
            Check(solution.WaitOne(5000), "Atomic solution rename produced no invalidation.");
            var error = new IOException("Injected watcher failure.");
            Invoke(monitor.GetType().GetMethod("OnError", Members)!, monitor, monitor, new ErrorEventArgs(error));
            Check(errors.Count == 1 && errors.TryPeek(out Exception? received) && ReferenceEquals(received, error), "Watcher error was not surfaced.");
        }
        int count = errors.Count;
        Invoke(monitor.GetType().GetMethod("OnError", Members)!, monitor, monitor, new ErrorEventArgs(new IOException("Late watcher failure.")));
        Check(errors.Count == count, "Disposed monitor published late error.");
        Refused<ArgumentException>(() => c.Call(store, "Watch", null, null, new Action<Exception>(_ => { })), "Missing change observer accepted.");
    }

    private static string Quote(string value)
    {
        if (value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0) throw new ArgumentException("Invalid child argument.");
        return "\"" + value + "\"";
    }

    private static Process StartChild(Context c, Fixture f, string kind, string id)
    {
        var start = new ProcessStartInfo(typeof(SettingsRegressionTests).Assembly.Location,
            "--settings-child " + string.Join(" ", new[] { f.Root, f.Legacy, c.Dll ?? "linked", kind, id }.Select(Quote)))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        return Process.Start(start) ?? throw new InvalidOperationException("Settings child did not start.");
    }

    private static void WaitReady(Fixture f, string id)
    {
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(f.Root, ".ready-" + id)))
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Settings child readiness timed out.");
            Thread.Sleep(10);
        }
    }

    private static void Finished(Process child)
    {
        Check(child.WaitForExit(15000), "Settings child did not exit.");
        string output = child.StandardOutput.ReadToEnd(), error = child.StandardError.ReadToEnd();
        Check(child.ExitCode == 0 && output.Contains("PASS"), "Settings child failed: " + output + error);
    }

    private static void StopOwned(Process child)
    {
        if (!child.HasExited)
        {
            child.Kill();
            if (!child.WaitForExit(5000)) throw new TimeoutException("Owned settings child did not retire.");
        }
    }

    private static void VerifyProcesses(Context c)
    {
        using var f = new Fixture();
        object store = c.Store(f.Root, f.Legacy);
        long revision = Property<long>(c.Call(store, "ReadGlobal"), "Revision");
        string a = Guid.NewGuid().ToString("N"), b = Guid.NewGuid().ToString("N");
        using (Process first = StartChild(c, f, "rate", a))
        using (Process second = StartChild(c, f, "volume", b))
        {
            try { WaitReady(f, a); WaitReady(f, b); File.WriteAllText(Path.Combine(f.Root, ".start"), "start"); Finished(first); Finished(second); }
            finally { StopOwned(first); StopOwned(second); }
        }
        object merged = c.Call(store, "ReadGlobal");
        Check(Context.Value<int?>(merged, "Rate") == 7 && Context.Value<int?>(merged, "Volume") == 37 && Context.Value<bool?>(merged, "Concise") == true, "Real-process updates lost another field or legacy mode.");
        Check(Property<long>(merged, "Revision") == revision + 40, "Real-process serialized revisions lost an update.");
        string owner = Guid.NewGuid().ToString("N");
        using (Process child = StartChild(c, f, "lock", owner))
        {
            try
            {
                WaitReady(f, owner);
                Task<object> pending = Task.Run(() => c.Call(store, "UpdateGlobal", c.Patch(c.Values(rate: 2), "Rate")));
                Check(!pending.Wait(100), "Live settings owner was taken over.");
                File.WriteAllText(Path.Combine(f.Root, ".release-" + owner), "release");
                Finished(child);
                Check(pending.Wait(5000) && Context.Value<int?>(pending.Result, "Rate") == 2, "Update did not proceed after lock release.");
            }
            finally { StopOwned(child); }
        }
        owner = Guid.NewGuid().ToString("N");
        using (Process child = StartChild(c, f, "lock", owner))
        {
            try { WaitReady(f, owner); child.Kill(); Check(child.WaitForExit(5000), "Owned crash fixture did not exit."); }
            finally { StopOwned(child); }
        }
        Check(Context.Value<int?>(c.Call(store, "UpdateGlobal", c.Patch(c.Values(volume: 38), "Volume")), "Volume") == 38, "Crashed file owner left stale ownership.");
        Check(new FileInfo(f.Global + ".lock").Length == 0, "Settings lock persisted owner metadata.");
    }

    private static void ValidateRoot(string root)
    {
        string full = Path.GetFullPath(root), parent = Path.GetDirectoryName(full) ?? "", name = Path.GetFileName(full);
        if (!string.Equals(parent.TrimEnd('\\'), Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith("VsCopilotSettings-", StringComparison.Ordinal) || !Guid.TryParseExact(name.Substring("VsCopilotSettings-".Length), "N", out _))
            throw new ArgumentException("Settings child requires an isolated test directory.");
    }

    internal static int Child(string[] args)
    {
        try
        {
            if (args.Length != 6 || !Guid.TryParseExact(args[5], "N", out _)) throw new ArgumentException("Invalid settings child request.");
            ValidateRoot(args[1]);
            if (!string.Equals(Path.GetFullPath(args[2]), Path.Combine(Path.GetFullPath(args[1]), "legacy-reader.txt"), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Legacy path escaped test directory.");
            var c = new Context(args[3] == "linked" ? null : args[3]);
            object store = c.Store(args[1], args[2]);
            if (args[4] == "lock")
            {
                using var held = new FileStream(Path.Combine(args[1], "global.xml.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                File.WriteAllText(Path.Combine(args[1], ".ready-" + args[5]), "ready");
                var elapsed = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(args[1], ".release-" + args[5])))
                {
                    if (elapsed.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Child lock was not released.");
                    Thread.Sleep(10);
                }
            }
            else
            {
                if (args[4] != "rate" && args[4] != "volume") throw new ArgumentException("Unknown settings child mode.");
                File.WriteAllText(Path.Combine(args[1], ".ready-" + args[5]), "ready");
                var elapsed = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(args[1], ".start")))
                {
                    if (elapsed.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Settings start barrier timed out.");
                    Thread.Sleep(10);
                }
                for (int i = 0; i < 20; i++) c.Call(store, "UpdateGlobal", args[4] == "rate" ? c.Patch(c.Values(rate: 7), "Rate") : c.Patch(c.Values(volume: 37), "Volume"));
            }
            Console.WriteLine("PASS: settings child completed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
    }

    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length < 1 || args.Length > 2) throw new ArgumentException("Use --settings-probe [VSIX-DLL].");
            assertions = 0;
            foreach (string? dll in args.Length == 2 ? new string?[] { null, args[1] } : new string?[] { null })
            {
                var c = new Context(dll);
                VerifyIdentity(c); VerifyValues(c); VerifyStore(c); VerifyWatch(c); VerifyProcesses(c);
            }
            Console.WriteLine("PASS: " + assertions + " scoped-settings assertions; isolated files/processes, no VS/audio or production preferences.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
    }
}