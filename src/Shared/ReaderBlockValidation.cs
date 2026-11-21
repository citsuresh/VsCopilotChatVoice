#nullable disable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ReadAlongTrial
{
    internal static class ReaderBlockValidation
    {
        internal readonly struct TextToken
        {
            internal readonly string Text;
            internal readonly bool Bold;
            internal TextToken(string text, bool bold) { Text = text; Bold = bold; }
        }

        internal static void ValidateHeader(string id, string kind, string text)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("An anchor ID is required.");
            if (String.IsNullOrWhiteSpace(text)) throw new ArgumentException("Readable text is required in " + id);
            if (kind != "heading" && kind != "paragraph" && kind != "bullet" && kind != "step")
                throw new ArgumentException("Unsupported content kind: " + kind);
        }

        internal static void ValidateNumber(string kind, int number)
        {
            if (kind == "step" && number < 1) throw new ArgumentOutOfRangeException("number");
        }

        internal static IEnumerable<TextToken> ReadTokens(string id, string text)
        {
            bool bold = false;
            foreach (string part in Regex.Split(text, "(\\*\\*)"))
            {
                if (part == "**") { bold = !bold; continue; }
                foreach (Match token in Regex.Matches(part, "\\s+|\\S+"))
                    yield return new TextToken(token.Value, bold);
            }
            if (bold) throw new ArgumentException("Unclosed bold marker in " + id);
        }

        internal static void ValidateReadable(string id, bool readable)
        {
            if (!readable) throw new ArgumentException("Readable text is required in " + id);
        }

        internal static void AddSectionId(HashSet<string> ids, string id)
        {
            if (!ids.Add(id)) throw new ArgumentException("Duplicate reply anchor: " + id);
        }

        internal static void ValidateSectionCount(int detailed, int concise)
        {
            if (detailed != concise) throw new ArgumentException("Concise and Detailed must have matching section IDs.");
        }

        internal static void MatchSectionId(HashSet<string> ids, string id)
        {
            if (!ids.Remove(id)) throw new ArgumentException("Concise section has no matching Detailed section: " + id);
        }
    }
}
