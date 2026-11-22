#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace VsCopilotChatVoice.Speech;

[DataContract]
internal sealed class SpeechVoiceCapability
{
    [DataMember(Name = "name", IsRequired = true, Order = 0)] public string Name = "";
    [DataMember(Name = "culture", IsRequired = true, Order = 1)] public string Culture = "";
    [DataMember(Name = "enabled", IsRequired = true, Order = 2)] public bool Enabled;

    public override string ToString() => Name;
}

internal enum RememberedVoiceAvailability { NotSelected, Available, Missing, Disabled, Ambiguous }

[DataContract]
internal sealed class SpeechCapabilityReport
{
    [DataMember(Name = "schema", IsRequired = true, Order = 0)] public string Schema = SpeechCapabilities.Schema;
    [DataMember(Name = "version", IsRequired = true, Order = 1)] public int Version = SpeechCapabilities.Version;
    [DataMember(Name = "engine", IsRequired = true, Order = 2)] public string Engine = "System.Speech";
    [DataMember(Name = "voices", IsRequired = true, Order = 3)] public SpeechVoiceCapability[] Voices = Array.Empty<SpeechVoiceCapability>();
    [DataMember(Name = "defaultVoice", IsRequired = true, Order = 4)] public SpeechVoiceCapability? DefaultVoice;
    [DataMember(Name = "rateMin", IsRequired = true, Order = 5)] public int RateMin = -10;
    [DataMember(Name = "rateMax", IsRequired = true, Order = 6)] public int RateMax = 10;
    [DataMember(Name = "volumeMin", IsRequired = true, Order = 7)] public int VolumeMin;
    [DataMember(Name = "volumeMax", IsRequired = true, Order = 8)] public int VolumeMax = 100;
    [DataMember(Name = "pitchSupported", IsRequired = true, Order = 9)] public bool PitchSupported;

    // Assess intent only; never select or substitute the default voice.
    internal RememberedVoiceAvailability AssessRememberedVoice(string? name)
    {
        if (name == null) return RememberedVoiceAvailability.NotSelected;
        var matches = Voices.Where(v => string.Equals(v.Name, name, StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0) return RememberedVoiceAvailability.Missing;
        if (matches.Length > 1) return RememberedVoiceAvailability.Ambiguous;
        return matches[0].Enabled ? RememberedVoiceAvailability.Available : RememberedVoiceAvailability.Disabled;
    }
}

internal static class SpeechCapabilities
{
    internal const string Schema = "VsCopilotChatVoice.SpeechCapabilities";
    internal const int Version = 1;
    internal const int MaxBytes = 65536;
    internal const int MaxVoices = 64;
    internal const int MaxNameChars = 256;
    internal const int MaxCultureChars = 85;
    internal static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal static byte[] Serialize(SpeechCapabilityReport report)
    {
        Validate(report);
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(SpeechCapabilityReport)).WriteObject(stream, report);
        stream.WriteByte(10);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Speech capabilities exceeded the byte limit.");
        byte[] bytes = stream.ToArray();
        Parse(bytes);
        return bytes;
    }

