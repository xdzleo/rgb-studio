namespace RgbStudio.Core;

/// <summary>
/// Effects with real motion. Each is a pure function of LED position and time (no
/// state between frames), so a strip of 3 LEDs and one of 120 show the same scene
/// and everything can be tested without hardware.
/// </summary>
public abstract class MotionEffect : Effect
{
    public override string Category => "Motion";

    protected static double Hash(int a, int b = 0)
    {
        uint x = unchecked((uint)(a * 374761393 + b * 668265263 + 1274126177));
        x = unchecked((x ^ (x >> 13)) * 1274126177);
        x ^= x >> 16;
        return (x & 0xFFFFFF) / (double)0x1000000;
    }

    /// <summary>Smooth noise in 0..1 over (x, y).</summary>
    protected static double Noise(double x, double y, int seed = 0)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        double u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
        double a = Hash(ix + seed * 131, iy), b = Hash(ix + 1 + seed * 131, iy);
        double c = Hash(ix + seed * 131, iy + 1), d = Hash(ix + 1 + seed * 131, iy + 1);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    /// <summary>Three octaves of <see cref="Noise"/>, for detail at more than one scale.</summary>
    protected static double Fbm(double x, double y, int seed = 0) =>
        (Noise(x, y, seed) * 4 + Noise(x * 2.1, y * 2.1, seed + 7) * 2 + Noise(x * 4.3, y * 4.3, seed + 13)) / 7;

    protected static Rgb Add(Rgb a, Rgb b) => new(
        (byte)Math.Min(255, a.R + b.R),
        (byte)Math.Min(255, a.G + b.G),
        (byte)Math.Min(255, a.B + b.B));

    /// <summary>A soft spot of light at <paramref name="center"/> (in LEDs).</summary>
    protected static double Glow(int i, double center, double width)
    {
        double d = (i - center) / Math.Max(0.6, width);
        return Math.Exp(-d * d);
    }

    /// <summary>A spot's width in LEDs: scaled to the strip, but always visible on a short one.</summary>
    protected static double Width(int n, double fraction) => Math.Max(0.7, n * fraction);
}

public sealed class AuroraEffect : MotionEffect
{
    public override string Id => "aurora";
    public override string Name => "Aurora";
    public override string Blurb => "Cortinas de luz que ondulam devagar, como no céu polar.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#00FF9D";
    public override string DefaultColor2 => "#7A2BFF";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed;
        for (int i = 0; i < leds.Length; i++)
        {
            double x = (double)i / Math.Max(1, leds.Length) * 3;
            double warp = Fbm(x * 0.8, s * 0.09, seed) * 2.4;
            double curtain = Math.Pow(Math.Sin((x + warp + s * 0.05) * Math.PI) * 0.5 + 0.5, 2.2);
            double shimmer = 0.55 + 0.45 * Noise(x * 5, s * 0.5, seed + 3);
            double hue = Fbm(x * 0.6 + 9, s * 0.05, seed + 5);
            leds[i] = Rgb.Lerp(p.Color1, p.Color2, Math.Clamp((hue - 0.3) * 2.5, 0, 1)).Scale(Math.Clamp(curtain * shimmer + 0.02, 0, 1));
        }
    }
}

public sealed class FireEffect : MotionEffect
{
    public override string Id => "fire";
    public override string Name => "Fogo";
    public override string Blurb => "Chamas que sobem da base, do vermelho escuro ao miolo quase branco.";
    public override bool UsesColor1 => false;
    public override bool UsesDirection => true;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed;
        for (int i = 0; i < leds.Length; i++)
        {
            double h = Pos(i, leds.Length, p.Reverse);
            double turbulence = Fbm(h * 7 - s * 2.2, s * 0.9, seed);
            double heat = Math.Clamp(1.15 - h * 1.25 + (turbulence - 0.5) * 1.5, 0, 1);
            leds[i] = heat < 0.4 ? Rgb.Lerp(Rgb.Black, new(200, 16, 0), heat / 0.4)
                : heat < 0.75 ? Rgb.Lerp(new(200, 16, 0), new(255, 120, 0), (heat - 0.4) / 0.35)
                : Rgb.Lerp(new(255, 120, 0), new(255, 225, 130), (heat - 0.75) / 0.25);
        }
    }
}

