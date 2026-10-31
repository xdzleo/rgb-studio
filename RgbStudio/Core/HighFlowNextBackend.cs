using System.Diagnostics;

namespace RgbStudio.Core;

/// <summary>
/// What the sensor tab and the engine share: the open sensor, the lighting it had of its own
/// (its own effects, which the screen edits), and whether the ring runs those or this app's.
/// </summary>
public sealed class HighFlowNextLink : IDisposable
{
    public HighFlowNext? Device { get; private set; }
    /// <summary>The sensor's own lighting: what it runs when it is not following this app.</summary>
    public HighFlowNextConfig? Native { get; set; }
    /// <summary>True: the ring runs the sensor's own effect. False: this app's frames.</summary>
    public volatile bool UseNative;

    public bool Open()
    {
        if (Device is not null) return true;
        try
        {
            Device = HighFlowNext.Open();
            if (Device is null) return false;
            Native = Device.Read();
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"high flow NEXT: {ex.Message}");
            Device?.Dispose();
            Device = null;
            return false;
        }
    }

    public void Dispose() { Device?.Dispose(); Device = null; }
}

/// <summary>
/// The high flow NEXT's ring as one more zone for the engine. The sensor takes no colors per
/// LED, so its 8 LED groups are spread over the ring's 10 LEDs, each set to a fixed color,
/// and the settings are resent for every frame (30/s held on hardware with no refusals).
/// Frames go to the sensor's memory only, never saved. Releasing puts its own lighting back.
/// </summary>
public sealed class HighFlowNextBackend : IRgbBackend
{
    public const string DeviceName = "high flow NEXT (anel)";
    public const uint DeviceType = 0x000A0000;
    // 11 positions: with 0-9 one LED stayed dark on the hardware. A position past the real end is ignored.
    const int RingLeds = 11;
    static readonly int[] GroupSizes = [2, 1, 1, 1, 2, 1, 1, 2];   // 8 groups, 11 positions
    static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1), Rebase = TimeSpan.FromSeconds(30);

    readonly HighFlowNextLink _link;
    HighFlowNextConfig? _baseline, _frame;
    readonly Rgb[] _last = new Rgb[GroupSizes.Length];
    long _lastSent, _lastRebase;
    bool _streaming, _nativeShown;
    string? _lastError;

    public HighFlowNextBackend(HighFlowNextLink link) => _link = link;

    public string Name => "high flow NEXT";

    public IReadOnlyList<DeviceInfo> Open()
    {
        if (!_link.Open() || _link.Native is null) return [];
        _baseline = _link.Native.Clone();
        _frame = Layout(_baseline);
        _lastRebase = Stopwatch.GetTimestamp();
        return [new DeviceInfo(0, DeviceName, DeviceType, RingLeds)];
    }

    /// <summary>The 8 LED groups laid over the ring, each a fixed color. The sensor's brightness stays its own.</summary>
    public static HighFlowNextConfig Layout(HighFlowNextConfig from)
    {
        var f = from.Clone();
        int start = 0;
        for (int g = 0; g < GroupSizes.Length; g++)
        {
            var c = f[g];
            c.Strip = 1; c.FirstLed = start; c.Count = GroupSizes[g]; c.Mode = 1;
            start += GroupSizes[g];
        }
        return f;
    }

    /// <summary>One color per group: the average of the ring LEDs it covers.</summary>
    public static Rgb[] Groups(ReadOnlySpan<Rgb> ring)
    {
        var groups = new Rgb[GroupSizes.Length];
        int at = 0;
        for (int g = 0; g < GroupSizes.Length; g++)
        {
            int r = 0, gr = 0, b = 0, n = GroupSizes[g];
            for (int k = 0; k < n; k++)
            {
                var c = at + k < ring.Length ? ring[at + k] : Rgb.Black;
                r += c.R; gr += c.G; b += c.B;
            }
            groups[g] = new Rgb((byte)(r / n), (byte)(gr / n), (byte)(b / n));
            at += n;
        }
        return groups;
    }

    public void Write(int device, ReadOnlySpan<Rgb> colors)
    {
        var dev = _link.Device;
        if (dev is null || _frame is null || _baseline is null) return;
        try
        {
            if (_link.UseNative)
            {
                if (!_nativeShown && _link.Native is { } native) { dev.Write(native); _nativeShown = true; }
                _streaming = false;
                return;
            }
            _nativeShown = false;

            long now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(_lastRebase, now) >= Rebase) RebaseOn(dev);

            var groups = Groups(colors);
            bool due = !_streaming || Stopwatch.GetElapsedTime(_lastSent, now) >= Refresh;
            if (!due && groups.AsSpan().SequenceEqual(_last)) return;
            groups.CopyTo(_last, 0);
            for (int g = 0; g < groups.Length; g++)
                for (int s = 0; s < _frame[g].Slots; s++) _frame[g].SetColor(s, groups[g]);
            dev.Stream(_baseline, _frame);
            _streaming = true;
            _lastSent = now;
            _lastError = null;
        }
        catch (Exception ex)
        {
            // A frame the sensor refuses is skipped; the next one tries again.
            if (ex.Message != _lastError) Log.Write($"high flow NEXT: {ex.Message}");
            _lastError = ex.Message;
        }
    }

    /// <summary>
    /// Takes in what changed outside the lighting meanwhile (aquasuite may change alarms or
    /// calibration), so the frames do not keep writing the old values back.
    /// </summary>
    void RebaseOn(HighFlowNext dev)
    {
        _lastRebase = Stopwatch.GetTimestamp();
        var live = dev.Read();
        var frame = Layout(live);
        if (_link.Native is { } native)
        {
            // The sensor's own lighting is ours to keep; everything else follows the sensor.
            var report = live.ToReport();
            var ownLighting = native.ToReport();
            for (int i = HighFlowNextConfig.StripBrightness; i < HighFlowNextConfig.FirstController + HighFlowNextConfig.Controllers * HighFlowNextConfig.ControllerSize; i++)
                report[i] = ownLighting[i];
            _baseline = HighFlowNextConfig.Parse(FixCrc(report));
            frame = Layout(_baseline);
        }
        _frame = frame;
        _streaming = false;   // resend everything on the next frame
    }

    static byte[] FixCrc(byte[] report)
    {
        ushort crc = HighFlowNextConfig.Crc16Usb(report.AsSpan(1, HighFlowNextConfig.CrcOffset - 1));
        report[HighFlowNextConfig.CrcOffset] = (byte)(crc >> 8);
        report[HighFlowNextConfig.CrcOffset + 1] = (byte)crc;
        return report;
    }

    /// <summary>Puts the sensor's own lighting back.</summary>
    public void Release()
    {
        if (!_streaming) return;
        _streaming = false;
        try { if (_link.Native is { } native) _link.Device?.Write(native); }
        catch (Exception ex) { Log.Write($"high flow NEXT: {ex.Message}"); }
    }

    public void Dispose() => Release();
}

/// <summary>Several backends as one: their devices one after another.</summary>
public sealed class CompositeBackend(params IRgbBackend[] parts) : IRgbBackend
{
    readonly List<(IRgbBackend Backend, int Index)> _map = [];

    public string Name => string.Join(" + ", parts.Select(p => p.Name));

    public IReadOnlyList<DeviceInfo> Open()
    {
        var devices = new List<DeviceInfo>();
        foreach (var part in parts)
            foreach (var d in part.Open())
            {
                _map.Add((part, d.Index));
                devices.Add(d with { Index = devices.Count });
            }
        return devices;
    }

    public void Write(int device, ReadOnlySpan<Rgb> colors)
    {
        var (backend, index) = _map[device];
        backend.Write(index, colors);
    }

    public void Release() { foreach (var p in parts) p.Release(); }
    public void Dispose() { foreach (var p in parts) p.Dispose(); }
}
