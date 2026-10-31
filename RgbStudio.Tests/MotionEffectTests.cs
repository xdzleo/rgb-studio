using RgbStudio.Core;
using Xunit;

namespace RgbStudio.Tests;

public class MotionEffectTests
{
    static IEnumerable<Effect> Motion => EffectCatalog.All.Where(e => e.Category != "Clássicos");

    static EffectParams Params(Effect e) => Presets.BuiltinDefault(e).ToParams();

    public static IEnumerable<object[]> MotionIds => EffectCatalog.All.Where(e => e.Category != "Clássicos").Select(e => new object[] { e.Id });

    [Fact]
    public void The_new_tabs_are_full_with_unique_ids_and_descriptions()
    {
        Assert.Equal(17, EffectCatalog.All.Count(e => e.Category == "RGB puro"));
        Assert.Equal(24, EffectCatalog.All.Count(e => e.Category == "Orgulho"));
        Assert.True(EffectCatalog.All.Count(e => e.Category == "Países") >= 70);
        Assert.Equal("Países", EffectCatalog.Get("grad-brasil").Category);   // moved, same id: saved zones keep it
        Assert.True(EffectCatalog.All.Count(e => e.Category == "Gradientes") >= 90);
        Assert.Equal(25, EffectCatalog.All.Count(e => e.Category == "Motion"));
        Assert.Equal(12, EffectCatalog.All.Count(e => e.Category == "Ciência"));
        Assert.Equal(9, EffectCatalog.All.Count(e => e.Category == "Sinais"));
        Assert.Equal(EffectCatalog.All.Count, EffectCatalog.All.Select(e => e.Id).Distinct().Count());
        Assert.All(Motion, e => Assert.False(string.IsNullOrWhiteSpace(e.Blurb)));
    }

    [Theory]
    [MemberData(nameof(MotionIds))]
    public void Lights_up_and_moves_on_a_long_strip(string id)
    {
        var e = EffectCatalog.Get(id);
        var seen = new HashSet<string>();
        bool lit = false;
        for (double t = 0; t < 12; t += 0.37)
        {
            var leds = new Rgb[120];
            e.Render(leds, t, Params(e), 0);
            lit |= leds.Any(c => c != Rgb.Black);
            seen.Add(string.Join(',', leds.Select(c => c.ToHex())));
        }
        Assert.True(lit, $"{id} never lit");
        // A traffic light or a siren has only a few looks, on purpose; the rest must keep moving.
        int enough = e.Category == "Sinais" ? 1 : 10;
        Assert.True(seen.Count > enough, $"{id} barely changes: {seen.Count} distinct frames");
    }

    [Theory]
    [MemberData(nameof(MotionIds))]
    public void Is_visible_on_the_three_leds_of_the_mainboard(string id)
    {
        var e = EffectCatalog.Get(id);
        bool lit = false;
        for (double t = 0; t < 40 && !lit; t += 0.11)
        {
            var leds = new Rgb[3];
            e.Render(leds, t, Params(e), 0);
            lit = leds.Any(c => c.R + c.G + c.B > 40);
        }
        Assert.True(lit, $"{id} shows nothing on 3 LEDs");
    }

    [Theory]
    [MemberData(nameof(MotionIds))]
    public void The_same_moment_always_draws_the_same(string id)
    {
        var e = EffectCatalog.Get(id);
        var a = new Rgb[60]; var b = new Rgb[60];
        e.Render(a, 7.25, Params(e), 42);
        e.Render(b, 7.25, Params(e), 42);
        Assert.Equal(a, b);
    }

    [Theory]
    [MemberData(nameof(MotionIds))]
    public void Survives_empty_strips_late_times_and_extreme_speeds(string id)
    {
        var e = EffectCatalog.Get(id);
        foreach (int n in new[] { 0, 1, 2, 8 })
            foreach (double t in new[] { 0.0, 86400.0 * 30 })
                foreach (double speed in new[] { 0.1, 5.0 })
                {
                    var p = Params(e);
                    p.Speed = speed;
                    e.Render(new Rgb[n], t, p, 1009);
                }
    }

    [Theory]
    [MemberData(nameof(MotionIds))]
    public void Draws_120_leds_well_inside_one_frame(string id)
    {
        var e = EffectCatalog.Get(id);
        var pars = Params(e);
        var leds = new Rgb[120];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        const int Frames = 40;
        for (int f = 0; f < Frames; f++) e.Render(leds, 5 + f * 0.31, pars, 0);
        double ms = watch.Elapsed.TotalMilliseconds / Frames;
        Assert.True(ms < 8, $"{id} takes {ms:0.0} ms per frame");
    }

