using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ReadAlongTrial;

internal static class ReadAlongNavigationTests
{
    private static int assertions;
    private static readonly string PreferenceFile = Path.Combine(Path.GetTempPath(), "ReadAlongPreference-" + Guid.NewGuid().ToString("N") + ".txt");
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    private static void VerifyOwnership()
    {
        Window owner = new Window { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            IntPtr hwnd = new WindowInteropHelper(owner).EnsureHandle();
            MethodInfo parse = typeof(Program).GetMethod("ParseOwner", BindingFlags.Static | BindingFlags.NonPublic);
            Check((IntPtr)parse.Invoke(null, new object[] { "0x" + hwnd.ToInt64().ToString("X") }) == hwnd, "Live owner handle was not parsed");
            foreach (string invalid in new[] { null, "", "1234", "0x", "0x0", "0xBADHEX", "0x7FFFFFFFFFFFFFFF" })
            {
                bool rejected = false;
                try { parse.Invoke(null, new object[] { invalid }); }
                catch (TargetInvocationException ex) { rejected = ex.InnerException is ArgumentException; }
                Check(rejected, "Invalid or stale owner accepted: " + invalid);
            }
            ReaderWindow owned = new ReaderWindow(null, null, hwnd, null, PreferenceFile);
            try
            {
                Field<SpeechSynthesizer>(owned, "speech").SetOutputToNull();
                IntPtr child = new WindowInteropHelper(owned).EnsureHandle();
                Check(!owned.Topmost, "Overlay is desktop-topmost");
                Check(!owned.ShowInTaskbar, "Owned overlay has a separate taskbar button");
                Check(GetWindow(child, 4) == hwnd && new WindowInteropHelper(owned).Owner == hwnd, "Native owner is not the selected window");
                Check((GetWindowLong(child, -20) & 8) == 0, "Native topmost style is enabled");
            }
            finally { owned.Close(); }
        }
        finally { owner.Close(); }
    }

    private static T Field<T>(ReaderWindow window, string name)
    {
        return (T)typeof(ReaderWindow).GetField(name, Private).GetValue(window);
    }

    private static void Call(ReaderWindow window, string name, params object[] arguments)
    {
        typeof(ReaderWindow).GetMethod(name, Private).Invoke(window, arguments);
    }

    private static void Check(bool result, string description)
    {
        assertions++;
        if (!result) throw new InvalidOperationException(description);
    }

