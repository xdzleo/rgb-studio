namespace RgbStudio.Core;

/// <summary>Effects that run the real thing: the algorithm or the equation, not a look-alike.</summary>
public abstract class ScienceEffect : MotionEffect
{
    public override string Category => "Ciência";

    /// <summary>Approximate color of something hot, from 0 (dying red) to 1 (blue-white).</summary>
    protected static Rgb Blackbody(double heat)
    {
        heat = Math.Clamp(heat, 0, 1);
        return heat < 0.33 ? Rgb.Lerp(new(30, 0, 0), new(255, 50, 0), heat / 0.33)
            : heat < 0.66 ? Rgb.Lerp(new(255, 50, 0), new(255, 200, 80), (heat - 0.33) / 0.33)
            : Rgb.Lerp(new(255, 200, 80), new(190, 220, 255), (heat - 0.66) / 0.34);
    }
}

/// <summary>Wolfram's elementary cellular automata, run generation by generation from a random row.</summary>
public sealed class AutomatonEffect : ScienceEffect
{
    public override string Id => "automaton";
    public override string Name => "Autômato celular";
    public override string Blurb => "Regras 30, 90, 110 e 150 de verdade: cada LED vive ou morre pelos vizinhos, e surge caos de uma regra simples.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#00F0FF";
    public override string DefaultColor2 => "#100030";
    static readonly int[] Rules = [30, 110, 90, 150];
    const int Generations = 200;

    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        if (n == 0) return;
        int cells = Math.Max(n, 32);
        double clock = t * p.Speed * 9;
        long whole = (long)Math.Floor(clock);
        int gen = (int)(whole % Generations);
        int era = (int)(whole / Generations % 1000);
        int rule = Rules[era % Rules.Length];

        var row = new bool[cells];
        var before = new bool[cells];
        var next = new bool[cells];
        for (int c = 0; c < cells; c++) row[c] = Hash(c + era * 97, seed) > 0.72;
        for (int g = 0; g < gen; g++)
        {
            Array.Copy(row, before, cells);
            for (int c = 0; c < cells; c++)
            {
                int pattern = (row[(c + cells - 1) % cells] ? 4 : 0) | (row[c] ? 2 : 0) | (row[(c + 1) % cells] ? 1 : 0);
                next[c] = (rule >> pattern & 1) == 1;
            }
            (row, next) = (next, row);
        }
        double blend = clock - whole;     // cells fade between generations instead of snapping
        for (int i = 0; i < n; i++)
        {
            int c = i * cells / n;
            double now = (row[c] ? 1 : 0) * blend + (before[c] ? 1 : 0) * (1 - blend);
            leds[i] = Rgb.Lerp(p.Color2, p.Color1, now);
        }
    }
}

/// <summary>Bubble sort on a shuffled rainbow, shown comparison by comparison.</summary>
public sealed class SortingEffect : ScienceEffect
{
    public override string Id => "sorting";
    public override string Name => "Ordenação";
    public override string Blurb => "Um arco-íris embaralhado é organizado pelo algoritmo Bubble Sort, comparação por comparação, até ficar perfeito.";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        if (n == 0) return;
        int items = Math.Clamp(n, 8, 160);
        const double SortTime = 11, HoldTime = 3;
        double clock = t * p.Speed;
        int round = (int)(Math.Floor(clock / (SortTime + HoldTime)) % 100000);
        double within = clock - Math.Floor(clock / (SortTime + HoldTime)) * (SortTime + HoldTime);

        var a = new int[items];
        for (int k = 0; k < items; k++) a[k] = k;
        for (int k = items - 1; k > 0; k--)          // Fisher-Yates
        {
            int j = (int)(Hash(k + round * 31, seed) * (k + 1));
            (a[k], a[j]) = (a[j], a[k]);
        }

        long total = items * (long)(items - 1) / 2;
        long budget = (long)(Math.Min(1, within / SortTime) * total);
        int hi = -1;
        for (int pass = 0; pass < items - 1 && budget > 0; pass++)
            for (int j = 0; j < items - 1 - pass && budget > 0; j++, budget--)
            {
                if (a[j] > a[j + 1]) (a[j], a[j + 1]) = (a[j + 1], a[j]);
                hi = j + 1;
            }

        double hold = within > SortTime ? (within - SortTime) / HoldTime : 0;
        for (int i = 0; i < n; i++)
        {
            int k = i * items / n;
            var c = Rgb.FromHsv(a[k] * 330.0 / items, 1, 1);
            // The comparison in progress shows in white; a finished sort gives a slow shine.
            if (k == hi && within < SortTime) c = Rgb.White;
            if (hold > 0) c = Rgb.Lerp(c, Rgb.White, 0.5 * Glow(k, hold * items, items * 0.05));
            leds[i] = c;
        }
    }
}