public sealed class PlasmaEffect : MotionEffect
{
    public override string Id => "plasma";
    public override string Name => "Plasma";
    public override string Blurb => "Ondas sobrepostas de cor pura que se fundem sem nunca repetir igual.";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed;
        for (int i = 0; i < leds.Length; i++)
        {
            double x = (double)i / Math.Max(1, leds.Length);
            double v = Math.Sin(x * 10 + s) + Math.Sin(x * 7 - s * 1.3) + Math.Sin(x * 17 + Math.Sin(s * 0.7) * 3);
            leds[i] = Rgb.FromHsv(v * 60 + s * 25 + seed, 1, 0.65 + 0.35 * Math.Sin(v * 1.5));
        }
    }
}

public sealed class OceanEffect : MotionEffect
{
    public override string Id => "ocean";
    public override string Name => "Oceano";
    public override string Blurb => "Água funda com brilhos de luz passando pela superfície.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#7FFFE8";
    public override string DefaultColor2 => "#002A66";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed;
        for (int i = 0; i < leds.Length; i++)
        {
            double x = (double)i / Math.Max(1, leds.Length) * 4;
            double swell = Fbm(x * 0.7 + s * 0.15, s * 0.1, seed);
            double caustic = Math.Pow(1 - Math.Abs(Math.Sin((x + swell * 3 - s * 0.4) * Math.PI)), 5);
            leds[i] = Rgb.Lerp(p.Color2.Scale(0.35 + swell * 0.65), p.Color1, Math.Clamp(caustic, 0, 1));
        }
    }
}

public sealed class LavaEffect : MotionEffect
{
    public override string Id => "lava";
    public override string Name => "Lava";
    public override string Blurb => "Bolhas quentes que crescem, se juntam e se separam.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#FF4A00";
    public override string DefaultColor2 => "#2A0014";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed * 0.35;
        for (int i = 0; i < leds.Length; i++)
        {
            double x = (double)i / Math.Max(1, leds.Length);
            double field = 0;
            for (int b = 0; b < 5; b++)
            {
                double center = 0.5 + 0.48 * Math.Sin(s * (0.6 + Hash(b, seed) * 0.8) + Hash(seed, b) * 6.28);
                double d = (x - center) / (0.05 + Hash(b + 11, seed) * 0.08);
                field += 1 / (1 + d * d);
            }
            double heat = Math.Clamp((field - 0.55) * 1.3, 0, 1);
            leds[i] = heat > 0.82 ? Rgb.Lerp(p.Color1, new(255, 220, 120), (heat - 0.82) / 0.18) : Rgb.Lerp(p.Color2, p.Color1, heat / 0.82);
        }
    }
}