    private static FrameworkElement Find(DependencyObject root, string id)
    {
        FrameworkElement element = root as FrameworkElement;
        if (element != null && AutomationProperties.GetAutomationId(element) == id) return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            FrameworkElement found = Find(VisualTreeHelper.GetChild(root, i), id);
            if (found != null) return found;
        }
        return null;
    }

    private static void VerifyLayout(ReaderWindow window, double width, double height)
    {
        Border shell = (Border)window.Content;
        shell.Measure(new Size(width, height)); shell.Arrange(new Rect(0, 0, width, height)); shell.UpdateLayout();
        string[] ids = { "CloseReader", "FollowSpeech", "PreviousParagraph", "NextParagraph", "PreviousSentence", "PlaySample", "NextSentence", "StopSpeech" };
        foreach (string id in ids)
        {
            Button button = Find(shell, id) as Button;
            Check(button != null, "Missing grouped control: " + id);
            Point topLeft = button.TranslatePoint(new Point(), shell);
            Check(topLeft.X >= 0 && topLeft.Y >= 0 && topLeft.X + button.ActualWidth <= width && topLeft.Y + button.ActualHeight <= height, "Control clipped: " + id);
            button.ApplyTemplate();
            Border surface = button.Template.FindName("Surface", button) as Border;
            Check(surface != null && surface.CornerRadius.TopLeft >= 8, "Default square button template remains: " + id);
            Check(!String.IsNullOrEmpty(AutomationProperties.GetName(button)) && button.ToolTip != null, "Grouped button has no accessible label: " + id);
        }
        FrameworkElement transport = Find(shell, "SentenceTransport");
        FrameworkElement rail = Find(shell, "ParagraphNavigation");
        Button play = Field<Button>(window, "play");
        Point playPosition = play.TranslatePoint(new Point(), shell);
        Check(Math.Abs(playPosition.X + play.ActualWidth / 2 - width / 2) < 1, "Playback button is not centered");
        Check(Find(transport, "PreviousSentence") != null && Find(transport, "NextSentence") != null, "Sentence buttons are not beside playback");
        Check(Find(rail, "PreviousParagraph") != null && Find(rail, "NextParagraph") != null && Find(transport, "PreviousParagraph") == null, "Paragraph controls are not separate");
        Check(Find(transport, "StopSpeech") == null && Find(transport, "FollowSpeech") == null, "Stop/Follow remain crowded with playback");
        Check(Field<RichTextBox>(window, "reader").ActualHeight > 70, "Grouped controls leave too little reading space");
        foreach (string id in new[] { "ReplyConcise", "ReplyDetailed" })
        {
            RadioButton choice = Find(shell, id) as RadioButton;
            Check(choice != null && !String.IsNullOrEmpty(AutomationProperties.GetName(choice)), "Missing accessible reply-version choice");
            Point topLeft = choice.TranslatePoint(new Point(), shell);
            Check(topLeft.X >= 0 && topLeft.Y >= 0 && topLeft.X + choice.ActualWidth <= width && topLeft.Y + choice.ActualHeight <= height, "Reply-version selector is clipped");
        }
    }

    private static void WriteReply(string path, params ReplyBlock[] blocks)
    {
        using (Stream output = File.Create(path)) new DataContractJsonSerializer(typeof(List<ReplyBlock>)).WriteObject(output, new List<ReplyBlock>(blocks));
    }

    private static void VerifyVersions()
    {
        string detailed = Path.GetTempFileName(), concise = Path.GetTempFileName(), status = Path.GetTempFileName();
        ReaderWindow window = null;
        try
        {
            WriteReply(detailed,
                new ReplyBlock { Id = "a", Kind = "paragraph", Text = "Detailed alpha begins here. More detail follows." },
                new ReplyBlock { Id = "b", Kind = "paragraph", Text = "Detailed bravo has extra words. Another detailed sentence." });
            WriteReply(concise,
                new ReplyBlock { Id = "b", Kind = "paragraph", Text = "Brief bravo." },
                new ReplyBlock { Id = "a", Kind = "paragraph", Text = "Brief alpha." });
            string originalDetailed = File.ReadAllText(detailed), originalConcise = File.ReadAllText(concise);
            window = new ReaderWindow(status, detailed, IntPtr.Zero, concise, PreferenceFile);
            Field<SpeechSynthesizer>(window, "speech").SetOutputToNull();
            Check(!Field<bool>(window, "concise") && Field<RadioButton>(window, "detailedChoice").IsChecked == true, "Default reply version is not Detailed");
            Check(Field<RadioButton>(window, "conciseChoice").IsEnabled, "Supplied Concise version is unavailable");
            VerifyLayout(window, 520, 370); VerifyLayout(window, 420, 280);
            List<Segment> detailedSegments = Field<List<Segment>>(window, "segments");
            int middle = detailedSegments[1].Spoken.IndexOf("extra", StringComparison.Ordinal);
            Call(window, "Seek", new ReadingPosition(1, middle), true);
            Prompt oldPrompt = Field<Prompt>(window, "activePrompt");
            Call(window, "SwitchVersion", true);
            Check(Field<string>(window, "phase") == "paused" && Field<Prompt>(window, "activePrompt") == null, "Speaking version switch did not pause/cancel");
            Check(Field<int>(window, "index") == 0 && Field<List<Segment>>(window, "segments")[0].Id == "b", "Version switch did not map by section ID");
            Check(Field<int>(window, "positionOffset") == 0 && Field<WordAnchor>(window, "activeWord").Display.Text == "Brief", "Version switch did not highlight the matching section start");
            Check(Field<RichTextBox>(window, "reader").Document == Field<FlowDocument>(window, "conciseDocument"), "Concise document was not displayed");
            Check(Field<RadioButton>(window, "conciseChoice").IsChecked == true && Field<RadioButton>(window, "detailedChoice").IsChecked == false, "Version selection state is incorrect");
            Check(File.ReadAllText(status).Contains("version=Concise"), "Status did not report Concise version");
            DateTime settled = DateTime.UtcNow.AddMilliseconds(300);
            PumpUntil(delegate { return DateTime.UtcNow >= settled; }, TimeSpan.FromSeconds(3));
            Check(Field<string>(window, "phase") == "paused" && Field<int>(window, "index") == 0 && Field<Prompt>(window, "activePrompt") == null, "Obsolete callbacks changed the selected version");

            ReaderWindow targetWindow = window;
            int progress = 0;
            Field<SpeechSynthesizer>(window, "speech").SpeakProgress += delegate(object sender, SpeakProgressEventArgs e)
            {
                targetWindow.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!Object.ReferenceEquals(e.Prompt, Field<Prompt>(targetWindow, "activePrompt"))) return;
                    List<Segment> current = Field<List<Segment>>(targetWindow, "segments");
                    int index = Field<int>(targetWindow, "index");
                    WordAnchor expected = current[index].FindWord(Field<int>(targetWindow, "promptOffset") + e.CharacterPosition);
                    if (expected == null) return;
                    Check(Object.ReferenceEquals(Field<WordAnchor>(targetWindow, "activeWord"), expected), "Version speech highlighted a word from another version");
                    progress++;
                }));
            };
            Call(window, "TogglePlayback");
            Check(Field<string>(window, "phase") == "speaking" && Field<int>(window, "promptOffset") == 0, "Play did not start the selected version");
            Prompt currentPrompt = Field<Prompt>(window, "activePrompt");
            Check(!Object.ReferenceEquals(oldPrompt, currentPrompt), "Version switch reused obsolete prompt");
            Call(window, "SwitchVersion", true);
            Check(Object.ReferenceEquals(currentPrompt, Field<Prompt>(window, "activePrompt")), "Selecting the current version restarted speech");
            PumpUntil(delegate { return Field<string>(targetWindow, "phase") == "finished"; }, TimeSpan.FromSeconds(15));
            Check(progress >= 4, "Selected-version speech progression not exercised");
            Call(window, "SwitchVersion", false);
            Check(Field<string>(window, "phase") == "paused" && Field<int>(window, "index") == 0 && !Field<bool>(window, "concise"), "Finished switch did not map the final section");
            Check(Field<RichTextBox>(window, "reader").Document == Field<FlowDocument>(window, "detailedDocument"), "Detailed document was not restored");
            Check(Field<FlowDocument>(window, "conciseDocument").Blocks.FirstBlock.Background == null, "Old version paragraph acquired unexpected highlighting");
            Call(window, "Start"); Call(window, "PauseResume");
            Call(window, "SwitchVersion", true);
            Check(Field<string>(window, "phase") == "paused" && Field<Prompt>(window, "activePrompt") == null, "Paused version switch resumed speech");
            Call(window, "Stop"); Call(window, "SwitchVersion", false);
            Check(Field<string>(window, "phase") == "paused", "Stopped version switch failed to await Play");
            for (int i = 0; i < 6; i++)
            {
                bool selected = i % 2 == 0;
                Call(window, "SwitchVersion", selected);
                Check(Field<bool>(window, "concise") == selected && Field<Prompt>(window, "activePrompt") == null, "Rapid switch lost state or started speech");
                Check(Field<List<Segment>>(window, "segments")[Field<int>(window, "index")].Id == "a", "Rapid switch lost matching section");
            }
            Check(originalDetailed == File.ReadAllText(detailed) && originalConcise == File.ReadAllText(concise), "Switching mutated source reply files");
            Check(File.ReadAllText(status).Contains("version=Detailed"), "Status did not return to Detailed");

            DocumentBuilder.ValidateVersions(detailedSegments, Field<List<Segment>>(window, "conciseSegments"));
            foreach (List<Segment> invalid in new[] { new List<Segment> { new Segment { Id = "a" } }, new List<Segment> { new Segment { Id = "a" }, new Segment { Id = "missing" } }, new List<Segment> { new Segment { Id = "a" }, new Segment { Id = "a" } } })
            {
                bool rejected = false;
                try { DocumentBuilder.ValidateVersions(detailedSegments, invalid); }
                catch (ArgumentException) { rejected = true; }
                Check(rejected, "Unmatched version sections accepted");
            }
        }
        finally
        {
            if (window != null) window.Close();
            File.Delete(detailed); File.Delete(concise); File.Delete(status);
        }
    }

    private static void VerifyTransitions()
    {
        ReaderWindow animated = new ReaderWindow(null, null, IntPtr.Zero, null, PreferenceFile) { Left = -10000, Top = -10000 };
        bool ended = false;
        animated.Closed += delegate { ended = true; };
        Field<SpeechSynthesizer>(animated, "speech").SetOutputToNull();
        try
        {
            animated.Show();
            Check(Field<string>(animated, "phase") == "speaking", "Animated entrance did not auto-play");
            PumpUntil(delegate { return animated.Opacity > 0.999 && ((TranslateTransform)((Border)animated.Content).RenderTransform).Y < 0.01; }, TimeSpan.FromSeconds(3));
            Check(((SolidColorBrush)((Border)animated.Content).Background).Color.A == 255, "Animation left pale yellow translucent");
            Call(animated, "TogglePlayback");
            Check(Field<string>(animated, "phase") == "paused" && AutomationProperties.GetName(Field<Button>(animated, "play")) == "Resume", "Combined button failed to pause");
            Call(animated, "TogglePlayback");
            Check(Field<string>(animated, "phase") == "speaking" && AutomationProperties.GetName(Field<Button>(animated, "play")) == "Pause", "Combined button failed to resume");
            animated.Close();
            if (SystemParameters.ClientAreaAnimation)
            {
                Check(!ended && Field<string>(animated, "phase") == "closing", "Close did not defer for animation");
                Check(Field<Prompt>(animated, "activePrompt") == null && !((UIElement)animated.Content).IsEnabled, "Close did not cancel speech and disable input immediately");
                animated.Close();
            }
            PumpUntil(delegate { return ended; }, TimeSpan.FromSeconds(3));
            Check(Field<string>(animated, "phase") == "closed", "Close animation did not finish cleanup");
        }
        finally { if (!ended) { typeof(ReaderWindow).GetField("allowClose", Private).SetValue(animated, true); animated.Close(); } }
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        DispatcherFrame frame = new DispatcherFrame();
        DateTime deadline = DateTime.UtcNow + timeout;
        DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += delegate { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Check(condition(), "Playback condition timed out");
    }

    private static void VerifyPreferences()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ReadAlongPreferences-" + Guid.NewGuid().ToString("N"));
        string preference = Path.Combine(directory, "settings", "version.txt");
        string detailed = Path.Combine(directory, "detailed.json"), concise = Path.Combine(directory, "concise.json");
        Directory.CreateDirectory(directory);
        ReaderWindow window = null;
        try
        {
            Check(!ReplyVersionPreference.Load(preference), "Missing preference did not default to Detailed");
            Check(!Directory.Exists(Path.GetDirectoryName(preference)), "Reading a missing preference created files");
            WriteReply(detailed, new ReplyBlock { Id = "a", Kind = "paragraph", Text = "Detailed first reply with more explanation." });
            WriteReply(concise, new ReplyBlock { Id = "a", Kind = "paragraph", Text = "Brief first reply." });
            window = new ReaderWindow(null, detailed, IntPtr.Zero, concise, preference);
            Field<SpeechSynthesizer>(window, "speech").SetOutputToNull();
            Call(window, "SwitchVersion", true);
            Check(ReplyVersionPreference.Load(preference), "Selecting Concise did not persist immediately");
            Check(Field<string>(window, "phase") == "paused" && Field<Prompt>(window, "activePrompt") == null, "Saving a choice started speech in the current reply");
            string saved = File.ReadAllText(preference);
            Call(window, "SwitchVersion", true);
            Check(File.ReadAllText(preference) == saved, "Same-choice selection changed the preference");
            window.Close(); window = null;

            WriteReply(detailed, new ReplyBlock { Id = "next", Kind = "paragraph", Text = "New detailed reply with a fresh section ID." });
            WriteReply(concise, new ReplyBlock { Id = "next", Kind = "paragraph", Text = "New concise reply." });
            VerifyReopenedProcess(detailed, concise, preference, "Concise");
            window = new ReaderWindow(null, detailed, IntPtr.Zero, null, preference);
            Field<SpeechSynthesizer>(window, "speech").SetOutputToNull();
            Check(!Field<bool>(window, "concise") && !Field<RadioButton>(window, "conciseChoice").IsEnabled, "Missing summary did not show Detailed");
            Check(Field<TextBlock>(window, "preferenceNotice").Visibility == Visibility.Visible, "Missing saved summary was not explained");
            Check(ReplyVersionPreference.Load(preference), "Missing summary overwrote the saved Concise choice");
            window.Close(); window = null;
            VerifyReopenedProcess(detailed, concise, preference, "Concise");

            window = new ReaderWindow(null, detailed, IntPtr.Zero, concise, preference);
            Field<SpeechSynthesizer>(window, "speech").SetOutputToNull();
            Check(Field<bool>(window, "concise"), "Concise was not restored before Loaded");
            Call(window, "SwitchVersion", false);
            Check(!ReplyVersionPreference.Load(preference), "Selecting Detailed did not replace Concise on disk");
            window.Close(); window = null;
            VerifyReopenedProcess(detailed, concise, preference, "Detailed");
            Check(Directory.GetFiles(Path.GetDirectoryName(preference), "*.tmp").Length == 0, "Atomic preference save left temporary files");

            File.WriteAllText(preference, "invalid preference");
            window = new ReaderWindow(null, detailed, IntPtr.Zero, concise, preference);
            Field<SpeechSynthesizer>(window, "speech").SetOutputToNull();
            Check(!Field<bool>(window, "concise") && Field<TextBlock>(window, "preferenceNotice").Text.Contains("Could not load"), "Malformed preference was silently ignored");
            Check(File.ReadAllText(preference) == "invalid preference", "Loading a bad preference silently overwrote it");
            Call(window, "SwitchVersion", true);
            Check(ReplyVersionPreference.Load(preference) && Field<TextBlock>(window, "preferenceNotice").Visibility == Visibility.Collapsed, "Explicit selection did not recover from a malformed preference");
            window.Close(); window = null;

            ReplyVersionPreference.Save(preference, false);
            window = new ReaderWindow(null, detailed, IntPtr.Zero, concise, preference);
            Field<SpeechSynthesizer>(window, "speech").SetOutputToNull();
            using (FileStream held = new FileStream(preference, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Call(window, "SwitchVersion", true);
                Check(Field<bool>(window, "concise") && Field<TextBlock>(window, "preferenceNotice").Text.Contains("Could not save"), "Save failure did not leave the current choice available with an explicit warning");
                Call(window, "Start");
                Check(Field<TextBlock>(window, "preferenceNotice").Visibility == Visibility.Visible, "Playback hid the persistence warning");
                Call(window, "Stop");
            }
            Check(!ReplyVersionPreference.Load(preference), "Failed save corrupted the last valid preference");
            Check(Directory.GetFiles(Path.GetDirectoryName(preference), "*.tmp").Length == 0, "Failed save left a temporary file");
            window.Close(); window = null;
            using (FileStream held = new FileStream(preference, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                window = new ReaderWindow(null, detailed, IntPtr.Zero, concise, preference);
                Field<SpeechSynthesizer>(window, "speech").SetOutputToNull();
                Check(!Field<bool>(window, "concise") && Field<TextBlock>(window, "preferenceNotice").Text.Contains("Could not load"), "Read failure was silently treated as a missing preference");
                window.Close(); window = null;
            }
        }
        finally
        {
            if (window != null) window.Close();
            File.Delete(detailed); File.Delete(concise); File.Delete(preference);
            if (Directory.Exists(Path.GetDirectoryName(preference))) Directory.Delete(Path.GetDirectoryName(preference));
            Directory.Delete(directory);
        }
    }

    private static void VerifyReopenedProcess(string detailed, string concise, string preference, string expected)
    {
        ProcessStartInfo start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
            "--verify-reopen \"" + detailed + "\" \"" + concise + "\" \"" + preference + "\" " + expected)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using (Process child = Process.Start(start))
        {
            Check(child.WaitForExit(15000), "Reopened reader process did not finish");
            string output = child.StandardOutput.ReadToEnd(), error = child.StandardError.ReadToEnd();
            Check(child.ExitCode == 0 && output.Contains("PASS: reopened " + expected), "Cross-process preference or auto-play failed: " + output + error);
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--speech-worker-probe") return SpeechWorkerRegressionTests.Run();
        if (args.Length == 2 && args[0] == "--toolbar") return ToolbarRegressionTests.Run(args[1]);
        if (args.Length == 2 && args[0] == "--bridge-probe") return BridgePocRegressionTests.Run(args[1]);
        if (args.Length == 5 && args[0] == "--verify-reopen")
        {
            ReaderWindow reopened = new ReaderWindow(null, args[1], IntPtr.Zero, args[2], args[3]) { Left = -10000, Top = -10000, ShowInTaskbar = false };
            try
            {
                Field<SpeechSynthesizer>(reopened, "speech").SetOutputToNull();
                bool expected = args[4] == "Concise";
                Check(Field<bool>(reopened, "concise") == expected, "Reopened process did not restore its mode");
                Check(Field<RadioButton>(reopened, expected ? "conciseChoice" : "detailedChoice").IsChecked == true, "Reopened selector disagreed with the document");
                Check(Field<RichTextBox>(reopened, "reader").Document == Field<FlowDocument>(reopened, expected ? "conciseDocument" : "detailedDocument"), "Reopened document was not the selected version");
                reopened.Show();
                Check(Field<string>(reopened, "phase") == "speaking" && Field<Prompt>(reopened, "activePrompt") != null, "Restored version did not auto-play on opening");
                Check(Field<int>(reopened, "index") == 0 && Field<int>(reopened, "promptOffset") == 0, "New reply inherited the previous reply position");
                Check(Field<List<Segment>>(reopened, "segments")[0].Spoken.StartsWith(expected ? "New concise" : "New detailed"), "Reopened speech used the old reply or wrong version");
                Console.WriteLine("PASS: reopened " + args[4] + "; auto-play of fresh content verified.");
                return 0;
            }
            finally { typeof(ReaderWindow).GetField("allowClose", Private).SetValue(reopened, true); reopened.Close(); }
        }
        try { return Run(); }
        finally { File.Delete(PreferenceFile); }
    }

    private static int Run()
    {
        VerifyOwnership();
        VerifyTransitions();
        VerifyVersions();
        VerifyPreferences();
        ReaderWindow window = new ReaderWindow(null, null, IntPtr.Zero, null, PreferenceFile);
        try
        {
            SpeechSynthesizer speech = Field<SpeechSynthesizer>(window, "speech");
            speech.SetOutputToNull();
            Check(((SolidColorBrush)((Border)window.Content).Background).Color.A == 255, "Panel is not opaque");
            Check(((SolidColorBrush)((Border)window.Content).Background).Color == Color.FromRgb(255, 249, 207), "Pale yellow changed");
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Check(Field<string>(window, "phase") == "speaking" && Field<Prompt>(window, "activePrompt") != null, "Panel did not auto-play on load");
            Call(window, "Stop");
            Check(!Field<RadioButton>(window, "conciseChoice").IsEnabled && Field<RadioButton>(window, "detailedChoice").IsChecked == true, "Legacy sample silently offers a missing Concise version");
            VerifyLayout(window, 520, 370);
            VerifyLayout(window, 420, 280);

            FlowDocument document = new FlowDocument();
            List<Segment> segments = new List<Segment>();
            segments.Add(DocumentBuilder.Add(document, "first", "paragraph", "Alpha one. Bravo two. Charlie three.", 0));
            segments.Add(DocumentBuilder.Add(document, "second", "paragraph", "Delta four. Echo five.", 0));
            Field<RichTextBox>(window, "reader").Document = document;
            typeof(ReaderWindow).GetField("segments", Private).SetValue(window, segments);
            int bravo = segments[0].Spoken.IndexOf("Bravo", StringComparison.Ordinal);
            int charlie = segments[0].Spoken.IndexOf("Charlie", StringComparison.Ordinal);
            int two = segments[0].Spoken.IndexOf("two", StringComparison.Ordinal);
            int echo = segments[1].Spoken.IndexOf("Echo", StringComparison.Ordinal);

            Call(window, "Seek", new ReadingPosition(0, bravo), false);
            Check(Field<Prompt>(window, "activePrompt") == null && Field<string>(window, "phase") == "stopped", "Selection started speech");
            Check(Field<WordAnchor>(window, "activeWord").Display.Text == "Bravo", "Selection highlight is wrong");
            Call(window, "Navigate", 1, false);
            Check(Field<int>(window, "positionOffset") == charlie, "Forward sentence did not seek");
            Call(window, "Navigate", 1, true);
            Check(Field<int>(window, "index") == 1 && Field<int>(window, "positionOffset") == 0, "Forward paragraph did not seek");
            Call(window, "Navigate", -1, false);
            Check(Field<int>(window, "index") == 0 && Field<int>(window, "positionOffset") == charlie, "Reverse sentence did not cross paragraph");
            Call(window, "Stop");
            Check(Field<int>(window, "positionOffset") == charlie, "Stop discarded selected position");

            Call(window, "Start");
            Check(Field<int>(window, "promptOffset") == charlie, "Play ignored selection");
            Call(window, "PauseResume");
            Check(Field<string>(window, "phase") == "paused" && speech.State == SynthesizerState.Paused, "Pause failed");
            Check(Field<Button>(window, "play").IsEnabled, "Play cannot resume a paused prompt");
            Check(AutomationProperties.GetName(Field<Button>(window, "play")) == "Resume", "Combined icon semantics are stale");
            Call(window, "Navigate", -1, false);
            Check(Field<string>(window, "phase") == "paused" && Field<int>(window, "positionOffset") == bravo, "Paused seek did not retain paused mode");
            Check(Field<Prompt>(window, "activePrompt") == null && AutomationProperties.GetName(Field<Button>(window, "play")) == "Resume", "Paused seek retained obsolete prompt or wrong playback icon");
            Check(speech.State != SynthesizerState.Paused, "Cancelled paused synthesizer was not released");

            Call(window, "Seek", new ReadingPosition(0, two), false);
            int verifiedProgress = 0;
            bool sawFirstSlicedWord = false;
            bool sawNextParagraph = false;
            speech.SpeakProgress += delegate(object sender, SpeakProgressEventArgs e)
            {
                window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!Object.ReferenceEquals(e.Prompt, Field<Prompt>(window, "activePrompt"))) return;
                    int segment = Field<int>(window, "index");
                    int originalOffset = Field<int>(window, "promptOffset") + e.CharacterPosition;
                    WordAnchor expected = segments[segment].FindWord(originalOffset);
                    if (expected == null) return;
                    Check(Object.ReferenceEquals(Field<WordAnchor>(window, "activeWord"), expected), "Live sliced speech highlighted wrong word");
                    Check(Field<int>(window, "positionOffset") == expected.Start, "Live navigation position is stale");
                    verifiedProgress++;
                    if (segment == 0 && originalOffset == two) sawFirstSlicedWord = true;
                    if (segment == 1) sawNextParagraph = true;
                }));
            };
            Call(window, "Start");
            PumpUntil(delegate { return Field<string>(window, "phase") == "finished"; }, TimeSpan.FromSeconds(15));
            Check(verifiedProgress > 2 && sawFirstSlicedWord && sawNextParagraph, "Did not verify sliced progress and paragraph continuation");
            Check(Field<Button>(window, "play").IsEnabled && AutomationProperties.GetName(Field<Button>(window, "play")) == "Play", "Completion controls are wrong");

            Call(window, "Start");
            Check(Field<int>(window, "index") == 0 && Field<int>(window, "promptOffset") == 0, "Finished playback did not restart");
            Prompt previous = Field<Prompt>(window, "activePrompt");
            Call(window, "Seek", new ReadingPosition(1, echo), true);
            Check(!Object.ReferenceEquals(previous, Field<Prompt>(window, "activePrompt")), "Playing seek reused the old prompt");
            Check(Field<string>(window, "phase") == "speaking" && Field<int>(window, "promptOffset") == echo, "Playing seek did not continue from target");
            Call(window, "PauseResume");
            Call(window, "Start");
            Check(Field<string>(window, "phase") == "speaking" && AutomationProperties.GetName(Field<Button>(window, "play")) == "Pause", "Play failed to resume paused speech");
            Call(window, "Stop");
            DateTime settled = DateTime.UtcNow.AddMilliseconds(500);
            PumpUntil(delegate { return DateTime.UtcNow >= settled; }, TimeSpan.FromSeconds(3));
            Check(Field<string>(window, "phase") == "stopped" && Field<int>(window, "index") == 1 && Field<int>(window, "positionOffset") == echo, "Cancelled callbacks moved stopped position");
            Check(Field<Prompt>(window, "activePrompt") == null && Field<Button>(window, "play").IsEnabled, "Stop left speech active");
            Console.WriteLine("PASS: " + assertions + " playback/state assertions; " + verifiedProgress + " live progress mappings; speech output suppressed.");
            return 0;
        }
        finally { window.Close(); }
    }
}