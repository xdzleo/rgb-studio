namespace RgbStudio.Core;

/// <summary>Lights that mean something on the street: emergency vehicles, roads, signs.</summary>
public abstract class SignalEffect : MotionEffect
{
    public override string Category => "Sinais";
}

public sealed class PoliceEffect : SignalEffect
{
    public override string Id => "police";
    public override string Name => "Sirene policial";
    public override string Blurb => "Metade vermelha e metade azul, em flashes duplos alternados. Pisca forte: evite se tem sensibilidade a luz.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#FF0010";
    public override string DefaultColor2 => "#0030FF";

    static double DoubleFlash(double f) => f is < 0.09 or (>= 0.14 and < 0.23) ? 1 : 0;

    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double f = t * p.Speed / 1.1 % 1;
        double left = f < 0.5 ? DoubleFlash(f) : 0, right = f >= 0.5 ? DoubleFlash(f - 0.5) : 0;
        for (int i = 0; i < n; i++)
        {
            if (n == 1) { leds[i] = Add(p.Color1.Scale(left), p.Color2.Scale(right)); continue; }
            // Every LED of a side at the same strength: with gamma, any falloff toward the middle
            // made the zones next to the center (the memory sticks, chained) look half off.
            bool isLeft = p.Reverse ? i * 2 >= n : i * 2 < n;
            leds[i] = isLeft ? p.Color1.Scale(left) : p.Color2.Scale(right);
        }
    }

    public override bool UsesDirection => true;
}

public sealed class BeaconEffect : SignalEffect
{
    public override string Id => "beacon";
    public override string Name => "Giroflex";
    public override string Blurb => "Dois fachos girando em lados opostos, como o espelho de um giroflex antigo.";
    public override bool UsesColor2 => true;
    public override bool UsesDirection => true;
    public override string DefaultColor1 => "#FF0A10";
    public override string DefaultColor2 => "#0A40FF";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        if (n == 0) return;
        double angle = t * p.Speed * 1.1 % 1;
        for (int i = 0; i < n; i++)
        {
            double x = Pos(i, n, p.Reverse);
            double a = Lobe(x - angle), b = Lobe(x - angle - 0.5);
            leds[i] = Add(p.Color1.Scale(a), p.Color2.Scale(b));
        }
    }

    /// <summary>How much of the beam reaches a point at this angle, wrapping around the circle.</summary>
    static double Lobe(double delta)
    {
        delta = (delta % 1 + 1) % 1;
        if (delta > 0.5) delta = 1 - delta;
        return Math.Exp(-Math.Pow(delta / 0.13, 2));
    }
}

public sealed class AmbulanceEffect : SignalEffect
{
    public override string Id => "ambulance";
    public override string Name => "Ambulância";
    public override string Blurb => "Blocos vermelhos e brancos trocando de lugar em ritmo de resgate.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#FF0018";
    public override string DefaultColor2 => "#FFFFFF";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double f = t * p.Speed / 0.9 % 1;
        bool swap = f >= 0.5;
        double g = swap ? f - 0.5 : f;
        double level = g is < 0.14 or (>= 0.2 and < 0.34) ? 1 : 0.06;
        for (int i = 0; i < n; i++)
        {
            bool odd = n <= 3 ? i % 2 == 1 : i * 4 / n % 2 == 1;
            leds[i] = (odd ^ swap ? p.Color2 : p.Color1).Scale(level);
        }
    }
}

public sealed class HazardEffect : SignalEffect
{
    public override string Id => "hazard";
    public override string Name => "Alerta âmbar";
    public override string Blurb => "Luz de obra: um pulso âmbar que nasce no centro e corre para as pontas.";
    public override string DefaultColor1 => "#FF9A00";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double f = t * p.Speed / 1.3 % 1;
        double mid = (n - 1) / 2.0;
        for (int i = 0; i < n; i++)
        {
            double d = mid <= 0 ? 0 : Math.Abs(i - mid) / mid;
            double g = f - d * 0.3;
            double level = g is >= 0 and < 0.35 ? Math.Sin(g / 0.35 * Math.PI) : 0;
            leds[i] = p.Color1.Scale(0.04 + 0.96 * level);
        }
    }
}

