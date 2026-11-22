using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using VsCopilotChatVoice.Gestures;
using VsCopilotChatVoice.Settings;

namespace VsCopilotChatVoice.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
[Guid("048978d7-5859-4691-8e64-d90e00c0462f")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(ReaderToolWindow), Style = VsDockStyle.Float)]
[ProvideToolWindow(typeof(SettingsToolWindow), Style = VsDockStyle.Float, Width = 640, Height = 600)]
public sealed class VoicePackage : AsyncPackage
{
    internal static readonly Guid CommandSet = new Guid("6f2c4b1a-3d5e-4f7a-9b8c-1e2d3c4b5a69");
    private const int ShowReaderToolWindowCommandId = 0x0100;
    internal const int ShowSettingsToolWindowCommandId = 0x0101;

    private ChatToolbarIntegration? toolbar;
    private ChatBridgePoc? bridgePoc;
    private ChatGesturePoc? gesturePoc;
    private ChatMetadataBridge? metadataBridge;
    private ScopedSettingsStore? settingsStore;
    private VoiceOverrides windowSettings = new VoiceOverrides();

    public override IVsAsyncToolWindowFactory? GetAsyncToolWindowFactory(Guid toolWindowType) =>
        toolWindowType == typeof(ReaderToolWindow).GUID || toolWindowType == typeof(SettingsToolWindow).GUID ? this : null;

    protected override string GetToolWindowTitle(Type toolWindowType, int id) =>
        toolWindowType == typeof(ReaderToolWindow) ? "Copilot Voice Reader (Preview)"
        : toolWindowType == typeof(SettingsToolWindow) ? "Copilot Voice Settings"
        : base.GetToolWindowTitle(toolWindowType, id);

    protected override Task<object?> InitializeToolWindowAsync(Type toolWindowType, int id, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(toolWindowType == typeof(SettingsToolWindow) ? this : null);

    protected override async Task InitializeAsync(CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        if (await GetServiceAsync(typeof(IMenuCommandService)) is IMenuCommandService commands)
        {
            commands.AddCommand(new MenuCommand(
                (sender, args) => JoinableTaskFactory.RunAsync(ShowReaderToolWindowAsync).Task.Forget(),
                new CommandID(CommandSet, ShowReaderToolWindowCommandId)));
            commands.AddCommand(new MenuCommand(
                (sender, args) => ShowSettings(),
                new CommandID(CommandSet, ShowSettingsToolWindowCommandId)));
        }
        EnsureSettingsStore();
        UpdateRuntimeSettings();
        toolbar = new ChatToolbarIntegration(ConfigureTheme,
            message => ActivityLog.LogInformation(nameof(VsCopilotChatVoice), message),
            error => ActivityLog.LogError(nameof(VsCopilotChatVoice),
                "Toolbar integration failed: " + error.GetType().FullName + " (" + error.HResult + ")."),
            ShowSettings);
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

    private void ShowSettings() => JoinableTaskFactory.RunAsync(ShowSettingsToolWindowAsync).Task.Forget();

    private async Task ShowSettingsToolWindowAsync()
    {
        try
        {
            ToolWindowPane pane = await ShowToolWindowAsync(typeof(SettingsToolWindow), 0, true, DisposalToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            (pane as SettingsToolWindow)?.RefreshContext(GetSolutionPath());
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            ActivityLog.LogError(nameof(VsCopilotChatVoice),
                "Settings tool window failed: " + ex.GetType().FullName + " (" + ex.HResult + ").");
        }
    }

    // Called by SettingsToolWindow once the pane has its package; the view reads the window overrides live.
    // VS can restore a persisted Settings window before InitializeAsync finishes, so the store is created on demand.
    private ScopedSettingsStore EnsureSettingsStore() =>
        settingsStore ??= new ScopedSettingsStore(ScopedSettingsStore.DefaultRoot, ScopedSettingsStore.DefaultLegacyPath);

    internal SettingsView CreateSettingsView()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ScopedSettingsStore store = EnsureSettingsStore();
        string packageFolder = Path.GetDirectoryName(typeof(VoicePackage).Assembly.Location) ?? "";
        string workerPath = Path.Combine(packageFolder, "Reader", "VsCopilotChatVoice.Reader.exe");
        return new SettingsView(store, GetSolutionPath(), workerPath, () => windowSettings,
            values => { windowSettings = values; UpdateRuntimeSettings(); }, UpdateRuntimeSettings,
            error => ActivityLog.LogError(nameof(VsCopilotChatVoice),
                "Settings operation failed: " + error.GetType().FullName + " (" + error.HResult + ")."));
    }

    private string? GetSolutionPath()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (GetService(typeof(SVsSolution)) is not IVsSolution solution) return null;
        ErrorHandler.ThrowOnFailure(solution.GetSolutionInfo(out _, out string file, out _));
        return string.IsNullOrWhiteSpace(file) ? null : file;
    }

    private void UpdateRuntimeSettings()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (settingsStore == null) return;
        string? solution = GetSolutionPath();
        EffectiveVoiceSettings effective = settingsStore.Load(solution, windowSettings);
        VoiceRuntimeSettings.Set(effective, new AudioPolicySettings((OtherAudioPolicy)effective.AudioPolicy.Value, effective.DuckPercent.Value));
        VoiceRuntimeSettings.SetAppearance(new VoiceAppearanceSettings(effective.Theme.Value, effective.FontSize.Value));
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
        // Enabled controls (Settings) use normal text; only the unconnected Mic/Speaker previews look disabled.
        button.SetResourceReference(Control.ForegroundProperty, button.IsEnabled
            ? EnvironmentColors.ToolWindowTextBrushKey : SystemColors.GrayTextBrushKey);
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