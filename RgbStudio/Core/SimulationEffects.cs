namespace RgbStudio.Core;

public sealed class FireworksEffect : MotionEffect
{
    public override string Id => "fireworks";
    public override string Name => "Fogos de artifício";
    public override string Blurb => "Foguetes sobem, explodem e as faíscas caem com gravidade, apagando no ar.";
    public override bool UsesColor1 => false;
    public override bool UsesDirection => true;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Clear();
        if (n == 0) return;
        const double Rise = 0.8, Burst = 2.2;
        for (int r = 0; r < 3; r++)
        {
            double clock = t * p.Speed / (Rise + Burst + 0.5 + Hash(r, seed)) + Hash(seed, r);
            int shot = (int)(Math.Floor(clock) % 100000);
            double time = (clock - Math.Floor(clock)) * (Rise + Burst + 0.5 + Hash(r, seed));
            double apex = (0.45 + Hash(shot, r) * 0.5) * (n - 1);
            if (time < Rise)
            {
                double up = 1 - Math.Pow(1 - time / Rise, 2);   // slows as it climbs
                Paint(leds, up * apex, new Rgb(255, 210, 150), Width(n, 0.012), p.Reverse);
                continue;
            }
            double b = time - Rise;
            if (b > Burst) continue;
            var color = Rgb.FromHsv(Hash(shot + 3, r) * 360, 0.85, 1);
            double fade = Math.Pow(1 - b / Burst, 1.7);
            for (int s = 0; s < 14; s++)
            {
                double speed = (Hash(s + shot, r + 9) * 2 - 1) * n * 0.55;
                double drag = 1 - Math.Exp(-b * 2.4);                // air slows the sparks
                double x = apex + speed * drag / 2.4 * 1.6 - n * 0.09 * b * b;
                double blink = Hash(s, (int)(t * 22) + shot) > 0.2 ? 1 : 0.3;
                Paint(leds, x, Rgb.Lerp(color, Rgb.White, s % 5 == 0 ? 0.5 : 0).Scale(fade * blink), Width(n, 0.008), p.Reverse);
            }
        }
    }

    static void Paint(Span<Rgb> leds, double center, Rgb c, double width, bool reverse)
    {
        int n = leds.Length;
        int from = Math.Max(0, (int)(center - width * 3)), to = Math.Min(n - 1, (int)(center + width * 3) + 1);
        for (int i = from; i <= to; i++)
        {
            int at = reverse ? n - 1 - i : i;
            leds[at] = Add(leds[at], c.Scale(Glow(i, center, width)));
        }
    }
}

public sealed class HourglassEffect : MotionEffect
{
    public override string Id => "hourglass";
    public override string Name => "Ampulheta";
    public override string Blurb => "Grão a grão, a areia cai de uma metade para a outra. Cheia, ela vira e recomeça.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#FFC36A";
    public override string DefaultColor2 => "#0C0A1E";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Fill(p.Color2);
        if (n == 0) return;
        const double Run = 24, FlipPause = 1.2;
        double clock = t * p.Speed;
        int turn = (int)(Math.Floor(clock / (Run + FlipPause)) % 100000);
        double within = clock - Math.Floor(clock / (Run + FlipPause)) * (Run + FlipPause);
        int sand = Math.Max(1, n / 2);
        int fallen = (int)Math.Min(sand, within / Run * sand);
        double grainFall = within / Run * sand - fallen;        // the grain in flight, 0 to 1

        bool flipped = turn % 2 == 1;
        for (int k = 0; k < sand; k++)
        {
            // The top half empties from its neck; the pile grows from the far end.
            if (k >= fallen) Set(leds, Math.Clamp(n / 2 - 1 - (k - fallen), 0, n - 1), p.Color1, flipped);
        }
        for (int k = 0; k < fallen; k++) Set(leds, n - 1 - k, p.Color1, flipped);
        if (fallen < sand && within < Run)
        {
            int neck = n / 2;
            int land = n - 1 - fallen;
            int at = neck + (int)(grainFall * grainFall * Math.Max(0, land - neck));
            Set(leds, Math.Clamp(at, 0, n - 1), Rgb.White, flipped);
        }
    }

    static void Set(Span<Rgb> leds, int i, Rgb c, bool flipped) => leds[flipped ? leds.Length - 1 - i : i] = c;
}

