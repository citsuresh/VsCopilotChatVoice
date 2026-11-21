using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using ReadAlongTrial;

internal static class ReaderModelRegressionTests
{
    private static int assertions;

    internal static int Run(string assemblyPath)
    {
        try
        {
            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
            foreach (string name in new[] { "WordAnchor", "Segment", "ReadingPosition", "ReadingNavigation", "ReplyBlock", "DocumentBuilder", "ReplyVersionPreference" })
            {
                Type type = assembly.GetType("ReadAlongTrial." + name, true);
                Check(type.IsPublic && type.Assembly == assembly, "The VSIX does not own the shared public type " + name);
            }
            Check(!assembly.GetReferencedAssemblies().Any(a => a.Name == "System.Speech" || a.Name == "VsCopilotChatVoice.Reader"),
                "The VSIX acquired a speech or standalone executable assembly dependency.");
            Type builder = assembly.GetType("ReadAlongTrial.DocumentBuilder", true);
            Type navigation = assembly.GetType("ReadAlongTrial.ReadingNavigation", true);
            Type preference = assembly.GetType("ReadAlongTrial.ReplyVersionPreference", true);
            Documents(builder, navigation);
            Preferences(preference);
            Console.WriteLine("PASS: shared reader model checks, " + assertions + " assertions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void Check(bool condition, string description)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(description);
    }

    private static object Invoke(Type type, string name, params object[] args)
    {
        return type.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
    }

    private static T Field<T>(object value, string name)
    {
        return (T)value.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance).GetValue(value);
    }

    private static IList Load(Type builder, string json)
    {
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            return (IList)Invoke(builder, "LoadReply", new FlowDocument(), input);
    }

    private static List<Segment> LoadStandalone(string json)
    {
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            return DocumentBuilder.LoadReply(new FlowDocument(), input);
    }

    private static void Compare(List<Segment> expected, IList actual)
    {
        Check(actual.Count == expected.Count, "Shared rendering changed the block count.");
        for (int i = 0; i < expected.Count; i++)
        {
            Segment segment = expected[i];
            object native = actual[i];
            Check(Field<string>(native, "Id") == segment.Id && Field<string>(native, "Spoken") == segment.Spoken,
                "Shared rendering changed section identity or speech text.");
            Check(Field<List<int>>(native, "SentenceStarts").SequenceEqual(segment.SentenceStarts), "Sentence boundaries changed.");
            Paragraph paragraph = Field<Paragraph>(native, "Paragraph");
            Check(paragraph.Margin == segment.Paragraph.Margin && paragraph.FontWeight == segment.Paragraph.FontWeight &&
                paragraph.LineHeight == segment.Paragraph.LineHeight, "Shared formatting changed.");
            IList words = Field<IList>(native, "Words");
            Check(words.Count == segment.Words.Count, "Shared rendering changed the word count.");
            for (int w = 0; w < words.Count; w++)
            {
                WordAnchor word = segment.Words[w];
                Run run = Field<Run>(words[w], "Display");
                Check(Field<int>(words[w], "Start") == word.Start && Field<int>(words[w], "Length") == word.Length &&
                    run.Text == word.Display.Text && run.FontWeight == word.Display.FontWeight, "Shared word/format mapping changed.");
            }
        }
    }

    private static void Documents(Type builder, Type navigation)
    {
        Compare(DocumentBuilder.Sample(new FlowDocument()), (IList)Invoke(builder, "Sample", new FlowDocument()));
        const string detailed = "[{\"id\":\"a\",\"kind\":\"paragraph\",\"text\":\"Dr. Smith reads **bold words**. Next sentence.\"}," +
            "{\"id\":\"b\",\"kind\":\"step\",\"text\":\"First line. Next line.\",\"number\":2}]";
        const string concise = "[{\"id\":\"b\",\"kind\":\"step\",\"text\":\"Brief step.\",\"number\":2}," +
            "{\"id\":\"a\",\"kind\":\"paragraph\",\"text\":\"Brief paragraph.\"}]";
        IList native = Load(builder, detailed), brief = Load(builder, concise);
        Compare(LoadStandalone(detailed), native);
        Compare(LoadStandalone(concise), brief);
        Invoke(builder, "ValidateVersions", native, brief);
        Check(Field<string>(brief[0], "Id") == "b", "Reordered paired versions lost their section ID.");
        Check(Field<string>(native[1], "Spoken") == "Step 2. First line. Next line.", "The step's spoken prefix changed.");
        IList words = Field<IList>(native[1], "Words");
        Check(Field<int>(words[0], "Start") == 0 && Field<int>(words[0], "Length") == 8 &&
            Field<Run>(words[0], "Display").Text == "2. ", "The spoken prefix no longer maps to the displayed step marker.");
        Check(Field<List<int>>(native[0], "SentenceStarts").Count == 2, "The abbreviation created an extra sentence.");
        Check(Field<List<int>>(native[1], "SentenceStarts").SequenceEqual(new[] { 0, 20 }), "Step sentence offsets include its prefix as a sentence.");
        Type positionType = navigation.Assembly.GetType("ReadAlongTrial.ReadingPosition", true);
        object position = Activator.CreateInstance(positionType, new object[] { 0, 0 });
        object next = Invoke(navigation, "Move", native, position, 1, true);
        Check(Field<int>(next, "Segment") == 1 && Field<int>(next, "Offset") == 0, "Paragraph navigation changed.");
        object last = Invoke(navigation, "Move", native, next, 1, false);
        Check(Field<int>(last, "Segment") == 1 && Field<int>(last, "Offset") == 20, "Sentence navigation changed.");
        object boundary = Invoke(navigation, "Move", native, last, 1, false);
        Check(Field<int>(boundary, "Segment") == 1 && Field<int>(boundary, "Offset") == 20, "End navigation did not retain position.");
        object selected = Invoke(navigation, "FromTextPointer", native, Field<Run>(words[1], "Display").ContentStart);
        Check(selected != null && Field<int>(selected, "Segment") == 1 && Field<int>(selected, "Offset") == 8, "Clicked-word mapping changed.");
        Check(Invoke(navigation, "FromTextPointer", native, null) == null, "Missing text pointer acquired a position.");
        Reject(() => Invoke(builder, "ValidateVersions", native, Load(builder, "[{\"id\":\"missing\",\"kind\":\"paragraph\",\"text\":\"x\"}]")), typeof(ArgumentException));
        Reject(() => Invoke(builder, "ValidateVersions", native, Load(builder, "[{\"id\":\"a\",\"kind\":\"paragraph\",\"text\":\"x\"},{\"id\":\"missing\",\"kind\":\"paragraph\",\"text\":\"y\"}]")), typeof(ArgumentException));
        foreach (string invalid in new[] { "[]", "[null]", detailed + "[]", "[{\"id\":\"x\",\"kind\":\"code\",\"text\":\"x\"}]",
            "[{\"id\":\"x\",\"kind\":\"paragraph\",\"text\":\"**broken\"}]", "[{\"id\":\"x\",\"kind\":\"step\",\"text\":\"x\",\"number\":0}]",
            "[{\"id\":\"x\",\"kind\":\"paragraph\",\"text\":\"x\"},{\"id\":\"x\",\"kind\":\"paragraph\",\"text\":\"y\"}]" })
        {
            Type expected = invalid.Contains("\"number\":0") ? typeof(ArgumentOutOfRangeException) : typeof(ArgumentException);
            Reject(() => Load(builder, invalid), expected);
            Reject(() => LoadStandalone(invalid), expected);
        }
    }

    private static void Reject(Action action, Type expected)
    {
        try { action(); }
        catch (TargetInvocationException ex)
        {
            Check(ex.InnerException != null && ex.InnerException.GetType() == expected, "Unexpected reflected refusal: " + ex);
            return;
        }
        catch (Exception ex) when (ex.GetType() == expected)
        {
            Check(true, "Expected refusal.");
            return;
        }
        throw new InvalidOperationException("Expected refusal was not raised: " + expected.Name);
    }

    private static void Preferences(Type preference)
    {
        string directory = Path.Combine(Path.GetTempPath(), "VoiceReaderModel-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "mode.txt");
        try
        {
            Check(!(bool)Invoke(preference, "Load", path) && !Directory.Exists(directory), "Loading a missing preference changed the default or created files.");
            Invoke(preference, "Save", path, true);
            Check(File.ReadAllText(path) == "Concise" && ReplyVersionPreference.Load(path), "The native choice is not readable by the standalone reader.");
            ReplyVersionPreference.Save(path, false);
            Check(File.ReadAllText(path) == "Detailed" && !(bool)Invoke(preference, "Load", path), "The standalone choice is not readable by the native model.");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Atomic preference save left temporary files.");
            File.WriteAllText(path, "invalid");
            Reject(() => Invoke(preference, "Load", path), typeof(InvalidDataException));
            Check(File.ReadAllText(path) == "invalid", "Loading invalid preferences rewrote them.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
}