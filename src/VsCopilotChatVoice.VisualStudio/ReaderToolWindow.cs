using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace VsCopilotChatVoice.VisualStudio;

[Guid(WindowGuidString)]
public sealed class ReaderToolWindow : ToolWindowPane
{
    public const string WindowGuidString = "9a1e7c3d-5b2f-4e68-8d4a-7c6b5a493827";
    private ReaderFrameLifecycle? lifecycle;

    public ReaderToolWindow() : base(null)
    {
        Caption = "Copilot Voice Reader (Preview)";
        string packageFolder = Path.GetDirectoryName(typeof(ReaderToolWindow).Assembly.Location) ?? "";
        string workerPath = Path.Combine(packageFolder, "Reader", "VsCopilotChatVoice.Reader.exe");
        bool silent = Environment.GetEnvironmentVariable(ReaderToolWindowControl.SilentVariable) == "1";
        if (NativeReaderPreview.IsEnabled())
        {
            var view = NativeReaderPreview.Create(workerPath, silent, () =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ErrorHandler.ThrowOnFailure(((IVsWindowFrame)Frame).Hide());
            }, OpenSettings);
            Content = view;
            lifecycle = new ReaderFrameLifecycle(view);
        }
        else Content = new ReaderToolWindowControl(workerPath, silent);
    }

    // Runs the package's Settings command, so the reader needs no reference to the package or the settings store.
    private static void OpenSettings()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!(Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell shell)) throw new InvalidOperationException("The Visual Studio shell is unavailable.");
        Guid commandSet = VoicePackage.CommandSet;
        object? argument = null;
        ErrorHandler.ThrowOnFailure(shell.PostExecCommand(ref commandSet, (uint)VoicePackage.ShowSettingsToolWindowCommandId, 0, ref argument));
    }

    public override void OnToolWindowCreated()
    {
        base.OnToolWindowCreated();
        ThreadHelper.ThrowIfNotOnUIThread();
        lifecycle?.Attach(Frame);
    }

    protected override void Dispose(bool disposing)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (disposing)
            {
                try { lifecycle?.Dispose(); }
                finally
                {
                    lifecycle = null;
                    (Content as IDisposable)?.Dispose();
                }
            }
        }
        finally { base.Dispose(disposing); }
    }
}
