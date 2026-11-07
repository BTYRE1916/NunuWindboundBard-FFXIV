using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace NunuWindboundBard.Core;

/// <summary>
/// Windows MIDI-IN (winmm). MIDI callbacks ONLY queue raw bytes; they never
/// enter FFXIV, Dalamud, ImGui or the playback engine from the driver thread.
/// Wine's winmm MIDI bridge may work on Linux if configured, but is not guaranteed.
/// Uses no external NuGet packages or managed synth/audio output.
/// </summary>
public sealed class WinMmMidiInput : IDisposable
{
    public sealed record Port(int Index, string Name);
    private const uint CallbackFunction = 0x00030000;
    private const uint MimData = 0x3C3;
    private const uint MimError = 0x3C5;
    private const int MaxPending = 2048;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiCaps
    {
        public ushort Manufacturer;
        public ushort Product;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? Name;
        public uint Support;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void MidiInCallback(nint handle, uint message, nint user, nint param1, nint param2);

    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll", EntryPoint = "midiInGetDevCapsW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetDevCaps(nuint id, out MidiCaps caps, uint size);
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint midiInOpen(out nint handle, uint id, MidiInCallback callback, nint user, uint flags);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint midiInStart(nint handle);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint midiInStop(nint handle);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint midiInReset(nint handle);
    [DllImport("winmm.dll", ExactSpelling = true)] private static extern uint midiInClose(nint handle);

    private readonly ConcurrentQueue<uint> queue = new();
    private readonly MidiInCallback callback;
    private nint deviceHandle;
    private int queued;
    private int overflow;
    private int driverErrors;
    private volatile bool closing;
    private bool disposed;
    public string ConnectedName { get; private set; } = "";
    public bool Connected => deviceHandle != 0;
    public int DriverErrors => Volatile.Read(ref driverErrors);
    public int OverflowCount => Volatile.Read(ref overflow);

    public WinMmMidiInput() => callback = OnDriverMessage;

    public static IReadOnlyList<Port> GetPorts()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("MIDI input currently uses Windows winmm. Linux-native ALSA input is not yet implemented.");
        var result = new List<Port>();
        uint count = Math.Min(midiInGetNumDevs(), 128);
        for (uint i = 0; i < count; i++)
        {
            uint code = GetDevCaps((nuint)i, out MidiCaps info, (uint)Marshal.SizeOf<MidiCaps>());
            if (code == 0) result.Add(new Port((int)i, string.IsNullOrWhiteSpace(info.Name) ? $"MIDI port {i}" : info.Name.Trim()));
        }
        return result;
    }

    public void Open(Port port)
    {
        if (disposed) throw new ObjectDisposedException(nameof(WinMmMidiInput));
        Close();
        closing = false;
        uint code = midiInOpen(out nint handle, (uint)port.Index, callback, 0, CallbackFunction);
        if (code != 0) throw new InvalidOperationException($"Could not open MIDI input '{port.Name}' (winmm error {code}). Close any DAW or app using the port, then retry.");
        deviceHandle = handle;
        code = midiInStart(handle);
        if (code != 0)
        {
            Close();
            throw new InvalidOperationException($"Could not start MIDI input '{port.Name}' (winmm error {code}).");
        }
        ConnectedName = port.Name;
    }

    private void OnDriverMessage(nint handle, uint message, nint user, nint param1, nint param2)
    {
        if (closing) return;
        if (message == MimError) { Interlocked.Increment(ref driverErrors); return; }
        if (message != MimData) return; // Ignore sysex & system messages.
        if (Interlocked.Increment(ref queued) > MaxPending)
        {
            Interlocked.Decrement(ref queued);
            Interlocked.Increment(ref overflow);
            return;
        }
        queue.Enqueue(unchecked((uint)(nuint)param1));
    }

    public bool TryDequeue(out uint value)
    {
        if (!queue.TryDequeue(out value)) return false;
        Interlocked.Decrement(ref queued);
        return true;
    }

    public void Drain()
    {
        while (TryDequeue(out _)) { }
    }

    public void Close()
    {
        closing = true;
        nint handle = deviceHandle;
        deviceHandle = 0;
        ConnectedName = "";
        if (handle != 0)
        {
            try { _ = midiInStop(handle); _ = midiInReset(handle); }
            finally { _ = midiInClose(handle); }
        }
        Drain();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Close();
        GC.KeepAlive(callback);
    }
}
