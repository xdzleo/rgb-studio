using RgbStudio.Core;
using Xunit;

namespace RgbStudio.Tests;

public class PresetEngineTests
{
    static Engine Make(AppSettings s, SimulatedBackend backend)
    {
        var engine = new Engine(s, () => backend);
        // Open the simulated devices the way the engine's thread does, without the thread.
        typeof(Engine).GetProperty("Devices")!.SetValue(engine, backend.Open());
        return engine;
    }

    static AppSettings WithStatic(string hex)
    {
        var s = new AppSettings { Gamma = false, HeaderMilliamps = 0, PowerLimit = 100 };
        var p = s.Builtin("static").Clone();
        p.Color1 = hex;
        s.Put(p);
        s.ActivePreset = p.Id;
        return s;
    }

    [Fact]
    public void Writes_every_device_with_its_full_led_count()
    {
        var backend = new SimulatedBackend();
        Make(WithStatic("#00FF00"), backend).RenderFrame(backend, 0);
        Assert.Equal(5, backend.Written.Count);
        Assert.Equal(120, backend.Written[3].Length);
        Assert.All(backend.Written[2], c => Assert.Equal(new Rgb(0, 255, 0), c));
    }

    [Fact]
    public void Disabled_devices_go_dark_and_short_strips_stop_at_their_length()
    {
        var backend = new SimulatedBackend();
        var s = WithStatic("#FFFFFF");
        s.For("Mainboard_Master").Enabled = false;
        s.For("AddressableStrip 1").LedCount = 30;
        Make(s, backend).RenderFrame(backend, 0);
        Assert.All(backend.Written[2], c => Assert.Equal(Rgb.Black, c));
        Assert.Equal(Rgb.White, backend.Written[3][29]);
        Assert.Equal(Rgb.Black, backend.Written[3][30]);
    }

    [Fact]
    public void A_device_with_its_own_effect_runs_it_and_the_rest_follow_the_active_one()
    {
        var backend = new SimulatedBackend();
        var s = WithStatic("#FF0000");
        var mine = Presets.NewFrom(s.Builtin("static"), "Azul");
        mine.Color1 = "#0000FF";
        s.MyEffects.Add(mine);
        s.For("Mainboard_Master").Preset = mine.Id;

        Make(s, backend).RenderFrame(backend, 0);
        Assert.All(backend.Written[2], c => Assert.Equal(new Rgb(0, 0, 255), c));   // placed
        Assert.All(backend.Written[0], c => Assert.Equal(new Rgb(255, 0, 0), c));   // follows the active one
    }

    [Fact]
    public void Deleting_an_effect_sends_its_devices_back_to_the_active_one()
    {
        var s = WithStatic("#FF0000");
        var mine = Presets.NewFrom(s.Builtin("static"), "x");
        s.MyEffects.Add(mine);
        s.For("AddressableStrip 2").Preset = mine.Id;
        s.Delete(mine.Id);
        Assert.Empty(s.MyEffects);
        Assert.Null(s.For("AddressableStrip 2").Preset);
    }

    [Fact]
    public void A_dangling_placement_falls_back_instead_of_failing()
    {
        var s = WithStatic("#123456");
        Assert.Equal("#123456", s.Resolve("no-such-id").Color1);
    }

    [Fact]
    public void Chain_spreads_the_active_gradient_across_the_devices_that_follow_it()
    {
        var backend = new SimulatedBackend();
        var s = new AppSettings { Gamma = false, HeaderMilliamps = 0, PowerLimit = 100, Chain = true, ActivePreset = Presets.BuiltinId("gradient") };
        var g = s.Builtin("gradient").Clone();
        g.Color1 = "#FF0000"; g.Color2 = "#0000FF";
        s.Put(g);
        foreach (var name in new[] { "ENE_RGB_For_ASUS0", "ENE_RGB_For_ASUS1", "Mainboard_Master" }) s.For(name).Enabled = false;

        Make(s, backend).RenderFrame(backend, 0);
        // Strip 1 starts red, strip 2 ends blue: one gradient over 240 LEDs.
        Assert.Equal(new Rgb(255, 0, 0), backend.Written[3][0]);
        Assert.True(backend.Written[4][119].B > 250);
        Assert.True(backend.Written[3][119].R > 120 && backend.Written[4][0].B > 120);
    }

