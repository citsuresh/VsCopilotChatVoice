#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace VsCopilotChatVoice.Speech;

// Compiled into the VSIX, the reader and the test harness. It must not depend on System.Speech or on
// Visual Studio: the IDE hosts presentation only and the external worker owns speech synthesis.
// Each consuming project assigns only the fields it sends.
#pragma warning disable CS0649
[DataContract]
internal sealed class WorkerMessage
{
    [DataMember(Name = "type", IsRequired = true)] public string Type = "";
    [DataMember(Name = "gen")] public long Generation;
    [DataMember(Name = "command")] public long Command;
    [DataMember(Name = "offset")] public int Offset;
    [DataMember(Name = "text")] public string? Text;
    [DataMember(Name = "message")] public string? Message;
}
#pragma warning restore CS0649

internal static class WorkerProtocol
{
    public const string Speak = "speak";
    public const string Cancel = "cancel";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Paused = "paused";
    public const string Resumed = "resumed";
    public const string Shutdown = "shutdown";
    public const string Progress = "progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Error = "error";

    public const int MaxLineBytes = 262144;
    public const int MaxSpeakChars = 100000;
    public const int MaxMessageChars = 200;

    private static readonly Regex PipeNamePattern = new Regex("^VsCopilotChatVoice\\.Speech\\.[0-9a-f]{32}$",
        RegexOptions.CultureInvariant);

    public static string NewPipeName() => "VsCopilotChatVoice.Speech." + Guid.NewGuid().ToString("N");

    public static bool IsValidPipeName(string? name) => name != null && PipeNamePattern.IsMatch(name);

    public static byte[] Serialize(WorkerMessage message)
    {
        using var buffer = new MemoryStream();
        new DataContractJsonSerializer(typeof(WorkerMessage)).WriteObject(buffer, message);
        buffer.WriteByte((byte)'\n');
        if (buffer.Length > MaxLineBytes) throw new ArgumentException("The message is too large.");
        return buffer.ToArray();
    }

    // Returns null for anything that is not one well-formed, known message of bounded size.
    public static WorkerMessage? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxLineBytes) return null;
        try
        {
            using var buffer = new MemoryStream(Encoding.UTF8.GetBytes(line));
            var message = new DataContractJsonSerializer(typeof(WorkerMessage)).ReadObject(buffer) as WorkerMessage;
            if (message == null) return null;
            switch (message.Type)
            {
                case Speak:
                    if (message.Generation < 1 || string.IsNullOrWhiteSpace(message.Text) ||
                        message.Text!.Length > MaxSpeakChars) return null;
                    return message;
                case Cancel:
                case Completed:
                case Cancelled:
                    return message.Generation < 1 ? null : message;
                case Pause:
                case Resume:
                case Paused:
                case Resumed:
                    return message.Generation < 1 || message.Command < 1 ? null : message;
                case Progress:
                    return message.Generation < 1 || message.Offset < 0 ? null : message;
                case Error:
                    if (message.Generation < 0 || message.Command < 0) return null;
                    if (message.Message != null && message.Message.Length > MaxMessageChars)
                        message.Message = message.Message.Substring(0, MaxMessageChars);
                    return message;
                case Shutdown:
                    return message;
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is SerializationException || ex is ArgumentException ||
            ex is InvalidCastException || ex is FormatException)
        {
            return null;
        }
    }
}

// Reads newline-terminated UTF-8 lines with a hard size bound; an overlong line is a protocol failure.
internal sealed class BoundedLineReader
{
    private readonly Stream stream;
    private readonly int maxBytes;
    private readonly Encoding encoding;
    private readonly byte[] chunk = new byte[4096];
    private int position;
    private int length;

    public BoundedLineReader(Stream stream, int maxBytes = WorkerProtocol.MaxLineBytes, bool strictUtf8 = false)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        this.maxBytes = maxBytes;
        encoding = strictUtf8 ? new UTF8Encoding(false, true) : Encoding.UTF8;
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellation)
    {
        using var line = new MemoryStream();
        while (true)
        {
            if (position == length)
            {
                length = await stream.ReadAsync(chunk, 0, chunk.Length, cancellation).ConfigureAwait(false);
                position = 0;
                if (length == 0) return null;
            }
            while (position < length)
            {
                byte value = chunk[position++];
                if (value == (byte)'\n') return encoding.GetString(line.ToArray()).TrimEnd('\r');
                if (line.Length >= maxBytes) throw new InvalidDataException("A worker line exceeded the size bound.");
                line.WriteByte(value);
            }
        }
    }
}

// UI-free word model shared by the tool window and the tests: spoken offsets from the worker map to words.
internal sealed class ReplyWord
{
    public ReplyWord(int start, int length) { Start = start; Length = length; }
    public int Start { get; }
    public int Length { get; }
}

internal static class ReplyText
{
    public static List<ReplyWord> Tokenize(string text)
    {
        var words = new List<ReplyWord>();
        foreach (Match token in Regex.Matches(text, "\\S+"))
            words.Add(new ReplyWord(token.Index, token.Length));
        return words;
    }

    public static int FindWord(IReadOnlyList<ReplyWord> words, int offset)
    {
        int low = 0, high = words.Count - 1, found = -1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (words[middle].Start <= offset) { found = middle; low = middle + 1; }
            else high = middle - 1;
        }
        return found;
    }
}
