using System.Diagnostics;

namespace RgbStudio.Core;

/// <summary>
/// Runs the selected effect on every enabled device, frame after frame, on
/// one thread of its own (an STA: the Aura SDK is a COM object and stays on
/// the thread that created it). The UI changes <see cref="Settings"/> and
/// reads <see cref="Frame"/> for its preview.
/// </summary>
public sealed class Engine : IDisposable
{
    readonly Func<IRgbBackend> _makeBackend;
    readonly object _gate = new();
    Thread? _thread;
    volatile bool _stop;
    AppSettings _settings;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    int _seconds;

    public IReadOnlyList<DeviceInfo> Devices { get; private set; } = [];
    public string BackendName { get; private set; } = "";
    public string? Error { get; private set; }
    public double ActualFps { get; private set; }

    /// <summary>The last frame, per device index; replaced whole each frame.</summary>
    public IReadOnlyDictionary<int, Rgb[]> Frame { get; private set; } = new Dictionary<int, Rgb[]>();

    public event Action? Started;

    public Engine(AppSettings settings, Func<IRgbBackend> makeBackend)
    {
        _settings = settings;
        _makeBackend = makeBackend;
    }

    public AppSettings Settings
    {
        get { lock (_gate) return _settings; }
        set { lock (_gate) _settings = value; }
    }

