#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ReadAlongTrial
{
    public sealed class WordAnchor
    {
        public int Start;
        public int Length;
        public Run Display;
    }

    public sealed class Segment
    {
        public string Id;
        public string Spoken;
        public Paragraph Paragraph;
        public readonly List<WordAnchor> Words = new List<WordAnchor>();
        public readonly List<int> SentenceStarts = new List<int>();

        public WordAnchor FindWord(int position)
        {
            foreach (WordAnchor word in Words)
                if (position >= word.Start && position < word.Start + word.Length)
                    return word;
            return null;
        }
    }

    public struct ReadingPosition
    {
        public readonly int Segment;
        public readonly int Offset;
        public ReadingPosition(int segment, int offset) { Segment = segment; Offset = offset; }
    }

    public static class ReadingNavigation
    {
        public static ReadingPosition Move(List<Segment> segments, ReadingPosition current, int direction, bool paragraph)
        {
            if (direction != -1 && direction != 1) throw new ArgumentOutOfRangeException("direction");
            if (current.Segment < 0 || current.Segment >= segments.Count) throw new ArgumentOutOfRangeException("current");
            Segment segment = segments[current.Segment];
            if (current.Offset < 0 || current.Offset >= segment.Spoken.Length) throw new ArgumentOutOfRangeException("current");
            if (paragraph)
            {
                int target = current.Segment + direction;
                return target < 0 || target >= segments.Count ? current : new ReadingPosition(target, 0);
            }
            int sentence = 0;
            while (sentence + 1 < segment.SentenceStarts.Count && segment.SentenceStarts[sentence + 1] <= current.Offset) sentence++;
            int next = sentence + direction;
            if (next >= 0 && next < segment.SentenceStarts.Count)
                return new ReadingPosition(current.Segment, segment.SentenceStarts[next]);
            int adjacent = current.Segment + direction;
            if (adjacent < 0 || adjacent >= segments.Count) return current;
            List<int> starts = segments[adjacent].SentenceStarts;
            return new ReadingPosition(adjacent, direction > 0 ? 0 : starts[starts.Count - 1]);
        }

        public static ReadingPosition? FromTextPointer(List<Segment> segments, TextPointer pointer)
        {
            if (pointer == null) return null;
            for (int i = 0; i < segments.Count; i++)
            {
                Segment segment = segments[i];
                if (pointer.CompareTo(segment.Paragraph.ContentStart) < 0 || pointer.CompareTo(segment.Paragraph.ContentEnd) > 0) continue;
                foreach (WordAnchor word in segment.Words)
                    if (pointer.CompareTo(word.Display.ContentEnd) <= 0) return new ReadingPosition(i, word.Start);
                if (segment.Words.Count > 0) return new ReadingPosition(i, segment.Words[segment.Words.Count - 1].Start);
            }
            return null;
        }
    }

    [DataContract]
    public sealed class ReplyBlock
    {
        [DataMember(Name = "id", IsRequired = true)] public string Id;
        [DataMember(Name = "kind", IsRequired = true)] public string Kind;
        [DataMember(Name = "text", IsRequired = true)] public string Text;
        [DataMember(Name = "number")] public int Number;
    }

    public static class DocumentBuilder
    {
        public static void ValidateVersions(List<Segment> detailed, List<Segment> concise)
        {
            if (detailed.Count != concise.Count) throw new ArgumentException("Concise and Detailed must have matching section IDs.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Segment segment in detailed) ids.Add(segment.Id);
            foreach (Segment segment in concise)
                if (!ids.Remove(segment.Id)) throw new ArgumentException("Concise section has no matching Detailed section: " + segment.Id);
        }

        public static List<Segment> LoadReply(FlowDocument document, Stream input)
        {
            byte[] data;
            using (MemoryStream buffer = new MemoryStream()) { input.CopyTo(buffer); data = buffer.ToArray(); }
            Encoding encoding = data.Length > 1 && data[0] == 0 ? Encoding.BigEndianUnicode :
                data.Length > 1 && data[1] == 0 ? Encoding.Unicode : Encoding.UTF8;
            // The contract reader validates JSON syntax but ignores trailing content.
            ValidateReplyBoundary(encoding.GetString(data));
            List<ReplyBlock> blocks;
            using (MemoryStream validated = new MemoryStream(data))
                blocks = (List<ReplyBlock>)new DataContractJsonSerializer(typeof(List<ReplyBlock>)).ReadObject(validated);
            if (blocks == null || blocks.Count == 0) throw new ArgumentException("The reply must contain readable blocks.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            List<Segment> segments = new List<Segment>();
            foreach (ReplyBlock block in blocks)
            {
                if (block == null) throw new ArgumentException("A reply block cannot be null.");
                if (!ids.Add(block.Id)) throw new ArgumentException("Duplicate reply anchor: " + block.Id);
                segments.Add(Add(document, block.Id, block.Kind, block.Text, block.Number));
            }
            return segments;
        }

        private static void ValidateReplyBoundary(string json)
        {
            bool started = false, complete = false, quoted = false, escaped = false;
            int depth = 0;
            foreach (char value in json)
            {
                if (!started || complete)
                {
                    if (value == ' ' || value == '\t' || value == '\r' || value == '\n') continue;
                    if (complete) throw new ArgumentException("Unexpected content after the reply array.");
                    if (value != '[') throw new ArgumentException("The reply must be a JSON array.");
                    started = true; depth = 1; continue;
                }
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (value == '\\') escaped = true;
                    else if (value == '"') quoted = false;
                    continue;
                }
                if (value == '"') quoted = true;
                else if (value == '[' || value == '{') depth++;
                else if (value == ']' || value == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        if (value != ']') throw new ArgumentException("The reply array is not closed correctly.");
                        complete = true;
                    }
                }
            }
            if (!complete) throw new ArgumentException("The reply must contain one complete JSON array.");
        }

        public static Segment Add(FlowDocument document, string id, string kind, string text, int number)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("An anchor ID is required.");
            if (String.IsNullOrWhiteSpace(text)) throw new ArgumentException("Readable text is required in " + id);
            if (kind != "heading" && kind != "paragraph" && kind != "bullet" && kind != "step")
                throw new ArgumentException("Unsupported content kind: " + kind);
            Segment segment = new Segment { Id = id, Paragraph = new Paragraph() };
            Paragraph paragraph = segment.Paragraph;
            paragraph.Margin = new Thickness(0, kind == "heading" ? 10 : 0, 0, 9);
            paragraph.LineHeight = 18;
            if (kind == "heading") { paragraph.FontSize = 14; paragraph.FontWeight = FontWeights.Bold; }
            string spokenPrefix = "";
            if (kind == "bullet" || kind == "step")
            {
                if (kind == "step" && number < 1) throw new ArgumentOutOfRangeException("number");
                string marker = kind == "step" ? number.ToString(CultureInfo.InvariantCulture) + ". " : "\u2022 ";
                Run markerRun = new Run(marker) { Foreground = Brushes.SteelBlue, FontWeight = FontWeights.Bold };
                paragraph.Inlines.Add(markerRun);
                paragraph.Margin = new Thickness(14, 0, 0, 8);
                if (kind == "step")
                {
                    spokenPrefix = "Step " + number.ToString(CultureInfo.InvariantCulture) + ". ";
                    segment.Words.Add(new WordAnchor { Start = 0, Length = spokenPrefix.Length, Display = markerRun });
                }
            }
            StringBuilder spoken = new StringBuilder(spokenPrefix);
            bool bold = false;
            foreach (string part in Regex.Split(text, "(\\*\\*)"))
            {
                if (part == "**") { bold = !bold; continue; }
                foreach (Match token in Regex.Matches(part, "\\s+|\\S+"))
                {
                    Run run = new Run(token.Value);
                    if (bold) run.FontWeight = FontWeights.Bold;
                    paragraph.Inlines.Add(run);
                    if (!String.IsNullOrWhiteSpace(token.Value))
                        segment.Words.Add(new WordAnchor { Start = spoken.Length, Length = token.Length, Display = run });
                    spoken.Append(token.Value);
                }
            }
            if (bold) throw new ArgumentException("Unclosed bold marker in " + id);
            segment.Spoken = spoken.ToString();
            if (segment.Words.Count == 0) throw new ArgumentException("Readable text is required in " + id);
            segment.SentenceStarts.Add(0);
            // Split the body only: the spoken "Step N." prefix belongs to its first sentence.
            foreach (Match ending in Regex.Matches(segment.Spoken.Substring(spokenPrefix.Length), "[.!?]+[\"'\u2019\u201D)\\]]*(?=\\s|$)"))
            {
                int end = spokenPrefix.Length + ending.Index + ending.Length;
                string before = segment.Spoken.Substring(spokenPrefix.Length, ending.Index + ending.Length);
                if (Regex.IsMatch(before, "\\b(?:Mr|Mrs|Ms|Dr|Prof|Sr|Jr|St|vs|e\\.g|i\\.e)\\.$", RegexOptions.IgnoreCase)) continue;
                foreach (WordAnchor word in segment.Words)
                {
                    if (word.Start < end) continue;
                    if (segment.SentenceStarts[segment.SentenceStarts.Count - 1] != word.Start) segment.SentenceStarts.Add(word.Start);
                    break;
                }
            }
            document.Blocks.Add(paragraph);
            return segment;
        }

        public static List<Segment> Sample(FlowDocument document)
        {
            List<Segment> segments = new List<Segment>();
            segments.Add(Add(document, "intro-title", "heading", "A voice conversation, with room to read", 0));
            segments.Add(Add(document, "intro", "paragraph", "This is a disposable read-along experiment. The panel owns its text, speech, highlighting, and scrolling. Copilot Chat can continue updating independently.", 0));
            segments.Add(Add(document, "watch-title", "heading", "What to watch", 0));
            segments.Add(Add(document, "watch-1", "bullet", "The highlighted word should follow the voice, including **emphasized words**.", 0));
            segments.Add(Add(document, "watch-2", "bullet", "Each bullet has its own anchor. Moving to a new item must not highlight an unrelated sentence.", 0));
            segments.Add(Add(document, "watch-3", "bullet", "Paragraph spacing should make the content comfortable to scan while listening.", 0));
            segments.Add(Add(document, "steps-title", "heading", "Try the controls", 0));
            segments.Add(Add(document, "step-1", "step", "Press the blue Pause button. The speech and highlighted position should stop together. The same button becomes Resume. Press it to continue.", 1));
            segments.Add(Add(document, "step-2", "step", "Scroll upward with your mouse wheel. Speech continues, but the panel stops following automatically.", 2));
            segments.Add(Add(document, "step-3", "step", "Press Follow speech. The panel returns to the current spoken word, not the beginning of the reply.", 3));
            segments.Add(Add(document, "step-4", "step", "Press Stop to keep your place. Use the sentence arrows to skip or repeat. Paragraph arrows visit headings and list items too. Click a word to choose a starting point, then press Play.", 4));
            segments.Add(Add(document, "scroll-title", "heading", "An independent viewport", 0));
            segments.Add(Add(document, "scroll-body", "paragraph", "By now the sample is longer than the panel. Automatic scrolling should keep the active word visible without selecting text or taking keyboard focus from your editor.", 0));
            segments.Add(Add(document, "limits-title", "heading", "Prototype boundaries", 0));
            segments.Add(Add(document, "limits", "paragraph", "This first part does not mirror chat, process tables, or receive new messages. Those features are not silently simulated. We are testing the reading experience first.", 0));
            segments.Add(Add(document, "finish", "paragraph", "That is the end of the sample. Tell me whether the timing, formatting, and scrolling feel comfortable.", 0));
            return segments;
        }
    }

    public static class ReplyVersionPreference
    {
        public static bool Load(string path)
        {
            string value;
            try { value = File.ReadAllText(path, Encoding.UTF8).Trim(); }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            if (value == "Concise") return true;
            if (value == "Detailed") return false;
            throw new InvalidDataException("Saved reading mode must be Concise or Detailed.");
        }

        public static void Save(string path, bool concise)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(pending, concise ? "Concise" : "Detailed", new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
            }
            finally { File.Delete(pending); }
        }
    }
}