public sealed class CollisionEffect : MotionEffect
{
    public override string Id => "collision";
    public override string Name => "Colisão";
    public override string Blurb => "Dois pulsos correm um contra o outro e explodem no centro.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#FF0A54";
    public override string DefaultColor2 => "#00C2FF";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        if (n == 0) return;
        double f = t * p.Speed / 2.6 % 1;
        double mid = (n - 1) / 2.0;
        const double Impact = 0.5;
        for (int i = 0; i < n; i++)
        {
            Rgb c = Rgb.Black;
            if (f < Impact)
            {
                double run = Math.Pow(f / Impact, 2);       // they accelerate into each other
                double tail = Width(n, 0.16);
                for (int k = 0; k < 4; k++)
                {
                    double back = k * 0.33 * tail, level = 1 - k * 0.27;
                    c = Add(c, p.Color1.Scale(level * Glow(i, run * mid - back, tail * 0.3)));
                    c = Add(c, p.Color2.Scale(level * Glow(i, n - 1 - run * mid + back, tail * 0.3)));
                }
            }
            else
            {
                double blast = (f - Impact) / (1 - Impact);
                double front = Math.Sqrt(blast) * (mid + 1);
                double ring = Glow(i, mid + front, Width(n, 0.07)) + Glow(i, mid - front, Width(n, 0.07));
                double core = Glow(i, mid, Width(n, 0.18)) * Math.Pow(1 - blast, 3);
                var mix = Rgb.Lerp(p.Color1, p.Color2, i < mid ? 0 : 1);
                c = Add(mix.Scale(Math.Clamp(ring, 0, 1) * (1 - blast * 0.8)), Rgb.White.Scale(Math.Clamp(core, 0, 1)));
            }
            leds[i] = c;
        }
    }
}

public sealed class MeteorsEffect : MotionEffect
{
    public override string Id => "meteors";
    public override string Name => "Meteoros";
    public override string Blurb => "Chuva de cabeças brilhantes com caudas que se apagam em faíscas.";
    public override bool UsesColor2 => true;
    public override bool UsesDirection => true;
    public override string DefaultColor1 => "#FFE9B0";
    public override string DefaultColor2 => "#FF5A1F";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Clear();
        if (n == 0) return;
        double tail = Math.Max(2.5, n * 0.28);
        for (int m = 0; m < 4; m++)
        {
            double rate = (0.32 + Hash(m, seed) * 0.34) * p.Speed;
            double clock = t * rate + Hash(seed, m + 3);
            int cycle = (int)Math.Floor(clock);
            double head = (clock - cycle) * (n + tail) ;
            for (int k = 0; k < n; k++)
            {
                double d = head - k;
                if (d < 0 || d > tail) continue;
                double level = 1 - d / tail;
                double spark = d > tail * 0.3 && Hash(k * 3 + cycle, (int)(t * 18)) > 0.75 ? 0.35 : 1;
                int at = p.Reverse ? n - 1 - k : k;
                var c = (d < 1.5 ? p.Color1 : Rgb.Lerp(p.Color2, p.Color1, level * 0.5)).Scale(level * level * spark);
                leds[at] = Add(leds[at], c);
            }
        }
    }
}

public sealed class HeartbeatEffect : MotionEffect
{
    public override string Id => "heartbeat";
    public override string Name => "Batimento";
    public override string Blurb => "Batida dupla de coração que se espalha do centro para as pontas.";
    public override string DefaultColor1 => "#FF0033";
    static double Beat(double f)
    {
        f = (f % 1 + 1) % 1;
        double one = Math.Exp(-Math.Pow((f - 0.08) / 0.045, 2));
        double two = 0.7 * Math.Exp(-Math.Pow((f - 0.27) / 0.06, 2));
        return one + two;
    }
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double f = t * p.Speed / 1.15;
        double mid = (leds.Length - 1) / 2.0;
        for (int i = 0; i < leds.Length; i++)
        {
            double d = mid <= 0 ? 0 : Math.Abs(i - mid) / mid;
            double level = Beat(f - d * 0.28) * (1 - d * 0.55);
            leds[i] = Add(p.Color1.Scale(Math.Clamp(level, 0, 1)), p.Color1.Scale(0.03));
        }
    }
}

