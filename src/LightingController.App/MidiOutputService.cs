using System.Runtime.InteropServices;
using System.Text;

namespace LightingController.App;
public sealed class MidiOutputService : IDisposable
{
    private const uint MidiHeaderDone = 0x00000001;
    private readonly object _sync = new();
    private readonly List<(nint Header, nint Data)> _pending = [];
    private nint _handle; public bool IsOpen => _handle != 0;
    public static IReadOnlyList<string> DeviceNames() { var names = new List<string>(); for (uint index = 0; index < midiOutGetNumDevs(); index++) if (midiOutGetDevCaps(index, out var caps, (uint)Marshal.SizeOf<MidiOutCaps>()) == 0) names.Add(caps.Name); return names; }
    public void Open(string? name) { Close(); if (string.IsNullOrWhiteSpace(name)) return; var index = DeviceNames().ToList().FindIndex(item => item == name); if (index < 0) throw new InvalidOperationException($"MIDI output '{name}' is unavailable."); var result = midiOutOpen(out _handle, (uint)index, 0, 0, 0); if (result != 0) throw new InvalidOperationException($"Unable to open MIDI output (WinMM error {result})."); }
    public void Send(byte status, byte data1, byte data2) { if (_handle == 0) return; var message = (uint)(status | data1 << 8 | data2 << 16); midiOutShortMsg(_handle, message); }
    public void SendMcuLcdRow(string text)
    {
        var row = (text ?? "").PadRight(56)[..56];
        var characters = Encoding.ASCII.GetBytes(row).Select(value => (byte)(value & 0x7f)).ToArray();
        SendSysEx([0xf0, 0x00, 0x00, 0x66, 0x14, 0x12, 0x00, .. characters, 0xf7]);
    }
    public void SendMcuLcdCell(int cell, string text, int row = 1)
    {
        if (cell is < 1 or > 8 || row is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(cell));
        var value = (text ?? "").PadRight(7)[..7];
        var characters = Encoding.ASCII.GetBytes(value).Select(item => (byte)(item & 0x7f)).ToArray();
        var offset = (byte)((row - 1) * 56 + (cell - 1) * 7);
        SendSysEx([0xf0, 0x00, 0x00, 0x66, 0x14, 0x12, offset, .. characters, 0xf7]);
    }
    public void SendSysEx(byte[] message)
    {
        lock (_sync)
        {
            if (_handle == 0 || message.Length == 0) return;
            CleanupCompleted();
            var data = Marshal.AllocHGlobal(message.Length); Marshal.Copy(message, 0, data, message.Length);
            var header = Marshal.AllocHGlobal(Marshal.SizeOf<MidiHeader>());
            Marshal.StructureToPtr(new MidiHeader { Data = data, BufferLength = (uint)message.Length, BytesRecorded = (uint)message.Length }, header, false);
            if (midiOutPrepareHeader(_handle, header, (uint)Marshal.SizeOf<MidiHeader>()) != 0 || midiOutLongMsg(_handle, header, (uint)Marshal.SizeOf<MidiHeader>()) != 0)
            { midiOutUnprepareHeader(_handle, header, (uint)Marshal.SizeOf<MidiHeader>()); Marshal.FreeHGlobal(header); Marshal.FreeHGlobal(data); return; }
            _pending.Add((header, data));
        }
    }
    private void CleanupCompleted(bool all = false)
    {
        foreach (var item in _pending.ToArray())
        {
            var header = Marshal.PtrToStructure<MidiHeader>(item.Header);
            if (!all && (header.Flags & MidiHeaderDone) == 0) continue;
            midiOutUnprepareHeader(_handle, item.Header, (uint)Marshal.SizeOf<MidiHeader>());
            Marshal.FreeHGlobal(item.Header); Marshal.FreeHGlobal(item.Data); _pending.Remove(item);
        }
    }
    public void Close() { lock (_sync) { if (_handle == 0) return; midiOutReset(_handle); CleanupCompleted(true); midiOutClose(_handle); _handle = 0; } }
    public void Dispose() => Close();
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MidiOutCaps { public ushort Manufacturer; public ushort Product; public uint DriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public ushort Technology; public ushort Voices; public ushort Notes; public ushort ChannelMask; public uint Support; }
    [StructLayout(LayoutKind.Sequential)] private struct MidiHeader { public nint Data; public uint BufferLength; public uint BytesRecorded; public nuint User; public uint Flags; public nint Next; public nuint Reserved; public uint Offset; public nuint Reserved0; public nuint Reserved1; public nuint Reserved2; public nuint Reserved3; public nuint Reserved4; public nuint Reserved5; public nuint Reserved6; public nuint Reserved7; }
    [DllImport("winmm.dll")] private static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint midiOutGetDevCaps(uint deviceId, out MidiOutCaps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutOpen(out nint handle, uint deviceId, nint callback, nint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint midiOutShortMsg(nint handle, uint message);
    [DllImport("winmm.dll")] private static extern uint midiOutPrepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutLongMsg(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutUnprepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutReset(nint handle);
    [DllImport("winmm.dll")] private static extern uint midiOutClose(nint handle);
}
