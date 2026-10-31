using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RgbStudio.Core;
using Forms = System.Windows.Forms;

namespace RgbStudio;

public partial class App : Application
{
    Engine? _engine;
    Forms.NotifyIcon? _tray;
    MainWindow? _window;
    Mutex? _single;
    bool _ownsSingle;
    const string ShowEventName = @"Local\RgbStudioShow";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? Arg(string name) { int i = Array.IndexOf(e.Args, name); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
        bool simulate = e.Args.Contains("--simulate");

        // One instance: two would fight over the same lights. A simulation touches no lights.
        if (!simulate)
        {
            _single = new Mutex(true, @"Local\RgbStudio", out _ownsSingle);
            if (!_ownsSingle)
            {
                // Already running, likely hidden in the tray: ask it to show itself.
                try { using var show = EventWaitHandle.OpenExisting(ShowEventName); show.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                Shutdown();
                return;
            }
            var showRequests = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            new Thread(() =>
            {
                while (showRequests.WaitOne()) Dispatcher.BeginInvoke(ShowWindow);
            }) { IsBackground = true, Name = "Show requests" }.Start();
        }

        Log.Write($"RGB Studio {typeof(App).Assembly.GetName().Version} iniciado ({string.Join(' ', e.Args)})");
        DispatcherUnhandledException += (_, ex) =>
        {
            // A bug in one screen action should not take the lights down with it.
            Log.Write($"Erro na tela: {ex.Exception}");
            MessageBox.Show(ex.Exception.Message, "RGB Studio");
            ex.Handled = true;
        };

        var settings = AppSettings.Load();
        // USB + SDK is what reached every light on this board; the others stay for comparison.
        Func<IRgbBackend> backend = simulate ? () => new SimulatedBackend()
            : e.Args.Contains("--sdk") ? () => new AuraSdkBackend()
            : e.Args.Contains("--usb") ? () => new HidAuraBackend()
            : () => new HybridAuraBackend(settings.Devices.Keys.Count(k => k.StartsWith("ENE_RGB", StringComparison.Ordinal)));

        if (e.Args.Contains("--selftest"))
        {
            // No window: open the hardware, draw a rainbow for 5 s, log, give it back.
            var test = new AppSettings { ActivePreset = Presets.BuiltinId("rainbow"), Devices = settings.Clone().Devices };
            foreach (var d in test.Devices.Values) d.Preset = null;
            var engine = new Engine(test, backend);
            using var opened = new ManualResetEventSlim();
            engine.Started += opened.Set;
            engine.Start();
            opened.Wait(TimeSpan.FromSeconds(30));   // the SDK takes seconds to enumerate
            Thread.Sleep(5000);
            Log.Write($"selftest: erro={engine.Error ?? "nenhum"}, dispositivos={engine.Devices.Count}, {engine.ActualFps:0.0} quadros/s");
            engine.Stop();
            Log.Write("selftest: fim");
            Shutdown();
            return;
        }

        _engine = new Engine(settings.Clone(), backend);
        _engine.Start();

        _window = new MainWindow(_engine, settings, canSave: !simulate) { CountLedsOn = Arg("--count-leds") };
        _tray = new Forms.NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "RGB Studio",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Items.Add("Abrir", null, (_, _) => ShowWindow());
        _tray.ContextMenuStrip.Items.Add("Sair (devolve ao Armoury Crate)", null, (_, _) => Quit());
        _tray.DoubleClick += (_, _) => ShowWindow();

        if (Arg("--smoke") is { } folder)
        {
            ShowWindow();
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try { await _window.RunSmoke(name => SaveShot(Path.Combine(folder, name + ".png"))); Log.Write("smoke: fim"); }
                catch (Exception ex) { Log.Write($"smoke falhou: {ex}"); }
                Quit();
            });
            return;
        }

        if (Arg("--shot") is { } shot)
        {
            // For checking the screen without elevation: show, draw, save a picture, leave.
            ShowWindow();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(double.Parse(Arg("--shot-after") ?? "2.5", System.Globalization.CultureInfo.InvariantCulture)) };
            timer.Tick += (_, _) => { timer.Stop(); SaveShot(shot); Quit(); };
            timer.Start();
            return;
        }

        // The logon task passes --minimized; the setting decides whether it stays in the tray then.
        // Opened by hand, the window always shows.
        bool minimized = e.Args.Contains("--minimized") && settings.StartMinimized;
        if (!minimized) ShowWindow();
    }

    void SaveShot(string path)
    {
        var root = (FrameworkElement)_window!.Content;
        var bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var solid = new DrawingVisual();
        using (var dc = solid.RenderOpen()) dc.DrawRectangle(_window.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(solid);
        bmp.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

    /// <summary>The app's mark, drawn rather than shipped as a file.</summary>
    static System.Drawing.Icon MakeIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var fill = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(0, 0, 32, 32), System.Drawing.Color.FromArgb(255, 45, 85), System.Drawing.Color.FromArgb(124, 77, 255), 45f);
            g.FillEllipse(fill, 1, 1, 30, 30);
            g.FillEllipse(System.Drawing.Brushes.White, 11, 11, 10, 10);
        }
        IntPtr handle = bmp.GetHicon();
        var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

    void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Quit()
    {
        _window?.SaveNow();
        _engine?.Stop();          // releases the lights back to Armoury Crate
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Dispose();
        if (_ownsSingle) _single?.ReleaseMutex();
        base.OnExit(e);
    }
}