public sealed class StormEffect : MotionEffect
{
    public override string Id => "storm";
    public override string Name => "Tempestade";
    public override string Blurb => "Nuvens carregadas e relâmpagos em lugares que nunca se repetem.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#DDE8FF";
    public override string DefaultColor2 => "#2438B8";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double s = t * p.Speed;
        int window = (int)Math.Floor(s / 2.4);
        double into = s - window * 2.4 - Hash(window, seed) * 1.6;      // when in the window it strikes
        double strike = into is >= 0 and < 0.45 ? Math.Exp(-into * 7) * (Hash(window, (int)(s * 28)) > 0.3 ? 1 : 0.25) : 0;
        double where = Hash(window + 5, seed) * (n - 1), wide = Width(n, 0.08 + Hash(window + 9, seed) * 0.25);
        for (int i = 0; i < n; i++)
        {
            double x = (double)i / Math.Max(1, n);
            var cloud = p.Color2.Scale(Math.Clamp(Math.Pow(Fbm(x * 3 + s * 0.08, s * 0.12, seed), 2) * 2.6, 0.12, 1));
            double jag = Noise(i * 0.9, window, seed + 2);
            double bolt = strike * Glow(i, where, wide) * (0.5 + jag * 0.5);
            leds[i] = Add(cloud, p.Color1.Scale(Math.Clamp(bolt, 0, 1)));
        }
    }
}

public sealed class DigitalRainEffect : MotionEffect
{
    public override string Id => "rain";
    public override string Name => "Chuva digital";
    public override string Blurb => "Gotas que caem com a ponta acesa e o rastro desaparecendo.";
    public override bool UsesDirection => true;
    public override string DefaultColor1 => "#20FF66";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Clear();
        if (n == 0) return;
        int drops = Math.Max(2, n / 14);
        double tail = Math.Max(2, n * 0.18);
        for (int d = 0; d < drops; d++)
        {
            double clock = t * p.Speed * (0.45 + Hash(d, seed) * 0.6) + Hash(seed + 1, d);
            int cycle = (int)Math.Floor(clock);
            if (Hash(d, cycle) < 0.25) continue;      // some turns skip, so the rhythm stays irregular
            double head = (clock - cycle) * (n + tail);
            for (int k = 0; k < n; k++)
            {
                double back = head - k;
                if (back < 0 || back > tail) continue;
                double level = Math.Pow(1 - back / tail, 2.4);
                int at = p.Reverse ? n - 1 - k : k;
                var c = back < 1 ? Rgb.Lerp(p.Color1, Rgb.White, 0.75) : p.Color1.Scale(level);
                leds[at] = Add(leds[at], c);
            }
        }
    }
}

public sealed class BouncingEffect : MotionEffect
{
    public override string Id => "bounce";
    public override string Name => "Gravidade";
    public override string Blurb => "Bolas de luz caindo e quicando, cada vez mais baixo, com física de verdade.";
    public override bool UsesDirection => true;
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Clear();
        if (n == 0) return;
        for (int b = 0; b < 3; b++)
        {
            double cycle = 6.5 + Hash(b, seed) * 2;
            double time = (t * p.Speed + Hash(seed, b) * 5) % cycle;
            // Each bounce keeps 72% of its speed, so it lasts 72% as long and reaches 52% as high.
            const double Bounce = 0.72, First = 1.3;
            double start = 0, length = First, peak = 1;
            for (int guard = 0; guard < 24 && time > start + length; guard++)
            {
                start += length;
                length *= Bounce;
                peak *= Bounce * Bounce;
            }
            double f = Math.Clamp((time - start) / length, 0, 1);
            double height = peak * 4 * f * (1 - f);
            double pos = height * (n - 1);
            var c = Rgb.FromHsv(Hash(b + 40, seed) * 360 + b * 120, 0.9, 1);
            for (int i = 0; i < n; i++)
            {
                int at = p.Reverse ? n - 1 - i : i;
                leds[at] = Add(leds[at], c.Scale(Glow(i, pos, Width(n, 0.035))));
            }
        }
    }
}