    [Fact]
    public void Police_flashes_each_color_on_its_own_half_never_both()
    {
        var e = new PoliceEffect();
        var p = Params(e);
        bool sawRed = false, sawBlue = false;
        for (double t = 0; t < 2.2; t += 0.01)
        {
            var leds = new Rgb[40];
            e.Render(leds, t, p, 0);
            bool red = leds[..20].Any(c => c.R > 40), blue = leds[20..].Any(c => c.B > 40);
            Assert.False(red && blue, $"both sides lit at {t}");
            Assert.DoesNotContain(leds[..20], c => c.B > c.R);
            Assert.DoesNotContain(leds[20..], c => c.R > c.B);
            sawRed |= red; sawBlue |= blue;
        }
        Assert.True(sawRed && sawBlue);
    }

    [Fact]
    public void Police_lights_every_led_of_a_side_equally_even_next_to_the_center()
    {
        var e = new PoliceEffect();
        var p = Params(e);
        var leds = new Rgb[32];
        e.Render(leds, 1.1 * 0.5 + 0.02, p, 0);      // blue side, first flash
        Assert.All(leds[16..], c => Assert.Equal(Rgb.FromHex(p.Color2.ToHex()), c));
        Assert.All(leds[..16], c => Assert.Equal(Rgb.Black, c));
    }

    [Fact]
    public void Calibration_lowers_only_its_zones_channels_and_starts_neutral()
    {
        var backend = new SimulatedBackend();
        var s = new AppSettings { PowerLimit = 100, HeaderMilliamps = 0, Gamma = false };
        var p = s.Builtin("static").Clone();
        p.Color1 = "#FFFFFF";
        s.Put(p);
        s.ActivePreset = p.Id;
        s.For("ENE_RGB_For_ASUS1").GreenGain = 50;
        s.For("ENE_RGB_For_ASUS1").RedGain = 80;
        var engine = new Engine(s.Clone(), () => backend);
        typeof(Engine).GetProperty("Devices")!.SetValue(engine, backend.Open());
        engine.RenderFrame(backend, 0);
        Assert.Equal(new Rgb(204, 127, 255), backend.Written[1][0]);
        Assert.Equal(Rgb.White, backend.Written[0][0]);       // other zones: neutral
        Assert.Equal(Rgb.White, engine.Frame[1][0]);          // the screen keeps the design
    }

    [Fact]
    public void Blue_is_sent_exactly_as_chosen_with_no_green_or_red_added()
    {
        var backend = new SimulatedBackend();
        var s = new AppSettings { PowerLimit = 100, HeaderMilliamps = 0, Gamma = true };
        var p = s.Builtin("static").Clone();
        p.Color1 = "#0000FF";
        s.Put(p);
        s.ActivePreset = p.Id;
        var engine = new Engine(s, () => backend);
        typeof(Engine).GetProperty("Devices")!.SetValue(engine, backend.Open());
        engine.RenderFrame(backend, 0);
        Assert.Equal(new Rgb(0, 0, 255), backend.Written[0][0]);
    }

    [Fact]
    public void Perceptual_mix_keeps_the_ends_and_blue_to_white_stays_blue()
    {
        Rgb blue = new(0, 0, 255), red = new(255, 0, 0);
        Assert.Equal(blue, Rgb.MixPerceptual(blue, Rgb.White, 0));
        Assert.Equal(Rgb.White, Rgb.MixPerceptual(blue, Rgb.White, 1));
        Assert.Equal(new Rgb(120, 30, 200), Rgb.MixPerceptual(new Rgb(120, 30, 200), new Rgb(120, 30, 200), 0.5));

        var mid = Rgb.MixPerceptual(blue, Rgb.White, 0.5);
        Assert.True(mid.G >= mid.R, $"drifted to purple: {mid}");   // the plain mix gives R = G
        // Between red and blue, the plain mix sags to a dark purple; this keeps more light.
        var plain = Rgb.Lerp(red, blue, 0.5);
        var ok = Rgb.MixPerceptual(red, blue, 0.5);
        Assert.True(ok.R + ok.G + ok.B >= plain.R + plain.G + plain.B - 10);
    }

    [Fact]
    public void Gradient_palettes_loop_without_a_seam_and_each_is_different()
    {
        var pars = new EffectParams();
        var seen = new HashSet<string>();
        var all = EffectCatalog.All.OfType<PaletteGradientEffect>().ToList();
        foreach (var e in all)
        {
            var leds = new Rgb[120];
            e.Render(leds, 3.3, pars, 0);
            seen.Add(string.Join(",", leds.Select(c => c.ToHex())));
            // Over a whole loop, no step between neighbours may jump: that is what a seam looks like.
            if (e.Blurb.StartsWith("As cores correm", StringComparison.Ordinal))
            {
                var loop = new Rgb[600];
                e.Render(loop, 0, pars, 0);
                for (int i = 1; i < loop.Length; i++)
                    Assert.True(Math.Abs(loop[i].R - loop[i - 1].R) + Math.Abs(loop[i].G - loop[i - 1].G) + Math.Abs(loop[i].B - loop[i - 1].B) < 40, $"{e.Id} jumps at {i}");
                Assert.True(Math.Abs(loop[599].R - loop[0].R) + Math.Abs(loop[599].G - loop[0].G) + Math.Abs(loop[599].B - loop[0].B) < 40, $"{e.Id} has a seam");
            }
        }
        Assert.Equal(all.Count, seen.Count);
    }

