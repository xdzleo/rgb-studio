using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RgbStudio.Core;

/// <summary>
/// The Aqua Computer high flow NEXT (0C70:F012) settings report, feature 0x03. Layout from
/// aquasuite 10.96's own structures (Pack=1, big-endian), checked against this device: the
/// CRC-16/USB closes at 0x2A8. The sensor runs its effects itself; this only edits their
/// settings, and only inside the lighting block.
/// </summary>
public sealed class HighFlowNextConfig
{
    public const int ReportId = 3;
    public const int Length = 0x2AA;           // what aquasuite reads; the HID buffer is longer
    public const int CrcOffset = 0x2A8;
    public const int StripBrightness = 0x059;  // u8, the whole device
    public const int FirstController = 0x05C;
    public const int ControllerSize = 70;
    public const int Controllers = 8;
    public const int MaxHue = 1535;            // device hue: 0 red, 512 green, 1024 blue
    public const int MaxLeds = 90;             // aquasuite's limit per strip
    const int ColorsAt = 46, Slots = 6;
    // Nothing outside the lighting block is ever changed (calibration, alarms, units live there).
    const int LightingStart = StripBrightness, LightingEnd = FirstController + Controllers * ControllerSize;

    /// <summary>The device's own effects, with aquasuite's numbers and the names its screen shows.</summary>
    public static readonly IReadOnlyList<(byte Mode, string Name)> Modes =
    [
        (1, "Cor fixa"), (2, "Respirar"), (3, "Arco-íris"), (19, "Arco-íris deslizante"), (12, "Ambiente"),
        (4, "Piscar 1"), (5, "Piscar 2"), (7, "Sequencial"), (8, "Visor 1"), (9, "Visor 2"),
        (10, "Onda (Wave)"), (11, "Letreiro 2"), (14, "Vela"), (15, "Chuva 1"), (16, "Chuva 2"), (17, "Chuva 3"),
        (18, "Alternar"), (13, "Barra"), (33, "Gradiente"), (0, "Desligado"),
    ];

    readonly byte[] _b;

    HighFlowNextConfig(byte[] b) => _b = b;

