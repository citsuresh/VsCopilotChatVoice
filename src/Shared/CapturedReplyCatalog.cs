#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ReadAlongTrial;
using VsCopilotChatVoice.Bridge;

namespace VsCopilotChatVoice.History;

internal sealed class CapturedConversationIdentity
{
    public Guid InstanceId { get; }
    public Guid SessionId { get; }
    public string ClientId { get; }

    public CapturedConversationIdentity(Guid instanceId, Guid sessionId, string clientId)
    {
        if (instanceId == Guid.Empty || sessionId == Guid.Empty || !BridgeProtocol.ValidClientId(clientId))
            throw new ArgumentException("A complete instance/session/client identity is required.");
        InstanceId = instanceId; SessionId = sessionId; ClientId = clientId;
    }

    internal bool Matches(CapturedConversationIdentity other) => InstanceId == other.InstanceId &&
        SessionId == other.SessionId && string.Equals(ClientId, other.ClientId, StringComparison.Ordinal);
}

internal sealed class CapturedReplyInput
{
    public CapturedConversationIdentity? Origin { get; set; }
    public string? ReplyId { get; set; }
    // A caller assertion, not proof of native completion, trust or transport authority.
    public bool Completed { get; set; }
    public ReplyBlock[]? Detailed { get; set; }
    public ReplyBlock[]? Concise { get; set; }
    public string? Metadata { get; set; }
}

internal sealed class CapturedReplyBlock
{
    public string Id { get; }
    public string Kind { get; }
    public string Text { get; }
    public int Number { get; }

    internal CapturedReplyBlock(string id, string kind, string text, int number)
    {
        Id = id; Kind = kind; Text = text; Number = number;
    }

    internal bool Matches(CapturedReplyBlock other) => Id == other.Id && Kind == other.Kind &&
        Text == other.Text && Number == other.Number;
}

internal sealed class CapturedReply
{
    public string ReplyId { get; }
    public ReadOnlyCollection<CapturedReplyBlock> Detailed { get; }
    public ReadOnlyCollection<CapturedReplyBlock>? Concise { get; }
    public string? Metadata { get; }
    public int TextUnits { get; }

    internal CapturedReply(string replyId, CapturedReplyBlock[] detailed, CapturedReplyBlock[]? concise,
        string? metadata, int textUnits)
    {
        ReplyId = replyId; Detailed = Array.AsReadOnly((CapturedReplyBlock[])detailed.Clone());
        Concise = concise == null ? null : Array.AsReadOnly((CapturedReplyBlock[])concise.Clone());
        Metadata = metadata; TextUnits = textUnits;
    }

    internal bool Matches(CapturedReply other)
    {
        if (Metadata != other.Metadata || Detailed.Count != other.Detailed.Count ||
            (Concise == null) != (other.Concise == null)) return false;
        for (int i = 0; i < Detailed.Count; i++) if (!Detailed[i].Matches(other.Detailed[i])) return false;
        if (Concise != null && other.Concise != null)
        {
            if (Concise.Count != other.Concise.Count) return false;
            for (int i = 0; i < Concise.Count; i++) if (!Concise[i].Matches(other.Concise[i])) return false;
        }
        return true;
    }
}

internal sealed class CapturedReplySnapshot
{
    public ReadOnlyCollection<CapturedReply> Replies { get; }
    public int SelectedIndex { get; }
    public bool ManualBrowsing { get; }
    public bool HasNewReplies { get; }
    public int RetainedTextUnits { get; }

    internal CapturedReplySnapshot(CapturedReply[] replies, int selectedIndex, bool manualBrowsing,
        bool hasNewReplies, int retainedTextUnits)
    {
        Replies = Array.AsReadOnly((CapturedReply[])replies.Clone()); SelectedIndex = selectedIndex;
        ManualBrowsing = manualBrowsing; HasNewReplies = hasNewReplies; RetainedTextUnits = retainedTextUnits;
    }
}

internal enum CaptureReplyResult { Added, Duplicate, ConflictingReplyId, CapacityExceeded }

internal sealed class CapturedReplyCatalog : IDisposable
{
    public const int MaxReplies = 64;
    public const int MaxRetainedTextUnits = 1000000;
    public const int MaxBlocksPerVariant = 128;
    public const int MaxBlockTextUnits = 100000;
    public const int MaxIdentifierUnits = 128;
    public const int MaxMetadataUnits = 1024;
    public const int MaxClientIdUnits = 256;

    private readonly object sync = new object();
    private readonly List<CapturedReply> replies = new List<CapturedReply>();
    private readonly Dictionary<string, CapturedReply> byId = new Dictionary<string, CapturedReply>(StringComparer.Ordinal);
    private int selectedIndex = -1, retainedTextUnits;
    private bool manualBrowsing, hasNewReplies, disposed;
    public CapturedConversationIdentity Origin { get; }

    public CapturedReplyCatalog(CapturedConversationIdentity origin)
    {
        if (origin == null) throw new ArgumentNullException(nameof(origin));
        Origin = new CapturedConversationIdentity(origin.InstanceId, origin.SessionId, origin.ClientId);
    }

