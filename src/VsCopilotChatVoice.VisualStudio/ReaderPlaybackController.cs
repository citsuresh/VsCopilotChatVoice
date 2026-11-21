using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ReadAlongTrial;

// This reusable owner verifies its supplied dispatcher, not the VS Shell's global main thread.
// InvokeAsync never blocks; every continuation touching the model explicitly returns to that dispatcher.
#pragma warning disable VSTHRD001, VSTHRD010

namespace VsCopilotChatVoice.VisualStudio;

internal enum ReaderPlaybackPhase { Stopped, Starting, Speaking, Pausing, Paused, Resuming, Finished, Error, Disposed }

// One reply only. This owner has no view, activation, focus, scrolling or preference-file side effects.
internal sealed class ReaderPlaybackController : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly IReaderSpeechBackend backend;
    private readonly Guid replyId;
    private readonly List<Segment> detailed;
    private readonly List<Segment>? concise;
    private List<Segment> segments;
    private bool useConcise;
    private long version = 1;
    private long operation;
    private int index;
    private int position;
    private int promptBase;
    private ReaderPlaybackPhase phase;
    private string? error;
    private IReaderSpeechSession? session;
    private PromptOperation? active;
    private Task retirement = Task.CompletedTask;

    internal ReaderPlaybackController(Dispatcher dispatcher, IReaderSpeechBackend backend, Guid replyId,
        List<Segment> detailed, List<Segment>? concise, bool useConcise)
    {
        dispatcher.VerifyAccess();
        if (dispatcher.HasShutdownStarted) throw new InvalidOperationException("The reader dispatcher is shutting down.");
        if (replyId == Guid.Empty) throw new ArgumentException("A reply lifetime identity is required.", nameof(replyId));
        Validate(detailed);
        if (concise != null) { Validate(concise); DocumentBuilder.ValidateVersions(detailed, concise); }
        if (useConcise && concise == null) throw new InvalidOperationException("No Concise version supplied for this reply.");
        this.dispatcher = dispatcher;
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.replyId = replyId;
        this.detailed = new List<Segment>(detailed);
        this.concise = concise == null ? null : new List<Segment>(concise);
        this.useConcise = useConcise;
        segments = useConcise ? this.concise! : this.detailed;
    }

    private static void Validate(List<Segment> values)
    {
        if (values == null || values.Count == 0) throw new ArgumentException("Readable sections are required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Segment value in values)
            if (value == null || string.IsNullOrWhiteSpace(value.Id) || !ids.Add(value.Id) ||
                string.IsNullOrWhiteSpace(value.Spoken) || value.Words.Count == 0 || value.SentenceStarts.Count == 0)
                throw new ArgumentException("Valid, uniquely identified reader sections are required.");
    }

    internal Guid ReplyId { get { dispatcher.VerifyAccess(); return replyId; } }
    internal long Version { get { dispatcher.VerifyAccess(); return version; } }
    internal long Operation { get { dispatcher.VerifyAccess(); return operation; } }
    internal bool IsConcise { get { dispatcher.VerifyAccess(); return useConcise; } }
    internal ReaderPlaybackPhase Phase { get { dispatcher.VerifyAccess(); return phase; } }
    internal string? Error { get { dispatcher.VerifyAccess(); return error; } }
    internal ReadingPosition Position { get { dispatcher.VerifyAccess(); return new ReadingPosition(index, position); } }
    internal int PromptBase { get { dispatcher.VerifyAccess(); return promptBase; } }
    internal long SpeechGeneration { get { dispatcher.VerifyAccess(); return active?.Generation ?? 0; } }
    internal WordAnchor? SelectedWord { get { dispatcher.VerifyAccess(); return segments[index].FindWord(position); } }

    internal Task PlayAsync()
    {
        VerifyAlive();
        if (phase == ReaderPlaybackPhase.Paused && active != null) return ChangeStateAsync(false);
        if (phase == ReaderPlaybackPhase.Starting || phase == ReaderPlaybackPhase.Speaking ||
            phase == ReaderPlaybackPhase.Pausing || phase == ReaderPlaybackPhase.Resuming) return Task.CompletedTask;
        if (phase == ReaderPlaybackPhase.Finished) { index = 0; position = 0; }
        return StartSegmentAsync();
    }

    internal Task PauseAsync()
    {
        VerifyAlive();
        return phase == ReaderPlaybackPhase.Speaking && active != null ? ChangeStateAsync(true) : Task.CompletedTask;
    }

    internal Task SeekAsync(ReadingPosition target)
    {
        VerifyAlive();
        if (target.Segment < 0 || target.Segment >= segments.Count || target.Offset < 0 ||
            target.Offset >= segments[target.Segment].Spoken.Length) throw new ArgumentOutOfRangeException(nameof(target));
        bool playing = phase == ReaderPlaybackPhase.Speaking;
        bool paused = phase == ReaderPlaybackPhase.Paused || phase == ReaderPlaybackPhase.Pausing;
        index = target.Segment;
        position = target.Offset;
        promptBase = 0;
        error = null;
        phase = paused ? ReaderPlaybackPhase.Paused : ReaderPlaybackPhase.Stopped;
        Task release = InvalidateAsync(true);
        return playing ? StartSegmentAsync() : release;
    }

    internal Task NavigateAsync(int direction, bool paragraph)
    {
        VerifyAlive();
        ReadingPosition target = ReadingNavigation.Move(segments, new ReadingPosition(index, position), direction, paragraph);
        return target.Segment == index && target.Offset == position ? Task.CompletedTask : SeekAsync(target);
    }

    internal Task SwitchVersionAsync(bool requestedConcise)
    {
        VerifyAlive();
        if (useConcise == requestedConcise) return Task.CompletedTask;
        if (requestedConcise && concise == null) throw new InvalidOperationException("No Concise version supplied for this reply.");
        string section = segments[index].Id;
        useConcise = requestedConcise;
        version++;
        segments = useConcise ? concise! : detailed;
        index = segments.FindIndex(value => value.Id == section);
        position = segments[index].Words[0].Start;
        promptBase = 0;
        error = null;
        phase = ReaderPlaybackPhase.Paused;
        return InvalidateAsync(true);
    }

    internal Task StopAsync()
    {
        VerifyAlive();
        phase = ReaderPlaybackPhase.Stopped;
        error = null;
        return InvalidateAsync(true);
    }

    internal Task HideAsync() => StopAsync();

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (phase == ReaderPlaybackPhase.Disposed) return;
        _ = InvalidateAsync(true);
        phase = ReaderPlaybackPhase.Disposed;
    }

    private void VerifyAlive()
    {
        dispatcher.VerifyAccess();
        if (phase == ReaderPlaybackPhase.Disposed) throw new ObjectDisposedException(nameof(ReaderPlaybackController));
    }

    private bool Current(PromptOperation value) => active == value && phase != ReaderPlaybackPhase.Disposed;

    private Task OnDispatcherAsync(Action action)
    {
        if (dispatcher.CheckAccess()) { action(); return Task.CompletedTask; }
        return dispatcher.InvokeAsync(action).Task;
    }

    private Task StartSegmentAsync()
    {
        error = null;
        promptBase = position;
        var value = new PromptOperation(this, ++operation, version, promptBase, segments[index].Spoken.Substring(promptBase));
        active = value;
        phase = ReaderPlaybackPhase.Starting;
        value.Start = StartAsync(value, session, retirement);
        return value.Start;
    }

    private async Task StartAsync(PromptOperation value, IReaderSpeechSession? existing, Task released)
    {
        IReaderSpeechSession? created = null;
        try
        {
#pragma warning disable VSTHRD003 // Owned cancellation/retirement is asynchronous; no caller blocks the dispatcher.
            await released.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            if (existing == null)
            {
                Task<IReaderSpeechSession>? launch = null;
                await OnDispatcherAsync(() =>
                {
                    if (Current(value)) launch = backend.LaunchAsync(value.Token);
                }).ConfigureAwait(false);
                if (launch == null) return;
                created = await launch.ConfigureAwait(false);
                if (created == null) throw new InvalidOperationException("The speech backend returned no session.");
                await OnDispatcherAsync(() =>
                {
                    if (!Current(value)) return;
                    session = created;
                    existing = created;
                    created = null;
                }).ConfigureAwait(false);
            }
            Task<long>? start = null;
            await OnDispatcherAsync(() =>
            {
                if (!Current(value)) return;
                value.Attach(existing!);
                start = existing!.SpeakAsync(value.Text, value.Token);
            }).ConfigureAwait(false);
            if (start == null) return;
            long generation = await start.ConfigureAwait(false);
            await OnDispatcherAsync(() =>
            {
                if (!Current(value)) return;
                if (generation < 1) { Fail("The speech backend returned an invalid generation."); return; }
                value.Generation = generation;
                phase = ReaderPlaybackPhase.Speaking;
                value.Ready();
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is OperationCanceledException ||
            ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception || ex is TimeoutException)
        {
            if (!dispatcher.HasShutdownStarted)
                await OnDispatcherAsync(() => { if (Current(value)) Fail("Speech could not start: " + ex.Message); }).ConfigureAwait(false);
        }
        finally
        {
            // A cancelled launch may ignore its token. Dispose its local result, never the owner's newer session.
            if (created != null) created.Dispose();
        }
    }

    private async Task ChangeStateAsync(bool pause)
    {
        PromptOperation value = active!;
        long command = ++value.Command;
        phase = pause ? ReaderPlaybackPhase.Pausing : ReaderPlaybackPhase.Resuming;
        try
        {
            Task<bool> acknowledgement = pause ? value.Session!.PauseAsync(value.Generation, value.Token) :
                value.Session!.ResumeAsync(value.Generation, value.Token);
            if (!await acknowledgement.ConfigureAwait(false)) return;
            await OnDispatcherAsync(() =>
            {
                if (Current(value) && value.Command == command)
                    phase = pause ? ReaderPlaybackPhase.Paused : ReaderPlaybackPhase.Speaking;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is OperationCanceledException || ex is TimeoutException)
        {
            if (!dispatcher.HasShutdownStarted)
                await OnDispatcherAsync(() => { if (Current(value) && value.Command == command) Fail("Speech state change failed: " + ex.Message); }).ConfigureAwait(false);
        }
    }

    private Task InvalidateAsync(bool retire)
    {
        PromptOperation? previous = active;
        active = null;
        operation++;
        previous?.Close();
        if (!retire || session == null) return previous?.Start ?? Task.CompletedTask;
        IReaderSpeechSession owned = session;
        session = null;
        retirement = ReleaseAsync(owned, operation);
        return retirement;
    }

    private async Task ReleaseAsync(IReaderSpeechSession owned, long retiredAt)
    {
        try { await owned.CancelAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is OperationCanceledException || ex is TimeoutException)
        {
            if (!dispatcher.HasShutdownStarted)
                await OnDispatcherAsync(() =>
                {
                    if (operation == retiredAt && (phase == ReaderPlaybackPhase.Stopped || phase == ReaderPlaybackPhase.Paused))
                    { error = "Speech cancellation failed: " + ex.Message; phase = ReaderPlaybackPhase.Error; }
                }).ConfigureAwait(false);
        }
        finally { owned.Dispose(); }
    }

    private void Fail(string message)
    {
        _ = InvalidateAsync(true);
        error = message;
        phase = ReaderPlaybackPhase.Error;
    }

    private void Drain(PromptOperation value, int offset, bool completed, string? fault)
    {
        dispatcher.VerifyAccess();
        if (!Current(value) || value.Version != version) return;
        if (fault != null) { Fail(fault); return; }
        if (offset >= 0 && offset >= value.LastOffset &&
            (phase == ReaderPlaybackPhase.Speaking || phase == ReaderPlaybackPhase.Pausing || phase == ReaderPlaybackPhase.Resuming))
        {
            value.LastOffset = offset;
            WordAnchor? word = segments[index].FindWord(value.Base + offset);
            if (word != null) position = word.Start;
        }
        if (!completed) return;
        bool pause = phase == ReaderPlaybackPhase.Paused || phase == ReaderPlaybackPhase.Pausing;
        bool statePending = phase == ReaderPlaybackPhase.Pausing || phase == ReaderPlaybackPhase.Resuming;
        _ = InvalidateAsync(statePending);
        if (index + 1 == segments.Count) { phase = ReaderPlaybackPhase.Finished; return; }
        index++;
        position = 0;
        promptBase = 0;
        if (pause) phase = ReaderPlaybackPhase.Paused;
        else _ = StartSegmentAsync();
    }

    // A single queued dispatcher callback and at most 16 early generation slots per prompt. Progress coalesces
    // monotonically; completion is sticky until Speak returns its generation. No unbounded worker/UI event queue.
    private sealed class PromptOperation
    {
        private sealed class Pending { internal int Offset = -1; internal bool Completed; }
        private readonly ReaderPlaybackController owner;
        private readonly object sync = new object();
        private readonly Dictionary<long, Pending> pending = new Dictionary<long, Pending>();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private DispatcherOperation? queued;
        private bool closed;
        private bool ready;
        private string? fault;
        internal readonly long Id;
        internal readonly long Version;
        internal readonly int Base;
        internal readonly string Text;
        internal readonly CancellationToken Token;
        internal Task Start = Task.CompletedTask;
        internal IReaderSpeechSession? Session;
        internal long Generation;
        internal long Command;
        internal int LastOffset = -1;

        internal PromptOperation(ReaderPlaybackController owner, long id, long version, int promptBase, string text)
        {
            this.owner = owner; Id = id; Version = version; Base = promptBase; Text = text; Token = cancellation.Token;
        }

        internal void Attach(IReaderSpeechSession value)
        {
            Session = value;
            value.Progress += Progress;
            value.Completed += Completed;
            value.Faulted += Faulted;
        }

        private void Progress(long generation, int offset) { if (offset >= 0 && offset < Text.Length) Receive(generation, offset, false); }
        private void Completed(long generation) => Receive(generation, -1, true);
        private void Faulted(string message)
        {
            lock (sync) { if (closed) return; fault = "Speech failed: " + message; Schedule(); }
        }

        private void Receive(long generation, int offset, bool completed)
        {
            lock (sync)
            {
                if (closed || generation < 1 || (ready && generation != Generation)) return;
                if (!pending.TryGetValue(generation, out Pending value))
                {
                    if (pending.Count == 16) { fault = "Speech event capacity exceeded before start acknowledgement."; Schedule(); return; }
                    value = new Pending(); pending.Add(generation, value);
                }
                value.Offset = Math.Max(value.Offset, offset);
                value.Completed |= completed;
                Schedule();
            }
        }

        internal void Ready()
        {
            lock (sync)
            {
                ready = true;
                Pending? matching = pending.TryGetValue(Generation, out Pending value) ? value : null;
                pending.Clear();
                if (matching != null) pending.Add(Generation, matching);
                Schedule();
            }
        }

        private void Schedule()
        {
            if (!ready || queued != null || owner.dispatcher.HasShutdownStarted) return;
            queued = owner.dispatcher.InvokeAsync(Drain);
        }

        private void Drain()
        {
            int offset = -1; bool completed = false; string? failure;
            lock (sync)
            {
                queued = null;
                if (closed) return;
                if (pending.TryGetValue(Generation, out Pending value)) { offset = value.Offset; completed = value.Completed; }
                pending.Clear(); failure = fault; fault = null;
            }
            owner.Drain(this, offset, completed, failure);
        }

        internal void Close()
        {
            lock (sync) { closed = true; queued?.Abort(); queued = null; pending.Clear(); fault = null; }
            if (Session != null) { Session.Progress -= Progress; Session.Completed -= Completed; Session.Faulted -= Faulted; }
            cancellation.Cancel(); cancellation.Dispose();
        }
    }
}