    internal static SpeechCapabilityReport Parse(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length > MaxBytes)
            throw new InvalidDataException("Invalid speech capability byte count.");
        try
        {
            var parser = new CapabilityJson(Utf8.GetString(bytes));
            SpeechCapabilityReport report = parser.Report();
            parser.End();
            Validate(report);
            return report;
        }
        catch (Exception ex) when (ex is DecoderFallbackException || ex is EncoderFallbackException || ex is FormatException || ex is OverflowException)
        { throw new InvalidDataException("Invalid speech capability encoding or number.", ex); }
    }

    private static void Validate(SpeechCapabilityReport report)
    {
        if (report == null || report.Schema != Schema || report.Version != Version || report.Engine != "System.Speech" ||
            report.RateMin != -10 || report.RateMax != 10 || report.VolumeMin != 0 || report.VolumeMax != 100 || report.PitchSupported ||
            report.Voices == null || report.Voices.Length > MaxVoices)
            throw new InvalidDataException("Unsupported speech capability schema, version or bounds.");
        foreach (SpeechVoiceCapability voice in report.Voices) ValidateVoice(voice);
        if (report.DefaultVoice != null)
        {
            ValidateVoice(report.DefaultVoice);
            if (!report.DefaultVoice.Enabled || !report.Voices.Any(v => v.Enabled && v.Name == report.DefaultVoice.Name && v.Culture == report.DefaultVoice.Culture))
                throw new InvalidDataException("The default voice is not an enabled installed voice.");
        }
    }

    private static void ValidateVoice(SpeechVoiceCapability voice)
    {
        if (voice == null || string.IsNullOrWhiteSpace(voice.Name) || voice.Name.Length > MaxNameChars || voice.Name.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(voice.Culture) || voice.Culture.Length > MaxCultureChars || voice.Culture.Any(char.IsControl))
            throw new InvalidDataException("Invalid bounded speech voice metadata.");
        try
        {
            Utf8.GetBytes(voice.Name);
            if (CultureInfo.GetCultureInfo(voice.Culture).Name != voice.Culture)
                throw new InvalidDataException("Speech voice culture is not canonical.");
        }
        catch (ArgumentException ex) { throw new InvalidDataException("Invalid speech voice culture or Unicode.", ex); }
    }

    // A schema-specific reader avoids the framework reader's trailing-root/unknown-member tolerance.
    private sealed class CapabilityJson
    {
        private readonly string text;
        private int position;
        internal CapabilityJson(string text) { this.text = text; }
        private void Space() { while (position < text.Length && " \r\n\t".IndexOf(text[position]) >= 0) position++; }
        private bool Take(char value) { Space(); if (position == text.Length || text[position] != value) return false; position++; return true; }
        private void Need(char value) { if (!Take(value)) throw new InvalidDataException("Malformed speech capability JSON."); }
        internal void End() { Space(); if (position != text.Length) throw new InvalidDataException("Trailing speech capability data."); }
        private string String(int max)
        {
            Need('"');
            var value = new StringBuilder();
            bool ended = false;
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
                if (value.Length > max) throw new InvalidDataException("Speech capability string exceeded its bound.");
            }
            if (!ended) throw new InvalidDataException("Incomplete JSON string.");
            string result = value.ToString();
            Utf8.GetBytes(result);
            return result;
        }
        private bool Literal(string literal)
        {
            Space();
            if (position + literal.Length > text.Length || string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0) return false;
            position += literal.Length; return true;
        }
        private bool Bool()
        {
            if (Literal("true")) return true;
            if (Literal("false")) return false;
            throw new InvalidDataException("Expected a speech capability boolean.");
        }
        private int Integer()
        {
            Space(); int start = position;
            if (position < text.Length && text[position] == '-') position++;
            int digits = position;
            while (position < text.Length && text[position] >= '0' && text[position] <= '9') position++;
            if (position == digits || position - digits > 10 || (position - digits > 1 && text[digits] == '0'))
                throw new InvalidDataException("Invalid speech capability integer.");
            return int.Parse(text.Substring(start, position - start), CultureInfo.InvariantCulture);
        }
        private void Object(Action<string> member, int required)
        {
            Need('{'); var fields = new HashSet<string>(StringComparer.Ordinal);
            if (!Take('}'))
            {
                do
                {
                    string field = String(32);
                    if (!fields.Add(field)) throw new InvalidDataException("Duplicate speech capability field.");
                    Need(':'); member(field);
                    if (Take('}')) break;
                    Need(',');
                } while (true);
            }
            if (fields.Count != required) throw new InvalidDataException("Missing speech capability field.");
        }
        private SpeechVoiceCapability Voice()
        {
            var voice = new SpeechVoiceCapability();
            Object(field =>
            {
                switch (field)
                {
                    case "name": voice.Name = String(MaxNameChars); break;
                    case "culture": voice.Culture = String(MaxCultureChars); break;
                    case "enabled": voice.Enabled = Bool(); break;
                    default: throw new InvalidDataException("Unknown speech voice field.");
                }
            }, 3);
            return voice;
        }
        private SpeechVoiceCapability[] Voices()
        {
            Need('['); var voices = new List<SpeechVoiceCapability>();
            if (!Take(']'))
            {
                do
                {
                    if (voices.Count == MaxVoices) throw new InvalidDataException("Too many speech voices.");
                    voices.Add(Voice());
                    if (Take(']')) break;
                    Need(',');
                } while (true);
            }
            return voices.ToArray();
        }
        internal SpeechCapabilityReport Report()
        {
            var report = new SpeechCapabilityReport();
            Object(field =>
            {
                switch (field)
                {
                    case "schema": report.Schema = String(64); break;
                    case "version": report.Version = Integer(); break;
                    case "engine": report.Engine = String(32); break;
                    case "voices": report.Voices = Voices(); break;
                    case "defaultVoice": report.DefaultVoice = Literal("null") ? null : Voice(); break;
                    case "rateMin": report.RateMin = Integer(); break;
                    case "rateMax": report.RateMax = Integer(); break;
                    case "volumeMin": report.VolumeMin = Integer(); break;
                    case "volumeMax": report.VolumeMax = Integer(); break;
                    case "pitchSupported": report.PitchSupported = Bool(); break;
                    default: throw new InvalidDataException("Unknown speech capability field.");
                }
            }, 10);
            return report;
        }
    }
}
