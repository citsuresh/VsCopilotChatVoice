using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FakeBackend = ReaderPlaybackRegressionTests.FakeBackend;
using FakeSession = ReaderPlaybackRegressionTests.FakeSession;
using static ReaderPlaybackRegressionTests;

// Synthetic WPF/frame checks against the actual VSIX view and audited owner. No native VS behavior is inferred.
internal static class NativeReaderRegressionTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly assembly;
    private static Type viewType, ownerType, previewType, frameType;
    private static string directory;
    private static int assertions;
    private const string Detailed = "[{\"id\":\"a\",\"kind\":\"paragraph\",\"text\":\"Alpha beta gamma. Next sentence.\"}," +
        "{\"id\":\"b\",\"kind\":\"step\",\"text\":\"First line. Next line.\",\"number\":2}]";
    private const string Concise = "[{\"id\":\"b\",\"kind\":\"step\",\"text\":\"Brief step.\",\"number\":2}," +
        "{\"id\":\"a\",\"kind\":\"paragraph\",\"text\":\"  Brief alpha.\"}]";

    internal static int Run(string assemblyPath)
    {
        SynchronizationContext original = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        ResolveEventHandler resolve = ResolveSdk;
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        directory = Path.Combine(Environment.CurrentDirectory, ".native-reader-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            if (Application.Current == null) new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
            viewType = assembly.GetType("VsCopilotChatVoice.VisualStudio.NativeReaderView", true);
            ownerType = assembly.GetType("VsCopilotChatVoice.VisualStudio.ReaderPlaybackController", true);
            previewType = assembly.GetType("VsCopilotChatVoice.VisualStudio.NativeReaderPreview", true);
            frameType = assembly.GetType("VsCopilotChatVoice.VisualStudio.ReaderFrameLifecycle", true);
            InitializeSeam(assembly);
            Check(!viewType.IsPublic && !frameType.IsPublic, "Native presentation widened the public API.");
            Check(!assembly.GetReferencedAssemblies().Any(a => a.Name == "System.Speech" || a.Name == "VsCopilotChatVoice.Reader"), "Native UI references synthesis or Reader executable.");
            SelectionRequestsAndControlEscape();
            Accessibility();
            ReentrantVersionSelection();
            ReentrantNotifications();
            Gates();
            Preferences();
            PresentationAndNavigation();
            ThemeAndNarrowLayout();
            VersionSwitchAndErrors();
            PendingStatePresentation();
            KeyboardAndFollow();
            FrameAndLifetime();
            DispatcherAndShutdown();
            SettingsPreviewWaitsForSpeech();
            SettingsUsesThemedControls();
            Console.WriteLine("PASS: actual-DLL native reader synthetic checks, " + assertions + " assertions (native Exp gates unverified)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolve;
            SynchronizationContext.SetSynchronizationContext(original);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    // SDK packages are build prerequisites, not new test dependencies. Resolve only already-provisioned assemblies.
    private static Assembly ResolveSdk(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        if (!name.StartsWith("Microsoft.VisualStudio", StringComparison.Ordinal) && !name.StartsWith("Microsoft.ServiceHub", StringComparison.Ordinal)) return null;
        string package = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", name.ToLowerInvariant());
        if (!Directory.Exists(package)) return null;
        foreach (string version in Directory.GetDirectories(package).OrderByDescending(p => p, StringComparer.Ordinal))
            foreach (string target in new[] { "net472", "net48", "net45", "netstandard2.0", "net20" })
            {
                string path = Path.Combine(version, "lib", target, name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
        return null;
    }
    private static void Check(bool value, string description) { assertions++; if (!value) throw new InvalidOperationException(description); }
    private static object Get(object value, string property) => value.GetType().GetProperty(property, Members).GetValue(value);
    private static T Field<T>(object value, string field) => (T)viewType.GetField(field, Members).GetValue(value);
    private static Task Call(object view, string method, params object[] args) => (Task)viewType.GetMethod(method, Members).Invoke(view, args);
    private static object Owner(object view) => Get(view, "Controller");
    private static string Phase(object view) => Get(Owner(view), "Phase").ToString();
    private static int Segment(object view) { object p = Get(Owner(view), "Position"); return (int)p.GetType().GetField("Segment").GetValue(p); }
    private static int Offset(object view) { object p = Get(Owner(view), "Position"); return (int)p.GetType().GetField("Offset").GetValue(p); }
    private static object Position(int segment, int offset) => Activator.CreateInstance(assembly.GetType("ReadAlongTrial.ReadingPosition", true), segment, offset);
    private static void Void(object view, string method, params object[] args) => viewType.GetMethod(method, Members).Invoke(view, args);
    private static bool HandleKey(object view, Key key, DependencyObject focused, bool handled = false, bool repeat = false, ModifierKeys modifiers = ModifierKeys.None) =>
        (bool)viewType.GetMethod("TryHandleKey", Members).Invoke(view, new object[] { key, focused, handled, repeat, modifiers });
    private static Exception Unwrap(Exception ex) { while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException; return ex; }
    private static void Reject(Action action, Type expected, string description)
    {
        try { action(); } catch (Exception ex) { Check(expected.IsInstanceOfType(Unwrap(ex)), description + ": " + Unwrap(ex)); return; }
        throw new InvalidOperationException(description + " was not refused.");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeBackend Backend;
        internal readonly object View;
        internal readonly ConcurrentBag<Task> Observed = new ConcurrentBag<Task>();
        internal readonly string Path;
        internal int Hides;
        internal Fixture(string choice = null, string concise = Concise, string path = null, FakeBackend backend = null,
            string detailed = Detailed, bool frame = true)
        {
            Backend = backend ?? new FakeBackend();
            Path = path ?? System.IO.Path.Combine(directory, Guid.NewGuid().ToString("N") + ".txt");
            if (choice != null) File.WriteAllText(Path, choice);
            View = Activator.CreateInstance(viewType, Members, null,
                new object[] { Backend.Proxy, Path, detailed, concise, new Action(() => Hides++), new Action<Task>(Observed.Add) }, null);
            Void(View, "SetFrameAvailable", frame, frame ? null : "test frame refusal");
        }
        internal RichTextBox Reader => Field<RichTextBox>(View, "reader");
        internal TextBlock Status => Field<TextBlock>(View, "status");
        internal TextBlock Notice => Field<TextBlock>(View, "preferenceNotice");
        internal Button Play => Field<Button>(View, "play");
        internal FakeSession Session => Backend.Latest;
        internal void Start() => Await(Call(View, "TogglePlaybackAsync"));
        internal void Choose(bool concise) => Await(Call(View, "ChooseVersionAsync", concise));
        internal void Seek(int segment, int offset) => Await(Call(View, "SeekAsync", Position(segment, offset)));
        internal void Click(ButtonBase button)
        {
            if (button is RadioButton choice) NativeRadioClick(choice);
            else button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Settle();
        }
        public void Dispose() { ((IDisposable)View).Dispose(); Settle(); }
    }

    private static AutomationPeer Peer(UIElement element) => UIElementAutomationPeer.CreatePeerForElement(element) ??
        throw new InvalidOperationException("The native WPF control did not create an automation peer.");
    private static ISelectionItemProvider Selection(RadioButton choice) =>
        (ISelectionItemProvider)Peer(choice).GetPattern(PatternInterface.SelectionItem);
    private static void NativeRadioClick(RadioButton choice) => choice.GetType().GetMethod("OnClick", Members).Invoke(choice, null);
    private static void AccessibleText(TextBlock element, string id, string description)
    {
        Check(Peer(element).GetName() == element.Text, description + " accessibility Name is not the displayed text.");
        Check(AutomationProperties.GetAutomationId(element) == id, description + " changed its stable automation ID.");
        Check(Peer(element).GetLiveSetting() == AutomationLiveSetting.Polite, description + " lost polite live-region semantics.");
    }

    private static void SelectionRequestsAndControlEscape()
    {
        var retained = new List<bool>();
        foreach (bool originalConcise in new[] { false, true })
        using (var f = new Fixture(choice: originalConcise ? "Concise" : null))
        {
            f.Start(); FakeSession old = f.Session;
            if (File.Exists(f.Path)) File.Delete(f.Path);
            RadioButton original = Field<RadioButton>(f.View, originalConcise ? "conciseChoice" : "detailedChoice"),
                candidate = Field<RadioButton>(f.View, originalConcise ? "detailedChoice" : "conciseChoice");
            bool entered = false;
            candidate.Checked += (s, e) => { if (entered) return; entered = true; Selection(original).Select(); };
            NativeRadioClick(candidate); Settle();
            string saved = File.Exists(f.Path) ? File.ReadAllText(f.Path) : "<none>";
            bool correct = entered && (bool)Get(Owner(f.View), "IsConcise") == originalConcise &&
                (long)Get(Owner(f.View), "Version") == 1 && Phase(f.View) == "Speaking" && old.Cancels == 0 &&
                saved == (originalConcise ? "Concise" : "Detailed");
            retained.Add(correct);
            if (!correct) Console.Error.WriteLine("REPRO: native " + candidate.Content + " Checked -> actual peer " + original.Content +
                ": version=" + Get(Owner(f.View), "Version") + ", model=" + Get(Owner(f.View), "IsConcise") +
                ", phase=" + Phase(f.View) + ", cancels=" + old.Cancels + ", saved=" + saved + ".");
        }
        bool escapeCorrect = true;
        foreach (string field in new[] { "play", "conciseChoice" })
        using (var f = new Fixture())
        {
            f.Seek(1, 8); f.Start(); FakeSession old = f.Session;
            bool handled = HandleKey(f.View, Key.Escape, Field<Control>(f.View, field)); Settle();
            bool correct = handled && f.Hides == 1 && Phase(f.View) == "Stopped" && Segment(f.View) == 1 && Offset(f.View) == 8 &&
                old.Cancels == 1 && old.Disposals == 1;
            escapeCorrect &= correct;
            if (!correct) Console.Error.WriteLine("REPRO: owned " + field + " Esc handled=" + handled + ", hides=" + f.Hides +
                ", phase=" + Phase(f.View) + ", cancels=" + old.Cancels + ".");
        }
        foreach (bool correct in retained) Check(correct, "Older native Click superseded a newer matching-model peer choice/save.");
        Check(escapeCorrect, "Focused owned button/radio Esc did not stop/hide while retaining position.");
        SelectionRequestEdges();
        ControlEscapeScope();
    }

    private static void SelectionRequestEdges()
    {
        foreach (bool originalConcise in new[] { false, true })
        foreach (int thirdClick in new[] { 0, 1, 2 })
        using (var f = new Fixture(choice: originalConcise ? "Concise" : null))
        {
            f.Start(); FakeSession old = f.Session; int observed = f.Observed.Count;
            RadioButton original = Field<RadioButton>(f.View, originalConcise ? "conciseChoice" : "detailedChoice"),
                candidate = Field<RadioButton>(f.View, originalConcise ? "detailedChoice" : "conciseChoice");
            bool entered = false;
            candidate.Checked += (s, e) =>
            {
                if (entered) return; entered = true;
                Selection(original).Select(); Selection(candidate).Select();
                if (thirdClick != 0) NativeRadioClick(thirdClick == 1 ? original : candidate);
            };
            NativeRadioClick(candidate); Settle();
            bool finalConcise = thirdClick == 1 ? originalConcise : !originalConcise;
            Check(entered && (bool)Get(Owner(f.View), "IsConcise") == finalConcise &&
                (long)Get(Owner(f.View), "Version") == (thirdClick == 1 ? 3 : 2),
                "Original OnToggle suppressed its later genuine peer Checked or overwrote a third nested native Click.");
            Check(File.ReadAllText(f.Path) == (finalConcise ? "Concise" : "Detailed") &&
                original.IsChecked == (thirdClick == 1) && candidate.IsChecked == (thirdClick != 1),
                "Two-peer/third-native sequence disagrees between model, radio and newest saved intent: originalConcise=" + originalConcise +
                ", thirdClick=" + thirdClick + ", model=" + Get(Owner(f.View), "IsConcise") + ", originalChecked=" + original.IsChecked +
                ", candidateChecked=" + candidate.IsChecked + ", saved='" + File.ReadAllText(f.Path) + "', notice='" + f.Notice.Text + "'.");
            Check(f.Observed.Count == observed + (thirdClick == 0 ? 2 : 3),
                "Older native Click double-saved after two genuine peers/third nested native Click.");
            Check(Phase(f.View) == "Paused" && old.Cancels == 1 && old.Disposals == 1 && f.Backend.Launches.Count == 1,
                "Two-peer/third-native sequence duplicated retirement or started speech.");
        }
        foreach (bool originalConcise in new[] { false, true })
        foreach (bool returnToOriginal in new[] { false, true })
        using (var f = new Fixture(choice: originalConcise ? "Concise" : null))
        {
            f.Start(); FakeSession old = f.Session; int observed = f.Observed.Count;
            RadioButton original = Field<RadioButton>(f.View, originalConcise ? "conciseChoice" : "detailedChoice"),
                candidate = Field<RadioButton>(f.View, originalConcise ? "detailedChoice" : "conciseChoice");
            bool entered = false;
            candidate.Checked += (s, e) =>
            {
                if (entered) return; entered = true;
                Selection(original).Select(); Selection(candidate).Select(); Selection(original).Select();
                if (!returnToOriginal) Selection(candidate).Select();
            };
            NativeRadioClick(candidate); Settle();
            bool finalConcise = returnToOriginal ? originalConcise : !originalConcise;
            Check(entered && (bool)Get(Owner(f.View), "IsConcise") == finalConcise &&
                (long)Get(Owner(f.View), "Version") == (returnToOriginal ? 3 : 4),
                "Native OnToggle swallowed a nested same-radio peer round trip or replayed older Click.");
            Check(File.ReadAllText(f.Path) == (finalConcise ? "Concise" : "Detailed") &&
                original.IsChecked == returnToOriginal && candidate.IsChecked == !returnToOriginal,
                "Nested peer round trip lost its newest checked state/preference.");
            Check(f.Observed.Count == observed + (returnToOriginal ? 3 : 4),
                "Own paired Checked or superseded Click duplicated selection/save work after a round trip.");
            Check(Phase(f.View) == "Paused" && old.Cancels == 1 && old.Disposals == 1 && f.Backend.Launches.Count == 1,
                "Peer round trip restarted speech or duplicated worker retirement.");
        }
        foreach (bool originalConcise in new[] { false, true })
        using (var f = new Fixture(choice: originalConcise ? "Concise" : null))
        {
            f.Start(); FakeSession old = f.Session; int observed = f.Observed.Count;
            RadioButton original = Field<RadioButton>(f.View, originalConcise ? "conciseChoice" : "detailedChoice"),
                candidate = Field<RadioButton>(f.View, originalConcise ? "detailedChoice" : "conciseChoice");
            bool entered = false;
            candidate.Checked += (s, e) => { if (entered) return; entered = true; NativeRadioClick(original); };
            NativeRadioClick(candidate); Settle();
            Check(entered && (bool)Get(Owner(f.View), "IsConcise") == originalConcise && (long)Get(Owner(f.View), "Version") == 1,
                "Older Click overwrote a newer native matching-mode Click from Checked.");
            Check(File.ReadAllText(f.Path) == (originalConcise ? "Concise" : "Detailed") && f.Observed.Count == observed + 1,
                "Nested native Click did not save exactly once or its own Checked was duplicated.");
            Check(Phase(f.View) == "Speaking" && old.Cancels == 0 && f.Backend.Launches.Count == 1,
                "Matching-mode nested native Click restarted/retired speech.");
        }
        foreach (bool originalConcise in new[] { false, true })
        using (var f = new Fixture(choice: originalConcise ? "Concise" : null))
        {
            f.Start(); FakeSession old = f.Session; int observed = f.Observed.Count;
            RadioButton original = Field<RadioButton>(f.View, originalConcise ? "conciseChoice" : "detailedChoice"),
                candidate = Field<RadioButton>(f.View, originalConcise ? "detailedChoice" : "conciseChoice");
            candidate.Click += (s, e) => Selection(original).Select();
            NativeRadioClick(candidate); Settle();
            Check((bool)Get(Owner(f.View), "IsConcise") == originalConcise && (long)Get(Owner(f.View), "Version") == 3 &&
                File.ReadAllText(f.Path) == (originalConcise ? "Concise" : "Detailed"),
                "Newer peer request from later native Click was overwritten.");
            Check(f.Observed.Count == observed + 2 && Phase(f.View) == "Paused" && old.Cancels == 1 && old.Disposals == 1 &&
                f.Backend.Launches.Count == 1, "Later Click peer request duplicated operations or restarted speech.");
        }
        foreach (bool originalConcise in new[] { false, true })
        foreach (bool checkedCallback in new[] { false, true })
        using (var f = new Fixture(choice: originalConcise ? "Concise" : null))
        {
            f.Start(); FakeSession old = f.Session; int observed = f.Observed.Count;
            RadioButton original = Field<RadioButton>(f.View, originalConcise ? "conciseChoice" : "detailedChoice"),
                candidate = Field<RadioButton>(f.View, originalConcise ? "detailedChoice" : "conciseChoice");
            bool entered = false;
            (checkedCallback ? candidate : original).AddHandler(checkedCallback ? ToggleButton.CheckedEvent : ToggleButton.UncheckedEvent,
                new RoutedEventHandler((s, e) => { if (entered) return; entered = true; Selection(original).Select(); }));
            Await((Task)ownerType.GetMethod("SwitchVersionAsync", Members).Invoke(Owner(f.View), new object[] { !originalConcise })); Settle();
            Check(entered && (bool)Get(Owner(f.View), "IsConcise") == originalConcise && (long)Get(Owner(f.View), "Version") == 3,
                "Model-write origin suppressed a genuine nested peer from Checked/Unchecked.");
            Check(File.ReadAllText(f.Path) == (originalConcise ? "Concise" : "Detailed") && original.IsChecked == true && candidate.IsChecked == false,
                "Nested peer from model synchronization lost its latest choice/save.");
            Check(f.Observed.Count == observed + 1 && old.Cancels == 1 && old.Disposals == 1 && f.Backend.Launches.Count == 1,
                "Model synchronization or its nested peer duplicated operations/worker cleanup.");
        }
        foreach (bool originalConcise in new[] { false, true })
        using (var f = new Fixture(choice: originalConcise ? "Concise" : "Detailed"))
        {
            f.Start(); Void(f.View, "SetFollowing", false);
            RadioButton original = Field<RadioButton>(f.View, originalConcise ? "conciseChoice" : "detailedChoice");
            using (var hook = new BringIntoViewOnce(Field<Run>(f.View, "highlighted"), () =>
            {
                using (var locked = new FileStream(f.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                { original.IsChecked = false; Selection(original).Select(); }
            }))
            {
                f.Choose(originalConcise); Settle();
                Check(hook.Entered && f.Notice.Text.Contains("Could not save") && f.Notice.Visibility == Visibility.Visible &&
                    Peer(f.Notice).GetName() == f.Notice.Text,
                    "Older same-version save retried/cleared a newer matching-mode peer's expected save failure.");
                Check((long)Get(Owner(f.View), "Version") == 1 && Phase(f.View) == "Speaking" &&
                    File.ReadAllText(f.Path) == (originalConcise ? "Concise" : "Detailed") && f.Observed.All(task => !task.IsFaulted),
                    "Matching-mode peer persistence changed playback/version or swallowed an unexpected error.");
            }
        }
        foreach (bool originalConcise in new[] { false, true })
        using (var f = new Fixture(choice: originalConcise ? "Concise" : null))
        {
            f.Start(); FakeSession old = f.Session;
            if (File.Exists(f.Path)) File.Delete(f.Path);
            RadioButton candidate = Field<RadioButton>(f.View, originalConcise ? "detailedChoice" : "conciseChoice");
            bool entered = false;
            candidate.Checked += (s, e) => { if (entered) return; entered = true; f.Dispose(); };
            NativeRadioClick(candidate); Settle();
            Check(entered && Phase(f.View) == "Disposed" && (bool)Get(Owner(f.View), "IsConcise") == originalConcise && !File.Exists(f.Path),
                "Native Click committed its pending choice after Checked synchronously disposed the view.");
            Check(old.Cancels == 1 && old.Disposals == 1 && f.Backend.Launches.Count == 1 && f.Observed.All(task => !task.IsFaulted),
                "Disposed pending Click duplicated cleanup, launched speech or faulted observed UI work.");
        }
        using (var f = new Fixture())
        {
            Field<RadioButton>(f.View, "conciseChoice").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Settle();
            Check(!(bool)Get(Owner(f.View), "IsConcise") && !File.Exists(f.Path) && f.Observed.Count == 0,
                "Bare synthetic radio Click bypassed actual native OnClick request tagging.");
        }
    }

    private static void ControlEscapeScope()
    {
        int count; using (var f = new Fixture()) count = Field<List<Control>>(f.View, "controls").Count;
        foreach (int index in Enumerable.Range(-3, count + 3))
        using (var f = new Fixture())
        {
            f.Seek(1, 8); f.Start(); FakeSession old = f.Session;
            Action<long, int> lateProgress = old.Progress; Action<long> lateCompletion = old.Completed; long generation = old.Last.Generation;
            DependencyObject focused;
            if (index == -3) { var child = new TextBlock { Text = "Button content focus", Focusable = true }; f.Play.Content = child; focused = child; }
            else focused = index == -2 ? (DependencyObject)f.View : index == -1 ? f.Reader : Field<List<Control>>(f.View, "controls")[index];
            Check(HandleKey(f.View, Key.Escape, focused), "Owned reader surface/text/button/radio Escape was refused."); Settle();
            Check(f.Hides == 1 && Phase(f.View) == "Stopped" && Segment(f.View) == 1 && Offset(f.View) == 8,
                "Owned control Esc did not hide once and retain reply/position.");
            Check(old.Cancels == 1 && old.Disposals == 1 && f.Backend.Launches.Count == 1,
                "Owned control Esc duplicated cleanup or started speech.");
            lateProgress(generation, 12); lateCompletion(generation); Settle();
            Check(f.Hides == 1 && Phase(f.View) == "Stopped" && Offset(f.View) == 8 && f.Backend.Launches.Count == 1,
                "Late progress/completion revived playback after control Esc.");
        }
        using (var f = new Fixture())
        {
            foreach (Control native in Field<List<Control>>(f.View, "controls"))
            {
                Check(new[] { Key.Space, Key.Enter, Key.Left, Key.Right, Key.Up, Key.Down, Key.Home, Key.End, Key.Tab }
                    .All(key => !HandleKey(f.View, key, native)), "Reader stole a non-Esc native button/radio key or Tab.");
                Check(!HandleKey(f.View, Key.Escape, native, handled: true) && !HandleKey(f.View, Key.Escape, native, repeat: true) &&
                    !HandleKey(f.View, Key.Escape, native, modifiers: ModifierKeys.Shift), "Handled/repeated/modified control Esc was invoked.");
            }
            var layout = (DockPanel)((UserControl)f.View).Content;
            foreach (Control native in new Control[] { new ComboBox(), new TextBox(), new ScrollBar(), new Slider(), new Button() })
            {
                layout.Children.Add(native);
                Check(!HandleKey(f.View, Key.Escape, native), "Reader intercepted dropdown/edit/scrollbar/slider/unowned-button Esc.");
            }
            var nestedEdit = new TextBox(); f.Play.Content = nestedEdit;
            Check(!HandleKey(f.View, Key.Escape, nestedEdit), "Owned button containment stole a nested editor's Esc.");
            Check(new DependencyObject[] { new Button(), new RadioButton(), new RichTextBox(), new UserControl() }
                .All(outside => !HandleKey(f.View, Key.Escape, outside)), "Reader intercepted Esc outside its own view.");
            Check(f.Hides == 0 && f.Backend.Launches.Count == 0, "Refused native/outside keys hid the frame or started speech.");
        }
    }

    private static void Accessibility()
    {
        // Execute both original repros before either assertion can terminate the route. No native UIA action
        // or toolkit selection-container inference: this is the actual WPF peer on the actual DLL's view.
        bool statusMatches, selectionApplied;
        using (var f = new Fixture())
        {
            string actualName = Peer(f.Status).GetName();
            statusMatches = actualName == f.Status.Text;
            if (!statusMatches) Console.Error.WriteLine("REPRO: status peer Name='" + actualName + "'; displayed='" + f.Status.Text + "'.");
        }
        using (var f = new Fixture())
        {
            RadioButton choice = Field<RadioButton>(f.View, "conciseChoice");
            Selection(choice).Select(); Settle();
            selectionApplied = choice.IsChecked == true && (bool)Get(Owner(f.View), "IsConcise") &&
                File.Exists(f.Path) && File.ReadAllText(f.Path) == "Concise";
            if (!selectionApplied) Console.Error.WriteLine("REPRO: WPF SelectionItem.Select checked=" + choice.IsChecked +
                ", modelConcise=" + Get(Owner(f.View), "IsConcise") + ", preferenceSaved=" + File.Exists(f.Path) + ".");
        }
        Check(statusMatches, "Status AutomationPeer.GetName hides the actual displayed phase/section.");
        Check(selectionApplied, "WPF SelectionItemProvider.Select did not apply/save the chosen reply version.");

        using (var f = new Fixture(backend: new FakeBackend { HoldLaunch = true, Configure = s => { s.HoldPause = true; s.HoldResume = true; } }))
        {
            AutomationPeer status = Peer(f.Status);
            AccessibleText(f.Status, "NativeReaderStatus", "Stopped status");
            Task start = Call(f.View, "TogglePlaybackAsync");
            Check(status.GetName().Contains("Starting"), "The existing status peer did not reflect Starting.");
            f.Backend.Launches[0].Complete(); Await(start);
            Check(status.GetName() == f.Status.Text && status.GetName().Contains("Speaking"), "The existing status peer did not reflect Speaking.");
            int announcements = 0;
            DependencyPropertyDescriptor name = DependencyPropertyDescriptor.FromProperty(AutomationProperties.NameProperty, typeof(TextBlock));
            EventHandler changed = (s, e) => announcements++;
            name.AddValueChanged(f.Status, changed);
            try { f.Session.ProgressAt(6); Settle(); f.Session.ProgressAt(11); Settle(); }
            finally { name.RemoveValueChanged(f.Status, changed); }
            Check(announcements == 0, "Word-only progress repeatedly changed the live-region Name.");
            Task pause = Call(f.View, "TogglePlaybackAsync");
            Check(status.GetName().Contains("Pausing"), "Pending Pause is hidden from the status peer.");
            f.Session.PauseReady.SetResult(true); Await(pause);
            Check(status.GetName().Contains("Paused") && status.GetName() == f.Status.Text, "Paused status is hidden from the existing peer.");
            Task resume = Call(f.View, "TogglePlaybackAsync");
            Check(status.GetName().Contains("Resuming"), "Pending Resume is hidden from the status peer.");
            f.Session.ResumeReady.SetResult(true); Await(resume);
            f.Session.Finish(); Settle();
            Check(status.GetName().Contains("section 2") && status.GetName() == f.Status.Text, "Section advance did not update the status peer.");
            f.Session.Finish(); Settle();
            Check(status.GetName().Contains("Finished"), "Natural completion is hidden from the status peer.");
        }
        using (var f = new Fixture(choice: "invalid preference"))
        {
            Check(f.Notice.Visibility == Visibility.Visible && f.Notice.Text.Contains("Could not load"), "Load warning fixture is not visible.");
            AccessibleText(f.Notice, "NativeReaderPreferenceNotice", "Load warning");
            f.Choose(true);
            Check(f.Notice.Visibility == Visibility.Collapsed && Peer(f.Notice).GetName() == "",
                "Cleared load warning retains stale accessible text: text='" + f.Notice.Text + "', peer='" + Peer(f.Notice).GetName() +
                "', nameProperty='" + AutomationProperties.GetName(f.Notice) + "', visibility=" + f.Notice.Visibility + ".");
        }
        using (var f = new Fixture(path: directory))
        {
            f.Choose(true);
            Check(f.Notice.Visibility == Visibility.Visible && f.Notice.Text.Contains("Could not save"), "Save warning fixture is not visible.");
            AccessibleText(f.Notice, "NativeReaderPreferenceNotice", "Save warning");
        }
        using (var f = new Fixture(frame: false))
        {
            TextBlock notice = Field<TextBlock>(f.View, "frameNotice");
            Check(notice.Visibility == Visibility.Visible && notice.Text.Contains("unavailable"), "Frame warning fixture is not visible.");
            AccessibleText(notice, "NativeReaderFrameNotice", "Frame warning");
            Void(f.View, "SetFrameAvailable", true, null);
            Check(notice.Visibility == Visibility.Collapsed && Peer(notice).GetName() == "", "Cleared frame warning retains stale accessible text.");
        }
        using (var f = new Fixture(backend: new FakeBackend { HoldLaunch = true }))
        {
            Task start = Call(f.View, "TogglePlaybackAsync");
            f.Backend.Launches[0].Fail(new IOException("Isolated worker refusal.")); Await(start);
            Check(f.Status.Text.Contains("Speech could not start"), "Operational status error was not produced.");
            AccessibleText(f.Status, "NativeReaderStatus", "Operational error");
        }
        using (var f = new Fixture(backend: new FakeBackend { Configure = s => s.BeforeSpeakReturn = (session, speech) =>
            { throw new NullReferenceException("Isolated accessibility error fixture."); } }))
        {
            f.Click(f.Play); PumpUntil(() => f.Observed.Any(t => t.IsFaulted));
            Check(f.Status.Text.Contains("operation failed"), "Unexpected status error was not produced.");
            AccessibleText(f.Status, "NativeReaderStatus", "Unexpected error");
        }
        using (var f = new Fixture())
        {
            RadioButton concise = Field<RadioButton>(f.View, "conciseChoice"), detailed = Field<RadioButton>(f.View, "detailedChoice");
            Check(Peer(concise).GetAutomationControlType() == AutomationControlType.RadioButton, "Version choice no longer exposes the native radio automation role.");
            int observed = f.Observed.Count;
            Selection(concise).Select(); Settle();
            Check((bool)Get(Owner(f.View), "IsConcise") && File.ReadAllText(f.Path) == "Concise" && Phase(f.View) == "Paused",
                "Accessibility selection did not apply/save Concise while paused.");
            Check(f.Observed.Count == observed + 1 && f.Backend.Launches.Count == 0, "Accessibility selection duplicated operations or started speech.");
            observed = f.Observed.Count;
            NativeRadioClick(detailed); Settle();
            Check(!(bool)Get(Owner(f.View), "IsConcise") && File.ReadAllText(f.Path) == "Detailed", "Native radio OnClick did not apply/save Detailed.");
            Check(f.Observed.Count == observed + 1 && (long)Get(Owner(f.View), "Version") == 3, "Checked/Click double-invoked a native version change.");
            Selection(concise).Select(); Settle();
            Check((bool)Get(Owner(f.View), "IsConcise") && (long)Get(Owner(f.View), "Version") == 4 && f.Backend.Launches.Count == 0,
                "Later accessibility selection was suppressed or automatically spoken after Click.");
        }
        using (var f = new Fixture())
        {
            f.Start(); FakeSession old = f.Session;
            Selection(Field<RadioButton>(f.View, "conciseChoice")).Select(); Settle();
            Check(Phase(f.View) == "Paused" && Segment(f.View) == 1 && Offset(f.View) == 2 && File.ReadAllText(f.Path) == "Concise",
                "Accessibility selection while speaking did not save/pause/map the current section.");
            Check(old.Cancels == 1 && old.Disposals == 1 && f.Backend.Launches.Count == 1,
                "Accessibility selection while speaking restarted speech or failed exact worker retirement.");
        }
        using (var f = new Fixture(choice: "Concise", concise: null))
        {
            RadioButton detailed = Field<RadioButton>(f.View, "detailedChoice"); int observed = f.Observed.Count;
            Reject(() => Selection(Field<RadioButton>(f.View, "conciseChoice")).Select(), typeof(ElementNotEnabledException),
                "Unavailable Concise accessibility selection");
            Check(detailed.IsChecked == true && File.ReadAllText(f.Path) == "Concise", "Missing-Concise fallback fixture changed saved intent.");
            NativeRadioClick(detailed); Settle();
            Check(File.ReadAllText(f.Path) == "Detailed" && f.Notice.Visibility == Visibility.Collapsed,
                "Already-selected Detailed native Click did not explicitly save fallback choice: saved=" + File.ReadAllText(f.Path) +
                ", notice=" + f.Notice.Text + ", observed=" + f.Observed.Count + ", faults=" +
                string.Join(";", f.Observed.Where(t => t.IsFaulted).Select(t => t.Exception.ToString())) + ".");
            Check(f.Observed.Count == observed + 1 && f.Backend.Launches.Count == 0, "Already-selected fallback Click was duplicated or spoken.");
        }
    }

    private static void ReentrantVersionSelection()
    {
        foreach (bool click in new[] { false, true })
        using (var f = new Fixture())
        {
            f.Start(); FakeSession old = f.Session;
            RadioButton choice = Field<RadioButton>(f.View, "conciseChoice");
            Action<long, int> lateProgress = old.Progress; Action<long> lateCompletion = old.Completed;
            long generation = old.Last.Generation; bool appliedDuring = false;
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 0, "beta"), () =>
            {
                if (click) NativeRadioClick(choice); else Selection(choice).Select();
                appliedDuring = (bool)Get(Owner(f.View), "IsConcise") && Phase(f.View) == "Paused" &&
                    File.Exists(f.Path) && File.ReadAllText(f.Path) == "Concise";
            }))
            {
                old.ProgressAt(6); Settle();
                Check(hook.Entered, "Version selection did not exercise the actual highlighted Run bring-into-view callback.");
                Check(appliedDuring, "Reentrant " + (click ? "native OnClick" : "WPF SelectionItem.Select") +
                    " was dropped/deferred: checked=" + choice.IsChecked + ", modelConcise=" + Get(Owner(f.View), "IsConcise") +
                    ", phase=" + Phase(f.View) + ", cancels=" + old.Cancels + ".");
                Check(choice.IsChecked == true && (long)Get(Owner(f.View), "Version") == 2,
                    "Reentrant version selection duplicated the change or left the radio out of sync.");
                Check(Segment(f.View) == 1 && Offset(f.View) == 2 && Peer(f.Status).GetName().Contains("Paused — Concise"),
                    "Reentrant selection lost the section mapping or accessible paused state.");
                Check(old.Cancels == 1 && old.Disposals == 1 && f.Backend.Launches.Count == 1,
                    "Reentrant version selection did not retire exactly the old worker without autoplay.");
                lateProgress(generation, 11); lateCompletion(generation); Settle();
                Check(Phase(f.View) == "Paused" && Segment(f.View) == 1 && Offset(f.View) == 2 && f.Backend.Launches.Count == 1,
                    "Retired progress/completion revived playback after reentrant accessible selection.");
                Check(f.Observed.All(task => !task.IsFaulted), "Reentrant version selection produced an unexpected observed failure.");
            }
        }

        foreach (bool outerClick in new[] { false, true })
        foreach (bool innerClick in new[] { false, true })
        using (var f = new Fixture())
        {
            f.Start(); FakeSession old = f.Session;
            RadioButton concise = Field<RadioButton>(f.View, "conciseChoice"), detailed = Field<RadioButton>(f.View, "detailedChoice");
            Run brief = Field<FlowDocument>(f.View, "conciseDocument").Blocks.Cast<Paragraph>().Last().Inlines.OfType<Run>().Single(r => r.Text == "Brief");
            using (var inner = new BringIntoViewOnce(brief, () =>
            { if (innerClick) NativeRadioClick(detailed); else Selection(detailed).Select(); }))
            using (var outer = new BringIntoViewOnce(DisplayWord(f, 0, "beta"), () =>
            { if (outerClick) NativeRadioClick(concise); else Selection(concise).Select(); }))
            {
                old.ProgressAt(6); Settle();
                Check(outer.Entered && inner.Entered, "Nested actual peer/OnClick choice did not reenter both versions' Follow callbacks.");
                Check(!(bool)Get(Owner(f.View), "IsConcise") && detailed.IsChecked == true && concise.IsChecked == false &&
                    (long)Get(Owner(f.View), "Version") == 3, "Nested genuine choice was dropped or Checked/Click double-invoked it.");
                Check(File.ReadAllText(f.Path) == "Detailed", "Older reentrant choice overwrote the newer accessible preference.");
                Check(Phase(f.View) == "Paused" && Segment(f.View) == 0 && Offset(f.View) == 0 &&
                    old.Cancels == 1 && old.Disposals == 1 && f.Backend.Launches.Count == 1,
                    "Nested accessible choice lost paused position/lifetime or restarted speech.");
            }
        }

        foreach (bool innerClick in new[] { false, true })
        using (var f = new Fixture())
        {
            f.Start(); FakeSession old = f.Session;
            RadioButton concise = Field<RadioButton>(f.View, "conciseChoice"), detailed = Field<RadioButton>(f.View, "detailedChoice");
            Run brief = Field<FlowDocument>(f.View, "conciseDocument").Blocks.Cast<Paragraph>().Last().Inlines.OfType<Run>().Single(r => r.Text == "Brief");
            using (var latest = new BringIntoViewOnce(DisplayWord(f, 0, "Alpha"), () => Selection(concise).Select()))
            using (var inner = new BringIntoViewOnce(brief, () =>
            { if (innerClick) NativeRadioClick(detailed); else Selection(detailed).Select(); }))
            using (var outer = new BringIntoViewOnce(DisplayWord(f, 0, "beta"), () => NativeRadioClick(concise)))
            {
                old.ProgressAt(6); Settle();
                Check(outer.Entered && inner.Entered && latest.Entered,
                    "Same-radio peer selection did not reenter the outer native Click's presentation.");
                Check((bool)Get(Owner(f.View), "IsConcise") && concise.IsChecked == true && detailed.IsChecked == false &&
                    (long)Get(Owner(f.View), "Version") == 4, "Outer native Click suppression swallowed the latest same-radio peer choice.");
                Check(File.ReadAllText(f.Path) == "Concise", "Older nested native Click/peer choice overwrote the latest preference.");
                Check(Phase(f.View) == "Paused" && Segment(f.View) == 1 && Offset(f.View) == 2 &&
                    old.Cancels == 1 && old.Disposals == 1 && f.Backend.Launches.Count == 1,
                    "Same-radio reentrant selection restarted speech or lost exact cleanup/section mapping.");
            }
        }

        foreach (string action in new[] { "close", "stop", "dispose" })
        using (var f = new Fixture())
        {
            f.Start(); FakeSession old = f.Session; Task mutation = Task.CompletedTask;
            Run brief = Field<FlowDocument>(f.View, "conciseDocument").Blocks.Cast<Paragraph>().Last().Inlines.OfType<Run>().Single(r => r.Text == "Brief");
            using (var inner = new BringIntoViewOnce(brief, () => mutation = Mutate(f, action)))
            using (var outer = new BringIntoViewOnce(DisplayWord(f, 0, "beta"), () =>
                Selection(Field<RadioButton>(f.View, "conciseChoice")).Select()))
            {
                old.ProgressAt(6); Settle(); Await(mutation);
                Check(outer.Entered && inner.Entered && (bool)Get(Owner(f.View), "IsConcise"),
                    "Accessible choice did not reach nested " + action + " presentation.");
                Check(Phase(f.View) == (action == "dispose" ? "Disposed" : "Stopped"),
                    "Accessible choice overwrote nested " + action + " lifetime.");
                Check(action == "dispose" ? !File.Exists(f.Path) : File.ReadAllText(f.Path) == "Concise",
                    "Accessible choice lost valid stopped intent or wrote preferences after disposal.");
                Check(old.Cancels == 1 && old.Disposals == 1 && f.Backend.Launches.Count == 1,
                    "Nested accessible lifetime action duplicated cleanup or launched speech.");
            }
        }

        using (var f = new Fixture(choice: "Concise", concise: null))
        {
            f.Start(); FakeSession old = f.Session; RadioButton detailed = Field<RadioButton>(f.View, "detailedChoice");
            int clicks = 0; detailed.Click += (s, e) => clicks++;
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 0, "beta"), () => NativeRadioClick(detailed)))
            {
                old.ProgressAt(6); Settle();
                Check(hook.Entered && clicks == 1 && File.ReadAllText(f.Path) == "Detailed" && f.Notice.Visibility == Visibility.Collapsed,
                    "Already-selected Detailed native Click during Follow did not explicitly save fallback intent exactly once.");
                Check((long)Get(Owner(f.View), "Version") == 1 && Phase(f.View) == "Speaking" && old.Cancels == 0 &&
                    f.Backend.Launches.Count == 1, "Already-selected fallback Click changed version or restarted/retired current speech.");
            }
        }
        using (var f = new Fixture(path: directory))
        {
            f.Start();
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 0, "beta"), () =>
                Selection(Field<RadioButton>(f.View, "conciseChoice")).Select()))
            {
                f.Session.ProgressAt(6); Settle();
                Check(hook.Entered && (bool)Get(Owner(f.View), "IsConcise") && Phase(f.View) == "Paused",
                    "Expected preference failure dropped the reentrant accessible choice.");
                Check(f.Notice.Visibility == Visibility.Visible && f.Notice.Text.Contains("Could not save") && Peer(f.Notice).GetName() == f.Notice.Text,
                    "Reentrant selection's expected save failure is not visibly/accessibly reported.");
                Check(f.Observed.All(task => !task.IsFaulted) && f.Backend.Launches.Count == 1,
                    "Expected reentrant save failure faulted UI work or restarted speech.");
            }
        }
        using (var f = new Fixture())
        {
            f.Start(); RadioButton choice = Field<RadioButton>(f.View, "conciseChoice");
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 0, "Alpha"), () =>
            {
                Reject(() => Selection(choice).Select(), typeof(ElementNotEnabledException), "Shutdown refresh accessible version selection");
                NativeRadioClick(choice); choice.RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));
            }))
            {
                f.Dispose();
                Check(hook.Entered && Phase(f.View) == "Disposed" && !(bool)Get(Owner(f.View), "IsConcise"),
                    "Shutdown Follow submitted a choice to the already-retired controller.");
                Check(!File.Exists(f.Path) && f.Backend.Launches.Count == 1,
                    "Shutdown choice wrote preferences or launched speech.");
                Check(f.Observed.All(task => !task.IsFaulted), "Shutdown refresh choice produced an unexpected task failure.");
            }
        }
        using (var f = new Fixture())
        {
            Check(f.Observed.Count == 0 && !File.Exists(f.Path), "Initial model-to-radio synchronization emitted an explicit choice/save.");
            foreach (bool concise in new[] { true, false })
            {
                Await((Task)ownerType.GetMethod("SwitchVersionAsync", Members).Invoke(Owner(f.View), new object[] { concise }));
                Check(Field<RadioButton>(f.View, concise ? "conciseChoice" : "detailedChoice").IsChecked == true &&
                    f.Observed.Count == 0 && !File.Exists(f.Path), "Model-driven Checked synchronization emitted a user operation/preference write.");
            }
            RadioButton choice = Field<RadioButton>(f.View, "conciseChoice"); f.Dispose(); int observed = f.Observed.Count;
            NativeRadioClick(choice); choice.RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent)); Settle();
            Check(Phase(f.View) == "Disposed" && !(bool)Get(Owner(f.View), "IsConcise") && !File.Exists(f.Path) && f.Observed.Count == observed,
                "Late native Click/Checked after disposal submitted work or wrote preferences.");
            Reject(() => Selection(choice).Select(), typeof(ElementNotEnabledException), "Disposed accessible version selection");
        }
    }

    private sealed class BringIntoViewOnce : IDisposable
    {
        private readonly Run run;
        private readonly RequestBringIntoViewEventHandler handler;
        internal bool Entered;
        internal BringIntoViewOnce(Run run, Action action)
        {
            this.run = run;
            handler = (s, e) => { if (Entered) return; Entered = true; action(); };
            run.AddHandler(FrameworkElement.RequestBringIntoViewEvent, handler, true);
        }
        public void Dispose() => run.RemoveHandler(FrameworkElement.RequestBringIntoViewEvent, handler);
    }

    private static Run DisplayWord(Fixture f, int segment, string text) =>
        f.Reader.Document.Blocks.Cast<Paragraph>().ElementAt(segment).Inlines.OfType<Run>().Single(r => r.Text == text);
    private static Task Mutate(Fixture f, string action, int segment = 1, int offset = 8)
    {
        switch (action)
        {
            case "close": Void(f.View, "FrameClosed"); return Task.CompletedTask;
            case "stop": return Call(f.View, "StopAsync");
            case "dispose": ((IDisposable)f.View).Dispose(); return Task.CompletedTask;
            case "version": return Call(f.View, "ChooseVersionAsync", true);
            case "seek": return Call(f.View, "SeekAsync", Position(segment, offset));
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private static void ReentrantNotifications()
    {
        // Exact independent repro: the progress highlight synchronously closes the view while completion
        // for that same prompt is already in the mailbox. No fake Changed notification or delayed close.
        foreach (string action in new[] { "close", "stop", "dispose", "version", "seek" })
        using (var f = new Fixture())
        {
            f.Start(); Task mutation = Task.CompletedTask; string during = null;
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 0, "beta"), () =>
            { mutation = Mutate(f, action); during = Phase(f.View); }))
            {
                f.Session.ProgressAt(6); f.Session.Finish(); Settle(); Await(mutation); Settle();
                Check(hook.Entered, "Progress/completion did not synchronously raise the highlighted Run bring-into-view event.");
                string expected = action == "dispose" ? "Disposed" : action == "version" ? "Paused" : action == "seek" ? "Speaking" : "Stopped";
                Check(Phase(f.View) == expected, "Reentrant " + action + " during highlight/completion was overwritten: during=" + during +
                    ", after=" + Phase(f.View) + ", launches=" + f.Backend.Launches.Count + ".");
                Check(f.Backend.Launches.Count == (action == "seek" ? 2 : 1), "Retired completion launched speech after reentrant " + action + ".");
                Check(action == "version" ? (bool)Get(Owner(f.View), "IsConcise") && Segment(f.View) == 1 && Offset(f.View) == 2 :
                    action == "seek" ? Segment(f.View) == 1 && Offset(f.View) == 8 : Segment(f.View) == 0 && Offset(f.View) == 6,
                    "Retired completion changed the selected section/word after reentrant " + action + ".");
                Check(f.Backend.Launches[0].Session.Cancels == 1 && f.Backend.Launches[0].Session.Disposals == 1,
                    "Reentrant " + action + " did not retire exactly the old speech session.");
            }
        }

        foreach (string action in new[] { "close", "stop", "dispose", "version", "seek" })
        using (var f = new Fixture())
        {
            f.Start(); Task mutation = Task.CompletedTask;
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 1, "First"), () => mutation = Mutate(f, action, 0, 11)))
            {
                Task seek = Call(f.View, "SeekAsync", Position(1, 8)); Await(seek); Await(mutation); Settle();
                Check(hook.Entered, "Automatic seek restart did not exercise synchronous presentation reentry.");
                string expected = action == "dispose" ? "Disposed" : action == "version" ? "Paused" : "Stopped";
                Check(Phase(f.View) == expected && f.Backend.Launches.Count == 1, "Old Seek restarted speech after reentrant " + action + ".");
                Check(action == "version" ? Segment(f.View) == 0 && (bool)Get(Owner(f.View), "IsConcise") :
                    action == "seek" ? Segment(f.View) == 0 && Offset(f.View) == 11 : Segment(f.View) == 1 && Offset(f.View) == 8,
                    "Old Seek overwrote newer reentrant selection for " + action + ".");
            }
        }

        foreach (bool resume in new[] { false, true })
        foreach (string action in new[] { "close", "stop", "dispose", "version", "seek" })
        using (var f = new Fixture())
        {
            f.Start(); if (resume) Await(Call(f.View, "TogglePlaybackAsync"));
            FakeSession old = f.Session; int commands = resume ? old.Resumes : old.Pauses;
            Task mutation = Task.CompletedTask;
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 0, "Alpha"), () => mutation = Mutate(f, action)))
            {
                Await(Call(f.View, "TogglePlaybackAsync")); Await(mutation); Settle();
                Check(hook.Entered, "Pending " + (resume ? "Resume" : "Pause") + " did not synchronously reenter presentation.");
                Check((resume ? old.Resumes : old.Pauses) == commands, "State command was sent to a retired prompt after reentrant " + action + ".");
                string expected = action == "dispose" ? "Disposed" : action == "version" || action == "seek" && !resume ? "Paused" : "Stopped";
                Check(Phase(f.View) == expected && f.Backend.Launches.Count == 1, "Pending state command overwrote the reentrant " + action + " transition.");
            }
        }

        foreach (string action in new[] { "close", "stop", "dispose", "version", "seek" })
        using (var f = new Fixture(backend: new FakeBackend { Configure = s => s.HoldSpeak = true }))
        {
            Task start = Call(f.View, "TogglePlaybackAsync"); PumpUntil(() => f.Backend.Launches.Count == 1 && f.Session.Speeches.Count == 1);
            object prompt = ownerType.GetField("active", Members).GetValue(Owner(f.View));
            FakeSession old = f.Session; Task mutation = Task.CompletedTask;
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 0, "Alpha"), () => mutation = Mutate(f, action)))
            {
                old.Last.Ready.SetResult(old.Last.Generation); Await(start);
                if (action == "seek")
                {
                    PumpUntil(() => f.Backend.Launches.Count == 2 && f.Session.Speeches.Count == 1);
                    f.Session.Last.Ready.SetResult(f.Session.Last.Generation);
                }
                Await(mutation); Settle();
                Check(hook.Entered && (bool)prompt.GetType().GetField("closed", Members).GetValue(prompt), "Accepted start did not exercise prompt retirement during Notify.");
                Check(!(bool)prompt.GetType().GetField("ready", Members).GetValue(prompt), "Accepted stale start called Ready after presentation retired its prompt.");
                Check(Phase(f.View) == (action == "dispose" ? "Disposed" : action == "version" ? "Paused" : action == "seek" ? "Speaking" : "Stopped"),
                    "Accepted start overwrote reentrant " + action + ".");
            }
        }

        // BackgroundWork is also synchronous. Retiring completion must fence its continuation after observers.
        foreach (string action in new[] { "close", "stop", "dispose", "version", "seek" })
        using (var f = new Fixture())
        {
            f.Start(); object owner = Owner(f.View); bool entered = false; Task mutation = Task.CompletedTask;
            Action<Task> observer = task =>
            {
                if (entered || !((UserControl)f.View).Dispatcher.CheckAccess() || (long)Get(owner, "SpeechGeneration") != 0) return;
                entered = true; mutation = Mutate(f, action);
            };
            EventInfo background = ownerType.GetEvent("BackgroundWork", Members);
            background.GetAddMethod(true).Invoke(owner, new object[] { observer });
            try
            {
                f.Session.Finish(); Settle(); Await(mutation); Settle();
                Check(entered, "Completion did not exercise synchronous BackgroundWork reentry.");
                Check(Phase(f.View) == (action == "dispose" ? "Disposed" : action == "version" ? "Paused" : action == "seek" ? "Speaking" : "Stopped"),
                    "Completion transition ignored reentrant " + action + " from its background-task observer.");
                Check(f.Backend.Launches.Count == (action == "seek" ? 2 : 1), "Background observer retirement leaked or restarted stale speech.");
            }
            finally { background.GetRemoveMethod(true).Invoke(owner, new object[] { observer }); }
        }

        // A same-prompt Pause does not retire the accepted start: legitimate sticky early completion still wins.
        using (var f = new Fixture(backend: new FakeBackend { Configure = s =>
        { s.HoldSpeak = true; s.BeforeSpeakReturn = (session, speech) => session.Finish(speech.Generation); } }))
        {
            Task start = Call(f.View, "TogglePlaybackAsync"); PumpUntil(() => f.Backend.Launches.Count == 1 && f.Session.Speeches.Count == 1);
            Task pause = Task.CompletedTask;
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 0, "Alpha"), () =>
            { if (Phase(f.View) == "Speaking") pause = Call(f.View, "TogglePlaybackAsync"); }))
            {
                f.Session.Last.Ready.SetResult(f.Session.Last.Generation); Await(start); Await(pause); Settle();
                Check(hook.Entered && Phase(f.View) == "Paused" && Segment(f.View) == 1,
                    "Same-prompt reentrant Pause suppressed legitimate accepted-start/early completion.");
                Check(f.Backend.Launches.Count == 1, "Same-prompt Pause restarted speech automatically.");
            }
        }

        foreach (string action in new[] { "close", "stop", "dispose", "version", "seek" })
        using (var f = new Fixture())
        {
            Task mutation = Task.CompletedTask;
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 0, "Alpha"), () => mutation = Mutate(f, action)))
            {
                Await(Call(f.View, "TogglePlaybackAsync")); Await(mutation); Settle();
                Check(hook.Entered && f.Backend.Launches.Count == 0, "Starting notification launched a worker after reentrant " + action + ".");
                Check(Phase(f.View) == (action == "dispose" ? "Disposed" : action == "version" ? "Paused" : "Stopped"),
                    "Starting notification overwrote reentrant " + action + ".");
                if (action == "close" || action == "stop")
                {
                    f.Start();
                    Check(Phase(f.View) == "Speaking" && f.Backend.Launches.Count == 1, "Lifetime fencing suppressed later deliberate Play.");
                }
            }
        }

        // View methods also resume after controller Notify/Follow. Preserve the newer version preference
        // and closed/disposed presentation rather than saving or navigating an older action afterwards.
        using (var f = new Fixture())
        {
            f.Start(); Task nested = Task.CompletedTask;
            Run conciseWord = Field<FlowDocument>(f.View, "conciseDocument").Blocks.Cast<Paragraph>().Last().Inlines.OfType<Run>().Single(r => r.Text == "Brief");
            using (var hook = new BringIntoViewOnce(conciseWord, () => nested = Call(f.View, "ChooseVersionAsync", false)))
            {
                Await(Call(f.View, "ChooseVersionAsync", true)); Await(nested); Settle();
                Check(hook.Entered && !(bool)Get(Owner(f.View), "IsConcise") && Phase(f.View) == "Paused", "Nested explicit version choice was not retained.");
                Check(File.ReadAllText(f.Path) == "Detailed", "Outer version continuation overwrote the newer explicit saved choice.");
            }
        }
        using (var f = new Fixture())
        {
            f.Start();
            Run conciseWord = Field<FlowDocument>(f.View, "conciseDocument").Blocks.Cast<Paragraph>().Last().Inlines.OfType<Run>().Single(r => r.Text == "Brief");
            using (var hook = new BringIntoViewOnce(conciseWord, () => ((IDisposable)f.View).Dispose()))
            {
                Await(Call(f.View, "ChooseVersionAsync", true)); Settle();
                Check(hook.Entered && Phase(f.View) == "Disposed", "Version presentation did not exercise synchronous disposal.");
                Check(!File.Exists(f.Path), "Disposed version continuation wrote preferences after shutdown.");
            }
        }
        using (var f = new Fixture())
        {
            f.Seek(0, 6); Void(f.View, "SetFollowing", false);
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 0, "beta"), () => Void(f.View, "FrameClosed")))
            {
                Await(Call(f.View, "NavigateAsync", 1, false)); Settle();
                Check(hook.Entered && Phase(f.View) == "Stopped", "Navigation Follow restore did not exercise synchronous close.");
                Check(Offset(f.View) == 6 && f.Backend.Launches.Count == 0, "Old navigation continued after Follow synchronously closed the view.");
            }
        }
        using (var f = new Fixture())
        {
            f.Start();
            using (var hook = new BringIntoViewOnce(DisplayWord(f, 0, "Alpha"), () => ((IDisposable)f.View).Dispose()))
            {
                Await(Call(f.View, "HideAsync", true)); Settle();
                Check(hook.Entered && Phase(f.View) == "Disposed", "Hide notification did not exercise synchronous disposal.");
                Check(f.Hides == 0, "Disposed Hide continuation invoked the retired owning frame action.");
            }
        }
    }

    private static void Gates()
    {
        var enabled = previewType.GetMethod("IsEnabled", Static);
        Func<string[], string, bool> gate = (args, opt) => (bool)enabled.Invoke(null, new object[] { args, opt });
        foreach (string opt in new[] { "", "0", "true", " 1", "1 " })
            Check(!gate(new[] { "devenv.exe", "/RootSuffix", "Exp" }, opt), "Nonexact opt-in enabled the preview.");
        foreach (string[] args in new[] { new string[0], new[] { "devenv.exe" }, new[] { "/RootSuffix" },
            new[] { "/RootSuffix:Exp" }, new[] { "/RootSuffix", "Exp2" }, new[] { "Exp", "/RootSuffix" }, new[] { "/RootSuffix", "Main" } })
            Check(!gate(args, "1"), "Ungated/main/nonexact root enabled the preview.");
        Check(gate(new[] { "devenv.exe", "/RootSuffix", "Exp" }, "1"), "Exact Debug Exp gate was refused.");
        Check(gate(new[] { "/rootsuffix", "exp" }, "1"), "Gate changed established case-insensitive argument handling.");
        var path = previewType.GetMethod("PreferencePath", Static);
        string isolated = Path.Combine(directory, "override.txt");
        string normal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatVoice", "reader-version.txt");
        Check((string)path.Invoke(null, new object[] { new[] { "/RootSuffix", "Exp" }, "1", isolated }) == isolated, "Gated preference override was ignored.");
        Check((string)path.Invoke(null, new object[] { new[] { "/RootSuffix", "Main" }, "1", isolated }) == normal, "Main instance honored a preview override.");
        Check((string)path.Invoke(null, new object[] { new[] { "/RootSuffix", "Exp" }, "0", isolated }) == normal, "Disabled gate honored a preview override.");
        Check((string)path.Invoke(null, new object[] { new[] { "/RootSuffix", "Exp" }, "1", " " }) == normal, "Empty override changed shared preference location.");
        string preparedDetailed = (string)previewType.GetField("DetailedFixture", Static).GetRawConstantValue();
        string preparedConcise = (string)previewType.GetField("ConciseFixture", Static).GetRawConstantValue();
        using (var f = new Fixture(detailed: preparedDetailed, concise: preparedConcise))
        {
            Check(f.Reader.Document.Blocks.Count >= 5, "Prepared fixture is too short for bounded native acceptance.");
            Check(new TextRange(f.Reader.Document.ContentStart, f.Reader.Document.ContentEnd).Text.Contains("prepared fixture"), "Detailed fixture lost its explicit prepared label.");
            f.Choose(true);
            Check(new TextRange(f.Reader.Document.ContentStart, f.Reader.Document.ContentEnd).Text.Contains("Prepared fixture"), "Concise fixture is not explicitly labelled.");
            Check(f.Backend.Launches.Count == 0, "Fixture/version initialization started speech.");
        }
    }

    private static void Preferences()
    {
        using (var f = new Fixture())
        {
            Check(Phase(f.View) == "Stopped" && !File.Exists(f.Path), "Missing preference was saved or started playback.");
            Check(!((bool)Get(Owner(f.View), "IsConcise")) && f.Backend.Launches.Count == 0, "Default initialization did not stay Detailed/silent.");
            f.Choose(true);
            Check(File.ReadAllText(f.Path) == "Concise", "Explicit choice was not saved immediately.");
            Check(Phase(f.View) == "Paused", "Version selection did not stay paused.");
            f.Choose(false);
            Check(File.ReadAllText(f.Path) == "Detailed", "Detailed choice did not save immediately.");
        }
        using (var f = new Fixture(choice: "Concise"))
        {
            Check((bool)Get(Owner(f.View), "IsConcise"), "Saved Concise was not loaded before playback.");
            Check(f.Backend.Launches.Count == 0 && Phase(f.View) == "Stopped", "Loaded choice launched audio.");
            Check(Field<RadioButton>(f.View, "conciseChoice").IsChecked == true, "Loaded choice UI is inconsistent.");
        }
        using (var f = new Fixture(choice: "Concise", concise: null))
        {
            Check(!(bool)Get(Owner(f.View), "IsConcise"), "Missing Concise did not display Detailed.");
            Check(File.ReadAllText(f.Path) == "Concise", "Missing version overwrote the saved choice.");
            Check(f.Notice.Text.Contains("unchanged") && f.Notice.Visibility == Visibility.Visible, "Missing version notice was not visible.");
            Check(!Field<RadioButton>(f.View, "conciseChoice").IsEnabled, "Missing variant control remained enabled.");
            f.Click(Field<RadioButton>(f.View, "detailedChoice"));
            Check(File.ReadAllText(f.Path) == "Detailed", "Explicit already-checked Detailed did not replace unavailable saved choice: saved='" +
                File.ReadAllText(f.Path) + "', notice='" + f.Notice.Text + "', observed=" + f.Observed.Count + ", faults=" +
                string.Join(";", f.Observed.Where(task => task.IsFaulted).Select(task => task.Exception.ToString())) + ".");
            Check(f.Notice.Visibility == Visibility.Collapsed, "Successful explicit choice retained obsolete warning.");
        }
        using (var f = new Fixture(choice: "invalid preference"))
        {
            Check(f.Notice.Text.Contains("Could not load") && f.Notice.Visibility == Visibility.Visible, "Invalid preference was silently reset.");
            Check(File.ReadAllText(f.Path) == "invalid preference" && !(bool)Get(Owner(f.View), "IsConcise"), "Invalid preference was overwritten or selected Concise.");
        }
        string locked = Path.Combine(directory, "locked.txt");
        File.WriteAllText(locked, "Concise");
        using (File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var f = new Fixture(path: locked))
            Check(f.Notice.Text.Contains("Could not load"), "Load IO failure was hidden.");
        using (var f = new Fixture(path: directory))
        {
            Check(f.Notice.Text.Contains("Could not load"), "Access-denied/directory preference load was hidden.");
            f.Choose(true);
            Check(f.Notice.Text.Contains("Could not save") && f.Notice.Visibility == Visibility.Visible, "Save failure was hidden.");
            Check((bool)Get(Owner(f.View), "IsConcise") && Phase(f.View) == "Paused", "Save failure discarded current explicit choice or started speech.");
        }
    }

    private static Rect BoundsIn(FrameworkElement root, FrameworkElement element) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static T Named<T>(FrameworkElement root, string id) where T : FrameworkElement
    {
        var stack = new Stack<DependencyObject>(); stack.Push(root);
        while (stack.Count > 0)
        {
            DependencyObject node = stack.Pop();
            if (node is T found && AutomationProperties.GetAutomationId(found) == id) return found;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) stack.Push(VisualTreeHelper.GetChild(node, i));
        }
        throw new InvalidOperationException("Missing element " + id);
    }

    private static void ReaderPlacementMatchesPoc(Fixture f)
    {
        var view = (FrameworkElement)f.View;
        view.Measure(new Size(520, 400)); view.Arrange(new Rect(0, 0, 520, 400)); view.UpdateLayout();
        Rect reader = BoundsIn(view, f.Reader);
        Rect concise = BoundsIn(view, Named<RadioButton>(view, "NativeReaderConcise"));
        Rect detailed = BoundsIn(view, Named<RadioButton>(view, "NativeReaderDetailed"));
        Rect follow = BoundsIn(view, Named<Button>(view, "NativeReaderFollow"));
        Rect previous = BoundsIn(view, Named<Button>(view, "NativeReaderPreviousSentence"));
        Rect play = BoundsIn(view, f.Play);
        Rect next = BoundsIn(view, Named<Button>(view, "NativeReaderNextSentence"));
        Rect stop = BoundsIn(view, Named<Button>(view, "NativeReaderStop"));
        Rect up = BoundsIn(view, Named<Button>(view, "NativeReaderPreviousParagraph"));
        Rect close = BoundsIn(view, Named<Button>(view, "NativeReaderClose"));
        Check(close.Right > follow.Right && close.Right > 480 && Math.Abs(close.Top - follow.Top) < 2, "Close is not the rightmost header button beside Follow.");
        var settingsButton = Named<Button>(view, "NativeReaderSettings");
        Rect settings = BoundsIn(view, settingsButton);
        Check(settings.Right <= follow.Left + 1 && Math.Abs(settings.Top - follow.Top) < 2, "Settings is not in the header immediately left of Follow.");
        int opened = 0;
        view.GetType().GetProperty("OpenSettings", Members).SetValue(view, new Action(() => opened++));
        settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(opened == 1, "Reader Settings button did not ask the host to open Settings.");
        Rect down = BoundsIn(view, Named<Button>(view, "NativeReaderNextParagraph"));
        Check(concise.Bottom <= reader.Top && detailed.Left > concise.Left && concise.Left < 40, "Version strip is not top-left above the text.");
        Check(follow.Right > 460 && follow.Bottom <= concise.Top + 1, "Follow is not in the header's right corner.");
        Check(previous.Left < play.Left && play.Left < next.Left && play.Top >= reader.Bottom - 1, "Previous/Play/Next are not below the text in order.");
        Check(Math.Abs((play.Left + play.Right) / 2 - 260) < 6, "Sentence transport is not centered at the bottom.");
        Check(stop.Left > next.Right + 20 && stop.Right > 460 && Math.Abs((stop.Top + stop.Bottom) / 2 - (play.Top + play.Bottom) / 2) < 6, "Stop is not right-aligned on the transport row.");
        Check(up.Left >= reader.Right - 1 && down.Left >= reader.Right - 1 && up.Bottom <= down.Top, "Paragraph up/down rail is not beside the text.");
        var stopButton = Named<Button>(view, "NativeReaderStop");
        Check(!stopButton.IsEnabled, "Fixture Stop expected disabled before playback.");
        var chrome = stopButton.Template.FindName("chrome", stopButton) as Border;
        Check(stopButton.Style.BasedOn != null && stopButton.Style.BasedOn.Setters.OfType<Setter>().Any(s => s.Property == Control.TemplateProperty), "Stop does not derive from the themed button template.");
        var plate = chrome?.Background as SolidColorBrush;
        Check(plate == null || plate.Color != Color.FromRgb(244, 244, 244), "Disabled Stop still paints the stock light plate.");
    }

    private static void PresentationAndNavigation()
    {
        using (var f = new Fixture())
        {
            Check(f.Reader.IsReadOnly && f.Reader.Focusable && f.Reader.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "Native document is not accessible/read-only/wrapping.");
            Check(f.Reader.Document.ColumnWidth == double.PositiveInfinity, "Document retained newspaper columns at narrow width.");
            ReaderPlacementMatchesPoc(f);
            foreach (Control control in Field<List<Control>>(f.View, "controls"))
            {
                Check(!string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(control)), "Native control lacks automation identity.");
                Check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)) && control.Focusable, "Native control lacks accessible name/keyboard focus.");
                Check(control.FocusVisualStyle != null, "Control lacks resource-based keyboard focus visual.");
            }
            f.Click(f.Play);
            PumpUntil(() => Phase(f.View) == "Speaking");
            Check(AutomationProperties.GetName(f.Play) == "Pause" && f.Status.Text.Contains("Speaking"), "Speaking notification did not update presentation.");
            Task.Run(() => f.Session.ProgressAt(6)).GetAwaiter().GetResult(); Settle();
            Check(Offset(f.View) == 6 && Field<Run>(f.View, "highlighted").Text == "beta", "Worker progress did not select the exact displayed word.");
            Check(f.Session.WrongThreadCommands == 0, "UI operation left its owning dispatcher.");
            f.Click(f.Play);
            PumpUntil(() => Phase(f.View) == "Paused");
            Check(AutomationProperties.GetName(f.Play) == "Resume" && f.Status.Text.Contains("Paused"), "Pause acknowledgement did not update presentation.");
            f.Click(f.Play); PumpUntil(() => Phase(f.View) == "Speaking");
            Check(f.Session.Speeches.Count == 1 && f.Session.Resumes == 1, "Resume restarted the prompt.");
            Await(Call(f.View, "StopAsync"));
            Check(Offset(f.View) == 6 && AutomationProperties.GetName(f.Play) == "Play", "Stop lost selection or presentation state.");
            f.Seek(0, 0); Await(Call(f.View, "NavigateAsync", 1, false));
            Check(Segment(f.View) == 0 && Offset(f.View) == 18, "Sentence navigation skipped its intra-paragraph destination.");
            Await(Call(f.View, "NavigateAsync", 1, true));
            Check(Segment(f.View) == 1 && Offset(f.View) == 0, "Paragraph navigation confused sentence offsets.");
            var paragraphs = f.Reader.Document.Blocks.Cast<Paragraph>().ToArray();
            var marker = (Run)paragraphs[1].Inlines.FirstInline;
            Await(Call(f.View, "SeekPointerAsync", marker.ContentStart));
            Check(Segment(f.View) == 1 && Offset(f.View) == 0 && Field<Run>(f.View, "highlighted").Text == "2. ", "Displayed step marker did not map to spoken prefix.");
            var words = paragraphs[0].Inlines.OfType<Run>().Where(r => !string.IsNullOrWhiteSpace(r.Text)).ToArray();
            Await(Call(f.View, "SeekPointerAsync", words[2].ContentStart));
            Check(Segment(f.View) == 0 && Offset(f.View) == 11, "Selected word pointer mapped to wrong section/offset.");
            f.Start();
            Check(f.Session.Last.Text == "gamma. Next sentence.", "Selected-word Play did not speak only the suffix.");
            f.Session.Finish(); Settle();
            Check(Segment(f.View) == 1 && f.Status.Text.Contains("section 2"), "Sequential completion did not notify the view.");
            f.Session.Finish(); Settle();
            Check(Phase(f.View) == "Finished" && f.Status.Text.Contains("Finished") && AutomationProperties.GetName(f.Play) == "Play", "Natural final completion did not update the view.");
            f.Start(); Check(Segment(f.View) == 0 && Offset(f.View) == 0, "Finished Play did not restart reply bounds.");
        }
    }

    private static void VersionSwitchAndErrors()
    {
        using (var f = new Fixture())
        {
            f.Seek(1, 8); f.Start();
            FakeSession old = f.Session; Action<long, int> late = old.Progress;
            f.Choose(true);
            Check(Phase(f.View) == "Paused" && Segment(f.View) == 0 && Offset(f.View) == 0, "Reordered version switch did not pause/map section identity.");
            Check(old.Cancels == 1 && old.Disposals == 1 && f.Backend.Launches.Count == 1, "Switch retained old prompt or auto-started Concise.");
            Check(f.Reader.Document == Field<FlowDocument>(f.View, "conciseDocument") && AutomationProperties.GetName(f.Play) == "Resume", "Switched document/control state is inconsistent.");
            late(old.Last.Generation, 12); Settle();
            Check(Offset(f.View) == 0 && Phase(f.View) == "Paused", "Stale progress revived switched version.");
            f.Choose(false);
            Check(Segment(f.View) == 1 && Phase(f.View) == "Paused", "Return version mapping used ordinal instead of identity.");
        }
        using (var f = new Fixture(backend: new FakeBackend { HoldLaunch = true }))
        {
            Task play = Call(f.View, "TogglePlaybackAsync");
            Check(Phase(f.View) == "Starting" && !f.Play.IsEnabled, "Starting state was not visible/disabled.");
            f.Backend.Launches[0].Fail(new IOException("Expected isolated worker refusal.")); Await(play);
            Check(Phase(f.View) == "Error" && f.Status.Text.Contains("Speech could not start"), "Operational backend failure was hidden.");
            f.Backend.HoldLaunch = false; f.Start();
            Check(Phase(f.View) == "Speaking" && f.Backend.Launches.Count == 2, "Deliberate retry did not recover operational failure.");
        }
        var broken = new FakeBackend { Configure = s => s.BeforeSpeakReturn = (session, speech) => { throw new NullReferenceException("Isolated unexpected failure."); } };
        using (var f = new Fixture(backend: broken))
        {
            f.Click(f.Play);
            PumpUntil(() => f.Observed.Any(t => t.IsFaulted));
            Check(f.Status.Text.Contains("operation failed") && f.Observed.Any(t => t.IsFaulted && t.Exception.GetBaseException() is NullReferenceException), "Unexpected UI task failure was swallowed or unreported.");
        }
        var brokenAdvance = new FakeBackend { Configure = s => s.BeforeSpeakReturn = (session, speech) =>
        { if (speech.Generation > 1) throw new NullReferenceException("Isolated autonomous advance failure."); } };
        using (var f = new Fixture(backend: brokenAdvance))
        {
            f.Start(); f.Session.Finish();
            PumpUntil(() => f.Observed.Any(t => t.IsFaulted));
            Check(f.Status.Text.Contains("operation failed"), "Autonomous sequential-advance task failure was not visible/observed.");
        }
    }

    private static void ThemeAndNarrowLayout()
    {
        using (var f = new Fixture())
        {
            var view = (UserControl)f.View;
            Void(view, "ApplyTheme", false); Void(view, "Refresh");
            object text = Get(view, "TextKey"), background = Get(view, "BackgroundKey"),
                highlight = Get(view, "HighlightKey"), highlightedText = Get(view, "HighlightTextKey");
            view.Resources[text] = Brushes.DarkBlue; view.Resources[background] = Brushes.Beige;
            view.Resources[highlight] = Brushes.Navy; view.Resources[highlightedText] = Brushes.White;
            view.Resources[SystemColors.GrayTextBrushKey] = Brushes.Gray;
            Void(view, "ApplyTheme", false); Void(view, "Refresh");
            var marker = f.Reader.Document.Blocks.Cast<Paragraph>().Last().Inlines.OfType<Run>().First();
            Check(marker.Foreground == Brushes.DarkBlue && view.Foreground == Brushes.DarkBlue, "Body/marker/header did not inherit dynamic owning-theme text.");
            Check(f.Reader.Background == Brushes.Beige && f.Reader.SelectionBrush == Brushes.Navy, "Content/selection did not use owning dynamic resources.");
            Check(Field<Run>(view, "highlighted").Background == Brushes.Navy && Field<Run>(view, "highlighted").Foreground == Brushes.White, "Highlight/foreground did not use owning resources.");
            Check(Field<Button>(view, "stop").Foreground == Brushes.Gray, "Disabled control did not use dynamic system gray text.");
            Check(((TextBlock)Field<Button>(view, "play").Content).Foreground == Brushes.DarkBlue,
                "Button glyph did not inherit the owning theme text brush: " + ((TextBlock)Field<Button>(view, "play").Content).Foreground);
            Check(((TextBlock)Field<RadioButton>(view, "conciseChoice").Content).Foreground == Brushes.DarkBlue,
                "Radio label did not inherit the owning theme text brush: " + ((TextBlock)Field<RadioButton>(view, "conciseChoice").Content).Foreground);
            view.Resources[text] = Brushes.DarkGreen;
            Void(view, "ApplyTheme", false);
            Check(marker.Foreground == Brushes.DarkGreen, "Live resource change left hard-coded marker colors.");
            var playButton = Field<Button>(view, "play");
            Check(playButton.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue && playButton.Foreground == Brushes.DarkGreen,
                "Icon button foreground was frozen to a resolved brush instead of following the VS theme resource: " + playButton.ReadLocalValue(Control.ForegroundProperty));
            view.Resources[text] = Brushes.DarkRed; Settle();
            Check(playButton.Foreground == Brushes.DarkRed, "Icon button did not follow a VS theme switch without re-applying the theme.");
            view.Resources[text] = Brushes.DarkGreen;
            view.Measure(new Size(180, 650)); view.Arrange(new Rect(0, 0, 180, 650)); view.UpdateLayout();
            foreach (Control control in Field<List<Control>>(view, "controls"))
            {
                Rect box = control.TransformToAncestor(view).TransformBounds(new Rect(control.RenderSize));
                Check(box.Left >= -0.5 && box.Right <= 180.5, "Narrow-window control is clipped: " + AutomationProperties.GetAutomationId(control));
            }
            Check(f.Reader.ActualWidth <= 180 && f.Reader.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "Narrow-window reply forced horizontal scrolling.");
            Void(view, "ApplyTheme", true); Void(view, "Refresh");
            Check(Get(view, "TextKey") == SystemColors.WindowTextBrushKey && Get(view, "BackgroundKey") == SystemColors.WindowBrushKey,
                "High Contrast did not override owning theme with system content resources.");
            Check(Get(view, "HighlightKey") == SystemColors.HighlightBrushKey && Get(view, "HighlightTextKey") == SystemColors.HighlightTextBrushKey,
                "High Contrast did not override highlight/selection resources.");
            Check(Get(view, "ErrorKey") == SystemColors.WindowTextBrushKey, "High Contrast error text lacks system contrast override.");
            Check(f.Reader.Background == SystemColors.WindowBrush && f.Reader.SelectionBrush == SystemColors.HighlightBrush,
                "High Contrast content/selection was not opaque system rendering.");
            Check(Field<Run>(view, "highlighted").Foreground == SystemColors.HighlightTextBrush, "High Contrast highlight text did not update.");
            Void(view, "ApplyTheme", false); Void(view, "Refresh");
            Check(marker.Foreground == Brushes.DarkGreen && f.Reader.Background == Brushes.Beige, "Returning from High Contrast lost live owning resources.");
        }
    }

    private static void PendingStatePresentation()
    {
        using (var f = new Fixture(backend: new FakeBackend { Configure = s => s.HoldPause = true }))
        {
            f.Start(); Task pause = Call(f.View, "TogglePlaybackAsync");
            Check(Phase(f.View) == "Pausing" && f.Status.Text.Contains("Pausing") && !f.Play.IsEnabled, "Pending Pause was not visibly bounded/disabled.");
            f.Session.ProgressAt(11); Settle();
            Check(Field<Run>(f.View, "highlighted").Text == "gamma.", "Pending state progress did not update exact visible word.");
            FakeSession old = f.Session;
            Await(Call(f.View, "HideAsync", false));
            old.PauseReady.SetResult(true); Await(pause); Settle();
            Check(Phase(f.View) == "Stopped" && f.Status.Text.Contains("Stopped") && Offset(f.View) == 11, "Late Pause after hide revived UI state/position.");
        }
        using (var f = new Fixture(backend: new FakeBackend { Configure = s => s.HoldPause = true }))
        {
            f.Start(); Task pause = Call(f.View, "TogglePlaybackAsync");
            f.Session.PauseReady.SetResult(false); f.Session.Finish(); Await(pause); Settle();
            Check(Phase(f.View) == "Paused" && Segment(f.View) == 1 && f.Status.Text.Contains("section 2") && AutomationProperties.GetName(f.Play) == "Resume",
                "Natural completion before Pause confirmation did not publish the audited next-section state.");
        }
        using (var f = new Fixture(backend: new FakeBackend { Configure = s => s.BeforeSpeakReturn = (session, speech) => session.ProgressAt(6, speech.Generation) }))
        {
            f.Start(); Settle();
            Check(Offset(f.View) == 6 && Field<Run>(f.View, "highlighted").Text == "beta", "Early worker event before Speak return was lost by presentation notifications.");
        }
        using (var f = new Fixture())
        {
            f.Start(); Action<long, int> late = f.Session.Progress;
            f.Session.ProgressAt(6);
            Await(Call(f.View, "HideAsync", false)); Settle();
            Check(Phase(f.View) == "Stopped" && Offset(f.View) == 0 && !f.Status.Text.Contains("operation failed"),
                "Retired queued progress/cancelled dispatcher task revived selection or displayed an unexpected failure.");
            late(f.Session.Last.Generation, 11); Settle();
            Check(Offset(f.View) == 0 && Phase(f.View) == "Stopped", "Early/stale callback after hide changed presentation.");
        }
    }

    private static void KeyboardAndFollow()
    {
        using (var f = new Fixture())
        {
            object view = f.View;
            var control = (UserControl)view;
            Check(!HandleKey(view, Key.Space, new TextBox()), "Keyboard outside reader was intercepted.");
            Check(!HandleKey(view, Key.Space, f.Reader, handled: true), "Handled key was invoked again.");
            Check(!HandleKey(view, Key.Space, f.Reader, repeat: true), "Repeated key toggled transport.");
            Check(!HandleKey(view, Key.Space, f.Reader, modifiers: ModifierKeys.Control), "Modified editor shortcut was intercepted.");
            Check(!HandleKey(view, Key.Tab, f.Reader), "Tab navigation was intercepted.");
            foreach (Control native in Field<List<Control>>(view, "controls"))
                Check(!HandleKey(view, Key.Space, native) && !HandleKey(view, Key.Right, native), "Reader stole a native button/radio key.");
            var combo = new ComboBox(); var edit = new TextBox(); var scroll = new ScrollBar();
            var layout = (DockPanel)control.Content; layout.Children.Add(combo); layout.Children.Add(edit); layout.Children.Add(scroll);
            Check(!HandleKey(view, Key.Down, combo) && !HandleKey(view, Key.Left, edit) && !HandleKey(view, Key.Down, scroll), "Dropdown/edit/scrollbar keys were intercepted.");
            Check(f.Backend.Launches.Count == 0, "Refused keys started speech.");
            Check(HandleKey(view, Key.Space, f.Reader), "Focused reader Space was refused."); PumpUntil(() => Phase(view) == "Speaking");
            Check(f.Backend.Launches.Count == 1, "Focused Space invoked Play more than once.");
            Check(HandleKey(view, Key.Space, f.Reader), "Focused pause Space was refused."); PumpUntil(() => Phase(view) == "Paused");
            Check(f.Session.Pauses == 1, "Focused Space paused more than once.");
            Check(HandleKey(view, Key.Right, f.Reader), "Focused sentence Right was refused."); Settle();
            Check(Segment(view) == 0 && Offset(view) == 18, "Keyboard sentence navigation used wrong offset.");
            Check(HandleKey(view, Key.Down, f.Reader), "Focused paragraph Down was refused."); Settle();
            Check(Segment(view) == 1 && Offset(view) == 0, "Keyboard paragraph navigation used wrong offset.");
            Check(HandleKey(view, Key.Up, f.Reader), "Focused paragraph Up was refused."); Settle();
            Check(Segment(view) == 0, "Keyboard Up did not return paragraph.");
            Check(HandleKey(view, Key.End, f.Reader), "Focused End was refused."); Settle();
            Check(Segment(view) == 1 && Offset(view) == 25, "End did not select the last word of the reply.");
            Check(HandleKey(view, Key.Home, f.Reader), "Focused Home was refused."); Settle();
            Check(Segment(view) == 0 && Offset(view) == 0, "Home did not select reply start.");
            Check(!HandleKey(view, Key.Escape, Field<RadioButton>(view, "conciseChoice"), handled: true), "Handled radio Esc was invoked again.");
            f.Reader.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            Check(!(bool)Get(view, "Following") && f.Status.Text.Contains("suspended"), "Manual wheel did not suspend Follow.");
            f.Start(); f.Session.ProgressAt(6); Settle();
            Check(!(bool)Get(view, "Following") && Offset(view) == 6, "Speech progress restored Follow during manual browsing.");
            f.Click(Field<Button>(view, "follow"));
            Check((bool)Get(view, "Following") && Offset(view) == 6, "Explicit Follow lost selected word or failed to restore follow.");
            f.Reader.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack, 2) { RoutedEvent = ScrollBar.ScrollEvent });
            Check(!(bool)Get(view, "Following"), "Scrollbar browsing did not suspend Follow.");
            f.Seek(0, 11); Check((bool)Get(view, "Following"), "Explicit word seek did not restore Follow.");
            Check(HandleKey(view, Key.Escape, f.Reader), "Focused Esc was refused."); Settle();
            Check(f.Hides == 1 && Phase(view) == "Stopped" && Offset(view) == 11, "Esc did not stop/hide exactly once while retaining position.");
            Check(!f.Reader.IsKeyboardFocused && !control.IsKeyboardFocused, "Scrolling or seeking stole keyboard focus.");
        }
    }

    private sealed class FrameProxy : RealProxy
    {
        internal object Sink;
        internal int Advises, Unadvises;
        internal int Result;
        internal FrameProxy(Type contract) : base(contract) { }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            object[] output = (object[])call.Args.Clone();
            switch (call.MethodName)
            {
                case "Advise": Advises++; Sink = call.Args[0]; output[1] = 71u; break;
                case "Unadvise": Unadvises++; Check((uint)call.Args[0] == 71, "Frame sink released another subscription."); break;
                default: throw new NotSupportedException(call.MethodName);
            }
            return new ReturnMessage(Result, output, output.Length, call.LogicalCallContext, call);
        }
    }
    private static object FrameSink(object view) => Activator.CreateInstance(frameType, Members, null, new[] { view }, null);
    private static void Attach(object sink, object frame) => frameType.GetMethod("Attach", Members).Invoke(sink, new[] { frame });
    private static void Close(object sink)
    {
        object[] options = { 123u };
        Check((int)frameType.GetMethod("OnClose", Members).Invoke(sink, options) == 0 && (uint)options[0] == 123u,
            "Close notification overwrote VS options or refused normal close.");
    }
    private static void FrameAndLifetime()
    {
        Type contract = frameType.GetInterfaces().Single(t => t.Name == "IVsWindowFrameNotify").Assembly.GetType("Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame2", true);
        using (var f = new Fixture(frame: false))
        {
            var proxy = new FrameProxy(contract); object sink = FrameSink(f.View);
            try
            {
                Attach(sink, new object());
                Check(!f.Play.IsEnabled && Field<TextBlock>(f.View, "frameNotice").Text.Contains("unavailable"), "Missing frame contract did not fail closed visibly.");
                f.Start(); Check(f.Backend.Launches.Count == 0, "Missing close registration permitted speech.");
                proxy.Result = unchecked((int)0x80004005); Attach(sink, proxy.GetTransparentProxy());
                Check(!f.Play.IsEnabled, "Failed Advise permitted speech.");
                proxy.Result = 0; Attach(sink, proxy.GetTransparentProxy());
                Check(f.Play.IsEnabled && proxy.Advises == 2 && proxy.Sink == sink, "Frame notification did not register its exact owned sink.");
                f.Seek(0, 6); f.Start();
                foreach (string method in new[] { "OnShow", "OnMove", "OnSize", "OnDockableChange" })
                    foreach (MethodInfo m in frameType.GetMethods(Members).Where(m => m.Name == method))
                        m.Invoke(sink, m.GetParameters().Select(p => (object)0).ToArray());
                var view = (UserControl)f.View;
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                view.Visibility = Visibility.Hidden; view.Visibility = Visibility.Visible;
                Check(Phase(f.View) == "Speaking" && f.Session.Cancels == 0, "Dock/tab/auto-hide/unload changed playback lifetime.");
                Close(sink); Settle();
                Check(Phase(f.View) == "Stopped" && Offset(f.View) == 6 && !((bool)Get(f.View, "IsDisposed")), "Synthetic X destroyed reply/position or failed to stop.");
                Check(f.Session.Cancels == 1 && f.Session.Disposals == 1 && f.Hides == 0, "Frame X forced manual hide/geometry or leaked worker.");
                view.Visibility = Visibility.Visible; view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); Settle();
                Check(Phase(f.View) == "Stopped" && f.Backend.Launches.Count == 1, "Reopen automatically started speech.");
            }
            finally { ((IDisposable)sink).Dispose(); ((IDisposable)sink).Dispose(); }
            Check(proxy.Unadvises == 1, "Frame sink unadvised more than once.");
            Close(sink); Check(Phase(f.View) == "Stopped", "Retired close sink affected state.");
        }
        using (var f = new Fixture(backend: new FakeBackend { HoldLaunch = true }))
        {
            Task startup = Call(f.View, "TogglePlaybackAsync");
            object sink = FrameSink(f.View);
            Close(sink);
            Check(Phase(f.View) == "Stopped" && f.Backend.Launches[0].Token.IsCancellationRequested, "Close during startup did not fence pending launch.");
            f.Backend.Launches[0].Complete(); Await(startup); Settle();
            Check(f.Session.Disposals == 1 && f.Session.Speeches.Count == 0, "Close/start race revived or leaked late worker.");
            ((IDisposable)sink).Dispose();
        }
        using (var f = new Fixture(backend: new FakeBackend { Configure = s => s.HoldCancel = true }))
        {
            f.Start(); Task hidden = Call(f.View, "HideAsync", true);
            Check(!hidden.IsCompleted && f.Hides == 1 && Phase(f.View) == "Stopped", "Hide blocked on cancellation acknowledgement.");
            f.Session.CancelReady.SetResult(true); Await(hidden);
            Check(f.Session.Disposals == 1, "Delayed cancellation did not dispose exact worker.");
        }
    }

    private static object PreviewMember(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name, Members);
        FieldInfo field = property == null ? target.GetType().GetField(name, Members) : null;
        if (property == null && field == null) throw new MissingMemberException(target.GetType().FullName, name);
        return property != null ? property.GetValue(target) : field.GetValue(target);
    }

    // The worker acknowledges options before it starts speaking, so Preview must keep the worker alive until Completed.
    private static void SettingsPreviewWaitsForSpeech()
    {
        string worker = Path.Combine(AppContext.BaseDirectory, "VsCopilotChatVoice.Reader.exe");
        Check(File.Exists(worker), "Preview worker fixture exists.");
        Type previewHelper = assembly.GetType("VsCopilotChatVoice.VisualStudio.SpeechPreview", true);
        Type hostType = assembly.GetType("VsCopilotChatVoice.Speech.SpeechWorkerHost", true);
        Type snapshotType = assembly.GetType("VsCopilotChatVoice.Speech.SpeechOptionsSnapshot", true);
        Type queryType = assembly.GetType("VsCopilotChatVoice.Speech.SpeechCapabilityQuery", true);
        MethodInfo speak = previewHelper.GetMethod("SpeakAsync", Static);
        Task<object> StartPreview(string voiceName, CancellationToken token, out object host)
        {
            Task launch = (Task)hostType.GetMethod("LaunchAsync", new[] { typeof(string), typeof(bool), typeof(TimeSpan), typeof(CancellationToken) })
                .Invoke(null, new object[] { worker, true, TimeSpan.FromSeconds(15), CancellationToken.None });
            launch.GetAwaiter().GetResult();
            host = launch.GetType().GetProperty("Result").GetValue(launch);
            object options = Activator.CreateInstance(snapshotType, voiceName, 0, 100);
            Task task = (Task)speak.Invoke(null, new[] { host, "This is a preview.", options, TimeSpan.FromSeconds(30), token });
            return task.ContinueWith(t => { t.GetAwaiter().GetResult(); return (object)null; });
        }
        Task capability = (Task)queryType.GetMethod("QueryAsync", Static).Invoke(null, new object[] { worker, TimeSpan.FromSeconds(15), CancellationToken.None });
        capability.GetAwaiter().GetResult();
        object voices = capability.GetType().GetProperty("Result").GetValue(capability);
        string voice = ((IEnumerable)PreviewMember(voices, "Voices")).Cast<object>()
            .Where(v => (bool)PreviewMember(v, "Enabled")).Select(v => (string)PreviewMember(v, "Name")).First();
        object host;
        Task<object> selected = StartPreview(voice, CancellationToken.None, out host);
        try
        {
            selected.GetAwaiter().GetResult();
            Check(true, "Preview with the selected voice runs to the worker's Completed event before teardown.");
        }
        finally { ((IDisposable)host).Dispose(); }
        Task<object> missing = StartPreview("Missing preview voice " + Guid.NewGuid().ToString("N"), CancellationToken.None, out host);
        try { Reject(() => missing.GetAwaiter().GetResult(), typeof(InvalidOperationException), "Missing preview voice is reported, not substituted."); }
        finally { ((IDisposable)host).Dispose(); }
        using (var stop = new CancellationTokenSource())
        {
            stop.Cancel();
            Reject(() => StartPreview(voice, stop.Token, out host).GetAwaiter().GetResult(), typeof(OperationCanceledException), "Cancelled Preview stops without fault.");
            ((IDisposable)host).Dispose();
        }
    }

    private static void SettingsUsesThemedControls()
    {
        Type styles = assembly.GetType("VsCopilotChatVoice.VisualStudio.VsThemedStyles", true);
        Func<string, Style> build = name => (Style)styles.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        var button = new Button { Style = build("Button"), Content = "Text" };
        button.Measure(new Size(200, 40)); button.Arrange(new Rect(0, 0, 200, 40)); button.UpdateLayout();
        var buttonChrome = button.Template.FindName("chrome", button) as Border;
        Check(buttonChrome != null && buttonChrome.Opacity == 1, "Themed button template lacks chrome or starts dimmed.");
        button.IsEnabled = false; button.UpdateLayout();
        Check(buttonChrome.Opacity < 1, "Disabled themed button is not dimmed (stock light plate risk).");
        Check(buttonChrome.Background is not SolidColorBrush plate || plate.Color != Color.FromRgb(244, 244, 244), "Themed button paints the stock light plate.");
        foreach (string name in new[] { "RadioButton", "CheckBox", "ComboBox", "ComboBoxItem", "IconButton", "VersionChoice" })
        {
            Style built = build(name);
            Control control = name == "RadioButton" || name == "VersionChoice" ? new RadioButton { Content = "x" } : name == "CheckBox" ? new CheckBox { Content = "x" }
                : name == "IconButton" ? new Button { Content = "x" } : name == "ComboBox" ? new ComboBox { Width = 120 } : (Control)new ComboBoxItem { Content = "x" };
            control.Style = built; control.Measure(new Size(200, 40)); control.Arrange(new Rect(0, 0, 200, 40)); control.UpdateLayout();
            Check(control.Template != null && VisualTreeHelper.GetChildrenCount(control) > 0, "Themed " + name + " template did not build.");
            control.IsEnabled = false; control.UpdateLayout();
            Check(control.Opacity < 1 || (VisualTreeHelper.GetChild(control, 0) as UIElement)?.Opacity < 1, "Disabled themed " + name + " is not dimmed.");
        }
        var slider = new Slider { Style = build("Slider"), Width = 200 };
        slider.Measure(new Size(200, 40)); slider.Arrange(new Rect(0, 0, 200, 40)); slider.ApplyTemplate(); slider.UpdateLayout();
        Check(slider.Template.FindName("PART_Track", slider) is System.Windows.Controls.Primitives.Track track && track.Thumb != null,
            "Themed settings slider template lacks a working track and thumb.");
        slider.IsEnabled = false;
        Check(slider.Opacity < 1, "Disabled themed slider is not dimmed.");
    }

    private static void DispatcherAndShutdown()
    {
        using (var f = new Fixture())
        {
            Exception error = Task.Run(() => { try { Get(f.View, "Following"); return null; } catch (Exception ex) { return Unwrap(ex); } }).GetAwaiter().GetResult();
            Check(error is InvalidOperationException, "View model read outside owning dispatcher succeeded.");
            error = Task.Run(() => { try { Call(f.View, "TogglePlaybackAsync"); return null; } catch (Exception ex) { return Unwrap(ex); } }).GetAwaiter().GetResult();
            Check(error is InvalidOperationException && f.Backend.Launches.Count == 0, "Off-dispatcher transport touched model/backend.");
        }
        FakeBackend backend = new FakeBackend { HoldLaunch = true };
        object view = null; Task startup = null; Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var d = Dispatcher.CurrentDispatcher;
                view = Activator.CreateInstance(viewType, Members, null, new object[] { backend.Proxy,
                    Path.Combine(directory, "shutdown.txt"), Detailed, Concise, new Action(() => { }), new Action<Task>(t => { }) }, null);
                Void(view, "SetFrameAvailable", true, null);
                startup = Call(view, "TogglePlaybackAsync");
                ((IDisposable)view).Dispose();
                d.InvokeShutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Check(thread.Join(5000) && failure == null, "Shutdown fixture failed: " + failure);
        backend.Launches[0].Complete();
        PumpUntil(() => startup.IsCompleted && backend.Latest.Disposals == 1);
        Check(backend.Latest.Speeches.Count == 0 && backend.Launches[0].Token.IsCancellationRequested, "Dispatcher shutdown revived late launch.");
    }
}
