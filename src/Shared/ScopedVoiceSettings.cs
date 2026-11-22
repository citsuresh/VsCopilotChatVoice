#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using ReadAlongTrial;

namespace VsCopilotChatVoice.Settings;

internal enum SettingOrigin { BuiltIn, Global, Solution, Window }
internal enum VoiceSetting { SpeakerEnabled, Concise, VoiceName, Rate, Volume, AudioPolicy, DuckPercent, Theme, FontSize }

internal readonly struct EffectiveSetting<T>
{
    internal T Value { get; }
    internal SettingOrigin Origin { get; }
    internal EffectiveSetting(T value, SettingOrigin origin) { Value = value; Origin = origin; }
}

internal sealed class VoiceOverrides
{
    internal bool? SpeakerEnabled { get; }
    internal bool? Concise { get; }
    internal string? VoiceName { get; }
    internal int? Rate { get; }
    internal int? Volume { get; }
    internal int? AudioPolicy { get; }
    internal int? DuckPercent { get; }
    internal int? Theme { get; }
    internal int? FontSize { get; }

    internal VoiceOverrides(bool? speakerEnabled = null, bool? concise = null,
        string? voiceName = null, int? rate = null, int? volume = null, int? audioPolicy = null,
        int? duckPercent = null, int? theme = null, int? fontSize = null)
    {
        if (voiceName != null && (voiceName.Length > 256 || voiceName.Any(char.IsControl) ||
            !string.Equals(voiceName, voiceName.Trim(), StringComparison.Ordinal)))
            throw new ArgumentException("Invalid speech voice name.", nameof(voiceName));
        if (rate.HasValue && (rate.Value < -10 || rate.Value > 10))
            throw new ArgumentOutOfRangeException(nameof(rate));
        if (volume.HasValue && (volume.Value < 0 || volume.Value > 100))
            throw new ArgumentOutOfRangeException(nameof(volume));
        if (audioPolicy.HasValue && (audioPolicy.Value < 0 || audioPolicy.Value > 2))
            throw new ArgumentOutOfRangeException(nameof(audioPolicy));
        if (duckPercent.HasValue && (duckPercent.Value < 0 || duckPercent.Value > 100))
            throw new ArgumentOutOfRangeException(nameof(duckPercent));
        if (theme.HasValue && (theme.Value < 0 || theme.Value > 3))
            throw new ArgumentOutOfRangeException(nameof(theme));
        if (fontSize.HasValue && (fontSize.Value < 10 || fontSize.Value > 28))
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        SpeakerEnabled = speakerEnabled;
        Concise = concise;
        VoiceName = voiceName;
        Rate = rate;
        Volume = volume;
        AudioPolicy = audioPolicy;
        DuckPercent = duckPercent;
        Theme = theme;
        FontSize = fontSize;
    }
}

internal sealed class EffectiveVoiceSettings
{
    internal EffectiveSetting<bool> SpeakerEnabled { get; }
    internal EffectiveSetting<bool> Concise { get; }
    internal EffectiveSetting<string> VoiceName { get; }
    internal EffectiveSetting<int> Rate { get; }
    internal EffectiveSetting<int> Volume { get; }
    internal EffectiveSetting<int> AudioPolicy { get; }
    internal EffectiveSetting<int> DuckPercent { get; }
    internal EffectiveSetting<int> Theme { get; }
    internal EffectiveSetting<int> FontSize { get; }

    internal EffectiveVoiceSettings(VoiceOverrides global, VoiceOverrides solution, VoiceOverrides window)
    {
        if (global == null) throw new ArgumentNullException(nameof(global));
        if (solution == null) throw new ArgumentNullException(nameof(solution));
        if (window == null) throw new ArgumentNullException(nameof(window));
        SpeakerEnabled = Resolve(false, global.SpeakerEnabled, solution.SpeakerEnabled, window.SpeakerEnabled);
        Concise = Resolve(false, global.Concise, solution.Concise, window.Concise);
        Rate = Resolve(0, global.Rate, solution.Rate, window.Rate);
        Volume = Resolve(100, global.Volume, solution.Volume, window.Volume);
        AudioPolicy = Resolve(0, global.AudioPolicy, solution.AudioPolicy, window.AudioPolicy);
        DuckPercent = Resolve(25, global.DuckPercent, solution.DuckPercent, window.DuckPercent);
        Theme = Resolve(0, global.Theme, solution.Theme, window.Theme);
        FontSize = Resolve(13, global.FontSize, solution.FontSize, window.FontSize);
        VoiceName = window.VoiceName != null ? new EffectiveSetting<string>(window.VoiceName, SettingOrigin.Window) :
            solution.VoiceName != null ? new EffectiveSetting<string>(solution.VoiceName, SettingOrigin.Solution) :
            global.VoiceName != null ? new EffectiveSetting<string>(global.VoiceName, SettingOrigin.Global) :
            new EffectiveSetting<string>("", SettingOrigin.BuiltIn);
    }

