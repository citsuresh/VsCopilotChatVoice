using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

// FileAndForget observes the existing asynchronous owner tasks; no caller synchronously joins them.
#pragma warning disable VSSDK007, VSTHRD003

namespace VsCopilotChatVoice.VisualStudio;

internal static class NativeReaderPreview
{
    internal const string GateVariable = "VSCOPILOTCHATVOICE_READER_PREVIEW";
    internal const string PreferenceVariable = "VSCOPILOTCHATVOICE_READER_PREFERENCE_PATH";
    internal const string DetailedFixture = "[{\"id\":\"intro\",\"kind\":\"heading\",\"text\":\"Prepared native reader fixture — Detailed\"}," +
        "{\"id\":\"presentation\",\"kind\":\"paragraph\",\"text\":\"This labelled reply is a prepared fixture, not a Copilot reply. The native reader presents text while a separate worker speaks.\"}," +
        "{\"id\":\"controls\",\"kind\":\"step\",\"number\":1,\"text\":\"Choose a word, then Play. Pause and Resume keep the same prompt. Sentence and paragraph controls have distinct destinations.\"}," +
        "{\"id\":\"follow\",\"kind\":\"paragraph\",\"text\":\"Manual wheel or scrollbar browsing suspends Follow. Follow or an explicit seek restores it without focusing Chat or an editor.\"}," +
        "{\"id\":\"versions\",\"kind\":\"paragraph\",\"text\":\"Concise and Detailed share section identities. Switching versions retains the section and stays paused. Closing retains your place; reopening never starts speech.\"}]";
    internal const string ConciseFixture = "[{\"id\":\"intro\",\"kind\":\"heading\",\"text\":\"Prepared native reader fixture — Concise\"}," +
        "{\"id\":\"presentation\",\"kind\":\"paragraph\",\"text\":\"Prepared fixture only. Speech stays external.\"}," +
        "{\"id\":\"controls\",\"kind\":\"step\",\"number\":1,\"text\":\"Choose a word and Play. Pause or Resume; navigate sentences or paragraphs.\"}," +
        "{\"id\":\"follow\",\"kind\":\"paragraph\",\"text\":\"Browse to suspend Follow. Follow or seek restores it.\"}," +
        "{\"id\":\"versions\",\"kind\":\"paragraph\",\"text\":\"Switch versions while paused. Close retains position; reopen is silent.\"}]";

    internal static bool IsEnabled(string[]? args = null, string? optIn = null)
    {
#if DEBUG
        args = args ?? Environment.GetCommandLineArgs();
        return (optIn ?? Environment.GetEnvironmentVariable(GateVariable)) == "1" &&
            Enumerable.Range(0, Math.Max(0, args.Length - 1)).Any(i =>
                string.Equals(args[i], "/RootSuffix", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(args[i + 1], "Exp", StringComparison.OrdinalIgnoreCase));
#else
        return false;
#endif
    }

    internal static string PreferencePath(string[]? args = null, string? optIn = null, string? overridePath = null)
    {
        string? configured = overridePath ?? Environment.GetEnvironmentVariable(PreferenceVariable);
        return IsEnabled(args, optIn) && !string.IsNullOrWhiteSpace(configured) ? configured! :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatVoice", "reader-version.txt");
    }

    internal static NativeReaderView Create(string workerPath, bool silent, Action hide)
    {
        if (!IsEnabled()) throw new InvalidOperationException("The native reader preview requires Debug, /RootSuffix Exp and explicit opt-in.");
        return new NativeReaderView(new ReaderSpeechBackend(workerPath, silent), PreferencePath(),
            DetailedFixture, ConciseFixture, hide, Observe);
    }

    private static void Observe(Task task) =>
        ThreadHelper.JoinableTaskFactory.RunAsync(() => task).FileAndForget("vscopilotchatvoice/reader/native");
}
