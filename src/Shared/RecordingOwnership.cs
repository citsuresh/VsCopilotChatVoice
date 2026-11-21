#nullable enable
using System;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace VsCopilotChatVoice.Gestures;

internal enum OwnershipResult { Acquired, AcquiredAfterAbandonment, Busy }

// Windows mutexes are thread-owned: keep acquire, release and disposal on the same thread.
internal sealed class RecordingOwnership : IDisposable
{
    private static readonly HashSet<string> LocalOwners = new HashSet<string>(StringComparer.Ordinal);
    private readonly Mutex mutex;
    private readonly string name;
    private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
    private bool disposed;

    internal static string DefaultName => @"Local\VsCopilotChatVoice.Recording." +
        CurrentUser.Value;

    private static SecurityIdentifier CurrentUser => WindowsIdentity.GetCurrent().User ??
        throw new InvalidOperationException("Current Windows user has no security identifier.");

    internal RecordingOwnership(string? name = null)
    {
        this.name = name ?? DefaultName;
        if (!this.name.StartsWith(@"Local\VsCopilotChatVoice.Recording.", StringComparison.Ordinal) ||
            this.name.Length > 240 || this.name.IndexOf('\0') >= 0 || this.name.LastIndexOf('\\') != 5)
            throw new ArgumentException("Invalid local recording mutex name.", nameof(name));
        var security = new MutexSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new MutexAccessRule(CurrentUser, MutexRights.FullControl,
            AccessControlType.Allow));
        mutex = new Mutex(false, this.name, out _, security);
    }

    internal bool IsOwner { get; private set; }

    private void VerifyThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != threadId)
            throw new InvalidOperationException("Recording ownership must be used on its creating thread.");
        if (disposed) throw new ObjectDisposedException(nameof(RecordingOwnership));
    }

    internal OwnershipResult TryAcquire()
    {
        VerifyThread();
        lock (LocalOwners)
        {
            // Prevent the kernel mutex's recursive same-thread acquire from creating two local owners.
            if (IsOwner || LocalOwners.Contains(name)) return OwnershipResult.Busy;
            OwnershipResult result;
            try { result = mutex.WaitOne(0) ? OwnershipResult.Acquired : OwnershipResult.Busy; }
            catch (AbandonedMutexException) { result = OwnershipResult.AcquiredAfterAbandonment; }
            if (result != OwnershipResult.Busy)
            {
                IsOwner = true;
                LocalOwners.Add(name);
            }
            return result;
        }
    }

    internal void Release()
    {
        VerifyThread();
        if (!IsOwner) return;
        lock (LocalOwners)
        {
            mutex.ReleaseMutex();
            IsOwner = false;
            LocalOwners.Remove(name);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        VerifyThread();
        Release();
        mutex.Dispose();
        disposed = true;
    }
}