using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VsCopilotChatVoice.Gestures;

internal static class GestureRegressionTests
{
    private static int assertions;

    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length > 2) throw new ArgumentException("Use --gesture-probe [built-VSIX-DLL].");
            Hold();
            Toggle();
            MissingKeyDown();
            CancellationAndRaces();
            Ownership();
            Processes();
            if (args.Length == 2) Observer(args[1]);
            Console.WriteLine("PASS: gesture checks, " + assertions + " assertions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class FakeCaptureOwner
    {
        internal readonly CtrlGestureDetector Detector;
        internal int Requests, Stops, Cancels, Results;
        internal long Requested;

        internal FakeCaptureOwner(GestureMode mode = GestureMode.Hold, GestureKey key = GestureKey.RightCtrl)
        {
            Detector = new CtrlGestureDetector(mode, key);
        }

        internal GestureDecision Apply(GestureDecision decision)
        {
            if (decision.Action == GestureAction.RequestCapture) { Requests++; Requested = decision.Generation; }
            if (decision.Action == GestureAction.FinalizeCapture) Stops++;
            if (decision.Action == GestureAction.CancelCapture) Cancels++;
            if (decision.Action == GestureAction.AcceptResult) Results++;
            return decision;
        }

        internal GestureDecision Down(GestureKey key = GestureKey.RightCtrl, bool repeat = false, bool held = false) =>
            Apply(Detector.KeyDown(key, repeat, held));
        internal GestureDecision Up(GestureKey key = GestureKey.RightCtrl, bool held = false) => Apply(Detector.KeyUp(key, held));
        internal void Ready(long generation) => Apply(Detector.CaptureReady(generation));
        internal void Stopped(long generation) => Apply(Detector.CaptureStopped(generation));
        internal void Result(long generation) => Apply(Detector.ResultReady(generation));
    }

    private static void Hold()
    {
        var f = new FakeCaptureOwner();
        Check(f.Detector.State == GestureState.Idle && f.Detector.Generation == 0, "Not initially idle.");
        Check(f.Down().Action == GestureAction.RequestCapture && f.Requests == 1 && f.Detector.State == GestureState.Starting,
            "Capture must be requested directly on down, not after a delay or release.");
        long generation = f.Requested;
        for (int i = 0; i < 20; i++)
            Check(f.Down(repeat: true).Action == GestureAction.None && f.Requests == 1, "Repeat started another capture.");
        Check(f.Down().Reason == "RepeatIgnored", "Duplicate down was not suppressed.");
        f.Ready(generation);
        Check(f.Detector.State == GestureState.Listening && f.Stops == 0, "Ready did not begin listening.");
        Check(f.Up().Action == GestureAction.FinalizeCapture && f.Stops == 1, "Hold release did not finalize.");
        Check(f.Up().Action == GestureAction.None && f.Stops == 1, "Duplicate release finalized twice.");
        f.Stopped(generation);
        Check(f.Detector.State == GestureState.Transcribing, "Stop did not await the final result.");
        f.Result(generation);
        Check(f.Results == 1 && f.Detector.State == GestureState.Idle, "Result did not complete once.");
        f.Result(generation);
        Check(f.Results == 1, "Duplicate result was accepted.");

        foreach (GestureKey key in new[] { GestureKey.LeftCtrl, GestureKey.Other })
        {
            f = new FakeCaptureOwner();
            Check(f.Down(key).Action == GestureAction.None && f.Requests == 0, "Wrong key activated the default gesture.");
        }
        f = new FakeCaptureOwner(key: GestureKey.LeftCtrl);
        Check(f.Down(GestureKey.RightCtrl).Action == GestureAction.None, "Right Ctrl activated Left Ctrl configuration.");
        Check(f.Down(GestureKey.LeftCtrl).Action == GestureAction.RequestCapture, "Configured Left Ctrl did not start.");

        f = new FakeCaptureOwner();
        Check(f.Down(held: true).Reason == "HeldInputIgnored" && f.Requests == 0, "Already-held key/modifier/mouse activated.");
        Check(f.Down().Action == GestureAction.None, "An ignored held press activated mid-press.");
        f.Up();
        Check(f.Down().Action == GestureAction.RequestCapture, "A later solitary gesture did not recover.");
        f = new FakeCaptureOwner();
        Check(f.Down(repeat: true).Action == GestureAction.None && f.Requests == 0, "Unpaired repeat activated capture.");

        foreach (bool ready in new[] { false, true })
        foreach (bool mouse in new[] { false, true })
        {
            f = new FakeCaptureOwner(); f.Down(); generation = f.Requested;
            if (ready) f.Ready(generation);
            f.Apply(mouse ? f.Detector.MouseDown() : f.Detector.KeyDown(GestureKey.Other));
            Check(f.Cancels == 1 && f.Detector.State == GestureState.Cancelling, "Shortcut/mouse did not cancel tentative hold.");
            f.Up(); f.Ready(generation); f.Stopped(generation); f.Result(generation);
            Check(f.Stops == 0 && f.Results == 0 && f.Detector.State == GestureState.Idle, "Invalidated hold finalized or accepted text.");
        }
    }

    private static void Toggle()
    {
        foreach (bool ready in new[] { false, true })
        {
            var f = new FakeCaptureOwner(GestureMode.Toggle);
            f.Down(); long g = f.Requested;
            if (ready) f.Ready(g);
            Check(f.Up().Reason == "ToggleConfirmed" && f.Stops == 0, "Toggle tap did not confirm on key-up.");
            if (!ready) f.Ready(g);
            Check(f.Detector.State == GestureState.Listening, "Confirmed toggle did not keep running.");
            foreach (GestureKey modifier in new[] { GestureKey.RightCtrl, GestureKey.LeftCtrl })
            {
                f.Down(modifier); f.Down(GestureKey.Other); f.Up(modifier);
                Check(f.Cancels == 0 && f.Stops == 0 && f.Detector.State == GestureState.Listening,
                    "Ctrl shortcut stopped an established toggle.");
            }
            f.Down(); f.Apply(f.Detector.MouseDown()); f.Up();
            Check(f.Cancels == 0 && f.Stops == 0, "Mouse input stopped an established toggle.");
            f.Down();
            Check(f.Stops == 0, "Second toggle tap stopped before key-up.");
            Check(f.Up().Action == GestureAction.FinalizeCapture && f.Stops == 1, "Second solitary tap did not stop toggle.");
            f.Stopped(g); f.Result(g);
            Check(f.Results == 1, "Toggle result not accepted.");
        }

        var delayed = new FakeCaptureOwner(GestureMode.Toggle);
        delayed.Down(); long generation = delayed.Requested; delayed.Up(); delayed.Down(); delayed.Up();
        Check(delayed.Stops == 0 && delayed.Detector.State == GestureState.Starting, "Second tap before ready raced startup.");
        delayed.Ready(generation);
        Check(delayed.Stops == 1 && delayed.Detector.State == GestureState.Stopping, "Pending toggle stop was lost.");
        delayed = new FakeCaptureOwner(GestureMode.Toggle);
        delayed.Down(); generation = delayed.Requested; delayed.Down(GestureKey.Other); delayed.Up();
        delayed.Ready(generation); delayed.Stopped(generation); delayed.Result(generation);
        Check(delayed.Cancels == 1 && delayed.Results == 0, "Initial toggle Ctrl combination was accepted.");
    }

    private static void MissingKeyDown()
    {
        foreach (GestureMode mode in new[] { GestureMode.Hold, GestureMode.Toggle })
        foreach (bool ctrlReleasedFirst in new[] { false, true })
        foreach (bool ready in new[] { false, true })
        {
            var f = new FakeCaptureOwner(mode);
            f.Down(); long generation = f.Requested;
            if (ready) f.Ready(generation);
            if (ctrlReleasedFirst) { f.Up(held: true); f.Up(GestureKey.Other); }
            else { f.Up(GestureKey.Other); f.Up(); }
            Check(f.Cancels == 1 && f.Stops == 0, "Shortcut with suppressed key-down confirmed a tentative gesture.");
            f.Ready(generation); f.Stopped(generation); f.Result(generation);
            Check(f.Detector.State == GestureState.Idle && f.Results == 0, "Suppressed-down shortcut produced a result.");
        }

        foreach (bool ctrlReleasedFirst in new[] { false, true })
        foreach (bool ready in new[] { false, true })
        {
            var f = new FakeCaptureOwner(GestureMode.Toggle);
            f.Down(); long generation = f.Requested; f.Up();
            if (ready) f.Ready(generation);
            f.Down();
            if (ctrlReleasedFirst) { f.Up(held: true); f.Up(GestureKey.Other); }
            else { f.Up(GestureKey.Other); f.Up(); }
            Check(f.Cancels == 0 && f.Stops == 0, "Suppressed shortcut key-down stopped an established toggle.");
            if (!ready) f.Ready(generation);
            Check(f.Detector.State == GestureState.Listening, "Established toggle did not stay listening after shortcut.");
            f.Down(); f.Up();
            Check(f.Stops == 1, "A later solitary stop tap did not recover after suppressed-down shortcut.");
        }

        foreach (GestureMode mode in new[] { GestureMode.Hold, GestureMode.Toggle })
        foreach (int stage in new[] { 0, 1, 2, 3 })
        {
            var f = new FakeCaptureOwner(mode); f.Down(); long generation = f.Requested;
            if (stage >= 1) f.Ready(generation);
            if (stage >= 2) { f.Up(); if (mode == GestureMode.Toggle) { f.Down(); f.Up(); } }
            if (stage >= 3) f.Stopped(generation);
            Check(f.Up(GestureKey.Escape).Action == GestureAction.CancelCapture,
                "Escape key-up did not cancel when its key-down was suppressed.");
            f.Up(); f.Ready(generation); f.Result(generation); f.Stopped(generation); f.Result(generation);
            Check(f.Detector.State == GestureState.Idle && f.Results == 0, "Escape release allowed a late result.");
        }
    }

    private static void CancellationAndRaces()
    {
        var f = new FakeCaptureOwner(); f.Down(); long g = f.Requested;
        Check(f.Up().Reason == "ReleaseBeforeReady" && f.Stops == 0, "Startup release was not retained.");
        f.Ready(g);
        Check(f.Stops == 1 && f.Detector.State == GestureState.Stopping, "Ready did not honor early release.");
        f.Ready(g); Check(f.Stops == 1, "Duplicate ready stopped twice.");

        foreach (GestureMode mode in new[] { GestureMode.Hold, GestureMode.Toggle })
        foreach (int stage in new[] { 0, 1, 2, 3 })
        foreach (bool focus in new[] { false, true })
        {
            f = new FakeCaptureOwner(mode); f.Down(); g = f.Requested;
            if (stage >= 1) f.Ready(g);
            if (stage >= 2)
            {
                f.Up();
                if (mode == GestureMode.Toggle) { f.Down(); f.Up(); }
            }
            if (stage >= 3) f.Stopped(g);
            f.Apply(focus ? f.Detector.FocusLost() : f.Detector.KeyDown(GestureKey.Escape));
            Check(f.Cancels == 1 && f.Detector.State == GestureState.Cancelling, "Esc/focus did not cancel lifecycle stage.");
            f.Up(); f.Ready(g); f.Result(g); f.Stopped(g); f.Result(g);
            Check(f.Results == 0 && f.Detector.State == GestureState.Idle, "Cancelled/late callback revived a result.");
            f.Down(); long next = f.Requested;
            Check(next > g && f.Requests == 2, "Cancellation prevented a later fresh gesture.");
            f.Ready(g); f.Stopped(g); f.Result(g);
            Check(f.Detector.State == GestureState.Starting && f.Detector.Generation == next, "Old generation corrupted new capture.");
        }

        f = new FakeCaptureOwner(); f.Down(); g = f.Requested;
        f.Apply(f.Detector.Cancel("OwnershipUnavailable")); f.Down();
        Check(f.Requests == 1, "Another request overlapped cancellation acknowledgement.");
        f.Stopped(g); f.Up(); f.Down();
        Check(f.Requests == 2, "Rejected ownership did not recover after release.");
        f = new FakeCaptureOwner();
        Check(f.Up().Action == GestureAction.None && f.Detector.FocusLost().Action == GestureAction.None,
            "Unpaired release/focus loss activated anything.");
    }

    private static string NewName() => @"Local\VsCopilotChatVoice.Recording.Test." + Guid.NewGuid().ToString("N");

    private static void Ownership()
    {
        string name = NewName();
        using (var first = new RecordingOwnership(name))
        using (var second = new RecordingOwnership(name))
        {
            Check(first.TryAcquire() == OwnershipResult.Acquired, "First owner did not acquire.");
            Check(first.TryAcquire() == OwnershipResult.Busy && second.TryAcquire() == OwnershipResult.Busy,
                "Same-thread recursive kernel acquire created two owners.");
            bool wrongThread = Task.Run(() =>
            {
                try { first.Release(); return false; }
                catch (InvalidOperationException) { return true; }
            }).GetAwaiter().GetResult();
            Check(wrongThread && first.IsOwner, "Foreign thread released ownership.");
            first.Release();
            Check(!first.IsOwner && second.TryAcquire() == OwnershipResult.Acquired, "Release did not permit next owner.");
            second.Dispose();
            Check(first.TryAcquire() == OwnershipResult.Acquired, "Dispose retained ownership.");
        }
        bool invalid = false;
        try { using (var unused = new RecordingOwnership("Global\\invalid")) { } }
        catch (ArgumentException) { invalid = true; }
        Check(invalid, "Invalid/global ownership name accepted.");
    }

    private static Process Child(string name, string mode)
    {
        return Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
            "--gesture-owner-child " + name + " " + mode)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        });
    }

    private static string Line(Process child)
    {
        Task<string> read = child.StandardOutput.ReadLineAsync();
        if (!read.Wait(5000)) throw new TimeoutException("Ownership child did not acknowledge within 5 seconds.");
        if (read.Result == null) throw new InvalidOperationException("Ownership child exited: " + child.StandardError.ReadToEnd());
        return read.Result;
    }

    private static void Finish(Process child)
    {
        if (!child.HasExited)
        {
            child.StandardInput.WriteLine("release"); child.StandardInput.Flush();
            if (!child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(5000); }
        }
        child.Dispose();
    }

    private static void Processes()
    {
        string name = NewName();
        using (var observer = new RecordingOwnership(name))
        {
            Process child = Child(name, "hold");
            try
            {
                Check(Line(child) == "Acquired", "Child did not acquire the real named mutex.");
                Stopwatch elapsed = Stopwatch.StartNew();
                Check(observer.TryAcquire() == OwnershipResult.Busy, "A live other process was taken over.");
                Check(elapsed.ElapsedMilliseconds < 500, "Competing acquire blocked instead of returning immediately.");
                child.StandardInput.WriteLine("release"); child.StandardInput.Flush();
                Check(child.WaitForExit(5000) && child.ExitCode == 0, "Child did not release normally.");
                Check(observer.TryAcquire() == OwnershipResult.Acquired, "Release in another process did not free ownership.");
                observer.Release();
            }
            finally { Finish(child); }

            child = Child(name, "hold");
            try
            {
                Check(Line(child) == "Acquired", "Crash fixture did not own mutex.");
                child.StandardInput.WriteLine("abandon"); child.StandardInput.Flush();
                Check(child.WaitForExit(5000), "Crash fixture did not exit.");
                Check(observer.TryAcquire() == OwnershipResult.AcquiredAfterAbandonment && observer.IsOwner,
                    "Abandoned mutex did not become a reported crashed-owner acquisition.");
                observer.Release();
            }
            finally { Finish(child); }
        }

        name = NewName();
        Process a = Child(name, "race"), b = Child(name, "race");
        try
        {
            Check(Line(a) == "Ready" && Line(b) == "Ready", "Race children did not become ready.");
            a.StandardInput.WriteLine("start"); a.StandardInput.Flush();
            b.StandardInput.WriteLine("start"); b.StandardInput.Flush();
            string ar = Line(a), br = Line(b);
            Check((ar == "Acquired" && br == "Busy") || (ar == "Busy" && br == "Acquired"),
                "Two racing real processes did not elect exactly one owner.");
            Process winner = ar == "Acquired" ? a : b, loser = ar == "Busy" ? a : b;
            winner.StandardInput.WriteLine("release"); winner.StandardInput.Flush();
            Check(winner.WaitForExit(5000) && winner.ExitCode == 0, "Winning child did not release.");
            loser.StandardInput.WriteLine("retry"); loser.StandardInput.Flush();
            Check(Line(loser) == "Acquired", "Losing process could not acquire after real owner release.");
        }
        finally { Finish(a); Finish(b); }
    }

    internal static int OwnerChild(string name, string mode)
    {
        try
        {
            var owner = new RecordingOwnership(name);
            if (mode == "race") { Console.WriteLine("Ready"); Console.Out.Flush(); Console.ReadLine(); }
            Console.WriteLine(owner.TryAcquire()); Console.Out.Flush();
            string command;
            while ((command = Console.ReadLine()) != null)
            {
                if (command == "abandon") Environment.Exit(0); // Deliberately leave the real mutex abandoned.
                if (command == "retry") { Console.WriteLine(owner.TryAcquire()); Console.Out.Flush(); continue; }
                if (command == "release") break;
            }
            owner.Dispose();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private sealed class HarnessSessionViewModel { }
    private sealed class HarnessToolPicker : Button { }
    private sealed class ChatSessionList : Border { }
    private sealed class WpfTextViewHost : TextBox { }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Observer(string dll)
    {
        Type type = Assembly.LoadFrom(Path.GetFullPath(dll)).GetType("VsCopilotChatVoice.VisualStudio.ChatGesturePoc", true);
        const BindingFlags methods = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
        MethodInfo enabled = type.GetMethod("IsEnabled", methods);
        Check(!(bool)enabled.Invoke(null, new object[] { new[] { "devenv", "/RootSuffix", "Exp" }, "0" }), "Opt-out gate accepted.");
        Check(!(bool)enabled.Invoke(null, new object[] { new[] { "devenv" }, "1" }), "Main-instance gate accepted.");
        Check(!(bool)enabled.Invoke(null, new object[] { new[] { "devenv", "/RootSuffix", "ExpOther" }, "1" }), "Non-Exp suffix accepted.");
        Check((bool)enabled.Invoke(null, new object[] { new[] { "devenv", "/rootsuffix", "exp" }, "1" }), "Debug Exp opt-in refused.");

        var picker = new HarnessToolPicker();
        var input = new WpfTextViewHost { Text = "PRIVATE_DRAFT" };
        var panel = new StackPanel { DataContext = new HarnessSessionViewModel() };
        panel.Children.Add(new ChatSessionList()); panel.Children.Add(input); panel.Children.Add(picker);
        var outside = new TextBox();
        var shell = new StackPanel(); shell.Children.Add(panel); shell.Children.Add(outside);
        var window = new Window { Content = shell, Left = -10000, Top = -10000, ShowInTaskbar = false };
        var log = new List<string>();
        var errors = new List<Exception>();
        Type modeType = type.Assembly.GetType("VsCopilotChatVoice.Gestures.GestureMode", true);
        Type keyType = type.Assembly.GetType("VsCopilotChatVoice.Gestures.GestureKey", true);
        object poc = Activator.CreateInstance(type, methods, null, new object[]
        {
            new Func<FrameworkElement>(() => picker), new Action<string>(log.Add), new Action<Exception>(errors.Add),
            Enum.Parse(modeType, "Hold"), Enum.Parse(keyType, "RightCtrl"), NewName()
        }, null);
        MethodInfo refresh = type.GetMethod("Refresh", methods);
        try
        {
            window.Show(); window.Activate(); input.Focus(); Pump();
            Check(ReferenceEquals(type.GetMethod("FindChatRoot", methods).Invoke(null, new object[] { picker }), panel),
                "Discovery selected something other than the nearest chat panel.");
            refresh.Invoke(poc, new object[] { picker });
            Check(log.Any(x => x.Contains("attached to chat panel")), "Observer did not attach.");
            PresentationSource source = PresentationSource.FromVisual(input);
            var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.RightCtrl)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent, Handled = true };
            input.RaiseEvent(down);
            Check(down.Handled && log.Any(x => x.Contains("SolitaryDown")), "Handled native event was not observed or was changed.");
            var up = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.RightCtrl)
                { RoutedEvent = Keyboard.PreviewKeyUpEvent };
            input.RaiseEvent(up); Pump();
            Check(!up.Handled && log.Any(x => x.Contains("ResultAccepted")), "Normal release was swallowed or did not finish.");

            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.RightCtrl)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent }); Pump();
            outside.Focus(); Pump();
            Check(log.Any(x => x.Contains("action=CancelCapture") && x.Contains("FocusLost")), "Chat focus loss did not cancel.");
            int before = log.Count;
            panel.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.RightCtrl)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(log.Count == before, "Event outside chat keyboard focus was accepted.");
            refresh.Invoke(poc, new object[] { null });
            input.Focus(); Pump(); before = log.Count;
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.RightCtrl)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(log.Count == before, "Detached root still observed input.");
            Check(errors.Count == 0, "Observer reported a fixture error.");
            Check(log.All(x => !x.Contains("PRIVATE_DRAFT")), "Observer logged draft content.");
        }
        finally { ((IDisposable)poc).Dispose(); window.Close(); Pump(); }
    }
}