/// <summary>A line through the Mandelbrot set, zooming toward a point on its edge.</summary>
public sealed class FractalEffect : ScienceEffect
{
    public override string Id => "fractal";
    public override string Name => "Fractal";
    public override string Blurb => "Mergulho real no conjunto de Mandelbrot, com a fórmula calculada para cada LED a cada quadro. A borda nunca acaba.";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        const double Cx = -0.743643887, Cy = 0.131825904;
        const int MaxIter = 110;
        double phase = t * p.Speed / 22 % 2;
        double f = phase < 1 ? phase : 2 - phase;      // in, then back out
        double span = 2.8 * Math.Exp(-f * 9.5);
        for (int i = 0; i < n; i++)
        {
            double x0 = Cx + ((n <= 1 ? 0.5 : (double)i / (n - 1)) - 0.5) * span;
            double x = 0, y = 0;
            int it = 0;
            double xx = 0, yy = 0;
            while (it < MaxIter && xx + yy < 256)
            {
                y = 2 * x * y + Cy;
                x = xx - yy + x0;
                xx = x * x;
                yy = y * y;
                it++;
            }
            if (it >= MaxIter) { leds[i] = Rgb.Black; continue; }
            double smooth = it + 1 - Math.Log(Math.Log(Math.Max(xx + yy, 1.0001)) / 2 / Math.Log(2)) / Math.Log(2);
            leds[i] = Rgb.FromHsv(smooth * 9 + t * p.Speed * 12, 0.95, 1);
        }
    }
}

public sealed class InterferenceEffect : ScienceEffect
{
    public override string Id => "interference";
    public override string Name => "Interferência";
    public override string Blurb => "Duas fontes emitem ondas: onde as cristas coincidem a luz soma, onde se opõem ela some.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#20E0FF";
    public override string DefaultColor2 => "#FF2BB0";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed;
        double a = 0.3 + 0.2 * Math.Sin(s * 0.23), b = 0.7 + 0.2 * Math.Sin(s * 0.31 + 2);
        for (int i = 0; i < leds.Length; i++)
        {
            double x = (double)i / Math.Max(1, leds.Length - 1);
            double wave = Math.Sin(Math.Abs(x - a) * 34 - s * 3.2) + Math.Sin(Math.Abs(x - b) * 34 - s * 3.2);
            double power = wave * wave / 4;        // intensity goes with amplitude squared
            leds[i] = Rgb.Lerp(p.Color2, p.Color1, x).Scale(power);
        }
    }
}

public sealed class StringEffect : ScienceEffect
{
    public override string Id => "string";
    public override string Name => "Corda vibrando";
    public override string Blurb => "A física de uma corda de violão: harmônicos somados, com as pontas sempre paradas.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#FFC400";
    public override string DefaultColor2 => "#00C8FF";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed;
        for (int i = 0; i < leds.Length; i++)
        {
            double x = (double)(i + 1) / (leds.Length + 1);
            double y = 0;
            for (int h = 1; h <= 4; h++)
            {
                double weight = Math.Pow(Math.Sin(s * 0.13 * h + h), 2) / h;
                y += weight * Math.Sin(h * Math.PI * x) * Math.Cos(h * s * 2.6);
            }
            leds[i] = (y >= 0 ? p.Color1 : p.Color2).Scale(Math.Clamp(Math.Abs(y) * 1.5, 0, 1));
        }
    }
}

/// <summary>Coupled oscillators in the spirit of Kuramoto's model: coupling grows, phases lock, then it lets go.</summary>
public sealed class SynchronyEffect : ScienceEffect
{
    public override string Id => "synchrony";
    public override string Name => "Sincronia";
    public override string Blurb => "Como vaga-lumes reais: cada LED pisca no seu tempo, vão se acertando até todos baterem juntos, e aí se soltam.";
    public override string DefaultColor1 => "#B6FF3A";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed;
        double cycle = s / 16 % 1;
        double sync = Math.Pow(Math.Sin(cycle * Math.PI), 3);   // 0 = each on its own, 1 = locked
        for (int i = 0; i < leds.Length; i++)
        {
            double own = Hash(i, seed) * 6.283;
            double drift = (Hash(i + 91, seed) - 0.5) * s * 0.9;
            double phase = s * 2.4 + (own + drift) * (1 - sync);
            double flash = Math.Pow(Math.Max(0, Math.Sin(phase)), 8);
            leds[i] = Add(p.Color1.Scale(flash), p.Color1.Scale(0.02));
        }
    }
}

