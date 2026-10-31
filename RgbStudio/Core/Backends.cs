using System.Runtime.InteropServices;

namespace RgbStudio.Core;

public sealed record DeviceInfo(int Index, string Name, uint Type, int MaxLeds)
{
    /// <summary>What the screen calls it; <see cref="Name"/> stays the key in the settings.</summary>
    public string Label => Name.StartsWith("ENE_RGB", StringComparison.Ordinal) && char.IsDigit(Name[^1])
        ? $"Memória {Name[^1] - '0' + 1}"
        : Name;

    /// <summary>A friendly name from the SDK's device type.</summary>
    public string Kind => (Type & 0xFFFF0000) switch
    {
        0x00010000 when Type == 0x00011000 => "Fita ARGB",
        0x00010000 => "Placa-mãe",
        0x00011000 => "Fita ARGB",
        0x00070000 => "Memória RAM",
        0x00020000 => "Placa de vídeo",
        0x00080000 => "Teclado",
        0x00030000 or 0x00040000 => "Periférico",
        0x000A0000 => "Sensor de fluxo",
        _ => $"Dispositivo 0x{Type:X8}",
    };

    public bool IsAddressableStrip => Type == 0x00011000;
}

/// <summary>Where colors go. All calls come from the engine's one thread.</summary>
public interface IRgbBackend : IDisposable
{
    string Name { get; }
    IReadOnlyList<DeviceInfo> Open();
    void Write(int device, ReadOnlySpan<Rgb> colors);
    /// <summary>Hands the lights back to Armoury Crate's own lighting.</summary>
    void Release();
}

/// <summary>
/// The official ASUS Aura SDK (COM "aura.sdk.1", AuraSdk_x64.dll, installed
/// with Armoury Crate). It needs administrator rights to reach memory (SMBus)
/// and some controllers; it talks to the hardware through LightingService.
/// Colors are 0x00BBGGRR; nothing shows until the device's Apply().
/// </summary>
public sealed class AuraSdkBackend : IRgbBackend
{
    dynamic? _sdk;
    readonly List<dynamic> _devices = [];
    readonly List<uint[]> _last = [];
    readonly Func<string, bool> _wanted;

    public AuraSdkBackend(Func<string, bool>? wanted = null) => _wanted = wanted ?? (_ => true);

    public string Name => "ASUS Aura SDK";

    public IReadOnlyList<DeviceInfo> Open()
    {
        var type = Type.GetTypeFromProgID("aura.sdk.1")
            ?? throw new InvalidOperationException("O Aura SDK não está registrado (instale o Armoury Crate / AuraSDK).");
        _sdk = Activator.CreateInstance(type)!;
        _sdk.SwitchMode();                     // take control from Armoury Crate's effects
        dynamic list = _sdk.Enumerate(0);     // 0 = every device type
        var infos = new List<DeviceInfo>();
        int count = list.Count;
        for (int i = 0; i < count; i++)
        {
            dynamic d = list[i];
            if (!_wanted((string)d.Name)) continue;
            _devices.Add(d);
            int leds = d.Lights.Count;
            _last.Add(new uint[leds]);
            Array.Fill(_last[^1], uint.MaxValue);   // force the first write
            infos.Add(new DeviceInfo(infos.Count, (string)d.Name, (uint)d.Type, leds));
        }
        return infos;
    }

    public void Write(int device, ReadOnlySpan<Rgb> colors)
    {
        dynamic d = _devices[device];
        dynamic lights = d.Lights;
        var last = _last[device];
        bool changed = false;
        int n = Math.Min(colors.Length, last.Length);
        for (int i = 0; i < n; i++)
        {
            uint c = colors[i].ToAura();
            if (c == last[i]) continue;        // each COM call costs; skip what did not change
            lights[i].Color = c;
            last[i] = c;
            changed = true;
        }
        if (changed) d.Apply();
    }

    public void Release()
    {
        try { _sdk?.ReleaseControl(0); } catch (COMException) { }
    }

    public void Dispose()
    {
        Release();
        foreach (var d in _devices) Marshal.FinalReleaseComObject(d);
        _devices.Clear();
        if (_sdk is not null) Marshal.FinalReleaseComObject(_sdk);
        _sdk = null;
    }
}

/// <summary>No hardware: the same devices as this machine, for tests and previews.</summary>
public sealed class SimulatedBackend : IRgbBackend
{
    public readonly Dictionary<int, Rgb[]> Written = [];
    public int Writes;
    public bool Released;
    public string Name => "Simulação (sem hardware)";

    public IReadOnlyList<DeviceInfo> Open() =>
    [
        new(0, "ENE_RGB_For_ASUS0", 0x00070000, 8),
        new(1, "ENE_RGB_For_ASUS1", 0x00070000, 8),
        new(2, "Mainboard_Master", 0x00010000, 3),
        new(3, "AddressableStrip 1", 0x00011000, 120),
        new(4, "AddressableStrip 2", 0x00011000, 120),
    ];

    public void Write(int device, ReadOnlySpan<Rgb> colors)
    {
        Written[device] = colors.ToArray();
        Writes++;
    }

    public void Release() => Released = true;
    public void Dispose() { }
}
