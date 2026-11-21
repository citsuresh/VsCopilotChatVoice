using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;
using VsCopilotChatVoice.Speech;

namespace ReadAlongTrial
{
    // Headless speech worker started by the Visual Studio tool window. It owns synthesis; the IDE only sends
    // text and receives progress. One pipe, one connected host, newline-delimited JSON messages.
    internal static class SpeechWorker
    {
        private static readonly object WriteLock = new object();
        private static readonly object SpeechLock = new object();
        private static NamedPipeClientStream pipe;
        private static Prompt currentPrompt;
        private static long currentGeneration;
        private static Prompt stoppingPrompt;
        private static readonly ManualResetEventSlim Stopped = new ManualResetEventSlim(true);
        private static SpeechOptionsSnapshot initialOptions;
        private static bool optionsTouched;

        public static int Run(string pipeName, bool silent)
        {
            if (!WorkerProtocol.IsValidPipeName(pipeName))
            {
                Console.Error.WriteLine("Invalid speech pipe name.");
                return 2;
            }
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                using (SpeechSynthesizer synthesizer = new SpeechSynthesizer())
                {
                    pipe = client;
                    initialOptions = ReadOptions(synthesizer);
                    optionsTouched = false;
                    client.Connect(10000);
                    if (silent) synthesizer.SetOutputToNull();
                    synthesizer.SpeakProgress += OnProgress;
                    synthesizer.SpeakCompleted += OnCompleted;
                    BoundedLineReader reader = new BoundedLineReader(client);
                    while (true)
                    {
                        string line = reader.ReadLineAsync(CancellationToken.None).GetAwaiter().GetResult();
                        if (line == null) break;
                        WorkerMessage message = WorkerProtocol.TryParse(line);
                        if (reader.LastLineHadInvalidUtf8 && message != null &&
                            (message.Type == WorkerProtocol.SpeakWithOptions || message.Type == WorkerProtocol.QueryOptions)) message = null;
                        if (message == null) { Send(WorkerProtocol.Error, 0, 0, "Invalid host message."); break; }
                        if (message.Type == WorkerProtocol.Shutdown) break;
                        Handle(synthesizer, message);
                    }
                    lock (SpeechLock) { currentPrompt = null; }
                    synthesizer.SpeakAsyncCancelAll();
                    if (synthesizer.State == SynthesizerState.Paused) synthesizer.Resume();
                    synthesizer.SpeakProgress -= OnProgress;
                    synthesizer.SpeakCompleted -= OnCompleted;
                }
                return 0;
            }
            catch (Exception ex) when (ex is IOException || ex is TimeoutException || ex is InvalidDataException || ex is ObjectDisposedException)
            {
                Console.Error.WriteLine("Speech worker connection ended: " + ex.GetType().Name);
                return 1;
            }
        }

