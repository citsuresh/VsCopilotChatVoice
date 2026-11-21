using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

// The pane checks the VS main thread; injectable sinks also verify their owning WPF dispatcher.
#pragma warning disable VSTHRD010

namespace VsCopilotChatVoice.VisualStudio;

// Advise adds an owned sink; it never replaces the pane's existing VSFPROPID_ViewHelper.
[ComVisible(true)]
internal sealed class ReaderFrameLifecycle : IVsWindowFrameNotify, IVsWindowFrameNotify2, IVsWindowFrameNotify3, IDisposable
{
    private readonly NativeReaderView view;
    private IVsWindowFrame2? frame;
    private uint cookie;
    private bool disposed;

    internal ReaderFrameLifecycle(NativeReaderView view) { this.view = view; }
    internal void Attach(object owningFrame)
    {
        view.Dispatcher.VerifyAccess();
        if (disposed) throw new ObjectDisposedException(nameof(ReaderFrameLifecycle));
        Detach();
        if (!(owningFrame is IVsWindowFrame2 notifications))
        { view.SetFrameAvailable(false, "The owning frame does not expose IVsWindowFrame2.Advise."); return; }
        int result = notifications.Advise(this, out uint registration);
        if (ErrorHandler.Failed(result))
        { view.SetFrameAvailable(false, "IVsWindowFrame2.Advise failed (" + result + ")."); return; }
        frame = notifications; cookie = registration;
        view.SetFrameAvailable(true);
    }
    public int OnClose(ref uint pgrfSaveOptions)
    {
        view.Dispatcher.VerifyAccess();
        if (!disposed) view.FrameClosed();
        return VSConstants.S_OK;
    }
    public int OnShow(int fShow) => VSConstants.S_OK;
    public int OnMove() => VSConstants.S_OK;
    public int OnSize() => VSConstants.S_OK;
    public int OnDockableChange(int fDockable) => VSConstants.S_OK;
    public int OnMove(int x, int y, int w, int h) => VSConstants.S_OK;
    public int OnSize(int x, int y, int w, int h) => VSConstants.S_OK;
    public int OnDockableChange(int fDockable, int x, int y, int w, int h) => VSConstants.S_OK;
    private void Detach()
    {
        IVsWindowFrame2? previous = frame; frame = null;
        if (previous == null) return;
        int result = previous.Unadvise(cookie); cookie = 0;
        if (ErrorHandler.Failed(result)) view.SetFrameAvailable(false, "IVsWindowFrame2.Unadvise failed (" + result + ").");
    }
    public void Dispose()
    {
        view.Dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        Detach();
    }
}
