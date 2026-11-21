#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace VsCopilotChatVoice.Bridge;

[DataContract]
internal sealed class BridgeRequest
{
    [DataMember(Name = "version", IsRequired = true)] public int Version { get; set; } = BridgeProtocol.Version;
    [DataMember(Name = "operation", IsRequired = true)] public string Operation { get; set; } = "";
    [DataMember(Name = "requestId", IsRequired = true)] public Guid RequestId { get; set; } = Guid.NewGuid();
    [DataMember(Name = "sequence", IsRequired = true)] public int Sequence { get; set; }
    [DataMember(Name = "clientPid", IsRequired = true)] public int ClientPid { get; set; }
    [DataMember(Name = "instanceId", IsRequired = true)] public Guid InstanceId { get; set; }
    [DataMember(Name = "sessionId", IsRequired = true)] public Guid SessionId { get; set; }
    [DataMember(Name = "clientId", IsRequired = true)] public string? ClientId { get; set; }
    [DataMember(Name = "epoch", IsRequired = true)] public Guid Epoch { get; set; }
}

[DataContract]
internal sealed class BridgeTarget
{
    [DataMember(Name = "sessionId", IsRequired = true)] public Guid SessionId { get; set; }
    [DataMember(Name = "clientId", IsRequired = true)] public string ClientId { get; set; } = "";
    [DataMember(Name = "epoch", IsRequired = true)] public Guid Epoch { get; set; }
    [DataMember(Name = "revision", IsRequired = true)] public long Revision { get; set; }
    [DataMember(Name = "status", IsRequired = true)] public string Status { get; set; } = "";
    [DataMember(Name = "trusted", IsRequired = true)] public bool? Trusted { get; set; }
    [DataMember(Name = "responding", IsRequired = true)] public bool? Responding { get; set; }
    [DataMember(Name = "readOnly", IsRequired = true)] public bool ReadOnly { get; set; }
    [DataMember(Name = "transcript", IsRequired = true)] public bool Transcript { get; set; }

    internal bool Matches(BridgeRequest request) => SessionId == request.SessionId && Epoch == request.Epoch &&
        string.Equals(ClientId, request.ClientId, StringComparison.Ordinal);

    internal void Validate()
    {
        if (SessionId == Guid.Empty || Epoch == Guid.Empty || !BridgeProtocol.ValidClientId(ClientId) ||
            Revision < 0 || (Status != "Observed" && Status != "Invalidated"))
            throw new InvalidDataException("Invalid target metadata.");
    }
}

[DataContract]
internal sealed class BridgeResponse
{
    [DataMember(Name = "version", IsRequired = true)] public int Version { get; set; } = BridgeProtocol.Version;
    [DataMember(Name = "requestId", IsRequired = true)] public Guid RequestId { get; set; }
    [DataMember(Name = "sequence", IsRequired = true)] public int Sequence { get; set; }
    [DataMember(Name = "instanceId", IsRequired = true)] public Guid InstanceId { get; set; }
    [DataMember(Name = "code", IsRequired = true)] public string Code { get; set; } = "";
    [DataMember(Name = "metadata", IsRequired = true)] public bool Metadata { get; set; } = true;
    [DataMember(Name = "atomicEdit", IsRequired = true)] public bool AtomicEdit { get; set; }
    [DataMember(Name = "replyFeed", IsRequired = true)] public bool ReplyFeed { get; set; }
    [DataMember(Name = "targets", IsRequired = true)] public BridgeTarget[] Targets { get; set; } = Array.Empty<BridgeTarget>();
}

