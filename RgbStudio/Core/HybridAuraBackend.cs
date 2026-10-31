namespace RgbStudio.Core;

/// <summary>
/// What worked on this board: mainboard and ARGB over USB, memory through the
/// official Aura SDK. Memory only answered the SDK right after LightingService
/// restarted (Apply took 3-8 ms instead of 0), and with the SDK in control the
/// service did not write over USB. So: restart the service, open the SDK, open USB.
/// Memory is never written over SMBus directly: that bus also holds the modules' SPD.
/// If the SDK brings no memory, the service is stopped and USB runs alone.
/// </summary>
public sealed class HybridAuraBackend : IRgbBackend
{
    const int Attempts = 3;
    readonly int _expectedMemory;
    readonly List<(IRgbBackend Backend, int Index)> _map = [];
    HidAuraBackend? _usb;
    AuraSdkBackend? _sdk;
    EneSmbusBackend? _ene;

    /// <param name="expectedMemory">Memory sticks seen before: fewer now means the SDK came up short.</param>
    public HybridAuraBackend(int expectedMemory = 0) => _expectedMemory = expectedMemory;

    public string Name { get; private set; } = "USB + Aura SDK";

    /// <summary>
    /// Opens until enough devices come back or the attempts run out, then keeps the last.
    /// The SDK has reported one stick of two (seen once, cause unknown), so a short
    /// answer gets another try rather than a zone quietly missing.
    /// </summary>
    public static IReadOnlyList<T> OpenUntilEnough<T>(Func<IReadOnlyList<T>> open, Action discard, int expected, int attempts, Action<int, int> retrying)
    {
        for (int attempt = 1; ; attempt++)
        {
            var found = open();
            if (found.Count >= expected || attempt >= attempts) return found;
            retrying(attempt, found.Count);
            discard();
        }
    }

    public IReadOnlyList<DeviceInfo> Open()
    {
        IReadOnlyList<DeviceInfo> memory = [];
        try
        {
            ServiceControl.Stop();           // the SDK starts it again, fresh
            Thread.Sleep(1000);
            memory = OpenUntilEnough(
                () =>
                {
                    _sdk = new AuraSdkBackend(name => name.StartsWith("ENE_RGB", StringComparison.Ordinal));
                    return _sdk.Open();
                },
                () => { _sdk?.Dispose(); _sdk = null; Thread.Sleep(2000); },
                _expectedMemory,
                Attempts,
                (attempt, count) => Log.Write($"SDK achou {count} de {_expectedMemory} memórias (tentativa {attempt}); reabrindo."));
            if (memory.Count < _expectedMemory) Log.Write($"Ficou com {memory.Count} de {_expectedMemory} memórias depois de {Attempts} tentativas.");
        }
        catch (Exception ex)
        {
            Log.Write($"Memória indisponível pelo SDK: {ex.Message}");
            _sdk?.Dispose();
            _sdk = null;
        }
        IRgbBackend? memoryBackend = _sdk;
        if (memory.Count == 0)
        {
            _sdk?.Dispose();
            _sdk = null;
            // No Aura SDK (Armoury Crate removed): the RAM straight over SMBus.
            try
            {
                _ene = new EneSmbusBackend();
                memory = _ene.Open();
            }
            catch (Exception ex) { Log.Write($"Memória pelo SMBus indisponível: {ex.Message}"); memory = []; }
            if (memory.Count == 0) { _ene?.Dispose(); _ene = null; }
            memoryBackend = _ene;
            Name = _ene is null ? "USB direto (sem memória)" : "USB + RAM direto";
        }

        _usb = new HidAuraBackend(manageService: _sdk is null);
        var devices = new List<DeviceInfo>();
        foreach (var d in _usb.Open())
        {
            _map.Add((_usb, d.Index));
            devices.Add(d with { Index = devices.Count });
        }
        if (memoryBackend is not null)
            foreach (var d in memory)
            {
                _map.Add((memoryBackend, d.Index));
                devices.Add(d with { Index = devices.Count });
            }
        return devices;
    }

    public void Write(int device, ReadOnlySpan<Rgb> colors)
    {
        var (backend, index) = _map[device];
        backend.Write(index, colors);
    }

    public void Release()
    {
        _usb?.Release();
        _sdk?.Release();
        _ene?.Release();
    }

    public void Dispose()
    {
        _usb?.Dispose();
        _sdk?.Dispose();
        _ene?.Dispose();
    }
}
