using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

internal static class ToolbarRegressionTests
{
    private const string ToolbarId = "VsCopilotChatVoiceToolbar";
    private static int assertions;

    internal static int Run(string assemblyPath)
    {
        Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        Type type = assembly.GetType("VsCopilotChatVoice.VisualStudio.ChatToolbarIntegration", true);
        ConstructorInfo constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(Action<FrameworkElement>), typeof(Action<string>), typeof(Action<Exception>) }, null);
        MethodInfo refresh = type.GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic);
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var statuses = new List<string>();
        var errors = new List<Exception>();
        int themed = 0;
        Action<FrameworkElement> configureTheme = element =>
        {
            if (element is Border group)
            {
                group.BorderBrush = Brushes.Gray;
                return;
            }
            var control = (Control)element;
            themed++;
            var style = new Style(typeof(Control));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 75.0));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 42.0));
            control.Style = style;
        };
        var integration = (IDisposable)constructor.Invoke(new object[]
        {
            configureTheme, new Action<string>(statuses.Add), new Action<Exception>(errors.Add)
        });
        Action<Window[]> update = windows =>
        {
            refresh.Invoke(integration, new object[] { windows });
            Dispatcher.CurrentDispatcher.Invoke(new Action(delegate { }), DispatcherPriority.ApplicationIdle);
        };
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        var existing = new Button { Content = "Existing tool" };
        var picker = new HarnessToolPicker();
        host.Children.Add(existing);
        host.Children.Add(picker);
        var window = TestWindow(host);
        Window second = null;
        try
        {
            window.Show();
            update(new[] { window });
            Border toolbar = FindToolbar(host);
            Check(toolbar != null, "Toolbar was not inserted");
            var buttons = (StackPanel)toolbar.Child;
            Check(toolbar.BorderThickness == new Thickness(1) && toolbar.CornerRadius.TopLeft > 0 && toolbar.BorderBrush != null,
                "Missing themed border around the button group");
            Check(toolbar.ActualHeight == 24 && toolbar.Padding.Top == 0 && toolbar.Padding.Bottom == 0,
                "Group border height differs from the compact 24-DIP toolbar target: " + toolbar.ActualHeight);
            Check(host.Children.IndexOf(toolbar) + 1 == host.Children.IndexOf(picker), "Toolbar is not immediately before the picker");
            Check(host.Children[0] == existing, "Existing control moved");
            Check(buttons.Children.Count == 3 && themed == 3, "Wrong control or theme-callback count");
            string[] ids = { "VsCopilotChatVoiceMic", "VsCopilotChatVoiceSpeaker", "VsCopilotChatVoiceSettings" };
            for (int i = 0; i < ids.Length; i++)
            {
                var button = buttons.Children[i] as ButtonBase;
                Check(button != null && AutomationProperties.GetAutomationId(button) == ids[i], "Wrong toolbar order");
                Check(i == 2 ? button.IsEnabled : !button.IsEnabled, i == 2 ? "Settings control was not enabled" : "Unconnected backend control was enabled");
                Check(button.ActualWidth == 28 && button.ActualHeight == 22, "Theme minimums expanded an icon-only button");
                Check(button.Content is TextBlock glyph && glyph.FontSize == 14 && glyph.ActualHeight <= button.ActualHeight,
                    "Tighter group changed the icon size or clipped its height");
                Check(button.BorderThickness == new Thickness(0) && button.Background == Brushes.Transparent,
                    "Toolbar icon has an individual dialog-style border or background");
                Check(!String.IsNullOrEmpty(AutomationProperties.GetName(button)), "Missing accessible name");
                Check(!String.IsNullOrEmpty(AutomationProperties.GetHelpText(button)), "Missing unavailable-state explanation");
                Check(i == 2 ? button.ToolTip != null : ToolTipService.GetShowOnDisabled(button) && button.ToolTip != null, i == 2 ? "Settings control has no tooltip" : "Disabled control cannot explain its state");
            }
            Check(buttons.Children[1] is ToggleButton && ((ToggleButton)buttons.Children[1]).IsChecked == false,
                "Speaker is not an unchecked preview toggle");
            int reports = statuses.Count;
            update(new[] { window });
            Check(FindToolbar(host) == toolbar && themed == 3, "Repeated refresh duplicated or replaced the toolbar");
            Check(statuses.Count == reports, "Stable placement repeatedly logged the same status");

            Check(picker.IsLoaded && toolbar.IsLoaded, "Orientation test requires a settled attachment");
            host.Orientation = Orientation.Vertical;
            update(new[] { window });
            Check(FindToolbar(host) == null && statuses.Last().Contains("unsupported"),
                "Attached toolbar survived an unsupported orientation change");
            host.Orientation = Orientation.Horizontal;
            update(new[] { window });
            Check(FindToolbar(host) != null, "Restored orientation did not recover placement");

            var competingToolbar = new StackPanel();
            AutomationProperties.SetAutomationId(competingToolbar, ToolbarId);
            Check(picker.IsLoaded && FindToolbar(host).IsLoaded, "Duplicate test requires a settled attachment");
            host.Children.Insert(0, competingToolbar);
            update(new[] { window });
            Check(host.Children.OfType<FrameworkElement>().Count(child => AutomationProperties.GetAutomationId(child) == ToolbarId) == 1,
                "A competing toolbar introduced after attachment was not suppressed");
            Check(FindToolbar(host) == null && host.Children.Contains(competingToolbar),
                "Duplicate suppression did not remove only the owned toolbar");
            Check(statuses.Last().Contains("duplicate suppressed"), "New competing toolbar was not reported");
            host.Children.Remove(competingToolbar);
            update(new[] { window });
            Check(FindToolbar(host) != null, "Removing the competing toolbar did not recover placement");

            var inserted = new Button { Content = "New external control" };
            host.Children.Insert(host.Children.IndexOf(picker), inserted);
            update(new[] { window });
            Check(host.Children.IndexOf(FindToolbar(host)) + 1 == host.Children.IndexOf(picker), "Toolbar did not follow reordered anchor");
            Check(host.Children.Contains(inserted), "External control was removed");
            host.Children.Remove(picker);
            update(new[] { window });
            Check(FindToolbar(host) == null && host.Children.Contains(existing), "Chat removal left toolbar or removed an unrelated control");
            picker = new HarnessToolPicker();
            host.Children.Add(picker);
            update(new[] { window });
            Check(FindToolbar(host) != null, "Recreated chat did not receive controls");

            host.Children.Remove(picker);
            var wrapper = new Grid();
            wrapper.Children.Add(picker);
            host.Children.Add(wrapper);
            update(new[] { window });
            Check(FindToolbar(host) != null && host.Children.IndexOf(FindToolbar(host)) + 1 == host.Children.IndexOf(wrapper),
                "Single-child wrapper did not receive adjacent placement");
            Check(picker.IsLoaded && FindToolbar(host).IsLoaded, "Wrapper test requires a settled attachment");
            var wrapperSibling = new Button { Content = "Unrelated wrapper child" };
            wrapper.Children.Add(wrapperSibling);
            update(new[] { window });
            Check(FindToolbar(host) == null && statuses.Last().Contains("unsupported"),
                "Attached toolbar survived its wrapper acquiring an unrelated child");
            Check(wrapper.Children.Contains(wrapperSibling) && wrapper.Children.Contains(picker),
                "Unsupported wrapper handling removed unrelated content");
            wrapper.Children.Remove(wrapperSibling);
            update(new[] { window });
            Check(FindToolbar(host) != null, "Restored single-child wrapper did not recover placement");
            wrapper.Children.Remove(picker);
            host.Children.Remove(wrapper);
            host.Children.Add(picker);
            update(new[] { window });

            integration.Dispose();
            integration.Dispose();
            Check(FindToolbar(host) == null && host.Children.Contains(picker), "Dispose did not remove only owned controls");
            update(new[] { window });
            Check(FindToolbar(host) == null, "Disposed integration reattached");

            integration = (IDisposable)constructor.Invoke(new object[]
            {
                configureTheme, new Action<string>(statuses.Add), new Action<Exception>(errors.Add)
            });
            var foreignToolbar = new StackPanel();
            AutomationProperties.SetAutomationId(foreignToolbar, ToolbarId);
            host.Children.Insert(0, foreignToolbar);
            update(new[] { window });
            Check(host.Children.OfType<FrameworkElement>().Count(child => AutomationProperties.GetAutomationId(child) == ToolbarId) == 1,
                "Existing toolbar was duplicated");
            integration.Dispose();
            Check(host.Children.Contains(foreignToolbar), "Dispose removed another integration's controls");
            host.Children.Remove(foreignToolbar);

            integration = (IDisposable)constructor.Invoke(new object[]
            {
                configureTheme, new Action<string>(statuses.Add), new Action<Exception>(errors.Add)
            });
            var secondHost = new StackPanel { Orientation = Orientation.Horizontal };
            secondHost.Children.Add(new HarnessToolPicker());
            second = TestWindow(secondHost);
            second.Show();
            update(new[] { window, second });
            Check(FindToolbar(host) == null && FindToolbar(secondHost) == null, "Ambiguous pickers received arbitrary placement");
            Check(statuses.Last().Contains("ambiguous"), "Ambiguous layout was not reported");
            second.Close();
            second = null;

            host.Orientation = Orientation.Vertical;
            host.Children.Add(new Button { Content = "Unrelated child" });
            update(new[] { window });
            Check(FindToolbar(host) == null && statuses.Last().Contains("unsupported"), "Unsupported layout was modified or not reported");
            host.Orientation = Orientation.Horizontal;
            update(new[] { window });
            Check(FindToolbar(host) != null, "Supported layout did not recover");
            second = TestWindow(secondHost);
            second.Show();
            update(new[] { window, second });
            Check(FindToolbar(host) == null && FindToolbar(secondHost) == null, "New ambiguity did not detach an existing toolbar");
            second.Close();
            second = null;
            update(new[] { window });
            Check(FindToolbar(host) != null, "Resolved ambiguity did not restore the toolbar");
            window.Hide();
            update(new[] { window });
            Check(FindToolbar(host) == null, "Hidden chat retained attached controls");
            Check(errors.Count == 0, "Normal lifecycle produced an exception notification");
            Console.WriteLine("PASS: " + assertions + " toolbar placement/lifecycle assertions; real VSIX assembly; no speech or user preferences.");
            return 0;
        }
        finally
        {
            integration.Dispose();
            if (second != null) second.Close();
            window.Close();
            GC.KeepAlive(application);
        }
    }

    private static Border FindToolbar(Panel host)
    {
        return host.Children.OfType<Border>().SingleOrDefault(child => AutomationProperties.GetAutomationId(child) == ToolbarId);
    }

    private static Window TestWindow(UIElement content)
    {
        return new Window
        {
            Content = content, Left = -10000, Top = -10000, Width = 400, Height = 100,
            ShowActivated = false, ShowInTaskbar = false, Title = "Toolbar regression test"
        };
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class HarnessToolPicker : ContentControl
    {
        internal HarnessToolPicker() { Content = new Button { Content = "Customize AI" }; }
    }
}