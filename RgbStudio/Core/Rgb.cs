namespace RgbStudio.Core;

/// <summary>An RGB color, 0-255 per channel.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);

    public static Rgb FromHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6) return Black;
        return new Rgb(
            Convert.ToByte(s[..2], 16),
            Convert.ToByte(s.Substring(2, 2), 16),
            Convert.ToByte(s.Substring(4, 2), 16));
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Hue 0-360, saturation and value 0-1.</summary>
    public static Rgb FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (h / 60) switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new Rgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    public Rgb Scale(double f) => new(ToByte(R / 255.0 * f), ToByte(G / 255.0 * f), ToByte(B / 255.0 * f));

    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>
    /// Blends in Oklab (Björn Ottosson), where halfway looks halfway: no purple between blue
    /// and white, no dark band between two bright colors. Costs more than <see cref="Lerp"/>.
    /// </summary>
    public static Rgb MixPerceptual(Rgb a, Rgb b, double t)
    {
        if (t <= 0) return a;
        if (t >= 1) return b;
        var (l1, a1, b1) = ToOklab(a);
        var (l2, a2, b2) = ToOklab(b);
        return FromOklab(l1 + (l2 - l1) * t, a1 + (a2 - a1) * t, b1 + (b2 - b1) * t);
    }

    static double ToLinear(byte v)
    {
        double x = v / 255.0;
        return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
    }

    static byte FromLinear(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return ToByte(x <= 0.0031308 ? x * 12.92 : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055);
    }

    static (double L, double A, double B) ToOklab(Rgb c)
    {
        double r = ToLinear(c.R), g = ToLinear(c.G), b = ToLinear(c.B);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    static Rgb FromOklab(double lightness, double a, double b)
    {
        double l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
        double m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
        double s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);
        return new Rgb(
            FromLinear(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
            FromLinear(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
            FromLinear(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }

    /// <summary>The Aura SDK's light color: 0x00BBGGRR.</summary>
    public uint ToAura() => (uint)(R | (G << 8) | (B << 16));

    static byte ToByte(double unit) => (byte)Math.Round(Math.Clamp(unit, 0, 1) * 255);
}
