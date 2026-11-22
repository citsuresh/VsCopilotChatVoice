using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using VsCopilotChatVoice.Settings;
using VsCopilotChatVoice.Speech;

namespace VsCopilotChatVoice.VisualStudio;

internal enum SettingsEditScope { Global, Solution, Window }

// Hosted by SettingsToolWindow. The view lives as long as the tool window, so the solution path and the temporary
// window overrides are read live rather than captured at construction.
internal sealed class SettingsView : UserControl, IDisposable
{
    private readonly ScopedSettingsStore store;
    private string? solutionPath;
    private readonly string workerPath;
    private readonly Func<VoiceOverrides> windowOverridesProvider;
    private readonly Action<VoiceOverrides> setWindowOverrides;
    private readonly Action settingsChanged;
    private readonly Action<Exception> reportError;
    private readonly ComboBox scope = new ComboBox();
    private readonly ComboBox voice = new ComboBox();
    private readonly Slider rate = new Slider { Minimum = -10, Maximum = 10, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Slider volume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TextBlock rateValue = NumericValue("Rate value");
    private readonly TextBlock volumeValue = NumericValue("Volume value");
    private readonly CheckBox speaker = new CheckBox { Content = "Enable automatic read-aloud" };
    private readonly CheckBox concise = new CheckBox { Content = "Prefer Concise when supplied" };
    private readonly ComboBox audioPolicy = new ComboBox();
    private readonly Slider duckPercent = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, Value = 25 };
    private readonly TextBlock duckPercentValue = NumericValue("Duck percentage value");
    private readonly ComboBox theme = new ComboBox();
    private readonly Slider fontSize = new Slider { Minimum = 10, Maximum = 28, TickFrequency = 1, IsSnapToTickEnabled = true, Value = 13 };
    private readonly TextBlock fontSizeValue = NumericValue("Reader font size value");
    private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button preview = new Button { Content = "Preview", IsEnabled = false };
    private readonly Button stop = new Button { Content = "Stop", IsEnabled = false };
    private readonly Button reset = new Button { Content = "Reset" };
    // Sliders save once the value settles, so dragging does not write on every tick.
    private readonly System.Windows.Threading.DispatcherTimer saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly List<SpeechVoiceCapability> capabilities = new List<SpeechVoiceCapability>();
    private SpeechWorkerHost? previewHost;
    private AudioPolicyScope? previewAudio;
    private CancellationTokenSource? previewRun;
    private SpeechCapabilityReport? capabilityReport;
    private bool loading;
    private bool capabilitiesRequested;
    private string? unavailableVoiceName;
    private VoiceOverrides loadedOverrides = new VoiceOverrides();
    private EffectiveVoiceSettings inheritedValues = new EffectiveVoiceSettings(new VoiceOverrides(), new VoiceOverrides(), new VoiceOverrides());

    private VoiceOverrides windowOverrides => windowOverridesProvider();