public sealed class BlackHoleEffect : ScienceEffect
{
    public override string Id => "blackhole";
    public override string Name => "Buraco negro";
    public override string Blurb => "Matéria acelera em queda livre, esquenta ao se aproximar e some no centro, que nunca acende.";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Clear();
        if (n == 0) return;
        double mid = (n - 1) / 2.0, horizon = n < 8 ? 0 : Math.Max(1, n * 0.05), edge = mid + 1;
        for (int k = 0; k < 14; k++)
        {
            double f = (t * p.Speed * (0.13 + Hash(k, seed) * 0.12) + Hash(seed, k)) % 1;
            double r = horizon + (edge - horizon) * Math.Pow(1 - f, 2.0 / 3);  // free fall: r ~ (t0 - t)^(2/3)
            double side = k % 2 == 0 ? 1 : -1;
            var c = Blackbody(f * f * 1.05);
            for (int i = 0; i < n; i++) leds[i] = Add(leds[i], c.Scale(0.45 + f * 0.55).Scale(Glow(i, mid + side * r, Width(n, 0.016))));
        }
        for (int i = 0; i < n; i++)
        {
            double d = Math.Abs(i - mid);
            if (n >= 8 && d < horizon) { leds[i] = Rgb.Black; continue; }
            double disk = n < 8 ? 0.2 : Math.Exp(-(d - horizon) / Math.Max(1, n * 0.05)) * (0.6 + 0.4 * Noise(i * 0.4 - t * p.Speed * 2.2, t * p.Speed * 0.4, seed));
            leds[i] = Add(leds[i], Blackbody(0.5 + disk * 0.5).Scale(disk * 0.85));
        }
    }
}

public sealed class SupernovaEffect : ScienceEffect
{
    public override string Id => "supernova";
    public override string Name => "Supernova";
    public override string Blurb => "A estrela pulsa, colapsa de repente e explode numa onda que esfria do azul ao vermelho, deixando um pulsar.";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double f = t * p.Speed / 13 % 1;
        double mid = (n - 1) / 2.0;
        const double Collapse = 0.42, Bang = 0.48;
        for (int i = 0; i < n; i++)
        {
            double d = Math.Abs(i - mid);
            if (f < Collapse)
            {
                // Swelling as it runs out of fuel, growing redder and unsteady.
                double age = f / Collapse;
                double radius = Width(n, 0.07 + age * 0.18);
                double wobble = 0.8 + 0.2 * Math.Sin(t * p.Speed * (3 + age * 9));
                leds[i] = Blackbody(0.75 - age * 0.4).Scale(Math.Exp(-Math.Pow(d / radius, 2)) * wobble);
            }
            else if (f < Bang)
            {
                double fall = (f - Collapse) / (Bang - Collapse);
                double radius = Width(n, 0.25 * (1 - fall) * (1 - fall) + 0.008);
                leds[i] = Blackbody(0.4 + fall * 0.6).Scale(Math.Exp(-Math.Pow(d / radius, 2)));
            }
            else
            {
                double blast = (f - Bang) / (1 - Bang);
                double front = (mid + 2) * Math.Pow(blast, 0.45);
                double shell = Math.Exp(-Math.Pow((d - front) / Width(n, 0.05 + blast * 0.12), 2));
                double inside = d < front ? 0.35 * Fbm(i * 0.22, t * p.Speed * 0.25, seed) * (1 - blast) : 0;
                var c = Blackbody(1 - blast * 0.95).Scale(Math.Clamp(shell * (1.1 - blast * 0.7) + inside, 0, 1));
                double spin = Math.Pow(Math.Max(0, Math.Sin(t * p.Speed * 18)), 10) * Math.Exp(-Math.Pow(d / Width(n, 0.02), 2));
                leds[i] = Add(c, new Rgb(170, 200, 255).Scale(spin * blast));
            }
        }
    }
}

