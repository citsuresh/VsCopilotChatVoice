using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;

namespace VsCopilotChatVoice.VisualStudio;

// Exp-only capability refusal. Native atomic editing is unproved; no draft access is allowed.
internal sealed class ChatDraftPoc
{
    internal const int MaxRequestBytes = 32768;
    internal const int MaxTranscriptCharacters = 4096;
    private readonly object sync = new object();
    private readonly Dictionary<Guid, Entry> requests = new Dictionary<Guid, Entry>();
    private readonly Action<Exception> reportError;
    private readonly int capacity;

    internal ChatDraftPoc(Action<Exception> reportError, int capacity = 128)
    {
        this.reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        if (capacity < 1 || capacity > 128) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    internal InsertResult RejectInsert(InsertRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        string fingerprint;
        using (var data = new MemoryStream())
        using (var hash = SHA256.Create())
        {
            new DataContractJsonSerializer(typeof(InsertRequest)).WriteObject(data, request);
            fingerprint = Convert.ToBase64String(hash.ComputeHash(data.ToArray()));
        }
        lock (sync)
        {
            if (requests.TryGetValue(request.RequestId, out Entry? previous))
                return previous.Fingerprint == fingerprint ? previous.Result.Copy(true) :
                    Result(request, "Rejected", "RequestIdConflict");
            if (requests.Count == capacity) return Result(request, "Rejected", "RequestCapacityReached");
            var entry = new Entry(fingerprint, Result(request, "Rejected", "NativeAtomicInsertionUnsupported"));
            requests.Add(request.RequestId, entry);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return entry.Result.Copy(false);
            }
            catch (OperationCanceledException error)
            {
                entry.Result = Result(request, "Rejected", "Cancelled");
                reportError(error);
                return entry.Result.Copy(false);
            }
        }
    }

    internal static bool HasConstructorOnlyBinding(PropertyInfo property)
    {
        MethodInfo? getter = property.GetMethod;
        if (getter == null || !getter.IsPublic || getter.IsVirtual || getter.IsStatic ||
            property.CanWrite || property.GetIndexParameters().Length != 0) return false;
        byte[]? bytes = getter.GetMethodBody()?.GetILAsByteArray();
        if (bytes == null || bytes.Length != 7 || bytes[0] != 2 || bytes[1] != 123 || bytes[6] != 42) return false;
        // ldarg.0; ldfld token; ret. Only inspect metadata, never the private field value.
        FieldInfo field = getter.Module.ResolveField(BitConverter.ToInt32(bytes, 2));
        return field.IsInitOnly && !field.IsStatic && field.DeclaringType == getter.DeclaringType &&
            field.FieldType == property.PropertyType;
    }

    private static void Validate(InsertRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.Version != 1 || request.RequestId == Guid.Empty || request.SessionId == Guid.Empty ||
            request.Generation == null || !Guid.TryParseExact(request.Generation, "N", out _) ||
            request.DraftRevision < 0 || request.DraftRevision == long.MaxValue ||
            string.IsNullOrWhiteSpace(request.ClientId) || request.ClientId.Length > 256 ||
            string.IsNullOrWhiteSpace(request.Transcript) || request.Transcript.Length > MaxTranscriptCharacters)
            throw new InvalidDataException("The guarded draft request is invalid or exceeds its limits.");
        foreach (char character in request.Transcript)
            if (char.IsControl(character) && character != '\r' && character != '\n' && character != '\t')
                throw new InvalidDataException("The transcript contains an unsupported control character.");
    }

    private static InsertResult Result(InsertRequest request, string status, string reason) =>
        new InsertResult { RequestId = request.RequestId, Status = status, Reason = reason };

    private sealed class Entry
    {
        internal readonly string Fingerprint;
        internal InsertResult Result;
        internal Entry(string fingerprint, InsertResult result) { Fingerprint = fingerprint; Result = result; }
    }

    [DataContract]
    internal sealed class InsertRequest
    {
        [DataMember(IsRequired = true)] public int Version { get; set; }
        [DataMember(IsRequired = true)] public Guid RequestId { get; set; }
        [DataMember(IsRequired = true)] public Guid SessionId { get; set; }
        [DataMember(IsRequired = true)] public string ClientId { get; set; } = "";
        [DataMember(IsRequired = true)] public string Generation { get; set; } = "";
        [DataMember(IsRequired = true)] public long DraftRevision { get; set; }
        [DataMember(IsRequired = true)] public string Transcript { get; set; } = "";
    }

    [DataContract]
    internal sealed class InsertResult
    {
        [DataMember] public Guid RequestId { get; set; }
        [DataMember] public string Status { get; set; } = "Rejected";
        [DataMember] public string Reason { get; set; } = "Unspecified";
        [DataMember] public bool MutationAttempted { get; set; }
        [DataMember] public bool HadExistingDraft { get; set; }
        [DataMember] public long? DraftRevision { get; set; }
        [DataMember] public bool Replayed { get; set; }
        internal InsertResult Copy(bool replayed) => new InsertResult
        {
            RequestId = RequestId, Status = Status, Reason = Reason, MutationAttempted = MutationAttempted,
            HadExistingDraft = HadExistingDraft, DraftRevision = DraftRevision, Replayed = replayed
        };
    }
}
