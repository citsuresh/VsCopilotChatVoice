using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace VsCopilotChatVoice.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[Guid("048978d7-5859-4691-8e64-d90e00c0462f")]
public sealed class VoicePackage : AsyncPackage
{
    private ChatToolbarIntegration? toolbar;
    private ChatBridgePoc? bridgePoc;

    protected override async Task InitializeAsync(CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        toolbar = new ChatToolbarIntegration(ConfigureTheme,
            message => ActivityLog.LogInformation(nameof(VsCopilotChatVoice), message),
            error => ActivityLog.LogError(nameof(VsCopilotChatVoice),
                "Toolbar integration failed: " + error.GetType().FullName + " (" + error.HResult + ")."));
        ActivityLog.LogInformation(nameof(VsCopilotChatVoice),
            "Toolbar preview loaded. Reader bundled; voice backends and delivery bridge are not connected yet.");
        if (ChatBridgePoc.IsEnabled())
        {
            bridgePoc = new ChatBridgePoc(error => ActivityLog.LogError(nameof(VsCopilotChatVoice),
                "Diagnostic bridge failed: " + error.GetType().FullName + " (" + error.HResult + ")."));
            ActivityLog.LogInformation(nameof(VsCopilotChatVoice), "Exp-only read-only chat diagnostic bridge enabled.");
        }
    }

    private static void ConfigureTheme(FrameworkElement element)
    {
        if (element is Border group)
        {
            group.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            return;
        }
        if (!(element is ButtonBase button))
            throw new ArgumentException("Unsupported voice toolbar element.", nameof(element));
        button.SetResourceReference(FrameworkElement.StyleProperty, button is ToggleButton
            ? ToolBar.ToggleButtonStyleKey : ToolBar.ButtonStyleKey);
        button.SetResourceReference(Control.ForegroundProperty, SystemColors.GrayTextBrushKey);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            bridgePoc?.Dispose();
            toolbar?.Dispose();
        }
        base.Dispose(disposing);
    }
}