namespace RgbStudio.Core;

/// <summary>What an effect draws with: two colors, speed and brightness.</summary>
public sealed class EffectParams
{
    public Rgb Color1 { get; set; } = new(255, 0, 40);
    public Rgb Color2 { get; set; } = new(0, 120, 255);
    /// <summary>1 is the default pace; 0.1 to 5.</summary>
    public double Speed { get; set; } = 1;
    /// <summary>0 to 100.</summary>
    public int Brightness { get; set; } = 100;
    public bool Reverse { get; set; }
    /// <summary>A list of colors, for effects that take one (Sequência).</summary>
    public IReadOnlyList<Rgb> Colors { get; set; } = [];
    /// <summary>Sequência: moving bands instead of fading the whole strip.</summary>
    public bool Bands { get; set; }
}

/// <summary>
/// One lighting effect. <see cref="Render"/> fills <paramref name="leds"/> for
/// one strip at time <paramref name="t"/> (seconds since the effect started).
/// Effects are pure: same inputs, same colors, so they are tested without hardware.
/// </summary>
public abstract class Effect
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public virtual string Glyph => "●";
    /// <summary>The gallery tab it shows under.</summary>
    public virtual string Category => "Clássicos";
    /// <summary>One line for the inspector.</summary>
    public virtual string Blurb => "";
    public virtual string DefaultColor1 => "#FF0028";
    public virtual string DefaultColor2 => "#0078FF";
    /// <summary>Whether the second color does anything (the UI hides it otherwise).</summary>
    public virtual bool UsesColor2 => false;
    public virtual bool UsesColor1 => true;
    /// <summary>Takes a list of colors instead of Cor 1 / Cor 2.</summary>
    public virtual bool UsesColors => false;
    /// <summary>Whether direction means anything for it.</summary>
    public virtual bool UsesDirection => false;
    /// <summary>Whether speed means anything for it.</summary>
    public virtual bool UsesSpeed => true;

    public abstract void Render(Span<Rgb> leds, double t, EffectParams p, int seed);

    protected static double Pos(int i, int n, bool reverse)
    {
        double x = n <= 1 ? 0 : (double)i / n;
        return reverse ? 1 - x : x;
    }
}

public sealed class StaticEffect : Effect
{
    public override string Blurb => "Uma cor só, firme.";
    public override bool UsesSpeed => false;
    public override string Id => "static";
    public override string Name => "Estático";
    public override string Glyph => "●";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed) => leds.Fill(p.Color1);
}

public sealed class BreathingEffect : Effect
{
    public override string Blurb => "Acende e apaga devagar, alternando as duas cores.";
    public override string Id => "breathing";
    public override string Name => "Respiração";
    public override string Glyph => "◐";
    public override bool UsesColor2 => true;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        // A full in-and-out every 4 s at speed 1; alternates colors each breath.
        double phase = t * p.Speed / 4;
        double level = (1 - Math.Cos(phase * 2 * Math.PI)) / 2;
        var c = ((int)Math.Floor(phase) % 2 == 0 ? p.Color1 : p.Color2).Scale(level);
        leds.Fill(c);
    }
}

public sealed class ColorCycleEffect : Effect
{
    public override string Blurb => "A fita inteira passa por todas as cores.";
    public override string Id => "cycle";
    public override string Name => "Ciclo de cor";
    public override string Glyph => "◎";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed) =>
        leds.Fill(Rgb.FromHsv(t * p.Speed * 36, 1, 1));
}

public sealed class RainbowEffect : Effect
{
    public override string Blurb => "O arco-íris completo andando pela fita.";
    public override bool UsesDirection => true;
    public override string Id => "rainbow";
    public override string Name => "Arco-íris";
    public override string Glyph => "◠";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        for (int i = 0; i < leds.Length; i++)
            leds[i] = Rgb.FromHsv(Pos(i, leds.Length, p.Reverse) * 360 + t * p.Speed * 90, 1, 1);
    }
}

public sealed class WaveEffect : Effect
{
    public override string Blurb => "Duas cores que se misturam numa onda contínua.";
    public override bool UsesDirection => true;
    public override string Id => "wave";
    public override string Name => "Onda";
    public override string Glyph => "∿";
    public override bool UsesColor2 => true;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        for (int i = 0; i < leds.Length; i++)
        {
            double x = Pos(i, leds.Length, p.Reverse) * 2 * Math.PI - t * p.Speed * 3;
            leds[i] = Rgb.MixPerceptual(p.Color1, p.Color2, (Math.Sin(x) + 1) / 2);
        }
    }
}

public sealed class CometEffect : Effect
{
    public override string Blurb => "Um ponto brilhante com cauda dando voltas.";
    public override bool UsesDirection => true;
    public override string Id => "comet";
    public override string Name => "Cometa";
    public override string Glyph => "☄";
    public override bool UsesColor2 => true;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        if (n == 0) return;
        double head = (t * p.Speed * 0.6 % 1) * n;
        double tail = Math.Max(3, n / 4.0);
        for (int i = 0; i < n; i++)
        {
            int idx = p.Reverse ? n - 1 - i : i;
            double d = head - i;
            if (d < 0) d += n;                       // wraps around the strip
            double level = d < tail ? 1 - d / tail : 0;
            leds[idx] = Rgb.Lerp(p.Color2.Scale(0.08), p.Color1, level);
        }
    }
}

