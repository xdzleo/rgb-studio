using System.Text.Json.Serialization;

namespace RgbStudio.Core;

/// <summary>
/// An effect as the person uses it: a base effect plus its colors and
/// settings, under a name. Built-in presets ("builtin:rainbow") come with the
/// app, one per base effect; the person's own ("Meus efeitos") have a GUID id.
/// </summary>
public sealed class EffectPreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The base effect's id in <see cref="EffectCatalog"/>.</summary>
    public string Effect { get; set; } = "static";
    public string Color1 { get; set; } = "#FF0028";
    public string Color2 { get; set; } = "#0078FF";
    /// <summary>For effects that take a list of colors (Sequência).</summary>
    public List<string> Colors { get; set; } = [];
    /// <summary>Sequência: colors as bands moving along the strip, instead of the whole strip fading.</summary>
    public bool Bands { get; set; }
    public double Speed { get; set; } = 1;
    public int Brightness { get; set; } = 100;
    public bool Reverse { get; set; }

    [JsonIgnore] public bool IsBuiltin => Id.StartsWith(Presets.BuiltinPrefix, StringComparison.Ordinal);

    public EffectParams ToParams() => new()
    {
        Color1 = Rgb.FromHex(Color1),
        Color2 = Rgb.FromHex(Color2),
        Colors = Colors.Select(Rgb.FromHex).ToArray(),
        Bands = Bands,
        Speed = Math.Clamp(Speed, 0.1, 5),
        Brightness = Math.Clamp(Brightness, 0, 100),
        Reverse = Reverse,
    };

    public EffectPreset Clone() => new()
    {
        Id = Id, Name = Name, Effect = Effect, Color1 = Color1, Color2 = Color2,
        Colors = [.. Colors], Bands = Bands, Speed = Speed, Brightness = Brightness, Reverse = Reverse,
    };
}

public static class Presets
{
    public const string BuiltinPrefix = "builtin:";

    public static string BuiltinId(string effect) => BuiltinPrefix + effect;

    /// <summary>The built-in preset of a base effect, as it ships.</summary>
    public static EffectPreset BuiltinDefault(Effect e) => new()
    {
        Id = BuiltinId(e.Id),
        Name = e.Name,
        Effect = e.Id,
        Color1 = e.DefaultColor1,
        Color2 = e.DefaultColor2,
        Colors = e.UsesColors ? ["#FF0028", "#FFB000", "#00D26A", "#0078FF", "#A100FF"] : [],
    };

    /// <summary>A new effect of the person's own, starting from <paramref name="from"/>.</summary>
    public static EffectPreset NewFrom(EffectPreset from, string name)
    {
        var p = from.Clone();
        p.Id = Guid.NewGuid().ToString("N");
        p.Name = name;
        if (EffectCatalog.Get(p.Effect).UsesColors && p.Colors.Count == 0)
            p.Colors = [p.Color1, p.Color2];
        return p;
    }
}
