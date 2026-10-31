using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RgbStudio.Core;

namespace RgbStudio;

/// <summary>
/// Pick a zone, click an effect: it is on. The inspector edits what you clicked, live.
/// Every moving thing here draws from the same effects and frames the LEDs get.
/// </summary>
public partial class MainWindow : Window
{
    const int ThumbLeds = 56, StageLeds = 240;
    const string AllZones = "";

    readonly Engine _engine;
    readonly AppSettings _settings;
    readonly bool _canSave;
    // Render priority: at the default (Background) a busy GPU starves the timer down to 1 update/s.
    readonly DispatcherTimer _tick = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    readonly DispatcherTimer _saveSoon = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    readonly WriteableBitmap _stage = new(StageLeds, 1, 96, 96, PixelFormats.Bgr32, null);
    readonly List<(string PresetId, WriteableBitmap Bitmap, TextBlock Where)> _thumbs = [];
    readonly Dictionary<string, (WriteableBitmap Bitmap, TextBlock Effect)> _zoneStrips = [];
    readonly Dictionary<string, RadioButton> _cards = [];
    string _zone = AllZones;
    string _selected;
    string _tab = "Motion";
    bool _loading = true;
    bool _paused;
    int _tickCount;
    Rgb[] _thumbBuffer = new Rgb[ThumbLeds];

    Brush Res(string key) => (Brush)FindResource(key);

    public MainWindow(Engine engine, AppSettings settings, bool canSave)
    {
        InitializeComponent();
        _engine = engine;
        _settings = settings;
        _canSave = canSave;
        _selected = settings.ActivePreset;

        StageStrip.Source = _stage;
        StageGlow.Source = _stage;
        RenderOptions.SetBitmapScalingMode(StageStrip, BitmapScalingMode.Linear);

        FpsSlider.Value = settings.Fps;
        LimitSlider.Value = settings.PowerLimit;
        LimitText.Text = $"{settings.PowerLimit}%";
        ChainBox.IsChecked = settings.Chain;
        GammaBox.IsChecked = settings.Gamma;
        StartMinBox.IsChecked = settings.StartMinimized;
        try
        {
            AutostartBox.IsChecked = Autostart.IsEnabled();
            // The app may have moved since it was turned on; the task must follow it.
            if (AutostartBox.IsChecked == true && canSave) Autostart.Enable(Environment.ProcessPath!);
        }
        catch { AutostartBox.IsEnabled = false; }

        var current = settings.Resolve(_selected);
        _tab = current.IsBuiltin ? EffectCatalog.Get(current.Effect).Category : "Mine";
        TabFor(_tab).IsChecked = true;

        StatusText.Text = "Conectando… (leva alguns segundos)";
        Pulse(StatusDot);
        BuildGallery();
        BuildInspector(animate: false);
        UpdateStageTitle();

        Gallery.SizeChanged += (_, _) => FitGallery();
        _saveSoon.Tick += (_, _) => { _saveSoon.Stop(); SaveNow(); };
        _tick.Tick += (_, _) => OnFrame();
        // Nothing to draw while hidden in the tray; the LEDs do not depend on this.
        IsVisibleChanged += (_, _) => { if (IsVisible) _tick.Start(); else _tick.Stop(); };
        _engine.Started += () => Dispatcher.BeginInvoke(OnEngineStarted);
        if (_engine.Devices.Count > 0 || _engine.Error is not null) OnEngineStarted();
        _loading = false;
    }

    // ---------------------------------------------------------------- engine state

    /// <summary>Opens straight on counting this zone, once the devices are known.</summary>
    public string? CountLedsOn { get; set; }

