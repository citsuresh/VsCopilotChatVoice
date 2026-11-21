#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace VsCopilotChatVoice.Speech;

// Explicit per-prompt values, independent of storage, discovery and synthesis ownership.
internal sealed class SpeechOptionsSnapshot
{
    public SpeechOptionsSnapshot(string voice, int rate, int volume)
    {
        if (voice == null || (voice.Length != 0 && string.IsNullOrWhiteSpace(voice)) ||
            voice.Length > SpeechCapabilities.MaxNameChars || voice.Any(char.IsControl))
            throw new ArgumentException("Invalid bounded speech voice name.", nameof(voice));
        try { SpeechCapabilities.Utf8.GetBytes(voice); }
        catch (EncoderFallbackException ex) { throw new ArgumentException("Invalid speech voice Unicode.", nameof(voice), ex); }
        if (rate < -10 || rate > 10) throw new ArgumentOutOfRangeException(nameof(rate));
        if (volume < 0 || volume > 100) throw new ArgumentOutOfRangeException(nameof(volume));
        Voice = voice;
        Rate = rate;
        Volume = volume;
    }

    public string Voice { get; }
    public int Rate { get; }
    public int Volume { get; }

    internal RememberedVoiceAvailability AssessVoice(SpeechVoiceCapability[] voices) =>
        new SpeechCapabilityReport { Voices = voices }.AssessRememberedVoice(Voice.Length == 0 ? null : Voice);
}

internal static class SpeechOptionsWire
{
    internal const string Kind = "System.Speech";
    internal const int Version = 1;

    internal static byte[] Serialize(WorkerMessage message)
    {
        string body = "{\"type\":" + Quote(message.Type) + ",\"gen\":" + message.Generation.ToString(CultureInfo.InvariantCulture);
        if (message.Type == WorkerProtocol.SpeakWithOptions) body += ",\"text\":" + Quote(message.Text!);
        else body += ",\"command\":" + message.Command.ToString(CultureInfo.InvariantCulture);
        if (message.Type != WorkerProtocol.QueryOptions)
        {
            SpeechOptionsSnapshot options = message.Options ?? throw new ArgumentException("Explicit speech options are required.");
            body += ",\"options\":{\"kind\":\"" + Kind + "\",\"version\":1,\"voice\":" + Quote(options.Voice) +
                ",\"rate\":" + options.Rate.ToString(CultureInfo.InvariantCulture) + ",\"volume\":" + options.Volume.ToString(CultureInfo.InvariantCulture) + "}";
        }
        byte[] bytes = SpeechCapabilities.Utf8.GetBytes(body + "}\n");
        if (bytes.Length > WorkerProtocol.MaxLineBytes) throw new ArgumentException("The message is too large.");
        Parse(SpeechCapabilities.Utf8.GetString(bytes));
        return bytes;
    }

