using System;

namespace VsCopilotChatVoice.Gestures;

internal enum GestureKey { RightCtrl, LeftCtrl, Escape, Other }
internal enum GestureMode { Hold, Toggle }
internal enum GestureState { Idle, Starting, Listening, Stopping, Transcribing, Cancelling }
internal enum GestureAction { None, RequestCapture, FinalizeCapture, CancelCapture, AcceptResult }

internal readonly struct GestureDecision
{
    internal GestureDecision(GestureAction action, long generation, string reason)
    {
        Action = action;
        Generation = generation;
        Reason = reason;
    }

    internal GestureAction Action { get; }
    internal long Generation { get; }
    internal string Reason { get; }
}

// All inputs and backend acknowledgements must be serialized by the caller.
internal sealed class CtrlGestureDetector
{
    private enum Press { None, Start, Stop }
    private readonly GestureMode mode;
    private readonly GestureKey control;
    private Press press;
    private bool controlDown;
    private bool finishWhenReady;
    private long sequence;

    internal CtrlGestureDetector(GestureMode mode = GestureMode.Hold, GestureKey control = GestureKey.RightCtrl)
    {
        if (mode != GestureMode.Hold && mode != GestureMode.Toggle) throw new ArgumentOutOfRangeException(nameof(mode));
        if (control != GestureKey.RightCtrl && control != GestureKey.LeftCtrl) throw new ArgumentOutOfRangeException(nameof(control));
        this.mode = mode;
        this.control = control;
    }

    internal GestureState State { get; private set; }
    internal long Generation { get; private set; }

    private GestureDecision Decision(GestureAction action, string reason) => new GestureDecision(action, Generation, reason);

    internal GestureDecision KeyDown(GestureKey key, bool repeat = false, bool otherInputsHeld = false)
    {
        if (key == GestureKey.Escape) return Cancel("Escape");
        if (key != control) return InvalidatePress("OtherInput");
        if (repeat || controlDown) return Decision(GestureAction.None, "RepeatIgnored");
        controlDown = true;
        if (otherInputsHeld) return Decision(GestureAction.None, "HeldInputIgnored");
        if (State == GestureState.Idle)
        {
            Generation = checked(++sequence);
            State = GestureState.Starting;
            press = Press.Start;
            finishWhenReady = false;
            return Decision(GestureAction.RequestCapture, "SolitaryDown");
        }
        if (mode == GestureMode.Toggle && (State == GestureState.Starting || State == GestureState.Listening) &&
            !finishWhenReady)
        {
            press = Press.Stop;
            return Decision(GestureAction.None, "TentativeStopTap");
        }
        return Decision(GestureAction.None, "BusyIgnored");
    }

    internal GestureDecision KeyUp(GestureKey key, bool otherInputsHeld = false)
    {
        if (key == GestureKey.Escape) return Cancel("EscapeRelease");
        if (key != control) return InvalidatePress("OtherRelease");
        controlDown = false;
        if (otherInputsHeld) return InvalidatePress("HeldInputAtRelease");
        Press released = press;
        press = Press.None;
        if (released == Press.None) return Decision(GestureAction.None, "ReleaseIgnored");
        if (mode == GestureMode.Toggle && released == Press.Start)
            return Decision(GestureAction.None, "ToggleConfirmed");
        if (State == GestureState.Starting)
        {
            finishWhenReady = true;
            return Decision(GestureAction.None, "ReleaseBeforeReady");
        }
        if (State == GestureState.Listening)
        {
            State = GestureState.Stopping;
            return Decision(GestureAction.FinalizeCapture, "SolitaryRelease");
        }
        return Decision(GestureAction.None, "ReleaseIgnored");
    }

    internal GestureDecision MouseDown() => InvalidatePress("MouseInput");

    private GestureDecision InvalidatePress(string reason)
    {
        Press invalidated = press;
        press = Press.None;
        if (invalidated == Press.Start) return Cancel(reason);
        return Decision(GestureAction.None, invalidated == Press.Stop ? "StopTapInvalidated" : reason);
    }

    internal GestureDecision FocusLost()
    {
        controlDown = false;
        return Cancel("FocusLost");
    }

    internal GestureDecision Cancel(string reason)
    {
        if (string.IsNullOrEmpty(reason)) throw new ArgumentException("Cancellation requires a reason.", nameof(reason));
        press = Press.None;
        finishWhenReady = false;
        if (State == GestureState.Idle || State == GestureState.Cancelling)
            return Decision(GestureAction.None, reason);
        State = GestureState.Cancelling;
        return Decision(GestureAction.CancelCapture, reason);
    }

    internal GestureDecision CaptureReady(long generation)
    {
        if (generation != Generation || State != GestureState.Starting)
            return Decision(GestureAction.None, "LateReadyIgnored");
        State = finishWhenReady ? GestureState.Stopping : GestureState.Listening;
        return Decision(finishWhenReady ? GestureAction.FinalizeCapture : GestureAction.None,
            finishWhenReady ? "FinalizeAfterReady" : "CaptureReady");
    }

    internal GestureDecision CaptureStopped(long generation)
    {
        if (generation != Generation || (State != GestureState.Stopping && State != GestureState.Cancelling))
            return Decision(GestureAction.None, "LateStopIgnored");
        bool cancelled = State == GestureState.Cancelling;
        State = cancelled ? GestureState.Idle : GestureState.Transcribing;
        GestureDecision result = Decision(GestureAction.None, cancelled ? "Cancelled" : "CaptureStopped");
        if (cancelled) Generation = 0;
        return result;
    }

    internal GestureDecision ResultReady(long generation)
    {
        if (generation != Generation || State != GestureState.Transcribing)
            return Decision(GestureAction.None, "LateResultIgnored");
        State = GestureState.Idle;
        GestureDecision result = Decision(GestureAction.AcceptResult, "ResultAccepted");
        Generation = 0;
        return result;
    }
}