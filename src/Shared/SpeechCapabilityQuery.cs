#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace VsCopilotChatVoice.Speech;

internal static class SpeechCapabilityQuery
{
    internal const int MaxErrorBytes = 8192;
    internal const int CleanupMilliseconds = 2000;
    internal static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(30);

    internal static Task<SpeechCapabilityReport> QueryAsync(string readerExecutable, TimeSpan timeout, CancellationToken cancellation = default)
    {
        long entered = Stopwatch.GetTimestamp();
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(readerExecutable) || !Path.IsPathRooted(readerExecutable) ||
            !string.Equals(Path.GetFullPath(readerExecutable), readerExecutable, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(readerExecutable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A canonical absolute Reader executable path is required.", nameof(readerExecutable));
        if (timeout <= TimeSpan.Zero || timeout > MaxTimeout) throw new ArgumentOutOfRangeException(nameof(timeout));
        return StartOwnedAsync(new ProcessStartInfo(readerExecutable, "--speech-capabilities"), Deadline(entered, timeout), cancellation, null);
    }

    // Test-only launch seam; no caller-supplied arguments exist on the production query entry point.
    internal static Task<SpeechCapabilityReport> QueryOwnedAsync(ProcessStartInfo start, TimeSpan timeout,
        CancellationToken cancellation, Action<int>? started = null)
    {
        long entered = Stopwatch.GetTimestamp();
        cancellation.ThrowIfCancellationRequested();
        if (timeout <= TimeSpan.Zero || timeout > MaxTimeout) throw new ArgumentOutOfRangeException(nameof(timeout));
        return StartOwnedAsync(start, Deadline(entered, timeout), cancellation, started);
    }

    private static long Deadline(long entered, TimeSpan timeout) => entered + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

    private static int Remaining(long deadline) => (int)Math.Max(0, Math.Ceiling((deadline - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency));

    private static void CheckLifetime(long deadline, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (Stopwatch.GetTimestamp() >= deadline) throw new TimeoutException("The speech capability child exceeded its deadline.");
    }

    private static Task<SpeechCapabilityReport> StartOwnedAsync(ProcessStartInfo start, long deadline,
        CancellationToken cancellation, Action<int>? started)
    {
        var completion = new TaskCompletionSource<SpeechCapabilityReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellation.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellation);
            return completion.Task;
        }
        if (Stopwatch.GetTimestamp() >= deadline)
        {
            completion.SetException(new TimeoutException("The speech capability child exceeded its deadline before launch."));
            return completion.Task;
        }
        // Neither orchestration nor pipe drains depend on the worker pool or timer callbacks.
        var worker = new Thread(() =>
        {
            try { completion.TrySetResult(QueryOwned(start, deadline, cancellation, started)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellation); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "Speech capability query" };
        try { worker.Start(); }
        catch (Exception ex) { completion.TrySetException(ex); }
        return completion.Task;
    }

    private static SpeechCapabilityReport QueryOwned(ProcessStartInfo start, long deadline,
        CancellationToken cancellation, Action<int>? started)
    {
        start.UseShellExecute = false; start.CreateNoWindow = true;
        start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        start.RedirectStandardInput = false;
        using var interrupted = new ManualResetEvent(false);
        using var interrupt = cancellation.Register(() => interrupted.Set());
        var process = new Process { StartInfo = start };
        Drain? output = null, error = null;
        ProcessExitHandle? exited = null;
        Exception? failure = null;
        SpeechCapabilityReport? report = null;
        bool launched = false;
        try
        {
            CheckLifetime(deadline, cancellation);
            if (!process.Start()) throw new InvalidOperationException("The speech capability child did not start.");
            launched = true;
            output = new Drain(process.StandardOutput.BaseStream, SpeechCapabilities.MaxBytes, "stdout");
            error = new Drain(process.StandardError.BaseStream, MaxErrorBytes, "stderr");
            output.Start(); error.Start();
            exited = new ProcessExitHandle(process);
            started?.Invoke(process.Id);
            var pending = new List<WaitHandle> { interrupted, exited, output.Completed, error.Completed };
            while (pending.Count > 1)
            {
                CheckLifetime(deadline, cancellation);
                int index = WaitHandle.WaitAny(pending.ToArray(), Remaining(deadline));
                CheckLifetime(deadline, cancellation);
                if (index == WaitHandle.WaitTimeout) continue;
                WaitHandle finished = pending[index];
                if (finished == output.Completed && output.Error != null) throw output.Error;
                if (finished == error.Completed && error.Error != null) throw error.Error;
                pending.Remove(finished);
            }
            string diagnostic;
            try { diagnostic = SpeechCapabilities.Utf8.GetString(error.Bytes!).Trim(); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("Capability stderr was not strict UTF-8.", ex); }
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Speech capability child exited " + process.ExitCode +
                    (diagnostic.Length == 0 ? " without diagnostics." : ": " + diagnostic));
            if (diagnostic.Length != 0) throw new InvalidDataException("Unexpected capability stderr: " + diagnostic);
            report = SpeechCapabilities.Parse(output.Bytes!);
            CheckLifetime(deadline, cancellation);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        var cleanupErrors = new List<Exception>();
        if (launched)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) when (process.HasExited) { }
            catch (Exception ex) { cleanupErrors.Add(ex); }
            try { process.StandardOutput.Dispose(); } catch (Exception ex) { cleanupErrors.Add(ex); }
            try { process.StandardError.Dispose(); } catch (Exception ex) { cleanupErrors.Add(ex); }
            var cleanupClock = Stopwatch.StartNew();
            try
            {
                if (!process.WaitForExit(CleanupMilliseconds)) cleanupErrors.Add(new TimeoutException("The exact capability child did not exit during cleanup."));
                foreach (Drain? drain in new[] { output, error })
                {
                    if (drain == null) continue;
                    int remaining = Math.Max(1, CleanupMilliseconds - (int)cleanupClock.ElapsedMilliseconds);
                    if (!drain.Finish(remaining)) cleanupErrors.Add(new TimeoutException("Owned capability stream did not finish during cleanup."));
                    else if (failure == null && drain.Error != null) cleanupErrors.Add(drain.Error);
                }
            }
            catch (Exception ex) { cleanupErrors.Add(ex); }
        }
        try { exited?.Dispose(); } catch (Exception ex) { cleanupErrors.Add(ex); }
        try { process.Dispose(); } catch (Exception ex) { cleanupErrors.Add(ex); }
        if (failure == null)
        {
            try { CheckLifetime(deadline, cancellation); } catch (Exception ex) { failure = ex; }
        }
        if (cleanupErrors.Count != 0)
        {
            if (failure != null) cleanupErrors.Insert(0, failure);
            throw new AggregateException("Speech capability query/owned cleanup failed.", cleanupErrors);
        }
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return report!;
    }

    private sealed class ProcessExitHandle : WaitHandle
    {
        internal ProcessExitHandle(Process process) { SafeWaitHandle = new SafeWaitHandle(process.Handle, false); }
    }

    private sealed class Drain
    {
        internal readonly ManualResetEvent Completed = new ManualResetEvent(false);
        internal byte[]? Bytes;
        internal Exception? Error;
        private readonly Thread worker;
        private readonly object sync = new object();
        private bool started;
        private bool retired;
        private bool finished;

        internal Drain(Stream stream, int limit, string channel)
        {
            worker = new Thread(() =>
            {
                try
                {
                    using var buffer = new MemoryStream();
                    var chunk = new byte[4096];
                    while (true)
                    {
                        int count = stream.Read(chunk, 0, Math.Min(chunk.Length, limit + 1 - (int)buffer.Length));
                        if (count == 0) { Bytes = buffer.ToArray(); return; }
                        if (buffer.Length + count > limit) throw new InvalidDataException("Capability " + channel + " exceeded its byte limit.");
                        buffer.Write(chunk, 0, count);
                    }
                }
                catch (Exception ex) { Error = ex; }
                finally
                {
                    lock (sync)
                    {
                        Completed.Set(); finished = true;
                        if (retired) Completed.Dispose();
                    }
                }
            }) { IsBackground = true, Name = "Speech capability " + channel };
        }

        internal void Start() { worker.Start(); started = true; }

        internal bool Finish(int milliseconds)
        {
            if (started && !worker.Join(milliseconds))
            {
                lock (sync) { retired = true; if (finished) Completed.Dispose(); }
                return false;
            }
            Completed.Dispose();
            return true;
        }
    }
}
