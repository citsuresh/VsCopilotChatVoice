using System;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VsCopilotChatVoice.VisualStudio;

// Passive PoC observation only; a snapshot does not authorize input or submission.
internal sealed class ChatTargetObservation : IDisposable
{
    private readonly object sync = new object();
    private FrameworkElement? anchor;
    private INotifyPropertyChanged? context;
    private object? composer;
    private INotifyPropertyChanged? composerChanges;
    private object? session;
    private INotifyPropertyChanged? input;
    private readonly Action<Exception> reportError;
    private Func<Action, Task>? scheduleOnUi;
    private readonly bool hasInputReplacementNotifications;
    private readonly TaskCompletionSource<bool> cleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string generation = Guid.NewGuid().ToString("N");
    private IDisposable? trustedSubscription;
    private IDisposable? respondingSubscription;
    private long draftRevision;
    private bool? trusted;
    private bool? responding;
    private string status = "Observing";
    private bool disposed;
    private bool initializing = true;
    private bool cleanupQueued;

    internal ChatTargetObservation(FrameworkElement anchor, INotifyPropertyChanged context,
        object session, object composer, INotifyPropertyChanged input, IObservable<bool> trusted,
        IObservable<bool> responding, Action<Exception> reportError, Func<Action, Task> scheduleOnUi)
    {
        this.anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.composer = composer ?? throw new ArgumentNullException(nameof(composer));
        composerChanges = composer as INotifyPropertyChanged;
        hasInputReplacementNotifications = composerChanges != null;
        this.input = input ?? throw new ArgumentNullException(nameof(input));
        this.reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        this.scheduleOnUi = scheduleOnUi ?? throw new ArgumentNullException(nameof(scheduleOnUi));
        _ = cleanup.Task.ContinueWith(task => reportError(task.Exception!.GetBaseException()),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        if (trusted == null) throw new ArgumentNullException(nameof(trusted));
        if (responding == null) throw new ArgumentNullException(nameof(responding));
        anchor.Dispatcher.VerifyAccess();
        if (!anchor.IsVisible || !ReferenceEquals(anchor.DataContext, context))
            throw new InvalidOperationException("The observation target is not current and visible.");
        bool attached = false;
        try
        {
            input.PropertyChanged += InputChanged;
            context.PropertyChanged += ContextChanged;
            if (composerChanges != null) composerChanges.PropertyChanged += ComposerChanged;
            anchor.DataContextChanged += ContextReplaced;
            anchor.IsVisibleChanged += VisibilityChanged;
            anchor.Unloaded += Unloaded;
            trustedSubscription = trusted.Subscribe(new StateObserver(this, true)) ??
                throw new InvalidOperationException("Trust observation returned no lifetime.");
            respondingSubscription = responding.Subscribe(new StateObserver(this, false)) ??
                throw new InvalidOperationException("Responding observation returned no lifetime.");
            attached = true;
        }
        finally
        {
            lock (sync) { initializing = false; }
            if (!attached)
            {
                TryInvalidate("ConstructionFailed");
                Detach();
            }
            else QueueCleanup();
        }
    }

    internal bool Matches(FrameworkElement candidate, object candidateContext, object candidateSession,
        object candidateComposer, object candidateInput)
    {
        lock (sync)
            return !disposed && status == "Observing" && ReferenceEquals(anchor, candidate) &&
                ReferenceEquals(context, candidateContext) && ReferenceEquals(session, candidateSession) &&
                ReferenceEquals(composer, candidateComposer) &&
                ReferenceEquals(input, candidateInput);
    }

    internal ObservationSnapshot Snapshot()
    {
        lock (sync)
            return new ObservationSnapshot
            {
                Generation = generation, DraftRevision = draftRevision, Status = status,
                IsTrusted = trusted, IsResponding = responding,
                HasInputReplacementNotifications = hasInputReplacementNotifications
            };
    }

    internal void Invalidate(string reason)
    {
        if (TryInvalidate(reason)) QueueCleanup();
    }

    private bool TryInvalidate(string reason)
    {
        lock (sync)
        {
            if (status != "Observing") return false;
            status = reason;
            trusted = null;
            responding = null;
            return true;
        }
    }

    private void InputChanged(object sender, PropertyChangedEventArgs args)
    {
        if (!string.IsNullOrEmpty(args.PropertyName) && args.PropertyName != "Prompt") return;
        lock (sync)
        {
            if (disposed || status != "Observing") return;
            if (draftRevision == long.MaxValue) Invalidate("DraftRevisionExhausted");
            else draftRevision++;
        }
    }

    private void ContextChanged(object sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == "Session" || args.PropertyName == "Composer")
            Invalidate("BindingChanged");
    }

