using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace VsCopilotChatVoice.VisualStudio;

// Floating-first tool window hosting the scoped settings view, so it can be docked or pinned like the reader.
[Guid(WindowGuidString)]
public sealed class SettingsToolWindow : ToolWindowPane
{
    public const string WindowGuidString = "3c8d5f21-6a4b-4d97-b1e0-2f7a9c5d8e14";
    private SettingsView? view;

    // VS reads Content while creating the frame, before OnToolWindowCreated, so the view is built here from the
    // package passed through the async tool window state.
    public SettingsToolWindow(VoicePackage package) : base(null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Caption = "Copilot Voice Settings";
        view = (package ?? throw new ArgumentNullException(nameof(package))).CreateSettingsView();
        Content = view;
    }

    internal void RefreshContext(string? solutionPath) => view?.UpdateSolutionPath(solutionPath);

    protected override void Dispose(bool disposing)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (disposing)
            {
                view?.Dispose();
                view = null;
            }
        }
        finally { base.Dispose(disposing); }
    }
}
