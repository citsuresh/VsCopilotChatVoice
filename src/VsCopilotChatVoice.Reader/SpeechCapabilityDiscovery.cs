#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using VsCopilotChatVoice.Speech;

namespace ReadAlongTrial;

internal static class SpeechCapabilityDiscovery
{
    internal static SpeechCapabilityReport Collect()
    {
        // This instance has no output/voice selection, synthesis, preferences or UI lifetime.
        using var speech = new SpeechSynthesizer();
        var installed = speech.GetInstalledVoices();
        if (installed.Count > SpeechCapabilities.MaxVoices) throw new InvalidDataException("Installed speech voices exceed the report capacity.");
        VoiceInfo current = speech.Voice;
        var voices = installed.Select(v => new SpeechVoiceCapability
        { Name = v.VoiceInfo.Name, Culture = v.VoiceInfo.Culture.Name, Enabled = v.Enabled }).ToArray();
        return new SpeechCapabilityReport
        {
            Voices = voices,
            DefaultVoice = new SpeechVoiceCapability { Name = current.Name, Culture = current.Culture.Name, Enabled = true }
        };
    }

    internal static int Run()
    {
        try
        {
            byte[] report = SpeechCapabilities.Serialize(Collect());
            using Stream output = Console.OpenStandardOutput();
            output.Write(report, 0, report.Length);
            output.Flush();
            return 0;
        }
        catch (Exception ex)
        {
            // stdout is reserved for one successful report; failures are never empty success.
            string message = ex.GetType().Name + ": " + ex.Message;
            if (message.Length > 1000) message = message.Substring(0, 1000);
            Console.Error.WriteLine(message);
            return 1;
        }
    }
}