    [Fact]
    public void Sequence_fades_through_its_colors_and_bands_lay_them_along_the_strip()
    {
        var p = new EffectParams { Colors = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255)], Speed = 1 };
        var one = new Rgb[1];
        var seq = new SequenceEffect();
        seq.Render(one, 0, p, 0);
        Assert.Equal(new Rgb(255, 0, 0), one[0]);
        seq.Render(one, 2, p, 0);                 // 2 s per color at speed 1
        Assert.Equal(new Rgb(0, 255, 0), one[0]);

        p.Bands = true;
        var strip = new Rgb[30];
        seq.Render(strip, 0, p, 0);
        Assert.Equal(new Rgb(255, 0, 0), strip[0]);
        Assert.Equal(new Rgb(0, 255, 0), strip[10]);
        Assert.Equal(new Rgb(0, 0, 255), strip[20]);
    }

    [Fact]
    public void Old_settings_become_a_tweaked_builtin()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rgbstudio-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "Effect": "gradient", "Color1": "#112233", "Speed": 2.5, "Chain": true }""");
            var s = AppSettings.Load(path);
            Assert.Equal(Presets.BuiltinId("gradient"), s.ActivePreset);
            Assert.Equal("#112233", s.Resolve(null).Color1);
            Assert.Equal(2.5, s.Resolve(null).Speed);
            Assert.True(s.Chain);
            s.Save(path);
            // The old top-level fields are gone (presets keep an "Effect" of their own).
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.False(doc.RootElement.TryGetProperty("Effect", out _));
            Assert.False(doc.RootElement.TryGetProperty("Color1", out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void My_effects_and_placements_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rgbstudio-{Guid.NewGuid():N}.json");
        try
        {
            var s = new AppSettings();
            var mine = Presets.NewFrom(s.Builtin("sequence"), "Natal");
            mine.Colors = ["#FF0000", "#00FF00"];
            mine.Bands = true;
            s.MyEffects.Add(mine);
            s.For("AddressableStrip 1").Preset = mine.Id;
            s.For("AddressableStrip 1").LedCount = 42;
            s.Save(path);

            var back = AppSettings.Load(path);
            var p = back.Resolve(back.For("AddressableStrip 1").Preset);
            Assert.Equal("Natal", p.Name);
            Assert.Equal(["#FF0000", "#00FF00"], p.Colors);
            Assert.True(p.Bands);
            Assert.Equal(42, back.For("AddressableStrip 1").LedCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_locator_lights_up_to_the_led_even_on_a_zone_that_is_off_and_is_never_saved()
    {
        var backend = new SimulatedBackend();
        var s = new AppSettings { Gamma = false, HeaderMilliamps = 0, PowerLimit = 100, LocatorZone = "AddressableStrip 1", LocatorLed = 12 };
        s.For("AddressableStrip 1").Enabled = false;
        var engine = new Engine(s.Clone(), () => backend);
        typeof(Engine).GetProperty("Devices")!.SetValue(engine, backend.Open());
        engine.RenderFrame(backend, 0);
        var leds = backend.Written[3];
        Assert.Equal(new Rgb(0, 120, 0), leds[10]);
        Assert.Equal(new Rgb(220, 220, 220), leds[11]);
        Assert.Equal(Rgb.Black, leds[12]);
        Assert.Equal(Rgb.Black, leds[119]);

        var path = Path.Combine(Path.GetTempPath(), $"rgbstudio-{Guid.NewGuid():N}.json");
        try
        {
            s.Save(path);
            Assert.Null(AppSettings.Load(path).LocatorZone);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_zone_name_survives_saving_and_the_engine_copy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rgbstudio-{Guid.NewGuid():N}.json");
        try
        {
            var s = new AppSettings();
            s.For("Conector ARGB 2").Alias = "Reservatório";
            Assert.Equal("Reservatório", s.Clone().For("Conector ARGB 2").Alias);
            s.Save(path);
            Assert.Equal("Reservatório", AppSettings.Load(path).For("Conector ARGB 2").Alias);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Chained_effects_skip_zones_that_are_off()
    {
        var backend = new SimulatedBackend();
        var s = new AppSettings { Gamma = false, HeaderMilliamps = 0, PowerLimit = 100, Chain = true, ActivePreset = Presets.BuiltinId("gradient") };
        var g = s.Builtin("gradient").Clone();
        g.Color1 = "#FF0000"; g.Color2 = "#0000FF";
        s.Put(g);
        foreach (var name in new[] { "ENE_RGB_For_ASUS0", "ENE_RGB_For_ASUS1", "Mainboard_Master", "AddressableStrip 1" }) s.For(name).Enabled = false;
        Make(s, backend).RenderFrame(backend, 0);
        // The only zone on gets the whole gradient, end to end.
        Assert.Equal(new Rgb(255, 0, 0), backend.Written[4][0]);
        Assert.True(backend.Written[4][119].B > 250);
        Assert.All(backend.Written[3], c => Assert.Equal(Rgb.Black, c));
    }

    [Fact]
    public void The_engine_copy_is_independent_of_the_ui_copy()
    {
        var s = WithStatic("#FF0000");
        var copy = s.Clone();
        s.Resolve(null).Color1 = "#000000";
        s.MyEffects.Add(Presets.NewFrom(s.Builtin("static"), "y"));
        Assert.Equal("#FF0000", copy.Resolve(null).Color1);
        Assert.Empty(copy.MyEffects);
    }
}