public sealed class StarryNightEffect : Effect
{
    public override string Blurb => "Cada LED cintila no seu próprio ritmo.";
    public override string Id => "starry";
    public override string Name => "Noite estrelada";
    public override string Glyph => "✦";
    public override bool UsesColor2 => true;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        // Every LED twinkles on its own period and phase, derived from its index.
        for (int i = 0; i < leds.Length; i++)
        {
            uint h = Hash((uint)(i * 7919 + seed));
            double period = 1.5 + (h % 1000) / 1000.0 * 3;   // 1.5 to 4.5 s
            double phase = (h >> 10) % 1000 / 1000.0;
            double w = (t * p.Speed / period + phase) % 1;
            double level = w < 0.5 ? Math.Pow(Math.Sin(w * 2 * Math.PI), 4) : 0;
            leds[i] = Rgb.Lerp(p.Color2.Scale(0.12), p.Color1, level);
        }
    }

    static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
        return x;
    }
}

public sealed class StrobeEffect : Effect
{
    public override string Blurb => "Flashes rápidos. Evite se você tem sensibilidade a luz piscando.";
    public override string Id => "strobe";
    public override string Name => "Estroboscópio";
    public override string Glyph => "✸";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double f = t * p.Speed * 4 % 1;
        leds.Fill(f < 0.15 ? p.Color1 : Rgb.Black);
    }
}

public sealed class GradientEffect : Effect
{
    public override string Blurb => "Transição parada de uma cor para a outra.";
    public override bool UsesSpeed => false;
    public override bool UsesDirection => true;
    public override string Id => "gradient";
    public override string Name => "Gradiente";
    public override string Glyph => "▤";
    public override bool UsesColor2 => true;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        for (int i = 0; i < leds.Length; i++)
            leds[i] = Rgb.MixPerceptual(p.Color1, p.Color2, Pos(i, leds.Length, p.Reverse));
    }
}

/// <summary>
/// Any number of colors: the whole strip holds each one and fades to the next,
/// or (Bands) the colors lie along the strip as bands that move.
/// </summary>
public sealed class SequenceEffect : Effect
{
    public override string Blurb => "Quantas cores você quiser, em transição ou em faixas.";
    public override string Id => "sequence";
    public override string Name => "Sequência";
    public override string Glyph => "▦";
    public override bool UsesColor1 => false;
    public override bool UsesColors => true;
    public override bool UsesDirection => true;

    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        IReadOnlyList<Rgb> colors = p.Colors.Count > 0 ? p.Colors : [p.Color1, p.Color2];
        int n = colors.Count;
        if (!p.Bands)
        {
            // Each color holds for 60% of its 2 s turn (at speed 1), then fades into the next.
            double x = t * p.Speed / 2;
            int k = (int)Math.Floor(x) % n;
            double f = x - Math.Floor(x);
            double mix = f < 0.6 ? 0 : (f - 0.6) / 0.4;
            leds.Fill(Rgb.MixPerceptual(colors[k], colors[(k + 1) % n], mix));
            return;
        }
        for (int i = 0; i < leds.Length; i++)
        {
            // One full set of bands along the strip, sliding one band per second at speed 1.
            double pos = Pos(i, leds.Length, p.Reverse) * n - t * p.Speed;
            pos = (pos % n + n) % n;
            int k = (int)Math.Floor(pos);
            double f = pos - k;
            double mix = f < 0.8 ? 0 : (f - 0.8) / 0.2;   // a soft edge between bands
            leds[i] = Rgb.MixPerceptual(colors[k % n], colors[(k + 1) % n], mix);
        }
    }
}

public sealed class OffEffect : Effect
{
    public override string Blurb => "Apaga esta zona.";
    public override bool UsesSpeed => false;
    public override string Id => "off";
    public override string Name => "Desligado";
    public override string Glyph => "⊘";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed) => leds.Fill(Rgb.Black);
}

public static class EffectCatalog
{
    public static readonly IReadOnlyList<Effect> All =
    [
        new StaticEffect(), new BreathingEffect(), new ColorCycleEffect(), new RainbowEffect(),
        new WaveEffect(), new CometEffect(), new StarryNightEffect(), new StrobeEffect(),
        new GradientEffect(), new SequenceEffect(), new OffEffect(),
        new AuroraEffect(), new FireEffect(), new PlasmaEffect(), new OceanEffect(), new LavaEffect(),
        new CollisionEffect(), new MeteorsEffect(), new HeartbeatEffect(), new StormEffect(), new DigitalRainEffect(),
        new BouncingEffect(), new ScannerEffect(), new GalaxyEffect(), new PendulumEffect(), new FirefliesEffect(),
        new KaleidoscopeEffect(), new BioluminescenceEffect(), new EclipseEffect(), new SpectrumEffect(),
        new FireworksEffect(), new HourglassEffect(), new DripEffect(), new StackingEffect(), new CandleEffect(), new SunriseEffect(),
        new AutomatonEffect(), new SortingEffect(), new FractalEffect(), new InterferenceEffect(), new StringEffect(), new SynchronyEffect(),
        new BlackHoleEffect(), new SupernovaEffect(), new PulsarEffect(), new DnaEffect(), new BinaryEffect(), new TeslaEffect(),
        new PoliceEffect(), new BeaconEffect(), new AmbulanceEffect(), new HazardEffect(), new TrafficLightEffect(), new HighwayEffect(), new BrokenNeonEffect(), new LighthouseEffect(), new MorseEffect(),
        .. GradientCatalog.All,
    ];

    public static Effect Get(string id) => All.FirstOrDefault(e => e.Id == id) ?? All[0];

    /// <summary>Renders, then applies brightness.</summary>
    public static void Render(Effect effect, Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        effect.Render(leds, t, p, seed);
        if (p.Brightness < 100)
        {
            double f = Math.Clamp(p.Brightness, 0, 100) / 100.0;
            for (int i = 0; i < leds.Length; i++) leds[i] = leds[i].Scale(f);
        }
    }
}
