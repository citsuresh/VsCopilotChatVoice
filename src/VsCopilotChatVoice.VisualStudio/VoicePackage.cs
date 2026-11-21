using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using VsCopilotChatVoice.Gestures;

namespace VsCopilotChatVoice.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[Guid("048978d7-5859-4691-8e64-d90e00c0462f")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(ReaderToolWindow), Style = VsDockStyle.Float)]
public sealed class VoicePackage : AsyncPackage
{
    private static readonly Guid CommandSet = new Guid("6f2c4b1a-3d5e-4f7a-9b8c-1e2d3c4b5a69");
    private const int ShowReaderToolWindowCommandId = 0x0100;

    private ChatToolbarIntegration? toolbar;
    private ChatBridgePoc? bridgePoc;
    private ChatGesturePoc? gesturePoc;
    private ChatMetadataBridge? metadataBridge;

    public override IVsAsyncToolWindowFactory? GetAsyncToolWindowFactory(Guid toolWindowType) =>
        toolWindowType == typeof(ReaderToolWindow).GUID ? this : null;

    protected override string GetToolWindowTitle(Type toolWindowType, int id) =>
        toolWindowType == typeof(ReaderToolWindow) ? "Copilot Voice Reader (Preview)" : base.GetToolWindowTitle(toolWindowType, id);

    protected override Task<object?> InitializeToolWindowAsync(Type toolWindowType, int id, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(null);

    protected override async Task InitializeAsync(CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        if (await GetServiceAsync(typeof(IMenuCommandService)) is IMenuCommandService commands)
            commands.AddCommand(new MenuCommand(
                (sender, args) => JoinableTaskFactory.RunAsync(ShowReaderToolWindowAsync).Task.Forget(),
                new CommandID(CommandSet, ShowReaderToolWindowCommandId)));
        toolbar = new ChatToolbarIntegration(ConfigureTheme,
            message => ActivityLog.LogInformation(nameof(VsCopilotChatVoice), message),
            error => ActivityLog.LogError(nameof(VsCopilotChatVoice),
                "Toolbar integration failed: " + error.GetType().FullName + " (" + error.HResult + ")."));
        ActivityLog.LogInformation(nameof(VsCopilotChatVoice),
            "Toolbar preview loaded. Reader bundled; voice backends and delivery bridge are not connected yet.");
        if (ChatGesturePoc.IsEnabled())
        {
            gesturePoc = new ChatGesturePoc(() => toolbar?.ChatPicker,
                message => ActivityLog.LogInformation(nameof(VsCopilotChatVoice), message),
                error => ActivityLog.LogError(nameof(VsCopilotChatVoice),
                    "Gesture PoC failed: " + error.GetType().FullName + " (" + error.HResult + ")."),
                Environment.GetEnvironmentVariable("VSCOPILOTCHATVOICE_GESTURE_TOGGLE") == "1" ? GestureMode.Toggle : GestureMode.Hold,
                Environment.GetEnvironmentVariable("VSCOPILOTCHATVOICE_GESTURE_LEFT") == "1" ? GestureKey.LeftCtrl : GestureKey.RightCtrl);
            ActivityLog.LogInformation(nameof(VsCopilotChatVoice), "Exp-only log-only Ctrl gesture PoC enabled; no microphone or delivery.");
        }
        if (ChatMetadataBridge.IsEnabled())
        {
            metadataBridge = new ChatMetadataBridge(
                message => ActivityLog.LogInformation(nameof(VsCopilotChatVoice), message),
                error => ActivityLog.LogError(nameof(VsCopilotChatVoice),
                    "Metadata bridge failed: " + error.GetType().FullName + " (" + error.HResult + ")."));
            ActivityLog.LogInformation(nameof(VsCopilotChatVoice), "Exp-only metadata bridge enabled; no contents, edit, send or reply feed.");
        }
        if (ChatBridgePoc.IsEnabled())
        {
            bridgePoc = new ChatBridgePoc(error => ActivityLog.LogError(nameof(VsCopilotChatVoice),
                "Diagnostic bridge failed: " + error.GetType().FullName + " (" + error.HResult + ")."));
            ActivityLog.LogInformation(nameof(VsCopilotChatVoice), ChatBridgePoc.IsDraftEnabled()
                ? "Exp-only atomic-insertion capability probe enabled; native edits and send remain unsupported."
                : "Exp-only read-only chat diagnostic bridge enabled.");
        }
    }

    private async Task ShowReaderToolWindowAsync()
    {
        try { await ShowToolWindowAsync(typeof(ReaderToolWindow), 0, true, DisposalToken); }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            ActivityLog.LogError(nameof(VsCopilotChatVoice),
                "Reader tool window failed: " + ex.GetType().FullName + " (" + ex.HResult + ").");
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
        ThreadHelper.ThrowIfNotOnUIThread();
        if (disposing)
        {
            metadataBridge?.Dispose();
            gesturePoc?.Dispose();
            bridgePoc?.Dispose();
            toolbar?.Dispose();
        }
        base.Dispose(disposing);
    }
}