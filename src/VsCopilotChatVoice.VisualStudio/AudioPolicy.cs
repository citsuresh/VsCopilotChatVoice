using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace VsCopilotChatVoice.VisualStudio;

internal enum OtherAudioPolicy { Off, Duck, Suppress }

internal sealed class AudioPolicySettings
{
    internal OtherAudioPolicy Policy { get; }
    internal int DuckPercent { get; }
    internal AudioPolicySettings(OtherAudioPolicy policy, int duckPercent)
    {
        if (duckPercent < 0 || duckPercent > 100) throw new ArgumentOutOfRangeException(nameof(duckPercent));
        Policy = policy; DuckPercent = duckPercent;
    }
}

internal sealed class AudioPolicyScope : IDisposable
{
    private readonly List<SessionVolume> sessions = new List<SessionVolume>();
    private bool disposed;

    private sealed class SessionVolume
    {
        internal readonly ISimpleAudioVolume Volume;
        internal readonly float Original;
        internal readonly bool Muted;
        internal readonly bool MuteChanged;
        internal readonly float Applied;
        internal SessionVolume(ISimpleAudioVolume volume, float original, bool muted, bool muteChanged, float applied)
        { Volume = volume; Original = original; Muted = muted; MuteChanged = muteChanged; Applied = applied; }
    }

    private AudioPolicyScope() { }

    internal static AudioPolicyScope Begin(AudioPolicySettings settings, int? excludedProcessId = null)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        var scope = new AudioPolicyScope();
        if (settings.Policy == OtherAudioPolicy.Off) return scope;
        try
        {
            scope.Apply(settings, excludedProcessId);
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private void Apply(AudioPolicySettings settings, int? excludedProcessId)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Check(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out IMMDevice device), "GetDefaultAudioEndpoint");
            Guid iid = typeof(IAudioSessionManager2).GUID;
            Check(device.Activate(ref iid, 23, IntPtr.Zero, out object managerObject), "Activate");
            var manager = (IAudioSessionManager2)managerObject;
            Check(manager.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator), "GetSessionEnumerator");
            Check(sessionEnumerator.GetCount(out int count), "GetCount");
            uint process = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            uint? excluded = excludedProcessId.HasValue ? (uint?)excludedProcessId.Value : null;
            for (int i = 0; i < count; i++)
            {
                Check(sessionEnumerator.GetSession(i, out IAudioSessionControl control), "GetSession");
                var control2 = (IAudioSessionControl2)control;
                Check(control2.GetProcessId(out uint pid), "GetProcessId");
                if (pid == 0 || pid == process || (excluded.HasValue && pid == excluded.Value)) continue;
                var volume = (ISimpleAudioVolume)control;
                Check(volume.GetMasterVolume(out float original), "GetMasterVolume");
                Check(volume.GetMute(out bool muted), "GetMute");
                float applied = settings.Policy == OtherAudioPolicy.Suppress ? 0f : original * settings.DuckPercent / 100f;
                bool muteChanged = settings.Policy == OtherAudioPolicy.Suppress;
                var captured = new SessionVolume(volume, original, muted, muteChanged, applied);
                sessions.Add(captured);
                try
                {
                    Guid context = Guid.Empty;
                    Check(volume.SetMasterVolume(applied, ref context), "SetMasterVolume");
                    if (settings.Policy == OtherAudioPolicy.Suppress) Check(volume.SetMute(true, ref context), "SetMute");
                }
                catch
                {
                    sessions.Remove(captured);
                    try { Restore(captured); } catch { }
                    throw;
                }
            }
        }
        catch (COMException ex) { throw new IOException("Other-application audio policy is unavailable.", ex); }
        catch (InvalidCastException ex) { throw new IOException("The default audio endpoint does not expose session volume control.", ex); }
        catch (Win32Exception ex) { throw new IOException("Other-application audio policy failed.", ex); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception> errors = new List<Exception>();
        foreach (SessionVolume session in sessions)
        {
            try
            {
                Check(session.Volume.GetMasterVolume(out float current), "GetMasterVolume");
                Check(session.Volume.GetMute(out bool muted), "GetMute");
                bool muteUnchanged = !session.MuteChanged || muted == true;
                if (Math.Abs(current - session.Applied) < 0.005f && muteUnchanged) Restore(session);
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidCastException || ex is Win32Exception)
            { errors.Add(ex); }
        }
        sessions.Clear();
        if (errors.Count != 0) throw new AggregateException("One or more audio sessions could not be restored.", errors);
    }

    private static void Restore(SessionVolume session)
    {
        Guid context = Guid.Empty;
        Check(session.Volume.SetMasterVolume(session.Original, ref context), "RestoreMasterVolume");
        Check(session.Volume.SetMute(session.Muted, ref context), "RestoreMute");
    }

    private static void Check(int hresult, string operation)
    {
        if (hresult < 0) Marshal.ThrowExceptionForHR(hresult);
    }

    private enum EDataFlow { Render, Capture, All }
    private enum ERole { Console, Multimedia, Communications }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), ClassInterface(ClassInterfaceType.None)] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDeviceEnumerator
    { int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out object devices); int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDevice
    { int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.Interface)] out object interfacePointer); }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioSessionManager2
    { int GetAudioSessionControl(ref Guid audioSessionGuid, uint streamFlags, out IAudioSessionControl sessionControl); int GetSimpleAudioVolume(ref Guid audioSessionGuid, uint streamFlags, out ISimpleAudioVolume audioVolume); int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum); }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioSessionEnumerator
    { int GetCount(out int count); int GetSession(int index, out IAudioSessionControl session); }
    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioSessionControl { }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioSessionControl2
    { int GetState(out int state); int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name); int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext); int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path); int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext); int GetGroupingParam(out Guid groupingId); int SetGroupingParam(ref Guid groupingId, ref Guid eventContext); int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id); int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id); int GetProcessId(out uint processId); }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface ISimpleAudioVolume
    { int SetMasterVolume(float level, ref Guid eventContext); int GetMasterVolume(out float level); int SetMute(bool mute, ref Guid eventContext); int GetMute(out bool mute); }
}