public sealed class DripEffect : MotionEffect
{
    public override string Id => "drip";
    public override string Name => "Gotas";
    public override string Blurb => "A gota incha na ponta, solta, acelera na queda e espalha um brilho ao bater na poça.";
    public override bool UsesDirection => true;
    public override string DefaultColor1 => "#39B8FF";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Clear();
        if (n == 0) return;
        double f = t * p.Speed / 2.8 % 1;
        double last = n - 1;
        for (int i = 0; i < n; i++)
        {
            double y = i;
            double c = 0;
            if (f < 0.55) c = Glow(i, 0, Width(n, 0.012 + f * 0.06)) * (0.3 + f);                  // swelling
            else if (f < 0.8) c = Glow(i, Math.Pow((f - 0.55) / 0.25, 2) * last, Width(n, 0.012));   // falling
            double since = f - 0.8;
            if (since >= 0)
            {
                double splash = Math.Exp(-since * 11);
                c = Math.Max(c, Glow(i, last - since * n * 0.8, Width(n, 0.02)) * splash);   // the ripple back
                c = Math.Max(c, Glow(i, last, Width(n, 0.03)) * splash);
            }
            double pool = Math.Exp(-(last - y) / Width(n, 0.05)) * 0.35;
            leds[p.Reverse ? n - 1 - i : i] = p.Color1.Scale(Math.Clamp(Math.Max(c, pool), 0, 1));
        }
    }
}

public sealed class StackingEffect : MotionEffect
{
    public override string Id => "stacking";
    public override string Name => "Empilhar";
    public override string Blurb => "Blocos coloridos caem e se empilham até encher a fita; aí tudo pisca e recomeça.";
    public override bool UsesColor1 => false;
    public override bool UsesDirection => true;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Clear();
        if (n == 0) return;
        int block = Math.Max(1, n / 10);
        int total = Math.Max(1, n / block);
        const double Fall = 0.55, Clear = 1.2;
        double period = total * Fall + Clear;
        double clock = t * p.Speed;
        int round = (int)(Math.Floor(clock / period) % 100000);
        double within = clock - Math.Floor(clock / period) * period;
        int landed = Math.Min(total, (int)(within / Fall));
        bool clearing = within >= total * Fall;
        double blink = clearing && (int)((within - total * Fall) * 8) % 2 == 1 ? 0.15 : 1;

        Rgb Color(int b) => Rgb.FromHsv(Hash(b, round + seed) * 360, 0.9, 1);
        for (int b = 0; b < landed; b++)
            Fill(leds, b * block, block, Color(b).Scale(blink), p.Reverse);
        if (!clearing && landed < total)
        {
            double f = (within - landed * Fall) / Fall;
            int top = n - block, rest = landed * block;
            int at = top - (int)(f * f * Math.Max(0, top - rest));
            Fill(leds, at, block, Color(landed), p.Reverse);
        }
    }

    static void Fill(Span<Rgb> leds, int start, int length, Rgb c, bool reverse)
    {
        int n = leds.Length;
        for (int i = Math.Max(0, start); i < Math.Min(n, start + length); i++) leds[reverse ? n - 1 - i : i] = c;
    }
}

public sealed class CandleEffect : MotionEffect
{
    public override string Id => "candle";
    public override string Name => "Vela";
    public override string Blurb => "Chama pequena e quente, com tremor natural e sopros de vento de vez em quando.";
    public override string DefaultColor1 => "#FF9329";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double s = t * p.Speed;
        double gust = Math.Pow(Noise(s * 0.3, 3, seed), 3) * 1.6;
        for (int i = 0; i < leds.Length; i++)
        {
            double local = Noise(i * 0.35, s * 4.5, seed + 1);
            double slow = Noise(i * 0.12, s * 0.9, seed + 2);
            double level = Math.Clamp(0.55 + (slow - 0.5) * 0.7 + (local - 0.5) * (0.35 + gust * 0.6) - gust * 0.25, 0.08, 1);
            leds[i] = Rgb.Lerp(new Rgb(150, 30, 0), p.Color1, level).Scale(level);
        }
    }
}

