#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using ReadAlongTrial;
using VsCopilotChatVoice.History;

internal static class ReplyHistoryRegressionTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static int assertions;

    private static void Check(bool condition, string message)
    {
        Interlocked.Increment(ref assertions);
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Refused<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { Interlocked.Increment(ref assertions); return; }
        throw new InvalidOperationException(message);
    }

    private static object? Invoke(MethodBase method, object? target, params object?[] args)
    {
        try { return method is ConstructorInfo constructor ? constructor.Invoke(args) : ((MethodInfo)method).Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name, Members)!.GetValue(target)!;
    private static void Set(object target, string name, object? value) => target.GetType().GetProperty(name, Members)!.SetValue(target, value);
    private static IList Items(object target, string name) => Get<IList>(target, name);
    private static string State(object snapshot)
    {
        var fields = new List<string> { Get<int>(snapshot, "SelectedIndex").ToString(), Get<bool>(snapshot, "ManualBrowsing").ToString(),
            Get<bool>(snapshot, "HasNewReplies").ToString(), Get<int>(snapshot, "RetainedTextUnits").ToString() };
        foreach (object reply in Items(snapshot, "Replies"))
        {
            fields.Add(Get<string>(reply, "ReplyId")); fields.Add(Get<string?>(reply, "Metadata") ?? "<null>");
            foreach (string variant in new[] { "Detailed", "Concise" })
            {
                IList? blocks = Get<IList?>(reply, variant);
                fields.Add(blocks == null ? "<absent>" : blocks.Count.ToString());
                if (blocks != null) foreach (object block in blocks)
                    fields.Add(Get<string>(block, "Id") + ":" + Get<string>(block, "Kind") + ":" + Get<int>(block, "Number") + ":" + Get<string>(block, "Text"));
            }
        }
        return string.Join("|", fields);
    }

    private sealed class Context
    {
        internal readonly Assembly Assembly;
        internal readonly Type IdentityType, CatalogType, InputType, BlockType;
        internal readonly Guid Instance = Guid.NewGuid(), Session = Guid.NewGuid();
        internal readonly object Origin;
        internal Context(string? dll)
        {
            Assembly = dll == null ? typeof(CapturedReplyCatalog).Assembly : Assembly.LoadFrom(System.IO.Path.GetFullPath(dll));
            IdentityType = Type("CapturedConversationIdentity"); CatalogType = Type("CapturedReplyCatalog"); InputType = Type("CapturedReplyInput");
            BlockType = dll == null ? typeof(ReplyBlock) : Assembly.GetType("ReadAlongTrial.ReplyBlock", true)!;
            Origin = Identity(Instance, Session, "client-A");
        }
        internal Type Type(string name) => Assembly.GetType("VsCopilotChatVoice.History." + name, true)!;
        internal object Identity(Guid instance, Guid session, string? client) =>
            Invoke(IdentityType.GetConstructors(Members).Single(), null, instance, session, client)!;
        internal object Catalog(object? origin = null) => Invoke(CatalogType.GetConstructors(Members).Single(), null, origin ?? Origin)!;
        internal object Block(string? text = "Done", string? id = "a", string? kind = "paragraph", int number = 0)
        {
            object block = Activator.CreateInstance(BlockType)!;
            foreach (var pair in new[] { ("Text", (object?)text), ("Id", (object?)id), ("Kind", (object?)kind), ("Number", (object?)number) })
                BlockType.GetField(pair.Item1)!.SetValue(block, pair.Item2);
            return block;
        }
        internal Array Blocks(params object?[] blocks)
        {
            Array array = Array.CreateInstance(BlockType, blocks.Length);
            for (int i = 0; i < blocks.Length; i++) array.SetValue(blocks[i], i);
            return array;
        }
        internal Array Variant(int count, int units = 1) => Blocks(Enumerable.Range(0, count).Select(i => Block(new string('x', units), "a" + i)).ToArray());
        internal Array Text(int units)
        {
            var blocks = new List<object>();
            while (units > 0) { int size = Math.Min(100000, units); blocks.Add(Block(new string('x', size), "a" + blocks.Count)); units -= size; }
            return Blocks(blocks.ToArray());
        }
        internal object Input(string? id = "r", Array? detailed = null, Array? concise = null, string? metadata = null, object? origin = null)
        {
            object input = Activator.CreateInstance(InputType)!;
            Set(input, "Origin", origin ?? Origin); Set(input, "ReplyId", id); Set(input, "Completed", true);
            Set(input, "Detailed", detailed ?? Blocks(Block())); Set(input, "Concise", concise); Set(input, "Metadata", metadata);
            return input;
        }
        internal object Call(object target, string method, params object?[] args) => Invoke(target.GetType().GetMethod(method, Members)!, target, args)!;
        internal string Capture(object catalog, object? input) => Call(catalog, "Capture", input).ToString()!;
        internal object Snapshot(object catalog) => Call(catalog, "Snapshot");
        internal void Dispose(object catalog) => ((IDisposable)catalog).Dispose();
        internal void Rejected(object catalog, object input, string message)
        {
            string before = State(Snapshot(catalog));
            Refused<ArgumentException>(() => Capture(catalog, input), message);
            Check(State(Snapshot(catalog)) == before, "Rejected input partially mutated the catalog: " + message);
        }
    }

    private static void IdentityAndStructure(Context c)
    {
        foreach (var pair in new[] { (Guid.Empty, c.Session), (c.Instance, Guid.Empty) })
            Refused<ArgumentException>(() => c.Identity(pair.Item1, pair.Item2, "client-A"), "Empty identity GUID accepted.");
        foreach (string? invalid in new[] { null, "", " ", "client\nA", new string('c', 257) })
            Refused<ArgumentException>(() => c.Identity(c.Instance, c.Session, invalid), "Invalid client ID accepted.");
        Check(Get<string>(c.Identity(c.Instance, c.Session, new string('c', 256)), "ClientId").Length == 256, "Exact client ID bound rejected.");
        Check(Get<string>(c.Identity(c.Instance, c.Session, " client-A "), "ClientId") == " client-A ", "Existing ordinal client ID semantics changed.");
        Refused<ArgumentNullException>(() => Invoke(c.CatalogType.GetConstructors(Members).Single(), null, new object?[] { null }), "Null catalog origin accepted.");
        foreach (string name in new[] { "CapturedConversationIdentity", "CapturedReplyBlock", "CapturedReply", "CapturedReplySnapshot" })
        {
            Type type = c.Type(name);
            Check(type.IsSealed, "History value is inheritable.");
            Check(type.GetProperties(BindingFlags.Public | BindingFlags.Instance).All(p => p.SetMethod == null), "History value has a setter.");
            Check(type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length == 0, "History value exposes mutable fields.");
            Check(type.GetProperties(Members).All(p => !typeof(System.Windows.DependencyObject).IsAssignableFrom(p.PropertyType)), "History value contains UI state.");
        }
        Check(c.CatalogType.GetEvents(Members).Length == 0, "Catalog exposes a callback/event.");
        Check(!c.CatalogType.GetMethods(Members).Any(m => m.GetParameters().Any(p => typeof(Delegate).IsAssignableFrom(p.ParameterType))), "Catalog accepts a callback.");
        foreach (var bound in new[] { ("MaxReplies", 64), ("MaxRetainedTextUnits", 1000000), ("MaxBlocksPerVariant", 128),
            ("MaxBlockTextUnits", 100000), ("MaxIdentifierUnits", 128), ("MaxMetadataUnits", 1024), ("MaxClientIdUnits", 256) })
            Check((int)c.CatalogType.GetField(bound.Item1, Members)!.GetRawConstantValue()! == bound.Item2, "History bound changed.");
        if (c.Assembly != typeof(CapturedReplyCatalog).Assembly)
            Check(!c.Assembly.GetReferencedAssemblies().Any(a => a.Name == "System.Speech" || a.Name == "VsCopilotChatVoice.Reader"), "Actual VSIX catalog pulls in speech/Reader.");
    }

    private static void ImmutabilityAndDedup(Context c)
    {
        object catalog = c.Catalog();
        try
        {
            object detailedA = c.Block("Detailed **alpha**.", "a"), detailedB = c.Block("Detailed beta.", "b", "step", 2);
            Array detailed = c.Blocks(detailedA, detailedB), concise = c.Blocks(c.Block("Beta.", "b", "step", 1), c.Block("Alpha.", "a"));
            object input = c.Input(detailed: detailed, concise: concise, metadata: "fixture");
            Check(c.Capture(catalog, input) == "Added", "Valid reordered pair rejected.");
            object snapshot = c.Snapshot(catalog), reply = Items(snapshot, "Replies")[0]!;
            string before = State(snapshot);
            Set(input, "ReplyId", "changed"); Set(input, "Origin", c.Identity(Guid.NewGuid(), c.Session, "other"));
            Set(input, "Completed", false); Set(input, "Metadata", "changed"); Set(input, "Detailed", null); Set(input, "Concise", null);
            foreach (string field in new[] { "Id", "Kind", "Text" }) c.BlockType.GetField(field)!.SetValue(detailedA, "mutated");
            c.BlockType.GetField("Number")!.SetValue(detailedB, 99); detailed.SetValue(null, 1); concise.SetValue(c.Block("changed"), 0);
            Check(State(c.Snapshot(catalog)) == before && State(snapshot) == before, "Caller DTO/array/nested mutation changed stored content.");
            foreach (IList list in new[] { Items(snapshot, "Replies"), Items(reply, "Detailed"), Items(reply, "Concise") })
            {
                Refused<NotSupportedException>(() => list[0] = null, "Snapshot collection permits replacement.");
                Refused<NotSupportedException>(() => list.Clear(), "Snapshot collection permits clearing.");
                Refused<NotSupportedException>(() => list.Add(null), "Snapshot collection permits append.");
            }
            Array immutableBlocks = Array.CreateInstance(c.Type("CapturedReplyBlock"), 1);
            immutableBlocks.SetValue(Items(reply, "Detailed")[0], 0);
            object defensiveReply = Invoke(c.Type("CapturedReply").GetConstructors(Members).Single(), null,
                "defensive", immutableBlocks, immutableBlocks, "fixture", 1)!;
            immutableBlocks.SetValue(null, 0);
            Check(Items(defensiveReply, "Detailed")[0] != null && Items(defensiveReply, "Concise")[0] != null, "Immutable reply constructor retains caller arrays.");
            Array immutableReplies = Array.CreateInstance(c.Type("CapturedReply"), 1);
            immutableReplies.SetValue(defensiveReply, 0);
            object defensiveSnapshot = Invoke(c.Type("CapturedReplySnapshot").GetConstructors(Members).Single(), null,
                immutableReplies, 0, false, false, 1)!;
            immutableReplies.SetValue(null, 0);
            Check(Items(defensiveSnapshot, "Replies")[0] != null, "Immutable snapshot constructor retains caller array.");
            object identical = c.Input(detailed: c.Blocks(c.Block("Detailed **alpha**.", "a"), c.Block("Detailed beta.", "b", "step", 2)),
                concise: c.Blocks(c.Block("Beta.", "b", "step", 1), c.Block("Alpha.", "a")), metadata: "fixture");
            Check(c.Capture(catalog, identical) == "Duplicate", "Identical delivery not deduplicated.");
            Check(State(c.Snapshot(catalog)) == before, "Duplicate mutated content/navigation/counters.");
            var conflicts = new List<object>();
            foreach (string field in new[] { "Id", "Kind", "Text", "Number" })
            {
                object block = c.Block("Detailed **alpha**.", "a");
                if (field == "Id") c.BlockType.GetField(field)!.SetValue(block, "different");
                if (field == "Kind") c.BlockType.GetField(field)!.SetValue(block, "heading");
                if (field == "Text") c.BlockType.GetField(field)!.SetValue(block, "Different.");
                if (field == "Number") { c.BlockType.GetField("Kind")!.SetValue(block, "step"); c.BlockType.GetField(field)!.SetValue(block, 3); }
                Array pair = c.Blocks(block, c.Block("Detailed beta.", "b", "step", 2));
                Array brief = c.Blocks(c.Block("Beta.", "b", "step", 1), c.Block("Alpha.", field == "Id" ? "different" : "a"));
                conflicts.Add(c.Input(detailed: pair, concise: brief, metadata: "fixture"));
            }
            conflicts.Add(c.Input(detailed: Get<Array>(identical, "Detailed"), concise: Get<Array>(identical, "Concise"), metadata: "different"));
            conflicts.Add(c.Input(detailed: Get<Array>(identical, "Detailed"), metadata: "fixture"));
            conflicts.Add(c.Input(detailed: Get<Array>(identical, "Detailed"), concise: c.Blocks(c.Block("Alpha.", "a"), c.Block("Beta.", "b", "step", 1)), metadata: "fixture"));
            conflicts.Add(c.Input(detailed: Get<Array>(identical, "Detailed"), concise: c.Blocks(c.Block("Changed.", "b", "step", 1), c.Block("Alpha.", "a")), metadata: "fixture"));
            foreach (object conflict in conflicts)
            {
                Check(c.Capture(catalog, conflict) == "ConflictingReplyId", "Conflicting reuse accepted.");
                Check(State(c.Snapshot(catalog)) == before, "Conflict partially mutated catalog.");
            }
            Set(identical, "ReplyId", "r2");
            Check(c.Capture(catalog, identical) == "Added" && Items(c.Snapshot(catalog), "Replies").Count == 2, "Identical text under different ID merged.");
            Check(State(snapshot) == before, "Arrival changed an earlier snapshot.");
            Check(Get<int>(c.Snapshot(catalog), "RetainedTextUnits") == 2 * Get<int>(reply, "TextUnits"), "Both variants not accounted.");
            Check(c.Capture(catalog, c.Input("unpaired")) == "Added", "Detailed-only reply rejected.");
            Check(Get<IList?>(Items(c.Snapshot(catalog), "Replies")[2]!, "Concise") == null, "Missing Concise fabricated.");
        }
        finally { c.Dispose(catalog); }
    }

    private static void Malformed(Context c)
    {
        object catalog = c.Catalog();
        try
        {
            Check(c.Capture(catalog, c.Input("kept")) == "Added", "Malformed fixture failed.");
            Refused<ArgumentNullException>(() => c.Capture(catalog, null), "Null input accepted.");
            foreach (object origin in new[] { c.Identity(Guid.NewGuid(), c.Session, "client-A"), c.Identity(c.Instance, Guid.NewGuid(), "client-A"),
                c.Identity(c.Instance, c.Session, "client-B"), c.Identity(c.Instance, c.Session, "CLIENT-A") })
                c.Rejected(catalog, c.Input(origin: origin), "Wrong explicit origin accepted.");
            object absentOrigin = c.Input(); Set(absentOrigin, "Origin", null); c.Rejected(catalog, absentOrigin, "Missing origin accepted.");
            object incomplete = c.Input(); Set(incomplete, "Completed", false); c.Rejected(catalog, incomplete, "Incomplete reply accepted.");
            foreach (string? invalid in new[] { null, "", " ", " padded", "padded ", "a\nb", new string('i', 129) })
            {
                c.Rejected(catalog, c.Input(invalid), "Malformed reply ID accepted.");
                c.Rejected(catalog, c.Input(detailed: c.Blocks(c.Block(id: invalid))), "Malformed anchor accepted.");
            }
            foreach (string metadata in new[] { "line\nbreak", new string('m', 1025) })
                c.Rejected(catalog, c.Input(metadata: metadata), "Malformed metadata accepted.");
            foreach (object? block in new object?[] { null, c.Block(null), c.Block(""), c.Block(" \t\n"), c.Block("**"), c.Block("**word"),
                c.Block(kind: null), c.Block(kind: "code"), c.Block(kind: "step", number: 0), c.Block(kind: "step", number: -1), c.Block(number: 1),
                c.Block("****", kind: "step", number: 1) })
            {
                c.Rejected(catalog, c.Input(detailed: c.Blocks(block)), "Malformed Detailed block accepted.");
                c.Rejected(catalog, c.Input(concise: c.Blocks(block)), "Malformed Concise block accepted.");
            }
            object missing = c.Input(); Set(missing, "Detailed", null); c.Rejected(catalog, missing, "Missing Detailed accepted.");
            c.Rejected(catalog, c.Input(detailed: c.Blocks()), "Empty Detailed accepted.");
            c.Rejected(catalog, c.Input(concise: c.Blocks()), "Empty Concise accepted.");
            c.Rejected(catalog, c.Input(detailed: c.Blocks(c.Block(), c.Block())), "Duplicate Detailed anchors accepted.");
            c.Rejected(catalog, c.Input(detailed: c.Blocks(c.Block(id: "a"), c.Block(id: "b")), concise: c.Blocks(c.Block(), c.Block())), "Duplicate Concise anchors accepted.");
            c.Rejected(catalog, c.Input(concise: c.Blocks(c.Block(id: "unknown"))), "Mismatched pair IDs accepted.");
            c.Rejected(catalog, c.Input(concise: c.Blocks(c.Block(), c.Block(id: "b"))), "Mismatched pair counts accepted.");
            Check(c.Capture(catalog, c.Input("valid", c.Blocks(c.Block("**bold**"), c.Block("Step.", "step", "step", int.MaxValue)))) == "Added", "Valid readability/kinds/step upper range changed.");
        }
        finally { c.Dispose(catalog); }
    }

    private static void Bounds(Context c)
    {
        object catalog = c.Catalog();
        try
        {
            Check(c.Capture(catalog, c.Input(new string('i', 128), c.Blocks(c.Block(id: new string('a', 128))), metadata: new string('m', 1024))) == "Added", "Exact identifier/metadata bound rejected.");
            c.Call(catalog, "Clear");
            foreach (bool concise in new[] { false, true })
            {
                Array blocks = c.Variant(128);
                Check(c.Capture(catalog, c.Input(detailed: blocks, concise: concise ? c.Variant(128) : null)) == "Added", "Exact block-count bound rejected.");
                Check(Items(Items(c.Snapshot(catalog), "Replies")[0]!, concise ? "Concise" : "Detailed").Count == 128, "Exact blocks not retained.");
                c.Rejected(catalog, concise ? c.Input("overflow", c.Variant(128), c.Variant(129)) : c.Input("overflow", c.Variant(129)), "Block-count limit+1 accepted.");
                c.Call(catalog, "Clear");
                Check(c.Capture(catalog, c.Input(detailed: c.Blocks(c.Block(new string('x', 100000))), concise: concise ? c.Blocks(c.Block(new string('y', 100000))) : null)) == "Added", "Exact block-text bound rejected.");
                c.Rejected(catalog, concise ? c.Input("overflow", concise: c.Blocks(c.Block(new string('x', 100001)))) : c.Input("overflow", c.Blocks(c.Block(new string('x', 100001)))), "Block-text limit+1 accepted.");
                c.Call(catalog, "Clear");
            }
            for (int i = 0; i < 64; i++) Check(c.Capture(catalog, c.Input("r" + i)) == "Added", "Reply capacity below exact limit rejected.");
            string full = State(c.Snapshot(catalog));
            Check(c.Capture(catalog, c.Input("r64")) == "CapacityExceeded", "Reply limit+1 accepted.");
            Check(State(c.Snapshot(catalog)) == full, "Reply capacity evicted/partially mutated history.");
            Check(c.Capture(catalog, c.Input("r0")) == "Duplicate", "Full catalog rejects duplicate.");
            Check(c.Capture(catalog, c.Input("r0", c.Blocks(c.Block("conflict")))) == "ConflictingReplyId", "Full catalog hides conflict behind capacity.");
            Check(State(c.Snapshot(catalog)) == full, "Full duplicate/conflict mutated state.");
            c.Call(catalog, "Clear");
            Check(c.Capture(catalog, c.Input(detailed: c.Text(500000), concise: c.Text(500000))) == "Added", "Exact paired total bound rejected.");
            Check(Get<int>(c.Snapshot(catalog), "RetainedTextUnits") == 1000000, "Paired UTF16 text counter incorrect.");
            full = State(c.Snapshot(catalog));
            Check(c.Capture(catalog, c.Input("overflow", c.Blocks(c.Block("x")))) == "CapacityExceeded", "Paired total limit+1 accepted.");
            Check(State(c.Snapshot(catalog)) == full, "Paired capacity partially mutated model.");
            c.Call(catalog, "Clear");
            Check(c.Capture(catalog, c.Input("too-big", c.Text(1000001))) == "CapacityExceeded", "Single reply total limit+1 accepted.");
            Check(Items(c.Snapshot(catalog), "Replies").Count == 0 && Get<int>(c.Snapshot(catalog), "SelectedIndex") == -1, "Rejected first reply initializes selection.");
            Check(c.Capture(catalog, c.Input("too-big", c.Text(1000000))) == "Added", "Rejected ID remains reserved.");
            c.Call(catalog, "Clear");
            Check(c.Capture(catalog, c.Input("first", c.Text(600000))) == "Added", "Cumulative fixture failed.");
            full = State(c.Snapshot(catalog));
            Check(c.Capture(catalog, c.Input("second", c.Text(400001))) == "CapacityExceeded", "Cumulative limit+1 accepted.");
            Check(State(c.Snapshot(catalog)) == full, "Cumulative rejection evicted/partially changed history.");
            Check(c.Capture(catalog, c.Input("second", c.Text(400000))) == "Added", "Exact cumulative limit/rejected-ID retry failed.");
            Check(Get<int>(c.Snapshot(catalog), "RetainedTextUnits") == 1000000 && Items(c.Snapshot(catalog), "Replies").Count == 2, "Exact cumulative model corrupted.");
            c.Call(catalog, "Clear");
            Check(c.Capture(catalog, c.Input(detailed: c.Blocks(c.Block("\ud83d\ude00")))) == "Added" && Get<int>(c.Snapshot(catalog), "RetainedTextUnits") == 2, "Text measured as code points instead of UTF16 units.");
        }
        finally { c.Dispose(catalog); }
    }

    private static void NavigationAndLifetime(Context c)
    {
        object catalog = c.Catalog();
        foreach (string method in new[] { "Snapshot", "Previous", "Next", "Latest" })
        {
            object empty = c.Call(catalog, method);
            Check(Items(empty, "Replies").Count == 0 && Get<int>(empty, "SelectedIndex") == -1 && !Get<bool>(empty, "ManualBrowsing") && !Get<bool>(empty, "HasNewReplies"), "Empty navigation changes selection/flags.");
        }
        Check(c.Capture(catalog, c.Input("first")) == "Added", "First arrival failed.");
        object first = c.Snapshot(catalog);
        Check(Get<int>(first, "SelectedIndex") == 0 && !Get<bool>(first, "ManualBrowsing") && !Get<bool>(first, "HasNewReplies"), "First arrival selection wrong.");
        Check(Get<int>(c.Call(catalog, "Previous"), "SelectedIndex") == 0, "Previous crossed lower bound.");
        c.Call(catalog, "Latest");
        c.Capture(catalog, c.Input("second")); c.Capture(catalog, c.Input("third"));
        object snapshot = c.Snapshot(catalog);
        Check(Get<int>(snapshot, "SelectedIndex") == 0 && Get<bool>(snapshot, "HasNewReplies"), "Arrival forced a selection jump.");
        snapshot = c.Call(catalog, "Next");
        Check(Get<int>(snapshot, "SelectedIndex") == 1 && Get<bool>(snapshot, "ManualBrowsing") && Get<bool>(snapshot, "HasNewReplies"), "Next lost manual/new-arrival state.");
        snapshot = c.Call(catalog, "Latest");
        Check(Get<int>(snapshot, "SelectedIndex") == 2 && !Get<bool>(snapshot, "ManualBrowsing") && !Get<bool>(snapshot, "HasNewReplies"), "Latest failed to reset live-mode indicators.");
        snapshot = c.Call(catalog, "Previous");
        Check(Get<int>(snapshot, "SelectedIndex") == 1 && Get<bool>(snapshot, "ManualBrowsing"), "Previous failed.");
        c.Capture(catalog, c.Input("fourth"));
        snapshot = c.Snapshot(catalog);
        Check(Get<int>(snapshot, "SelectedIndex") == 1 && Get<bool>(snapshot, "ManualBrowsing") && Get<bool>(snapshot, "HasNewReplies"), "Manual browsing changed on arrival.");
        for (int i = 0; i < 4; i++) snapshot = c.Call(catalog, "Next");
        Check(Get<int>(snapshot, "SelectedIndex") == 3 && Get<bool>(snapshot, "ManualBrowsing") && Get<bool>(snapshot, "HasNewReplies"), "Next/latest boundary cleared sticky manual indicator.");
        c.Call(catalog, "Latest"); c.Call(catalog, "Next");
        Check(Get<bool>(c.Snapshot(catalog), "ManualBrowsing"), "Explicit bounded Next leaves manual mode prematurely.");
        snapshot = c.Call(catalog, "Latest");
        c.Capture(catalog, c.Input("fifth"));
        Check(Get<int>(c.Snapshot(catalog), "SelectedIndex") == 3 && Get<bool>(c.Snapshot(catalog), "HasNewReplies"), "Live arrival has invented automatic queue policy.");
        c.Call(catalog, "Clear");
        Check(Items(c.Snapshot(catalog), "Replies").Count == 0 && Get<int>(c.Snapshot(catalog), "RetainedTextUnits") == 0 && Get<int>(c.Snapshot(catalog), "SelectedIndex") == -1, "Clear retained content/counters/selection.");
        Check(((ICollection)c.CatalogType.GetField("byId", Members)!.GetValue(catalog)!).Count == 0, "Clear retains dedup payload references.");
        Check(Items(snapshot, "Replies").Count == 4, "Clear mutated a caller-owned snapshot.");
        Check(c.Capture(catalog, c.Input("first")) == "Added", "Clear retained dedup identity.");
        c.Dispose(catalog); c.Dispose(catalog);
        Check(((ICollection)c.CatalogType.GetField("replies", Members)!.GetValue(catalog)!).Count == 0 &&
            ((ICollection)c.CatalogType.GetField("byId", Members)!.GetValue(catalog)!).Count == 0, "Dispose retains owned payload references.");
        Refused<ObjectDisposedException>(() => c.Capture(catalog, c.Input("late")), "Late input revived disposed catalog.");
        Refused<ObjectDisposedException>(() => c.Capture(catalog, null), "Late malformed input bypasses disposal.");
        foreach (string method in new[] { "Snapshot", "Previous", "Next", "Latest", "Clear" })
            Refused<ObjectDisposedException>(() => c.Call(catalog, method), "Disposed operation accepted.");
        Check(Items(first, "Replies").Count == 1, "Disposal mutated an earlier snapshot.");
        object independent = c.Catalog(c.Identity(Guid.NewGuid(), c.Session, "client-A")), sameOrigin = c.Catalog();
        try
        {
            c.Rejected(independent, c.Input("cross"), "Same-solution/session other instance accepts origin.");
            Check(c.Capture(independent, c.Input("local", origin: Get<object>(independent, "Origin"))) == "Added", "Independent instance fixture failed.");
            Check(Items(c.Snapshot(sameOrigin), "Replies").Count == 0, "Models with same origin share content.");
            Check(c.Capture(sameOrigin, c.Input("local")) == "Added", "Independent catalog shares dedup ledger.");
            c.Call(sameOrigin, "Clear");
            Check(Items(c.Snapshot(independent), "Replies").Count == 1, "Clear leaks across independent models.");
        }
        finally { c.Dispose(independent); c.Dispose(sameOrigin); }
    }

    private static void Concurrency(Context c)
    {
        object catalog = c.Catalog();
        try
        {
            int added = 0, duplicate = 0;
            Parallel.For(0, 32, i =>
            {
                string result = c.Capture(catalog, c.Input("same"));
                if (result == "Added") Interlocked.Increment(ref added);
                else if (result == "Duplicate") Interlocked.Increment(ref duplicate);
                else throw new InvalidOperationException("Concurrent duplicate unexpected result.");
            });
            Check(added == 1 && duplicate == 31 && Items(c.Snapshot(catalog), "Replies").Count == 1, "Concurrent duplicates not atomic.");
            c.Call(catalog, "Clear"); added = 0; int capacity = 0;
            Parallel.For(0, 96, i =>
            {
                string result = c.Capture(catalog, c.Input("r" + i));
                if (result == "Added") Interlocked.Increment(ref added);
                else if (result == "CapacityExceeded") Interlocked.Increment(ref capacity);
                else throw new InvalidOperationException("Concurrent capacity unexpected result.");
                object snap = c.Snapshot(catalog);
                if (Get<int>(snap, "SelectedIndex") < 0 || Get<int>(snap, "SelectedIndex") >= Items(snap, "Replies").Count)
                    throw new InvalidOperationException("Concurrent snapshot has inconsistent selection.");
                c.Call(catalog, i % 3 == 0 ? "Latest" : i % 3 == 1 ? "Previous" : "Next");
            });
            object full = c.Snapshot(catalog);
            Check(added == 64 && capacity == 32 && Items(full, "Replies").Count == 64 && Get<int>(full, "RetainedTextUnits") == 256, "Concurrent distinct arrivals lost/corrupted entries or exceeded capacity.");
            Check(Items(full, "Replies").Cast<object>().Select(r => Get<string>(r, "ReplyId")).Distinct().Count() == 64, "Concurrent arrivals duplicated identity.");
            c.Call(catalog, "Clear"); added = 0; capacity = 0;
            Parallel.For(0, 16, i =>
            {
                string result = c.Capture(catalog, c.Input("text" + i, c.Text(100000)));
                if (result == "Added") Interlocked.Increment(ref added);
                else if (result == "CapacityExceeded") Interlocked.Increment(ref capacity);
                else throw new InvalidOperationException("Concurrent text capacity unexpected result.");
            });
            Check(added == 10 && capacity == 6 && Get<int>(c.Snapshot(catalog), "RetainedTextUnits") == 1000000, "Concurrent retained-text capacity not atomic.");
            c.Call(catalog, "Clear"); added = 0; int conflicts = 0;
            Parallel.For(0, 32, i =>
            {
                string result = c.Capture(catalog, c.Input("conflict", c.Blocks(c.Block("payload" + i))));
                if (result == "Added") Interlocked.Increment(ref added);
                else if (result == "ConflictingReplyId") Interlocked.Increment(ref conflicts);
                else throw new InvalidOperationException("Concurrent conflict unexpected result.");
            });
            Check(added == 1 && conflicts == 31 && Items(c.Snapshot(catalog), "Replies").Count == 1, "Concurrent conflicting IDs partially mutate.");
            Parallel.For(0, 32, i =>
            {
                if (i == 0) c.Dispose(catalog);
                else try { c.Capture(catalog, c.Input("late" + i)); } catch (ObjectDisposedException) { }
            });
            Refused<ObjectDisposedException>(() => c.Capture(catalog, c.Input("after")), "Concurrent disposal permits late arrivals.");
            Check(((ICollection)c.CatalogType.GetField("byId", Members)!.GetValue(catalog)!).Count == 0, "Concurrent disposal retains entries.");
        }
        finally { c.Dispose(catalog); }
    }

    private static void LegacyValidation()
    {
        foreach (var error in new[] { ("", "wrong", "", 0, "An anchor ID is required."), ("a", "wrong", "", 0, "Readable text is required in a"),
            ("a", "wrong", "**bad", 0, "Unsupported content kind: wrong"), ("a", "step", "**bad", 0, "number"),
            ("a", "paragraph", "**bad", 0, "Unclosed bold marker in a"), ("a", "paragraph", "****", 0, "Readable text is required in a") })
        {
            var document = new FlowDocument();
            try { DocumentBuilder.Add(document, error.Item1, error.Item2, error.Item3, error.Item4); throw new InvalidOperationException("Legacy malformed block accepted."); }
            catch (ArgumentException ex)
            {
                Check(error.Item5 == "number" ? ex is ArgumentOutOfRangeException && ex.ParamName == "number" : ex.Message == error.Item5, "Legacy validation error order/message changed.");
                Check(document.Blocks.Count == 0, "Legacy rejected block mutated document.");
            }
        }
        Segment step = DocumentBuilder.Add(new FlowDocument(), "a", "step", "****", 1);
        Check(step.Spoken == "Step 1. " && step.Words.Count == 1, "Legacy prefix-only step behavior changed.");
        Segment paragraph = DocumentBuilder.Add(new FlowDocument(), " padded ", "paragraph", "Body.", 99);
        Check(paragraph.Id == " padded " && paragraph.Spoken == "Body.", "Legacy permissive identifiers/nonstep number changed.");
    }

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length < 1 || args.Length > 2) throw new ArgumentException("Usage: --reply-history-probe [VSIX-DLL]");
            assertions = 0;
            foreach (string? dll in args.Length == 2 ? new string?[] { null, args[1] } : new string?[] { null })
            {
                var context = new Context(dll);
                IdentityAndStructure(context); ImmutabilityAndDedup(context); Malformed(context); Bounds(context);
                NavigationAndLifetime(context); Concurrency(context);
                Console.WriteLine("PASS: reply history " + (dll == null ? "linked" : "actual DLL") + " verified.");
            }
            LegacyValidation();
            Console.WriteLine("PASS: " + assertions + " reply history assertions; data only, no feed/UI/audio/preferences.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: reply history after " + assertions + " assertions: " + ex); return 1; }
    }
}
