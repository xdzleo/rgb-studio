using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RgbStudio.Core;

namespace RgbStudio;

/// <summary>
/// The high flow NEXT tab. The sensor runs its own effects, so this edits their settings
/// (effect, colors, brightness) instead of streaming frames. A change applies at once and
/// lasts until the sensor loses power; "Salvar no aparelho" keeps it.
/// </summary>
public partial class MainWindow
{
    HighFlowNext? _flow;
    HighFlowNextConfig? _flowEdit;
    string _flowStatus = "";
    TextBlock? _flowStatusText;
    bool _flowWriting, _flowAgain;
    readonly DispatcherTimer _flowSoon = new() { Interval = TimeSpan.FromMilliseconds(250) };

    void BuildFlowTab()
    {
        if (!_canSave)
        {
            Gallery.Children.Add(Note("Em modo simulado o sensor não é tocado."));
            return;
        }
        if (_flowEdit is null)
        {
            try
            {
                _flow ??= HighFlowNext.Open();
                if (_flow is null) { Gallery.Children.Add(Note("high flow NEXT não encontrado no USB.")); return; }
                _flowEdit = _flow.Read();
                _flowStatus = "Lido do sensor.";
            }
            catch (Exception ex)
            {
                Log.Write($"high flow NEXT: {ex.Message}");
                Gallery.Children.Add(Note($"Não deu para ler o sensor: {ex.Message}"));
                return;
            }
        }
        if (_flowSoon.Tag is null)
        {
            _flowSoon.Tag = true;
            _flowSoon.Tick += (_, _) => { _flowSoon.Stop(); SendFlow(save: false); };
        }

        var c = _flowEdit;
        Gallery.Children.Add(Note("O sensor roda os efeitos sozinho: aqui você escolhe o efeito e as cores de cada grupo de LEDs. Cada mudança vale na hora, até o sensor ficar sem energia. Para manter depois de desligar, use \"Salvar no aparelho\". O aquasuite aberto pode sobrescrever o que for mudado aqui."));
        Gallery.Children.Add(FlowDeviceCard(c));
        foreach (var ctrl in c.All.Where(x => x.Count > 0).OrderByDescending(x => x.Strip))   // the ring first
            Gallery.Children.Add(FlowControllerCard(ctrl));
    }

    TextBlock Note(string text) => new()
    {
        Text = text, Foreground = Res("Dim"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 12),
    };

    Border FlowDeviceCard(HighFlowNextConfig c)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "high flow NEXT", FontSize = 14, FontWeight = FontWeights.SemiBold });
        _flowStatusText = new TextBlock { Text = _flowStatus, Foreground = Res("Dim"), FontSize = 12, Margin = new Thickness(0, 0, 0, 10) };
        body.Children.Add(_flowStatusText);

        var shown = new TextBlock { Text = $"{c.Brightness * 100 / 255}%", Foreground = Res("Dim"), FontSize = 12 };
        var head = new DockPanel();
        DockPanel.SetDock(shown, Dock.Right);
        head.Children.Add(shown);
        head.Children.Add(new TextBlock { Text = "Brilho do sensor", FontSize = 13 });
        var slider = new Slider { Minimum = 0, Maximum = 255, Value = c.Brightness, Margin = new Thickness(0, 2, 0, 10) };
        slider.ValueChanged += (_, e) => { c.Brightness = (int)e.NewValue; shown.Text = $"{c.Brightness * 100 / 255}%"; FlowChanged(); };
        body.Children.Add(head);
        body.Children.Add(slider);

        var actions = new WrapPanel();
        var save = new Button { Style = (Style)FindResource("Primary"), Content = "Salvar no aparelho", Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Grava no sensor, para continuar assim depois de desligar o PC" };
        save.Click += (_, _) => { _flowSoon.Stop(); SendFlow(save: true); };
        var reload = new Button { Style = (Style)FindResource("Flat"), Content = "Ler de novo", ToolTip = "Descarta o que está na tela e lê o que o sensor tem agora" };
        reload.Click += (_, _) => { _flowSoon.Stop(); _flowEdit = null; BuildGallery(); };
        actions.Children.Add(save);
        actions.Children.Add(reload);
        body.Children.Add(actions);
        return ZoneCard(body, false);
    }

    Border FlowControllerCard(HighFlowNextConfig.Controller ctrl)
    {
        var body = new StackPanel();
        string title = ctrl.Strip == 1 ? "Anel do sensor" : $"Saída RGBpx · LEDs {ctrl.FirstLed}–{ctrl.FirstLed + ctrl.Count - 1}";
        body.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = $"Grupo {ctrl.Index + 1} no aquasuite (LED Controller {ctrl.Index + 1}) · {ctrl.Count} LEDs", Foreground = Res("Dim"), FontSize = 12, Margin = new Thickness(0, 0, 0, 10) });

        var modes = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var known = HighFlowNextConfig.Modes.Any(m => m.Mode == ctrl.Mode);
        foreach (var (mode, name) in HighFlowNextConfig.Modes)
        {
            var chip = new RadioButton { Style = (Style)FindResource("Tab"), Content = name, GroupName = $"flow{ctrl.Index}", IsChecked = mode == ctrl.Mode, Margin = new Thickness(0, 0, 4, 4) };
            chip.Checked += (_, _) => { if (ctrl.Mode != mode) { ctrl.Mode = mode; FlowChanged(); } };
            modes.Children.Add(chip);
        }
        if (!known) body.Children.Add(new TextBlock { Text = $"Efeito atual: modo {ctrl.Mode}, sem nome conhecido.", Foreground = Res("Dim"), FontSize = 12 });
        body.Children.Add(modes);

        for (int slot = 0; slot < ctrl.Slots; slot++)
        {
            int at = slot;
            var editor = new ColorEditor(at == 0 ? "Fundo" : $"Cor {at}", ctrl.Color(at).ToHex(), Res("Text"), Res("Line"), Res("Bg"), expanded: false) { Margin = new Thickness(0, 0, 0, 8) };
            editor.Changed += hex => { ctrl.SetColor(at, Rgb.FromHex(hex)); FlowChanged(); };
            body.Children.Add(editor);
        }
        return ZoneCard(body, false);
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
