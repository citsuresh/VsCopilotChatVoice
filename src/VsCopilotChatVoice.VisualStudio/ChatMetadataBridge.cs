using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using VsCopilotChatVoice.Bridge;

namespace VsCopilotChatVoice.VisualStudio;

// Phase 1 exposes metadata only. A target epoch is not permission to edit or send.
internal sealed class ChatMetadataBridge : IDisposable
{
    private readonly Dictionary<FrameworkElement, ChatTargetObservation> observations = new Dictionary<FrameworkElement, ChatTargetObservation>();
    private readonly ChatMetadataServer server;
    private readonly DispatcherTimer timer;
    private readonly Action<string> report;
    private bool disposed;

    internal static bool IsEnabled(string[]? args = null, string? optIn = null)
    {
#if DEBUG
        args = args ?? Environment.GetCommandLineArgs();
        return (optIn ?? Environment.GetEnvironmentVariable("VSCOPILOTCHATVOICE_METADATA_POC")) == "1" &&
            Enumerable.Range(0, Math.Max(0, args.Length - 1)).Any(i =>
                string.Equals(args[i], "/RootSuffix", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(args[i + 1], "Exp", StringComparison.OrdinalIgnoreCase));
#else
        return false;
#endif
    }

    internal ChatMetadataBridge(Action<string> report, Action<Exception> reportError)
    {
        this.report = report ?? throw new ArgumentNullException(nameof(report));
        if (reportError == null) throw new ArgumentNullException(nameof(reportError));
        server = new ChatMetadataServer(BridgeProtocol.PipeName(Process.GetCurrentProcess().Id),
            cancellation => CaptureAsync(cancellation, reportError), reportError);
        _ = server.Completion.ContinueWith(t => reportError(t.Exception!.GetBaseException()), CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += Prune;
        timer.Start();
    }

    private async Task<BridgeTarget[]> CaptureAsync(CancellationToken cancellation, Action<Exception> reportError)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (disposed) throw new ObjectDisposedException(nameof(ChatMetadataBridge));
        List<FrameworkElement> roots = ChatBridgePoc.Inspect(Application.Current.Windows.Cast<Window>()).NativeTargets;
        if (roots.Count > BridgeProtocol.MaxTargets) throw new InvalidDataException("Metadata target capacity exceeded.");
        foreach (FrameworkElement missing in observations.Keys.Where(r => !roots.Contains(r)).ToArray()) Retire(missing);
        var targets = new List<BridgeTarget>();
        foreach (FrameworkElement root in roots)
        {
            cancellation.ThrowIfCancellationRequested();
            observations.TryGetValue(root, out ChatTargetObservation? observation);
            var snapshot = new ChatBridgePoc.ProbeSnapshot();
            try { ChatBridgePoc.CaptureTarget(snapshot, root, ref observation, reportError, ScheduleAsync); }
            finally
            {
                if (observation != null) observations[root] = observation;
                else observations.Remove(root);
            }
            ChatTargetObservation.ObservationSnapshot state = snapshot.Target ?? throw new InvalidDataException("Target observation unavailable.");
            var target = new BridgeTarget
            {
                SessionId = snapshot.SessionId ?? throw new InvalidDataException("Session identity unavailable."),
                ClientId = snapshot.ClientId ?? throw new InvalidDataException("Client identity unavailable."), Epoch = Guid.ParseExact(state.Generation, "N"),
                Revision = state.DraftRevision, Status = state.Status == "Observing" ? "Observed" : "Invalidated",
                Trusted = state.IsTrusted, Responding = state.IsResponding,
                ReadOnly = snapshot.IsReadOnly ?? throw new InvalidDataException("Read-only state unavailable."),
                Transcript = snapshot.IsTranscript ?? throw new InvalidDataException("Transcript state unavailable.")
            };
            target.Validate();
            targets.Add(target);
        }
        cancellation.ThrowIfCancellationRequested();
        report("Metadata bridge snapshot: targets=" + targets.Count + "; no contents or edit/send capability.");
        return targets.ToArray();
    }

    private static async Task ScheduleAsync(Action action)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        action();
    }

    private void Prune(object? sender, EventArgs args)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (FrameworkElement root in observations.Keys.Where(r => !r.IsVisible ||
            observations[r].Snapshot().Status != "Observing").ToArray()) Retire(root);
    }

    private void Retire(FrameworkElement root)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        observations[root].Dispose();
        observations.Remove(root);
    }

    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        timer.Dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        timer.Stop();
        timer.Tick -= Prune;
        server.Dispose();
        foreach (FrameworkElement root in observations.Keys.ToArray()) Retire(root);
    }
}