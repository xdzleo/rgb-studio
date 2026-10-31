using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RgbStudio.Core;

/// <summary>
/// The Aura USB controller's reports, as OpenRGB's AuraMainboardController sends
/// them for firmware AULA3-AR32 (checked against this board's own replies). Every
/// report is 65 bytes starting 0xEC. Direct mode never sends the commit, EC 3F.
/// </summary>
public static class AuraUsbProtocol
{
    public const int ReportSize = 65;
    public const int LedsPerReport = 20;
    public const byte RequestFirmware = 0x82;
    public const byte RequestConfig = 0xB0;
    public const string SupportedFirmware = "AULA3-AR32";
    /// <summary>The mainboard's own LEDs; addressable headers use their index.</summary>
    public const byte MainboardDirectChannel = 0x04;

    static byte[] Report(params byte[] body)
    {
        var r = new byte[ReportSize];
        r[0] = 0xEC;
        body.CopyTo(r, 1);
        return r;
    }

    public static byte[] Query(byte command) => Report(command);

    /// <summary>EC 52 53 00 01, which OpenRGB sends once at start.</summary>
    public static byte[] SetGen1() => Report(0x52, 0x53, 0x00, 0x01);

    /// <summary>EC 35 channel 00 00 FF. Addressable headers are effect channels 0..n-1, the mainboard is n.</summary>
    public static byte[] DirectMode(byte effectChannel) => Report(0x35, effectChannel, 0x00, 0x00, 0xFF);

    /// <summary>EC 40 channel[|80 on the last] offset count RGB..., up to 20 LEDs per report.</summary>
    public static IEnumerable<byte[]> Direct(byte channel, IReadOnlyList<Rgb> colors)
    {
        for (int start = 0; start < colors.Count; start += LedsPerReport)
        {
            int n = Math.Min(LedsPerReport, colors.Count - start);
            var r = new byte[ReportSize];
            r[0] = 0xEC;
            r[1] = 0x40;
            r[2] = (byte)(channel | (start + n >= colors.Count ? 0x80 : 0));
            r[3] = (byte)start;
            r[4] = (byte)n;
            for (int i = 0; i < n; i++)
            {
                var c = colors[start + i];
                r[5 + i * 3] = c.R;
                r[6 + i * 3] = c.G;
                r[7 + i * 3] = c.B;
            }
            yield return r;
        }
    }

    public static string ParseFirmware(byte[] reply) =>
        reply[1] != 0x02 ? "" : new string(reply.Skip(2).Take(16).TakeWhile(b => b >= 32 && b < 127).Select(b => (char)b).ToArray());

    /// <summary>(addressable headers, mainboard LEDs), from a config-table reply; the table starts at byte 4.</summary>
    public static (int Headers, int MainboardLeds) ParseConfig(byte[] reply) =>
        reply[1] != 0x30 ? (0, 0) : (reply[4 + 0x02], reply[4 + 0x1B]);
}

/// <summary>
/// Talks to the motherboard's Aura controller (0B05:19AF) directly over USB.
/// LightingService writes over anything sent while it runs, so it is stopped while
/// this backend is open and started again on release; its startup type is not
/// touched. Memory is not on this controller: it stays dark to this backend.
/// </summary>
public sealed class HidAuraBackend : IRgbBackend
{
    const string InterfaceGuid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";
    const int ArgbMaxLeds = 120;
    static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    FileStream? _hid;
    bool _serviceStopped;
    readonly List<byte> _directChannels = [];
    readonly List<Rgb[]> _last = [];
    readonly List<long> _lastSent = [];
    readonly List<int> _sentLength = [];
    readonly bool _manageService;

    public HidAuraBackend(bool manageService = true) => _manageService = manageService;

    public string Name => "USB direto (controlador Aura da placa)";

