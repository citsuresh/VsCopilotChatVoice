using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VsCopilotChatVoice.Gestures;

namespace VsCopilotChatVoice.VisualStudio;

// An Exp-only observer with a fake capture lifecycle. It never records, edits, sends or handles input.
internal sealed class ChatGesturePoc : IDisposable
{
    private readonly Func<FrameworkElement?> getPicker;
    private readonly Action<string> report;
    private readonly Action<Exception> reportError;
    private readonly CtrlGestureDetector detector;
    private readonly RecordingOwnership ownership;
    private readonly DispatcherTimer timer;
    private readonly KeyEventHandler downHandler;
    private readonly KeyEventHandler upHandler;
    private readonly MouseButtonEventHandler mouseHandler;
    private FrameworkElement? root;
    private Window? window;
    private long ownerGeneration;
    private long downCount, upCount, repeatCount, mouseCount, focusCount;
    private string? lastDiscovery;
    private bool disposed;

    internal static bool IsEnabled(string[]? args = null, string? optIn = null)
    {
#if DEBUG
        args = args ?? Environment.GetCommandLineArgs();
        return (optIn ?? Environment.GetEnvironmentVariable("VSCOPILOTCHATVOICE_GESTURE_POC")) == "1" &&
            Enumerable.Range(0, Math.Max(0, args.Length - 1)).Any(i =>
                string.Equals(args[i], "/RootSuffix", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(args[i + 1], "Exp", StringComparison.OrdinalIgnoreCase));
#else
        return false;
#endif
    }

    internal ChatGesturePoc(Func<FrameworkElement?> getPicker, Action<string> report, Action<Exception> reportError,
        GestureMode mode = GestureMode.Hold, GestureKey control = GestureKey.RightCtrl, string? ownershipName = null)
    {
        this.getPicker = getPicker ?? throw new ArgumentNullException(nameof(getPicker));
        this.report = report ?? throw new ArgumentNullException(nameof(report));
        this.reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        detector = new CtrlGestureDetector(mode, control);
        ownership = new RecordingOwnership(ownershipName);
        downHandler = KeyDown;
        upHandler = KeyUp;
        mouseHandler = MouseDown;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += Tick;
        timer.Start();
    }

    private void Tick(object? sender, EventArgs args)
    {
        timer.Dispatcher.VerifyAccess();
        try { Refresh(getPicker()); }
        catch (InvalidOperationException ex) { Fail(ex); }
        catch (ArgumentException ex) { Fail(ex); }
    }

    private void Fail(Exception error)
    {
        Detach();
        reportError(error);
    }

    internal void Refresh(FrameworkElement? picker)
    {
        timer.Dispatcher.VerifyAccess();
        if (disposed) return;
        FrameworkElement? candidate = picker == null || !picker.IsVisible ? null : FindChatRoot(picker);
        if (candidate == root && candidate != null && candidate.IsVisible && candidate.IsLoaded) return;
        Detach();
        Window? owner = candidate == null ? null : Window.GetWindow(candidate);
        if (candidate == null || owner == null || !candidate.IsLoaded)
        {
            Discovery("Gesture PoC waiting for a supported chat panel; no input observed.");
            return;
        }
        root = candidate;
        window = owner;
        root.AddHandler(Keyboard.PreviewKeyDownEvent, downHandler, true);
        root.AddHandler(Keyboard.PreviewKeyUpEvent, upHandler, true);
        root.AddHandler(Mouse.PreviewMouseDownEvent, mouseHandler, true);
        root.IsKeyboardFocusWithinChanged += FocusChanged;
        root.IsVisibleChanged += VisibilityChanged;
        root.Unloaded += Unloaded;
        root.DataContextChanged += ContextChanged;
        window.Deactivated += Deactivated;
        Discovery("Gesture PoC attached to chat panel; fake capture only, input never handled.");
    }

    private void Discovery(string message)
    {
        if (lastDiscovery == message) return;
        lastDiscovery = message;
        report(message);
    }

    internal static FrameworkElement? FindChatRoot(FrameworkElement picker)
    {
        DependencyObject? ancestor = VisualTreeHelper.GetParent(picker);
        for (int depth = 0; ancestor != null && !(ancestor is Window) && depth < 32; depth++)
        {
            if (ancestor is FrameworkElement element &&
                element.DataContext?.GetType().Name == "HarnessSessionViewModel" && HasChatParts(element))
                return element;
            ancestor = VisualTreeHelper.GetParent(ancestor);
        }
        return null;
    }

    private static bool HasChatParts(DependencyObject root)
    {
        bool input = false, transcript = false;
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        int visited = 0;
        while (pending.Count > 0)
        {
            if (++visited > 12000) return false;
            DependencyObject current = pending.Pop();
            input |= current.GetType().Name == "WpfTextViewHost";
            transcript |= current.GetType().Name == "ChatSessionList";
            if (input && transcript) return true;
            for (int i = VisualTreeHelper.GetChildrenCount(current) - 1; i >= 0; i--)
                pending.Push(VisualTreeHelper.GetChild(current, i));
        }
        return false;
    }

    private bool AcceptInput => !disposed && root != null && root.IsVisible && root.IsKeyboardFocusWithin &&
        window != null && window.IsActive;

    private static GestureKey Classify(Key key) => key == Key.RightCtrl ? GestureKey.RightCtrl :
        key == Key.LeftCtrl ? GestureKey.LeftCtrl : key == Key.Escape ? GestureKey.Escape : GestureKey.Other;

    private static bool OtherInputsHeld(Key current)
    {
        // Chat can suppress other key-down events. Sample at Ctrl down/up without retaining keys.
        foreach (Key key in Enum.GetValues(typeof(Key)))
            if (key != Key.None && key != current && key != Key.System && Keyboard.IsKeyDown(key)) return true;
        return Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed ||
            Mouse.MiddleButton == MouseButtonState.Pressed || Mouse.XButton1 == MouseButtonState.Pressed ||
            Mouse.XButton2 == MouseButtonState.Pressed;
    }

    private void KeyDown(object sender, KeyEventArgs args)
    {
        if (!AcceptInput) return;
        downCount++;
        if (args.IsRepeat) repeatCount++;
        Key key = args.Key == Key.System ? args.SystemKey : args.Key;
        Apply(detector.KeyDown(Classify(key), args.IsRepeat,
            (key == Key.RightCtrl || key == Key.LeftCtrl) && OtherInputsHeld(key)));
    }

    private void KeyUp(object sender, KeyEventArgs args)
    {
        if (!AcceptInput) return;
        upCount++;
        Key key = args.Key == Key.System ? args.SystemKey : args.Key;
        Apply(detector.KeyUp(Classify(key),
            (key == Key.RightCtrl || key == Key.LeftCtrl) && OtherInputsHeld(key)));
    }

    private void MouseDown(object sender, MouseButtonEventArgs args)
    {
        if (!AcceptInput) return;
        mouseCount++;
        Apply(detector.MouseDown());
    }

    private void FocusChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!root!.IsKeyboardFocusWithin) LoseFocus();
    }

    private void Deactivated(object? sender, EventArgs args) => LoseFocus();
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!root!.IsVisible) Detach();
    }
    private void Unloaded(object sender, RoutedEventArgs args) => Detach();
    private void ContextChanged(object sender, DependencyPropertyChangedEventArgs args) => Detach();

    private void LoseFocus()
    {
        focusCount++;
        Apply(detector.FocusLost());
    }

    private void Apply(GestureDecision decision)
    {
        report("Gesture PoC: decision=" + decision.Reason + "; action=" + decision.Action + "; state=" + detector.State +
            "; generation=" + decision.Generation + "; down=" + downCount + "; up=" + upCount +
            "; repeats=" + repeatCount + "; mouse=" + mouseCount + "; focusLoss=" + focusCount + ".");
        long generation = decision.Generation;
        if (decision.Action == GestureAction.RequestCapture)
        {
            OwnershipResult acquired = ownership.TryAcquire();
            report("Gesture PoC ownership: " + acquired + "; fake capture only.");
            if (acquired == OwnershipResult.Busy)
            {
                Apply(detector.Cancel("OwnershipUnavailable"));
                return;
            }
            ownerGeneration = generation;
            Post(() =>
            {
                if (!disposed) Apply(detector.CaptureReady(generation));
            });
        }
        else if (decision.Action == GestureAction.FinalizeCapture || decision.Action == GestureAction.CancelCapture)
        {
            bool finalize = decision.Action == GestureAction.FinalizeCapture;
            Post(() =>
            {
                if (disposed) return;
                if (ownerGeneration == generation)
                {
                    ownership.Release();
                    ownerGeneration = 0;
                }
                Apply(detector.CaptureStopped(generation));
                if (finalize)
                    Post(() =>
                    {
                        if (!disposed) Apply(detector.ResultReady(generation));
                    });
            });
        }
    }

    private void Post(Action callback)
    {
        // Deliberately enqueue fake backend acknowledgements, even when already on the UI thread.
#pragma warning disable VSTHRD001
        _ = timer.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try { callback(); }
            catch (InvalidOperationException ex) { Fail(ex); }
            catch (ArgumentException ex) { Fail(ex); }
        }));
#pragma warning restore VSTHRD001
    }

    private void Detach()
    {
        if (root == null) return;
        LoseFocus();
        root.RemoveHandler(Keyboard.PreviewKeyDownEvent, downHandler);
        root.RemoveHandler(Keyboard.PreviewKeyUpEvent, upHandler);
        root.RemoveHandler(Mouse.PreviewMouseDownEvent, mouseHandler);
        root.IsKeyboardFocusWithinChanged -= FocusChanged;
        root.IsVisibleChanged -= VisibilityChanged;
        root.Unloaded -= Unloaded;
        root.DataContextChanged -= ContextChanged;
        window!.Deactivated -= Deactivated;
        root = null;
        window = null;
    }

    public void Dispose()
    {
        timer.Dispatcher.VerifyAccess();
        if (disposed) return;
        Detach();
        disposed = true;
        timer.Stop();
        timer.Tick -= Tick;
        ownership.Dispose();
        ownerGeneration = 0;
    }
}