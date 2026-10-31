using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RgbStudio.Core;

/// <summary>
/// The ENE light controllers on the RAM, over SMBus, with no ASUS software: the PawnIO
/// driver and its PIIX4 module (AMD chipsets) carry the transfers. Written from the register
/// map OpenRGB documents, tested on this machine's ADATA XPG Lancer RGB (firmware
/// AUDA0-E6K5-0101, 8 LEDs, at 0x71 and 0x73).
///
/// Safety: only 0x70-0x77 are ever addressed (the modules' SPD data sits at 0x50-0x57 on the
/// same bus); only the color registers and, on a cold start, the two remap registers at 0x77
/// are written; the controller's save command is never sent; an unknown firmware is left alone.
/// </summary>
public sealed class EneSmbusBackend : IRgbBackend
{
    public static class Ene
    {
        public const int DeviceName = 0x1000, ConfigTable = 0x1C00, Direct = 0x8020, Apply = 0x80A0,
            SlotIndex = 0x80F8, I2cAddress = 0x80F9, Colors = 0x8100;
        public const string TestedFirmware = "AUDA0-E6K5-0101";
        public const int Unmapped = 0x77, MaxLeds = 10;

        /// <summary>The register pointer goes to command 0x00 byte-swapped.</summary>
        public static ulong Pointer(int reg) => (ulong)(((reg << 8) & 0xFF00) | ((reg >> 8) & 0xFF));

        /// <summary>The controller takes red, blue, green.</summary>
        public static byte[] Rbg(Rgb c) => [c.R, c.B, c.G];

        public static bool AddressAllowed(int addr) => addr is >= 0x70 and <= 0x77;
    }

    const string ModuleFile = "SmbusPIIX4.bin";
    const string MutexName = @"Global\Access_SMBUS.HTP.Method";   // shared with other SMBus software
    static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(33);

    IntPtr _h;
    Mutex? _mutex;
    readonly List<(int Address, int Leds)> _sticks = [];
    readonly List<Rgb[]> _last = [];
    readonly List<long> _lastSent = [];
    string? _lastError;

    public string Name => "RAM direto (SMBus)";

    public IReadOnlyList<DeviceInfo> Open()
    {
        var module = Path.Combine(AppContext.BaseDirectory, ModuleFile);
        if (!File.Exists(module)) { Log.Write($"Memória pelo SMBus: {ModuleFile} não está junto do app."); return []; }
        int hr;
        try { hr = Pawn.pawnio_open(out _h); }
        catch (DllNotFoundException) { Log.Write("Memória pelo SMBus: o driver PawnIO não está instalado."); return []; }
        if (hr < 0) { Log.Write($"Memória pelo SMBus: PawnIO não abriu (0x{hr:X8})."); return []; }
        var blob = File.ReadAllBytes(module);
        hr = Pawn.pawnio_load(_h, blob, (nuint)blob.Length);
        if (hr < 0) { Log.Write($"Memória pelo SMBus: o módulo PIIX4 não carregou (0x{hr:X8}); chipset não AMD?"); Close(); return []; }

        _mutex = new Mutex(false, MutexName);
        var devices = new List<DeviceInfo>();
        Locked(() =>
        {
            Remap();
            for (int addr = 0x70; addr <= 0x76; addr++)
            {
                if (!Xfer(addr, true, 0, 1, 0, out _)) continue;
                string name = ReadName(addr);
                if (name != Ene.TestedFirmware) { Log.Write($"Memória 0x{addr:X2}: firmware \"{name}\" não testado; deixada como está."); continue; }
                int leds = Math.Clamp((int)Read(addr, Ene.ConfigTable + 2), 1, Ene.MaxLeds);
                if (Read(addr, Ene.Direct) != 1) { Write(addr, Ene.Direct, 1); Write(addr, Ene.Apply, 1); }   // direct mode, not saved
                // Same names the Aura SDK gave, so each stick keeps its settings.
                devices.Add(new DeviceInfo(devices.Count, $"ENE_RGB_For_ASUS{devices.Count}", 0x00070000, leds));
                _sticks.Add((addr, leds));
                _last.Add(new Rgb[leds]);
                _lastSent.Add(0);
                Log.Write($"Memória 0x{addr:X2}: {name}, {leds} LEDs.");
            }
        });
        if (devices.Count == 0) Close();
        return devices;
    }

    /// <summary>
    /// After a cold start the sticks all answer at 0x77, unmapped; Armoury Crate used to move
    /// each to its own address. Same sequence: while 0x77 answers, give the next slot the next
    /// free address. Volatile: undone by a power cycle.
    /// </summary>
    void Remap()
    {
        int next = 0x70;
        for (int slot = 0; slot < 8; slot++)
        {
            if (!Xfer(Ene.Unmapped, true, 0, 1, 0, out _)) return;
            while (next < Ene.Unmapped && Xfer(next, true, 0, 1, 0, out _)) next++;
            if (next >= Ene.Unmapped) { Log.Write("Memória: sem endereço livre para remapear."); return; }
            Write(Ene.Unmapped, Ene.SlotIndex, (byte)slot);
            Write(Ene.Unmapped, Ene.I2cAddress, (byte)(next << 1));
            Log.Write($"Memória: slot {slot} remapeado de 0x77 para 0x{next:X2}.");
            next++;
        }
    }