public sealed class PulsarEffect : ScienceEffect
{
    public override string Id => "pulsar";
    public override string Name => "Pulsar";
    public override string Blurb => "Estrela de nêutrons girando: um brilho constante no centro e jatos que varrem em pulsos exatos.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#A8C8FF";
    public override string DefaultColor2 => "#5A18FF";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double s = t * p.Speed;
        double mid = (n - 1) / 2.0;
        double spin = Math.Pow(Math.Max(0, Math.Cos(s * 2 * Math.PI * 0.9)), 12);
        for (int i = 0; i < n; i++)
        {
            double d = mid <= 0 ? 0 : Math.Abs(i - mid) / mid;
            double delayed = Math.Pow(Math.Max(0, Math.Cos((s - d * 0.16) * 2 * Math.PI * 0.9)), 12);
            double jet = delayed * Math.Exp(-d * 1.8);
            double core = Math.Exp(-Math.Pow(d / 0.06, 2));
            leds[i] = Add(p.Color2.Scale(Math.Clamp(jet * 1.2, 0, 1)), p.Color1.Scale(Math.Clamp(core * (0.35 + spin * 0.65) + jet * 0.5, 0, 1)));
        }
    }
}

public sealed class DnaEffect : ScienceEffect
{
    public override string Id => "dna";
    public override string Name => "Hélice de DNA";
    public override string Blurb => "Duas fitas girando em torno uma da outra, com pontes de bases onde elas se cruzam.";
    public override bool UsesColor2 => true;
    public override bool UsesDirection => true;
    public override string DefaultColor1 => "#00FFA8";
    public override string DefaultColor2 => "#FF2E9A";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed;
        for (int i = 0; i < leds.Length; i++)
        {
            double x = Pos(i, leds.Length, p.Reverse);
            double turn = x * 3.2 * 2 * Math.PI - s * 2.2;
            double depth = Math.Sin(turn);     // +1 strand A in front, -1 strand B in front
            var front = depth >= 0 ? p.Color1 : p.Color2;
            double near = Math.Pow(Math.Abs(depth), 1.6);
            int pair = (int)Math.Floor(turn / Math.PI);
            double bridge = Math.Exp(-Math.Pow(depth / 0.16, 2));
            var rung = Hash(pair, seed) > 0.5 ? new Rgb(255, 225, 90) : new Rgb(90, 180, 255);
            leds[i] = Add(front.Scale(near), rung.Scale(bridge * 0.85));
        }
    }
}

public sealed class BinaryEffect : ScienceEffect
{
    public override string Id => "binary";
    public override string Name => "Contador binário";
    public override string Blurb => "A fita conta em binário de verdade: cada bloco é um bit e a ponta menos significativa pisca mais rápido.";
    public override bool UsesColor2 => true;
    public override bool UsesDirection => true;
    public override string DefaultColor1 => "#30FF80";
    public override string DefaultColor2 => "#04150A";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        if (n == 0) return;
        int bits = Math.Clamp(n, 1, 16);
        double clock = t * p.Speed * 4;
        long value = (long)Math.Floor(clock);
        for (int i = 0; i < n; i++)
        {
            int at = p.Reverse ? n - 1 - i : i;
            int bit = i * bits / n;
            leds[at] = (value >> bit & 1) == 1 ? p.Color1 : p.Color2;
        }
    }
}

public sealed class TeslaEffect : ScienceEffect
{
    public override string Id => "tesla";
    public override string Name => "Bobina de Tesla";
    public override string Blurb => "Arcos elétricos que saltam de um ponto e tremem ao redor, nunca duas vezes iguais.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#E6E0FF";
    public override string DefaultColor2 => "#6A22FF";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Clear();
        if (n == 0) return;
        double s = t * p.Speed;
        for (int arc = 0; arc < 3; arc++)
        {
            int strike = (int)Math.Floor(s * (1.5 + arc * 0.6) + arc * 0.37);
            int flick = (int)Math.Floor(s * 30);
            double on = Hash(flick, arc + strike) > 0.3 ? 1 : 0.15;
            double from = Hash(strike, arc + seed) * (n - 1);
            double length = Math.Max(1, n * (0.1 + Hash(strike + 3, arc) * 0.25));
            for (int i = 0; i < n; i++)
            {
                double d = Math.Abs(i - from) / length;
                if (d > 1) continue;
                double jag = Noise(i * 0.8, flick * 0.6, strike + arc);
                double core = jag > 0.42 ? (1 - d) * on : (1 - d) * 0.12 * on;
                leds[i] = Add(leds[i], Add(p.Color2.Scale((1 - d) * 0.55 * on), p.Color1.Scale(Math.Clamp(core, 0, 1))));
            }
        }
    }
}

