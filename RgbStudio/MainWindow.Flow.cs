using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RgbStudio.Core;

namespace RgbStudio;

/// <summary>
/// The high flow NEXT's ring is a zone like any other and takes every effect of this app.
/// It also has effects of its own, which run even with this app closed: those are in the
/// "Do sensor" tab, and picking one hands the ring back to the sensor.
/// </summary>
public partial class MainWindow
{
    /// <summary>The sensor shared with the engine; null in a simulation.</summary>
    public HighFlowNextLink? Sensor { get; set; }

    string _flowStatus = "";
    TextBlock? _flowStatusText;
    bool _flowWriting, _flowAgain;
    DispatcherTimer? _flowSoon;

    bool RingZone => _zone == HighFlowNextBackend.DeviceName;

    /// <summary>The ring is running the sensor's own effect, not this app's.</summary>
    bool SensorOn => _settings.Devices.TryGetValue(HighFlowNextBackend.DeviceName, out var d) && d.SensorEffect;

    /// <summary>The ring's groups in the sensor's own lighting.</summary>
    IEnumerable<HighFlowNextConfig.Controller> RingControllers() =>
        Sensor?.Native?.All.Where(c => c.Count > 0 && c.Strip == 1) ?? [];

    HighFlowNextConfig.Controller? RingMain() => RingControllers().FirstOrDefault();

    void UpdateSensorTab()
    {
        bool show = RingZone && RingMain() is not null;
        TabSensor.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show && _tab == "Sensor") TabMotion.IsChecked = true;
    }

    static string FlowModeName(byte mode) =>
        HighFlowNextConfig.Modes.FirstOrDefault(m => m.Mode == mode) is { Name: { } n } ? n : $"Modo {mode}";

    string SensorEffectName() => RingMain() is { } c ? FlowModeName(c.Mode) : "";

    /// <summary>The sensor draws its own effect; the screen shows its colors.</summary>
    Rgb[] SensorPreview()
    {
        if (RingMain() is not { } c) return [Rgb.Black];
        var bg = c.Color(0); var fg = c.Color(1);
        return c.Mode is 3 or 19 ? Enumerable.Range(0, 24).Select(i => Rgb.FromHsv(i * 15, 1, 1)).ToArray()
            : [bg, bg, fg, fg, bg, bg, bg, bg, fg, fg, bg, bg];
    }

    void BuildFlowGallery()
    {
        if (RingMain() is not { } main) return;
        Gallery.Children.Add(new TextBlock
        {
            Text = "Efeitos do próprio sensor: ele os roda sozinho, mesmo com este app fechado. Para o anel voltar a seguir o app, escolha um efeito em qualquer outra aba.",
            Foreground = Res("Dim"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 12),
        });
        foreach (var (mode, name) in HighFlowNextConfig.Modes)
        {
            var card = new RadioButton
            {
                Style = (Style)FindResource("Surface"), GroupName = "flowmodes", IsChecked = SensorOn && main.Mode == mode,
                Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(14, 12, 14, 12),
                Content = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold },
            };
            card.Checked += (_, _) =>
            {
                foreach (var c in RingControllers()) c.Mode = mode;
                _settings.For(HighFlowNextBackend.DeviceName).SensorEffect = true;
                Changed();
                SendFlow(save: false);
                UpdateStageTitle();
                BuildInspector(animate: false);
            };
            Gallery.Children.Add(card);
        }
    }

    void BuildFlowInspector()
    {
        if (RingMain() is not { } main || Sensor?.Native is not { } cfg) return;
        Inspector.Children.Add(new TextBlock { Text = SensorOn ? FlowModeName(main.Mode) : "Efeitos do sensor", FontFamily = (FontFamily)FindResource("Display"), FontSize = 24, FontWeight = FontWeights.SemiBold });
        Inspector.Children.Add(new TextBlock
        {
            Text = SensorOn ? $"O sensor está rodando o efeito dele nos {RingControllers().Sum(c => c.Count)} LEDs do anel."
                : "O anel está seguindo o app. Escolha um efeito ao lado para entregá-lo ao sensor.",
            Foreground = Res("Dim"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        });
        Spacer(16);

        for (int slot = 0; slot < main.Slots; slot++)
        {
            int at = slot;
            var editor = new ColorEditor(at == 0 ? "Fundo" : $"Cor {at}", main.Color(at).ToHex(), Res("Text"), Res("Line"), Res("Bg"), expanded: at < 2) { Margin = new Thickness(0, 0, 0, at < 2 ? 14 : 8) };
            editor.Changed += hex =>
            {
                foreach (var c in RingControllers()) c.SetColor(at, Rgb.FromHex(hex));
                FlowChanged();
            };
            Inspector.Children.Add(editor);
        }
        Spacer(6);
        AddSlider("Brilho do sensor", 0, 255, cfg.Brightness, v => $"{(int)v * 100 / 255}%", v => { cfg.Brightness = (int)v; FlowChanged(); });

        _flowStatusText = new TextBlock { Text = _flowStatus, Foreground = Res("Dim"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        Inspector.Children.Add(_flowStatusText);
        var save = new Button { Style = (Style)FindResource("Primary"), Content = "Salvar no aparelho", Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Grava este efeito no sensor: é o que ele roda ao ligar e com o app fechado" };
        save.Click += (_, _) => { _flowSoon?.Stop(); SendFlow(save: true); };
        Inspector.Children.Add(save);
        if (SensorOn)
        {
            var reload = new Button { Style = (Style)FindResource("Flat"), Content = "Ler de novo do sensor", Margin = new Thickness(0, 0, 0, 14),
                ToolTip = "Descarta o que não foi enviado e lê o que o sensor tem agora" };
            reload.Click += (_, _) => ReloadFlow();
            Inspector.Children.Add(reload);
        }
        Inspector.Children.Add(new TextBlock
        {
            Text = "Mudanças valem na hora, até o sensor ficar sem energia; o que fica gravado é o que você salvar. O aquasuite aberto pode sobrescrever o que for mudado aqui.",
            Foreground = Res("Dim"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
        });
    }

    void ReloadFlow()
    {
        if (Sensor?.Device is not { } dev) return;
        _flowSoon?.Stop();
        try { Sensor.Native = dev.Read(); SetFlowStatus($"Lido do sensor às {DateTime.Now:HH:mm:ss}."); }
        catch (Exception ex) { SetFlowStatus($"Erro: {ex.Message}"); }
        UpdateStageTitle();
        BuildGallery();
        BuildInspector(animate: false);
    }

    void FlowChanged()
    {
        if (_flowSoon is null)
        {
            _flowSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _flowSoon.Tick += (_, _) => { _flowSoon.Stop(); SendFlow(save: false); };
        }
        _flowSoon.Stop();
        _flowSoon.Start();
    }

    /// <summary>
    /// One write at a time, off the screen's thread; edits made meanwhile go in the next one.
    /// While the ring follows the app, edits wait in memory (the app's frames would cover them)
    /// unless they are being saved.
    /// </summary>
    void SendFlow(bool save)
    {
        if (Sensor?.Device is not { } device || Sensor.Native is null) return;
        if (!save && !SensorOn) { SetFlowStatus("Guardado: vale quando o anel usar um efeito do sensor."); return; }
        if (_flowWriting) { _flowAgain = true; return; }
        _flowWriting = true;
        var edit = Sensor.Native.Clone();
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