    internal SettingsView(ScopedSettingsStore store, string? solutionPath, string workerPath,
        Func<VoiceOverrides> windowOverridesProvider, Action<VoiceOverrides> setWindowOverrides, Action settingsChanged, Action<Exception> reportError)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.solutionPath = solutionPath;
        this.workerPath = workerPath ?? throw new ArgumentNullException(nameof(workerPath));
        this.windowOverridesProvider = windowOverridesProvider ?? throw new ArgumentNullException(nameof(windowOverridesProvider));
        this.setWindowOverrides = setWindowOverrides ?? throw new ArgumentNullException(nameof(setWindowOverrides));
        this.settingsChanged = settingsChanged ?? throw new ArgumentNullException(nameof(settingsChanged));
        this.reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        MinWidth = 320;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        AutomationProperties.SetName(this, "Copilot Voice Settings");
        BuildLayout();
        scope.SelectionChanged += (sender, args) => { if (!loading) LoadScope(); };
        preview.Click += (sender, args) => Observe(PreviewAsync());
        stop.Click += (sender, args) => Observe(StopPreviewAsync());
        reset.Click += (sender, args) => { saveTimer.Stop(); Observe(ResetAsync()); };
        audioPolicy.SelectionChanged += (sender, args) => UpdateAudioVisibility();
        saveTimer.Tick += (sender, args) => { saveTimer.Stop(); SaveNow(); };
        foreach (CheckBox box in new[] { speaker, concise })
        {
            box.Checked += (sender, args) => SaveNow();
            box.Unchecked += (sender, args) => SaveNow();
            box.Indeterminate += (sender, args) => SaveNow();
        }
        voice.SelectionChanged += (sender, args) => SaveNow();
        audioPolicy.SelectionChanged += (sender, args) => SaveNow();
        theme.SelectionChanged += (sender, args) => SaveNow();
        foreach (Slider slider in new[] { rate, volume, duckPercent, fontSize })
            slider.ValueChanged += (sender, args) => { if (loading) return; saveTimer.Stop(); saveTimer.Start(); };
        Loaded += (sender, args) =>
        {
            // Loaded fires again whenever the pane is re-docked; discover voices only once.
            if (capabilitiesRequested) return;
            capabilitiesRequested = true;
            Observe(LoadCapabilitiesAsync());
        };
    }

    public void Dispose()
    {
        FlushPendingSave();
        previewRun?.Cancel(); previewAudio?.Dispose(); previewAudio = null; previewHost?.Dispose(); previewHost = null;
    }

    // Called each time the tool window is shown, because the solution can change while the pane stays open.
    internal void UpdateSolutionPath(string? path)
    {
        if (string.Equals(solutionPath, path, StringComparison.OrdinalIgnoreCase)) return;
        FlushPendingSave();
        solutionPath = path;
        var solutionItem = (ComboBoxItem)scope.Items[1];
        solutionItem.Content = path == null ? "This solution (unavailable)" : "This solution";
        solutionItem.IsEnabled = path != null;
        if (path == null && SelectedScope == SettingsEditScope.Solution) scope.SelectedIndex = 0;
        else LoadScope();
    }

    private void BuildLayout()
    {
        ApplyVisualStudioTheme();
        SetResourceReference(FontFamilyProperty, Microsoft.VisualStudio.Shell.VsFonts.EnvironmentFontFamilyKey);
        SetResourceReference(FontSizeProperty, Microsoft.VisualStudio.Shell.VsFonts.EnvironmentFontSizeKey);
        var root = new DockPanel { LastChildFill = true };
        root.SetResourceReference(Panel.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

        // Footer: status on the left, Reset on the right. Changes are saved as they are made, like VS Options.
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(reset);
        foreach (Button button in buttons.Children)
        {
            button.MinWidth = 80; button.Padding = new Thickness(12, 3, 12, 3); button.Margin = new Thickness(8, 0, 0, 0);
            AutomationProperties.SetName(button, button.Content.ToString());
        }
        status.Margin = new Thickness(0, 0, 12, 0); status.VerticalAlignment = VerticalAlignment.Center;
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.Children.Add(status); Grid.SetColumn(buttons, 1); footerGrid.Children.Add(buttons);
        var footer = new Border { Padding = new Thickness(16, 10, 16, 10), BorderThickness = new Thickness(0, 1, 0, 0), Child = footerGrid };
        footer.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
        footer.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        scroll.Content = panel;

        scope.Items.Add(new ComboBoxItem { Content = "Global defaults", Tag = SettingsEditScope.Global });
        scope.Items.Add(new ComboBoxItem { Content = solutionPath == null ? "This solution (unavailable)" : "This solution", Tag = SettingsEditScope.Solution, IsEnabled = solutionPath != null });
        scope.Items.Add(new ComboBoxItem { Content = "This VS window (temporary)", Tag = SettingsEditScope.Window });
        scope.SelectedIndex = solutionPath == null ? 0 : 1;
        Grid section = Section(panel, "Settings scope", true);
        AddRow(section, "Changes apply to", scope);

        section = Section(panel, "Speech", false);
        var voiceButtons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (Button button in new[] { preview, stop })
        {
            button.MinWidth = 70; button.Padding = new Thickness(10, 2, 10, 2); button.Margin = new Thickness(8, 0, 0, 0);
            AutomationProperties.SetName(button, button.Content.ToString());
            voiceButtons.Children.Add(button);
        }
        var voiceRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(voiceButtons, Dock.Right); voiceRow.Children.Add(voiceButtons); voiceRow.Children.Add(voice);
        AddRow(section, "Voice", voiceRow, voice);
        AddRow(section, "Rate", SliderRow(rate, rateValue, value => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)), rate);
        AddRow(section, "Volume", SliderRow(volume, volumeValue, value => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%"), volume);
        AddSpanning(section, speaker); AddSpanning(section, concise);

        section = Section(panel, "Other application audio", false);
        audioPolicy.Items.Add("Off"); audioPolicy.Items.Add("Duck to percentage"); audioPolicy.Items.Add("Suppress while speaking"); audioPolicy.SelectedIndex = 0;
        AddRow(section, "While speaking", audioPolicy);
        AddRow(section, "Duck percentage", SliderRow(duckPercent, duckPercentValue, value => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%"), duckPercent);

        section = Section(panel, "Reader appearance", false);
        theme.Items.Add("Follow Visual Studio"); theme.Items.Add("Light"); theme.Items.Add("Dark"); theme.Items.Add("High Contrast (system)"); theme.SelectedIndex = 0;
        AddRow(section, "Theme", theme);
        AddRow(section, "Font size", SliderRow(fontSize, fontSizeValue, value => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " pt"), fontSize);

        var inherited = new TextBlock { Text = "Changes are saved as you make them. Inherited values come from the lower-priority scope. Reset clears overrides for Solution or Window; Global restores built-in defaults.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) };
        inherited.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.CommandBarTextInactiveBrushKey);
        panel.Children.Add(inherited);
        root.Children.Add(scroll); Content = root;
        LoadScope(); UpdateAudioVisibility();
    }

    private static Grid Section(Panel host, string title, bool first)
    {
        var header = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, first ? 0 : 20, 0, 4) };
        header.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        var rule = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 8) };
        rule.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBorderBrushKey);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        host.Children.Add(header); host.Children.Add(rule); host.Children.Add(grid);
        return grid;
    }

    private static void AddRow(Grid grid, string label, FrameworkElement control, FrameworkElement? named = null)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        control.Margin = new Thickness(0, 4, 0, 4); control.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(named ?? control, label);
        Grid.SetRow(text, row); Grid.SetRow(control, row); Grid.SetColumn(control, 1);
        grid.Children.Add(text); grid.Children.Add(control);
    }

    private static void AddSpanning(Grid grid, FrameworkElement control)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        control.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(control, row); Grid.SetColumnSpan(control, 2);
        grid.Children.Add(control);
    }

    private void ApplyVisualStudioTheme()
    {
        // Implicit styles for every control type used here; they follow the active VS theme, including disabled state.
        // VS's public control styles look like the classic tool-window controls, not the modern Options page, so the
        // self-contained EnvironmentColors styles (accent from SystemHighlight) are used for every control type here.
        Resources[typeof(Button)] = VsThemedStyles.Button();
        Resources[typeof(ComboBox)] = VsThemedStyles.ComboBox();
        Resources[typeof(ComboBoxItem)] = VsThemedStyles.ComboBoxItem();
        Resources[typeof(CheckBox)] = VsThemedStyles.CheckBox();
        Resources[typeof(Slider)] = VsThemedStyles.Slider();
    }

    private static TextBlock NumericValue(string name)
    {
        var value = new TextBlock
        {
            Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0), FontWeight = FontWeights.SemiBold
        };
        AutomationProperties.SetName(value, name);
        return value;
    }

    private static DockPanel SliderRow(Slider slider, TextBlock value, Func<double, string> format)
    {
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(value, Dock.Right); row.Children.Add(value); row.Children.Add(slider);
        slider.ValueChanged += (sender, args) => value.Text = format(args.NewValue);
        value.Text = format(slider.Value);
        return row;
    }

    private SettingsEditScope SelectedScope => (SettingsEditScope)((ComboBoxItem)scope.SelectedItem).Tag;
    private VoiceOverrides CurrentOverrides()
    {
        string? selectedVoice = voice.SelectedItem is SpeechVoiceCapability item ? item.Name : voice.SelectedItem as string;
        if (unavailableVoiceName != null && string.Equals(selectedVoice, unavailableVoiceName + " (unavailable)", StringComparison.Ordinal)) selectedVoice = unavailableVoiceName;
        if (string.IsNullOrWhiteSpace(selectedVoice) || selectedVoice == "Use inherited value") selectedVoice = null;
        return new VoiceOverrides(speaker.IsThreeState && speaker.IsChecked == null ? null : speaker.IsChecked == true,
            concise.IsThreeState && concise.IsChecked == null ? null : concise.IsChecked == true,
            selectedVoice,
            Preserve(loadedOverrides.Rate, (int)rate.Value, inheritedValues.Rate.Value),
            Preserve(loadedOverrides.Volume, (int)volume.Value, inheritedValues.Volume.Value),
            Preserve(loadedOverrides.AudioPolicy, audioPolicy.SelectedIndex, inheritedValues.AudioPolicy.Value),
            Preserve(loadedOverrides.DuckPercent, (int)duckPercent.Value, inheritedValues.DuckPercent.Value),
            Preserve(loadedOverrides.Theme, theme.SelectedIndex, inheritedValues.Theme.Value),
            Preserve(loadedOverrides.FontSize, (int)fontSize.Value, inheritedValues.FontSize.Value));
    }

    private static int? Preserve(int? originalOverride, int current, int inherited) =>
        originalOverride.HasValue || current != inherited ? current : (int?)null;

    private void LoadScope()
    {
        if (scope.SelectedItem == null) return;
        loading = true;
        try
        {
            VoiceOverrides selected;
            if (SelectedScope == SettingsEditScope.Window)
            {
                selected = windowOverrides;
                VoiceOverrides global = store.ReadGlobal().Values;
                VoiceOverrides solution = solutionPath == null ? new VoiceOverrides() : store.ReadSolution(solutionPath).Values;
                inheritedValues = new EffectiveVoiceSettings(global, solution, new VoiceOverrides());
            }
            else if (SelectedScope == SettingsEditScope.Global)
            {
                selected = store.ReadGlobal().Values;
                inheritedValues = new EffectiveVoiceSettings(new VoiceOverrides(), new VoiceOverrides(), new VoiceOverrides());
            }
            else
            {
                selected = solutionPath == null ? new VoiceOverrides() : store.ReadSolution(solutionPath).Values;
                VoiceOverrides global = store.ReadGlobal().Values;
                inheritedValues = new EffectiveVoiceSettings(global, new VoiceOverrides(), new VoiceOverrides());
            }
            loadedOverrides = selected;
            ApplyOverrideControls(selected);
            EffectiveVoiceSettings effective = store.Load(solutionPath, windowOverrides);
            status.Text = "Effective values: voice " + Origin(effective.VoiceName.Origin) + ", rate " + Origin(effective.Rate.Origin) + ", volume " + Origin(effective.Volume.Origin) + ".";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is TimeoutException)
        { status.Text = "Settings could not be loaded: " + ex.Message; reportError(ex); }
        finally { loading = false; }
    }

    private void ApplyOverrideControls(VoiceOverrides selected)
    {
        speaker.IsThreeState = true; speaker.IsChecked = selected.SpeakerEnabled;
        concise.IsThreeState = true; concise.IsChecked = selected.Concise;
        rate.Value = selected.Rate ?? 0; volume.Value = selected.Volume ?? 100;
        audioPolicy.SelectedIndex = selected.AudioPolicy ?? 0; duckPercent.Value = selected.DuckPercent ?? 25;
        theme.SelectedIndex = selected.Theme ?? 0; fontSize.Value = selected.FontSize ?? 13;
        unavailableVoiceName = null;
        voice.Items.Clear(); voice.Items.Add("Use inherited value");
        foreach (SpeechVoiceCapability item in capabilities.Where(v => v.Enabled).OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)) voice.Items.Add(item);
        voice.SelectedIndex = selected.VoiceName == null ? 0 : Math.Max(0, voice.Items.Cast<object>().ToList().FindIndex(v => v is SpeechVoiceCapability capability && capability.Name == selected.VoiceName));
        if (selected.VoiceName != null && voice.SelectedIndex == 0) { unavailableVoiceName = selected.VoiceName; voice.Items.Insert(1, selected.VoiceName + " (unavailable)"); voice.SelectedIndex = 1; }
    }

    private static string Origin(SettingOrigin origin) => origin == SettingOrigin.BuiltIn ? "built-in" : origin.ToString().ToLowerInvariant() + " override";

    private async Task LoadCapabilitiesAsync()
    {
        try
        {
            capabilityReport = await SpeechCapabilityQuery.QueryAsync(workerPath, TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            capabilities.Clear(); capabilities.AddRange(capabilityReport.Voices);
            LoadScope(); preview.IsEnabled = capabilityReport.Voices.Any(v => v.Enabled);
            status.Text = "Loaded " + capabilityReport.Voices.Count(v => v.Enabled) + " available System.Speech voices.";
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is InvalidOperationException || ex is TimeoutException || ex is UnauthorizedAccessException || ex is Win32Exception || ex is AggregateException)
        { status.Text = "Voice discovery failed: " + ex.Message + ". Preview is unavailable; no voice was substituted."; reportError(ex); }
    }

    private void FlushPendingSave()
    {
        if (!saveTimer.IsEnabled) return;
        saveTimer.Stop(); SaveNow();
    }

    private void SaveNow()
    {
        if (loading) return;
        Observe(SaveAsync());
    }

    private async Task SaveAsync()
    {
        VoiceOverrides values = CurrentOverrides();
        try
        {
            VoiceSettingsPatch patch = new VoiceSettingsPatch(values, new[] { VoiceSetting.SpeakerEnabled, VoiceSetting.Concise, VoiceSetting.VoiceName, VoiceSetting.Rate, VoiceSetting.Volume, VoiceSetting.AudioPolicy, VoiceSetting.DuckPercent, VoiceSetting.Theme, VoiceSetting.FontSize });
            switch (SelectedScope)
            {
                case SettingsEditScope.Global: store.UpdateGlobal(patch); break;
                case SettingsEditScope.Solution:
                    if (solutionPath == null) throw new InvalidOperationException("No solution is open; solution settings cannot be saved.");
                    store.UpdateSolution(solutionPath, patch); break;
                case SettingsEditScope.Window: setWindowOverrides(values); break;
            }
            loadedOverrides = values;
            ApplyRuntimePresentationSettings();
            settingsChanged();
            status.Text = "Saved to " + scope.Text + ".";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is TimeoutException || ex is InvalidOperationException)
        { status.Text = "Settings were not saved: " + ex.Message; reportError(ex); }
        await Task.CompletedTask;
    }

    private async Task ResetAsync()
    {
        loading = true;
        try
        {
            if (SelectedScope == SettingsEditScope.Global)
            {
                store.UpdateGlobal(new VoiceSettingsPatch(new VoiceOverrides(), new[] { VoiceSetting.SpeakerEnabled, VoiceSetting.Concise, VoiceSetting.VoiceName, VoiceSetting.Rate, VoiceSetting.Volume, VoiceSetting.AudioPolicy, VoiceSetting.DuckPercent, VoiceSetting.Theme, VoiceSetting.FontSize }));
            }
            else if (SelectedScope == SettingsEditScope.Solution)
            {
                if (solutionPath == null) throw new InvalidOperationException("No solution is open; solution settings cannot be cleared.");
                store.UpdateSolution(solutionPath, new VoiceSettingsPatch(new VoiceOverrides(), new[] { VoiceSetting.SpeakerEnabled, VoiceSetting.Concise, VoiceSetting.VoiceName, VoiceSetting.Rate, VoiceSetting.Volume, VoiceSetting.AudioPolicy, VoiceSetting.DuckPercent, VoiceSetting.Theme, VoiceSetting.FontSize }));
            }
            else setWindowOverrides(new VoiceOverrides());
            VoiceRuntimeSettings.SetAppearance(new VoiceAppearanceSettings(0, 13));
            ApplyRuntimeAudioSettings(OtherAudioPolicy.Off, 25);
            settingsChanged();
            LoadScope(); status.Text = SelectedScope == SettingsEditScope.Global ? "Global defaults restored." : "Override cleared; inherited values are active.";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is TimeoutException || ex is InvalidOperationException)
        { status.Text = "Reset failed: " + ex.Message; reportError(ex); }
        finally { loading = false; }
        await Task.CompletedTask;
    }

    private async Task PreviewAsync()
    {
        if (capabilityReport == null) return;
        SpeechWorkerHost? host = null;
        AudioPolicyScope? audio = null;
        CancellationTokenSource? run = null;
        try
        {
            string? selected = ResolvePreviewVoice();
            if (selected == null || !capabilityReport.Voices.Any(v => v.Enabled && v.Name == selected)) throw new InvalidOperationException("No available speech voice is configured.");
            status.Text = "Starting Preview with " + selected + "...";
            previewHost?.Dispose();
            previewAudio?.Dispose();
            preview.IsEnabled = false;
            host = await SpeechWorkerHost.LaunchAsync(workerPath, Environment.GetEnvironmentVariable("VSCOPILOTCHATVOICE_SPEECH_SILENT") == "1", TimeSpan.FromSeconds(15)).ConfigureAwait(true);
            previewHost = host;
            audio = AudioPolicyScope.Begin(new AudioPolicySettings(
                audioPolicy.SelectedIndex == 1 ? OtherAudioPolicy.Duck : audioPolicy.SelectedIndex == 2 ? OtherAudioPolicy.Suppress : OtherAudioPolicy.Off,
                (int)duckPercent.Value), host.WorkerProcessId);
            previewAudio = audio;
            run = new CancellationTokenSource(); previewRun = run;
            stop.IsEnabled = true;
            status.Text = "Speaking Preview with " + selected + "...";
            await SpeechPreview.SpeakAsync(host, "This is a preview of the selected Copilot voice settings.", new SpeechOptionsSnapshot(selected, (int)rate.Value, (int)volume.Value), TimeSpan.FromSeconds(60), run.Token).ConfigureAwait(true);
            status.Text = "Preview completed.";
        }
        catch (Exception) when (run != null && run.IsCancellationRequested)
        { /* Stop or closing ended the preview deliberately; Stop already reported its status. */ }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is TimeoutException || ex is UnauthorizedAccessException || ex is OperationCanceledException || ex is Win32Exception || ex is AggregateException)
        { status.Text = "Preview failed: " + ex.Message; reportError(ex); }
        finally
        {
            stop.IsEnabled = false; preview.IsEnabled = capabilityReport != null && capabilityReport.Voices.Any(v => v.Enabled);
            host?.Dispose(); if (ReferenceEquals(previewHost, host)) previewHost = null;
            try { audio?.Dispose(); } finally { if (ReferenceEquals(previewAudio, audio)) previewAudio = null; }
            if (ReferenceEquals(previewRun, run)) previewRun = null;
            run?.Dispose();
        }
    }

    private string? ResolvePreviewVoice()
    {
        if (voice.SelectedItem is SpeechVoiceCapability selected) return selected.Name;
        EffectiveVoiceSettings effective = store.Load(solutionPath, windowOverrides);
        if (!string.IsNullOrWhiteSpace(effective.VoiceName.Value)) return effective.VoiceName.Value;
        return capabilityReport?.DefaultVoice?.Enabled == true ? capabilityReport.DefaultVoice.Name : null;
    }

    private async Task StopPreviewAsync()
    {
        previewRun?.Cancel();
        if (previewHost != null) { previewHost.Cancel(); previewHost.Dispose(); previewHost = null; }
        if (previewAudio != null) { previewAudio.Dispose(); previewAudio = null; }
        stop.IsEnabled = false; preview.IsEnabled = capabilityReport != null && capabilityReport.Voices.Any(v => v.Enabled); status.Text = "Preview stopped."; await Task.CompletedTask;
    }

    private void UpdateAudioVisibility() => duckPercent.IsEnabled = audioPolicy.SelectedIndex == 1;

    private void ApplyRuntimePresentationSettings()
    {
        VoiceRuntimeSettings.SetAppearance(new VoiceAppearanceSettings(theme.SelectedIndex, fontSize.Value));
        ApplyRuntimeAudioSettings(audioPolicy.SelectedIndex == 1 ? OtherAudioPolicy.Duck : audioPolicy.SelectedIndex == 2 ? OtherAudioPolicy.Suppress : OtherAudioPolicy.Off, (int)duckPercent.Value);
    }

    private static void ApplyRuntimeAudioSettings(OtherAudioPolicy policy, int percent) =>
        VoiceRuntimeSettings.SetAudio(new AudioPolicySettings(policy, percent));

    private void Observe(Task task) => _ = task.ContinueWith(t => { if (t.IsFaulted && t.Exception != null) reportError(t.Exception.InnerException ?? t.Exception); }, TaskScheduler.FromCurrentSynchronizationContext());
}