    private static EffectiveSetting<T> Resolve<T>(T baseline, T? global, T? solution, T? window) where T : struct =>
        window.HasValue ? new EffectiveSetting<T>(window.Value, SettingOrigin.Window) :
        solution.HasValue ? new EffectiveSetting<T>(solution.Value, SettingOrigin.Solution) :
        global.HasValue ? new EffectiveSetting<T>(global.Value, SettingOrigin.Global) :
        new EffectiveSetting<T>(baseline, SettingOrigin.BuiltIn);
}

internal sealed class VoiceSettingsPatch
{
    private readonly VoiceOverrides values;
    private readonly HashSet<VoiceSetting> fields;

    internal VoiceSettingsPatch(VoiceOverrides values, IEnumerable<VoiceSetting> fields)
    {
        this.values = values ?? throw new ArgumentNullException(nameof(values));
        if (fields == null) throw new ArgumentNullException(nameof(fields));
        this.fields = new HashSet<VoiceSetting>();
        foreach (VoiceSetting field in fields)
            if (!Enum.IsDefined(typeof(VoiceSetting), field) || !this.fields.Add(field))
                throw new ArgumentException("Unknown or repeated settings field.", nameof(fields));
    }

    internal VoiceOverrides Apply(VoiceOverrides current)
    {
        if (current == null) throw new ArgumentNullException(nameof(current));
        return new VoiceOverrides(
            fields.Contains(VoiceSetting.SpeakerEnabled) ? values.SpeakerEnabled : current.SpeakerEnabled,
            fields.Contains(VoiceSetting.Concise) ? values.Concise : current.Concise,
            fields.Contains(VoiceSetting.VoiceName) ? values.VoiceName : current.VoiceName,
            fields.Contains(VoiceSetting.Rate) ? values.Rate : current.Rate,
            fields.Contains(VoiceSetting.Volume) ? values.Volume : current.Volume,
            fields.Contains(VoiceSetting.AudioPolicy) ? values.AudioPolicy : current.AudioPolicy,
            fields.Contains(VoiceSetting.DuckPercent) ? values.DuckPercent : current.DuckPercent,
            fields.Contains(VoiceSetting.Theme) ? values.Theme : current.Theme,
            fields.Contains(VoiceSetting.FontSize) ? values.FontSize : current.FontSize);
    }
}

internal sealed class VoiceScopeSnapshot
{
    internal long Revision { get; }
    internal VoiceOverrides Values { get; }
    internal VoiceScopeSnapshot(long revision, VoiceOverrides values)
    {
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        Revision = revision;
        Values = values ?? throw new ArgumentNullException(nameof(values));
    }
}

internal sealed class ScopedSettingsStore
{
    private readonly string root;
    private readonly string legacyPath;
    private static readonly string[] Attributes = { "version", "revision", "speaker", "concise", "voice", "rate", "volume", "audioPolicy", "duckPercent", "theme", "fontSize" };
    internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VsCopilotChatVoice", "Settings");
    internal static string DefaultLegacyPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatVoice", "reader-version.txt");

    internal ScopedSettingsStore(string root, string legacyPath)
    {
        SettingsIdentity.CanonicalPath(root);
        SettingsIdentity.CanonicalPath(legacyPath);
        this.root = Path.GetFullPath(root);
        this.legacyPath = Path.GetFullPath(legacyPath);
    }

    private string GlobalPath => Path.Combine(root, "global.xml");
    private string SolutionPath(string solution) => Path.Combine(root, "solution-" + SettingsIdentity.SolutionKey(solution) + ".xml");

