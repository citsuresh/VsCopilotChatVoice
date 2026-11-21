using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Threading;

// Loads production controller/adapter from the supplied DLL. RealProxy implements its internal typed seam;
// no Compile-linked playback implementation, packages, preference files or Visual Studio windows are used.
internal static class ReaderPlaybackRegressionTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly assembly;
    private static Type controllerType, backendType, sessionType;
    private static int assertions;
    private static readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private const string Detailed = "[{\"id\":\"a\",\"kind\":\"paragraph\",\"text\":\"Alpha beta gamma. Next sentence.\"}," +
        "{\"id\":\"b\",\"kind\":\"step\",\"text\":\"First line. Next line.\",\"number\":2}]";
    private const string Concise = "[{\"id\":\"b\",\"kind\":\"step\",\"text\":\"Brief step.\",\"number\":2}," +
        "{\"id\":\"a\",\"kind\":\"paragraph\",\"text\":\"  Brief alpha.\"}]";

    // Native presentation checks reuse the actual-DLL typed fake boundary, not a linked production owner.
    internal static void InitializeSeam(Assembly loaded)
    {
        backendType = loaded.GetType("VsCopilotChatVoice.VisualStudio.IReaderSpeechBackend", true);
        sessionType = loaded.GetType("VsCopilotChatVoice.VisualStudio.IReaderSpeechSession", true);
    }

    internal static int Run(string assemblyPath)
    {
        SynchronizationContext original = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
            controllerType = assembly.GetType("VsCopilotChatVoice.VisualStudio.ReaderPlaybackController", true);
            backendType = assembly.GetType("VsCopilotChatVoice.VisualStudio.IReaderSpeechBackend", true);
            sessionType = assembly.GetType("VsCopilotChatVoice.VisualStudio.IReaderSpeechSession", true);
            Check(!controllerType.IsPublic && !backendType.IsPublic && !sessionType.IsPublic, "Playback widened the production public API.");
            Check(!assembly.GetReferencedAssemblies().Any(a => a.Name == "System.Speech" || a.Name == "VsCopilotChatVoice.Reader"),
                "The VSIX acquired an in-process synthesis/executable dependency.");
            ExceptionBoundary();
            BasicAndSeeking();
            VersionAndValidation();
            PauseResumeAndCompletion();
            PendingStateLifetimeRaces();
            CompletionStateOrdering();
            EarlyEventsAndBounds();
            StartupAndLifetimeRaces();
            ErrorsAndIndependentOwners();
            DispatcherAffinity();
            RealAdapter();
            Console.WriteLine("PASS: actual-DLL reader playback checks, " + assertions + " assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally { SynchronizationContext.SetSynchronizationContext(original); }
    }

    private static void Check(bool value, string description)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(description);
    }

    internal static void PumpUntil(Func<bool> condition, int milliseconds = 5000)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!condition() && DateTime.UtcNow < end)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            SynchronizationContext.SetSynchronizationContext(null);
        }
        if (!condition()) throw new TimeoutException("Playback fixture did not settle within " + milliseconds + " ms.");
    }

    internal static void Settle() { DateTime end = DateTime.UtcNow.AddMilliseconds(60); PumpUntil(() => DateTime.UtcNow >= end); }
    internal static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Get<T>(object owner, string name) => (T)controllerType.GetProperty(name, Members).GetValue(owner);
    private static Task Call(object owner, string name, params object[] args) => (Task)controllerType.GetMethod(name, Members).Invoke(owner, args);
    private static void Dispose(object owner) => ((IDisposable)owner).Dispose();
    private static string Phase(object owner) => Get<object>(owner, "Phase").ToString();
    private static int Segment(object owner) => (int)Get<object>(owner, "Position").GetType().GetField("Segment").GetValue(Get<object>(owner, "Position"));
    private static int Offset(object owner) => (int)Get<object>(owner, "Position").GetType().GetField("Offset").GetValue(Get<object>(owner, "Position"));
    private static object Position(int segment, int offset) => Activator.CreateInstance(assembly.GetType("ReadAlongTrial.ReadingPosition", true), segment, offset);
    private static Task Seek(object owner, int segment, int offset) => Call(owner, "SeekAsync", Position(segment, offset));
    private static IList Load(string json)
    {
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            return (IList)assembly.GetType("ReadAlongTrial.DocumentBuilder", true).GetMethod("LoadReply").Invoke(null, new object[] { new FlowDocument(), input });
    }

    private static object Owner(FakeBackend backend, string detailed = Detailed, string concise = Concise, bool initialConcise = false) =>
        Owner(backend.Proxy, detailed, concise, initialConcise);
    private static object Owner(object backend, string detailed, string concise, bool initialConcise = false) =>
        Activator.CreateInstance(controllerType, Members, null, new object[] { dispatcher, backend, Guid.NewGuid(), Load(detailed),
            concise == null ? null : Load(concise), initialConcise }, null);

    private static void Reject(Action action, Type expected, string description)
    {
        try { action(); }
        catch (Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            Check(expected.IsInstanceOfType(ex), description + ": " + ex);
            return;
        }
        throw new InvalidOperationException(description + " (not refused)");
    }

    private sealed class SeamProxy : RealProxy
    {
        private readonly Type contract;
        private readonly Func<IMethodCallMessage, object> invoke;
        internal SeamProxy(Type type, Func<IMethodCallMessage, object> invoke) : base(type) { contract = type; this.invoke = invoke; }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try
            {
                object result = call.MethodName == "GetType" ? contract : invoke(call);
                return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
            }
            catch (Exception ex) { return new ReturnMessage(ex, call); }
        }
    }

    internal sealed class Launch
    {
        internal readonly FakeSession Session;
        internal readonly CancellationToken Token;
        private readonly object completion;
        internal Launch(FakeSession session, CancellationToken token)
        {
            Session = session; Token = token;
            completion = Activator.CreateInstance(typeof(TaskCompletionSource<>).MakeGenericType(sessionType), TaskCreationOptions.RunContinuationsAsynchronously);
        }
        internal object Task => completion.GetType().GetProperty("Task").GetValue(completion);
        internal void Complete() => completion.GetType().GetMethod("SetResult").Invoke(completion, new[] { Session.Proxy });
        internal void Fail(Exception ex)
        {
            ((IDisposable)Session.Proxy).Dispose();
            completion.GetType().GetMethod("SetException", new[] { typeof(Exception) }).Invoke(completion, new object[] { ex });
        }
    }

    internal sealed class FakeBackend
    {
        internal readonly object Proxy;
        internal readonly List<Launch> Launches = new List<Launch>();
        internal bool HoldLaunch;
        internal Action<FakeSession> Configure;
        internal FakeBackend()
        {
            Proxy = new SeamProxy(backendType, call =>
            {
                if (call.MethodName != "LaunchAsync") throw new NotSupportedException(call.MethodName);
                var session = new FakeSession(); Configure?.Invoke(session);
                var launch = new Launch(session, (CancellationToken)call.Args[0]); Launches.Add(launch);
                if (!HoldLaunch) launch.Complete();
                return launch.Task;
            }).GetTransparentProxy();
        }
        internal FakeSession Latest => Launches.Last().Session;
    }

    internal sealed class Speech
    {
        internal readonly long Generation;
        internal readonly string Text;
        internal readonly CancellationToken Token;
        internal readonly TaskCompletionSource<long> Ready = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Speech(long generation, string text, CancellationToken token) { Generation = generation; Text = text; Token = token; }
    }

    internal sealed class FakeSession
    {
        internal readonly object Proxy;
        internal readonly List<Speech> Speeches = new List<Speech>();
        internal Action<long, int> Progress;
        internal Action<long> Completed;
        internal Action<string> Faulted;
        internal bool HoldSpeak, HoldPause, HoldResume, HoldCancel;
        internal Action<FakeSession, Speech> BeforeSpeakReturn;
        internal int Disposals, Cancels, Pauses, Resumes, WrongThreadCommands;
        internal long PauseGeneration, ResumeGeneration;
        internal TaskCompletionSource<bool> PauseReady, ResumeReady;
        internal readonly TaskCompletionSource<bool> CancelReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal FakeSession()
        {
            Proxy = new SeamProxy(sessionType, call =>
            {
                if (call.MethodName != "Dispose" && !dispatcher.CheckAccess()) WrongThreadCommands++;
                switch (call.MethodName)
                {
                    case "add_Progress": Progress += (Action<long, int>)call.Args[0]; return null;
                    case "remove_Progress": Progress -= (Action<long, int>)call.Args[0]; return null;
                    case "add_Completed": Completed += (Action<long>)call.Args[0]; return null;
                    case "remove_Completed": Completed -= (Action<long>)call.Args[0]; return null;
                    case "add_Faulted": Faulted += (Action<string>)call.Args[0]; return null;
                    case "remove_Faulted": Faulted -= (Action<string>)call.Args[0]; return null;
                    case "SpeakAsync":
                        var speech = new Speech(Speeches.Count + 1, (string)call.Args[0], (CancellationToken)call.Args[1]);
                        Speeches.Add(speech); BeforeSpeakReturn?.Invoke(this, speech);
                        if (!HoldSpeak) speech.Ready.TrySetResult(speech.Generation);
                        return speech.Ready.Task;
                    case "PauseAsync":
                        Pauses++; PauseGeneration = (long)call.Args[0];
                        PauseReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        if (!HoldPause) PauseReady.SetResult(true); return PauseReady.Task;
                    case "ResumeAsync":
                        Resumes++; ResumeGeneration = (long)call.Args[0];
                        ResumeReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        if (!HoldResume) ResumeReady.SetResult(true); return ResumeReady.Task;
                    case "CancelAsync":
                        Cancels++; if (!HoldCancel) CancelReady.TrySetResult(true); return CancelReady.Task;
                    case "Dispose": Disposals++; return null;
                    default: throw new NotSupportedException(call.MethodName);
                }
            }).GetTransparentProxy();
        }
        internal Speech Last => Speeches.Last();
        internal void ProgressAt(int offset, long? generation = null) => Progress?.Invoke(generation ?? Last.Generation, offset);
        internal void Finish(long? generation = null) => Completed?.Invoke(generation ?? Last.Generation);
    }

    private static void ExceptionBoundary()
    {
        foreach (bool unexpected in new[] { true, false })
        foreach (string stage in new[] { "launch", "speak", "pause", "resume", "cancel" })
        {
            var backend = new FakeBackend
            {
                HoldLaunch = stage == "launch",
                Configure = s => { s.HoldSpeak = stage == "speak"; s.HoldCancel = stage == "cancel"; }
            };
            object owner = Owner(backend);
            Exception failure = unexpected ? (Exception)new NullReferenceException(stage + " programmer failure") :
                stage == "pause" || stage == "resume" ? new TimeoutException(stage + " operational failure") :
                new IOException(stage + " operational failure");
            try
            {
                Task operation = Call(owner, "PlayAsync");
                if (stage == "launch") backend.Launches[0].Fail(failure);
                else
                {
                    PumpUntil(() => backend.Latest.Speeches.Count == 1);
                    FakeSession session = backend.Latest;
                    if (stage == "speak") session.Last.Ready.SetException(failure);
                    else
                    {
                        Await(operation);
                        if (stage == "cancel") { operation = Call(owner, "StopAsync"); session.CancelReady.SetException(failure); }
                        else
                        {
                            if (stage == "resume") Await(Call(owner, "PauseAsync"));
                            session.HoldPause = true; session.HoldResume = true;
                            operation = Call(owner, stage == "pause" ? "PauseAsync" : "PlayAsync");
                            (stage == "pause" ? session.PauseReady : session.ResumeReady).SetException(failure);
                        }
                    }
                }
                PumpUntil(() => operation.IsCompleted);
                if (unexpected)
                {
                    Check(operation.IsFaulted && ReferenceEquals(operation.Exception.InnerException, failure),
                        stage + " swallowed or replaced an unexpected programmer exception.");
                    Check(Phase(owner) != "Error" && Get<string>(owner, "Error") == null,
                        stage + " converted a programmer failure into an operational Error result.");
                }
                else
                {
                    Check(operation.Status == TaskStatus.RanToCompletion, stage + " expected operational failure escaped its handler.");
                    Check(Phase(owner) == "Error" && Get<string>(owner, "Error").Contains(failure.Message),
                        stage + " expected operational failure lost its explicit Error state.");
                }
                Check(backend.Launches.Count == 1, stage + " failure automatically retried playback.");
            }
            finally { Dispose(owner); }
            Check(backend.Latest.Disposals == 1, stage + " exception boundary leaked or double-disposed its exact resource.");
        }

        var stale = new FakeBackend { HoldLaunch = true }; object current = Owner(stale);
        try
        {
            Task oldStart = Call(current, "PlayAsync"); Task stop = Call(current, "StopAsync");
            Task newStart = Call(current, "PlayAsync"); stale.Launches[1].Complete(); Await(newStart);
            var programmerFailure = new NullReferenceException("obsolete launch programmer failure");
            stale.Launches[0].Fail(programmerFailure); PumpUntil(() => oldStart.IsCompleted && stop.IsCompleted);
            Check(oldStart.IsFaulted && ReferenceEquals(oldStart.Exception.InnerException, programmerFailure) && stop.IsFaulted,
                "Stale-operation fencing swallowed an unexpected launch exception.");
            Check(Phase(current) == "Speaking" && stale.Launches[1].Session.Disposals == 0 && stale.Launches[0].Session.Disposals == 1,
                "A stale programmer failure changed or disposed the current session.");
        }
        finally { Dispose(current); }
    }

    private static void BasicAndSeeking()
    {
        var backend = new FakeBackend(); object owner = Owner(backend);
        try
        {
            Check(Phase(owner) == "Stopped" && backend.Launches.Count == 0, "Constructing a reader started speech.");
            Check(Get<Guid>(owner, "ReplyId") != Guid.Empty && Get<long>(owner, "Version") == 1, "Reply/version identity is absent.");
            Check(Segment(owner) == 0 && Offset(owner) == 0 && Get<long>(owner, "SpeechGeneration") == 0, "Initial selection is wrong.");
            Await(Seek(owner, 0, 6));
            Check(backend.Launches.Count == 0 && Offset(owner) == 6 && Phase(owner) == "Stopped", "Stopped word seeking launched speech.");
            Await(Call(owner, "PlayAsync")); FakeSession first = backend.Latest;
            Check(Phase(owner) == "Speaking" && first.Last.Text == "beta gamma. Next sentence.", "Play did not speak the selected suffix.");
            Check(Get<int>(owner, "PromptBase") == 6 && Get<long>(owner, "SpeechGeneration") == 1, "Prompt base/generation was conflated with selection.");
            first.ProgressAt(5); Settle();
            Check(Offset(owner) == 11, "Suffix progress did not add its prompt base.");
            first.ProgressAt(0); first.ProgressAt(-1); first.ProgressAt(500); first.ProgressAt(12, 99); Settle();
            Check(Offset(owner) == 11, "Invalid/stale/out-of-order progress moved selection backwards.");
            Action<long, int> oldProgress = first.Progress; Action<long> oldCompleted = first.Completed;
            Await(Seek(owner, 1, 8)); FakeSession second = backend.Latest;
            Check(first.Cancels == 1 && first.Disposals == 1, "Seeking did not retire the exact previous session.");
            Check(second != first && second.Last.Text == "First line. Next line." && Get<int>(owner, "PromptBase") == 8,
                "Playing seek did not speak the step's selected body suffix.");
            oldProgress(1, 0); oldCompleted(1); Settle();
            Check(Segment(owner) == 1 && Offset(owner) == 8 && second.Speeches.Count == 1, "Retired session events reached a new prompt.");
            Await(Call(owner, "NavigateAsync", 1, false)); FakeSession third = backend.Latest;
            Check(third.Last.Text == "Next line." && Offset(owner) == 20, "Sentence navigation lost step-prefix offsets.");
            int launches = backend.Launches.Count; Await(Call(owner, "NavigateAsync", 1, false));
            Check(backend.Launches.Count == launches, "Navigation at the end restarted speech.");
            Await(Call(owner, "StopAsync"));
            Check(Phase(owner) == "Stopped" && Offset(owner) == 20 && Get<long>(owner, "SpeechGeneration") == 0, "Stop did not retain position/invalidate prompt.");
            Check(third.Disposals == 1 && third.Progress == null && third.Completed == null && third.Faulted == null, "Stop leaked session handlers/resources.");
            Await(Call(owner, "NavigateAsync", -1, true));
            Check(Segment(owner) == 0 && Offset(owner) == 0 && backend.Launches.Count == launches, "Stopped paragraph navigation spoke.");
            Reject(() => Seek(owner, -1, 0), typeof(ArgumentOutOfRangeException), "Invalid seek was accepted");
            Reject(() => Seek(owner, 0, 500), typeof(ArgumentOutOfRangeException), "Out-of-bounds seek was accepted");
            Reject(() => Call(owner, "NavigateAsync", 0, false), typeof(ArgumentOutOfRangeException), "Invalid navigation direction was accepted");
        }
        finally { Dispose(owner); }
    }

    private static void VersionAndValidation()
    {
        var backend = new FakeBackend(); object owner = Owner(backend);
        try
        {
            Await(Seek(owner, 1, 20)); Await(Call(owner, "PlayAsync")); FakeSession first = backend.Latest;
            long version = Get<long>(owner, "Version"); Await(Call(owner, "SwitchVersionAsync", true));
            Check(Get<bool>(owner, "IsConcise") && Get<long>(owner, "Version") == version + 1, "Version lifetime did not change.");
            Check(Segment(owner) == 0 && Offset(owner) == 0 && Phase(owner) == "Paused", "Reordered variant did not select its matching section's first word paused.");
            Check(first.Disposals == 1 && backend.Launches.Count == 1 && Get<long>(owner, "SpeechGeneration") == 0, "Version switch did not cancel without replay.");
            Await(Call(owner, "PlayAsync")); Check(backend.Latest.Last.Text == "Step 2. Brief step.", "Play used the old variant or lost its spoken prefix.");
            Await(Call(owner, "NavigateAsync", 1, true)); Await(Call(owner, "SwitchVersionAsync", false));
            Check(Segment(owner) == 0 && Offset(owner) == 0 && Phase(owner) == "Paused", "Concise-to-Detailed mapped by index rather than ID.");
            Await(Call(owner, "SwitchVersionAsync", true));
            Check(Segment(owner) == 1 && Offset(owner) == 2, "Version switch selected whitespace rather than the first displayed word.");
            version = Get<long>(owner, "Version"); int launches = backend.Launches.Count;
            Await(Call(owner, "SwitchVersionAsync", true));
            Check(Get<long>(owner, "Version") == version && backend.Launches.Count == launches, "Same-version selection changed lifetime or spoke.");
            Await(Call(owner, "NavigateAsync", -1, true));
            Check(Phase(owner) == "Paused" && backend.Launches.Count == launches, "Paused navigation began speech.");
        }
        finally { Dispose(owner); }
        object detailedOnly = Owner(new FakeBackend(), Detailed, null);
        try { Reject(() => Call(detailedOnly, "SwitchVersionAsync", true), typeof(InvalidOperationException), "Missing Concise was silently fabricated"); }
        finally { Dispose(detailedOnly); }
        Reject(() => Owner(new FakeBackend(), Detailed, null, true), typeof(InvalidOperationException), "Unavailable initial variant was silently substituted");
        Reject(() => Owner(new FakeBackend(), Detailed, "[{\"id\":\"missing\",\"kind\":\"paragraph\",\"text\":\"x\"}]"), typeof(ArgumentException), "Mismatched variants were accepted");
        object conciseFirst = Owner(new FakeBackend(), initialConcise: true);
        try { Check(Get<bool>(conciseFirst, "IsConcise") && Phase(conciseFirst) == "Stopped", "Injected saved choice was ignored or auto-played."); }
        finally { Dispose(conciseFirst); }
    }

    private static void PauseResumeAndCompletion()
    {
        var backend = new FakeBackend { Configure = s => { s.HoldPause = true; s.HoldResume = true; } };
        object owner = Owner(backend);
        try
        {
            Await(Call(owner, "PlayAsync")); FakeSession session = backend.Latest;
            Task pause = Call(owner, "PauseAsync");
            Check(Phase(owner) == "Pausing" && !pause.IsCompleted && session.Pauses == 1, "Pause was claimed before backend acknowledgement.");
            Await(Call(owner, "PauseAsync")); Await(Call(owner, "PlayAsync"));
            Check(session.Pauses == 1 && session.Speeches.Count == 1, "Pending pause accepted duplicate commands or replayed.");
            Await(Task.Run(() => session.PauseReady.SetResult(true))); Await(pause);
            long generation = Get<long>(owner, "SpeechGeneration");
            Check(Phase(owner) == "Paused" && generation == session.PauseGeneration, "Backend-acknowledged pause lost the prompt.");
            int selected = Offset(owner); session.ProgressAt(11); Settle();
            Check(Offset(owner) == selected, "Progress arriving while paused advanced the selected word.");
            Task resume = Call(owner, "PlayAsync");
            Check(Phase(owner) == "Resuming" && !resume.IsCompleted && session.Resumes == 1, "Resume was claimed before acknowledgement.");
            Await(Call(owner, "PlayAsync")); Await(Call(owner, "PauseAsync"));
            Check(session.Resumes == 1, "Pending resume accepted duplicate commands.");
            Await(Task.Run(() => session.ResumeReady.SetResult(true))); Await(resume);
            Check(Phase(owner) == "Speaking" && Get<long>(owner, "SpeechGeneration") == generation && session.Speeches.Count == 1 &&
                session.ResumeGeneration == generation && backend.Launches.Count == 1, "Resume cancelled/re-spoke rather than continuing the same prompt.");
            Action<long> oldComplete = session.Completed;
            oldComplete(generation); oldComplete(generation); Settle();
            Check(Segment(owner) == 1 && session.Speeches.Count == 2 && session.Last.Text == "Step 2. First line. Next line.", "Duplicate completion advanced more than once or skipped a segment.");
            oldComplete(generation); session.ProgressAt(0, generation); Settle();
            Check(session.Speeches.Count == 2 && Segment(owner) == 1, "Previous-prompt completion/progress was accepted by the next prompt.");
            session.Finish(); session.Finish(); Settle();
            Check(Phase(owner) == "Finished" && session.Speeches.Count == 2 && Get<long>(owner, "SpeechGeneration") == 0, "Final completion was not terminal/exactly once.");
            Await(Call(owner, "PlayAsync"));
            Check(Segment(owner) == 0 && Offset(owner) == 0 && session.Speeches.Count == 3 && session.Last.Text.StartsWith("Alpha"), "Finished Play did not restart the reply.");
            Task racingPause = Call(owner, "PauseAsync"); Action<long> completing = session.Completed;
            completing(session.Last.Generation); Settle();
            Check(Phase(owner) == "Paused" && Segment(owner) == 1 && backend.Launches.Count == 1, "Completion during pause automatically spoke the next segment.");
            Await(Task.Run(() => session.PauseReady.SetResult(true))); Await(racingPause);
            Check(Phase(owner) == "Paused" && session.Disposals == 1, "Late pause acknowledgement attached to another segment or leaked its session.");
            Await(Call(owner, "PlayAsync")); FakeSession next = backend.Latest;
            Check(next != session && next.Last.Text.StartsWith("Step 2."), "Play after pause/completion did not deliberately start the selected next segment.");
            next.HoldPause = false;
            Await(Call(owner, "PauseAsync"));
        }
        finally { Dispose(owner); }
    }

    private static void PendingStateLifetimeRaces()
    {
        foreach (bool resume in new[] { false, true })
        foreach (string action in new[] { "StopAsync", "HideAsync", "Dispose", "SeekAsync", "SwitchVersionAsync" })
        {
            var backend = new FakeBackend(); object owner = Owner(backend);
            try
            {
                Await(Call(owner, "PlayAsync")); FakeSession old = backend.Latest;
                if (resume) Await(Call(owner, "PauseAsync"));
                old.HoldPause = true; old.HoldResume = true;
                Task change = Call(owner, resume ? "PlayAsync" : "PauseAsync");
                TaskCompletionSource<bool> acknowledgement = resume ? old.ResumeReady : old.PauseReady;
                Action<long> oldComplete = old.Completed;
                Task stop = Task.CompletedTask;
                if (action == "Dispose") Dispose(owner);
                else if (action == "SeekAsync") stop = Seek(owner, 1, 8);
                else if (action == "SwitchVersionAsync") stop = Call(owner, action, true);
                else stop = Call(owner, action);
                Await(stop);
                acknowledgement.SetResult(true); Await(change); oldComplete(1); Settle();
                string expected = action == "Dispose" ? "Disposed" : action == "SwitchVersionAsync" || (!resume && action == "SeekAsync") ? "Paused" : "Stopped";
                Check(Phase(owner) == expected && Get<long>(owner, "SpeechGeneration") == 0,
                    "Late " + (resume ? "resume" : "pause") + " acknowledgement revived " + action + ".");
                Check(old.Disposals == 1 && backend.Launches.Count == 1, "Pending state teardown leaked/replayed a session.");
            }
            finally { Dispose(owner); }
        }

        var completing = new FakeBackend(); object completingOwner = Owner(completing);
        try
        {
            Await(Call(completingOwner, "PlayAsync")); FakeSession old = completing.Latest;
            Await(Call(completingOwner, "PauseAsync")); old.HoldResume = true;
            Task resume = Call(completingOwner, "PlayAsync"); old.Finish(); Settle();
            Check(Phase(completingOwner) == "Speaking" && Segment(completingOwner) == 1 && old.Disposals == 1 && completing.Launches.Count == 2,
                "Completion during resume failed to continue exactly once with a fresh usable session.");
            old.ResumeReady.SetException(new OperationCanceledException("completed")); Await(resume);
            Check(Phase(completingOwner) == "Speaking" && completing.Latest.Speeches.Count == 1, "Late resume completion/error poisoned the next segment.");
        }
        finally { Dispose(completingOwner); }
    }

    private static void CompletionStateOrdering()
    {
        foreach (bool resume in new[] { false, true })
        foreach (bool completionFirst in new[] { false, true })
        foreach (bool finalSegment in new[] { false, true })
        {
            var backend = new FakeBackend(); object owner = Owner(backend);
            try
            {
                if (finalSegment) Await(Seek(owner, 1, 0));
                Await(Call(owner, "PlayAsync")); Settle(); FakeSession old = backend.Latest;
                if (resume) Await(Call(owner, "PauseAsync"));
                old.HoldPause = true; old.HoldResume = true;
                Task change = Call(owner, resume ? "PlayAsync" : "PauseAsync");
                TaskCompletionSource<bool> acknowledgement = resume ? old.ResumeReady : old.PauseReady;
                Action<long> completion = old.Completed;
                if (completionFirst)
                {
                    completion(old.Last.Generation); Settle();
                    acknowledgement.SetResult(false); Await(change);
                }
                else
                {
                    acknowledgement.SetResult(false); Await(change);
                    Check(Phase(owner) == (resume ? "Resuming" : "Pausing") && Get<string>(owner, "Error") == null,
                        "A completed-prompt outcome falsely acknowledged state or faulted before its completion callback.");
                    completion(old.Last.Generation); Settle();
                }
                string expected = finalSegment ? "Finished" : resume ? "Speaking" : "Paused";
                PumpUntil(() => Phase(owner) == expected);
                Check(Segment(owner) == 1 && Get<string>(owner, "Error") == null,
                    "Completion/state outcome ordering lost the section or produced an error.");
                Check(old.Disposals == 1 && old.Cancels == 1 && backend.Launches.Count == (!finalSegment && resume ? 2 : 1),
                    "Completion/state outcome ordering leaked, replayed or double-retired a session.");
                completion(old.Last.Generation); Settle();
                Check(Phase(owner) == expected && Segment(owner) == 1 && (!resume || finalSegment || backend.Latest.Speeches.Count == 1),
                    "Duplicate completion after a terminal state outcome advanced or restarted playback.");
            }
            finally { Dispose(owner); }
        }
        foreach (bool resume in new[] { false, true })
        {
            var backend = new FakeBackend(); object owner = Owner(backend);
            try
            {
                Await(Call(owner, "PlayAsync"));
                if (resume) Await(Call(owner, "PauseAsync"));
                FakeSession session = backend.Latest; session.HoldPause = true; session.HoldResume = true;
                Task change = Call(owner, resume ? "PlayAsync" : "PauseAsync");
                (resume ? session.ResumeReady : session.PauseReady).SetCanceled(); Await(change);
                Check(Phase(owner) == "Error" && Get<string>(owner, "Error") != null && session.Disposals == 1,
                    "A genuine cancelled state acknowledgement was mistaken for normal completion.");
            }
            finally { Dispose(owner); }
        }
    }

    private static void EarlyEventsAndBounds()
    {
        var synchronous = new FakeBackend
        {
            Configure = s => s.BeforeSpeakReturn = (session, speech) =>
            {
                session.ProgressAt(0); session.Finish(); session.Finish();
            }
        };
        object immediate = Owner(synchronous);
        try
        {
            Await(Call(immediate, "PlayAsync")); Settle();
            Check(Phase(immediate) == "Finished" && synchronous.Latest.Speeches.Count == 2,
                "Synchronous progress/completion inside Speak submission was lost or repeated.");
        }
        finally { Dispose(immediate); }
        var backend = new FakeBackend { Configure = s => s.HoldSpeak = true }; object owner = Owner(backend);
        try
        {
            Task play = Call(owner, "PlayAsync"); PumpUntil(() => backend.Latest.Speeches.Count == 1); FakeSession session = backend.Latest;
            Check(Phase(owner) == "Starting" && !play.IsCompleted && Get<long>(owner, "SpeechGeneration") == 0, "Pending Speak returned a fabricated active generation.");
            Action<long> earlyCompletion = session.Completed;
            Await(Task.Run(() =>
            {
                session.ProgressAt(6); session.ProgressAt(11); session.ProgressAt(0);
                session.Finish(99); earlyCompletion(1); earlyCompletion(1);
            }));
            Check(Segment(owner) == 0 && Offset(owner) == 0, "Early unknown-generation events mutated the model.");
            session.HoldSpeak = false; session.Last.Ready.SetResult(1); Await(play); Settle();
            Check(Segment(owner) == 1 && session.Speeches.Count == 2, "Completion before Speak return was lost or processed twice.");
            earlyCompletion(1); Settle(); Check(session.Speeches.Count == 2, "Stale early completion revived after generation handoff.");
            session.Finish(); Settle(); Check(Phase(owner) == "Finished", "Sequential early completion lost the final segment.");
        }
        finally { Dispose(owner); }

        var progressBackend = new FakeBackend { Configure = s => s.HoldSpeak = true }; owner = Owner(progressBackend);
        try
        {
            Task play = Call(owner, "PlayAsync"); PumpUntil(() => progressBackend.Latest.Speeches.Count == 1); FakeSession session = progressBackend.Latest;
            Await(Task.Run(() => { for (int i = 0; i < 10000; i++) session.ProgressAt(i % 12); }));
            session.Last.Ready.SetResult(1); Await(play); Settle();
            Check(Offset(owner) == 11 && Phase(owner) == "Speaking", "Coalesced early progress was lost or regressed.");
            session.ProgressAt(6); Settle(); Check(Offset(owner) == 11, "Later out-of-order progress regressed the selected word.");
            Await(Task.Run(() => { for (int i = 0; i < 10000; i++) session.ProgressAt(18); })); Settle();
            Check(Offset(owner) == 18, "Live progress flood did not coalesce to the expected word.");
            object active = controllerType.GetField("active", Members).GetValue(owner);
            Check(active.GetType().GetField("pending", Members).GetValue(active) is IDictionary pending && pending.Count <= 1,
                "Acknowledged prompt retained an unbounded event queue.");
        }
        finally { Dispose(owner); }

        var overflow = new FakeBackend { Configure = s => s.HoldSpeak = true }; owner = Owner(overflow);
        try
        {
            Task play = Call(owner, "PlayAsync"); PumpUntil(() => overflow.Latest.Speeches.Count == 1); FakeSession session = overflow.Latest;
            for (long generation = 1; generation <= 17; generation++) session.ProgressAt(0, generation);
            object active = controllerType.GetField("active", Members).GetValue(owner);
            Check(((IDictionary)active.GetType().GetField("pending", Members).GetValue(active)).Count == 16, "Early event capacity is unbounded.");
            session.Last.Ready.SetResult(1); Await(play); Settle();
            Check(Phase(owner) == "Error" && Get<string>(owner, "Error").Contains("capacity") && session.Disposals == 1,
                "Early generation overflow was silently dropped or auto-retried.");
        }
        finally { Dispose(owner); }
    }

    private static void StartupAndLifetimeRaces()
    {
        foreach (string action in new[] { "StopAsync", "HideAsync", "Dispose", "SwitchVersionAsync", "SeekAsync" })
        {
            var backend = new FakeBackend { HoldLaunch = true }; object owner = Owner(backend);
            try
            {
                Task start = Call(owner, "PlayAsync"); Launch obsolete = backend.Launches[0];
                Task stopped = Task.CompletedTask;
                if (action == "Dispose") Dispose(owner);
                else if (action == "SwitchVersionAsync") stopped = Call(owner, action, true);
                else if (action == "SeekAsync") stopped = Seek(owner, 1, 8);
                else stopped = Call(owner, action);
                Check(obsolete.Token.IsCancellationRequested, action + " did not cancel pending launch.");
                obsolete.Complete(); Await(start); Await(stopped); Settle();
                Check(obsolete.Session.Disposals == 1 && obsolete.Session.Speeches.Count == 0 && obsolete.Session.Progress == null,
                    action + " attached/spoke/leaked an ignored-cancellation launch.");
                Check(Phase(owner) == (action == "Dispose" ? "Disposed" : action == "SwitchVersionAsync" ? "Paused" : "Stopped"),
                    action + " allowed a late launch to change state.");
            }
            finally { Dispose(owner); }
        }

        var delayed = new FakeBackend { HoldLaunch = true }; object replacementOwner = Owner(delayed);
        try
        {
            Task oldStart = Call(replacementOwner, "PlayAsync"); Launch old = delayed.Launches[0];
            Task stop = Call(replacementOwner, "StopAsync"); Task newStart = Call(replacementOwner, "PlayAsync"); Launch current = delayed.Launches[1];
            current.Complete(); Await(newStart);
            Check(Phase(replacementOwner) == "Speaking" && current.Session.Disposals == 0, "New session did not start independently of obsolete launch.");
            old.Complete(); Await(oldStart); Await(stop); Settle();
            Check(old.Session.Disposals == 1 && current.Session.Disposals == 0 && current.Session.Speeches.Count == 1,
                "An obsolete launch disposed or attached the newer session.");
        }
        finally { Dispose(replacementOwner); }

        foreach (string action in new[] { "StopAsync", "HideAsync", "Dispose", "SwitchVersionAsync" })
        {
            var backend = new FakeBackend { Configure = s => s.HoldSpeak = true }; object owner = Owner(backend);
            try
            {
                Task start = Call(owner, "PlayAsync"); PumpUntil(() => backend.Latest.Speeches.Count == 1); FakeSession old = backend.Latest;
                Action<long, int> progress = old.Progress; Action<long> complete = old.Completed; Action<string> fault = old.Faulted;
                progress(1, 11); complete(1);
                Task stop = Task.CompletedTask;
                if (action == "Dispose") Dispose(owner);
                else stop = action == "SwitchVersionAsync" ? Call(owner, action, true) : Call(owner, action);
                Check(old.Last.Token.IsCancellationRequested, action + " did not cancel a pending speech write.");
                old.Last.Ready.SetResult(1); Await(start); Await(stop);
                progress(1, 18); complete(1); fault("obsolete"); Settle();
                Check(old.Disposals == 1 && backend.Launches.Count == 1 && Get<long>(owner, "SpeechGeneration") == 0,
                    action + " accepted callbacks from an ignored-cancellation speech write.");
                Check(Phase(owner) == (action == "Dispose" ? "Disposed" : action == "SwitchVersionAsync" ? "Paused" : "Stopped"),
                    action + " was revived by a queued progress/completion/error.");
            }
            finally { Dispose(owner); }
        }

        var cleanup = new FakeBackend { Configure = s => s.HoldCancel = true }; object cleanupOwner = Owner(cleanup);
        try
        {
            Await(Call(cleanupOwner, "PlayAsync")); FakeSession retired = cleanup.Latest;
            Task stop = Call(cleanupOwner, "StopAsync");
            Check(Phase(cleanupOwner) == "Stopped" && !stop.IsCompleted && retired.Disposals == 0, "Stop synchronously waited or disposed before cancellation finished.");
            Task replacement = Call(cleanupOwner, "PlayAsync");
            Check(Phase(cleanupOwner) == "Starting" && !replacement.IsCompleted && cleanup.Launches.Count == 1,
                "A replacement launched before the owner's previous session retired.");
            retired.CancelReady.SetException(new IOException("late cancellation")); Await(stop); Await(replacement);
            FakeSession current = cleanup.Latest;
            Check(retired.Disposals == 1 && current.Disposals == 0 && Phase(cleanupOwner) == "Speaking" && Get<string>(cleanupOwner, "Error") == null,
                "Retired cancellation/error disposed or faulted the newer session.");
            current.CancelReady.SetResult(true);
        }
        finally { Dispose(cleanupOwner); }

        var stoppedCleanup = new FakeBackend { Configure = s => s.HoldCancel = true }; object stoppedOwner = Owner(stoppedCleanup);
        try
        {
            Await(Call(stoppedOwner, "PlayAsync")); FakeSession old = stoppedCleanup.Latest;
            Task stop = Call(stoppedOwner, "StopAsync"); old.CancelReady.SetException(new IOException("cancellation failed")); Await(stop);
            Check(Phase(stoppedOwner) == "Error" && Get<string>(stoppedOwner, "Error").Contains("cancellation failed") && old.Disposals == 1,
                "Current cancellation failure was hidden or leaked its exact session.");
            Check(stoppedCleanup.Launches.Count == 1, "Cancellation error automatically retried playback.");
        }
        finally { Dispose(stoppedOwner); }
    }

    private static void ErrorsAndIndependentOwners()
    {
        var backend = new FakeBackend { HoldLaunch = true }; object owner = Owner(backend);
        try
        {
            Task start = Call(owner, "PlayAsync"); backend.Launches[0].Fail(new IOException("launch failed")); Await(start);
            Check(Phase(owner) == "Error" && Get<string>(owner, "Error").Contains("launch failed") && backend.Launches.Count == 1,
                "Launch failure was hidden or automatically retried.");
            Settle(); Check(backend.Launches.Count == 1, "Failure silently retried startup.");
            Check(backend.Launches[0].Session.Disposals == 1, "Failed launch factory leaked its unreturned resource.");
            backend.HoldLaunch = false; Await(Call(owner, "PlayAsync")); FakeSession session = backend.Latest;
            Check(Phase(owner) == "Speaking" && Get<string>(owner, "Error") == null, "Explicit Play did not retry an error.");
            Action<string> oldFault = session.Faulted; oldFault("worker disconnected"); Settle();
            Check(Phase(owner) == "Error" && Get<string>(owner, "Error").Contains("worker disconnected") && session.Disposals == 1,
                "Worker failure did not report an error and retire its exact resource.");
            Await(Call(owner, "PlayAsync")); oldFault("late failure"); Settle();
            Check(Phase(owner) == "Speaking" && Get<string>(owner, "Error") == null, "A retired session error poisoned deliberate retry.");
            FakeSession current = backend.Latest; current.HoldPause = true;
            Task pause = Call(owner, "PauseAsync"); current.PauseReady.SetException(new TimeoutException("missing acknowledgement")); Await(pause);
            Check(Phase(owner) == "Error" && Get<string>(owner, "Error").Contains("missing acknowledgement") && current.Disposals == 1,
                "Failed pause was reported as paused or silently replayed.");
        }
        finally { Dispose(owner); }

        foreach (bool pauseFailure in new[] { false, true })
        {
            var failing = new FakeBackend { Configure = s => { s.HoldSpeak = !pauseFailure; s.HoldResume = pauseFailure; } };
            owner = Owner(failing);
            try
            {
                Task play = Call(owner, "PlayAsync"); PumpUntil(() => failing.Latest.Speeches.Count == 1); FakeSession session = failing.Latest;
                if (!pauseFailure) { session.Last.Ready.SetException(new IOException("write failed")); Await(play); }
                else { Await(play); Await(Call(owner, "PauseAsync")); Task resume = Call(owner, "PlayAsync"); session.ResumeReady.SetException(new TimeoutException("resume failed")); Await(resume); }
                Check(Phase(owner) == "Error" && session.Disposals == 1 && failing.Launches.Count == 1, "Write/resume failure was hidden, leaked or automatically retried.");
            }
            finally { Dispose(owner); }
        }

        var shared = new FakeBackend(); object a = Owner(shared), b = Owner(shared);
        try
        {
            Check(Get<Guid>(a, "ReplyId") != Get<Guid>(b, "ReplyId"), "Independent owners shared reply identity.");
            Await(Call(a, "PlayAsync")); FakeSession first = shared.Latest;
            Await(Seek(b, 1, 8)); Await(Call(b, "PlayAsync")); FakeSession second = shared.Latest;
            Check(first != second && shared.Launches.Count == 2, "Independent owners shared a speech session.");
            first.ProgressAt(11); second.ProgressAt(12); Settle();
            Check(Segment(a) == 0 && Offset(a) == 11 && Segment(b) == 1 && Offset(b) == 20, "Independent owners mixed progress/base offsets.");
            Await(Call(a, "SwitchVersionAsync", true));
            Check(Phase(a) == "Paused" && Phase(b) == "Speaking" && !Get<bool>(b, "IsConcise") && second.Disposals == 0,
                "Version/cancellation crossed owner boundaries.");
            Dispose(a); Dispose(a);
            Check(Phase(b) == "Speaking" && second.Disposals == 0, "Disposing one owner disposed the other.");
            Reject(() => Call(a, "PlayAsync"), typeof(ObjectDisposedException), "Disposed owner allowed Play");
            Reject(() => Call(a, "StopAsync"), typeof(ObjectDisposedException), "Disposed owner allowed Stop");
            second.Finish(); Settle(); Check(Phase(b) == "Finished", "Independent owner stopped receiving its own completion.");
        }
        finally { Dispose(a); Dispose(b); }
    }

    private static void DispatcherAffinity()
    {
        var backend = new FakeBackend { HoldLaunch = true }; object owner = Owner(backend);
        try
        {
            Check(SynchronizationContext.Current == null, "Fixture unexpectedly relied on a synchronization context.");
            Task start = Call(owner, "PlayAsync"); Await(Task.Run(() => backend.Launches[0].Complete())); Await(start);
            FakeSession session = backend.Latest;
            Check(session.WrongThreadCommands == 0, "Launch continuation attached/spoke outside the owning dispatcher.");
            Await(Task.Run(() => session.ProgressAt(11))); Settle();
            Check(Offset(owner) == 11, "Background progress failed to marshal to the owning dispatcher.");
            Await(Task.Run(() =>
            {
                Reject(() => Get<object>(owner, "Position"), typeof(InvalidOperationException), "Off-dispatcher model read was accepted");
                Reject(() => Call(owner, "PlayAsync"), typeof(InvalidOperationException), "Off-dispatcher Play was accepted");
                Reject(() => Dispose(owner), typeof(InvalidOperationException), "Off-dispatcher disposal was accepted");
            }));
            session.HoldPause = true; Task pause = Call(owner, "PauseAsync");
            Await(Task.Run(() => session.PauseReady.SetResult(true))); Await(pause);
            session.HoldResume = true; Task resume = Call(owner, "PlayAsync");
            Await(Task.Run(() => session.ResumeReady.SetResult(true))); Await(resume);
            Await(Task.Run(() => session.Finish())); Settle();
            Check(session.Speeches.Count == 2 && session.WrongThreadCommands == 0, "State/completion continuations accessed model or backend off-dispatcher.");
            Action<long> queued = session.Completed; queued(session.Last.Generation); Await(Call(owner, "HideAsync")); Settle();
            Check(Phase(owner) == "Stopped" && Segment(owner) == 1 && session.Disposals == 1, "Hide did not invalidate a queued acknowledged completion.");
        }
        finally { Dispose(owner); }
    }

    private static int WorkerPid(object owner)
    {
        object session = controllerType.GetField("session", Members).GetValue(owner);
        object host = session.GetType().GetField("host", Members).GetValue(session);
        return (int)host.GetType().GetProperty("WorkerProcessId", Members).GetValue(host);
    }

    private static bool Exited(int pid)
    {
        try { using (Process process = Process.GetProcessById(pid)) return process.HasExited; }
        catch (ArgumentException) { return true; }
    }

    private static void RealAdapter()
    {
        Type realBackend = assembly.GetType("VsCopilotChatVoice.VisualStudio.ReaderSpeechBackend", true);
        string executable = Path.Combine(AppContext.BaseDirectory, "VsCopilotChatVoice.Reader.exe");
        object backend = Activator.CreateInstance(realBackend, Members, null, new object[] { executable, true }, null);
        string longReply = "[{\"id\":\"real\",\"kind\":\"paragraph\",\"text\":\"" +
            string.Join(" ", Enumerable.Repeat("Alpha beta gamma delta epsilon.", 100)) + "\"}]";
        object a = Owner(backend, longReply, null), b = Owner(backend, longReply, null);
        int firstPid = 0, secondPid = 0;
        try
        {
            Await(Call(a, "PlayAsync")); firstPid = WorkerPid(a);
            Await(Call(b, "PlayAsync")); secondPid = WorkerPid(b);
            Check(firstPid != secondPid && !Exited(firstPid) && !Exited(secondPid), "Actual adapter did not own two independent external workers.");
            PumpUntil(() => Offset(a) > 0 && Offset(b) > 0, 15000);
            long generation = Get<long>(a, "SpeechGeneration");
            Await(Call(a, "PauseAsync"));
            Check(Phase(a) == "Paused" && Get<long>(a, "SpeechGeneration") == generation, "Real backend did not acknowledge same-prompt pause.");
            int paused = Offset(a); Settle();
            Check(Offset(a) == paused && Phase(b) == "Speaking", "Real paused owner advanced or paused the independent worker.");
            Await(Call(a, "PlayAsync"));
            Check(Get<long>(a, "SpeechGeneration") == generation && WorkerPid(a) == firstPid, "Real resume launched/re-spoke a replacement prompt.");
            PumpUntil(() => Offset(a) > paused, 10000);
            Await(Call(a, "HideAsync")); PumpUntil(() => Exited(firstPid), 7000);
            Check(Phase(a) == "Stopped" && Offset(a) >= paused && !Exited(secondPid), "Real hide did not retain selection/clean only its worker.");
            Await(Call(b, "StopAsync")); PumpUntil(() => Exited(secondPid), 7000);
            Check(Phase(b) == "Stopped", "Real Stop failed.");
            Await(Seek(a, 0, 6)); Await(Call(a, "PlayAsync")); firstPid = WorkerPid(a);
            Check(Get<int>(a, "PromptBase") == 6, "Real adapter did not submit a selected suffix.");
            PumpUntil(() => Offset(a) > 6, 10000);
            Check(Offset(a) >= 11, "Real suffix progress did not map back into the full section.");
            Await(Call(a, "StopAsync")); PumpUntil(() => Exited(firstPid), 7000);
        }
        finally
        {
            Dispose(a); Dispose(b);
            if (firstPid != 0) PumpUntil(() => Exited(firstPid), 7000);
            if (secondPid != 0) PumpUntil(() => Exited(secondPid), 7000);
        }
    }
}
