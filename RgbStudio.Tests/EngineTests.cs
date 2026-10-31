using RgbStudio.Core;
using Xunit;

namespace RgbStudio.Tests;

public class ColorTests
{
    [Fact]
    public void Aura_colors_are_bgr()
    {
        Assert.Equal(0x000000FFu, new Rgb(255, 0, 0).ToAura());
        Assert.Equal(0x0000FF00u, new Rgb(0, 255, 0).ToAura());
        Assert.Equal(0x00FF0000u, new Rgb(0, 0, 255).ToAura());
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(120, 0, 255, 0)]
    [InlineData(240, 0, 0, 255)]
    [InlineData(360, 255, 0, 0)]
    public void Hsv_primaries(double h, byte r, byte g, byte b) => Assert.Equal(new Rgb(r, g, b), Rgb.FromHsv(h, 1, 1));

    [Fact]
    public void Hex_round_trips() => Assert.Equal("#1A2B3C", Rgb.FromHex("#1a2b3c").ToHex());
}

public class EffectTests
{
    static readonly EffectParams P = new() { Color1 = new(255, 0, 0), Color2 = new(0, 0, 255) };

    [Fact]
    public void Every_effect_fills_any_length_without_throwing()
    {
        foreach (var effect in EffectCatalog.All)
            foreach (int n in new[] { 0, 1, 3, 8, 120 })
                foreach (double t in new[] { 0, 0.37, 12.5, 1000 })
                {
                    var leds = new Rgb[n];
                    EffectCatalog.Render(effect, leds, t, P, seed: 3);
                }
    }

    [Fact]
    public void Static_and_off()
    {
        var leds = new Rgb[5];
        EffectCatalog.Render(new StaticEffect(), leds, 1, P, 0);
        Assert.All(leds, c => Assert.Equal(P.Color1, c));
        EffectCatalog.Render(new OffEffect(), leds, 1, P, 0);
        Assert.All(leds, c => Assert.Equal(Rgb.Black, c));
    }

    [Fact]
    public void Brightness_scales_every_led()
    {
        var leds = new Rgb[3];
        EffectCatalog.Render(new StaticEffect(), leds, 0, new EffectParams { Color1 = new(200, 100, 50), Brightness = 50 }, 0);
        Assert.All(leds, c => Assert.Equal(new Rgb(100, 50, 25), c));
    }

    [Fact]
    public void Breathing_is_dark_at_start_and_full_at_half_period()
    {
        var leds = new Rgb[1];
        var effect = new BreathingEffect();
        effect.Render(leds, 0, P, 0);
        Assert.Equal(Rgb.Black, leds[0]);
        effect.Render(leds, 2, P, 0);           // speed 1: a breath lasts 4 s
        Assert.Equal(P.Color1, leds[0]);
    }

    [Fact]
    public void Gradient_goes_from_color1_to_color2_and_reverses()
    {
        var leds = new Rgb[10];
        new GradientEffect().Render(leds, 0, P, 0);
        Assert.Equal(P.Color1, leds[0]);
        Assert.True(leds[9].B > leds[9].R, "the far end should lean to the second color");
        new GradientEffect().Render(leds, 0, new EffectParams { Color1 = P.Color1, Color2 = P.Color2, Reverse = true }, 0);
        Assert.Equal(P.Color2, leds[0]);
    }

    [Fact]
    public void Rainbow_moves_over_time()
    {
        var a = new Rgb[8]; var b = new Rgb[8];
        new RainbowEffect().Render(a, 0, P, 0);
        new RainbowEffect().Render(b, 1, P, 0);
        Assert.NotEqual(a, b);
    }
}

