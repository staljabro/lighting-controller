using System.Runtime.InteropServices;

namespace LightingController.App;

public sealed class MidiInputService : IDisposable
{
    private delegate void MidiInCallback(nint handle, uint message, nint instance, nint parameter1, nint parameter2);
    private const uint DataMessage = 0x3C3;
    private const uint LongDataMessage = 0x3C4;
    private readonly MidiInCallback _callback;
    private readonly List<(nint Header, nint Data)> _longBuffers = [];
    private nint _handle;
    private bool _closing;
    public event Action<byte, byte, byte>? MessageReceived;
    public event Action<byte[]>? SysExReceived;
    public bool IsOpen => _handle != 0;

    public MidiInputService() => _callback = OnMessage;

    public static IReadOnlyList<string> DeviceNames()
    {
        var names = new List<string>();
        for (uint index = 0; index < midiInGetNumDevs(); index++)
        {
            if (midiInGetDevCaps(index, out var caps, (uint)Marshal.SizeOf<MidiInCaps>()) == 0) names.Add(caps.Name);
        }
        return names;
    }

    public void Open(string? deviceName)
    {
        Close();
        if (string.IsNullOrWhiteSpace(deviceName)) return;
        var names = DeviceNames(); var index = names.ToList().FindIndex(name => name == deviceName);
        if (index < 0) throw new InvalidOperationException($"MIDI input '{deviceName}' is not available.");
        var result = midiInOpen(out _handle, (uint)index, _callback, 0, 0x00030000);
        if (result != 0) throw new InvalidOperationException($"Unable to open MIDI input (WinMM error {result}).");
        _closing = false;
        for (var bufferIndex = 0; bufferIndex < 4; bufferIndex++) AddLongBuffer();
        result = midiInStart(_handle); if (result != 0) { Close(); throw new InvalidOperationException($"Unable to start MIDI input (WinMM error {result})."); }
    }

    private void OnMessage(nint handle, uint message, nint instance, nint parameter1, nint parameter2)
    {
        if (message == DataMessage)
        {
            var packed = unchecked((uint)parameter1.ToInt64());
            MessageReceived?.Invoke((byte)(packed & 0xff), (byte)((packed >> 8) & 0xff), (byte)((packed >> 16) & 0xff));
        }
        else if (message == LongDataMessage && parameter1 != 0)
        {
            var header = Marshal.PtrToStructure<MidiHeader>(parameter1);
            if (header.BytesRecorded > 0)
            {
                var data = new byte[header.BytesRecorded]; Marshal.Copy(header.Data, data, 0, data.Length); SysExReceived?.Invoke(data);
            }
            if (!_closing && _handle != 0)
            {
                header.BytesRecorded = 0; Marshal.StructureToPtr(header, parameter1, false);
                midiInAddBuffer(_handle, parameter1, (uint)Marshal.SizeOf<MidiHeader>());
            }
        }
    }

    private void AddLongBuffer()
    {
        var data = Marshal.AllocHGlobal(1024); var header = Marshal.AllocHGlobal(Marshal.SizeOf<MidiHeader>());
        Marshal.StructureToPtr(new MidiHeader { Data = data, BufferLength = 1024 }, header, false);
        if (midiInPrepareHeader(_handle, header, (uint)Marshal.SizeOf<MidiHeader>()) != 0 || midiInAddBuffer(_handle, header, (uint)Marshal.SizeOf<MidiHeader>()) != 0)
        { midiInUnprepareHeader(_handle, header, (uint)Marshal.SizeOf<MidiHeader>()); Marshal.FreeHGlobal(header); Marshal.FreeHGlobal(data); return; }
        _longBuffers.Add((header, data));
    }

    public void Close()
    {
        if (_handle == 0) return; _closing = true; midiInStop(_handle); midiInReset(_handle);
        foreach (var item in _longBuffers) { midiInUnprepareHeader(_handle, item.Header, (uint)Marshal.SizeOf<MidiHeader>()); Marshal.FreeHGlobal(item.Header); Marshal.FreeHGlobal(item.Data); }
        _longBuffers.Clear(); midiInClose(_handle); _handle = 0;
    }
    public void Dispose() => Close();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiInCaps { public ushort Manufacturer; public ushort Product; public uint DriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public uint Support; }
    [StructLayout(LayoutKind.Sequential)] private struct MidiHeader { public nint Data; public uint BufferLength; public uint BytesRecorded; public nuint User; public uint Flags; public nint Next; public nuint Reserved; public uint Offset; public nuint Reserved0; public nuint Reserved1; public nuint Reserved2; public nuint Reserved3; public nuint Reserved4; public nuint Reserved5; public nuint Reserved6; public nuint Reserved7; }
    [DllImport("winmm.dll")] private static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint midiInGetDevCaps(uint deviceId, out MidiInCaps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint midiInOpen(out nint handle, uint deviceId, MidiInCallback callback, nint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint midiInStart(nint handle);
    [DllImport("winmm.dll")] private static extern uint midiInStop(nint handle);
    [DllImport("winmm.dll")] private static extern uint midiInReset(nint handle);
    [DllImport("winmm.dll")] private static extern uint midiInPrepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiInAddBuffer(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiInUnprepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiInClose(nint handle);
}