public sealed class SunriseEffect : MotionEffect
{
    public override string Id => "sunrise";
    public override string Name => "Nascer do sol";
    public override string Blurb => "Um dia inteiro em um minuto: noite, aurora, céu claro, pôr do sol e estrelas de novo.";
    public override bool UsesColor1 => false;
    public override bool UsesDirection => true;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        double day = t * p.Speed / 60 % 1;
        double sun = Math.Sin(day * 2 * Math.PI - Math.PI / 2);        // -1 midnight, +1 noon
        for (int i = 0; i < leds.Length; i++)
        {
            double h = Pos(i, leds.Length, p.Reverse);            // 0 horizon, 1 zenith
            double light = Math.Clamp((sun + 0.2) / 1.2, 0, 1);
            double golden = Math.Exp(-Math.Pow(sun / 0.28, 2));
            var night = new Rgb(4, 6, 22);
            var day1 = Rgb.Lerp(new Rgb(150, 205, 255), new Rgb(30, 90, 220), h);
            var dawn = Rgb.Lerp(new Rgb(255, 110, 20), new Rgb(120, 40, 120), h);
            var c = Rgb.Lerp(night, day1, light);
            c = Rgb.Lerp(c, dawn, golden * (1 - h * 0.45));
            double star = sun < -0.1 && Hash(i, seed) > 0.9 ? Math.Pow(Math.Sin(t * p.Speed * (1 + Hash(i + 3, seed) * 2)) * 0.5 + 0.5, 4) * Math.Min(1, -sun * 1.5) : 0;
            leds[i] = Add(c, Rgb.White.Scale(star * 0.7));
        }
    }
}

/// <summary>International Morse timing: dot 1, dash 3, gap 1, letter gap 3, word gap 7.</summary>
public sealed class MorseEffect : SignalEffect
{
    public override string Id => "morse";
    public override string Name => "Código Morse";
    public override string Blurb => "A fita transmite \"RGB STUDIO\" em Morse de verdade, no tempo certo. Dá para decodificar olhando.";
    public override string DefaultColor1 => "#FFD400";

    static readonly Dictionary<char, string> Table = new()
    {
        ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..", ['E'] = ".", ['F'] = "..-.", ['G'] = "--.", ['H'] = "....",
        ['I'] = "..", ['J'] = ".---", ['K'] = "-.-", ['L'] = ".-..", ['M'] = "--", ['N'] = "-.", ['O'] = "---", ['P'] = ".--.",
        ['Q'] = "--.-", ['R'] = ".-.", ['S'] = "...", ['T'] = "-", ['U'] = "..-", ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-",
        ['Y'] = "-.--", ['Z'] = "--..",
    };

    /// <summary>The message as on/off units, ending in a word gap so it can loop.</summary>
    public static bool[] Encode(string message)
    {
        var units = new List<bool>();
        foreach (var word in message.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            for (int l = 0; l < word.Length; l++)
            {
                var code = Table[word[l]];
                for (int m = 0; m < code.Length; m++)
                {
                    units.AddRange(Enumerable.Repeat(true, code[m] == '-' ? 3 : 1));
                    if (m < code.Length - 1) units.Add(false);
                }
                if (l < word.Length - 1) units.AddRange(Enumerable.Repeat(false, 3));
            }
            units.AddRange(Enumerable.Repeat(false, 7));
        }
        return units.ToArray();
    }

    static readonly bool[] Message = Encode("RGB STUDIO");

    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        long unit = (long)Math.Floor(t * p.Speed / 0.18);
        bool on = Message[unit % Message.Length];
        leds.Fill(on ? p.Color1 : p.Color1.Scale(0.03));
    }
}