    private void ContextReplaced(object sender, DependencyPropertyChangedEventArgs args) => Invalidate("ContextReplaced");
    private void ComposerChanged(object sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == "PromptInput")
            Invalidate("InputBindingChanged");
    }
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!(bool)args.NewValue) Invalidate("TargetHidden");
    }
    private void Unloaded(object sender, RoutedEventArgs args) => Invalidate("TargetUnloaded");

    private void StateChanged(bool isTrust, bool value)
    {
        lock (sync)
        {
            if (disposed || status != "Observing") return;
            if (isTrust) trusted = value;
            else responding = value;
        }
    }

    private void StateEnded(bool isTrust, Exception? error)
    {
        if (!TryInvalidate(isTrust ? "TrustObservationEnded" : "RespondingObservationEnded")) return;
        QueueCleanup();
        reportError(error ?? new InvalidOperationException("A required chat state observation completed."));
    }

    internal void RequestDisposal()
    {
        Invalidate("OwnerDisposed");
        QueueCleanup();
    }

    public void Dispose()
    {
        anchor?.Dispatcher.VerifyAccess();
        TryInvalidate("Disposed");
        Detach();
    }

    private void QueueCleanup()
    {
        Func<Action, Task> scheduler;
        lock (sync)
        {
            if (initializing || cleanupQueued || disposed || status == "Observing") return;
            cleanupQueued = true;
            scheduler = scheduleOnUi ?? throw new InvalidOperationException("The observation cleanup scheduler is unavailable.");
        }
        Task scheduled;
        try
        {
            scheduled = scheduler(Detach) ?? throw new InvalidOperationException("Observation cleanup returned no task.");
        }
        catch (InvalidOperationException error)
        {
            cleanup.TrySetException(error);
            return;
        }
        _ = scheduled.ContinueWith(task =>
        {
            if (task.IsFaulted) cleanup.TrySetException(task.Exception!.GetBaseException());
            else if (task.IsCanceled) cleanup.TrySetException(new OperationCanceledException("Observation UI cleanup was canceled."));
            else if (!cleanup.Task.IsCompleted)
                cleanup.TrySetException(new InvalidOperationException("Observation UI cleanup did not detach its target."));
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Detach()
    {
        FrameworkElement previousAnchor;
        INotifyPropertyChanged previousContext;
        INotifyPropertyChanged previousInput;
        INotifyPropertyChanged? previousComposer;
        IDisposable? previousTrusted;
        IDisposable? previousResponding;
        lock (sync)
        {
            if (disposed) return;
            previousAnchor = anchor ?? throw new InvalidOperationException("The observation anchor is unavailable.");
            previousAnchor.Dispatcher.VerifyAccess();
            previousContext = context!;
            previousInput = input!;
            previousComposer = composerChanges;
            previousTrusted = trustedSubscription;
            previousResponding = respondingSubscription;
            disposed = true;
            trusted = null;
            responding = null;
            anchor = null;
            context = null;
            input = null;
            session = null;
            composer = null;
            composerChanges = null;
            scheduleOnUi = null;
            trustedSubscription = null;
            respondingSubscription = null;
        }
        previousInput.PropertyChanged -= InputChanged;
        previousContext.PropertyChanged -= ContextChanged;
        if (previousComposer != null) previousComposer.PropertyChanged -= ComposerChanged;
        previousAnchor.DataContextChanged -= ContextReplaced;
        previousAnchor.IsVisibleChanged -= VisibilityChanged;
        previousAnchor.Unloaded -= Unloaded;
        try { previousTrusted?.Dispose(); }
        finally { previousResponding?.Dispose(); }
        cleanup.TrySetResult(true);
    }

    private sealed class StateObserver : IObserver<bool>
    {
        private readonly ChatTargetObservation owner;
        private readonly bool isTrust;
        internal StateObserver(ChatTargetObservation owner, bool isTrust) { this.owner = owner; this.isTrust = isTrust; }
        public void OnNext(bool value) => owner.StateChanged(isTrust, value);
        public void OnError(Exception error) => owner.StateEnded(isTrust, error);
        public void OnCompleted() => owner.StateEnded(isTrust, null);
    }

    [DataContract]
    internal sealed class ObservationSnapshot
    {
        [DataMember] public string Generation { get; set; } = "";
        [DataMember] public long DraftRevision { get; set; }
        [DataMember] public string Status { get; set; } = "";
        [DataMember] public bool? IsTrusted { get; set; }
        [DataMember] public bool? IsResponding { get; set; }
        [DataMember] public bool HasInputReplacementNotifications { get; set; }
    }
}