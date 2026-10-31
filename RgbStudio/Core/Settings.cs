using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RgbStudio.Core;

public sealed class DeviceSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>LEDs actually on the strip (an ARGB header reports up to 120). 0 = all.</summary>
    public int LedCount { get; set; }
    /// <summary>The effect placed on this device; null = the one active on every device.</summary>
    public string? Preset { get; set; }
    /// <summary>What the person calls this zone ("Reservatório"); null = the device's own name.</summary>
    public string? Alias { get; set; }
    /// <summary>
    /// Per-channel ceiling for this zone in percent, as FastLED's setCorrection: LEDs differ, and
    /// balancing means lowering the strong channels, since none can go past full.
    /// </summary>
    public int RedGain { get; set; } = 100;
    public int GreenGain { get; set; } = 100;
    public int BlueGain { get; set; } = 100;

    public DeviceSettings Clone() => new()
    {
        Enabled = Enabled, LedCount = LedCount, Preset = Preset, Alias = Alias,
        RedGain = RedGain, GreenGain = GreenGain, BlueGain = BlueGain,
    };
}

public sealed class AppSettings
{
    /// <summary>The effect on every device that has none of its own.</summary>
    public string ActivePreset { get; set; } = Presets.BuiltinId("rainbow");
    /// <summary>The person's own effects ("Meus efeitos"), in the order shown.</summary>
    public List<EffectPreset> MyEffects { get; set; } = [];
    /// <summary>Colors and settings changed on built-in effects, by preset id.</summary>
    public Dictionary<string, EffectPreset> BuiltinTweaks { get; set; } = [];
    /// <summary>Treat the devices on the shared effect as one long strip, so waves and comets travel across them.</summary>
    public bool Chain { get; set; }
    public int Fps { get; set; } = 30;
    public bool StartMinimized { get; set; }
    /// <summary>A ceiling on every LED, in percent, whatever the effect asks: long strips draw real current.</summary>
    public int PowerLimit { get; set; } = 60;
    /// <summary>Corrects for how the eye sees light, so fades stay smooth and colors keep their hue.</summary>
    public bool Gamma { get; set; } = true;
    /// <summary>
    /// Ceiling per ARGB connector, in estimated mA (worst case 60 mA for a white LED). 0 = none.
    /// An estimate, not a measurement; the default is conservative, not the board's rating.
    /// </summary>
    public int HeaderMilliamps { get; set; } = 2000;
    /// <summary>The zone being counted by eye, if any; never saved.</summary>
    [JsonIgnore] public string? LocatorZone { get; set; }
    /// <summary>Which LED (from 1) the count is at: green up to it, this one white.</summary>
    [JsonIgnore] public int LocatorLed { get; set; }
    public Dictionary<string, DeviceSettings> Devices { get; set; } = [];

    // Version 0.1 kept one effect's settings here; read once, then moved into a preset.
    [JsonPropertyName("Effect")] public string? LegacyEffect { get; set; }
    [JsonPropertyName("Color1")] public string? LegacyColor1 { get; set; }
    [JsonPropertyName("Color2")] public string? LegacyColor2 { get; set; }
    [JsonPropertyName("Speed")] public double? LegacySpeed { get; set; }
    [JsonPropertyName("Brightness")] public int? LegacyBrightness { get; set; }
    [JsonPropertyName("Reverse")] public bool? LegacyReverse { get; set; }

    /// <summary>Built-ins (with the person's tweaks), then the person's own.</summary>
    public IEnumerable<EffectPreset> AllPresets() =>
        EffectCatalog.All.Select(e => Builtin(e.Id)).Concat(MyEffects);

    public EffectPreset Builtin(string effectId)
    {
        var id = Presets.BuiltinId(effectId);
        return BuiltinTweaks.TryGetValue(id, out var t) ? t : Presets.BuiltinDefault(EffectCatalog.Get(effectId));
    }

    /// <summary>The preset by id; a missing one (deleted) falls back to the shared effect, then to Arco-íris.</summary>
    public EffectPreset Resolve(string? id)
    {
        id ??= ActivePreset;
        if (id.StartsWith(Presets.BuiltinPrefix, StringComparison.Ordinal))
            return Builtin(id[Presets.BuiltinPrefix.Length..]);
        return MyEffects.FirstOrDefault(p => p.Id == id)
            ?? (id != ActivePreset ? Resolve(ActivePreset) : Builtin("rainbow"));
    }

    /// <summary>Stores a change to any preset: a built-in's goes to its tweaks.</summary>
    public void Put(EffectPreset preset)
    {
        if (preset.IsBuiltin) { BuiltinTweaks[preset.Id] = preset; return; }
        int i = MyEffects.FindIndex(p => p.Id == preset.Id);
        if (i >= 0) MyEffects[i] = preset; else MyEffects.Add(preset);
    }

    /// <summary>Deletes one of the person's effects; devices that had it go back to the shared one.</summary>
    public void Delete(string id)
    {
        MyEffects.RemoveAll(p => p.Id == id);
        foreach (var d in Devices.Values) if (d.Preset == id) d.Preset = null;
        if (ActivePreset == id) ActivePreset = Presets.BuiltinId("rainbow");
    }

    /// <summary>Puts an effect on one device, or (null) on all of them, clearing their own.</summary>
    public void Place(string? device, string presetId)
    {
        if (device is null)
        {
            ActivePreset = presetId;
            foreach (var d in Devices.Values) d.Preset = null;
        }
        else
        {
            For(device).Preset = presetId == ActivePreset ? null : presetId;
        }
    }

    public DeviceSettings For(string device)
    {
        if (!Devices.TryGetValue(device, out var s)) Devices[device] = s = new DeviceSettings();
        return s;
    }

    /// <summary>A deep copy: the engine draws from its own, so the UI can edit freely.</summary>
    public AppSettings Clone() => new()
    {
        ActivePreset = ActivePreset,
        MyEffects = MyEffects.Select(p => p.Clone()).ToList(),
        BuiltinTweaks = BuiltinTweaks.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
        Chain = Chain,
        Fps = Fps,
        StartMinimized = StartMinimized,
        PowerLimit = PowerLimit,
        Gamma = Gamma,
        LocatorZone = LocatorZone,
        LocatorLed = LocatorLed,
        HeaderMilliamps = HeaderMilliamps,
        Devices =Devices.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
    };

    void Migrate()
    {
        if (LegacyEffect is null) return;
        var p = Builtin(LegacyEffect).Clone();
        if (LegacyColor1 is not null) p.Color1 = LegacyColor1;
        if (LegacyColor2 is not null) p.Color2 = LegacyColor2;
        if (LegacySpeed is not null) p.Speed = LegacySpeed.Value;
        if (LegacyBrightness is not null) p.Brightness = LegacyBrightness.Value;
        if (LegacyReverse is not null) p.Reverse = LegacyReverse.Value;
        Put(p);
        ActivePreset = p.Id;
        LegacyEffect = LegacyColor1 = LegacyColor2 = null;
        LegacySpeed = null; LegacyBrightness = null; LegacyReverse = null;
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RgbStudio", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        AppSettings s;
        try { s = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (JsonException) { s = new(); }
        s.Migrate();
        return s;
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