    /// <summary>Accepts only a report whose layout is proven: right id, structure 1 and a closing CRC.</summary>
    public static HighFlowNextConfig Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length < Length) throw new InvalidDataException($"Relatório curto ({report.Length} bytes).");
        if (report[0] != ReportId) throw new InvalidDataException($"Relatório 0x{report[0]:X2}, esperado 0x03.");
        if ((report[1] << 8 | report[2]) != 1) throw new InvalidDataException("Estrutura diferente da conhecida.");
        if (Crc16Usb(report[1..CrcOffset]) != (report[CrcOffset] << 8 | report[CrcOffset + 1]))
            throw new InvalidDataException("O checksum não confere: layout desconhecido, nada será escrito.");
        return new HighFlowNextConfig(report[..Length].ToArray());
    }

    public byte[] ToReport()
    {
        var r = (byte[])_b.Clone();
        ushort crc = Crc16Usb(r.AsSpan(1, CrcOffset - 1));
        r[CrcOffset] = (byte)(crc >> 8);
        r[CrcOffset + 1] = (byte)crc;
        return r;
    }

    public HighFlowNextConfig Clone() => new((byte[])_b.Clone());

    /// <summary>Offsets that differ, which must all be inside the lighting block before anything is sent.</summary>
    public static bool OnlyLightingDiffers(HighFlowNextConfig a, HighFlowNextConfig b)
    {
        for (int i = 0; i < CrcOffset; i++)
            if (a._b[i] != b._b[i] && (i < LightingStart || i >= LightingEnd)) return false;
        return true;
    }

    public int Brightness { get => _b[StripBrightness]; set => _b[StripBrightness] = (byte)Math.Clamp(value, 0, 255); }

    public Controller this[int index] =>
        index is >= 0 and < Controllers ? new Controller(this, FirstController + index * ControllerSize, index)
        : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerable<Controller> All => Enumerable.Range(0, Controllers).Select(i => this[i]);

    public sealed class Controller(HighFlowNextConfig config, int at, int index)
    {
        public int Index => index;
        /// <summary>0 = the RGBpx output, 1 = the sensor's own ring.</summary>
        public int Strip { get => config._b[at]; set => config._b[at] = (byte)Math.Clamp(value, 0, 1); }
        public int FirstLed { get => config._b[at + 1]; set => config._b[at + 1] = (byte)Math.Clamp(value, 0, MaxLeds - 1); }
        public int Count { get => config._b[at + 2]; set => config._b[at + 2] = (byte)Math.Clamp(value, 0, MaxLeds); }
        public byte Mode { get => config._b[at + 3]; set => config._b[at + 3] = value; }
        public bool InUse => Count > 0 && Mode != 0;
        public int Slots => HighFlowNextConfig.Slots;

        /// <summary>Slot 0 is the background, 1-5 the effect's colors.</summary>
        public (int Hue, byte Sat, byte Val) Hsv(int slot)
        {
            int o = SlotAt(slot);
            return (config._b[o] << 8 | config._b[o + 1], config._b[o + 2], config._b[o + 3]);
        }

        public void SetHsv(int slot, int hue, byte sat, byte val)
        {
            int o = SlotAt(slot);
            hue = Math.Clamp(hue, 0, MaxHue);
            config._b[o] = (byte)(hue >> 8);
            config._b[o + 1] = (byte)hue;
            config._b[o + 2] = sat;
            config._b[o + 3] = val;
        }

        public Rgb Color(int slot)
        {
            var (h, s, v) = Hsv(slot);
            return Rgb.FromHsv(h * 360.0 / (MaxHue + 1), s / 255.0, v / 255.0);
        }

        public void SetColor(int slot, Rgb c)
        {
            var (h, s, v) = Rgb.ToHsv(c);
            SetHsv(slot, (int)Math.Round(h / 360 * (MaxHue + 1)) % (MaxHue + 1), (byte)Math.Round(s * 255), (byte)Math.Round(v * 255));
        }

        int SlotAt(int slot) => slot is >= 0 and < HighFlowNextConfig.Slots ? at + ColorsAt + 4 * slot : throw new ArgumentOutOfRangeException(nameof(slot));
    }

    /// <summary>CRC-16/USB (poly 0x8005 reflected, init and xorout 0xFFFF), as in the report's last two bytes.</summary>
    public static ushort Crc16Usb(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (ushort)(crc >> 1 ^ 0xA001) : (ushort)(crc >> 1);
        }
        return (ushort)(crc ^ 0xFFFF);
    }
}