    [Fact]
    public void Pure_rgb_stays_fully_saturated_at_every_led()
    {
        var e = EffectCatalog.Get("grad-rgb");
        var leds = new Rgb[200];
        e.Render(leds, 4.2, new EffectParams(), 0);
        foreach (var c in leds)
        {
            Assert.Equal(255, Math.Max(c.R, Math.Max(c.G, c.B)));
            Assert.InRange(Math.Min(c.R, Math.Min(c.G, c.B)), 0, 1);
        }
    }

    [Fact]
    public void All_colors_really_wanders_the_rgb_cube()
    {
        var e = EffectCatalog.Get("grad-rgb-todas");
        var visited = new HashSet<(int, int, int)>();
        var led = new Rgb[1];
        for (double t = 0; t < 2000; t += 0.05)
        {
            e.Render(led, t, new EffectParams(), 0);
            visited.Add((led[0].R / 32, led[0].G / 32, led[0].B / 32));
        }
        Assert.True(visited.Count > 400, $"reached only {visited.Count} of 512 regions");
    }

    [Fact]
    public void Flags_keep_their_stripes_at_the_official_colors()
    {
        var e = EffectCatalog.Get("grad-trans");
        var leds = new Rgb[500];
        e.Render(leds, 0, new EffectParams(), 0);
        foreach (var hex in new[] { "#5BCEFA", "#F5A9B8", "#FFFFFF" })
            Assert.Contains(Rgb.FromHex(hex), leds);
    }

    [Fact]
    public void Sorting_really_ends_sorted()
    {
        var e = new SortingEffect();
        var leds = new Rgb[60];
        e.Render(leds, 12, Params(e), 0);          // in the hold, after 11 s of sorting
        double Hue(Rgb c)
        {
            double r = c.R, g = c.G, b = c.B, max = Math.Max(r, Math.Max(g, b)), d = max - Math.Min(r, Math.Min(g, b));
            return d == 0 ? -1 : max == r ? (60 * ((g - b) / d) + 360) % 360 : max == g ? 60 * ((b - r) / d) + 120 : 60 * ((r - g) / d) + 240;
        }
        var hues = leds.Select(Hue).Where(h => h >= 0).ToList();
        for (int i = 1; i < hues.Count; i++) Assert.True(hues[i] >= hues[i - 1] - 8, $"out of order at {i}");

        var midway = new Rgb[60];
        e.Render(midway, 1, Params(e), 0);
        Assert.NotEqual(leds, midway);
    }