    public void Write(int device, ReadOnlySpan<Rgb> colors)
    {
        if (_h == IntPtr.Zero) return;
        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastSent[device], now) < MinGap) return;
        var (addr, leds) = _sticks[device];
        var last = _last[device];
        var pending = new List<int>();
        for (int i = 0; i < leds; i++)
        {
            var c = i < colors.Length ? colors[i] : Rgb.Black;
            if (c != last[i] || _lastSent[device] == 0) pending.Add(i);
        }
        if (pending.Count == 0) return;
        try
        {
            // Another SMBus user (sensor software) may hold the bus: skip the frame rather than wait.
            if (!_mutex!.WaitOne(5)) return;
            try
            {
                foreach (int i in pending)
                {
                    var c = i < colors.Length ? colors[i] : Rgb.Black;
                    WriteBlock(addr, Ene.Colors + 3 * i, Ene.Rbg(c));
                    last[i] = c;
                }
            }
            finally { _mutex.ReleaseMutex(); }
            _lastSent[device] = now;
            _lastError = null;
        }
        catch (Exception ex)
        {
            if (ex.Message != _lastError) Log.Write($"Memória 0x{addr:X2}: {ex.Message}");
            _lastError = ex.Message;
        }
    }

    /// <summary>Nothing to hand back without Armoury Crate: the sticks keep the last colors.</summary>
    public void Release() { }

    public void Dispose() => Close();

    void Close()
    {
        if (_h != IntPtr.Zero) { Pawn.pawnio_close(_h); _h = IntPtr.Zero; }
        _mutex?.Dispose();
        _mutex = null;
    }

    void Locked(Action a)
    {
        if (!_mutex!.WaitOne(2000)) throw new InvalidOperationException("SMBus ocupado por outro programa.");
        try { a(); } finally { _mutex.ReleaseMutex(); }
    }

    string ReadName(int addr)
    {
        var s = new StringBuilder();
        for (int i = 0; i < 16; i++) { byte b = Read(addr, Ene.DeviceName + i); if (b == 0) break; s.Append((char)b); }
        return s.ToString();
    }

    byte Read(int addr, int reg)
    {
        if (!Xfer(addr, false, 0x00, 3, Ene.Pointer(reg), out _)) throw new IOException($"0x{addr:X2}: ponteiro 0x{reg:X4} recusado");
        if (!Xfer(addr, true, 0x81, 2, 0, out var v)) throw new IOException($"0x{addr:X2}: leitura 0x{reg:X4} falhou");
        return (byte)v;
    }

    void Write(int addr, int reg, byte value)
    {
        if (reg is not (Ene.Direct or Ene.Apply or Ene.SlotIndex or Ene.I2cAddress)) throw new InvalidOperationException($"registrador 0x{reg:X4} recusado");
        if (reg == Ene.Apply && value != 1) throw new InvalidOperationException("só aplicar; salvar nunca");
        if (!Xfer(addr, false, 0x00, 3, Ene.Pointer(reg), out _)) throw new IOException($"0x{addr:X2}: ponteiro 0x{reg:X4} recusado");
        if (!Xfer(addr, false, 0x01, 2, value, out _)) throw new IOException($"0x{addr:X2}: escrita 0x{reg:X4} falhou");
    }

    /// <summary>One LED: pointer, then a 3-byte SMBus block to command 0x03 (the most this controller takes).</summary>
    void WriteBlock(int addr, int reg, byte[] data)
    {
        if (reg < Ene.Colors || reg + data.Length > Ene.Colors + 3 * Ene.MaxLeds || data.Length != 3) throw new InvalidOperationException($"registrador 0x{reg:X4} recusado");
        if (!Xfer(addr, false, 0x00, 3, Ene.Pointer(reg), out _)) throw new IOException($"0x{addr:X2}: ponteiro 0x{reg:X4} recusado");
        // The module unpacks [length, bytes...] little-endian from the cells after the four header ones.
        ulong packed = (ulong)(byte)data.Length | (ulong)data[0] << 8 | (ulong)data[1] << 16 | (ulong)data[2] << 24;
        ulong[] input = [(ulong)addr, 0, 0x03, 5, packed, 0, 0, 0, 0];
        int hr = Pawn.pawnio_execute(_h, "ioctl_smbus_xfer", input, (nuint)input.Length, [], 0, out _);
        if (hr < 0) throw new IOException($"0x{addr:X2}: bloco 0x{reg:X4} falhou (0x{hr:X8})");
    }

    bool Xfer(int addr, bool read, int cmd, int proto, ulong data, out ulong value)
    {
        if (!Ene.AddressAllowed(addr)) throw new InvalidOperationException($"endereço 0x{addr:X2} recusado");
        ulong[] input = read ? [(ulong)addr, 1, (ulong)cmd, (ulong)proto] : [(ulong)addr, 0, (ulong)cmd, (ulong)proto, data];
        var output = new ulong[1];
        int hr = Pawn.pawnio_execute(_h, "ioctl_smbus_xfer", input, (nuint)input.Length, output, (nuint)output.Length, out _);
        value = output[0];
        return hr >= 0;
    }

    static class Pawn
    {
        // Installed with the PawnIO driver; the app's installer puts it there if missing.
        const string Lib = @"C:\Program Files\PawnIO\PawnIOLib.dll";
        [DllImport(Lib)] public static extern int pawnio_open(out IntPtr handle);
        [DllImport(Lib)] public static extern int pawnio_load(IntPtr handle, byte[] blob, nuint size);
        [DllImport(Lib, CharSet = CharSet.Ansi)] public static extern int pawnio_execute(IntPtr handle, string name, ulong[] input, nuint inSize, ulong[] output, nuint outSize, out nuint returned);
        [DllImport(Lib)] public static extern int pawnio_close(IntPtr handle);
    }
}
