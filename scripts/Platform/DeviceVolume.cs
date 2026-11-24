using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Godot;

namespace TinyWorks.Platform;

// The system's master volume (0..1) and mute: Core Audio on Windows, pactl on Linux (PulseAudio/PipeWire).
// Every call fails quietly (null / no-op) where neither is available.
public static class DeviceVolume
{
    public static (float Level, bool Muted)? Get()
    {
        try
        {
            if (OS.GetName() == "Windows")
            {
                var v = Endpoint();
                Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out float level));
                Marshal.ThrowExceptionForHR(v.GetMute(out bool muted));
                return (level, muted);
            }
            var vol = Regex.Match(Pactl("get-sink-volume"), @"(\d+)%");
            if (!vol.Success) return null;
            return (int.Parse(vol.Groups[1].Value) / 100f, Pactl("get-sink-mute").Contains("yes"));
        }
        catch (Exception e) { GD.PushWarning($"Device volume unreadable: {e.Message}"); return null; }
    }

    public static void Set(float level, bool muted)
    {
        try
        {
            if (OS.GetName() == "Windows")
            {
                var v = Endpoint();
                Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Clamp(level, 0, 1), IntPtr.Zero));
                Marshal.ThrowExceptionForHR(v.SetMute(muted, IntPtr.Zero));
                return;
            }
            Pactl("set-sink-volume", $"{Mathf.RoundToInt(level * 100)}%");
            Pactl("set-sink-mute", muted ? "1" : "0");
        }
        catch (Exception e) { GD.PushWarning($"Device volume not set: {e.Message}"); }
    }

    static string Pactl(string cmd, string value = null)
    {
        var output = new Godot.Collections.Array();
        var args = value == null ? new[] { cmd, "@DEFAULT_SINK@" } : new[] { cmd, "@DEFAULT_SINK@", value };
        OS.Execute("pactl", args, output);
        return output.Count > 0 ? output[0].AsString() : "";
    }

    static IAudioEndpointVolume Endpoint()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var device));
        var iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var o));
        return (IAudioEndpointVolume)o;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    // Only the methods up to GetMute are declared; the vtable order must match the Windows header.
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, IntPtr context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr context);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float db, IntPtr context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
