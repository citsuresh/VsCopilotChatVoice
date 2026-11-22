using System;
using VsCopilotChatVoice.Settings;
using VsCopilotChatVoice.Speech;

namespace VsCopilotChatVoice.VisualStudio;

internal sealed class VoiceAppearanceSettings
{
    internal int Theme { get; }
    internal double FontSize { get; }
    internal VoiceAppearanceSettings(int theme, double fontSize)
    {
        if (theme < 0 || theme > 3) throw new ArgumentOutOfRangeException(nameof(theme));
        if (fontSize < 10 || fontSize > 28) throw new ArgumentOutOfRangeException(nameof(fontSize));
        Theme = theme; FontSize = fontSize;
    }
}

internal static class VoiceRuntimeSettings
{
    private static readonly object Sync = new object();
    private static SpeechOptionsSnapshot speech = new SpeechOptionsSnapshot("", 0, 100);
    private static AudioPolicySettings audio = new AudioPolicySettings(OtherAudioPolicy.Off, 25);
    private static VoiceAppearanceSettings appearance = new VoiceAppearanceSettings(0, 13);
    private static bool? conciseOverride;
    internal static event EventHandler? Changed;

    internal static SpeechOptionsSnapshot Speech
    {
        get { lock (Sync) return speech; }
    }

    internal static AudioPolicySettings Audio
    {
        get { lock (Sync) return audio; }
    }

    internal static VoiceAppearanceSettings Appearance
    {
        get { lock (Sync) return appearance; }
    }

    internal static bool? ConciseOverride
    {
        get { lock (Sync) return conciseOverride; }
    }

    internal static void SetAppearance(VoiceAppearanceSettings value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        lock (Sync) appearance = value;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    internal static void SetAudio(AudioPolicySettings value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        lock (Sync) audio = value;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    internal static void Set(EffectiveVoiceSettings effective, AudioPolicySettings audioSettings)
    {
        if (effective == null) throw new ArgumentNullException(nameof(effective));
        if (audioSettings == null) throw new ArgumentNullException(nameof(audioSettings));
        lock (Sync)
        {
            speech = new SpeechOptionsSnapshot(effective.VoiceName.Value, effective.Rate.Value, effective.Volume.Value);
            conciseOverride = effective.Concise.Origin == SettingOrigin.BuiltIn ? (bool?)null : effective.Concise.Value;
            audio = audioSettings;
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