    public void Start()
    {
        if (_thread is not null) return;
        _stop = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "RGB engine" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Stops and gives the lights back to Armoury Crate.</summary>
    public void Stop()
    {
        _stop = true;
        _thread?.Join(8000);   // the SDK can take seconds to answer
        _thread = null;
    }

    void Run()
    {
        IRgbBackend? backend = null;
        try
        {
            Log.Write("Motor iniciando: abrindo o backend...");
            backend = _makeBackend();
            Devices = backend.Open();
            BackendName = backend.Name;
            Error = null;
            Log.Write($"{BackendName}: {Devices.Count} dispositivos: " +
                string.Join(", ", Devices.Select(d => $"{d.Name} ({d.MaxLeds})")));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Write($"Falha ao abrir {backend?.Name}: {ex}");
            backend?.Dispose();
            Started?.Invoke();
            return;
        }
        Started?.Invoke();

        try
        {
            var frameTimer = Stopwatch.StartNew();
            int frames = 0;
            while (!_stop)
            {
                var start = _clock.Elapsed;
                RenderFrame(backend, start.TotalSeconds);
                frames++;
                if (frameTimer.Elapsed.TotalSeconds >= 1)
                {
                    ActualFps = frames / frameTimer.Elapsed.TotalSeconds;
                    if (++_seconds % 10 == 1) Log.Write($"{ActualFps:0.0} quadros/s, efeito {Settings.Resolve(null).Name}");
                    frames = 0;
                    frameTimer.Restart();
                }
                var budget = TimeSpan.FromSeconds(1.0 / Math.Clamp(Settings.Fps, 1, 60));
                var left = budget - (_clock.Elapsed - start);
                if (left > TimeSpan.Zero) Thread.Sleep(left);
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Write($"Parou com erro: {ex}");
        }
        finally
        {
            Log.Write("Devolvendo as luzes ao Armoury Crate.");
            backend.Release();
            backend.Dispose();
        }
    }

    /// <summary>One frame: every device's colors, written to the backend. Public for tests.</summary>
    public void RenderFrame(IRgbBackend backend, double t)
    {
        var s = Settings;
        var frame = new Dictionary<int, Rgb[]>();
        foreach (var d in Devices) frame[d.Index] = new Rgb[d.MaxLeds];   // LEDs past a strip's length stay black

        // Lengths: what the person set for each strip, else all the LEDs it reports.
        var lengths = Devices.Select(d =>
        {
            var ds = s.For(d.Name);
            return ds.Enabled ? (ds.LedCount > 0 ? Math.Min(ds.LedCount, d.MaxLeds) : d.MaxLeds) : 0;
        }).ToArray();

        // Devices with no effect of their own share the active one; chained, as one long strip.
        var shared = Devices.Where(d => s.For(d.Name).Preset is null).ToList();
        if (s.Chain && shared.Count > 0)
        {
            var preset = s.Resolve(null);
            var all = new Rgb[shared.Sum(d => lengths[d.Index])];
            EffectCatalog.Render(EffectCatalog.Get(preset.Effect), all, t, preset.ToParams(), seed: 0);
            int at = 0;
            foreach (var d in shared)
            {
                Array.Copy(all, at, frame[d.Index], 0, lengths[d.Index]);
                at += lengths[d.Index];
            }
        }

        foreach (var d in Devices)
        {
            var own = s.For(d.Name).Preset;
            if (own is null && s.Chain) continue;   // drawn above
            var preset = s.Resolve(own);
            EffectCatalog.Render(EffectCatalog.Get(preset.Effect), frame[d.Index].AsSpan(0, lengths[d.Index]), t,
                preset.ToParams(), seed: d.Index * 1009);
        }

        // The ceiling comes last, so no effect or setting can get past it.
        if (s.PowerLimit < 100)
        {
            double limit = Math.Clamp(s.PowerLimit, 0, 100) / 100.0;
            foreach (var colors in frame.Values)
                for (int k = 0; k < colors.Length; k++) colors[k] = colors[k].Scale(limit);
        }

        // Counting LEDs by eye overrides the zone, even one that is off, whatever the limits say.
        if (s.LocatorZone is { } counting && Devices.FirstOrDefault(d => d.Name == counting) is { } target)
        {
            var colors = frame[target.Index];
            Array.Clear(colors);
            int upTo = Math.Clamp(s.LocatorLed, 1, colors.Length);
            for (int k = 0; k < upTo - 1; k++) colors[k] = new Rgb(0, 120, 0);
            colors[upTo - 1] = new Rgb(220, 220, 220);
        }

        // The screen keeps the frame as designed; the LEDs get it corrected and within budget.
        foreach (var (i, colors) in frame) backend.Write(i, ForLeds(colors, Devices[i], s));
        Frame = frame;
    }

    static readonly byte[] GammaTable = Enumerable.Range(0, 256)
        .Select(v => (byte)Math.Round(Math.Pow(v / 255.0, 2.2) * 255)).ToArray();

    /// <summary>A WS2812 at full white is commonly rated about 60 mA: 20 mA per channel, as a worst case.</summary>
    const double MilliampsPerChannel = 20;

    /// <summary>Estimated draw of a set of colors, in mA.</summary>
    public static double EstimateMilliamps(ReadOnlySpan<Rgb> colors)
    {
        long sum = 0;
        foreach (var c in colors) sum += c.R + c.G + c.B;
        return sum / 255.0 * MilliampsPerChannel;
    }

    static Rgb Calibrate(Rgb c, DeviceSettings z) => new(
        (byte)(c.R * Math.Clamp(z.RedGain, 0, 100) / 100),
        (byte)(c.G * Math.Clamp(z.GreenGain, 0, 100) / 100),
        (byte)(c.B * Math.Clamp(z.BlueGain, 0, 100) / 100));

    static Rgb[] ForLeds(Rgb[] colors, DeviceInfo device, AppSettings s)
    {
        var output = colors;
        if (s.Gamma)
        {
            output = new Rgb[colors.Length];
            for (int k = 0; k < colors.Length; k++)
                output[k] = new Rgb(GammaTable[colors[k].R], GammaTable[colors[k].G], GammaTable[colors[k].B]);
        }
        var zone = s.For(device.Name);
        if (zone.RedGain != 100 || zone.GreenGain != 100 || zone.BlueGain != 100)
        {
            var balanced = new Rgb[output.Length];
            for (int k = 0; k < output.Length; k++) balanced[k] = Calibrate(output[k], zone);
            output = balanced;
        }
        if (s.HeaderMilliamps > 0 && device.IsAddressableStrip)
        {
            double draw = EstimateMilliamps(output);
            if (draw > s.HeaderMilliamps)
            {
                double keep = s.HeaderMilliamps / draw;
                var limited = new Rgb[output.Length];
                for (int k = 0; k < output.Length; k++) limited[k] = output[k].Scale(keep);
                output = limited;
            }
        }
        return output;
    }

    public void Dispose() => Stop();
}
