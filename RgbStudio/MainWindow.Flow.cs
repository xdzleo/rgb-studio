using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RgbStudio.Core;

namespace RgbStudio;

/// <summary>
/// The high flow NEXT as zones. The sensor runs its own effects, so its zones list the
/// sensor's effects instead of this app's, and a change edits the sensor's settings instead
/// of streaming frames. It applies at once and lasts until the sensor loses power;
/// "Salvar no aparelho" keeps it.
/// </summary>
public partial class MainWindow
{
    const string FlowRing = "flow:ring", FlowPx = "flow:px";

    HighFlowNext? _flow;
    HighFlowNextConfig? _flowEdit;
    string _flowStatus = "";
    TextBlock? _flowStatusText;
    bool _flowWriting, _flowAgain, _flowTried;
    readonly DispatcherTimer _flowSoon = new() { Interval = TimeSpan.FromMilliseconds(250) };

    static bool IsFlowZone(string key) => key.StartsWith("flow:", StringComparison.Ordinal);
    bool FlowZone => IsFlowZone(_zone);

    /// <summary>The ring is the sensor's own LEDs (strip 1); RGBpx is its output, edited as one.</summary>
    IEnumerable<HighFlowNextConfig.Controller> FlowControllers(string key) =>
        _flowEdit?.All.Where(c => c.Count > 0 && c.Strip == (key == FlowRing ? 1 : 0)) ?? [];

    HighFlowNextConfig.Controller? FlowMain(string key) => FlowControllers(key).FirstOrDefault();

    /// <summary>Reads the sensor once, when the zones are first built; a missing one adds nothing.</summary>
    void AddFlowZones()
    {
        if (!_canSave) return;   // a simulation never touches hardware
        if (_flowEdit is null && !_flowTried)
        {
            _flowTried = true;
            try
            {
                _flow = HighFlowNext.Open();
                if (_flow is not null)
                {
                    _flowEdit = _flow.Read();
                    _flowStatus = "Lido do sensor.";
                    Log.Write("high flow NEXT encontrado.");
                }
            }
            catch (Exception ex) { Log.Write($"high flow NEXT: {ex.Message}"); }
            _flowSoon.Tick += (_, _) => { _flowSoon.Stop(); SendFlow(save: false); };
        }
        if (_flowEdit is null) return;
        if (FlowMain(FlowRing) is { } ring) ZoneList.Children.Add(ZoneRow(FlowRing, "Sensor de fluxo · anel", null));
        if (FlowMain(FlowPx) is not null) ZoneList.Children.Add(ZoneRow(FlowPx, "Sensor de fluxo · RGBpx", null));
        PaintFlowRows();
    }

    static string FlowModeName(byte mode) =>
        HighFlowNextConfig.Modes.FirstOrDefault(m => m.Mode == mode) is { Name: { } n } ? n : $"Modo {mode}";

    string FlowEffectName(string key) => FlowMain(key) is { } c ? FlowModeName(c.Mode) : "";

    /// <summary>The device draws the effect, so the strips here show its colors: background with the first color in it.</summary>
    Rgb[] FlowPreview(string key)
    {
        if (FlowMain(key) is not { } c) return [Rgb.Black];
        var bg = c.Color(0); var fg = c.Color(1);
        return c.Mode is 3 or 19 ? Enumerable.Range(0, 24).Select(i => Rgb.FromHsv(i * 15, 1, 1)).ToArray()
            : [bg, bg, fg, fg, bg, bg, bg, bg, fg, fg, bg, bg];
    }

    void PaintFlowRows()
    {
        foreach (var key in new[] { FlowRing, FlowPx })
            if (_zoneStrips.TryGetValue(key, out var row))
            {
                var p = FlowPreview(key);
                Blit(row.Bitmap, p, p.Length, 1);
                row.Effect.Text = FlowEffectName(key);
            }
    }

    void EnterFlowZone()
    {
        Tabs.Visibility = Visibility.Collapsed;
        if (_settings.LocatorZone is not null) { _settings.LocatorZone = null; Changed(); }
        BuildGallery();
        GalleryScroll.ScrollToTop();
        BuildInspector();
        UpdateStageTitle();
    }

