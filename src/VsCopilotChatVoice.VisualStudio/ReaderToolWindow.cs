using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace VsCopilotChatVoice.VisualStudio;

[Guid(WindowGuidString)]
public sealed class ReaderToolWindow : ToolWindowPane
{
    public const string WindowGuidString = "9a1e7c3d-5b2f-4e68-8d4a-7c6b5a493827";

    public ReaderToolWindow() : base(null)
    {
        Caption = "Copilot Voice Reader (Preview)";
        string packageFolder = Path.GetDirectoryName(typeof(ReaderToolWindow).Assembly.Location) ?? "";
        Content = new ReaderToolWindowControl(
            Path.Combine(packageFolder, "Reader", "VsCopilotChatVoice.Reader.exe"),
            Environment.GetEnvironmentVariable(ReaderToolWindowControl.SilentVariable) == "1");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) (Content as IDisposable)?.Dispose();
        base.Dispose(disposing);
    }
}
