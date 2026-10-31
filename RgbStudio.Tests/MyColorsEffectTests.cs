using RgbStudio.Core;
using Xunit;

namespace RgbStudio.Tests;

public class MyColorsEffectTests
{
    static Rgb[] Draw(string motion, double t, params string[] colors)
    {
        var leds = new Rgb[12];
        var p = new EffectParams { Colors = colors.Select(Rgb.FromHex).ToArray(), Motion = motion, Speed = 1 };
        new MyColorsEffect().Render(leds, t, p, 0);
        return leds;
    }

    [Fact]
    public void Still_lays_the_colors_along_the_strip_in_order()
    {
        var leds = Draw("still", 0, "#FF0000", "#00FF00", "#0000FF");
        Assert.Equal(new Rgb(255, 0, 0), leds[0]);
        Assert.Equal(new Rgb(0, 255, 0), leds[4]);
        Assert.Equal(new Rgb(0, 0, 255), leds[8]);
    }

    [Fact]
    public void Run_moves_and_bands_keep_hard_edges()
    {
        Assert.NotEqual(Draw("run", 0, "#FF0000", "#0000FF"), Draw("run", 1, "#FF0000", "#0000FF"));
        var bands = Draw("bands", 0, "#FF0000", "#0000FF");
        Assert.All(bands.Take(5), c => Assert.Equal(new Rgb(255, 0, 0), c));
    }

    [Theory]
    [MemberData(nameof(AllMotions))]
    public void Every_motion_draws_only_the_chosen_colors_or_darker(string motion)
    {
        for (double t = 0; t < 6; t += 0.37)
            foreach (var c in Draw(motion, t, "#FF0000", "#0000FF"))
                Assert.True(c.G <= Math.Max(c.R, c.B), $"{c} turned green");   // red to blue goes through purple
    }

    [Fact]
    public void Blink_turns_off_between_colors()
    {
        Assert.Equal(new Rgb(255, 0, 0), Draw("blink", 0.1, "#FF0000", "#0000FF")[0]);
        Assert.Equal(Rgb.Black, Draw("blink", 0.5, "#FF0000", "#0000FF")[0]);
        Assert.Equal(new Rgb(0, 0, 255), Draw("blink", 0.7, "#FF0000", "#0000FF")[0]);
    }

    public static IEnumerable<object[]> AllMotions => MyColorsEffect.Motions.Select(m => new object[] { m.Id });
}
