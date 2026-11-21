using System;
using System.Threading;
using System.Threading.Tasks;
using VsCopilotChatVoice.Speech;

namespace VsCopilotChatVoice.VisualStudio;

internal interface IReaderSpeechBackend
{
    Task<IReaderSpeechSession> LaunchAsync(CancellationToken cancellation);
}

internal interface IReaderSpeechSession : IDisposable
{
    event Action<long, int> Progress;
    event Action<long> Completed;
    event Action<string> Faulted;
    Task<long> SpeakAsync(string text, CancellationToken cancellation);
    Task<bool> PauseAsync(long generation, CancellationToken cancellation);
    Task<bool> ResumeAsync(long generation, CancellationToken cancellation);
    Task<bool> CancelAsync();
}

// No executable assembly reference: only the external worker owns System.Speech.
internal sealed class ReaderSpeechBackend : IReaderSpeechBackend
{
    private readonly string workerPath;
    private readonly bool silent;

    internal ReaderSpeechBackend(string workerPath, bool silent)
    {
        this.workerPath = workerPath;
        this.silent = silent;
    }

    public async Task<IReaderSpeechSession> LaunchAsync(CancellationToken cancellation)
    {
        SpeechWorkerHost host = await Task.Run(() => SpeechWorkerHost.LaunchAsync(
            workerPath, silent, TimeSpan.FromSeconds(15), cancellation)).ConfigureAwait(false);
        return new Session(host);
    }

    private sealed class Session : IReaderSpeechSession
    {
        private readonly SpeechWorkerHost host;
        internal Session(SpeechWorkerHost host) { this.host = host; }
        public event Action<long, int> Progress { add { host.Progress += value; } remove { host.Progress -= value; } }
        public event Action<long> Completed { add { host.Completed += value; } remove { host.Completed -= value; } }
        public event Action<string> Faulted { add { host.Faulted += value; } remove { host.Faulted -= value; } }
        public Task<long> SpeakAsync(string text, CancellationToken cancellation) => host.SpeakAsync(text, cancellation);
        public Task<bool> PauseAsync(long generation, CancellationToken cancellation) => host.PauseAsync(generation, cancellation);
        public Task<bool> ResumeAsync(long generation, CancellationToken cancellation) => host.ResumeAsync(generation, cancellation);
        public Task<bool> CancelAsync() => host.CancelAsync();
        public void Dispose() => host.Dispose();
    }
}