    void BuildFlowGallery()
    {
        if (FlowMain(_zone) is not { } main) return;
        Gallery.Children.Add(new TextBlock
        {
            Text = "Efeitos do próprio sensor: ele os roda sozinho, mesmo com este app fechado.",
            Foreground = Res("Dim"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 12),
        });
        foreach (var (mode, name) in HighFlowNextConfig.Modes)
        {
            var card = new RadioButton
            {
                Style = (Style)FindResource("Surface"), GroupName = "flowmodes", IsChecked = main.Mode == mode,
                Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(14, 12, 14, 12),
                Content = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold },
            };
            card.Checked += (_, _) =>
            {
                foreach (var c in FlowControllers(_zone)) c.Mode = mode;
                FlowChanged();
                PaintFlowRows();
                UpdateStageTitle();
                BuildInspector(animate: false);
            };
            Gallery.Children.Add(card);
        }
    }

    void BuildFlowInspector()
    {
        if (FlowMain(_zone) is not { } main || _flowEdit is not { } cfg) return;
        Inspector.Children.Add(new TextBlock { Text = FlowModeName(main.Mode), FontFamily = (FontFamily)FindResource("Display"), FontSize = 24, FontWeight = FontWeights.SemiBold });
        int leds = FlowControllers(_zone).Sum(c => c.Count);
        string where = _zone == FlowRing ? $"Anel do sensor, {leds} LEDs" : $"Saída RGBpx, {leds} LEDs em {FlowControllers(_zone).Count()} grupos (todos recebem o mesmo)";
        Inspector.Children.Add(new TextBlock { Text = where, Foreground = Res("Dim"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        Spacer(16);

        for (int slot = 0; slot < main.Slots; slot++)
        {
            int at = slot;
            var editor = new ColorEditor(at == 0 ? "Fundo" : $"Cor {at}", main.Color(at).ToHex(), Res("Text"), Res("Line"), Res("Bg"), expanded: at < 2) { Margin = new Thickness(0, 0, 0, at < 2 ? 14 : 8) };
            editor.Changed += hex =>
            {
                foreach (var c in FlowControllers(_zone)) c.SetColor(at, Rgb.FromHex(hex));
                FlowChanged();
                PaintFlowRows();
            };
            Inspector.Children.Add(editor);
        }
        Spacer(6);

        AddSlider("Brilho do sensor (tudo)", 0, 255, cfg.Brightness, v => $"{(int)v * 100 / 255}%", v => { cfg.Brightness = (int)v; FlowChanged(); });

        _flowStatusText = new TextBlock { Text = _flowStatus, Foreground = Res("Dim"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        Inspector.Children.Add(_flowStatusText);
        var save = new Button { Style = (Style)FindResource("Primary"), Content = "Salvar no aparelho", Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Grava no sensor, para continuar assim depois de desligar o PC" };
        save.Click += (_, _) => { _flowSoon.Stop(); SendFlow(save: true); };
        Inspector.Children.Add(save);
        var reload = new Button { Style = (Style)FindResource("Flat"), Content = "Ler de novo do sensor", Margin = new Thickness(0, 0, 0, 14),
            ToolTip = "Descarta o que não foi enviado e lê o que o sensor tem agora" };
        reload.Click += (_, _) => ReloadFlow();
        Inspector.Children.Add(reload);
        Inspector.Children.Add(new TextBlock
        {
            Text = "Cada mudança vale na hora, até o sensor ficar sem energia. O aquasuite aberto pode sobrescrever o que for mudado aqui.",
            Foreground = Res("Dim"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
        });
    }

    void ReloadFlow()
    {
        if (_flow is null) return;
        _flowSoon.Stop();
        try { _flowEdit = _flow.Read(); SetFlowStatus($"Lido do sensor às {DateTime.Now:HH:mm:ss}."); }
        catch (Exception ex) { SetFlowStatus($"Erro: {ex.Message}"); }
        PaintFlowRows();
        if (FlowZone) EnterFlowZone();
    }

    void FlowChanged()
    {
        _flowSoon.Stop();
        _flowSoon.Start();
    }

    /// <summary>One write at a time, off the screen's thread; edits made meanwhile go in the next one.</summary>
    void SendFlow(bool save)
    {
        if (_flow is null || _flowEdit is null) return;
        if (_flowWriting) { _flowAgain = true; return; }
        _flowWriting = true;
        var edit = _flowEdit.Clone();
        var device = _flow;
        SetFlowStatus(save ? "Salvando no sensor…" : "Aplicando…");
        Task.Run(() => device.Write(edit, save)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            _flowWriting = false;
            if (t.Exception?.GetBaseException() is { } ex)
            {
                Log.Write($"high flow NEXT: {ex.Message}");
                SetFlowStatus($"Erro: {ex.Message}");
            }
            else SetFlowStatus(save ? $"Salvo no sensor às {DateTime.Now:HH:mm:ss}." : $"Aplicado às {DateTime.Now:HH:mm:ss} (até desligar).");
            if (_flowAgain) { _flowAgain = false; SendFlow(save: false); }
        }));
    }

    void SetFlowStatus(string text)
    {
        _flowStatus = text;
        if (_flowStatusText is not null) _flowStatusText.Text = text;
    }
}