internal static class BridgeProtocol
{
    internal const int Version = 1;
    internal const int MaxBytes = 65536;
    internal const int MaxTargets = 32;
    internal const int MaxRequests = 64;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly string[] RequestFields = { "version", "operation", "requestId", "sequence", "clientPid", "instanceId", "sessionId", "clientId", "epoch" };
    private static readonly string[] ResponseFields = { "version", "requestId", "sequence", "instanceId", "code", "metadata", "atomicEdit", "replyFeed", "targets" };
    private static readonly string[] Codes = { "Ok", "Malformed", "UnsupportedVersion", "UnknownOperation", "PeerMismatch", "HelloRequired", "WrongInstance", "DuplicateRequest", "OutOfOrder", "CapacityExceeded", "UnknownTarget", "AmbiguousTarget", "TargetUnavailable" };

    internal static string PipeName(int processId)
    {
        if (processId < 1) throw new ArgumentOutOfRangeException(nameof(processId));
        return "VsCopilotChatVoice.Metadata." + processId;
    }

    internal static bool ValidClientId(string? value) => !string.IsNullOrWhiteSpace(value) && value!.Length <= 256 &&
        !value.Any(char.IsControl);

    internal static byte[] Serialize<T>(T value)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        stream.WriteByte(10);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Metadata exceeded the frame limit.");
        return stream.ToArray();
    }

    private static T Parse<T>(string line, string[] allowed)
    {
        byte[] bytes = Utf8.GetBytes(line);
        if (bytes.Length == 0 || bytes.Length > MaxBytes) throw new InvalidDataException("Invalid metadata frame size.");
        // The framework JSON reader can stop at the first root and ignore trailing data.
        int depth = 0;
        bool quoted = false, escaped = false, ended = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']')
            {
                if (--depth == 0)
                {
                    if (line.Skip(i + 1).Any(ch => !char.IsWhiteSpace(ch))) throw new InvalidDataException("Trailing metadata data.");
                    ended = true;
                    break;
                }
            }
        }
        if (!ended) throw new InvalidDataException("Incomplete metadata object.");
        try
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max))
            {
                reader.MoveToContent();
                if (reader.GetAttribute("type") != "object") throw new InvalidDataException("Expected a metadata object.");
                reader.ReadStartElement();
                var fields = new HashSet<string>(StringComparer.Ordinal);
                while (reader.MoveToContent() == XmlNodeType.Element)
                {
                    if (!allowed.Contains(reader.LocalName) || !fields.Add(reader.LocalName))
                        throw new InvalidDataException("Unexpected or repeated metadata field.");
                    reader.Skip();
                }
                reader.ReadEndElement();
                if (reader.Read()) throw new InvalidDataException("Trailing metadata data.");
            }
            using var stream = new MemoryStream(bytes);
            return (T)(new DataContractJsonSerializer(typeof(T)).ReadObject(stream) ??
                throw new InvalidDataException("Missing metadata object."));
        }
        catch (Exception ex) when (ex is SerializationException || ex is XmlException || ex is FormatException || ex is ArgumentException)
        {
            throw new InvalidDataException("Malformed metadata object.", ex);
        }
    }

    internal static BridgeRequest ParseRequest(string line)
    {
        BridgeRequest request = Parse<BridgeRequest>(line, RequestFields);
        if (request.RequestId == Guid.Empty || request.Sequence < 1 || request.ClientPid < 1 ||
            string.IsNullOrEmpty(request.Operation) || request.Operation.Length > 32)
            throw new InvalidDataException("Invalid request identity.");
        return request;
    }

    internal static BridgeResponse ParseResponse(string line)
    {
        BridgeResponse response = Parse<BridgeResponse>(line, ResponseFields);
        if (response.Version != Version || response.InstanceId == Guid.Empty || !Codes.Contains(response.Code) ||
            !response.Metadata || response.AtomicEdit || response.ReplyFeed || response.Targets == null ||
            response.Targets.Length > MaxTargets || (response.Code != "Ok" && response.Targets.Length != 0))
            throw new InvalidDataException("Invalid metadata acknowledgement.");
        foreach (BridgeTarget target in response.Targets) (target ?? throw new InvalidDataException("Null target.")).Validate();
        return response;
    }
}