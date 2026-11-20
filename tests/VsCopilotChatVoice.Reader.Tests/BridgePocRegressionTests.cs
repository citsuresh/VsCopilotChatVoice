using System;
using System.Collections;
using System.Collections.Concurrent;
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

internal static class BridgePocRegressionTests
{
    private static int assertions;

    internal static int Run(string assemblyPath)
    {
        Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        Type type = assembly.GetType("VsCopilotChatVoice.VisualStudio.ChatBridgePoc", true);
        MethodInfo enabled = type.GetMethod("IsEnabled", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo inspect = type.GetMethod("Inspect", BindingFlags.Static | BindingFlags.NonPublic);
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
                Check(ValidReply(name).Contains("\"Visited\":1"), "Baseline diagnostic request failed.");
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