    private FileStream Acquire(string path)
    {
        Directory.CreateDirectory(root);
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) == 32 || (ex.HResult & 0xffff) == 33)
            {
                if (deadline.Elapsed >= TimeSpan.FromSeconds(5))
                    throw new TimeoutException("Another settings update did not release its file lock.", ex);
                Thread.Sleep(10);
            }
        }
    }

    internal VoiceScopeSnapshot ReadGlobal()
    {
        using var held = Acquire(GlobalPath);
        VoiceScopeSnapshot current = Read(GlobalPath);
        if (current.Revision != 0) return current;
        var imported = new VoiceScopeSnapshot(1, new VoiceOverrides(concise: ReplyVersionPreference.Load(legacyPath)));
        Save(GlobalPath, imported);
        return imported;
    }

    internal VoiceScopeSnapshot ReadSolution(string solution)
    {
        string path = SolutionPath(solution);
        using var held = Acquire(path);
        return Read(path);
    }

    internal VoiceScopeSnapshot UpdateGlobal(VoiceSettingsPatch patch) => Update(GlobalPath, patch, true);
    internal VoiceScopeSnapshot UpdateSolution(string solution, VoiceSettingsPatch patch) => Update(SolutionPath(solution), patch, false);

    private VoiceScopeSnapshot Update(string path, VoiceSettingsPatch patch, bool global)
    {
        if (patch == null) throw new ArgumentNullException(nameof(patch));
        using var held = Acquire(path);
        VoiceScopeSnapshot current = Read(path);
        VoiceOverrides values = current.Revision == 0 && global ?
            new VoiceOverrides(concise: ReplyVersionPreference.Load(legacyPath)) : current.Values;
        var updated = new VoiceScopeSnapshot(checked(current.Revision + 1), patch.Apply(values));
        Save(path, updated);
        return updated;
    }

    internal EffectiveVoiceSettings Load(string? solution, VoiceOverrides window)
    {
        if (window == null) throw new ArgumentNullException(nameof(window));
        VoiceOverrides global = ReadGlobal().Values;
        return new EffectiveVoiceSettings(global, solution == null ? new VoiceOverrides() : ReadSolution(solution).Values, window);
    }

    private static VoiceScopeSnapshot Read(string path)
    {
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        catch (FileNotFoundException) { return new VoiceScopeSnapshot(0, new VoiceOverrides()); }
        catch (DirectoryNotFoundException) { return new VoiceScopeSnapshot(0, new VoiceOverrides()); }
        using (stream)
        {
            if (stream.Length > 16384) throw new InvalidDataException("Settings file exceeds its size limit.");
            try
            {
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4096
                });
                if (reader.MoveToContent() != XmlNodeType.Element || reader.Name != "voiceSettings" || reader.NamespaceURI.Length != 0)
                    throw new InvalidDataException("Expected scoped voice settings.");
                for (int i = 0; i < reader.AttributeCount; i++)
                {
                    reader.MoveToAttribute(i);
                    if (reader.NamespaceURI.Length != 0 || !Attributes.Contains(reader.Name))
                        throw new InvalidDataException("Unknown settings attribute.");
                }
                reader.MoveToElement();
                if (reader.GetAttribute("version") != "1") throw new InvalidDataException("Unsupported settings schema version.");
                long revision = XmlConvert.ToInt64(reader.GetAttribute("revision") ?? throw new InvalidDataException("Missing settings revision."));
                if (revision < 1) throw new InvalidDataException("Invalid settings revision.");
                string? speaker = reader.GetAttribute("speaker"), concise = reader.GetAttribute("concise");
                string? rate = reader.GetAttribute("rate"), volume = reader.GetAttribute("volume"), audioPolicy = reader.GetAttribute("audioPolicy"),
                    duckPercent = reader.GetAttribute("duckPercent"), theme = reader.GetAttribute("theme"), fontSize = reader.GetAttribute("fontSize");
                var values = new VoiceOverrides(speaker == null ? null : XmlConvert.ToBoolean(speaker),
                    concise == null ? null : XmlConvert.ToBoolean(concise), reader.GetAttribute("voice"),
                    rate == null ? null : XmlConvert.ToInt32(rate), volume == null ? null : XmlConvert.ToInt32(volume),
                    audioPolicy == null ? null : XmlConvert.ToInt32(audioPolicy), duckPercent == null ? null : XmlConvert.ToInt32(duckPercent),
                    theme == null ? null : XmlConvert.ToInt32(theme), fontSize == null ? null : XmlConvert.ToInt32(fontSize));
                bool empty = reader.IsEmptyElement;
                reader.ReadStartElement();
                if (!empty) reader.ReadEndElement();
                if (reader.MoveToContent() != XmlNodeType.None) throw new InvalidDataException("Trailing settings data.");
                return new VoiceScopeSnapshot(revision, values);
            }
            catch (Exception ex) when (ex is XmlException || ex is FormatException || ex is ArgumentException || ex is OverflowException)
            {
                throw new InvalidDataException("Malformed scoped voice settings.", ex);
            }
        }
    }

    private static void Save(string path, VoiceScopeSnapshot snapshot)
    {
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Exception? failure = null;
        bool created = false;
        try
        {
            try
            {
                using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    created = true;
                    using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false, true), CloseOutput = false });
                    writer.WriteStartElement("voiceSettings");
                    writer.WriteAttributeString("version", "1");
                    writer.WriteAttributeString("revision", snapshot.Revision.ToString(CultureInfo.InvariantCulture));
                    VoiceOverrides values = snapshot.Values;
                    if (values.SpeakerEnabled.HasValue) writer.WriteAttributeString("speaker", XmlConvert.ToString(values.SpeakerEnabled.Value));
                    if (values.Concise.HasValue) writer.WriteAttributeString("concise", XmlConvert.ToString(values.Concise.Value));
                    if (values.VoiceName != null) writer.WriteAttributeString("voice", values.VoiceName);
                    if (values.Rate.HasValue) writer.WriteAttributeString("rate", XmlConvert.ToString(values.Rate.Value));
                    if (values.Volume.HasValue) writer.WriteAttributeString("volume", XmlConvert.ToString(values.Volume.Value));
                    if (values.AudioPolicy.HasValue) writer.WriteAttributeString("audioPolicy", XmlConvert.ToString(values.AudioPolicy.Value));
                    if (values.DuckPercent.HasValue) writer.WriteAttributeString("duckPercent", XmlConvert.ToString(values.DuckPercent.Value));
                    if (values.Theme.HasValue) writer.WriteAttributeString("theme", XmlConvert.ToString(values.Theme.Value));
                    if (values.FontSize.HasValue) writer.WriteAttributeString("fontSize", XmlConvert.ToString(values.FontSize.Value));
                    writer.WriteEndElement();
                    writer.Flush();
                    stream.Flush(true);
                }
                AtomicFileReplace.Publish(pending, path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException || ex is ArgumentException)
            {
                failure = ex;
                throw;
            }
        }
        finally
        {
            try { if (created) File.Delete(pending); }
            catch (Exception cleanup) when (cleanup is IOException || cleanup is UnauthorizedAccessException)
            {
                if (failure != null) throw new AggregateException("Settings save and temporary-file cleanup failed.", failure, cleanup);
                throw;
            }
        }
    }

    internal IDisposable Watch(string? solution, Action<bool> changed, Action<Exception> failed)
    {
        if (changed == null) throw new ArgumentNullException(nameof(changed));
        if (failed == null) throw new ArgumentNullException(nameof(failed));
        string? solutionName = solution == null ? null : Path.GetFileName(SolutionPath(solution));
        Directory.CreateDirectory(root);
        return new SettingsMonitor(root, solutionName, changed, failed);
    }

    private sealed class SettingsMonitor : IDisposable
    {
        private readonly object lifetime = new object();
        private readonly FileSystemWatcher watcher;
        private readonly string? solutionName;
        private readonly Action<bool> changed;
        private readonly Action<Exception> failed;
        private bool disposed;

        internal SettingsMonitor(string root, string? solutionName, Action<bool> changed, Action<Exception> failed)
        {
            this.solutionName = solutionName;
            this.changed = changed;
            this.failed = failed;
            watcher = new FileSystemWatcher(root, "*.xml") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            try { watcher.EnableRaisingEvents = true; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                watcher.Dispose();
                throw;
            }
        }

        private void Notify(string? name)
        {
            lock (lifetime)
            {
                if (disposed) return;
                if (string.Equals(name, "global.xml", StringComparison.OrdinalIgnoreCase)) changed(true);
                else if (solutionName != null && string.Equals(name, solutionName, StringComparison.OrdinalIgnoreCase)) changed(false);
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e) => Notify(e.Name);
        private void OnRenamed(object sender, RenamedEventArgs e) { Notify(e.OldName); Notify(e.Name); }
        private void OnError(object sender, ErrorEventArgs e)
        {
            lock (lifetime) { if (!disposed) failed(e.GetException()); }
        }

        public void Dispose()
        {
            lock (lifetime) { if (disposed) return; disposed = true; }
            watcher.Dispose();
        }
    }
}