    public IReadOnlyList<DeviceInfo> Open()
    {
        _hid = OpenController() ?? throw new InvalidOperationException("Controlador Aura USB (0B05:19AF) não encontrado.");

        // Identify before writing anything: other firmwares number their channels differently.
        var firmware = AuraUsbProtocol.ParseFirmware(Ask(AuraUsbProtocol.RequestFirmware));
        var (headers, boardLeds) = AuraUsbProtocol.ParseConfig(Ask(AuraUsbProtocol.RequestConfig));
        Log.Write($"Controlador: firmware {firmware}, {headers} conectores ARGB, {boardLeds} LEDs na placa");
        if (!firmware.StartsWith(AuraUsbProtocol.SupportedFirmware, StringComparison.Ordinal) || headers is < 1 or > 8 || boardLeds is < 0 or > 20)
            throw new InvalidOperationException($"Firmware \"{firmware}\" não testado neste app: nada foi enviado.");

        if (_manageService)
        {
            ServiceControl.Stop();
            _serviceStopped = true;
        }

        Send(AuraUsbProtocol.SetGen1());
        var devices = new List<DeviceInfo>();
        if (boardLeds > 0)
        {
            Send(AuraUsbProtocol.DirectMode((byte)headers));
            devices.Add(Add("Placa-mãe (Back IO)", 0x00010000, boardLeds, AuraUsbProtocol.MainboardDirectChannel, devices.Count));
        }
        for (int h = 0; h < headers; h++)
        {
            Send(AuraUsbProtocol.DirectMode((byte)h));
            devices.Add(Add($"Conector ARGB {h + 1}", 0x00011000, ArgbMaxLeds, (byte)h, devices.Count));
        }
        return devices;
    }

    DeviceInfo Add(string name, uint type, int leds, byte channel, int index)
    {
        _directChannels.Add(channel);
        _last.Add(new Rgb[leds]);
        _lastSent.Add(0);
        _sentLength.Add(leds);   // the first frame clears the whole channel
        return new DeviceInfo(index, name, type, leds);
    }

    public void Write(int device, ReadOnlySpan<Rgb> colors)
    {
        long now = Stopwatch.GetTimestamp();
        bool due = Stopwatch.GetElapsedTime(_lastSent[device], now) >= Refresh;
        if (!due && colors.SequenceEqual(_last[device])) return;   // nothing new; still refreshed once a second
        colors.CopyTo(_last[device]);

        // A dark tail needs no reports (120 LEDs cost 6 per frame): send up to the last lit
        // LED, or up to where the previous frame reached, so what was lit there goes dark.
        int lit = colors.Length;
        while (lit > 0 && colors[lit - 1] == Rgb.Black) lit--;
        int count = Math.Max(lit, _sentLength[device]);
        _sentLength[device] = lit;
        if (count > 0)
            foreach (var report in AuraUsbProtocol.Direct(_directChannels[device], new ArraySegment<Rgb>(_last[device], 0, count))) Send(report);
        _lastSent[device] = now;
    }

    public void Release()
    {
        if (!_serviceStopped) return;
        _serviceStopped = false;
        ServiceControl.Start();
    }

    public void Dispose()
    {
        Release();
        _hid?.Dispose();
        _hid = null;
    }

    void Send(byte[] report)
    {
        if (!_hid!.WriteAsync(report).AsTask().Wait(1000)) throw new IOException("O controlador não aceitou a escrita em 1 s.");
    }

    byte[] Ask(byte command)
    {
        Send(AuraUsbProtocol.Query(command));
        var reply = new byte[AuraUsbProtocol.ReportSize];
        if (!_hid!.ReadAsync(reply).AsTask().Wait(2000)) throw new IOException($"Sem resposta ao comando 0x{command:X2}.");
        return reply;
    }

    /// <summary>The registry also keeps instances of unplugged devices; only a present one opens.</summary>
    static FileStream? OpenController()
    {
        const string key = @"SYSTEM\CurrentControlSet\Enum\HID\VID_0B05&PID_19AF&MI_02";
        using var hid = Registry.LocalMachine.OpenSubKey(key);
        foreach (var instance in hid?.GetSubKeyNames() ?? [])
        {
            var path = $@"\\?\HID#VID_0B05&PID_19AF&MI_02#{instance}#{InterfaceGuid}";
            var handle = CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            if (!handle.IsInvalid) return new FileStream(handle, FileAccess.ReadWrite, AuraUsbProtocol.ReportSize, isAsync: true);
            handle.Dispose();
        }
        return null;
    }
}

/// <summary>Stops and starts LightingService; its startup type is never touched.</summary>
public static class ServiceControl
{
    const string ServiceName = "LightingService";

    public static void Stop()
    {
        Sc($"stop {ServiceName}", out _);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            Sc($"query {ServiceName}", out var text);
            if (text.Contains("STOPPED", StringComparison.Ordinal))
            {
                Log.Write($"{ServiceName} parado.");
                return;
            }
            Thread.Sleep(250);
        }
        throw new InvalidOperationException($"Não consegui parar o {ServiceName}; ele escreveria por cima das cores.");
    }

    public static void Start() => Log.Write($"{ServiceName} religado (sc {Sc($"start {ServiceName}", out _)}).");

    static int Sc(string args, out string output)
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(10_000);
        return p.ExitCode;
    }
}