        private static void Handle(SpeechSynthesizer synthesizer, WorkerMessage message)
        {
            if (message.Type == WorkerProtocol.Pause || message.Type == WorkerProtocol.Resume)
            {
                ChangeState(synthesizer, message);
                return;
            }
            if (message.Type == WorkerProtocol.QueryOptions)
            {
                if (message.Generation != currentGeneration)
                    Send(WorkerProtocol.Error, message.Generation, 0, "The speech generation is no longer current.", message.Command);
                else SendOptions(synthesizer, message.Generation, message.Command);
                return;
            }
            if (message.Type == WorkerProtocol.Speak || message.Type == WorkerProtocol.SpeakWithOptions)
            {
                StopForReplacement(synthesizer, message.Generation);
                lock (SpeechLock) { currentGeneration = message.Generation; }
                Prompt prompt = null;
                try
                {
                    if (message.Type == WorkerProtocol.SpeakWithOptions)
                    {
                        optionsTouched = true;
                        ApplyOptions(synthesizer, message.Options);
                        SendOptions(synthesizer, message.Generation);
                    }
                    else if (optionsTouched)
                    {
                        ApplyOptions(synthesizer, initialOptions);
                        optionsTouched = false;
                    }
                    prompt = new Prompt(message.Text);
                    lock (SpeechLock) { currentPrompt = prompt; }
                    synthesizer.SpeakAsync(prompt);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
                {
                    lock (SpeechLock) { if (currentPrompt == prompt) currentPrompt = null; }
                    Send(WorkerProtocol.Error, message.Generation, 0, "Speech could not start: " + (ex is SpeechOptionException ? ex.Message : ex.GetType().Name));
                }
            }
            else if (message.Type == WorkerProtocol.Cancel)
            {
                bool cancel;
                lock (SpeechLock) { cancel = message.Generation == currentGeneration && currentPrompt != null; }
                if (!cancel) return;
                synthesizer.SpeakAsyncCancelAll();
                if (synthesizer.State == SynthesizerState.Paused) synthesizer.Resume();
            }
        }

        private sealed class SpeechOptionException : InvalidOperationException
        {
            internal SpeechOptionException(string message) : base(message) { }
        }

        private static void StopForReplacement(SpeechSynthesizer synthesizer, long replacementGeneration)
        {
            bool wait;
            lock (SpeechLock)
            {
                stoppingPrompt = currentPrompt;
                wait = stoppingPrompt != null;
                currentPrompt = null;
                if (wait) Stopped.Reset();
            }
            synthesizer.SpeakAsyncCancelAll();
            if (synthesizer.State == SynthesizerState.Paused) synthesizer.Resume();
            if (wait && !Stopped.Wait(5000))
            {
                Send(WorkerProtocol.Error, replacementGeneration, 0, "The previous speech prompt did not stop.");
                throw new IOException("The replacement speech boundary could not be reached.");
            }
            lock (SpeechLock) { stoppingPrompt = null; }
        }

        private static SpeechOptionsSnapshot ReadOptions(SpeechSynthesizer synthesizer) =>
            new SpeechOptionsSnapshot(synthesizer.Voice.Name, synthesizer.Rate, synthesizer.Volume);

        private static void ApplyOptions(SpeechSynthesizer synthesizer, SpeechOptionsSnapshot options)
        {
            string name = options.Voice.Length == 0 ? initialOptions.Voice : options.Voice;
            var voices = synthesizer.GetInstalledVoices().Select(v => new SpeechVoiceCapability
            { Name = v.VoiceInfo.Name, Culture = v.VoiceInfo.Culture.Name, Enabled = v.Enabled }).ToArray();
            var selection = new SpeechOptionsSnapshot(name, options.Rate, options.Volume);
            RememberedVoiceAvailability availability = selection.AssessVoice(voices);
            if (availability != RememberedVoiceAvailability.Available)
                throw new SpeechOptionException("The requested speech voice is " + availability.ToString() + ".");
            synthesizer.SelectVoice(name);
            synthesizer.Rate = options.Rate;
            synthesizer.Volume = options.Volume;
            SpeechOptionsSnapshot applied = ReadOptions(synthesizer);
            if (applied.Voice != name || applied.Rate != options.Rate || applied.Volume != options.Volume)
                throw new SpeechOptionException("The requested speech options were not applied exactly.");
        }

        private static void SendOptions(SpeechSynthesizer synthesizer, long generation, long command = 0) =>
            Write(new WorkerMessage { Type = WorkerProtocol.OptionsApplied, Generation = generation, Command = command, Options = ReadOptions(synthesizer) });

        private static void ChangeState(SpeechSynthesizer synthesizer, WorkerMessage message)
        {
            Prompt prompt;
            lock (SpeechLock) { prompt = message.Generation == currentGeneration ? currentPrompt : null; }
            if (prompt == null)
            {
                Send(WorkerProtocol.Error, message.Generation, 0, "The speech generation is no longer active.", message.Command);
                return;
            }
            try
            {
                // Pause can wait for synthesis callbacks; do not hold the lock those callbacks need.
                bool pause = message.Type == WorkerProtocol.Pause;
                if (pause) synthesizer.Pause();
                else synthesizer.Resume();
                bool current;
                lock (SpeechLock) { current = currentPrompt == prompt && currentGeneration == message.Generation; }
                if (!current)
                {
                    if (synthesizer.State == SynthesizerState.Paused) synthesizer.Resume();
                    Send(WorkerProtocol.Error, message.Generation, 0, "Speech completed before the state change.", message.Command);
                    return;
                }
                if ((synthesizer.State == SynthesizerState.Paused) != pause)
                    throw new InvalidOperationException("The requested speech state was not reached.");
                Send(pause ? WorkerProtocol.Paused : WorkerProtocol.Resumed, message.Generation, 0, null, message.Command);
            }
            catch (InvalidOperationException ex)
            {
                Send(WorkerProtocol.Error, message.Generation, 0, "Speech state change failed: " + ex.GetType().Name, message.Command);
            }
        }

        private static void OnProgress(object sender, SpeakProgressEventArgs e)
        {
            long generation;
            lock (SpeechLock)
            {
                if (e.Prompt != currentPrompt) return;
                generation = currentGeneration;
            }
            Send(WorkerProtocol.Progress, generation, e.CharacterPosition, null);
        }

        private static void OnCompleted(object sender, SpeakCompletedEventArgs e)
        {
            long generation;
            lock (SpeechLock)
            {
                if (e.Prompt == stoppingPrompt) { Stopped.Set(); return; }
                if (e.Prompt != currentPrompt) return;
                generation = currentGeneration;
                currentPrompt = null;
            }
            if (e.Error != null) Send(WorkerProtocol.Error, generation, 0, "Speech failed: " + e.Error.GetType().Name);
            else if (e.Cancelled) Send(WorkerProtocol.Cancelled, generation, 0, null);
            else Send(WorkerProtocol.Completed, generation, 0, null);
        }

        private static void Send(string type, long generation, int offset, string text, long command = 0)
        {
            WorkerMessage message = new WorkerMessage { Type = type, Generation = generation, Command = command, Offset = offset, Message = text };
            Write(message);
        }

        private static void Write(WorkerMessage message)
        {
            byte[] bytes = WorkerProtocol.Serialize(message);
            try
            {
                lock (WriteLock)
                {
                    pipe.Write(bytes, 0, bytes.Length);
                    pipe.Flush();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
            {
                // The host is gone; the read loop sees the closed pipe and exits.
            }
        }
    }
}