    private static string Quote(string value)
    {
        SpeechCapabilities.Utf8.GetBytes(value);
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(string)).WriteObject(stream, value);
        return SpeechCapabilities.Utf8.GetString(stream.ToArray());
    }

    internal static WorkerMessage Parse(string line)
    {
        if (SpeechCapabilities.Utf8.GetByteCount(line) > WorkerProtocol.MaxLineBytes)
            throw new InvalidDataException("The speech option frame exceeded its byte bound.");
        var reader = new OptionsJson(line);
        WorkerMessage message = reader.Message();
        reader.End();
        return message;
    }

    // Deliberately confined to the new command/ack/query; legacy wire parsing stays unchanged.
    private sealed class OptionsJson
    {
        private readonly string text;
        private int position;
        internal OptionsJson(string text) { this.text = text; }
        private void Space() { while (position < text.Length && " \r\n\t".IndexOf(text[position]) >= 0) position++; }
        private bool Take(char value) { Space(); if (position == text.Length || text[position] != value) return false; position++; return true; }
        private void Need(char value) { if (!Take(value)) throw new InvalidDataException("Malformed speech options JSON."); }
        internal void End() { Space(); if (position != text.Length) throw new InvalidDataException("Trailing speech options data."); }
        private string String(int max)
        {
            Need('"'); var value = new StringBuilder(); bool ended = false;
            while (position < text.Length)
            {
                char c = text[position++];
                if (c == '"') { ended = true; break; }
                if (c < 32) throw new InvalidDataException("Unescaped JSON control character.");
                if (c == '\\')
                {
                    if (position == text.Length) throw new InvalidDataException("Incomplete JSON escape.");
                    c = text[position++];
                    switch (c)
                    {
                        case '"': case '\\': case '/': break;
                        case 'b': c = '\b'; break; case 'f': c = '\f'; break;
                        case 'n': c = '\n'; break; case 'r': c = '\r'; break; case 't': c = '\t'; break;
                        case 'u':
                            if (position + 4 > text.Length) throw new InvalidDataException("Incomplete Unicode escape.");
                            c = (char)int.Parse(text.Substring(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                            position += 4; break;
                        default: throw new InvalidDataException("Unknown JSON escape.");
                    }
                }
                value.Append(c);
                if (value.Length > max) throw new InvalidDataException("Speech options string exceeded its bound.");
            }
            if (!ended) throw new InvalidDataException("Incomplete JSON string.");
            string result = value.ToString(); SpeechCapabilities.Utf8.GetBytes(result); return result;
        }
        private long Integer()
        {
            Space(); int start = position;
            if (position < text.Length && text[position] == '-') position++;
            int digits = position;
            while (position < text.Length && text[position] >= '0' && text[position] <= '9') position++;
            if (position == digits || position - digits > 19 || (position - digits > 1 && text[digits] == '0'))
                throw new InvalidDataException("Invalid speech options integer.");
            return long.Parse(text.Substring(start, position - start), CultureInfo.InvariantCulture);
        }
        private HashSet<string> Object(Action<string> member)
        {
            Need('{'); var fields = new HashSet<string>(StringComparer.Ordinal);
            if (!Take('}'))
            {
                do
                {
                    string field = String(32);
                    if (!fields.Add(field)) throw new InvalidDataException("Duplicate speech options field.");
                    Need(':'); member(field);
                    if (Take('}')) break;
                    Need(',');
                } while (true);
            }
            return fields;
        }
        private SpeechOptionsSnapshot Options()
        {
            string? voice = null, kind = null; long version = 0, rate = 0, volume = 0;
            HashSet<string> fields = Object(field =>
            {
                switch (field)
                {
                    case "kind": kind = String(32); break;
                    case "version": version = Integer(); break;
                    case "voice": voice = String(SpeechCapabilities.MaxNameChars); break;
                    case "rate": rate = Integer(); break;
                    case "volume": volume = Integer(); break;
                    default: throw new InvalidDataException("Unknown speech options field.");
                }
            });
            if (fields.Count != 5 || kind != Kind || version != Version || rate < -10 || rate > 10 || volume < 0 || volume > 100)
                throw new InvalidDataException("Unsupported or incomplete speech options profile.");
            return new SpeechOptionsSnapshot(voice!, (int)rate, (int)volume);
        }
        internal WorkerMessage Message()
        {
            var message = new WorkerMessage();
            HashSet<string> fields = Object(field =>
            {
                switch (field)
                {
                    case "type": message.Type = String(32); break;
                    case "gen": message.Generation = Integer(); break;
                    case "command": message.Command = Integer(); break;
                    case "text": message.Text = String(WorkerProtocol.MaxSpeakChars); break;
                    case "options": message.Options = Options(); break;
                    default: throw new InvalidDataException("Unknown configured speech frame field.");
                }
            });
            bool speak = message.Type == WorkerProtocol.SpeakWithOptions;
            bool applied = message.Type == WorkerProtocol.OptionsApplied;
            bool query = message.Type == WorkerProtocol.QueryOptions;
            if ((!speak && !applied && !query) || message.Generation < 1 ||
                !fields.Contains("type") || !fields.Contains("gen") ||
                (speak ? fields.Count != 4 || !fields.Contains("text") || string.IsNullOrWhiteSpace(message.Text) || fields.Contains("command") :
                    !fields.Contains("command") || message.Command < (query ? 1 : 0) || fields.Contains("text") || fields.Count != (query ? 3 : 4)) ||
                (query ? fields.Contains("options") : message.Options == null))
                throw new InvalidDataException("Invalid configured speech command or evidence.");
            return message;
        }
    }
}
