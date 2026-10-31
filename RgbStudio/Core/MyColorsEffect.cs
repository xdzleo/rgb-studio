namespace RgbStudio.Core;

/// <summary>
/// The person's own colors, in the way of moving they pick: the easy way to make an effect.
/// Colors loop seamlessly and blend in Oklab, except where the motion wants hard edges.
/// </summary>
public sealed class MyColorsEffect : Effect
{
    public const string EffectId = "my-colors";

    /// <summary>The ways of moving, with the names the screen shows. The first is the default.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> Motions =
    [
        ("run", "Correr"), ("bands", "Faixas"), ("comets", "Cometas"), ("blocks", "Blocos"),
        ("bounce", "Vai e vem"), ("mirror", "Espelho"), ("breathe", "Respirar"), ("blink", "Piscar"), ("still", "Parado"),
    ];

    public override string Id => EffectId;
    public override string Name => "Minhas cores";
    public override string Glyph => "≋";
    public override string Blurb => "Suas cores, do jeito de andar que você escolher.";
    public override bool UsesColor1 => false;
    public override bool UsesColors => true;
    public override bool UsesDirection => true;
    public override bool UsesMotion => true;

    public override void Render(Span<Rgb> leds, double t, EffectParams p, int seed)
    {
        IReadOnlyList<Rgb> colors = p.Colors.Count > 0 ? p.Colors : [p.Color1, p.Color2];
        int n = colors.Count, len = leds.Length;
        double s = t * p.Speed;

        // Along a loop of all the colors: u in 0..1 is one full round.
        Rgb At(double u, double edge)
        {
            double pos = (u % 1 + 1) % 1 * n;
            int k = (int)pos;
            double f = pos - k;
            double mix = f < edge ? 0 : (f - edge) / (1 - edge);
            return Rgb.MixPerceptual(colors[k % n], colors[(k + 1) % n], mix);
        }

        switch (p.Motion)
        {
            case "breathe":
            {
                // The whole zone takes one color at a time, fading out and back in to the next.
                double x = s * 0.25, phase = x - Math.Floor(x);
                var c = colors[(int)Math.Floor(x) % n].Scale(0.08 + 0.92 * Math.Sin(Math.PI * phase));
                leds.Fill(c);
                return;
            }
            case "blink":
            {
                double x = s * 1.5;
                var c = x - Math.Floor(x) < 0.55 ? colors[(int)Math.Floor(x) % n] : Rgb.Black;
                leds.Fill(c);
                return;
            }
            case "comets":
            {
                leds.Clear();
                if (len == 0) return;
                double tail = Math.Max(2.5, len * 0.25);
                for (int m = 0; m < n; m++)
                {
                    double head = ((s * 0.35 + (double)m / n) % 1) * (len + tail);
                    for (int i = 0; i < len; i++)
                    {
                        double x = p.Reverse ? len - 1 - i : i;
                        double d = head - x;
                        if (d < 0 || d > tail) continue;
                        var c = colors[m].Scale(Math.Pow(1 - d / tail, 2));
                        var o = leds[i];
                        leds[i] = new Rgb((byte)Math.Max(o.R, c.R), (byte)Math.Max(o.G, c.G), (byte)Math.Max(o.B, c.B));
                    }
                }
                return;
            }
        }

        double back = (s * 0.12 % 2 + 2) % 2;
        if (back > 1) back = 2 - back;
        for (int i = 0; i < len; i++)
        {
            double x = Pos(i, len, p.Reverse);
            leds[i] = p.Motion switch
            {
                "bands" => At(x - s * 0.15, 0.85),
                "blocks" => At(Math.Floor(x * n * 2) / (n * 2) - Math.Floor(s * 2) / (n * 2), 1.0),
                "bounce" => At(x * 0.8 + back, 0),
                "mirror" => At(Math.Abs(x * 2 - 1) - s * 0.15, 0),
                "still" => At(x, 0),
                _ => At(x - s * 0.15, 0),   // run
            };
        }
    }
}
