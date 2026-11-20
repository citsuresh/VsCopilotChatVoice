using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class BridgePocRegressionTests
{
    private static int assertions;

    internal static int Run(string assemblyPath)
    {
        Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        Type type = assembly.GetType("VsCopilotChatVoice.VisualStudio.ChatBridgePoc", true);
        MethodInfo enabled = type.GetMethod("IsEnabled", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo inspect = type.GetMethod("Inspect", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(IEnumerable<Window>) }, null);
        string previous = Environment.GetEnvironmentVariable("VSCOPILOTCHATVOICE_BRIDGE_POC");
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var control = new Border { DataContext = new Context() };
        AutomationProperties.SetAutomationId(control, "session");
        var window = new Window
        {
            Content = control, Width = 200, Height = 100, Left = -10000, Top = -10000,
            ShowInTaskbar = false, ShowActivated = false
        };
        try
        {
            Environment.SetEnvironmentVariable("VSCOPILOTCHATVOICE_BRIDGE_POC", "1");
            Check(!(bool)enabled.Invoke(null, null), "An environment variable alone enabled the bridge outside Exp.");
            window.Show();
            object snapshot = inspect.Invoke(null, new object[] { new[] { window } });
            object[] controls = ((IEnumerable)Property(snapshot, "Controls")).Cast<object>().ToArray();
            Check(controls.Length == 1, "Probe did not select the explicit session control.");
            Check((string)Property(controls[0], "AutomationId") == "session", "Probe lost the source control ID.");
            Check((string)Property(controls[0], "ContextType") == typeof(Context).FullName, "Probe lost binding metadata.");
            string[] identities = ((IEnumerable)Property(controls[0], "Identities")).Cast<string>().ToArray();
            Check(identities.SequenceEqual(new[] { "SessionId=" + Context.Session }), "Identity was guessed or omitted.");
            Check((int)Property(snapshot, "Visited") > 0, "Probe did not report its traversal.");
            Check(((string[])Property(controls[0], "Properties")).Any(value => value.StartsWith("Sensitive:", StringComparison.Ordinal)),
                "Metadata inventory omitted a public property.");
            Check(Context.SensitiveReads == 0, "Probe read unrelated context content.");

            control.DataContext = new BindingContext();
            snapshot = inspect.Invoke(null, new object[] { new[] { window } });
            object nested = ((IEnumerable)Property(snapshot, "Controls")).Cast<object>().Single();
            object[] bindings = ((IEnumerable)Property(nested, "Bindings")).Cast<object>().ToArray();
            Check(bindings.Select(value => (string)Property(value, "Name")).SequenceEqual(new[] { "Session", "Composer", "ChatLog" }),
                "Probe did not restrict related bindings to the three observed paths.");
            object session = bindings[0];
            Check((string)Property(session, "ContextType") == typeof(SessionContext).FullName, "Probe lost the nested runtime type.");
            Check(((IEnumerable)Property(session, "Identities")).Cast<string>().SequenceEqual(new[] { "Id=" + Context.Session }),
                "An inherited, explicitly implemented session identity was omitted.");
            Check(((string[])Property(session, "Properties")).Any(value => value.StartsWith("Sensitive:", StringComparison.Ordinal)),
                "Nested metadata inventory omitted a public property.");
            Check(((IEnumerable)Property(bindings[1], "Identities")).Cast<string>().Count() == 0,
                "A private identity getter was read.");
            Check((string)Property(bindings[2], "ContextType") == "(none)", "A missing related binding was hidden.");
            Check(!((IEnumerable)Property(bindings[2], "Identities")).Cast<string>().Any(), "A null binding acquired an identity.");
            Check(BindingContext.UnrelatedReads == 0 && SessionContext.SensitiveReads == 0 && ComposerContext.IdentityReads == 0,
                "Nested inspection read content or a private getter.");

            control.DataContext = new DerivedBindingContext();
            snapshot = inspect.Invoke(null, new object[] { new[] { window } });
            object hidden = ((IEnumerable)Property(snapshot, "Controls")).Cast<object>().Single();
            object[] hiddenBindings = ((IEnumerable)Property(hidden, "Bindings")).Cast<object>().ToArray();
            Check(hiddenBindings.Length == 1, "A hidden inherited property made binding selection ambiguous.");
            Check(((IEnumerable)Property(hiddenBindings[0], "Identities")).Cast<string>()
                .SequenceEqual(new[] { "Id=" + Context.Session }), "Probe did not choose the most-derived binding.");
            Check(BaseBindingContext.Reads == 0, "Probe invoked the hidden base binding getter.");

            control.DataContext = new PrivateBindingContext();
            snapshot = inspect.Invoke(null, new object[] { new[] { window } });
            object privateBinding = ((IEnumerable)Property(snapshot, "Controls")).Cast<object>().Single();
            Check(!((IEnumerable)Property(privateBinding, "Bindings")).Cast<object>().Any(),
                "An inaccessible shadowing getter fell back to a hidden base binding.");
            Check(BaseBindingContext.Reads == 0 && PrivateBindingContext.Reads == 0,
                "A private or hidden base getter was invoked.");

            control.DataContext = new InheritedBindingContext();
            snapshot = inspect.Invoke(null, new object[] { new[] { window } });
            object inherited = ((IEnumerable)Property(snapshot, "Controls")).Cast<object>().Single();
            object inheritedSession = ((IEnumerable)Property(inherited, "Bindings")).Cast<object>().Single();
            Check((string)Property(inheritedSession, "ContextType") == typeof(SessionContext).FullName,
                "An unshadowed inherited public binding was omitted.");

            control.DataContext = new TypedIdentityContext("test-client");
            snapshot = inspect.Invoke(null, new object[] { new[] { window } });
            object typed = ((IEnumerable)Property(snapshot, "Controls")).Cast<object>().Single();
            Check(((IEnumerable)Property(typed, "Identities")).Cast<string>().SequenceEqual(new[]
                { "SessionId.Guid=" + Context.Session, "SessionId.ClientId.Id=test-client" }),
                "Typed session identity lost its Guid or client namespace.");
            Check(Microsoft.VisualStudio.Copilot.CopilotSessionId.Stringifications == 0,
                "Probe stringified an arbitrary typed identity.");
            control.DataContext = new TypedIdentityContext(new string('x', 257));
            try
            {
                inspect.Invoke(null, new object[] { new[] { window } });
                throw new InvalidOperationException("Oversized typed client identity was accepted.");
            }
            catch (TargetInvocationException error)
            {
                Check(error.InnerException is InvalidDataException, "Oversized typed identity was not explicitly rejected.");
            }

            control.DataContext = new PromptBindingContext();
            snapshot = inspect.Invoke(null, new object[] { new[] { window } });
            object prompt = ((IEnumerable)Property(snapshot, "Controls")).Cast<object>().Single();
            object[] promptBindings = ((IEnumerable)Property(prompt, "Bindings")).Cast<object>().ToArray();
            Check(promptBindings.Select(value => (string)Property(value, "Name"))
                .SequenceEqual(new[] { "Composer", "Composer.PromptInput", "Composer.ActionButton" }),
                "Composer inspection followed unapproved paths or lost the observed children.");
            Check(((string[])Property(promptBindings[1], "Properties")).Any(value => value.StartsWith("Text:", StringComparison.Ordinal)),
                "Prompt metadata was not inventoried.");
            Check(PromptInputContext.ContentReads == 0 && PromptComposerContext.UnrelatedReads == 0,
                "Composer inspection read a draft or followed unrelated content.");

            VerifyObservation(assembly, window, control);
            control.DataContext = null;
            snapshot = inspect.Invoke(null, new object[] { new[] { window } });
            object empty = ((IEnumerable)Property(snapshot, "Controls")).Cast<object>().Single();
            Check((string)Property(empty, "ContextType") == "(none)", "A missing binding was hidden.");
            Check(!((IEnumerable)Property(empty, "Identities")).Cast<string>().Any(), "Probe invented an identity.");

            window.Hide();
            snapshot = inspect.Invoke(null, new object[] { new[] { window } });
            Check(!((IEnumerable)Property(snapshot, "Controls")).Cast<object>().Any(), "Hidden windows were inspected.");
            VerifyConnections(type);
            Console.WriteLine("PASS: " + assertions + " bridge probe/IPC assertions; no VS input, speech or user preferences.");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("VSCOPILOTCHATVOICE_BRIDGE_POC", previous);
            window.Close();
        }
    }

    private static void VerifyObservation(Assembly assembly, Window window, FrameworkElement anchor)
    {
        Type observerType = assembly.GetType("VsCopilotChatVoice.VisualStudio.ChatTargetObservation", true);
        ConstructorInfo constructor = observerType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(FrameworkElement), typeof(INotifyPropertyChanged), typeof(object),
                typeof(object), typeof(INotifyPropertyChanged), typeof(IObservable<bool>),
                typeof(IObservable<bool>), typeof(Action<Exception>), typeof(Func<Action, Task>) }, null);
        MethodInfo snapshot = observerType.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo matches = observerType.GetMethod("Matches", BindingFlags.Instance | BindingFlags.NonPublic);
        var root = new NotifyingContext();
        var input = new NotifyingContext();
        var composer = new NotifyingContext();
        var session = new object();
        var trusted = new BooleanFeed();
        var responding = new BooleanFeed();
        var errors = new ConcurrentQueue<Exception>();
        anchor.DataContext = root;
        Func<Action, Task> scheduleOnUi = action =>
        {
            if (!anchor.Dispatcher.CheckAccess()) return anchor.Dispatcher.InvokeAsync(action).Task;
            action();
            return Task.CompletedTask;
        };
        Func<IDisposable> start = () => (IDisposable)constructor.Invoke(new object[]
            { anchor, root, session, composer, input, trusted, responding, new Action<Exception>(errors.Enqueue), scheduleOnUi });
        string generation;
        using (IDisposable observation = start())
        {
            object state = snapshot.Invoke(observation, null);
            generation = (string)Property(state, "Generation");
            Guid parsed;
            Check(Guid.TryParseExact(generation, "N", out parsed) && parsed != Guid.Empty, "Observation epoch is missing.");
            Check((long)Property(state, "DraftRevision") == 0, "New observation did not start at revision zero.");
            Check((bool)Property(state, "HasInputReplacementNotifications"), "Available input replacement notifications were omitted.");
            Check(Property(state, "IsTrusted") == null && Property(state, "IsResponding") == null,
                "Unobserved trust/responding state was given a success-shaped default.");
            Check((bool)matches.Invoke(observation, new object[] { anchor, root, session, composer, input }), "Current target did not match.");
            Check(!(bool)matches.Invoke(observation, new object[] { anchor, root, new object(), composer, input }), "Replaced session matched.");
            Check(!(bool)matches.Invoke(observation, new object[] { anchor, root, session, composer, new NotifyingContext() }), "Replaced input matched.");
            input.Notify("Unrelated");
            Check((long)Property(snapshot.Invoke(observation, null), "DraftRevision") == 0, "Unrelated input notification changed the revision.");
            input.Notify("Prompt");
            input.Notify("Prompt");
            Check((long)Property(snapshot.Invoke(observation, null), "DraftRevision") == 2,
                "An ABA draft change lost its monotonic revision.");
            input.Notify(null);
            input.Notify("");
            Task.Run(() => input.Notify("Prompt")).GetAwaiter().GetResult();
            Check((long)Property(snapshot.Invoke(observation, null), "DraftRevision") == 5,
                "Wildcard/background notifications were lost.");
            trusted.Next(true);
            responding.Next(false);
            state = snapshot.Invoke(observation, null);
            Check((bool)Property(state, "IsTrusted") && !(bool)Property(state, "IsResponding"), "Observed state values were not retained.");
            trusted.Next(false);
            responding.Next(true);
            state = snapshot.Invoke(observation, null);
            Check(!(bool)Property(state, "IsTrusted") && (bool)Property(state, "IsResponding"), "Trust/busy transitions were lost.");
            root.Notify("Unrelated");
            Check((string)Property(snapshot.Invoke(observation, null), "Status") == "Observing", "Unrelated root notification invalidated the target.");
            root.Notify("Session");
            state = snapshot.Invoke(observation, null);
            Check((string)Property(state, "Status") == "BindingChanged", "Session replacement did not invalidate the observation.");
            Check(Property(state, "IsTrusted") == null && Property(state, "IsResponding") == null, "Invalid target retained usable readiness.");
            Check(!(bool)matches.Invoke(observation, new object[] { anchor, root, session, composer, input }), "Invalidated target still matched.");
            trusted.LateNext(true);
            responding.LateNext(false);
            input.Notify("Prompt");
            state = snapshot.Invoke(observation, null);
            Check(Property(state, "IsTrusted") == null && (long)Property(state, "DraftRevision") == 5,
                "Late events revived the invalidated target.");
        }
        Check(root.Subscribers == 0 && composer.Subscribers == 0 && input.Subscribers == 0 && trusted.Active == 0 && responding.Active == 0,
            "Disposal retained observation subscriptions.");
        using (IDisposable observation = start())
        {
            Check((string)Property(snapshot.Invoke(observation, null), "Generation") != generation,
                "A recreated observation reused its prior epoch.");
            window.Hide();
            Check((string)Property(snapshot.Invoke(observation, null), "Status") == "TargetHidden", "Hiding the target did not invalidate it.");
            window.Show();
            Check(!(bool)matches.Invoke(observation, new object[] { anchor, root, session, composer, input }), "Showing the old target revived its epoch.");
        }
        using (IDisposable observation = start())
        {
            anchor.DataContext = new NotifyingContext();
            Check((string)Property(snapshot.Invoke(observation, null), "Status") == "ContextReplaced", "DataContext replacement did not invalidate the target.");
        }
        anchor.DataContext = root;
        using (IDisposable observation = start())
        {
            responding.Complete();
            Check((string)Property(snapshot.Invoke(observation, null), "Status") == "RespondingObservationEnded", "Completed state feed remained usable.");
            Exception ended;
            Check(errors.TryDequeue(out ended) && ended is InvalidOperationException, "Completed state feed was not reported.");
        }
        using (IDisposable observation = start())
        {
            var failure = new InvalidOperationException("Synthetic trust feed failure.");
            trusted.Fail(failure);
            Check((string)Property(snapshot.Invoke(observation, null), "Status") == "TrustObservationEnded", "Failed state feed remained usable.");
            Exception reported;
            Check(errors.TryDequeue(out reported) && ReferenceEquals(reported, failure), "State feed failure was not reported exactly.");
        }
        using (IDisposable observation = start())
        {
            anchor.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Check((string)Property(snapshot.Invoke(observation, null), "Status") == "TargetUnloaded", "Unloading the target did not invalidate it.");
        }
        responding.ThrowOnSubscribe = true;
        try
        {
            start();
            throw new InvalidOperationException("Failed subscription was accepted.");
        }
        catch (TargetInvocationException error)
        {
            Check(error.InnerException is InvalidOperationException, "Subscription failure was not explicit.");
        }
        finally { responding.ThrowOnSubscribe = false; }
        Check(root.Subscribers == 0 && composer.Subscribers == 0 && input.Subscribers == 0 && trusted.Active == 0 && responding.Active == 0,
            "Partial initialization retained subscriptions.");
        IDisposable stopped = start();
        stopped.Dispose();
        stopped.Dispose();
        trusted.LateNext(true);
        responding.LateNext(false);
        input.Notify("Prompt");
        object disposed = snapshot.Invoke(stopped, null);
        Check((string)Property(disposed, "Status") == "Disposed" && Property(disposed, "IsTrusted") == null &&
            Property(disposed, "IsResponding") == null && (long)Property(disposed, "DraftRevision") == 0,
            "Late callbacks revived a disposed observation.");
        Check(root.PromptReads == 0 && input.PromptReads == 0, "Passive observation read draft content.");
        Check(errors.IsEmpty, "Observation reported an unexpected error.");
        using (IDisposable observation = start())
        {
            observerType.GetField("draftRevision", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(observation, long.MaxValue);
            input.Notify("Prompt");
            object exhausted = snapshot.Invoke(observation, null);
            Check((string)Property(exhausted, "Status") == "DraftRevisionExhausted" &&
                (long)Property(exhausted, "DraftRevision") == long.MaxValue, "Revision exhaustion wrapped or revived the observation.");
        }
        using (IDisposable observation = start())
        using (var scheduled = new ManualResetEventSlim())
        {
            MethodInfo requestDisposal = observerType.GetMethod("RequestDisposal", BindingFlags.Instance | BindingFlags.NonPublic);
            Task cleanup = null;
            Task scheduling = Task.Run(() =>
            {
                requestDisposal.Invoke(observation, null);
                cleanup = (Task)Property(observerType.GetField("cleanup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(observation), "Task");
                scheduled.Set();
            });
            Check(scheduled.Wait(5000), "Background cleanup was not scheduled.");
            scheduling.GetAwaiter().GetResult();
            trusted.LateNext(true);
            responding.LateNext(false);
            object pending = snapshot.Invoke(observation, null);
            Check((string)Property(pending, "Status") == "OwnerDisposed" && Property(pending, "IsTrusted") == null &&
                Property(pending, "IsResponding") == null, "Background disposal allowed late readiness before UI cleanup.");
            PumpCleanup(cleanup, anchor.Dispatcher);
            Check(root.Subscribers == 0 && composer.Subscribers == 0 && input.Subscribers == 0 && trusted.Active == 0 && responding.Active == 0,
                "Background cleanup retained subscriptions.");
        }
        using (IDisposable observation = start())
        {
            composer.Notify("PromptInput");
            Check((string)Property(snapshot.Invoke(observation, null), "Status") == "InputBindingChanged",
                "Transient prompt-input replacement did not invalidate the epoch.");
            Check(!(bool)matches.Invoke(observation, new object[] { anchor, root, session, composer, input }),
                "Restoring a replaced prompt input revived its old epoch.");
        }
        using (var observation = (IDisposable)constructor.Invoke(new object[]
            { anchor, root, session, new object(), input, trusted, responding, new Action<Exception>(errors.Enqueue), scheduleOnUi }))
        {
            Check(!(bool)Property(snapshot.Invoke(observation, null), "HasInputReplacementNotifications"),
                "A non-notifying native composer was given a success-shaped notification capability.");
            input.Notify("Prompt");
            Check((long)Property(snapshot.Invoke(observation, null), "DraftRevision") == 1,
                "Missing composer notifications disabled observed prompt changes.");
        }
        trusted.CompleteOnSubscribe = true;
        try
        {
            using (IDisposable observation = start())
            {
                Check((string)Property(snapshot.Invoke(observation, null), "Status") == "TrustObservationEnded",
                    "A synchronous constructor callback lost its terminal reason.");
                Check(root.Subscribers == 0 && composer.Subscribers == 0 && input.Subscribers == 0 &&
                    trusted.Active == 0 && responding.Active == 0,
                    "Synchronous subscription completion escaped deferred constructor cleanup.");
                Check(ReleasedReferences(observerType, observation), "Constructor completion retained native objects.");
                Exception ended;
                Check(errors.TryDequeue(out ended) && ended is InvalidOperationException, "Synchronous feed completion was not reported.");
            }
        }
        finally { trusted.CompleteOnSubscribe = false; }
        using (IDisposable observation = start())
        {
            var failure = new InvalidOperationException("Synthetic background state failure.");
            Task.Run(() => trusted.Fail(failure)).GetAwaiter().GetResult();
            Task cleanup = (Task)Property(observerType.GetField("cleanup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(observation), "Task");
            PumpCleanup(cleanup, anchor.Dispatcher);
            Check(root.Subscribers == 0 && composer.Subscribers == 0 && input.Subscribers == 0 &&
                trusted.Active == 0 && responding.Active == 0, "A background terminal event required inspection/disposal to detach.");
            Check(ReleasedReferences(observerType, observation), "Background terminal cleanup retained native objects.");
            trusted.LateNext(true);
            Check((string)Property(snapshot.Invoke(observation, null), "Status") == "TrustObservationEnded",
                "Background cleanup lost or revived its terminal reason.");
            Exception reported;
            Check(errors.TryDequeue(out reported) && ReferenceEquals(reported, failure), "Background terminal failure was not reported.");
        }
        var closingAnchor = new Border { DataContext = root };
        var closingWindow = new Window
        {
            Content = closingAnchor, Width = 200, Height = 100, Left = -10000, Top = -10000,
            ShowInTaskbar = false, ShowActivated = false
        };
        try
        {
            closingWindow.Show();
            using (var observation = (IDisposable)constructor.Invoke(new object[]
                { closingAnchor, root, session, composer, input, trusted, responding, new Action<Exception>(errors.Enqueue), scheduleOnUi }))
            {
                closingWindow.Close();
                Check(root.Subscribers == 0 && composer.Subscribers == 0 && input.Subscribers == 0 &&
                    trusted.Active == 0 && responding.Active == 0,
                    "Closing a target retained subscriptions without another inspection.");
                Check(ReleasedReferences(observerType, observation), "Closing a target retained native/model references.");
                string terminal = (string)Property(snapshot.Invoke(observation, null), "Status");
                Check(terminal == "TargetHidden" || terminal == "TargetUnloaded", "Automatic closure cleanup replaced its terminal reason.");
            }
        }
        finally { closingWindow.Close(); }
    }

    private static bool ReleasedReferences(Type type, object observation) =>
        new[] { "anchor", "context", "composer", "composerChanges", "session", "input", "scheduleOnUi",
            "trustedSubscription", "respondingSubscription" }
        .All(name => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(observation) == null);

    private static void PumpCleanup(Task cleanup, Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Send,
            (sender, args) => frame.Continue = false, dispatcher);
        _ = cleanup.ContinueWith(task => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        try { Dispatcher.PushFrame(frame); }
        finally { timeout.Stop(); }
        Check(cleanup.IsCompleted, "Background cleanup did not finish on the UI dispatcher.");
        cleanup.GetAwaiter().GetResult();
    }

    private static void VerifyConnections(Type type)
    {
        Type snapshotType = type.Assembly.GetType(type.FullName + "+ProbeSnapshot", true);
        Type captureType = typeof(Func<,>).MakeGenericType(typeof(CancellationToken), typeof(Task<>).MakeGenericType(snapshotType));
        MethodInfo factory = typeof(BridgePocRegressionTests).GetMethod(nameof(Capture), BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(snapshotType);
        Delegate capture = Delegate.CreateDelegate(captureType, factory);
        ConstructorInfo constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(Action<Exception>), typeof(string), typeof(TimeSpan), captureType }, null);
        string name = "VsCopilotChatVoice.Test." + Guid.NewGuid().ToString("N");
        var errors = new ConcurrentQueue<Exception>();
        using (var reported = new SemaphoreSlim(0))
        {
            var server = (IDisposable)constructor.Invoke(new object[]
            {
                new Action<Exception>(error => { errors.Enqueue(error); reported.Release(); }),
                name, TimeSpan.FromSeconds(1), capture
            });
            Task worker = (Task)type.GetField("worker", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(server);
            try
            {
                string baseline = ValidReply(name);
                Check(baseline.Contains("\"Visited\":1"), "Baseline diagnostic request failed.");
                Check(!baseline.Contains("NativeTargets"), "Native WPF references escaped the diagnostic JSON contract.");
                using (NamedPipeClientStream client = Connect(name))
                {
                    Write(client, "bad\n");
                    Check(Read(client) == "", "Malformed request produced a success snapshot.");
                }
                ExpectError<InvalidDataException>(errors, reported);
                Check(ValidReply(name).Contains("\"Visited\":1"), "Malformed request killed the listener.");

                using (NamedPipeClientStream client = Connect(name)) { }
                ExpectDisconnect(errors, reported);
                Check(ValidReply(name).Contains("\"Visited\":1"), "Early disconnect killed the listener.");

                using (NamedPipeClientStream client = Connect(name)) { Write(client, "inspect"); }
                ExpectDisconnect(errors, reported);
                Check(ValidReply(name).Contains("\"Visited\":1"), "Unterminated request killed the listener.");

                using (NamedPipeClientStream stalled = Connect(name))
                {
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    ExpectError<TimeoutException>(errors, reported);
                    Check(elapsed.ElapsedMilliseconds < 5000, "Idle request exceeded its bounded deadline.");
                    Check(ValidReply(name).Contains("\"Visited\":1"), "Idle client blocked the next request after timeout.");
                }
                using (NamedPipeClientStream stalled = Connect(name))
                {
                    Write(stalled, "ins");
                    ExpectError<TimeoutException>(errors, reported);
                    Check(ValidReply(name).Contains("\"Visited\":1"), "Partial request blocked the next client after timeout.");
                }

                oversized = true;
                try
                {
                    using (NamedPipeClientStream client = Connect(name))
                    {
                        Write(client, "inspect\n");
                        Check(Read(client) == "", "Oversized response escaped its limit.");
                    }
                    ExpectError<InvalidDataException>(errors, reported);
                }
                finally { oversized = false; }
                Check(ValidReply(name).Contains("\"Visited\":1"), "Oversized snapshot killed the listener.");

                ambiguousCapture = true;
                try
                {
                    using (NamedPipeClientStream client = Connect(name))
                    {
                        Write(client, "inspect\n");
                        Check(Read(client) == "", "Ambiguous capture produced a success snapshot.");
                    }
                    ExpectError<AmbiguousMatchException>(errors, reported);
                }
                finally { ambiguousCapture = false; }
                Check(ValidReply(name).Contains("\"Visited\":1"), "Reflection failure killed the listener.");

                using (NamedPipeClientStream stalled = Connect(name))
                {
                    server.Dispose();
                    Check(worker.Wait(5000), "Shutdown did not release the idle connection.");
                    Check(worker.Status == TaskStatus.RanToCompletion, "Shutdown faulted the listener.");
                }
                Check(errors.IsEmpty, "Shutdown was reported as a client timeout/error.");
            }
            finally
            {
                oversized = false;
                ambiguousCapture = false;
                server.Dispose();
                if (!worker.Wait(5000)) throw new InvalidOperationException("Test listener failed to stop.");
            }
        }
    }

    private static volatile bool oversized;
    private static volatile bool ambiguousCapture;

    private static Task<T> Capture<T>(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ambiguousCapture) return Task.FromException<T>(new AmbiguousMatchException("Synthetic binding collision."));
        T snapshot = (T)Activator.CreateInstance(typeof(T), true);
        typeof(T).GetProperty("Visited").SetValue(snapshot, 1);
        if (oversized)
        {
            Type controlType = typeof(T).Assembly.GetType(typeof(T).DeclaringType.FullName + "+ProbeControl", true);
            object control = Activator.CreateInstance(controlType, true);
            controlType.GetProperty("Type").SetValue(control, new string('x', 262145));
            ((IList)typeof(T).GetProperty("Controls").GetValue(snapshot)).Add(control);
        }
        return Task.FromResult(snapshot);
    }

    private static NamedPipeClientStream Connect(string name)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        bool connected = false;
        try { client.Connect(5000); connected = true; return client; }
        finally { if (!connected) client.Dispose(); }
    }

    private static void Write(NamedPipeClientStream client, string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        Task write = client.WriteAsync(bytes, 0, bytes.Length);
        Check(Task.WhenAny(write, Task.Delay(5000)).GetAwaiter().GetResult() == write, "Test request write timed out.");
        write.GetAwaiter().GetResult();
    }

    private static string Read(NamedPipeClientStream client)
    {
        using (var reader = new StreamReader(client, Encoding.UTF8, false, 4096, true))
        {
            Task<string> read = reader.ReadToEndAsync();
            Check(Task.WhenAny(read, Task.Delay(5000)).GetAwaiter().GetResult() == read, "Test response read timed out.");
            return read.GetAwaiter().GetResult();
        }
    }

    private static string ValidReply(string name)
    {
        using (NamedPipeClientStream client = Connect(name))
        {
            Write(client, "inspect\n");
            return Read(client);
        }
    }

    private static void ExpectError<T>(ConcurrentQueue<Exception> errors, SemaphoreSlim reported) where T : Exception
    {
        Check(reported.Wait(5000), "Listener did not report the expected " + typeof(T).Name);
        Exception error;
        Check(errors.TryDequeue(out error) && error is T, "Listener reported the wrong rejection/timeout type: " + error);
    }

    private static void ExpectDisconnect(ConcurrentQueue<Exception> errors, SemaphoreSlim reported)
    {
        Check(reported.Wait(5000), "Listener did not report the disconnected client.");
        Exception error;
        Check(errors.TryDequeue(out error) && (error is InvalidDataException || error is IOException),
            "Disconnect did not produce an explicit framing/transport rejection: " + error);
    }

    private static object Property(object value, string name) => value.GetType().GetProperty(name).GetValue(value);

    private static void Check(bool success, string message)
    {
        assertions++;
        if (!success) throw new InvalidOperationException(message);
    }

    public sealed class Context
    {
        internal static readonly Guid Session = new Guid("c7a6df35-8ac9-4241-b9fc-7519c186cd4f");
        internal static int SensitiveReads;
        public Guid SessionId => Session;
        public string Sensitive { get { SensitiveReads++; throw new InvalidOperationException("Must not read content."); } }
    }

    public interface IIdentityContext { Guid Id { get; } }
    public interface ISessionContext : IIdentityContext { }

    public sealed class BindingContext
    {
        internal static int UnrelatedReads;
        public ISessionContext Session { get; } = new SessionContext();
        public ComposerContext Composer { get; } = new ComposerContext();
        public object ChatLog => null;
        public object Unrelated { get { UnrelatedReads++; throw new InvalidOperationException("Must not follow this path."); } }
    }

    public sealed class SessionContext : ISessionContext
    {
        internal static int SensitiveReads;
        Guid IIdentityContext.Id => Context.Session;
        public string Sensitive { get { SensitiveReads++; throw new InvalidOperationException("Must not read nested content."); } }
        public object Session { get { SensitiveReads++; throw new InvalidOperationException("Must not recurse."); } }
    }

    public sealed class ComposerContext
    {
        internal static int IdentityReads;
        public string Id
        {
            private get { IdentityReads++; throw new InvalidOperationException("Must not read a private getter."); }
            set { }
        }
    }

    public class BaseBindingContext
    {
        internal static int Reads;
        public object Session { get { Reads++; throw new InvalidOperationException("Must not read a hidden base binding."); } }
    }

    public sealed class DerivedBindingContext : BaseBindingContext
    {
        public new ISessionContext Session { get; } = new SessionContext();
    }

    public sealed class PrivateBindingContext : BaseBindingContext
    {
        internal new static int Reads;
        public new string Session
        {
            private get { Reads++; throw new InvalidOperationException("Must not read a private binding getter."); }
            set { }
        }
    }

    public class PublicBindingContext
    {
        public ISessionContext Session { get; } = new SessionContext();
    }

    public sealed class InheritedBindingContext : PublicBindingContext { }

    public sealed class TypedIdentityContext
    {
        public TypedIdentityContext(string clientId)
        {
            SessionId = new Microsoft.VisualStudio.Copilot.CopilotSessionId(Context.Session,
                new Microsoft.VisualStudio.Copilot.CopilotClientId(clientId));
        }
        public Microsoft.VisualStudio.Copilot.CopilotSessionId SessionId { get; }
    }

    public sealed class PromptBindingContext
    {
        public PromptComposerContext Composer { get; } = new PromptComposerContext();
    }

    public sealed class PromptComposerContext
    {
        internal static int UnrelatedReads;
        public PromptInputContext PromptInput { get; } = new PromptInputContext();
        public object ActionButton => null;
        public object Unrelated { get { UnrelatedReads++; throw new InvalidOperationException("Must not follow unrelated composer paths."); } }
    }

    public sealed class PromptInputContext
    {
        internal static int ContentReads;
        public string Text { get { ContentReads++; throw new InvalidOperationException("Must not read a draft."); } }
    }

    public sealed class NotifyingContext : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler changed;
        internal int Subscribers;
        internal int PromptReads;
        public event PropertyChangedEventHandler PropertyChanged
        {
            add { changed += value; Subscribers++; }
            remove { changed -= value; Subscribers--; }
        }
        public string Prompt { get { PromptReads++; throw new InvalidOperationException("Must not read a draft."); } }
        internal void Notify(string name) => changed?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class BooleanFeed : IObservable<bool>
    {
        private IObserver<bool> observer;
        private IObserver<bool> lastObserver;
        internal int Active;
        internal bool ThrowOnSubscribe;
        internal bool CompleteOnSubscribe;
        public IDisposable Subscribe(IObserver<bool> value)
        {
            if (ThrowOnSubscribe) throw new InvalidOperationException("Synthetic subscription failure.");
            if (observer != null) throw new InvalidOperationException("Duplicate observation subscription.");
            observer = lastObserver = value;
            Active++;
            if (CompleteOnSubscribe) value.OnCompleted();
            return new FeedSubscription(this);
        }
        internal void Next(bool value) => observer.OnNext(value);
        internal void Complete() => observer.OnCompleted();
        internal void Fail(Exception error) => observer.OnError(error);
        internal void LateNext(bool value) => lastObserver.OnNext(value);
        private sealed class FeedSubscription : IDisposable
        {
            private BooleanFeed owner;
            internal FeedSubscription(BooleanFeed owner) { this.owner = owner; }
            public void Dispose()
            {
                if (owner == null) return;
                owner.observer = null;
                owner.Active--;
                owner = null;
            }
        }
    }
}

namespace Microsoft.VisualStudio.Copilot
{
    public readonly struct CopilotSessionId
    {
        internal static int Stringifications;
        public CopilotSessionId(Guid guid, CopilotClientId clientId) { Guid = guid; ClientId = clientId; }
        public Guid Guid { get; }
        public CopilotClientId ClientId { get; }
        public override string ToString() { Stringifications++; throw new InvalidOperationException("Must read only verified identity fields."); }
    }

    public sealed class CopilotClientId
    {
        public CopilotClientId(string id) { Id = id; }
        public string Id { get; }
    }
}