public sealed class TrafficLightEffect : SignalEffect
{
    public override string Id => "traffic";
    public override string Name => "Semáforo";
    public override string Blurb => "Verde, amarelo e vermelho no tempo de um cruzamento, com aviso antes de fechar.";
    public override bool UsesColor1 => false;
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        Rgb green = new(0, 230, 70), amber = new(255, 170, 0), red = new(255, 0, 16);
        int n = leds.Length;
        double f = t * p.Speed % 10;
        int stage = f < 4.2 ? 0 : f < 5.4 ? 1 : 2;
        double level = stage == 0 && f > 3.2 && (int)(f * 5) % 2 == 1 ? 0.15 : 1;   // green warns before it goes
        var active = stage == 0 ? green : stage == 1 ? amber : red;
        for (int i = 0; i < n; i++)
        {
            int slot = n < 3 ? stage : i * 3 / n;     // too few LEDs for three lamps: the whole strip is the lamp
            leds[i] = slot == stage ? active.Scale(level) : Rgb.Black;
        }
    }
}

public sealed class HighwayEffect : SignalEffect
{
    public override string Id => "highway";
    public override string Name => "Rodovia à noite";
    public override string Blurb => "Faróis brancos vindo de um lado e lanternas vermelhas indo para o outro.";
    public override bool UsesColor2 => true;
    public override string DefaultColor1 => "#FFF2D0";
    public override string DefaultColor2 => "#FF0A0A";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        leds.Clear();
        if (n == 0) return;
        double spot = Width(n, 0.012), gap = Math.Max(1.2, n * 0.022);
        for (int car = 0; car < 7; car++)
        {
            bool coming = car % 2 == 0;
            double f = (t * p.Speed * (0.1 + Hash(car, seed) * 0.11) + Hash(seed, car)) % 1;
            double x = f * (n + 8) - 4;
            if (!coming) x = n - 1 - x;
            var c = coming ? p.Color1 : p.Color2;
            for (int i = 0; i < n; i++)
            {
                double light = Glow(i, x, spot) + Glow(i, x + gap, spot);
                leds[i] = Add(leds[i], c.Scale(Math.Clamp(light, 0, 1) * 0.9));
            }
        }
    }
}

public sealed class BrokenNeonEffect : SignalEffect
{
    public override string Id => "neon";
    public override string Name => "Neon com defeito";
    public override string Blurb => "Letreiro de bar: quase sempre aceso, até um trecho engasgar e voltar piscando.";
    public override string DefaultColor1 => "#FF1E8C";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double s = t * p.Speed;
        int fit = (int)Math.Floor(s / 4.5);
        double into = s - fit * 4.5 - Hash(fit, seed) * 3;
        bool failing = into is >= 0 and < 0.9;
        int flick = (int)Math.Floor(s * 15);
        int bad = (int)(Hash(fit + 2, seed) * 4);
        double hum = 0.86 + 0.14 * Math.Sin(s * 50);
        for (int i = 0; i < n; i++)
        {
            int part = n < 4 ? i : i * 4 / n;
            double level = hum;
            if (failing && part == bad % Math.Max(1, Math.Min(4, n)))
                level *= Hash(flick, part) > 0.5 ? 1 : 0.04;
            // The tube's ends always burn a little dimmer.
            double e = n < 8 ? 1 : 0.8 + 0.2 * Math.Sin((double)(i * 4 % n) / n * Math.PI);
            leds[i] = p.Color1.Scale(level * e);
        }
    }
}

public sealed class LighthouseEffect : SignalEffect
{
    public override string Id => "lighthouse";
    public override string Name => "Farol marítimo";
    public override string Blurb => "Um único facho quente varrendo a escuridão com neblina em volta.";
    public override bool UsesDirection => true;
    public override string DefaultColor1 => "#FFE3A8";
    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        int n = leds.Length;
        double s = t * p.Speed;
        double angle = s / 5 % 1;
        for (int i = 0; i < n; i++)
        {
            double x = Pos(i, n, p.Reverse);
            double delta = ((x - angle) % 1 + 1) % 1;
            if (delta > 0.5) delta = 1 - delta;
            double beam = Math.Exp(-Math.Pow(delta / 0.075, 2));
            double fog = 0.03 + Noise(x * 6 + s * 0.2, s * 0.15, seed) * 0.07;
            leds[i] = p.Color1.Scale(Math.Clamp(beam + fog, 0, 1));
        }
    }
}
