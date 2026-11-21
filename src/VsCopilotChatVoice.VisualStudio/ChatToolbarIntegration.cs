using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace VsCopilotChatVoice.VisualStudio;

internal sealed class ChatToolbarIntegration : IDisposable
{
    internal const string ToolbarId = "VsCopilotChatVoiceToolbar";
    private const int DiscoveryLimit = 12000;
    private readonly Action<FrameworkElement> configureTheme;
    private readonly Action<string> reportStatus;
    private readonly Action<Exception> reportError;
    private readonly DispatcherTimer timer;
    private StackPanel? parent;
    private FrameworkElement? picker;
    private UIElement? anchor;
    private Border? toolbar;
    private string? lastStatus;
    private bool disposed;

    internal FrameworkElement? ChatPicker => picker;

    internal ChatToolbarIntegration(Action<FrameworkElement> configureTheme, Action<string> reportStatus,
        Action<Exception> reportError)
    {
        this.configureTheme = configureTheme ?? throw new ArgumentNullException(nameof(configureTheme));
        this.reportStatus = reportStatus ?? throw new ArgumentNullException(nameof(reportStatus));
        this.reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += Tick;
        timer.Start();
    }

    private void Tick(object? sender, EventArgs args)
    {
        try
        {
            Refresh(Application.Current.Windows.Cast<Window>());
        }
        catch (InvalidOperationException error) { ReportFailure(error); }
        catch (ArgumentException error) { ReportFailure(error); }
    }

    private void ReportFailure(Exception error)
    {
        Detach();
        if (lastStatus == error.GetType().FullName) return;
        lastStatus = error.GetType().FullName;
        reportError(error);
    }

    internal void Refresh(IEnumerable<Window> windows)
    {
        timer.Dispatcher.VerifyAccess();
        if (disposed) return;
        var candidates = new List<FrameworkElement>();
        int visited = 0;
        foreach (Window window in windows)
        {
            if (!window.IsVisible) continue;
            var pending = new Stack<DependencyObject>();
            pending.Push(window);
            while (pending.Count != 0)
            {
                if (++visited > DiscoveryLimit)
                {
                    Detach();
                    SetStatus("Toolbar discovery limit reached; no placement performed.");
                    return;
                }
                DependencyObject current = pending.Pop();
                if (current is FrameworkElement element)
                {
                    if (AutomationProperties.GetAutomationId(element) == ToolbarId) continue;
                    if (element.GetType().Name == "HarnessToolPicker" && element.IsVisible)
                    {
                        candidates.Add(element);
                        if (candidates.Count > 1)
                        {
                            Detach();
                            SetStatus("Multiple Copilot tool pickers found; toolbar placement is ambiguous.");
                            return;
                        }
                    }
                }
                for (int child = VisualTreeHelper.GetChildrenCount(current) - 1; child >= 0; child--)
                    pending.Push(VisualTreeHelper.GetChild(current, child));
            }
        }

        if (candidates.Count == 0)
        {
            Detach();
            SetStatus("Waiting for a supported Copilot Chat toolbar.");
            return;
        }
        FrameworkElement candidate = candidates[0];
        if (candidate == picker && parent != null && anchor != null && toolbar != null &&
            TryFindHost(candidate, out StackPanel? currentHost, out UIElement? currentBranch) &&
            currentHost == parent && currentBranch == anchor &&
            !parent.Children.OfType<FrameworkElement>().Any(child =>
                child != toolbar && AutomationProperties.GetAutomationId(child) == ToolbarId) &&
            picker.IsLoaded && toolbar.IsLoaded && parent.Children.Contains(anchor) && parent.Children.Contains(toolbar) &&
            parent.Children.IndexOf(toolbar) + 1 == parent.Children.IndexOf(anchor)) return;

        Detach();
        if (!TryFindHost(candidate, out StackPanel? host, out UIElement? branch) || host == null || branch == null)
        {
            SetStatus("Copilot tool picker layout is unsupported; toolbar not inserted.");
            return;
        }
        if (host.Children.OfType<FrameworkElement>().Any(child =>
            AutomationProperties.GetAutomationId(child) == ToolbarId))
        {
            SetStatus("A voice toolbar already exists at the insertion point; duplicate suppressed.");
            return;
        }

        Border view = CreateToolbar();
        host.Children.Insert(host.Children.IndexOf(branch), view);
        parent = host;
        picker = candidate;
        anchor = branch;
        toolbar = view;
        SetStatus("Toolbar preview attached before Customize AI; backend-dependent controls are disabled.");
    }

    private static bool TryFindHost(FrameworkElement candidate, out StackPanel? host, out UIElement? branch)
    {
        branch = candidate;
        for (int depth = 0; depth < 8; depth++)
        {
            DependencyObject owner = VisualTreeHelper.GetParent(branch);
            if (owner is StackPanel stack && stack.Orientation == Orientation.Horizontal)
            {
                host = stack;
                return true;
            }
            // Cross only single-child wrappers, never a container shared with unrelated controls.
            if (!(owner is UIElement wrapper) || owner is Window || VisualTreeHelper.GetChildrenCount(owner) != 1)
                break;
            branch = wrapper;
        }
        host = null;
        branch = null;
        return false;
    }

    private Border CreateToolbar()
    {
        var view = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var group = new Border
        {
            Child = view, Height = 24, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(0, 0, 4, 0), Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true, UseLayoutRounding = true
        };
        AutomationProperties.SetAutomationId(group, ToolbarId);
        AutomationProperties.SetName(group, "Copilot Chat Voice preview");
        configureTheme(group);
        AddButton(view, new Button(), "VsCopilotChatVoiceMic", "Microphone unavailable", "\uE720",
            "Voice dictation is not connected in this preview. VoiceType integration is planned.");
        AddButton(view, new ToggleButton { IsChecked = false }, "VsCopilotChatVoiceSpeaker", "Automatic read-aloud unavailable", "\uE767",
            "The reader bridge and saved Speaker state are not connected in this preview.");
        AddButton(view, new Button(), "VsCopilotChatVoiceSettings", "Voice settings unavailable", "\uE713",
            "Voice, speed, volume and speech-mode settings are not implemented in this preview.");
        return group;
    }

    private void AddButton(Panel view, ButtonBase button, string id, string name, string glyph, string explanation)
    {
        button.Width = 28;
        button.Height = 22;
        button.MinWidth = 0;
        button.MinHeight = 0;
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Padding = new Thickness(3);
        button.Margin = new Thickness(1, 0, 1, 0);
        button.IsEnabled = false;
        button.ToolTip = explanation;
        button.Content = new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetHelpText(button, explanation);
        ToolTipService.SetShowOnDisabled(button, true);
        configureTheme(button);
        view.Children.Add(button);
    }

    private void SetStatus(string message)
    {
        if (lastStatus == message) return;
        lastStatus = message;
        reportStatus(message);
    }

    private void Detach()
    {
        if (parent != null && toolbar != null) parent.Children.Remove(toolbar);
        parent = null;
        picker = null;
        anchor = null;
        toolbar = null;
    }

    public void Dispose()
    {
        timer.Dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        timer.Stop();
        timer.Tick -= Tick;
        Detach();
    }
}