    void OnEngineStarted()
    {
        StatusDot.BeginAnimation(OpacityProperty, null);
        StatusDot.Opacity = 1;
        if (_engine.Error is { } error)
        {
            StatusDot.Fill = Res("Accent");
            StatusText.Text = $"Erro: {error}";
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0xE6, 0x8A));
            StatusText.Text = $"{_engine.BackendName} · {_engine.Devices.Count} zonas";
        }
        PauseButton.IsEnabled = true;
        BuildZones();
        if (CountLedsOn is not null) { TabCount.IsChecked = true; StartPendingCount(); }
        else if (_tab == "Count") BuildGallery();
        UpdateStageTitle();
        BuildInspector(animate: false);
    }

    static void Pulse(UIElement e)
    {
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }

    // ---------------------------------------------------------------- zones

    DeviceInfo? ZoneDevice => _engine.Devices.FirstOrDefault(d => d.Name == _zone);

    string ZoneName(DeviceInfo d) => _settings.For(d.Name).Alias is { Length: > 0 } alias ? alias : d.Label;

    void BuildZones()
    {
        ZoneList.Children.Clear();
        _zoneStrips.Clear();
        ZoneList.Children.Add(ZoneRow(AllZones, "Todas as zonas", null));
        foreach (var d in _engine.Devices) ZoneList.Children.Add(ZoneRow(d.Name, ZoneName(d), d));
    }

    FrameworkElement ZoneRow(string key, string title, DeviceInfo? device)
    {
        var effectLine = new TextBlock { Foreground = Res("Dim"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 8) };
        var bitmap = new WriteableBitmap(48, 1, 96, 96, PixelFormats.Bgr32, null);
        var strip = new Image { Source = bitmap, Stretch = Stretch.Fill, Height = 6 };

        var head = new DockPanel();
        if (device is not null)
        {
            var ds = _settings.For(device.Name);
            var on = new CheckBox { IsChecked = ds.Enabled, ToolTip = "Liga ou apaga esta zona" };
            on.Click += (_, _) => { ds.Enabled = on.IsChecked == true; BuildZones(); UpdateStageTitle(); Changed(); };
            DockPanel.SetDock(on, Dock.Right);
            head.Children.Add(on);
        }
        head.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });

        var row = new RadioButton
        {
            Style = (Style)FindResource("Surface"),
            GroupName = "zones",
            IsChecked = key == _zone,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 12),
            Content = new StackPanel
            {
                Children =
                {
                    head,
                    effectLine,
                    new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = strip, Background = Brushes.Black },
                },
            },
        };
        row.Checked += (_, _) =>
        {
            if (_settings.LocatorZone is not null && _settings.LocatorZone != key)
            {
                _settings.LocatorZone = null;
                Changed();
                if (_tab == "Count") BuildGallery();
            }
            _zone = key;
            _selected = device is null ? _settings.ActivePreset : _settings.For(device.Name).Preset ?? _settings.ActivePreset;
            ShowSelectedInGallery();
            BuildInspector();
            UpdateStageTitle();
        };
        _zoneStrips[key] = (bitmap, effectLine);
        // A zone that is off stays reachable, but steps back.
        if (device is not null && !_settings.For(device.Name).Enabled) row.Opacity = 0.55;
        return row;
    }

    string EffectNameOf(string key)
    {
        if (key == AllZones) return _settings.Resolve(null).Name;
        var own = _settings.For(key).Preset;
        return own is null ? $"{_settings.Resolve(null).Name} (igual às outras)" : _settings.Resolve(own).Name;
    }

    void UpdateStageTitle()
    {
        var device = ZoneDevice;
        StageZone.Text = (device is null ? "Todas as zonas" : ZoneName(device)).ToUpperInvariant();
        StageEffect.Text = _settings.Resolve(device is null ? null : _settings.For(device.Name).Preset).Name;
        foreach (var (key, zone) in _zoneStrips) zone.Effect.Text = EffectNameOf(key);
    }

    // ---------------------------------------------------------------- gallery

    RadioButton TabFor(string tab) => tab switch
    {
        "Motion" => TabMotion,
        "Gradientes" => TabGradients,
        "RGB puro" => TabRaw,
        "Orgulho" => TabPride,
        "Ciência" => TabScience,
        "Sinais" => TabSignals,
        "Mine" => TabMine,
        "Count" => TabCount,
        _ => TabClassic,
    };

    void OnTab(object sender, RoutedEventArgs e)
    {
        _tab = sender == TabMotion ? "Motion" : sender == TabGradients ? "Gradientes" : sender == TabRaw ? "RGB puro" : sender == TabPride ? "Orgulho" : sender == TabScience ? "Ciência" : sender == TabSignals ? "Sinais" : sender == TabMine ? "Mine" : sender == TabCount ? "Count" : "Clássicos";
        // Counting lights take over a zone; leaving the tab gives it back.
        if (_tab != "Count" && _settings.LocatorZone is not null) { _settings.LocatorZone = null; Changed(); }
        if (!_loading)
        {
            BuildGallery();
            GalleryScroll.ScrollToTop();
        }
    }

    void ShowSelectedInGallery()
    {
        if (_tab == "Count") return;
        var p = _settings.Resolve(_selected);
        var tab = p.IsBuiltin ? EffectCatalog.Get(p.Effect).Category : "Mine";
        if (tab != _tab) TabFor(tab).IsChecked = true;
        else if (_cards.TryGetValue(_selected, out var card)) card.IsChecked = true;
    }

    /// <summary>The cards share the width in whole columns, so no strip of empty space is left over.</summary>
    void FitGallery()
    {
        double room = Gallery.ActualWidth;
        if (room <= 0) return;
        const double Wanted = 176, Gap = 12;
        double width = _tab == "Count"
            ? room - Gap      // one zone per row
            : Math.Floor(room / Math.Max(1, Math.Floor(room / (Wanted + Gap)))) - Gap;
        foreach (FrameworkElement card in Gallery.Children) card.Width = width;
    }

    void BuildGallery()
    {
        Gallery.Children.Clear();
        _thumbs.Clear();
        _cards.Clear();
        if (_tab == "Count")
        {
            BuildCountTab();
            FitGallery();
            return;
        }

        var presets = _settings.AllPresets().Where(p =>
            _tab == "Mine" ? !p.IsBuiltin : p.IsBuiltin && EffectCatalog.Get(p.Effect).Category == _tab);
        int order = 0;
        foreach (var preset in presets)
        {
            var card = Card(preset);
            Gallery.Children.Add(card);
            // The cards arrive one after another instead of popping in together.
            card.Opacity = 0;
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(Math.Min(order++, 16) * 18) });
        }

        if (_tab == "Mine")
        {
            var create = new Button { Style = (Style)FindResource("Flat"), Width = 176, Height = 116, Margin = new Thickness(0, 0, 12, 12) };
            create.Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "+", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "Criar a partir do atual", FontSize = 13, Foreground = Res("Dim") },
                },
            };
            create.Click += (_, _) => CreateEffect();
            Gallery.Children.Add(create);
        }
        FitGallery();
    }

    FrameworkElement Card(EffectPreset preset)
    {
        var bitmap = new WriteableBitmap(ThumbLeds, 1, 96, 96, PixelFormats.Bgr32, null);
        var strip = new Grid { Height = 52 };
        // The glow is the same pixels stretched and faded at the edges: a blur on every card
        // would be recomputed for all of them on every frame.
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 255, 255), 0.5));
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        var band = new Image { Source = bitmap, Stretch = Stretch.Fill, OpacityMask = fade };
        RenderOptions.SetBitmapScalingMode(band, BitmapScalingMode.Linear);
        strip.Children.Add(band);
        strip.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Fill, Height = 8, VerticalAlignment = VerticalAlignment.Center });

        var where = new TextBlock { FontSize = 10.5, Foreground = Res("Accent"), FontWeight = FontWeights.SemiBold, Height = 15 };
        var card = new RadioButton
        {
            Style = (Style)FindResource("Surface"),
            GroupName = "effects",
            Width = 176,
            Height = 116,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(10, 8, 10, 8),
            IsChecked = preset.Id == _selected,
            ToolTip = EffectCatalog.Get(preset.Effect).Blurb is { Length: > 0 } b ? b : preset.Name,
            Content = new StackPanel
            {
                Children =
                {
                    strip,
                    new TextBlock { Text = preset.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis },
                    where,
                },
            },
        };
        // One click puts it on the selected zone. Checked alone would not fire on re-clicks.
        card.PreviewMouseLeftButtonUp += (_, _) => Activate(preset.Id);
        card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) Activate(preset.Id); };
        _thumbs.Add((preset.Id, bitmap, where));
        _cards[preset.Id] = card;
        return card;
    }

    void Activate(string presetId)
    {
        _settings.Place(ZoneDevice?.Name, presetId);
        _selected = presetId;
        Changed();
        UpdateStageTitle();
        BuildInspector();
    }

    void CreateEffect()
    {
        var preset = Presets.NewFrom(_settings.Resolve(_selected), $"Meu efeito {_settings.MyEffects.Count + 1}");
        _settings.MyEffects.Add(preset);
        _selected = preset.Id;
        _settings.Place(ZoneDevice?.Name, preset.Id);
        if (_tab == "Mine") BuildGallery(); else TabMine.IsChecked = true;
        Changed();
        UpdateStageTitle();
        BuildInspector();
        _nameBox?.Focus();
        _nameBox?.SelectAll();
    }

    // ---------------------------------------------------------------- inspector

    TextBox? _nameBox;
    Button? _resetButton;

    void BuildInspector(bool animate = true)
    {
        Inspector.Children.Clear();
        _nameBox = null;
        _resetButton = null;
        var preset = _settings.Resolve(_selected).Clone();
        var effect = EffectCatalog.Get(preset.Effect);

        void Commit()
        {
            _settings.Put(preset.Clone());
            if (_resetButton is not null) _resetButton.Visibility = Visibility.Visible;
            Changed();
        }

        // Which effect, in words.
        if (preset.IsBuiltin)
        {
            Inspector.Children.Add(new TextBlock { Text = preset.Name, FontFamily = (FontFamily)FindResource("Display"), FontSize = 24, FontWeight = FontWeights.SemiBold });
        }
        else
        {
            _nameBox = new TextBox { Text = preset.Name, FontFamily = (FontFamily)FindResource("Display"), FontSize = 20, FontWeight = FontWeights.SemiBold, Background = Brushes.Transparent, Foreground = Res("Text"), CaretBrush = Res("Text"), BorderBrush = Res("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 2, 0, 4) };
            var box = _nameBox;
            box.LostFocus += (_, _) =>
            {
                var t = box.Text.Trim();
                if (t.Length > 0 && t != preset.Name) { preset.Name = t; Commit(); BuildGallery(); UpdateStageTitle(); }
            };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            Inspector.Children.Add(box);
            Inspector.Children.Add(new TextBlock { Text = $"Baseado em {effect.Name}", Foreground = Res("Dim"), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        }
        if (effect.Blurb.Length > 0)
            Inspector.Children.Add(new TextBlock { Text = effect.Blurb, Foreground = Res("Dim"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        Spacer(16);

        // Colors.
        if (effect.UsesColors) BuildColorList(preset, Commit);
        else
        {
            if (effect.UsesColor1) AddColor(effect.UsesColor2 ? "Cor principal" : "Cor", preset.Color1, v => { preset.Color1 = v; Commit(); });
            if (effect.UsesColor2) AddColor("Cor de apoio", preset.Color2, v => { preset.Color2 = v; Commit(); });
        }

        // Motion.
        if (effect.UsesSpeed) AddSlider("Velocidade", 0.1, 5, preset.Speed, v => $"{v:0.0}×", v => { preset.Speed = Math.Round(v, 2); Commit(); });
        AddSlider("Brilho", 0, 100, preset.Brightness, v => $"{v:0}%", v => { preset.Brightness = (int)v; Commit(); });
        if (effect.UsesDirection) AddSwitch("Inverter direção", preset.Reverse, v => { preset.Reverse = v; Commit(); });
        if (effect.UsesColors) AddSwitch("Faixas andando pela fita", preset.Bands, v => { preset.Bands = v; Commit(); });

        // Keep it, undo it.
        Spacer(8);
        if (preset.IsBuiltin)
        {
            AddButton("Salvar como meu efeito", "Primary", CreateEffect);
            _resetButton = AddButton("Restaurar padrão", "Flat", () =>
            {
                _settings.BuiltinTweaks.Remove(preset.Id);
                Changed();
                BuildInspector();
            });
            _resetButton.Visibility = _settings.BuiltinTweaks.ContainsKey(preset.Id) ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            AddButton("Duplicar", "Flat", CreateEffect);
            AddButton("Excluir", "Flat", () =>
            {
                if (MessageBox.Show(this, $"Excluir \"{preset.Name}\"? As zonas com ele voltam ao efeito das outras.", "Excluir efeito", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                _settings.Delete(preset.Id);
                _selected = _settings.ActivePreset;
                BuildGallery();
                Changed();
                UpdateStageTitle();
                BuildInspector();
            });
        }

        // The zone itself: its name and, for a strip, how long it really is.
        if (ZoneDevice is { } here)
        {
            Spacer(14);
            Inspector.Children.Add(new Border { Height = 1, Background = Res("Line"), Margin = new Thickness(0, 0, 0, 14) });
            var zoneSettings = _settings.For(here.Name);
            Inspector.Children.Add(new TextBlock { Text = "Nome desta zona", FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
            var alias = new TextBox { Text = ZoneName(here), FontSize = 13, Background = Res("Bg"), Foreground = Res("Text"), CaretBrush = Res("Text"), BorderBrush = Res("Line"), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 12) };
            alias.LostFocus += (_, _) =>
            {
                var t = alias.Text.Trim();
                zoneSettings.Alias = t.Length == 0 || t == here.Label ? null : t;
                BuildZones();
                UpdateStageTitle();
                Changed();
            };
            alias.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            Inspector.Children.Add(alias);
            Inspector.Children.Add(new TextBlock { Text = "Calibração de cor desta zona", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 2) });
            Inspector.Children.Add(new TextBlock { Text = "Nenhum canal passa do máximo: para equilibrar, baixe os que estão fortes. Se o azul parece fraco, baixe um pouco o verde e o vermelho.", Foreground = Res("Dim"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            AddSlider("Vermelho", 0, 100, zoneSettings.RedGain, v => $"{v:0}%", v => { zoneSettings.RedGain = (int)v; Changed(); });
            AddSlider("Verde", 0, 100, zoneSettings.GreenGain, v => $"{v:0}%", v => { zoneSettings.GreenGain = (int)v; Changed(); });
            AddSlider("Azul", 0, 100, zoneSettings.BlueGain, v => $"{v:0}%", v => { zoneSettings.BlueGain = (int)v; Changed(); });
            AddButton("Restaurar calibração", "Flat", () =>
            {
                zoneSettings.RedGain = zoneSettings.GreenGain = zoneSettings.BlueGain = 100;
                Changed();
                BuildInspector(animate: false);
            });
            Spacer(6);
        }
        if (ZoneDevice is { IsAddressableStrip: true } strip)
        {
            var ds = _settings.For(strip.Name);
            AddSlider($"LEDs em {ZoneName(strip)}", 1, strip.MaxLeds, ds.LedCount > 0 ? ds.LedCount : strip.MaxLeds, v => $"{v:0}", v =>
            {
                int n = (int)v;
                ds.LedCount = n == strip.MaxLeds ? 0 : n;
                Changed();
            });
            Inspector.Children.Add(new TextBlock { Text = "Coloque a quantidade real: o efeito se ajusta ao tamanho e o app roda mais rápido.", Foreground = Res("Dim"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            AddButton("Contar os LEDs desta zona", "Flat", () => { CountLedsOn = strip.Name; TabCount.IsChecked = true; StartPendingCount(); });
            Spacer(6);
            AddSlider("Corrente máxima por conector", 500, 5000, _settings.HeaderMilliamps > 0 ? _settings.HeaderMilliamps : 5000, v => $"{v / 1000:0.0} A", v =>
            {
                _settings.HeaderMilliamps = (int)Math.Round(v / 100) * 100;
                Changed();
            });
            Inspector.Children.Add(new TextBlock { Text = "Estimativa (pior caso de 60 mA por LED branco), não medição. Confira o limite real do conector no manual da placa.", Foreground = Res("Dim"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }

        if (animate)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Inspector.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            InspectorMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        }
    }

    void StartPendingCount()
    {
        if (CountLedsOn is { } zone && _engine.Devices.Any(d => d.Name == zone))
        {
            CountLedsOn = null;
            StartCount(zone, 1);
        }
    }

    void StartCount(string zone, int led)
    {
        _settings.LocatorZone = zone;
        _settings.LocatorLed = led;
        if (_zone != zone)
        {
            _zone = zone;
            _selected = _settings.For(zone).Preset ?? _settings.ActivePreset;
            BuildZones();
            UpdateStageTitle();
            BuildInspector(animate: false);
        }
        Changed();
        BuildGallery();
    }

    void StopCount()
    {
        _settings.LocatorZone = null;
        Changed();
        BuildGallery();
    }

    /// <summary>
    /// LEDs do not report how many they are, so each zone is counted by eye: green up to a
    /// number, that one white. The last number where the white still shows is the length.
    /// </summary>
    void BuildCountTab()
    {
        if (_engine.Devices.Count == 0)
        {
            Gallery.Children.Add(new TextBlock { Text = "Conectando às zonas…", Foreground = Res("Dim") });
            return;
        }
        Gallery.Children.Add(new TextBlock
        {
            Text = "Os LEDs não informam quantos são: quem conta é o seu olho. Abra uma zona e avance o número até o LED branco sumir. O último número em que ele ainda aparece é o total.",
            Foreground = Res("Dim"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 12),
        });
        foreach (var d in _engine.Devices.OrderByDescending(d => d.IsAddressableStrip))   // the ones to count first
            Gallery.Children.Add(d.IsAddressableStrip ? CountCard(d) : FixedCard(d));
    }

    Border ZoneCard(UIElement content, bool active) => new()
    {
        Background = Res("Raised"),
        BorderBrush = active ? (Brush)FindResource("AccentGrad") : Res("Line"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16, 12, 16, 12),
        Margin = new Thickness(0, 0, 12, 10),
        Child = content,
    };

    /// <summary>Soldered to the board: the controller or the SDK says how many, nothing to count.</summary>
    Border FixedCard(DeviceInfo d)
    {
        var row = new DockPanel();
        var total = new TextBlock { Text = $"{d.MaxLeds} LEDs", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(total, Dock.Right);
        row.Children.Add(total);
        row.Children.Add(new TextBlock { Text = ZoneName(d), FontSize = 14, FontWeight = FontWeights.SemiBold });
        return ZoneCard(new StackPanel { Children = { row, new TextBlock { Text = "Informado pelo próprio dispositivo", Foreground = Res("Dim"), FontSize = 12 } } }, false);
    }

    Border CountCard(DeviceInfo d)
    {
        var ds = _settings.For(d.Name);
        bool counting = _settings.LocatorZone == d.Name;
        string saved = !ds.Enabled ? "Marcada como vazia ou desligada"
            : ds.LedCount > 0 ? $"Salvo: {ds.LedCount} LEDs"
            : $"Ainda não contada (usando o máximo, {d.MaxLeds})";

        var head = new DockPanel();
        if (!counting)
        {
            var open = new Button { Style = (Style)FindResource("Primary"), Content = "Contar" };
            open.Click += (_, _) => StartCount(d.Name, 1);
            DockPanel.SetDock(open, Dock.Right);
            head.Children.Add(open);
        }
        head.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = ZoneName(d), FontSize = 14, FontWeight = FontWeights.SemiBold },
                new TextBlock { Text = saved, Foreground = Res("Dim"), FontSize = 12 },
            },
        });
        var body = new StackPanel { Children = { head } };
        if (!counting) return ZoneCard(body, false);

        int led = _settings.LocatorLed;
        body.Children.Add(new TextBlock { Text = $"LED {led}", FontFamily = (FontFamily)FindResource("Display"), FontSize = 34, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 8) });

        var flat = (Style)FindResource("Flat");
        Button Step(string text, int to, Style style)
        {
            var b = new Button { Style = style, Content = text, MinWidth = 56, Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) => StartCount(d.Name, Math.Clamp(to, 1, d.MaxLeds));
            return b;
        }
        var steps = new WrapPanel();
        steps.Children.Add(Step("− 1", led - 1, flat));
        steps.Children.Add(Step("+ 1", led + 1, (Style)FindResource("Primary")));
        steps.Children.Add(Step("+ 10", led + 10, flat));
        foreach (int n in new[] { 8, 11, 12, 16, 22, 24, 36, 48 }) steps.Children.Add(Step(n.ToString(), n, flat));
        body.Children.Add(steps);

        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Action(string text, Style style, Action click)
        {
            var b = new Button { Style = style, Content = text, Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) => click();
            actions.Children.Add(b);
        }
        Action($"Tem {led} LEDs: salvar", (Style)FindResource("Primary"), () =>
        {
            ds.LedCount = led >= d.MaxLeds ? 0 : led;
            ds.Enabled = true;
            StopCount();
            BuildZones();
            UpdateStageTitle();
        });
        Action("Nada acende: marcar vazia", flat, () =>
        {
            ds.Enabled = false;
            StopCount();
            BuildZones();
            UpdateStageTitle();
        });
        Action("Cancelar", flat, StopCount);
        body.Children.Add(actions);
        return ZoneCard(body, true);
    }

    void Spacer(double height) => Inspector.Children.Add(new Border { Height = height });

    void AddColor(string label, string hex, Action<string> set)
    {
        var editor = new ColorEditor(label, hex, Res("Text"), Res("Line"), Res("Bg")) { Margin = new Thickness(0, 0, 0, 14) };
        editor.Changed += set;
        Inspector.Children.Add(editor);
    }

    void BuildColorList(EffectPreset preset, Action commit)
    {
        for (int i = 0; i < preset.Colors.Count; i++)
        {
            int at = i;
            var editor = new ColorEditor($"Cor {at + 1}", preset.Colors[at], Res("Text"), Res("Line"), Res("Bg"), expanded: false) { Margin = new Thickness(0, 0, 0, 8) };
            editor.Changed += v => { preset.Colors[at] = v; commit(); };
            if (preset.Colors.Count > 1)
                editor.AddRemove((Style)FindResource("Flat"), () => { preset.Colors.RemoveAt(at); commit(); BuildInspector(animate: false); });
            Inspector.Children.Add(editor);
        }
        var add = new Button { Style = (Style)FindResource("Flat"), Content = "+ Adicionar cor", Margin = new Thickness(0, 0, 0, 14) };
        add.Click += (_, _) => { preset.Colors.Add(preset.Colors.LastOrDefault() ?? "#FFFFFF"); commit(); BuildInspector(animate: false); };
        Inspector.Children.Add(add);
    }

    void AddSlider(string label, double min, double max, double value, Func<double, string> format, Action<double> set)
    {
        var shown = new TextBlock { Text = format(value), Foreground = Res("Dim"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel();
        DockPanel.SetDock(shown, Dock.Right);
        head.Children.Add(shown);
        head.Children.Add(new TextBlock { Text = label, FontSize = 13 });
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Margin = new Thickness(0, 2, 0, 10) };
        slider.ValueChanged += (_, e) => { shown.Text = format(e.NewValue); set(e.NewValue); };
        Inspector.Children.Add(head);
        Inspector.Children.Add(slider);
    }

    void AddSwitch(string label, bool value, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = value, Content = new TextBlock { Text = label, FontSize = 13 }, Margin = new Thickness(0, 2, 0, 12) };
        box.Click += (_, _) => set(box.IsChecked == true);
        Inspector.Children.Add(box);
    }

    Button AddButton(string text, string style, Action click)
    {
        var b = new Button { Style = (Style)FindResource(style), Content = text, Margin = new Thickness(0, 0, 0, 8) };
        b.Click += (_, _) => click();
        Inspector.Children.Add(b);
        return b;
    }

    // ---------------------------------------------------------------- drawing

    void OnFrame()
    {
        _tickCount++;
        var frame = _engine.Frame;
        // The frames carry the brightness limit; the screen shows the effect as designed.
        double undo = 100.0 / Math.Max(5, _settings.PowerLimit);

        var all = new List<Rgb>();
        var everyZone = new List<Rgb>();
        var zone = ZoneDevice;
        foreach (var d in _engine.Devices)
        {
            if (!frame.TryGetValue(d.Index, out var colors)) continue;
            var ds = _settings.For(d.Name);
            int length = ds.LedCount > 0 ? Math.Min(ds.LedCount, colors.Length) : colors.Length;
            if (_zoneStrips.TryGetValue(d.Name, out var row)) Blit(row.Bitmap, colors, length, undo);
            if (ds.Enabled)
                for (int k = 0; k < length; k++) everyZone.Add(colors[k]);
            if (zone is null ? ds.Enabled : d.Name == zone.Name)
                for (int k = 0; k < length; k++) all.Add(colors[k]);
        }
        var stageColors = all.ToArray();
        Blit(_stage, stageColors, stageColors.Length, undo);
        if (_zoneStrips.TryGetValue(AllZones, out var allRow)) Blit(allRow.Bitmap, everyZone.ToArray(), everyZone.Count, undo);
        SetAmbient(stageColors, undo);

        // Effect previews: every other tick is plenty for something this small.
        if (_tickCount % 2 == 0)
        {
            double t = _clock.Elapsed.TotalSeconds;
            foreach (var (id, bitmap, _) in _thumbs)
            {
                var p = _settings.Resolve(id);
                var pars = p.ToParams();
                pars.Brightness = 100;
                EffectCatalog.Get(p.Effect).Render(_thumbBuffer, t, pars, 0);
                Blit(bitmap, _thumbBuffer, ThumbLeds, 1);
            }
        }

        if (_tickCount % 15 == 0)
        {
            FpsText.Text = _paused || _engine.Error is not null ? "" : $"{_engine.ActualFps:0} reais";
            foreach (var (id, _, where) in _thumbs) where.Text = WhereUsed(id);
        }
    }

    string WhereUsed(string id)
    {
        int own = _settings.Devices.Values.Count(d => d.Preset == id);
        if (id == _settings.ActivePreset) return own > 0 ? $"ATIVO + {own}" : "ATIVO";
        return own switch { 0 => "", 1 => "em 1 zona", _ => $"em {own} zonas" };
    }

    static void Blit(WriteableBitmap bmp, Rgb[] colors, int length, double gain)
    {
        int w = bmp.PixelWidth;
        var px = new int[w];
        if (length > 0)
        {
            for (int x = 0; x < w; x++)
            {
                var c = colors[Math.Min(length - 1, x * length / w)];
                int r = Math.Min(255, (int)(c.R * gain)), g = Math.Min(255, (int)(c.G * gain)), b = Math.Min(255, (int)(c.B * gain));
                px[x] = r << 16 | g << 8 | b;
            }
        }
        bmp.WritePixels(new Int32Rect(0, 0, w, 1), px, w * 4, 0);
    }

    Color _ambient = Color.FromRgb(40, 20, 60), _ambient2 = Color.FromRgb(30, 20, 60);

    /// <summary>The room glow: the two halves of the strip, averaged, drifting toward the real color.</summary>
    void SetAmbient(Rgb[] colors, double gain)
    {
        if (colors.Length == 0) return;
        _ambient = Drift(_ambient, Average(colors, 0, colors.Length / 2 + 1), gain);
        _ambient2 = Drift(_ambient2, Average(colors, colors.Length / 2, colors.Length), gain);
        AmbientA.Color = Color.FromArgb(120, _ambient.R, _ambient.G, _ambient.B);
        AmbientB.Color = Color.FromArgb(110, _ambient2.R, _ambient2.G, _ambient2.B);
    }

    static (double R, double G, double B) Average(Rgb[] colors, int from, int to)
    {
        double r = 0, g = 0, b = 0;
        int n = Math.Max(1, to - from);
        for (int i = from; i < to && i < colors.Length; i++) { r += colors[i].R; g += colors[i].G; b += colors[i].B; }
        return (r / n, g / n, b / n);
    }

    static Color Drift(Color from, (double R, double G, double B) to, double gain)
    {
        const double Rate = 0.08;
        static byte Step(byte a, double b) => (byte)Math.Clamp(a + (b - a) * Rate, 0, 255);
        return Color.FromRgb(Step(from.R, to.R * gain), Step(from.G, to.G * gain), Step(from.B, to.B * gain));
    }

    /// <summary>
    /// Walks the same paths as the clicks (for `--smoke`, as nothing here can click a real
    /// window) and logs how fast the screen keeps up. Errors reach the log via the app handler.
    /// </summary>
    public async Task RunSmoke(Action<string> shot)
    {
        async Task Settle(int ms = 120) => await Task.Delay(ms);
        while (_engine.Devices.Count == 0 && _engine.Error is null) await Settle();
        await Settle(400);

        int firstTick = _tickCount;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Settle(3000);
        Log.Write($"smoke: tela a {(_tickCount - firstTick) / watch.Elapsed.TotalSeconds:0.0} atualizações/s");
        foreach (var tab in new[] { TabGradients, TabRaw, TabPride, TabMotion, TabScience, TabSignals, TabClassic })
        {
            tab.IsChecked = true;
            await Settle(800);
            firstTick = _tickCount;
            watch.Restart();
            await Settle(2500);
            Log.Write($"smoke: aba {tab.Content}, {Gallery.Children.Count} efeitos: {(_tickCount - firstTick) / watch.Elapsed.TotalSeconds:0.0} atualizações/s");
        }

        var zones = new[] { AllZones }.Concat(_engine.Devices.Select(d => d.Name)).ToList();
        foreach (var preset in _settings.AllPresets().ToList())
        {
            _selected = preset.Id;
            ShowSelectedInGallery();
            Activate(preset.Id);
            await Settle(40);
        }
        foreach (var zone in zones)
        {
            _zone = zone;
            _selected = ZoneDevice is { } d ? _settings.For(d.Name).Preset ?? _settings.ActivePreset : _settings.ActivePreset;
            ShowSelectedInGallery();
            BuildInspector();
            UpdateStageTitle();
            Activate(Presets.BuiltinId("fire"));
            await Settle(40);
        }
        var strip = _engine.Devices.First(d => d.IsAddressableStrip);
        _zone = strip.Name;
        Activate(Presets.BuiltinId("aurora"));
        ShowSelectedInGallery();
        BuildZones();
        UpdateStageTitle();
        BuildInspector();
        await Settle(1500);
        shot("aurora-zona");

        _selected = Presets.BuiltinId("sequence");
        Activate(_selected);
        CreateEffect();
        var mine = _selected;
        var edited = _settings.Resolve(mine).Clone();
        edited.Colors.Add("#00FF88");
        edited.Name = "Teste";
        _settings.Put(edited);
        Changed();
        BuildGallery();
        BuildInspector();
        await Settle(1200);
        shot("meus-efeitos");

        TabCount.IsChecked = true;
        StartCount(strip.Name, 12);
        await Settle(800);
        shot("contar-leds");
        StopCount();

        _settings.Delete(mine);
        _selected = _settings.ActivePreset;
        BuildGallery();
        BuildInspector();
        Log.Write($"smoke: excluído; zona {strip.Name} voltou para {_settings.Resolve(_settings.For(strip.Name).Preset).Name}");

        OnPause(this, new RoutedEventArgs());
        await Settle(400);
        OnPause(this, new RoutedEventArgs());
        while (!PauseButton.IsEnabled) await Settle();
        Log.Write($"smoke: pausar e retomar ok, erro do motor: {_engine.Error ?? "nenhum"}");

        _zone = AllZones;
        _selected = Presets.BuiltinId("plasma");
        TabMotion.IsChecked = true;
        Activate(_selected);
        BuildZones();
        UpdateStageTitle();
        await Settle(4000);
        Log.Write($"smoke: {_engine.ActualFps:0.0} quadros/s do motor, texto do contador: \"{FpsText.Text}\"");
        shot("motion");

        TabScience.IsChecked = true;
        Activate(Presets.BuiltinId("fractal"));
        ShowSelectedInGallery();
        await Settle(2500);
        shot("ciencia");

        TabGradients.IsChecked = true;
        Activate(Presets.BuiltinId("grad-cyberpunk"));
        ShowSelectedInGallery();
        await Settle(2500);
        shot("gradientes");

        TabPride.IsChecked = true;
        Activate(Presets.BuiltinId("grad-pride"));
        ShowSelectedInGallery();
        await Settle(2500);
        shot("orgulho");

        TabRaw.IsChecked = true;
        Activate(Presets.BuiltinId("grad-rgb"));
        ShowSelectedInGallery();
        await Settle(2500);
        shot("rgb-puro");

        TabSignals.IsChecked = true;
        Activate(Presets.BuiltinId("police"));
        ShowSelectedInGallery();
        await Settle(2500);
        shot("sinais");
    }

    // ---------------------------------------------------------------- settings

    void OnChanged(object sender, RoutedEventArgs e) => Changed();
    void OnSlider(object sender, RoutedPropertyChangedEventArgs<double> e) => Changed();

    void Changed()
    {
        if (_loading) return;
        _settings.Fps = (int)FpsSlider.Value;
        _settings.PowerLimit = (int)LimitSlider.Value;
        LimitText.Text = $"{_settings.PowerLimit}%";
        _settings.Chain = ChainBox.IsChecked == true;
        _settings.Gamma = GammaBox.IsChecked == true;
        _settings.StartMinimized = StartMinBox.IsChecked == true;
        _engine.Settings = _settings.Clone();   // the engine draws from its own copy
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    public void SaveNow()
    {
        if (!_canSave) return;
        try { _settings.Save(); }
        catch (Exception ex) { StatusText.Text = $"Não salvou: {ex.Message}"; }
    }

    void OnAutostart(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AutostartBox.IsChecked == true) Autostart.Enable(Environment.ProcessPath!);
            else Autostart.Disable();
        }
        catch (Exception ex)
        {
            AutostartBox.IsChecked = !AutostartBox.IsChecked;
            MessageBox.Show(this, ex.Message, "Iniciar com o Windows");
        }
    }

    void OnPause(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        if (_paused)
        {
            _engine.Stop();
            PauseButton.Content = "Retomar";
            StatusDot.Fill = Res("Dim");
            StatusText.Text = "Pausado: o Armoury Crate controla as luzes";
        }
        else
        {
            // Reconnecting takes seconds; no second click until it is done.
            PauseButton.Content = "Pausar";
            PauseButton.IsEnabled = false;
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20));
            StatusText.Text = "Conectando…";
            Pulse(StatusDot);
            _engine.Start();
        }
    }

    void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void OnMaximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void OnClose(object sender, RoutedEventArgs e) => Close();
    void OnQuit(object sender, RoutedEventArgs e) => ((App)Application.Current).Quit();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window keeps the lights running from the tray.
        e.Cancel = true;
        SaveNow();
        Hide();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        // A borderless maximized window would overflow the screen by its resize border.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        if (WindowState == WindowState.Minimized) Hide();
        base.OnStateChanged(e);
    }
}