public sealed class KaleidoscopeEffect : MotionEffect
{
    public override string Id => "kaleidoscope";
    public override string Name => "Caleidoscópio";
    public override string Blurb => "Padrões orgânicos espelhados em dobras, sempre simétricos e sempre mudando.";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed;
        for (int i = 0; i < leds.Length; i++)
        {
            double x = (double)i / Math.Max(1, leds.Length - 1) * 3;      // three mirrored folds
            double fold = Math.Abs((x % 2 + 2) % 2 - 1);
            double v = Fbm(fold * 2.2 + s * 0.12, s * 0.17, seed);
            leds[i] = Rgb.FromHsv(v * 520 + s * 16, 0.95, Math.Clamp(Math.Pow(v * 1.55, 2), 0.02, 1));
        }
    }
}

public sealed class BioluminescenceEffect : MotionEffect
{
    public override string Id => "biolum";
    public override string Name => "Bioluminescência";
    public override string Blurb => "Mar escuro à noite: cada onda que passa acorda um rastro azul que se apaga aos poucos.";
    public override bool UsesColor2 => true;
    public override bool UsesDirection => true;
    public override string DefaultColor1 => "#20E4FF";
    public override string DefaultColor2 => "#010A18";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double s = t * p.Speed / 4.5;
        double front = s % 1;
        for (int i = 0; i < n; i++)
        {
            double x = Pos(i, n, p.Reverse);
            double since = ((front - x) % 1 + 1) % 1;       // how long ago the wave passed here
            double plankton = 0.25 + 0.75 * Math.Pow(Hash(i, seed), 2.5);
            double glow = Math.Exp(-since * 6) * plankton;
            double twinkle = Noise(i * 0.7, t * p.Speed * 2.2, seed);
            leds[i] = Add(p.Color2, p.Color1.Scale(Math.Clamp(glow * (0.6 + twinkle * 0.8), 0, 1)));
        }
    }
}

public sealed class EclipseEffect : MotionEffect
{
    public override string Id => "eclipse";
    public override string Name => "Eclipse";
    public override string Blurb => "A lua cobre o sol, a coroa aparece em volta e o anel de diamante brilha na saída.";
    public override string DefaultColor1 => "#FFC86A";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double f = t * p.Speed / 14 % 1;
        double mid = (n - 1) / 2.0;
        double sunR = Width(n, 0.26), moonR = sunR * 1.02;
        double moon = mid + (f * 2 - 1) * (sunR + moonR + n * 0.05);
        double totality = Math.Exp(-Math.Pow((moon - mid) / (sunR * 0.16), 2));
        for (int i = 0; i < n; i++)
        {
            double fromSun = Math.Abs(i - mid), fromMoon = Math.Abs(i - moon);
            double sun = fromSun < sunR ? 1 : 0;
            double covered = fromMoon < moonR ? 0 : 1;
            double corona = fromSun >= sunR * 0.8 ? Math.Exp(-(fromSun - sunR * 0.8) / Width(n, 0.09)) * (0.35 + 0.65 * totality) : 0;
            double streak = 0.75 + 0.25 * Noise(i * 0.5, t * p.Speed * 0.3, seed);
            var c = p.Color1.Scale(Math.Clamp(sun * covered, 0, 1));
            c = Add(c, new Rgb(190, 215, 255).Scale(Math.Clamp(corona * streak * covered, 0, 1)));
            // The diamond ring: one bead of sun just before it clears the moon.
            double bead = Math.Exp(-Math.Pow((i - (moon - moonR * Math.Sign(moon - mid) * -1)) / Width(n, 0.02), 2));
            double ringMoment = Math.Exp(-Math.Pow((Math.Abs(moon - mid) - sunR * 0.1) / (sunR * 0.08), 2));
            leds[i] = Add(c, Rgb.White.Scale(bead * ringMoment));
        }
    }
}

public sealed class SpectrumEffect : MotionEffect
{
    public override string Id => "spectrum";
    public override string Name => "Espectro";
    public override string Blurb => "Bandas de cor pulsando como um equalizador, em ritmo de música imaginária. Não ouve o som do PC.";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double s = t * p.Speed;
        int bands = Math.Clamp(n, 1, 10);
        double beat = Math.Pow(Math.Max(0, Math.Cos(s * Math.PI * 2 * 2.0 / 2)), 6);   // one thump per second at speed 1
        for (int i = 0; i < n; i++)
        {
            int b = i * bands / n;
            double level = Noise(b * 3.7, s * (1.2 + b * 0.3), seed);
            double bass = b < bands / 3 + 1 ? beat * 0.7 : beat * 0.15;
            leds[i] = Rgb.FromHsv(b * 300.0 / bands, 1, 1).Scale(Math.Clamp(Math.Pow(level, 1.6) + bass, 0, 1));
        }
    }
}
