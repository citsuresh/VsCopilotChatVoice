using System;
using System.Threading;
using System.Threading.Tasks;
using VsCopilotChatVoice.Speech;

namespace VsCopilotChatVoice.VisualStudio;

internal static class SpeechPreview
{
    // The worker acknowledges options before it starts speaking, and SpeakWithOptionsAsync completes on that
    // acknowledgement. Callers must keep the host alive until this returns, or the speech is cut off silently.
    internal static async Task SpeakAsync(SpeechWorkerHost host, string text, SpeechOptionsSnapshot options, TimeSpan timeout, CancellationToken token)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        var finished = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<long> completed = generation => finished.TrySetResult(null);
        Action<string> faulted = message => finished.TrySetResult(message);
        host.Completed += completed;
        host.Faulted += faulted;
        try
        {
            await host.SpeakWithOptionsAsync(text, options, token).ConfigureAwait(false);
            Task first = await Task.WhenAny(finished.Task, Task.Delay(timeout, token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (first != finished.Task) throw new TimeoutException("The speech worker did not finish the preview in time.");
            string? fault = await finished.Task.ConfigureAwait(false);
            if (fault != null) throw new InvalidOperationException(fault);
        }
        finally
        {
            host.Completed -= completed;
            host.Faulted -= faulted;
        }
    }
}