/// <summary>
/// Reads and writes the high flow NEXT's settings. Each write is read-modify-write: the
/// report is read again, only the lighting block is taken from the edit, and the result is
/// read back. Without <see cref="Save"/> a change lasts until the sensor loses power.
/// </summary>
public sealed class HighFlowNext : IDisposable
{
    const string InterfaceGuid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";
    // aquasuite sends this after a change to keep it across power loss (report 0x02).
    static readonly byte[] CommitReport = [0x02, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x34, 0xC6];

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buffer, int length);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out Caps caps);

    [StructLayout(LayoutKind.Sequential)]
    struct Caps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort A, B, C, D, E, F, G, H, I, J;
    }

    readonly SafeFileHandle _h;
    readonly int _featureLength, _outputLength;
    readonly Lock _lock = new();

    HighFlowNext(SafeFileHandle h, int featureLength, int outputLength) { _h = h; _featureLength = featureLength; _outputLength = outputLength; }

    /// <summary>The sensor, or null when it is not plugged in.</summary>
    public static HighFlowNext? Open()
    {
        const string key = @"SYSTEM\CurrentControlSet\Enum\HID\VID_0C70&PID_F012&MI_01";
        using var hid = Registry.LocalMachine.OpenSubKey(key);
        foreach (var instance in hid?.GetSubKeyNames() ?? [])
        {
            var handle = CreateFileW($@"\\?\HID#VID_0C70&PID_F012&MI_01#{instance}#{InterfaceGuid}", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); continue; }
            int length = 0, output = 0;
            if (HidD_GetPreparsedData(handle, out var data))
            {
                if (HidP_GetCaps(data, out var caps) >= 0) { length = caps.FeatureLength; output = caps.OutputLength; }
                HidD_FreePreparsedData(data);
            }
            if (length >= HighFlowNextConfig.Length) return new HighFlowNext(handle, length, output);
            handle.Dispose();
        }
        return null;
    }

    public HighFlowNextConfig Read()
    {
        lock (_lock) return ReadLocked(attempts: 15);   // it may still be busy with the last write (3 s)
    }

    // Right after a write the sensor answers nothing for a moment (error 31, seen on hardware).
    HighFlowNextConfig ReadLocked(int attempts = 1)
    {
        for (int i = 1; ; i++)
        {
            var b = new byte[_featureLength];
            b[0] = HighFlowNextConfig.ReportId;
            if (HidD_GetFeature(_h, b, b.Length)) return HighFlowNextConfig.Parse(b);
            int error = Marshal.GetLastWin32Error();
            if (i >= attempts) throw new IOException($"O sensor não respondeu à leitura (erro {error}).");
            Thread.Sleep(200);
        }
    }

    /// <summary>Sends the lighting part of <paramref name="edit"/>; returns what the sensor holds afterwards.</summary>
    public HighFlowNextConfig Write(HighFlowNextConfig edit, bool save = false)
    {
        lock (_lock)
        {
            var live = ReadLocked();
            // Lighting from the edit, everything else exactly as the sensor has it now.
            var merged = live.ToReport();
            var wanted = edit.ToReport();
            for (int i = HighFlowNextConfig.StripBrightness; i < HighFlowNextConfig.FirstController + HighFlowNextConfig.Controllers * HighFlowNextConfig.ControllerSize; i++)
                merged[i] = wanted[i];
            var next = HighFlowNextConfig.Parse(Fix(merged));
            if (!HighFlowNextConfig.OnlyLightingDiffers(live, next)) throw new InvalidOperationException("Mudança fora da iluminação: nada foi enviado.");

            var buffer = new byte[_featureLength];
            next.ToReport().CopyTo(buffer, 0);
            if (!HidD_SetFeature(_h, buffer, buffer.Length)) throw new IOException($"O sensor recusou a escrita (erro {Marshal.GetLastWin32Error()}).");
            Thread.Sleep(300);
            if (save)
            {
                // An output report: this sensor declares one of exactly these 11 bytes. Sent as a
                // feature report, Windows refused it (error 87, seen on hardware).
                if (_outputLength < CommitReport.Length) throw new IOException("O sensor não tem o relatório de salvar esperado: nada foi salvo.");
                var commit = new byte[_outputLength];
                CommitReport.CopyTo(commit, 0);
                if (!HidD_SetOutputReport(_h, commit, commit.Length)) throw new IOException($"A cor foi aplicada, mas o sensor recusou salvar (erro {Marshal.GetLastWin32Error()}).");
                Thread.Sleep(300);
            }
            return ReadLocked(attempts: 10);
        }
    }

    /// <summary>
    /// For live frames: sends the report without reading it first or after. Only the lighting
    /// may differ from <paramref name="baseline"/>, a report read from the sensor. Never saves.
    /// </summary>
    public void Stream(HighFlowNextConfig baseline, HighFlowNextConfig frame)
    {
        if (!HighFlowNextConfig.OnlyLightingDiffers(baseline, frame)) throw new InvalidOperationException("Mudança fora da iluminação: nada foi enviado.");
        lock (_lock)
        {
            var buffer = new byte[_featureLength];
            frame.ToReport().CopyTo(buffer, 0);
            if (!HidD_SetFeature(_h, buffer, buffer.Length)) throw new IOException($"O sensor recusou o quadro (erro {Marshal.GetLastWin32Error()}).");
        }
    }

    static byte[] Fix(byte[] report)
    {
        ushort crc = HighFlowNextConfig.Crc16Usb(report.AsSpan(1, HighFlowNextConfig.CrcOffset - 1));
        report[HighFlowNextConfig.CrcOffset] = (byte)(crc >> 8);
        report[HighFlowNextConfig.CrcOffset + 1] = (byte)crc;
        return report;
    }

    public void Dispose() => _h.Dispose();
}
