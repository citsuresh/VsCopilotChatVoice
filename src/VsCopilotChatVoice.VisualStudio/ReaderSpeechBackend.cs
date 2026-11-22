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
    private readonly Func<SpeechOptionsSnapshot>? optionsProvider;
    private readonly Func<AudioPolicySettings>? audioPolicyProvider;

    internal ReaderSpeechBackend(string workerPath, bool silent)
        : this(workerPath, silent, null, null)
    {
    }

    internal ReaderSpeechBackend(string workerPath, bool silent, Func<SpeechOptionsSnapshot>? optionsProvider,
        Func<AudioPolicySettings>? audioPolicyProvider)
    {
        this.workerPath = workerPath;
        this.silent = silent;
        this.optionsProvider = optionsProvider;
        this.audioPolicyProvider = audioPolicyProvider;
    }

    public async Task<IReaderSpeechSession> LaunchAsync(CancellationToken cancellation)
    {
        SpeechWorkerHost host = await Task.Run(() => SpeechWorkerHost.LaunchAsync(
            workerPath, silent, TimeSpan.FromSeconds(15), cancellation)).ConfigureAwait(false);
        return new Session(host, optionsProvider, audioPolicyProvider);
    }

    private sealed class Session : IReaderSpeechSession
    {
        private readonly SpeechWorkerHost host;
        private readonly Func<SpeechOptionsSnapshot>? optionsProvider;
        private readonly Func<AudioPolicySettings>? audioPolicyProvider;
        private AudioPolicyScope? audioScope;
        internal Session(SpeechWorkerHost host, Func<SpeechOptionsSnapshot>? optionsProvider,
            Func<AudioPolicySettings>? audioPolicyProvider)
        {
            this.host = host;
            this.optionsProvider = optionsProvider;
            this.audioPolicyProvider = audioPolicyProvider;
            host.Completed += generation => ReleaseAudio();
            host.Faulted += error => ReleaseAudio();
        }
        public event Action<long, int> Progress { add { host.Progress += value; } remove { host.Progress -= value; } }
        public event Action<long> Completed { add { host.Completed += value; } remove { host.Completed -= value; } }
        public event Action<string> Faulted { add { host.Faulted += value; } remove { host.Faulted -= value; } }
        public Task<long> SpeakAsync(string text, CancellationToken cancellation)
        {
            ReleaseAudio();
            try
            {
                if (audioPolicyProvider != null) audioScope = AudioPolicyScope.Begin(audioPolicyProvider(), host.WorkerProcessId);
                SpeechOptionsSnapshot? options = optionsProvider == null ? null : optionsProvider();
                return options == null ? host.SpeakAsync(text, cancellation) : host.SpeakWithOptionsAsync(text, options, cancellation);
            }
            catch
            {
                ReleaseAudio();
                host.Dispose();
                throw;
            }
        }
        public Task<bool> PauseAsync(long generation, CancellationToken cancellation) => host.PauseAsync(generation, cancellation);
        public Task<bool> ResumeAsync(long generation, CancellationToken cancellation) => host.ResumeAsync(generation, cancellation);
        public async Task<bool> CancelAsync()
        {
            try { return await host.CancelAsync().ConfigureAwait(false); }
            finally { ReleaseAudio(); }
        }
        private void ReleaseAudio()
        {
            AudioPolicyScope? scope = audioScope;
            audioScope = null;
            if (scope != null) scope.Dispose();
        }
        public void Dispose()
        {
            try { host.Dispose(); }
            finally { ReleaseAudio(); }
        }
    }
}