public sealed class ScannerEffect : MotionEffect
{
    public override string Id => "scanner";
    public override string Name => "Scanner";
    public override string Blurb => "Um feixe que varre de ponta a ponta e deixa um rastro quente.";
    public override string DefaultColor1 => "#FF1020";
    static double Head(double t)
    {
        double f = (t % 2 + 2) % 2;
        double tri = f < 1 ? f : 2 - f;
        return tri * tri * (3 - 2 * tri);     // slows into each end, like something with weight
    }
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double s = t * p.Speed * 0.8;
        for (int i = 0; i < n; i++)
        {
            double level = 0;
            for (int k = 0; k < 7; k++)
                level = Math.Max(level, Math.Pow(0.62, k) * Glow(i, Head(s - k * 0.035) * (n - 1), Width(n, 0.028)));
            leds[i] = p.Color1.Scale(Math.Clamp(level, 0, 1));
        }
    }
}

public sealed class GalaxyEffect : MotionEffect
{
    public override string Id => "galaxy";
    public override string Name => "Galáxia";
    public override string Blurb => "Braços de nebulosa girando devagar, com estrelas piscando no meio.";
    public override bool UsesColor2 => true;
    public override bool UsesDirection => true;
    public override string DefaultColor1 => "#B04CFF";
    public override string DefaultColor2 => "#10C8FF";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed;
        for (int i = 0; i < leds.Length; i++)
        {
            double x = Pos(i, leds.Length, p.Reverse);
            double arms = Math.Pow(Math.Cos(x * Math.PI * 4 - s * 0.9) * 0.5 + 0.5, 2.5);
            double dust = Fbm(x * 5 - s * 0.14, s * 0.08, seed);
            var nebula = Rgb.Lerp(p.Color1, p.Color2, Math.Clamp(dust * 1.6 - 0.2, 0, 1)).Scale(arms * (0.3 + dust * 0.9));
            double blink = Hash(i, seed) > 0.86 ? Math.Pow(Math.Sin(s * (1 + Hash(i + 7, seed) * 2) + Hash(i, 3) * 6.28) * 0.5 + 0.5, 6) : 0;
            leds[i] = Add(nebula, Rgb.White.Scale(blink));
        }
    }
}

public sealed class PendulumEffect : MotionEffect
{
    public override string Id => "pendulum";
    public override string Name => "Pêndulos";
    public override string Blurb => "Doze pêndulos de ritmos quase iguais: saem de fase, formam ondas e voltam a se alinhar.";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Clear();
        if (n == 0) return;
        const int Count = 12;
        double s = t * p.Speed;
        for (int b = 0; b < Count; b++)
        {
            // Frequencies in a ladder, so all twelve line up again every 40 s at speed 1.
            double freq = (16 + b) / 40.0;
            double pos = (Math.Sin(s * freq * 2 * Math.PI) * 0.5 + 0.5) * (n - 1);
            var c = Rgb.FromHsv(b * 360.0 / Count, 0.92, 1);
            for (int i = 0; i < n; i++) leds[i] = Add(leds[i], c.Scale(0.8 * Glow(i, pos, Width(n, 0.012))));
        }
    }
}

public sealed class FirefliesEffect : MotionEffect
{
    public override string Id => "fireflies";
    public override string Name => "Vaga-lumes";
    public override string Blurb => "Pontos quentes que acendem sem pressa enquanto vagam no escuro.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#D8FF3A";
    public override string DefaultColor2 => "#08160A";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double s = t * p.Speed;
        leds.Fill(p.Color2);
        if (n == 0) return;
        int flies = Math.Max(3, n / 10);
        for (int f = 0; f < flies; f++)
        {
            double path = Fbm(s * 0.05 + f * 3.1, f * 1.7, seed);
            double pos = Math.Clamp((path - 0.25) * 2, 0, 1) * (n - 1);
            double phase = s * (0.18 + Hash(f, seed) * 0.2) + Hash(seed, f);
            double blink = Math.Pow(Math.Max(0, Math.Sin(phase * 2 * Math.PI)), 3);
            for (int i = 0; i < n; i++) leds[i] = Add(leds[i], p.Color1.Scale(blink * Glow(i, pos, Width(n, 0.018))));
        }
    }
}