    [Fact]
    public void Automaton_changes_between_generations()
    {
        var e = new AutomatonEffect();
        var a = new Rgb[40]; var b = new Rgb[40];
        e.Render(a, 0, Params(e), 0);
        e.Render(b, 1.0 / 9 * 3, Params(e), 0);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Fractal_center_keeps_coloring_as_it_zooms()
    {
        var e = new FractalEffect();
        for (double t = 0; t < 22; t += 2)
        {
            var leds = new Rgb[64];
            e.Render(leds, t, Params(e), 0);
            Assert.Contains(leds, c => c != Rgb.Black);
        }
    }

    [Fact]
    public void Morse_follows_the_international_timing()
    {
        // S = ... , then a word gap: 1 on, 1 off, 1 on, 1 off, 1 on, 7 off.
        Assert.Equal(new[] { true, false, true, false, true, false, false, false, false, false, false, false }, MorseEffect.Encode("S"));
        // A = .- and N = -. with a letter gap of 3 between them.
        var an = MorseEffect.Encode("AN");
        Assert.Equal("10111000111010000000", string.Concat(an.Select(b => b ? "1" : "0")));
    }

    [Fact]
    public void Hourglass_never_creates_or_loses_sand()
    {
        var e = new HourglassEffect();
        var p = Params(e);
        foreach (double t in new[] { 0.0, 3, 9, 15, 20, 23.9, 26, 40 })
        {
            var leds = new Rgb[40];
            e.Render(leds, t, p, 0);
            int sand = leds.Count(c => c == Rgb.FromHex(p.Color1.ToHex()));
            Assert.InRange(sand, 19, 20);      // 20 grains, one may be in flight (drawn white)
        }
    }

    [Fact]
    public void Gamma_darkens_midtones_and_keeps_the_ends()
    {
        var backend = new SimulatedBackend();
        var s = new AppSettings { PowerLimit = 100, HeaderMilliamps = 0, Gamma = true };
        var p = s.Builtin("static").Clone();
        p.Color1 = "#80FF00";
        s.Put(p);
        s.ActivePreset = p.Id;
        var engine = new Engine(s, () => backend);
        typeof(Engine).GetProperty("Devices")!.SetValue(engine, backend.Open());
        engine.RenderFrame(backend, 0);
        var led = backend.Written[0][0];
        Assert.InRange(led.R, 50, 60);
        Assert.Equal(255, led.G);
        Assert.Equal(0, led.B);
        Assert.Equal(128, engine.Frame[0][0].R);     // the screen keeps the frame as designed
    }

    [Fact]
    public void A_connector_never_goes_over_its_estimated_budget_and_others_are_left_alone()
    {
        var backend = new SimulatedBackend();
        var s = new AppSettings { PowerLimit = 100, Gamma = false, HeaderMilliamps = 2000 };
        var p = s.Builtin("static").Clone();
        p.Color1 = "#FFFFFF";
        s.Put(p);
        s.ActivePreset = p.Id;
        var engine = new Engine(s, () => backend);
        typeof(Engine).GetProperty("Devices")!.SetValue(engine, backend.Open());
        engine.RenderFrame(backend, 0);
        Assert.True(Engine.EstimateMilliamps(backend.Written[3]) <= 2010);   // 120 white LEDs would be 7200
        Assert.True(Engine.EstimateMilliamps(backend.Written[3]) > 1900);
        Assert.Equal(Rgb.White, backend.Written[2][0]);                       // not an ARGB connector
    }

    [Fact]
    public void Enough_memory_opens_once_and_a_short_answer_retries_until_it_runs_out()
    {
        int opens = 0, discards = 0;
        var one = new[] { "a" }; var two = new[] { "a", "b" };

        var r1 = HybridAuraBackend.OpenUntilEnough<string>(() => { opens++; return two; }, () => discards++, 2, 3, (_, _) => { });
        Assert.Equal((2, 1, 0), (r1.Count, opens, discards));

        opens = discards = 0;
        var r2 = HybridAuraBackend.OpenUntilEnough<string>(() => { opens++; return opens < 2 ? one : two; }, () => discards++, 2, 3, (_, _) => { });
        Assert.Equal((2, 2, 1), (r2.Count, opens, discards));

        opens = discards = 0;
        var r3 = HybridAuraBackend.OpenUntilEnough<string>(() => { opens++; return one; }, () => discards++, 2, 3, (_, _) => { });
        Assert.Equal((1, 3, 2), (r3.Count, opens, discards));     // keeps the last; the backend stays open
    }

    [Fact]
    public void Fire_is_hotter_at_the_base_and_reverse_flips_it()
    {
        double Red(Rgb[] leds, int from, int to) => leds[from..to].Average(c => (double)c.R + c.G);
        double baseHeat = 0, tipHeat = 0;
        var e = new FireEffect();
        for (double t = 0; t < 8; t += 0.5)
        {
            var leds = new Rgb[60];
            e.Render(leds, t, Params(e), 0);
            baseHeat += Red(leds, 0, 15);
            tipHeat += Red(leds, 45, 60);
        }
        Assert.True(baseHeat > tipHeat * 2);
    }

    [Fact]
    public void Heartbeat_spreads_from_the_center()
    {
        var e = new HeartbeatEffect();
        var leds = new Rgb[41];
        e.Render(leds, 0.08 * 1.15, Params(e), 0);   // the moment of the first beat, at the center
        Assert.True(leds[20].R > leds[0].R);
    }

    [Fact]
    public void Colliding_pulses_meet_in_the_middle()
    {
        var e = new CollisionEffect();
        var leds = new Rgb[61];
        e.Render(leds, 0.5 * 2.6 + 0.01, Params(e), 0);   // just after impact
        Assert.True(leds[30].G > 200, "the blast core should be nearly white");
    }

    [Fact]
    public void Placing_on_all_clears_each_devices_own_and_on_one_keeps_the_rest()
    {
        var s = new AppSettings();
        s.Place("AddressableStrip 1", Presets.BuiltinId("fire"));
        Assert.Equal(Presets.BuiltinId("fire"), s.For("AddressableStrip 1").Preset);
        Assert.Equal(Presets.BuiltinId("rainbow"), s.ActivePreset);

        s.Place(null, Presets.BuiltinId("plasma"));
        Assert.Equal(Presets.BuiltinId("plasma"), s.ActivePreset);
        Assert.Null(s.For("AddressableStrip 1").Preset);

        s.Place("Mainboard_Master", Presets.BuiltinId("plasma"));   // same as the shared one: follows it
        Assert.Null(s.For("Mainboard_Master").Preset);
    }
}
