using System;
using System.IO;
using System.IO.Pipes;
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
                        if (message == null) { Send(WorkerProtocol.Error, 0, 0, "Invalid host message."); break; }
                        if (message.Type == WorkerProtocol.Shutdown) break;
                        Handle(synthesizer, message);
                    }
                    lock (SpeechLock) { currentPrompt = null; synthesizer.SpeakAsyncCancelAll(); }
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
            lock (SpeechLock)
            {
                if (message.Type == WorkerProtocol.Speak)
                {
                    // A new request replaces the previous prompt; the old prompt's events are then ignored.
                    currentPrompt = null;
                    synthesizer.SpeakAsyncCancelAll();
                    currentGeneration = message.Generation;
                    Prompt prompt = new Prompt(message.Text);
                    currentPrompt = prompt;
                    try { synthesizer.SpeakAsync(prompt); }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
                    {
                        currentPrompt = null;
                        Send(WorkerProtocol.Error, message.Generation, 0, "Speech could not start: " + ex.GetType().Name);
                    }
                }
                else if (message.Type == WorkerProtocol.Cancel && message.Generation == currentGeneration && currentPrompt != null)
                {
                    synthesizer.SpeakAsyncCancelAll();
                }
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
                if (e.Prompt != currentPrompt) return;
                generation = currentGeneration;
                currentPrompt = null;
            }
            if (e.Error != null) Send(WorkerProtocol.Error, generation, 0, "Speech failed: " + e.Error.GetType().Name);
            else if (e.Cancelled) Send(WorkerProtocol.Cancelled, generation, 0, null);
            else Send(WorkerProtocol.Completed, generation, 0, null);
        }

        private static void Send(string type, long generation, int offset, string text)
        {
            WorkerMessage message = new WorkerMessage { Type = type, Generation = generation, Offset = offset, Message = text };
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
