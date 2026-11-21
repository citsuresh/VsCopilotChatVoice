using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Microsoft.VisualStudio.PlatformUI;
using ReadAlongTrial;

// The owning WPF dispatcher, not a captured SynchronizationContext, governs every model/view continuation.
#pragma warning disable VSTHRD001, VSTHRD003, VSTHRD010, VSTHRD109, VSSDK007

namespace VsCopilotChatVoice.VisualStudio;

// A single prepared paired reply. No chat, metadata, synthesis, automatic opening or playback ownership here.
internal sealed class NativeReaderView : UserControl, IDisposable
{
    private readonly string preferencePath;
    private readonly Action hide;
    private readonly Action<Task> observe;
    private readonly ReaderPlaybackController controller;
    private readonly FlowDocument detailedDocument;
    private readonly FlowDocument? conciseDocument;
    private readonly List<Segment> detailed;
    private readonly List<Segment>? concise;
    private readonly RichTextBox reader = new RichTextBox
    {
        IsReadOnly = true, IsReadOnlyCaretVisible = false, BorderThickness = new Thickness(0),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Padding = new Thickness(8)
    };
    private readonly Button play = new Button();
    private readonly Button stop = new Button();
    private readonly Button follow = new Button();
    private readonly VersionChoice detailedChoice = new VersionChoice { Content = "Detailed", GroupName = "ReaderVersion" };
    private readonly VersionChoice conciseChoice = new VersionChoice { Content = "Concise", GroupName = "ReaderVersion" };
    private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 4) };
    private readonly TextBlock preferenceNotice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 6) };
    private readonly TextBlock frameNotice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 6) };
    private readonly List<Control> controls = new List<Control>();
    private Run? highlighted;
    private bool following = true;
    private bool frameAvailable;
    private bool disposed;
    private bool highContrast;
    private long selectionRequest;
    private string? unexpectedError;

    internal NativeReaderView(IReaderSpeechBackend backend, string preferencePath, string detailedFixture,
        string? conciseFixture, Action hide, Action<Task> observe)
    {
        Dispatcher.VerifyAccess();
        this.preferencePath = preferencePath ?? throw new ArgumentNullException(nameof(preferencePath));
        this.hide = hide ?? throw new ArgumentNullException(nameof(hide));
        this.observe = observe ?? throw new ArgumentNullException(nameof(observe));
        detailedDocument = NewDocument();
        detailed = Load(detailedDocument, detailedFixture);
        if (conciseFixture != null)
        {
            conciseDocument = NewDocument();
            concise = Load(conciseDocument, conciseFixture);
            DocumentBuilder.ValidateVersions(detailed, concise);
        }
        bool preferredConcise = false;
        try { preferredConcise = ReplyVersionPreference.Load(preferencePath); }
        catch (Exception ex) when (PreferenceFailure(ex))
        { preferenceNotice.Text = "Could not load reading mode; using Detailed for this reply: " + ex.Message; }
        if (preferredConcise && concise == null)
            preferenceNotice.Text = "No Concise version supplied. Showing Detailed; your saved Concise choice is unchanged.";
        controller = new ReaderPlaybackController(Dispatcher, backend, Guid.NewGuid(), detailed, concise, preferredConcise && concise != null);
        controller.Changed += OnChanged;
        controller.BackgroundWork += ObserveBackground;

        AutomationProperties.SetAutomationId(this, "NativeVoiceReader");
        AutomationProperties.SetName(this, "Prepared single-reply native voice reader preview");
        Identify(reader, "NativeReaderDocument", "Prepared reply text; select a word to seek");
        IdentifyText(status, "NativeReaderStatus");
        IdentifyText(preferenceNotice, "NativeReaderPreferenceNotice");
        IdentifyText(frameNotice, "NativeReaderFrameNotice");
        var toolbar = new WrapPanel { Margin = new Thickness(4) };
        AddButton(toolbar, play, "Play", "NativeReaderPlay", TogglePlaybackAsync);
        AddButton(toolbar, stop, "Stop", "NativeReaderStop", StopAsync);
        AddButton(toolbar, new Button(), "Previous sentence", "NativeReaderPreviousSentence", () => NavigateAsync(-1, false));
        AddButton(toolbar, new Button(), "Next sentence", "NativeReaderNextSentence", () => NavigateAsync(1, false));
        AddButton(toolbar, new Button(), "Previous paragraph", "NativeReaderPreviousParagraph", () => NavigateAsync(-1, true));
        AddButton(toolbar, new Button(), "Next paragraph", "NativeReaderNextParagraph", () => NavigateAsync(1, true));
        AddButton(toolbar, follow, "Following speech", "NativeReaderFollow", () => { SetFollowing(!following); return Task.CompletedTask; });
        ConfigureChoice(toolbar, detailedChoice, "NativeReaderDetailed", false);
        ConfigureChoice(toolbar, conciseChoice, "NativeReaderConcise", true);
        conciseChoice.ToolTip = concise == null ? "No Concise version supplied for this reply." : "Choose the supplied Concise version; stays paused.";
        var layout = new DockPanel();
        var heading = new TextBlock { Text = "Prepared fixture only — not a Copilot reply", TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8), FontWeight = FontWeights.SemiBold };
        foreach (FrameworkElement top in new FrameworkElement[] { heading, toolbar, status, preferenceNotice, frameNotice })
        { DockPanel.SetDock(top, Dock.Top); layout.Children.Add(top); }
        layout.Children.Add(reader);
        Content = layout;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Continue);
        reader.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel), true);
        reader.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnReaderMouseDown), true);
        reader.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(OnScroll), true);
        PreviewKeyDown += OnKeyDown;
        SystemParameters.StaticPropertyChanged += OnSystemChanged;
        ApplyTheme(SystemParameters.HighContrast);
        Refresh();
    }

    internal ReaderPlaybackController Controller { get { Dispatcher.VerifyAccess(); return controller; } }
    internal bool Following { get { Dispatcher.VerifyAccess(); return following; } }
    internal bool IsDisposed { get { Dispatcher.VerifyAccess(); return disposed; } }
    private List<Segment> Segments => controller.IsConcise ? concise! : detailed;

    private static FlowDocument NewDocument() => new FlowDocument
    { PagePadding = new Thickness(4), FontFamily = new FontFamily("Segoe UI"), FontSize = 13, ColumnWidth = double.PositiveInfinity };
    private static List<Segment> Load(FlowDocument document, string fixture)
    {
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(fixture))) return DocumentBuilder.LoadReply(document, input);
    }
    private static bool PreferenceFailure(Exception ex) => ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException;
    private static void Identify(DependencyObject element, string id, string name)
    { AutomationProperties.SetAutomationId(element, id); AutomationProperties.SetName(element, name); }

    private static void IdentifyText(TextBlock element, string id)
    {
        AutomationProperties.SetAutomationId(element, id);
        element.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(TextBlock.Text)) { Source = element, Mode = BindingMode.OneWay });
        AutomationProperties.SetLiveSetting(element, AutomationLiveSetting.Polite);
    }

    // Mark only the originating Checked event, not nested peer events inside its callbacks.
    private sealed class VersionChoice : RadioButton
    {
        private bool nativeCheckPending;
        private bool modelCheckPending;
        private RoutedEventArgs? ignoredCheck;
        internal Func<long> BeginClick { get; set; } = null!;
        internal long? ClickRequest { get; private set; }
        internal bool IgnoreChecked(RoutedEventArgs args) => ReferenceEquals(ignoredCheck, args);
        internal void Synchronize(bool selected)
        {
            if (IsChecked == selected) return;
            bool previous = modelCheckPending;
            modelCheckPending = selected;
            try { IsChecked = selected; }
            finally { modelCheckPending = previous; }
        }
        protected override void OnClick()
        {
            long? previous = ClickRequest;
            ClickRequest = BeginClick();
            try { base.OnClick(); }
            finally { ClickRequest = previous; }
        }
        protected override void OnToggle()
        {
            bool previous = nativeCheckPending;
            nativeCheckPending = IsChecked != true;
            try { base.OnToggle(); }
            finally { nativeCheckPending = previous; }
        }
        protected override void OnChecked(RoutedEventArgs e)
        {
            RoutedEventArgs? previous = ignoredCheck;
            ignoredCheck = nativeCheckPending || modelCheckPending ? e : null;
            nativeCheckPending = false;
            modelCheckPending = false;
            try { base.OnChecked(e); }
            finally { ignoredCheck = previous; }
        }
    }

    private void AddButton(Panel panel, Button button, string label, string id, Func<Task> action)
    {
        Label(button, label);
        Identify(button, id, label);
        button.Margin = new Thickness(3);
        button.Padding = new Thickness(7, 4, 7, 4);
        button.Click += (s, e) => { if (!disposed) Observe(action); };
        controls.Add(button); panel.Children.Add(button);
    }
    private void ConfigureChoice(Panel panel, VersionChoice choice, string id, bool useConcise)
    {
        choice.Margin = new Thickness(6);
        Identify(choice, id, (string)choice.Content);
        choice.BeginClick = RegisterSelection;
        choice.Checked += (s, e) => { if (!disposed && !choice.IgnoreChecked(e)) Observe(() => ChooseVersionAsync(useConcise)); };
        choice.Click += (s, e) =>
        {
            if (!disposed && choice.ClickRequest is long requestedAt && selectionRequest == requestedAt)
                Observe(() => ApplyVersionAsync(useConcise, requestedAt));
        };
        controls.Add(choice); panel.Children.Add(choice);
    }
    private long RegisterSelection() { Dispatcher.VerifyAccess(); return checked(++selectionRequest); }
    private static void Label(Button button, string value)
    { button.Content = value; button.ToolTip = value; AutomationProperties.SetName(button, value); }

    private void Observe(Func<Task> action) => observe(PresentFailuresAsync(action));
    private void ObserveBackground(Task task) => Observe(() => task);
    private async Task PresentFailuresAsync(Func<Task> action)
    {
        Task? work = null;
        try { work = action(); await work.ConfigureAwait(false); }
        catch (OperationCanceledException) when (work != null && work.IsCanceled)
        { /* Aborted dispatcher drains are expected when the owning prompt is retired. */ }
        catch (Exception ex)
        {
            if (!Dispatcher.HasShutdownStarted)
                await Dispatcher.InvokeAsync(() =>
                {
                    if (disposed) return;
                    unexpectedError = "Reader operation failed: " + ex.GetType().Name + ". See the activity log.";
                    Refresh();
                }).Task.ConfigureAwait(false);
            throw; // FileAndForget (or the injected reporter) must observe unexpected failures, not success-shaped tasks.
        }
    }

    internal Task TogglePlaybackAsync()
    {
        Dispatcher.VerifyAccess();
        if (disposed || !frameAvailable) return Task.CompletedTask;
        unexpectedError = null;
        return controller.Phase == ReaderPlaybackPhase.Speaking ? controller.PauseAsync() : controller.PlayAsync();
    }
    internal Task StopAsync()
    { Dispatcher.VerifyAccess(); return disposed ? Task.CompletedTask : controller.StopAsync(); }
    internal Task NavigateAsync(int direction, bool paragraph)
    {
        Dispatcher.VerifyAccess();
        if (disposed) return Task.CompletedTask;
        long navigatingAt = controller.Operation;
        long navigatingVersion = controller.Version;
        SetFollowing(true);
        if (disposed || controller.Operation != navigatingAt || controller.Version != navigatingVersion) return Task.CompletedTask;
        return controller.NavigateAsync(direction, paragraph);
    }
    internal Task SeekAsync(ReadingPosition target)
    {
        Dispatcher.VerifyAccess();
        if (disposed) return Task.CompletedTask;
        Task task = controller.SeekAsync(target);
        SetFollowing(true);
        return task;
    }
    internal Task SeekPointerAsync(TextPointer pointer)
    {
        Dispatcher.VerifyAccess();
        if (disposed) return Task.CompletedTask;
        ReadingPosition? target = ReadingNavigation.FromTextPointer(Segments, pointer);
        return target.HasValue ? SeekAsync(target.Value) : Task.CompletedTask;
    }
    internal Task ChooseVersionAsync(bool useConcise)
    {
        Dispatcher.VerifyAccess();
        if (disposed) return Task.CompletedTask;
        return ApplyVersionAsync(useConcise, RegisterSelection());
    }
    private Task ApplyVersionAsync(bool useConcise, long requestedAt)
    {
        Dispatcher.VerifyAccess();
        if (disposed || selectionRequest != requestedAt) return Task.CompletedTask;
        long chosenVersion = controller.Version + (controller.IsConcise == useConcise ? 0 : 1);
        Task task = controller.SwitchVersionAsync(useConcise);
        if (disposed || selectionRequest != requestedAt || controller.Version != chosenVersion) return task;
        SetFollowing(true);
        if (disposed || selectionRequest != requestedAt || controller.Version != chosenVersion) return task;
        try { ReplyVersionPreference.Save(preferencePath, useConcise); preferenceNotice.Text = ""; }
        catch (Exception ex) when (PreferenceFailure(ex))
        { preferenceNotice.Text = "Could not save reading mode; this choice applies only to this reply: " + ex.Message; }
        Refresh();
        return task;
    }
    internal Task HideAsync(bool hideFrame)
    {
        Dispatcher.VerifyAccess();
        if (disposed) return Task.CompletedTask;
        long hidingAt = controller.Operation + 1;
        Task task = controller.HideAsync();
        if (hideFrame && !disposed && controller.Operation == hidingAt) hide();
        return task;
    }
    internal void FrameClosed() { Dispatcher.VerifyAccess(); if (!disposed) Observe(() => HideAsync(false)); }
    internal void SetFrameAvailable(bool available, string? reason = null)
    {
        Dispatcher.VerifyAccess();
        if (disposed) return;
        frameAvailable = available;
        frameNotice.Text = available ? "" : "Native frame close notification unavailable; playback disabled. " + reason;
        if (!available) Observe(StopAsync);
        Refresh();
    }
    internal void SetFollowing(bool value)
    {
        Dispatcher.VerifyAccess();
        if (disposed) return;
        following = value;
        Label(follow, following ? "Following speech" : "Follow speech");
        Refresh();
    }
    private void Follow() { if (following && highlighted != null) highlighted.BringIntoView(); }
    private void OnChanged(ReaderPlaybackController owner) { Dispatcher.VerifyAccess(); if (!disposed) Refresh(); }
    private void Refresh()
    {
        Dispatcher.VerifyAccess();
        FlowDocument document = controller.IsConcise ? conciseDocument! : detailedDocument;
        if (reader.Document != document) reader.Document = document;
        detailedChoice.Synchronize(!controller.IsConcise);
        conciseChoice.Synchronize(controller.IsConcise);
        ReaderPlaybackPhase phase = controller.Phase;
        foreach (Control control in controls) control.IsEnabled = !disposed;
        conciseChoice.IsEnabled = !disposed && concise != null;
        play.IsEnabled = !disposed && frameAvailable && phase != ReaderPlaybackPhase.Starting &&
            phase != ReaderPlaybackPhase.Pausing && phase != ReaderPlaybackPhase.Resuming;
        stop.IsEnabled = !disposed && phase != ReaderPlaybackPhase.Stopped;
        Label(play, phase == ReaderPlaybackPhase.Speaking || phase == ReaderPlaybackPhase.Pausing ? "Pause" :
            phase == ReaderPlaybackPhase.Paused || phase == ReaderPlaybackPhase.Resuming ? "Resume" : "Play");
        ReadingPosition position = controller.Position;
        status.Text = unexpectedError ?? controller.Error ?? (phase + " — " + (controller.IsConcise ? "Concise" : "Detailed") +
            ", section " + (position.Segment + 1) + " of " + Segments.Count + ". " +
            (following ? "Following speech." : "Follow suspended.") +
            (phase == ReaderPlaybackPhase.Paused ? " Press Resume to continue." : ""));
        status.SetResourceReference(TextBlock.ForegroundProperty, unexpectedError != null || controller.Error != null ?
            ErrorKey : TextKey);
        preferenceNotice.Visibility = preferenceNotice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        frameNotice.Visibility = frameNotice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Run? selected = controller.SelectedWord?.Display;
        if (selected != highlighted)
        {
            if (highlighted != null)
            { highlighted.ClearValue(TextElement.BackgroundProperty); highlighted.SetResourceReference(TextElement.ForegroundProperty, TextKey); }
            highlighted = selected;
            if (highlighted != null)
            { highlighted.SetResourceReference(TextElement.BackgroundProperty, HighlightKey); highlighted.SetResourceReference(TextElement.ForegroundProperty, HighlightTextKey); }
        }
        Follow();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e) { SetFollowing(false); Refresh(); }
    private void OnScroll(object sender, ScrollEventArgs e) { SetFollowing(false); Refresh(); }
    private static DependencyObject? GetParent(DependencyObject node) => node is Visual || node is Visual3D ?
        VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node) :
        node is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(node);
    private void OnReaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (disposed || e.ChangedButton != MouseButton.Left) return;
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node != null && node != reader)
        {
            if (node is ScrollBar) { SetFollowing(false); Refresh(); return; }
            node = GetParent(node);
        }
        if (e.Handled) return;
        TextPointer pointer = reader.GetPositionFromPoint(e.GetPosition(reader), false);
        if (pointer == null || !ReadingNavigation.FromTextPointer(Segments, pointer).HasValue) return;
        e.Handled = true;
        Observe(() => SeekPointerAsync(pointer));
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleKey(e.Key, Keyboard.FocusedElement as DependencyObject, e.Handled, e.IsRepeat, Keyboard.Modifiers)) e.Handled = true;
    }
    internal bool TryHandleKey(Key key, DependencyObject? focused, bool handled, bool repeat, ModifierKeys modifiers)
    {
        Dispatcher.VerifyAccess();
        if (disposed || handled || repeat || modifiers != ModifierKeys.None || focused == null) return false;
        DependencyObject? node = focused;
        bool ownedButton = false;
        while (node != null && node != this)
        {
            if (node is ComboBox || node is ScrollBar || node is Slider ||
                node is TextBoxBase && node != reader) return false;
            if (node is ButtonBase button)
            {
                if (key != Key.Escape || !controls.Contains(button)) return false;
                ownedButton = true;
            }
            node = GetParent(node);
        }
        if (node != this || (focused != this && focused != reader && !reader.IsAncestorOf(focused) &&
            !(key == Key.Escape && ownedButton))) return false;
        switch (key)
        {
            case Key.Space: Observe(TogglePlaybackAsync); break;
            case Key.Left: Observe(() => NavigateAsync(-1, false)); break;
            case Key.Right: Observe(() => NavigateAsync(1, false)); break;
            case Key.Up: Observe(() => NavigateAsync(-1, true)); break;
            case Key.Down: Observe(() => NavigateAsync(1, true)); break;
            case Key.Home: Observe(() => SeekAsync(new ReadingPosition(0, Segments[0].Words[0].Start))); break;
            case Key.End:
                List<Segment> values = Segments;
                Observe(() => SeekAsync(new ReadingPosition(values.Count - 1, values[values.Count - 1].Words[values[values.Count - 1].Words.Count - 1].Start)));
                break;
            case Key.Escape: Observe(() => HideAsync(true)); break;
            default: return false;
        }
        return true;
    }

    private object TextKey => highContrast ? SystemColors.WindowTextBrushKey : EnvironmentColors.ToolWindowTextBrushKey;
    private object BackgroundKey => highContrast ? SystemColors.WindowBrushKey : EnvironmentColors.ToolWindowBackgroundBrushKey;
    private object HighlightKey => highContrast ? SystemColors.HighlightBrushKey : EnvironmentColors.SystemHighlightBrushKey;
    private object HighlightTextKey => highContrast ? SystemColors.HighlightTextBrushKey : EnvironmentColors.SystemHighlightTextBrushKey;
    private object ErrorKey => highContrast ? SystemColors.WindowTextBrushKey : EnvironmentColors.ToolWindowTextBrushKey;
    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.HighContrast)) return;
        ObserveBackground(Dispatcher.InvokeAsync(() => { if (!disposed) { ApplyTheme(SystemParameters.HighContrast); Refresh(); } }).Task);
    }
    internal void ApplyTheme(bool systemHighContrast)
    {
        Dispatcher.VerifyAccess();
        if (disposed) return;
        highContrast = systemHighContrast;
        SetResourceReference(BackgroundProperty, BackgroundKey);
        SetResourceReference(ForegroundProperty, TextKey);
        reader.SetResourceReference(Control.BackgroundProperty, BackgroundKey);
        reader.SetResourceReference(Control.ForegroundProperty, TextKey);
        reader.SetResourceReference(TextBoxBase.SelectionBrushProperty, HighlightKey);
        reader.SetResourceReference(TextBoxBase.SelectionTextBrushProperty, HighlightTextKey);
        foreach (FlowDocument document in conciseDocument == null ? new[] { detailedDocument } : new[] { detailedDocument, conciseDocument })
        {
            document.SetResourceReference(FlowDocument.BackgroundProperty, BackgroundKey);
            document.SetResourceReference(TextElement.ForegroundProperty, TextKey);
            foreach (Paragraph paragraph in document.Blocks)
                foreach (Inline inline in paragraph.Inlines) inline.SetResourceReference(TextElement.ForegroundProperty, TextKey);
        }
        foreach (TextBlock notice in new[] { preferenceNotice, frameNotice }) notice.SetResourceReference(TextBlock.ForegroundProperty, ErrorKey);
        var focusShape = new FrameworkElementFactory(typeof(Rectangle));
        focusShape.SetValue(Shape.StrokeProperty, new DynamicResourceExtension(TextKey));
        focusShape.SetValue(Shape.StrokeDashArrayProperty, new DoubleCollection { 1, 1 });
        focusShape.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
        var focusStyle = new Style(typeof(Control));
        focusStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = focusShape }));
        foreach (Control control in controls)
        {
            Type nativeType = control is RadioButton ? typeof(RadioButton) : control.GetType();
            var style = new Style(nativeType, TryFindResource(nativeType) as Style);
            style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(TextKey)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(BackgroundKey)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(highContrast ? SystemColors.WindowTextBrushKey : EnvironmentColors.ToolWindowBorderBrushKey)));
            var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(SystemColors.GrayTextBrushKey)));
            style.Triggers.Add(disabled);
            control.Style = style; control.FocusVisualStyle = focusStyle;
        }
        reader.FocusVisualStyle = focusStyle;
        highlighted = null;
    }
    public void Dispose()
    {
        Dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        controller.Dispose();
        controller.Changed -= OnChanged;
        controller.BackgroundWork -= ObserveBackground;
        SystemParameters.StaticPropertyChanged -= OnSystemChanged;
        reader.RemoveHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
        reader.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnReaderMouseDown));
        reader.RemoveHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(OnScroll));
        PreviewKeyDown -= OnKeyDown;
        Refresh();
    }
}