    public CaptureReplyResult Capture(CapturedReplyInput input)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (input == null) throw new ArgumentNullException(nameof(input));
            CapturedConversationIdentity? origin = input.Origin;
            string? replyId = input.ReplyId, metadata = input.Metadata;
            bool completed = input.Completed;
            ReplyBlock[]? detailedInput = input.Detailed, conciseInput = input.Concise;
            if (origin == null || !Origin.Matches(origin)) throw new ArgumentException("The reply origin does not match this catalog.");
            if (!completed) throw new ArgumentException("Only caller-supplied completed replies are accepted.");
            ValidateIdentifier(replyId);
            if (metadata != null && (metadata.Length > MaxMetadataUnits || HasControl(metadata)))
                throw new ArgumentException("Invalid reply metadata.");
            int textUnits = 0;
            CapturedReplyBlock[] detailed = CopyVariant(detailedInput, ref textUnits);
            CapturedReplyBlock[]? concise = conciseInput == null ? null : CopyVariant(conciseInput, ref textUnits);
            if (concise != null)
            {
                ReaderBlockValidation.ValidateSectionCount(detailed.Length, concise.Length);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (CapturedReplyBlock block in detailed) ids.Add(block.Id);
                foreach (CapturedReplyBlock block in concise) ReaderBlockValidation.MatchSectionId(ids, block.Id);
            }
            var candidate = new CapturedReply(replyId!, detailed, concise, metadata, textUnits);
            if (byId.TryGetValue(replyId!, out CapturedReply existing))
                return existing.Matches(candidate) ? CaptureReplyResult.Duplicate : CaptureReplyResult.ConflictingReplyId;
            if (replies.Count == MaxReplies || textUnits > MaxRetainedTextUnits - retainedTextUnits)
                return CaptureReplyResult.CapacityExceeded;
            replies.Add(candidate); byId.Add(candidate.ReplyId, candidate); retainedTextUnits += textUnits;
            if (selectedIndex < 0) selectedIndex = 0;
            else hasNewReplies = true;
            return CaptureReplyResult.Added;
        }
    }

    private static CapturedReplyBlock[] CopyVariant(ReplyBlock[]? input, ref int textUnits)
    {
        if (input == null || input.Length == 0 || input.Length > MaxBlocksPerVariant)
            throw new ArgumentException("A variant must contain 1 through 128 blocks.");
        var copy = new CapturedReplyBlock[input.Length];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < input.Length; i++)
        {
            ReplyBlock block = input[i] ?? throw new ArgumentException("A reply block cannot be null.");
            string id = block.Id, kind = block.Kind, text = block.Text;
            int number = block.Number;
            ValidateIdentifier(id);
            ReaderBlockValidation.AddSectionId(ids, id);
            ReaderBlockValidation.ValidateHeader(id, kind, text);
            if (text.Length > MaxBlockTextUnits) throw new ArgumentException("A block exceeds 100000 UTF16 text units.");
            ReaderBlockValidation.ValidateNumber(kind, number);
            if (kind != "step" && number != 0) throw new ArgumentException("Only step blocks may have a number.");
            bool readable = false;
            foreach (ReaderBlockValidation.TextToken token in ReaderBlockValidation.ReadTokens(id, text))
                if (!string.IsNullOrWhiteSpace(token.Text)) readable = true;
            ReaderBlockValidation.ValidateReadable(id, readable);
            textUnits += text.Length;
            copy[i] = new CapturedReplyBlock(id, kind, text, number);
        }
        return copy;
    }

    private static bool HasControl(string value)
    {
        foreach (char character in value) if (char.IsControl(character)) return true;
        return false;
    }

    private static void ValidateIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value!.Length > MaxIdentifierUnits || HasControl(value) || value != value.Trim())
            throw new ArgumentException("Identifiers must be nonblank, unpadded, control-free and at most 128 UTF16 units.");
    }

    public CapturedReplySnapshot Snapshot()
    {
        lock (sync) { ThrowIfDisposed(); return CreateSnapshot(); }
    }

    public CapturedReplySnapshot Previous() => Navigate(-1);
    public CapturedReplySnapshot Next() => Navigate(1);

    private CapturedReplySnapshot Navigate(int direction)
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (selectedIndex >= 0)
            {
                selectedIndex = Math.Max(0, Math.Min(replies.Count - 1, selectedIndex + direction));
                manualBrowsing = true;
            }
            return CreateSnapshot();
        }
    }

    public CapturedReplySnapshot Latest()
    {
        lock (sync)
        {
            ThrowIfDisposed(); selectedIndex = replies.Count - 1; manualBrowsing = false; hasNewReplies = false;
            return CreateSnapshot();
        }
    }

    private CapturedReplySnapshot CreateSnapshot() =>
        new CapturedReplySnapshot(replies.ToArray(), selectedIndex, manualBrowsing, hasNewReplies, retainedTextUnits);

    public void Clear()
    {
        lock (sync) { ThrowIfDisposed(); ClearOwned(); }
    }

    private void ClearOwned()
    {
        replies.Clear(); byId.Clear(); selectedIndex = -1; retainedTextUnits = 0; manualBrowsing = false; hasNewReplies = false;
    }

    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; ClearOwned(); }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(CapturedReplyCatalog));
    }
}
