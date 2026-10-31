using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RgbStudio.Core;

namespace RgbStudio;

/// <summary>
/// A color picked in place: hue, saturation and brightness on rails that show what they
/// do, plus a hex field. Replaces the Windows dialog, which stopped the app to ask.
/// </summary>
public sealed class ColorEditor : StackPanel
{
    readonly Slider _hue = new() { Minimum = 0, Maximum = 360, Foreground = Brushes.Transparent };
    readonly Slider _sat = new() { Minimum = 0, Maximum = 1, Foreground = Brushes.Transparent };
    readonly Slider _val = new() { Minimum = 0, Maximum = 1, Foreground = Brushes.Transparent };
    readonly Border _swatch = new() { Width = 30, Height = 30, CornerRadius = new CornerRadius(8) };
    readonly TextBox _hex = new() { Width = 86, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 3, 6, 3), BorderThickness = new Thickness(1) };
    readonly DockPanel _head = new();
    readonly StackPanel _rails = new();
    bool _updating;

    public event Action<string>? Changed;

    /// <summary>A small × at the end of the row.</summary>
    public void AddRemove(Style style, Action remove)
    {
        var b = new Button { Style = style, Content = "×", Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remover esta cor" };
        b.Click += (_, _) => remove();
        DockPanel.SetDock(b, Dock.Right);
        _head.Children.Insert(0, b);
    }

    public ColorEditor(string label, string hex, Brush text, Brush line, Brush field, bool expanded = true)
    {
        _hex.Foreground = text;
        _hex.CaretBrush = text;
        _hex.BorderBrush = line;
        _hex.Background = field;

        _head.Margin = new Thickness(0, 0, 0, 6);
        var head = _head;
        DockPanel.SetDock(_hex, Dock.Right);
        head.Children.Add(_hex);
        _swatch.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(_swatch);
        head.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(head);
        _rails.Children.Add(_hue);
        _rails.Children.Add(_sat);
        _rails.Children.Add(_val);
        _rails.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        Children.Add(_rails);

        // In a long list the rails stay folded; the swatch opens them.
        _swatch.Cursor = System.Windows.Input.Cursors.Hand;
        _swatch.ToolTip = "Clique para ajustar";
        _swatch.MouseLeftButtonUp += (_, _) => _rails.Visibility = _rails.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int h = 0; h <= 6; h++)
        {
            var c = Rgb.FromHsv(h * 60, 1, 1);
            rainbow.GradientStops.Add(new GradientStop(Color.FromRgb(c.R, c.G, c.B), h / 6.0));
        }
        _hue.Background = rainbow;

        Set(hex);
        _hue.ValueChanged += (_, _) => FromSliders();
        _sat.ValueChanged += (_, _) => FromSliders();
        _val.ValueChanged += (_, _) => FromSliders();
        _hex.TextChanged += (_, _) => FromText();
    }

    void Set(string hex)
    {
        _updating = true;
        var c = Rgb.FromHex(hex);
        var (h, s, v) = ToHsv(c);
        _hue.Value = h;
        _sat.Value = s;
        _val.Value = v;
        _hex.Text = c.ToHex();
        Paint(c);
        _updating = false;
    }

    void FromSliders()
    {
        if (_updating) return;
        var c = Rgb.FromHsv(_hue.Value, _sat.Value, _val.Value);
        _updating = true;
        _hex.Text = c.ToHex();
        _updating = false;
        Paint(c);
        Changed?.Invoke(c.ToHex());
    }

    void FromText()
    {
        if (_updating) return;
        var s = _hex.Text.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return;
        var c = Rgb.FromHex(s);
        var (h, sat, v) = ToHsv(c);
        _updating = true;
        _hue.Value = h;
        _sat.Value = sat;
        _val.Value = v;
        _updating = false;
        Paint(c);
        Changed?.Invoke(c.ToHex());
    }

    void Paint(Rgb c)
    {
        _swatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        var pure = Rgb.FromHsv(_hue.Value, 1, 1);
        var hueColor = Color.FromRgb(pure.R, pure.G, pure.B);
        _sat.Background = new LinearGradientBrush(Colors.White, hueColor, 0);
        _val.Background = new LinearGradientBrush(Colors.Black, hueColor, 0);
    }

    static (double H, double S, double V) ToHsv(Rgb c) => Rgb.ToHsv(c);
}
