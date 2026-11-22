using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using VsCopilotChatVoice.Speech;

// FileAndForget observes and logs failures of the fire-and-forget UI work, which the analyzer does not recognize.
#pragma warning disable VSSDK007

namespace VsCopilotChatVoice.VisualStudio;

// Presentation only. Speech synthesis runs in the external reader worker process; this control sends text over
// the host pipe and highlights the words that the worker reports. It never touches the Copilot Chat window.
internal sealed class ReaderToolWindowControl : UserControl, IDisposable
{
    internal const string SilentVariable = "VSCOPILOTCHATVOICE_SPEECH_SILENT";

    private const string SampleReply =
        "This is a prepared sample reply for the native reader tool window preview.\n\n" +
        "The words you hear are spoken by a separate worker process. This window only shows the text and moves " +
        "the highlight as the worker reports progress.\n\n" +
        "Press Cancel at any time and the highlight stops, even if the worker sends a late message.";

    private readonly string workerPath;
    private readonly bool silent;
    private readonly Button speakButton = new Button { Content = "Speak", Margin = new Thickness(0, 0, 6, 0), MinWidth = 72 };
    private readonly Button cancelButton = new Button { Content = "Cancel", MinWidth = 72, IsEnabled = false };
    private readonly TextBlock status = new TextBlock { Text = "Idle", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly FlowDocumentScrollViewer viewer = new FlowDocumentScrollViewer { Focusable = false, IsToolBarVisible = false };
    private readonly List<ReplyWord> words = ReplyText.Tokenize(SampleReply);
    private readonly List<Run> runs = new List<Run>();
    private SpeechWorkerHost? host;
    private long uiGeneration;
    private int highlighted = -1;
    private bool starting;
    private bool disposed;

    public ReaderToolWindowControl(string workerPath, bool silent)
    {
        this.workerPath = workerPath;
        this.silent = silent;
        AutomationProperties.SetAutomationId(speakButton, "VoiceReaderSpeak");
        AutomationProperties.SetAutomationId(cancelButton, "VoiceReaderCancel");
        AutomationProperties.SetAutomationId(status, "VoiceReaderStatus");
        AutomationProperties.SetAutomationId(viewer, "VoiceReaderDocument");
        speakButton.Click += OnSpeak;
        cancelButton.Click += OnCancel;
        viewer.Document = BuildDocument();
        viewer.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        viewer.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        foreach (Button button in new[] { speakButton, cancelButton })
        {
            button.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            button.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            button.SetResourceReference(Control.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        buttons.Children.Add(speakButton);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(status);
        var layout = new DockPanel();
        layout.SetResourceReference(Panel.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        DockPanel.SetDock(buttons, Dock.Top);
        layout.Children.Add(buttons);
        layout.Children.Add(viewer);
        layout.SetResourceReference(Panel.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        Content = layout;
    }

    private FlowDocument BuildDocument()
    {
        var document = new FlowDocument { PagePadding = new Thickness(12) };
        document.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        document.SetResourceReference(FlowDocument.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        var paragraph = new Paragraph();
        int previousEnd = 0;
        foreach (ReplyWord word in words)
        {
            string gap = SampleReply.Substring(previousEnd, word.Start - previousEnd);
            if (gap.IndexOf('\n') >= 0 && paragraph.Inlines.Count > 0)
            {
                document.Blocks.Add(paragraph);
                paragraph = new Paragraph();
            }
            else if (gap.Length > 0 && paragraph.Inlines.Count > 0) paragraph.Inlines.Add(new Run(" "));
            var run = new Run(SampleReply.Substring(word.Start, word.Length));
            runs.Add(run);
            paragraph.Inlines.Add(run);
            previousEnd = word.Start + word.Length;
        }
        document.Blocks.Add(paragraph);
        return document;
    }

    private void OnSpeak(object sender, RoutedEventArgs e) =>
        ThreadHelper.JoinableTaskFactory.RunAsync(SpeakAsync).FileAndForget("vscopilotchatvoice/reader/speak");

    private async Task SpeakAsync()
    {
        if (disposed || starting) return;
        starting = true;
        speakButton.IsEnabled = false;
        try
        {
            if (host == null)
            {
                SetStatus("Starting speech worker...");
                SpeechWorkerHost created = await SpeechWorkerHost.LaunchAsync(workerPath, silent, TimeSpan.FromSeconds(15));
                if (disposed) { created.Dispose(); return; }
                created.Progress += OnWorkerProgress;
                created.Completed += OnWorkerCompleted;
                created.Faulted += OnWorkerFaulted;
                host = created;
            }
            SetHighlight(-1);
            uiGeneration = host.Speak(SampleReply);
            cancelButton.IsEnabled = true;
            SetStatus("Speaking");
        }
        catch (Exception ex) when (ex is System.IO.IOException || ex is InvalidOperationException ||
            ex is TimeoutException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
        {
            DropHost();
            SetStatus("Speech unavailable: " + ex.GetType().Name);
        }
        finally
        {
            starting = false;
            if (!disposed) speakButton.IsEnabled = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (disposed) return;
        uiGeneration = 0;
        cancelButton.IsEnabled = false;
        SetHighlight(-1);
        try { host?.Cancel(); }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.IO.IOException) { DropHost(); }
        SetStatus("Cancelled");
    }

    // Worker events arrive on pipe threads. They are checked again on the UI thread against the control's own
    // generation, because Cancel can run between the pipe thread and the dispatcher callback.
    private void OnWorkerProgress(long generation, int offset)
    {
        Post(() =>
        {
            if (generation != uiGeneration) return;
            int index = ReplyText.FindWord(words, offset);
            SetHighlight(index);
            SetStatus("Speaking: word " + (index + 1) + " of " + words.Count);
        });
    }

    private void OnWorkerCompleted(long generation)
    {
        Post(() =>
        {
            if (generation != uiGeneration) return;
            uiGeneration = 0;
            cancelButton.IsEnabled = false;
            SetHighlight(-1);
            SetStatus("Finished");
        });
    }

    private void OnWorkerFaulted(string reason)
    {
        Post(() =>
        {
            uiGeneration = 0;
            cancelButton.IsEnabled = false;
            SetHighlight(-1);
            DropHost();
            SetStatus("Speech worker stopped: " + reason);
        });
    }

    private void Post(Action action)
    {
        if (disposed) return;
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!disposed) action();
        }).FileAndForget("vscopilotchatvoice/reader/worker-event");
    }

    private void SetStatus(string text)
    {
        status.Text = text;
    }

    private void SetHighlight(int index)
    {
        if (highlighted >= 0 && highlighted < runs.Count)
        {
            runs[highlighted].ClearValue(TextElement.BackgroundProperty);
            runs[highlighted].ClearValue(TextElement.ForegroundProperty);
        }
        highlighted = index >= 0 && index < runs.Count ? index : -1;
        if (highlighted < 0) return;
        Run run = runs[highlighted];
        run.SetResourceReference(TextElement.BackgroundProperty, EnvironmentColors.SystemHighlightBrushKey);
        run.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.SystemHighlightTextBrushKey);
        run.BringIntoView();
    }

    private void DropHost()
    {
        SpeechWorkerHost? old = host;
        host = null;
        if (old == null) return;
        old.Progress -= OnWorkerProgress;
        old.Completed -= OnWorkerCompleted;
        old.Faulted -= OnWorkerFaulted;
        old.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        DropHost();
    }
}
