using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ReadAlongTrial
{
    public sealed class WordAnchor
    {
        public int Start;
        public int Length;
        public Run Display;
    }

    public sealed class Segment
    {
        public string Id;
        public string Spoken;
        public Paragraph Paragraph;
        public readonly List<WordAnchor> Words = new List<WordAnchor>();
        public readonly List<int> SentenceStarts = new List<int>();

        public WordAnchor FindWord(int position)
        {
            foreach (WordAnchor word in Words)
                if (position >= word.Start && position < word.Start + word.Length)
                    return word;
            return null;
        }
    }

    public struct ReadingPosition
    {
        public readonly int Segment;
        public readonly int Offset;
        public ReadingPosition(int segment, int offset) { Segment = segment; Offset = offset; }
    }

    public static class ReadingNavigation
    {
        public static ReadingPosition Move(List<Segment> segments, ReadingPosition current, int direction, bool paragraph)
        {
            if (direction != -1 && direction != 1) throw new ArgumentOutOfRangeException("direction");
            if (current.Segment < 0 || current.Segment >= segments.Count) throw new ArgumentOutOfRangeException("current");
            Segment segment = segments[current.Segment];
            if (current.Offset < 0 || current.Offset >= segment.Spoken.Length) throw new ArgumentOutOfRangeException("current");
            if (paragraph)
            {
                int target = current.Segment + direction;
                return target < 0 || target >= segments.Count ? current : new ReadingPosition(target, 0);
            }
            int sentence = 0;
            while (sentence + 1 < segment.SentenceStarts.Count && segment.SentenceStarts[sentence + 1] <= current.Offset) sentence++;
            int next = sentence + direction;
            if (next >= 0 && next < segment.SentenceStarts.Count)
                return new ReadingPosition(current.Segment, segment.SentenceStarts[next]);
            int adjacent = current.Segment + direction;
            if (adjacent < 0 || adjacent >= segments.Count) return current;
            List<int> starts = segments[adjacent].SentenceStarts;
            return new ReadingPosition(adjacent, direction > 0 ? 0 : starts[starts.Count - 1]);
        }

        public static ReadingPosition? FromTextPointer(List<Segment> segments, TextPointer pointer)
        {
            if (pointer == null) return null;
            for (int i = 0; i < segments.Count; i++)
            {
                Segment segment = segments[i];
                if (pointer.CompareTo(segment.Paragraph.ContentStart) < 0 || pointer.CompareTo(segment.Paragraph.ContentEnd) > 0) continue;
                foreach (WordAnchor word in segment.Words)
                    if (pointer.CompareTo(word.Display.ContentEnd) <= 0) return new ReadingPosition(i, word.Start);
                if (segment.Words.Count > 0) return new ReadingPosition(i, segment.Words[segment.Words.Count - 1].Start);
            }
            return null;
        }
    }

    [DataContract]
    public sealed class ReplyBlock
    {
        [DataMember(Name = "id", IsRequired = true)] public string Id;
        [DataMember(Name = "kind", IsRequired = true)] public string Kind;
        [DataMember(Name = "text", IsRequired = true)] public string Text;
        [DataMember(Name = "number")] public int Number;
    }

    public static class DocumentBuilder
    {
        public static void ValidateVersions(List<Segment> detailed, List<Segment> concise)
        {
            if (detailed.Count != concise.Count) throw new ArgumentException("Concise and Detailed must have matching section IDs.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Segment segment in detailed) ids.Add(segment.Id);
            foreach (Segment segment in concise)
                if (!ids.Remove(segment.Id)) throw new ArgumentException("Concise section has no matching Detailed section: " + segment.Id);
        }

        public static List<Segment> LoadReply(FlowDocument document, Stream input)
        {
            byte[] data;
            using (MemoryStream buffer = new MemoryStream()) { input.CopyTo(buffer); data = buffer.ToArray(); }
            Encoding encoding = data.Length > 1 && data[0] == 0 ? Encoding.BigEndianUnicode :
                data.Length > 1 && data[1] == 0 ? Encoding.Unicode : Encoding.UTF8;
            // The contract reader validates JSON syntax but ignores trailing content.
            ValidateReplyBoundary(encoding.GetString(data));
            List<ReplyBlock> blocks;
            using (MemoryStream validated = new MemoryStream(data))
                blocks = (List<ReplyBlock>)new DataContractJsonSerializer(typeof(List<ReplyBlock>)).ReadObject(validated);
            if (blocks == null || blocks.Count == 0) throw new ArgumentException("The reply must contain readable blocks.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            List<Segment> segments = new List<Segment>();
            foreach (ReplyBlock block in blocks)
            {
                if (block == null) throw new ArgumentException("A reply block cannot be null.");
                if (!ids.Add(block.Id)) throw new ArgumentException("Duplicate reply anchor: " + block.Id);
                segments.Add(Add(document, block.Id, block.Kind, block.Text, block.Number));
            }
            return segments;
        }

        private static void ValidateReplyBoundary(string json)
        {
            bool started = false, complete = false, quoted = false, escaped = false;
            int depth = 0;
            foreach (char value in json)
            {
                if (!started || complete)
                {
                    if (value == ' ' || value == '\t' || value == '\r' || value == '\n') continue;
                    if (complete) throw new ArgumentException("Unexpected content after the reply array.");
                    if (value != '[') throw new ArgumentException("The reply must be a JSON array.");
                    started = true; depth = 1; continue;
                }
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (value == '\\') escaped = true;
                    else if (value == '"') quoted = false;
                    continue;
                }
                if (value == '"') quoted = true;
                else if (value == '[' || value == '{') depth++;
                else if (value == ']' || value == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        if (value != ']') throw new ArgumentException("The reply array is not closed correctly.");
                        complete = true;
                    }
                }
            }
            if (!complete) throw new ArgumentException("The reply must contain one complete JSON array.");
        }

        public static Segment Add(FlowDocument document, string id, string kind, string text, int number)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("An anchor ID is required.");
            if (String.IsNullOrWhiteSpace(text)) throw new ArgumentException("Readable text is required in " + id);
            if (kind != "heading" && kind != "paragraph" && kind != "bullet" && kind != "step")
                throw new ArgumentException("Unsupported content kind: " + kind);
            Segment segment = new Segment { Id = id, Paragraph = new Paragraph() };
            Paragraph paragraph = segment.Paragraph;
            paragraph.Margin = new Thickness(0, kind == "heading" ? 10 : 0, 0, 9);
            paragraph.LineHeight = 18;
            if (kind == "heading") { paragraph.FontSize = 14; paragraph.FontWeight = FontWeights.Bold; }
            string spokenPrefix = "";
            if (kind == "bullet" || kind == "step")
            {
                if (kind == "step" && number < 1) throw new ArgumentOutOfRangeException("number");
                string marker = kind == "step" ? number.ToString(CultureInfo.InvariantCulture) + ". " : "\u2022 ";
                Run markerRun = new Run(marker) { Foreground = Brushes.SteelBlue, FontWeight = FontWeights.Bold };
                paragraph.Inlines.Add(markerRun);
                paragraph.Margin = new Thickness(14, 0, 0, 8);
                if (kind == "step")
                {
                    spokenPrefix = "Step " + number.ToString(CultureInfo.InvariantCulture) + ". ";
                    segment.Words.Add(new WordAnchor { Start = 0, Length = spokenPrefix.Length, Display = markerRun });
                }
            }
            StringBuilder spoken = new StringBuilder(spokenPrefix);
            bool bold = false;
            foreach (string part in Regex.Split(text, "(\\*\\*)"))
            {
                if (part == "**") { bold = !bold; continue; }
                foreach (Match token in Regex.Matches(part, "\\s+|\\S+"))
                {
                    Run run = new Run(token.Value);
                    if (bold) run.FontWeight = FontWeights.Bold;
                    paragraph.Inlines.Add(run);
                    if (!String.IsNullOrWhiteSpace(token.Value))
                        segment.Words.Add(new WordAnchor { Start = spoken.Length, Length = token.Length, Display = run });
                    spoken.Append(token.Value);
                }
            }
            if (bold) throw new ArgumentException("Unclosed bold marker in " + id);
            segment.Spoken = spoken.ToString();
            if (segment.Words.Count == 0) throw new ArgumentException("Readable text is required in " + id);
            segment.SentenceStarts.Add(0);
            // Split the body only: the spoken "Step N." prefix belongs to its first sentence.
            foreach (Match ending in Regex.Matches(segment.Spoken.Substring(spokenPrefix.Length), "[.!?]+[\"'\u2019\u201D)\\]]*(?=\\s|$)"))
            {
                int end = spokenPrefix.Length + ending.Index + ending.Length;
                string before = segment.Spoken.Substring(spokenPrefix.Length, ending.Index + ending.Length);
                if (Regex.IsMatch(before, "\\b(?:Mr|Mrs|Ms|Dr|Prof|Sr|Jr|St|vs|e\\.g|i\\.e)\\.$", RegexOptions.IgnoreCase)) continue;
                foreach (WordAnchor word in segment.Words)
                {
                    if (word.Start < end) continue;
                    if (segment.SentenceStarts[segment.SentenceStarts.Count - 1] != word.Start) segment.SentenceStarts.Add(word.Start);
                    break;
                }
            }
            document.Blocks.Add(paragraph);
            return segment;
        }

        public static List<Segment> Sample(FlowDocument document)
        {
            List<Segment> segments = new List<Segment>();
            segments.Add(Add(document, "intro-title", "heading", "A voice conversation, with room to read", 0));
            segments.Add(Add(document, "intro", "paragraph", "This is a disposable read-along experiment. The panel owns its text, speech, highlighting, and scrolling. Copilot Chat can continue updating independently.", 0));
            segments.Add(Add(document, "watch-title", "heading", "What to watch", 0));
            segments.Add(Add(document, "watch-1", "bullet", "The highlighted word should follow the voice, including **emphasized words**.", 0));
            segments.Add(Add(document, "watch-2", "bullet", "Each bullet has its own anchor. Moving to a new item must not highlight an unrelated sentence.", 0));
            segments.Add(Add(document, "watch-3", "bullet", "Paragraph spacing should make the content comfortable to scan while listening.", 0));
            segments.Add(Add(document, "steps-title", "heading", "Try the controls", 0));
            segments.Add(Add(document, "step-1", "step", "Press the blue Pause button. The speech and highlighted position should stop together. The same button becomes Resume. Press it to continue.", 1));
            segments.Add(Add(document, "step-2", "step", "Scroll upward with your mouse wheel. Speech continues, but the panel stops following automatically.", 2));
            segments.Add(Add(document, "step-3", "step", "Press Follow speech. The panel returns to the current spoken word, not the beginning of the reply.", 3));
            segments.Add(Add(document, "step-4", "step", "Press Stop to keep your place. Use the sentence arrows to skip or repeat. Paragraph arrows visit headings and list items too. Click a word to choose a starting point, then press Play.", 4));
            segments.Add(Add(document, "scroll-title", "heading", "An independent viewport", 0));
            segments.Add(Add(document, "scroll-body", "paragraph", "By now the sample is longer than the panel. Automatic scrolling should keep the active word visible without selecting text or taking keyboard focus from your editor.", 0));
            segments.Add(Add(document, "limits-title", "heading", "Prototype boundaries", 0));
            segments.Add(Add(document, "limits", "paragraph", "This first part does not mirror chat, process tables, or receive new messages. Those features are not silently simulated. We are testing the reading experience first.", 0));
            segments.Add(Add(document, "finish", "paragraph", "That is the end of the sample. Tell me whether the timing, formatting, and scrolling feel comfortable.", 0));
            return segments;
        }
    }

    public static class ReplyVersionPreference
    {
        public static bool Load(string path)
        {
            string value;
            try { value = File.ReadAllText(path, Encoding.UTF8).Trim(); }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            if (value == "Concise") return true;
            if (value == "Detailed") return false;
            throw new InvalidDataException("Saved reading mode must be Concise or Detailed.");
        }

        public static void Save(string path, bool concise)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(pending, concise ? "Concise" : "Detailed", new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally { File.Delete(pending); }
        }
    }

    public sealed class ReaderWindow : Window
    {
        private readonly SpeechSynthesizer speech = new SpeechSynthesizer();
        private readonly RichTextBox reader = new RichTextBox();
        private readonly TextBlock status = new TextBlock();
        private readonly TextBlock preferenceNotice = new TextBlock { Visibility = Visibility.Collapsed, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed };
        private readonly string preferencePath;
        private readonly Button play = new Button();
        private readonly Button followButton = new Button();
        private readonly RadioButton conciseChoice;
        private readonly RadioButton detailedChoice;
        private readonly FlowDocument detailedDocument;
        private readonly FlowDocument conciseDocument;
        private readonly List<Segment> detailedSegments;
        private readonly List<Segment> conciseSegments;
        private bool concise;
        private readonly TranslateTransform entranceOffset = new TranslateTransform();
        private List<Segment> segments;
        private Prompt activePrompt;
        private WordAnchor activeWord;
        private int index;
        private int positionOffset;
        private int promptOffset;
        private bool following = true;
        private bool closed;
        private bool closing;
        private bool allowClose;
        private readonly string statusPath;
        private readonly DispatcherTimer heartbeat;
        private string phase = "ready";

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        public ReaderWindow(string output, string replyPath = null, IntPtr owner = default(IntPtr), string concisePath = null, string preferenceFile = null)
        {
            if (concisePath != null && replyPath == null) throw new ArgumentException("A Concise reply requires a matching Detailed reply.");
            statusPath = output;
            preferencePath = preferenceFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatVoice", "reader-version.txt");
            Title = replyPath == null ? "Read-along trial - Sample" : "Read-along trial - Reply";
            AutomationProperties.SetAutomationId(this, "ReadAlongTrialWindow");
            Width = 520; Height = 370; Left = 35; Top = 120;
            MinWidth = 420; MinHeight = 280;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = false; ShowInTaskbar = owner == IntPtr.Zero; Topmost = false;
            if (owner != IntPtr.Zero) new WindowInteropHelper(this).Owner = owner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Focusable = false;

            Border shell = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 249, 207)), BorderBrush = new SolidColorBrush(Color.FromRgb(219, 209, 157)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), RenderTransform = entranceOffset };
            Opacity = SystemParameters.ClientAreaAnimation ? 0 : 1;
            Content = shell;
            DockPanel layout = new DockPanel(); shell.Child = layout;
            DockPanel header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
            Button close = MakeButton("Close", "CloseReader", delegate { Close(); });
            DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            ConfigureButton(followButton, "Following speech", "FollowSpeech", delegate { following = !following; UpdateFollow(); Follow(); WriteStatus(); });
            followButton.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(followButton, Dock.Right); header.Children.Add(followButton);
            TextBlock title = new TextBlock { Text = "Read-along trial", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center };
            Border dragHandle = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Child = title, ToolTip = "Drag heading to move" };
            AutomationProperties.SetAutomationId(dragHandle, "DragHeading");
            header.Children.Add(dragHandle);
            dragHandle.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

            StackPanel choices = new StackPanel { Orientation = Orientation.Horizontal };
            conciseChoice = MakeVersionChoice("Concise", "ReplyConcise", false, delegate { SwitchVersion(true); });
            detailedChoice = MakeVersionChoice("Detailed", "ReplyDetailed", true, delegate { SwitchVersion(false); });
            choices.Children.Add(conciseChoice); choices.Children.Add(detailedChoice);
            Border versionStrip = new Border { Background = new SolidColorBrush(Color.FromRgb(245, 237, 191)), CornerRadius = new CornerRadius(10), Padding = new Thickness(2), Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, Child = choices };
            DockPanel.SetDock(versionStrip, Dock.Top); layout.Children.Add(versionStrip);

            StackPanel bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
            Grid controls = new Grid { Margin = new Thickness(0, 10, 0, 6) }; bottom.Children.Add(controls);
            controls.ColumnDefinitions.Add(new ColumnDefinition());
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition());
            StackPanel transport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(transport, "SentenceTransport");
            Grid.SetColumn(transport, 1); controls.Children.Add(transport);
            Button previous = MakeButton("Previous sentence", "PreviousSentence", delegate { Navigate(-1, false); });
            previous.Margin = new Thickness(0, 0, 10, 0); transport.Children.Add(previous);
            ConfigureButton(play, "Play", "PlaySample", delegate { TogglePlayback(); });
            play.Width = 42; play.Height = 42; transport.Children.Add(play);
            Button next = MakeButton("Next sentence", "NextSentence", delegate { Navigate(1, false); });
            next.Margin = new Thickness(10, 0, 0, 0); transport.Children.Add(next);
            Button stop = MakeButton("Stop", "StopSpeech", delegate { Stop(); });
            stop.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(stop, 2); controls.Children.Add(stop);
            status.Foreground = Brushes.DarkSlateGray; status.FontSize = 11; status.TextWrapping = TextWrapping.Wrap;
            AutomationProperties.SetAutomationId(status, "PlaybackStatus"); bottom.Children.Add(status);
            AutomationProperties.SetAutomationId(preferenceNotice, "PreferenceNotice"); bottom.Children.Add(preferenceNotice);

            reader.IsReadOnly = true; reader.IsReadOnlyCaretVisible = false; reader.Focusable = false;
            reader.BorderThickness = new Thickness(0); reader.Background = Brushes.Transparent;
            reader.Foreground = Brushes.Black; reader.FontFamily = new FontFamily("Segoe UI"); reader.FontSize = 12;
            reader.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            reader.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            reader.Document = new FlowDocument { PagePadding = new Thickness(2, 0, 15, 5), FontSize = 12, FontFamily = new FontFamily("Segoe UI") };
            AutomationProperties.SetAutomationId(reader, "ReadAlongDocument");
            if (replyPath == null) segments = DocumentBuilder.Sample(reader.Document);
            else
            {
                using (Stream input = File.OpenRead(replyPath)) segments = DocumentBuilder.LoadReply(reader.Document, input);
            }
            detailedDocument = reader.Document; detailedSegments = segments;
            if (concisePath != null)
            {
                conciseDocument = new FlowDocument { PagePadding = new Thickness(2, 0, 15, 5), FontSize = 12, FontFamily = new FontFamily("Segoe UI") };
                using (Stream input = File.OpenRead(concisePath)) conciseSegments = DocumentBuilder.LoadReply(conciseDocument, input);
                DocumentBuilder.ValidateVersions(detailedSegments, conciseSegments);
            }
            conciseChoice.IsEnabled = conciseSegments != null;
            if (!conciseChoice.IsEnabled) conciseChoice.ToolTip = "No Concise version supplied for this reply.";
            try
            {
                bool preferredConcise = ReplyVersionPreference.Load(preferencePath);
                concise = preferredConcise && conciseSegments != null;
                if (concise)
                {
                    segments = conciseSegments; reader.Document = conciseDocument;
                }
                if (preferredConcise && !concise)
                    ShowPreferenceNotice("No Concise version supplied. Showing Detailed; your saved Concise choice is unchanged.");
            }
            catch (InvalidDataException ex) { ShowPreferenceNotice("Could not load reading mode; using Detailed for this reply: " + ex.Message); }
            catch (IOException ex) { ShowPreferenceNotice("Could not load reading mode; using Detailed for this reply: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { ShowPreferenceNotice("Could not load reading mode; using Detailed for this reply: " + ex.Message); }
            conciseChoice.IsChecked = concise; detailedChoice.IsChecked = !concise;
            reader.PreviewMouseWheel += delegate { following = false; UpdateFollow(); WriteStatus(); };
            reader.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnReaderMouseDown), true);
            DockPanel readingArea = new DockPanel(); layout.Children.Add(readingArea);
            StackPanel paragraphRail = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            AutomationProperties.SetAutomationId(paragraphRail, "ParagraphNavigation");
            DockPanel.SetDock(paragraphRail, Dock.Right); readingArea.Children.Add(paragraphRail);
            Button up = MakeButton("Previous paragraph", "PreviousParagraph", delegate { Navigate(-1, true); });
            up.Margin = new Thickness(0, 0, 0, 8); paragraphRail.Children.Add(up);
            paragraphRail.Children.Add(MakeButton("Next paragraph", "NextParagraph", delegate { Navigate(1, true); }));
            readingArea.Children.Add(reader);
            SizeChanged += delegate { if (following) Dispatcher.BeginInvoke(new Action(Follow), DispatcherPriority.Background); };

            SourceInitialized += delegate
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                const int styleIndex = -20;
                SetWindowLong(hwnd, styleIndex, GetWindowLong(hwnd, styleIndex) | 0x08000000);
                HwndSource.FromHwnd(hwnd).AddHook(WindowHook);
            };
            speech.SpeakProgress += OnProgress;
            speech.SpeakCompleted += OnCompleted;
            speech.SetOutputToDefaultAudioDevice();
            UpdatePlaybackButton(); UpdateFollow();
            status.Text = "Ready. Click a word or use the arrows, then Play. Uses your Windows default voice.";
            heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            heartbeat.Tick += delegate { WriteStatus(); };
            Loaded += delegate { AnimateEntrance(); heartbeat.Start(); Start(); };
            Closing += OnClosing;
            Closed += delegate { closed = true; heartbeat.Stop(); activePrompt = null; speech.SpeakAsyncCancelAll(); speech.Dispose(); phase = "closed"; WriteStatus(); };
        }

        private void AnimateEntrance()
        {
            if (!SystemParameters.ClientAreaAnimation) { Opacity = 1; return; }
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
            Opacity = 1;
            entranceOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (allowClose || !IsVisible || !SystemParameters.ClientAreaAnimation) return;
            e.Cancel = true;
            if (closing) return;
            closing = true; CancelSpeech(); heartbeat.Stop(); phase = "closing"; WriteStatus();
            ((UIElement)Content).IsEnabled = false;
            DoubleAnimation fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
            fade.Completed += delegate { allowClose = true; Close(); };
            entranceOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(entranceOffset.Y, 10, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            BeginAnimation(OpacityProperty, fade);
        }

        private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0021) { handled = true; return new IntPtr(3); }
            return IntPtr.Zero;
        }

        private static Button MakeButton(string text, string id, Action action)
        {
            Button b = new Button(); ConfigureButton(b, text, id, action); return b;
        }

        private static void ConfigureButton(Button b, string text, string id, Action action)
        {
            SetButtonLabel(b, text);
            ApplyButtonTheme(b, id);
            b.Click += delegate { action(); };
        }

        private static RadioButton MakeVersionChoice(string text, string id, bool selected, Action action)
        {
            RadioButton button = new RadioButton { GroupName = "ReplyVersion", Content = text, IsChecked = selected, ToolTip = text };
            ApplyButtonTheme(button, id);
            button.Width = 82; button.Height = 28; button.FontSize = 12; button.FontWeight = FontWeights.SemiBold;
            AutomationProperties.SetName(button, text);
            ToolTipService.SetShowOnDisabled(button, true);
            button.Checked += delegate { action(); };
            return button;
        }

        private static void ApplyButtonTheme(ButtonBase b, string id)
        {
            b.Focusable = false; b.Cursor = Cursors.Hand; b.Padding = new Thickness(6); b.Margin = new Thickness(0);
            b.Width = 32; b.Height = 32; b.VerticalAlignment = VerticalAlignment.Center;
            bool primary = id == "PlaySample";
            b.Background = primary ? new SolidColorBrush(Color.FromRgb(36, 87, 181)) : Brushes.Transparent;
            b.Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(62, 67, 63));
            b.BorderThickness = new Thickness(0);
            FrameworkElementFactory surface = new FrameworkElementFactory(typeof(Border));
            surface.Name = "Surface"; surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(primary ? 21 : 8));
            surface.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            surface.AppendChild(content);
            ControlTemplate template = new ControlTemplate(b.GetType()) { VisualTree = surface };
            Trigger hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, primary ? new SolidColorBrush(Color.FromRgb(27, 68, 148)) :
                id == "CloseReader" ? new SolidColorBrush(Color.FromRgb(250, 220, 207)) : new SolidColorBrush(Color.FromRgb(237, 228, 180)), "Surface"));
            template.Triggers.Add(hover);
            Trigger pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.65, "Surface")); template.Triggers.Add(pressed);
            Trigger disabled = new Trigger { Property = IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35, "Surface")); template.Triggers.Add(disabled);
            if (b is RadioButton)
            {
                Trigger selected = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                selected.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(224, 234, 249)), "Surface"));
                selected.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(36, 87, 181))));
                template.Triggers.Add(selected);
            }
            b.Template = template;
            AutomationProperties.SetAutomationId(b, id);
        }

        private void SwitchVersion(bool useConcise)
        {
            if (closing || closed || concise == useConcise) return;
            if (useConcise && conciseSegments == null) throw new InvalidOperationException("No Concise version supplied for this reply.");
            string section = segments[index].Id;
            CancelSpeech(); ClearHighlight();
            concise = useConcise;
            segments = concise ? conciseSegments : detailedSegments;
            reader.Document = concise ? conciseDocument : detailedDocument;
            index = segments.FindIndex(delegate(Segment segment) { return segment.Id == section; });
            positionOffset = segments[index].Words[0].Start; promptOffset = 0;
            phase = "paused"; following = true; UpdateFollow();
            conciseChoice.IsChecked = concise; detailedChoice.IsChecked = !concise;
            Highlight(segments[index].FindWord(positionOffset));
            reader.UpdateLayout(); Follow();
            status.Text = (concise ? "Concise" : "Detailed") + " - " + section + ". Press Play to continue from this section.";
            try
            {
                ReplyVersionPreference.Save(preferencePath, concise);
                preferenceNotice.Text = ""; preferenceNotice.Visibility = Visibility.Collapsed;
            }
            catch (IOException ex) { ShowPreferenceNotice("Could not save reading mode; this choice applies only to this reply: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { ShowPreferenceNotice("Could not save reading mode; this choice applies only to this reply: " + ex.Message); }
            WriteStatus();
        }

        private void ShowPreferenceNotice(string message)
        {
            preferenceNotice.Text = message; preferenceNotice.Visibility = Visibility.Visible;
        }

        internal static void SetButtonLabel(Button button, string label)
        {
            string glyph;
            switch (label)
            {
                case "Play": case "Resume": glyph = "\uE768"; break;
                case "Pause": glyph = "\uE769"; break;
                case "Stop": glyph = "\uE71A"; break;
                case "Previous sentence": glyph = "\uE76B"; break;
                case "Next sentence": glyph = "\uE76C"; break;
                case "Previous paragraph": glyph = "\uE70E"; break;
                case "Next paragraph": glyph = "\uE70D"; break;
                case "Close": glyph = "\uE8BB"; break;
                case "Following speech": glyph = "\uE73E"; break;
                case "Follow speech": glyph = "\uE72A"; break;
                default: throw new ArgumentException("Unknown button label: " + label);
            }
            button.Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
        }

        private void OnReaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            DependencyObject node = e.OriginalSource as DependencyObject;
            while (node != null && node != reader)
            {
                if (node is ScrollBar) { following = false; UpdateFollow(); WriteStatus(); return; }
                node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
            }
            ReadingPosition? target = ReadingNavigation.FromTextPointer(segments, reader.GetPositionFromPoint(e.GetPosition(reader), false));
            if (!target.HasValue) return;
            Seek(target.Value, phase == "speaking");
            e.Handled = true;
        }

        private void Start()
        {
            if (phase == "paused" && activePrompt != null) { PauseResume(); return; }
            Seek(phase == "finished" ? new ReadingPosition(0, 0) : new ReadingPosition(index, positionOffset), true);
        }

        private void TogglePlayback()
        {
            if (closing || closed) return;
            if (phase == "speaking") PauseResume();
            else Start();
        }

        private void UpdatePlaybackButton()
        {
            SetButtonLabel(play, phase == "speaking" ? "Pause" : phase == "paused" ? "Resume" : "Play");
            play.IsEnabled = !closing && !closed;
        }

        private void Navigate(int direction, bool paragraph)
        {
            ReadingPosition current = new ReadingPosition(index, positionOffset);
            ReadingPosition target = ReadingNavigation.Move(segments, current, direction, paragraph);
            if (target.Segment == current.Segment && target.Offset == current.Offset) return;
            Seek(target, phase == "speaking");
        }

        private void CancelSpeech()
        {
            activePrompt = null;
            speech.SpeakAsyncCancelAll();
            if (speech.State == SynthesizerState.Paused) speech.Resume();
        }

        private void Seek(ReadingPosition target, bool speakNow)
        {
            bool wasPaused = phase == "paused";
            CancelSpeech();
            index = target.Segment; positionOffset = target.Offset;
            following = true; UpdateFollow();
            Highlight(segments[index].FindWord(positionOffset)); Follow();
            if (speakNow) SpeakSegment();
            else
            {
                phase = wasPaused ? "paused" : "stopped";
                status.Text = "Selected - " + segments[index].Id + ". Press Play to speak from here.";
                WriteStatus();
            }
        }

        private void SpeakSegment()
        {
            Segment segment = segments[index];
            phase = "speaking";
            status.Text = "Speaking " + (index + 1) + " / " + segments.Count + " - " + segment.Id;
            promptOffset = positionOffset;
            activePrompt = new Prompt(segment.Spoken.Substring(promptOffset));
            try { speech.SpeakAsync(activePrompt); }
            catch (Exception ex) { Fail("Speech could not start: " + ex.Message); }
            WriteStatus();
        }

        private void OnProgress(object sender, SpeakProgressEventArgs e)
        {
            if (closed) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (closed || closing || e.Prompt != activePrompt || index < 0 || index >= segments.Count) return;
                WordAnchor next = segments[index].FindWord(promptOffset + e.CharacterPosition);
                if (next == null) return;
                positionOffset = next.Start;
                Highlight(next);
                Follow(); WriteStatus();
            }));
        }

        private void OnCompleted(object sender, SpeakCompletedEventArgs e)
        {
            if (closed) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (closed || closing || e.Prompt != activePrompt) return;
                if (e.Error != null) { Fail("Speech failed: " + e.Error.Message); return; }
                if (e.Cancelled) { Stop(); return; }
                activePrompt = null;
                if (index + 1 == segments.Count)
                {
                    phase = "finished"; ClearHighlight();
                    status.Text = "Finished. Play restarts; arrows or a click choose another position.";
                    WriteStatus(); return;
                }
                index++; positionOffset = 0;
                if (phase == "paused")
                {
                    Highlight(segments[index].FindWord(0));
                    status.Text = "Paused - " + segments[index].Id + ". Press Play to continue.";
                    WriteStatus();
                }
                else SpeakSegment();
            }));
        }

        private void Highlight(WordAnchor word)
        {
            if (Object.ReferenceEquals(activeWord, word)) return;
            ClearHighlight(); activeWord = word;
            if (word == null) return;
            word.Display.Background = Brushes.DarkBlue; word.Display.Foreground = Brushes.White;
        }

        private void ClearHighlight()
        {
            if (activeWord == null) return;
            activeWord.Display.ClearValue(TextElement.BackgroundProperty);
            activeWord.Display.ClearValue(TextElement.ForegroundProperty);
            activeWord = null;
        }

        private void Follow()
        {
            if (!following || activeWord == null || !reader.IsLoaded) return;
            Rect rect = activeWord.Display.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty) return;
            double top = 25, bottom = Math.Max(top, reader.ViewportHeight - 45);
            if (rect.Top < top) reader.ScrollToVerticalOffset(Math.Max(0, reader.VerticalOffset + rect.Top - top));
            else if (rect.Bottom > bottom) reader.ScrollToVerticalOffset(reader.VerticalOffset + rect.Bottom - bottom);
        }

        private void UpdateFollow()
        {
            SetButtonLabel(followButton, following ? "Following speech" : "Follow speech");
            followButton.Background = following ? new SolidColorBrush(Color.FromRgb(224, 234, 249)) : Brushes.Transparent;
            followButton.Foreground = following ? new SolidColorBrush(Color.FromRgb(36, 87, 181)) : new SolidColorBrush(Color.FromRgb(62, 67, 63));
        }

        private void PauseResume()
        {
            if (activePrompt == null) return;
            if (speech.State == SynthesizerState.Paused)
            {
                speech.Resume(); phase = "speaking"; status.Text = "Speaking - " + segments[index].Id;
            }
            else
            {
                speech.Pause(); phase = "paused"; status.Text = "Paused - " + segments[index].Id;
            }
            WriteStatus();
        }

        private void Stop()
        {
            CancelSpeech(); phase = "stopped";
            status.Text = "Stopped. Play continues here; click a word or use the arrows to move.";
            WriteStatus();
        }

        private void Fail(string message)
        {
            Stop(); phase = "error"; status.Text = message; WriteStatus();
        }

        private void WriteStatus()
        {
            UpdatePlaybackButton();
            if (String.IsNullOrEmpty(statusPath)) return;
            string anchor = index >= 0 && index < segments.Count ? segments[index].Id : "";
            string word = activeWord == null ? "" : activeWord.Display.Text;
            string payload = "phase=" + phase + "\nanchor=" + anchor + "\nword=" + word + "\nfollow=" + following + "\noffset=" + reader.VerticalOffset.ToString(CultureInfo.InvariantCulture) + "\nposition=" + positionOffset + "\npromptOffset=" + promptOffset + "\nversion=" + (concise ? "Concise" : "Detailed") + "\n";
            try { File.WriteAllText(statusPath, payload); }
            catch (IOException ex) { status.Text = "Status output failed: " + ex.Message; }
            catch (UnauthorizedAccessException ex) { status.Text = "Status output denied: " + ex.Message; }
        }
    }

    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);

        private static IntPtr ParseOwner(string text)
        {
            long value;
            if (String.IsNullOrEmpty(text) || !text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                !Int64.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) || value <= 0)
                throw new ArgumentException("Supply a live owner window handle in hexadecimal, for example --owner 0x1234.");
            IntPtr owner = new IntPtr(value);
            if (!IsWindow(owner)) throw new ArgumentException("The owner window no longer exists. Identify the current Visual Studio window before opening the overlay.");
            return owner;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
            if ((args.Length != 4 && args.Length != 6 && args.Length != 8) || args[0] != "--status" || args[2] != "--owner" ||
                (args.Length >= 6 && args[4] != "--reply") || (args.Length == 8 && args[6] != "--concise"))
            {
                Console.Error.WriteLine("Use --self-test or --status <session-status-path> --owner <VS-window-handle> [--reply <detailed-json> [--concise <concise-json>]]. Both versions need matching section IDs."); return 2;
            }
            try
            {
                IntPtr owner = ParseOwner(args[3]);
                Application app = new Application(); app.Run(new ReaderWindow(args[1], args.Length >= 6 ? args[5] : null, owner, args.Length == 8 ? args[7] : null)); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
        }

        private static int SelfTest()
        {
            int assertions = 0;
            Action<bool, string> check = delegate(bool ok, string description) { assertions++; if (!ok) throw new InvalidOperationException(description); };
            FlowDocument document = new FlowDocument();
            List<Segment> segments = DocumentBuilder.Sample(document);
            HashSet<string> ids = new HashSet<string>();
            foreach (Segment segment in segments)
            {
                check(ids.Add(segment.Id), "Duplicate anchor: " + segment.Id);
                check(segment.Spoken.Length > 0, "Empty spoken segment");
                check(!segment.Spoken.Contains("**"), "Formatting leaked into speech");
                int previousEnd = 0;
                foreach (WordAnchor word in segment.Words)
                {
                    check(word.Start >= previousEnd && word.Start + word.Length <= segment.Spoken.Length, "Invalid word offsets");
                    check(Object.ReferenceEquals(segment.FindWord(word.Start), word), "Start position mapping");
                    check(Object.ReferenceEquals(segment.FindWord(word.Start + word.Length - 1), word), "End position mapping");
                    check(word.Display.Parent == segment.Paragraph, "Word has wrong paragraph anchor");
                    previousEnd = word.Start + word.Length;
                }
                check(segment.FindWord(-1) == null && segment.FindWord(segment.Spoken.Length) == null, "Outside mapping");
                if (segment.Id.StartsWith("step-")) check(segment.Spoken.StartsWith("Step "), "Step marker absent");
            }
            Segment bold = DocumentBuilder.Add(document, "bold-test", "paragraph", "Before **important words** after.", 0);
            check(bold.Spoken == "Before important words after.", "Bold text changed");
            check(bold.FindWord(7).Display.FontWeight == FontWeights.Bold, "Bold display missing");
            Segment duplicateWords = DocumentBuilder.Add(document, "repeat-test", "paragraph", "cloud Claude cloud", 0);
            check(!Object.ReferenceEquals(duplicateWords.FindWord(0), duplicateWords.FindWord(13)), "Repeated words share anchor");
            bool invalid = false;
            try { DocumentBuilder.Add(document, "bad", "table", "Unsupported", 0); } catch (ArgumentException) { invalid = true; }
            check(invalid, "Unsupported blocks not rejected");
            Button iconButton = new Button();
            foreach (string label in new[] { "Play", "Pause", "Resume", "Stop", "Following speech", "Follow speech", "Close", "Pause", "Previous sentence", "Next sentence", "Previous paragraph", "Next paragraph" })
            {
                ReaderWindow.SetButtonLabel(iconButton, label);
                TextBlock icon = iconButton.Content as TextBlock;
                check(icon != null && icon.Text.Length == 1, "Button is not a single icon");
                check(icon.FontFamily.Source == "Segoe MDL2 Assets", "Wrong icon font");
                check(AutomationProperties.GetName(iconButton) == label, "Accessible button name is stale");
                check((string)iconButton.ToolTip == label, "Button tooltip is stale");
            }
            check(segments[0].Paragraph.FontSize == 14 && segments[0].Paragraph.LineHeight == 18, "Compact heading typography changed");
            foreach (Segment segment in segments)
            {
                check(segment.SentenceStarts[0] == 0, "First sentence must start at zero");
                foreach (WordAnchor word in segment.Words)
                {
                    ReadingPosition? picked = ReadingNavigation.FromTextPointer(segments, word.Display.ContentStart);
                    check(picked.HasValue && Object.ReferenceEquals(segments[picked.Value.Segment], segment) && picked.Value.Offset == word.Start, "Clicked word maps to wrong occurrence");
                    string suffix = segment.Spoken.Substring(word.Start);
                    check(Object.ReferenceEquals(segment.FindWord(word.Start + 0), word), "Sliced prompt maps to wrong original word");
                    check(suffix.Length > 0, "Empty sliced prompt");
                }
            }
            FlowDocument navigationDocument = new FlowDocument();
            List<Segment> navigation = new List<Segment>();
            navigation.Add(DocumentBuilder.Add(navigationDocument, "n1", "paragraph", "First sentence. Second **sentence!** Third?", 0));
            navigation.Add(DocumentBuilder.Add(navigationDocument, "n2", "step", "Repeat cloud. Then cloud.", 2));
            navigation.Add(DocumentBuilder.Add(navigationDocument, "n3", "paragraph", "Dr. Smith said \"Go!\" Next costs 3.14 dollars. Last one", 0));
            Action<ReadingPosition, int, int, string> at = delegate(ReadingPosition actual, int block, int offset, string description)
            {
                check(actual.Segment == block && actual.Offset == offset, description);
            };
            check(navigation[0].SentenceStarts.Count == 3, "Sentence punctuation or emphasis split failed");
            check(navigation[1].SentenceStarts.Count == 2, "Numbered prefix became a separate sentence");
            check(navigation[2].SentenceStarts.Count == 3, "Abbreviation, quote, decimal or final unpunctuated sentence split failed");
            at(ReadingNavigation.Move(navigation, new ReadingPosition(0, 4), 1, false), 0, 16, "Next sentence from mid-sentence");
            at(ReadingNavigation.Move(navigation, new ReadingPosition(0, 34), -1, false), 0, 16, "Previous sentence");
            at(ReadingNavigation.Move(navigation, new ReadingPosition(0, 34), 1, false), 1, 0, "Forward across paragraph boundary");
            at(ReadingNavigation.Move(navigation, new ReadingPosition(1, 0), -1, false), 0, 33, "Backward across paragraph boundary");
            at(ReadingNavigation.Move(navigation, new ReadingPosition(1, 8), 1, false), 1, 22, "Skip numbered first sentence");
            at(ReadingNavigation.Move(navigation, new ReadingPosition(1, 22), -1, false), 1, 0, "Return to numbered first sentence with prefix");
            at(ReadingNavigation.Move(navigation, new ReadingPosition(1, 22), -1, true), 0, 0, "Previous paragraph");
            at(ReadingNavigation.Move(navigation, new ReadingPosition(1, 22), 1, true), 2, 0, "Next paragraph");
            at(ReadingNavigation.Move(navigation, new ReadingPosition(0, 0), -1, false), 0, 0, "Before first sentence must not wrap");
            int lastWord = navigation[2].Words[navigation[2].Words.Count - 1].Start;
            at(ReadingNavigation.Move(navigation, new ReadingPosition(2, lastWord), 1, false), 2, lastWord, "After last sentence must not restart");
            at(ReadingNavigation.Move(navigation, new ReadingPosition(2, lastWord), 1, true), 2, lastWord, "After last paragraph must not restart");
            check(!ReadingNavigation.FromTextPointer(navigation, null).HasValue, "Blank click must not seek");
            Func<string, List<Segment>> readReply = delegate(string json)
            {
                using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(json))) return DocumentBuilder.LoadReply(new FlowDocument(), input);
            };
            List<Segment> reply = readReply("[{\"id\":\"h\",\"kind\":\"heading\",\"text\":\"Real reply\"},{\"id\":\"p\",\"kind\":\"paragraph\",\"text\":\"Keep **bold words**.\"},{\"id\":\"b\",\"kind\":\"bullet\",\"text\":\"A bullet\"},{\"id\":\"s\",\"kind\":\"step\",\"text\":\"Do this.\",\"number\":2}]");
            check(reply.Count == 4 && reply[0].Spoken == "Real reply", "Real reply was replaced with sample text");
            check(reply[1].Spoken == "Keep bold words." && reply[1].FindWord(5).Display.FontWeight == FontWeights.Bold, "Reply emphasis changed");
            check(reply[3].Spoken == "Step 2. Do this.", "Reply step prefix changed");
            foreach (string bad in new[] { "[]", "null", "[null]", "[{\"id\":\"x\",\"kind\":\"paragraph\",\"text\":null}]", "[{\"id\":\"x\",\"kind\":\"paragraph\",\"text\":\"   \"}]", "[{\"id\":\"x\",\"kind\":\"table\",\"text\":\"No\"}]", "[{\"id\":\"x\",\"kind\":\"paragraph\",\"text\":\"One\"},{\"id\":\"x\",\"kind\":\"paragraph\",\"text\":\"Two\"}]", "[{\"id\":\"x\",\"kind\":\"step\",\"text\":\"Missing number\"}]", "[{\"kind\":\"paragraph\",\"text\":\"Missing id\"}]", "not json" })
            {
                bool rejected = false;
                try { readReply(bad); }
                catch (ArgumentException) { rejected = true; }
                catch (SerializationException) { rejected = true; }
                check(rejected, "Invalid reply accepted: " + bad);
            }
            string completeReply = "[{\"id\":\"only\",\"kind\":\"paragraph\",\"text\":\"Exact body.\"}]";
            string dateReply = "[{\"id\":\"date\",\"kind\":\"paragraph\",\"text\":\"\\/Date(253402300800000)\\/\"}]";
            string escapedText = "Quoted \"]}\" and a slash \\\\ inside [text], plus caf\u00E9.";
            string escapedReply;
            using (MemoryStream output = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(List<ReplyBlock>)).WriteObject(output, new List<ReplyBlock> { new ReplyBlock { Id = "quoted", Kind = "paragraph", Text = escapedText } });
                escapedReply = Encoding.UTF8.GetString(output.ToArray());
            }
            foreach (Encoding encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
            {
                using (MemoryStream input = new MemoryStream(encoding.GetBytes(completeReply + " \r\n\t")))
                    check(DocumentBuilder.LoadReply(new FlowDocument(), input)[0].Spoken == "Exact body.", "Valid reply encoding or trailing whitespace rejected");
                using (MemoryStream input = new MemoryStream(encoding.GetBytes(dateReply)))
                    check(DocumentBuilder.LoadReply(new FlowDocument(), input)[0].Spoken == "/Date(253402300800000)/", "Date-shaped string content changed or was rejected");
                using (MemoryStream input = new MemoryStream(encoding.GetBytes(escapedReply)))
                    check(DocumentBuilder.LoadReply(new FlowDocument(), input)[0].Spoken == escapedText, "Escapes, delimiters inside strings, or Unicode text changed");
                foreach (string trailing in new[] { " {\"extra\":true}", " []", " null", " true", " 42", " garbage" })
                {
                    bool rejected = false;
                    try
                    {
                        using (MemoryStream input = new MemoryStream(encoding.GetBytes(completeReply + trailing))) DocumentBuilder.LoadReply(new FlowDocument(), input);
                    }
                    catch (ArgumentException) { rejected = true; }
                    catch (SerializationException) { rejected = true; }
                    check(rejected, "Trailing input was ignored for " + encoding.WebName + ":" + trailing);
                }
                bool trailingAfterEscapes = false;
                try
                {
                    using (MemoryStream input = new MemoryStream(encoding.GetBytes(escapedReply + "{}"))) DocumentBuilder.LoadReply(new FlowDocument(), input);
                }
                catch (ArgumentException) { trailingAfterEscapes = true; }
                check(trailingAfterEscapes, "Escaped text hid trailing content");
            }
            foreach (string incomplete in new[] { "", " \r\n\t", "[", "[{]}", "[\"unterminated]", "[\"escaped quote\\\"]", completeReply + "\v" })
            {
                bool rejected = false;
                try { readReply(incomplete); }
                catch (ArgumentException) { rejected = true; }
                catch (SerializationException) { rejected = true; }
                check(rejected, "Invalid reply boundary accepted: " + incomplete);
            }
            Console.WriteLine("PASS: " + assertions + " assertions; " + segments.Count + " sample segments.");
            return 0;
        }
    }
}