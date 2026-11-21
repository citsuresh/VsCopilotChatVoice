using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;

internal static class DraftPocRegressionTests
{
    private static int assertions;

    internal static int Run(Assembly assembly)
    {
        Type core = assembly.GetType("VsCopilotChatVoice.VisualStudio.ChatDraftPoc", true);
        Type requestType = core.GetNestedType("InsertRequest", BindingFlags.NonPublic);
        MethodInfo reject = core.GetMethod("RejectInsert", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo binding = core.GetMethod("HasConstructorOnlyBinding", BindingFlags.Static | BindingFlags.NonPublic);
        var errors = new ConcurrentQueue<Exception>();
        Func<int, object> newCore = capacity => Activator.CreateInstance(core, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { new Action<Exception>(errors.Enqueue), capacity }, null);
        Func<object> newRequest = () =>
        {
            object request = Activator.CreateInstance(requestType, true);
            Set(request, "Version", 1); Set(request, "RequestId", Guid.NewGuid());
            Set(request, "SessionId", Guid.NewGuid()); Set(request, "ClientId", "test-client");
            Set(request, "Generation", Guid.NewGuid().ToString("N")); Set(request, "DraftRevision", 0L);
            Set(request, "Transcript", "POC_MUST_NOT_INSERT");
            return request;
        };
        Func<object, object, object> invoke = (service, request) =>
            reject.Invoke(service, new[] { request, (object)CancellationToken.None });
        Check((bool)binding.Invoke(null, new object[] { typeof(ImmutableInput).GetProperty("Input") }), "Immutable slot was not recognized.");
        Check(!(bool)binding.Invoke(null, new object[] { typeof(MutableInput).GetProperty("Input") }), "Mutable slot was accepted.");
        Check(!(bool)binding.Invoke(null, new object[] { typeof(ComputedInput).GetProperty("Input") }), "Computed slot was accepted.");
        Check(!(bool)binding.Invoke(null, new object[] { typeof(VirtualInput).GetProperty("Input") }), "Virtual slot was accepted.");
        Check(core.GetNestedType("DraftTarget", BindingFlags.NonPublic) == null &&
            core.GetMethod("Insert", BindingFlags.Instance | BindingFlags.NonPublic) == null,
            "The unfenced snapshot/read/write API remains reachable.");
        Type bridge = assembly.GetType("VsCopilotChatVoice.VisualStudio.ChatBridgePoc", true);
        Check(bridge.GetMethod("RequirePromptProperty", BindingFlags.NonPublic | BindingFlags.Static) == null &&
            bridge.GetMethod("InsertDraftOnUiThread", BindingFlags.NonPublic | BindingFlags.Instance) == null,
            "The native prompt getter/setter insertion path remains reachable.");
        object service = newCore(128);
        object request = newRequest();
        object result = invoke(service, request);
        Check(Text(result, "Status") == "Rejected" && Text(result, "Reason") == "NativeAtomicInsertionUnsupported",
            "Unproved native concurrency did not fail closed.");
        Check(!(bool)Get(result, "MutationAttempted"), "Capability refusal reported mutation.");
        object replay = invoke(service, request);
        Check((bool)Get(replay, "Replayed") && Text(replay, "Reason") == "NativeAtomicInsertionUnsupported",
            "Refusal reconciliation changed the outcome.");
        Set(replay, "Status", "caller mutation");
        Check(Text(invoke(service, request), "Status") == "Rejected", "Caller modified the cached outcome.");
        Set(request, "Transcript", "different text");
        Check(Text(invoke(service, request), "Reason") == "RequestIdConflict", "Changed request reused its identity.");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            result = reject.Invoke(newCore(128), new[] { newRequest(), (object)cancellation.Token });
            Check(Text(result, "Reason") == "Cancelled" && !(bool)Get(result, "MutationAttempted"), "Cancelled request did not fail closed.");
        }
        Check(errors.TryDequeue(out Exception error) && error is OperationCanceledException && errors.IsEmpty,
            "Cancellation was not explicitly reported.");
        Action<object>[] invalid =
        {
            value => Set(value, "Version", 2), value => Set(value, "RequestId", Guid.Empty),
            value => Set(value, "SessionId", Guid.Empty), value => Set(value, "ClientId", ""),
            value => Set(value, "Generation", "invalid"), value => Set(value, "DraftRevision", -1L),
            value => Set(value, "DraftRevision", long.MaxValue), value => Set(value, "Transcript", " "),
            value => Set(value, "Transcript", new string('x', 4097)), value => Set(value, "Transcript", "bad\0text")
        };
        foreach (Action<object> invalidate in invalid)
        {
            object value = newRequest();
            invalidate(value);
            try { invoke(newCore(128), value); throw new InvalidOperationException("Invalid request was accepted."); }
            catch (TargetInvocationException failure) { Check(failure.InnerException is InvalidDataException, "Wrong validation failure."); }
        }
        object limited = newCore(1);
        object original = newRequest();
        invoke(limited, original);
        Check(Text(invoke(limited, newRequest()), "Reason") == "RequestCapacityReached", "Ledger silently evicted a request.");
        Check((bool)Get(invoke(limited, original), "Replayed"), "Capacity prevented original-request reconciliation.");
        return assertions;
    }

    private static object Get(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
    private static string Text(object value, string name) => (string)Get(value, name);
    private static void Set(object value, string name, object data) => value.GetType().GetProperty(name).SetValue(value, data);
    private static void Check(bool success, string message) { assertions++; if (!success) throw new InvalidOperationException(message); }
    public sealed class ImmutableInput { public object Input { get; } = new object(); }
    public sealed class MutableInput { public object Input { get; set; } = new object(); }
    public sealed class ComputedInput { public object Input => new object(); }
    public class VirtualInput { public virtual object Input { get; } = new object(); }
}
