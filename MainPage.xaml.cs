using System.Globalization;
using ToyPadMaui.Models;
using ToyPadMaui.Services;
using ToyPadMaui.ViewModels;

namespace ToyPadMaui;

public partial class MainPage : ContentPage
{
    readonly Ps3Service _ps3;
    readonly FigureLibrary _lib;

    readonly Dictionary<string, SlotVm> _slots = new();
    readonly Dictionary<string, Border> _slotViews = new();
    string? _selectedSlot;          // slot awaiting a figure tap
    string _curCat = "all";
    string _curQuery = "";
    bool _connected;

    const string DefaultHintFallback = "";
    string DefaultHint => Loc.T("hint.default");

    public MainPage(Ps3Service ps3, FigureLibrary lib)
    {
        InitializeComponent();
        _ps3 = ps3;
        _lib = lib;
        BuildPanelGlows();
        BuildSlots();
        SizeChanged += OnPageSizeChanged;
        PadImageWrap.SizeChanged += OnPadWrapSizeChanged;
        ApplyLanguage();
        RefreshPlatformUi();
        _ = InitAsync();
    }

    // ---- localization -------------------------------------------------------
    void OnToggleLanguage(object? sender, EventArgs e)
    {
        AppSettings.Language = AppSettings.IsEnglish ? "es" : "en";
        ApplyLanguage();
        RefreshPlatformUi();
        SetConnected(_connected);
    }

    // Push all static UI strings from Loc for the current language.
    void ApplyLanguage()
    {
        AboutBtn.Text   = Loc.T("btn.about");
        ImportBtn.Text  = Loc.T("btn.import");
        ConnectBtn.Text = Loc.T("btn.connect");
        CreditsBtn.Text = Loc.T("btn.credits");
        LangBtn.Text    = Loc.T("btn.lang");
        SyncBtn.Text    = Loc.T("btn.sync");
        ClearBtn.Text   = Loc.T("btn.clearall");
        FabFiguras.Text = Loc.T("btn.figs");
        CatAll.Text     = Loc.T("cat.all");
        CatChar.Text    = Loc.T("cat.char");
        CatVeh.Text     = Loc.T("cat.veh");
        CatGad.Text     = Loc.T("cat.gad");
        SearchBox.Placeholder = Loc.T("search.placeholder");
        if (HintLabel is not null) HintLabel.Text = DefaultHint;
    }

    // Reflect the current platform on the toolbar button + install button.
    void RefreshPlatformUi()
    {
        if (PlatformButton is not null)
            PlatformButton.Text = Loc.T(AppSettings.IsPs4 ? "btn.mode.ps4" : "btn.mode.ps3");
        if (InstallButton is not null)
            InstallButton.Text = Loc.T(AppSettings.IsPs4 ? "btn.install.ps4" : "btn.install.ps3");
    }

    // ---- LED mirror ---------------------------------------------------------
    // The plugin writes the pad's light colors to leds.txt; we poll it while
    // connected and tint the three color bars (left / center / right).
    IDispatcherTimer? _ledTimer;
    bool _ledBusy;

    void StartLedPolling()
    {
        if (_ledTimer is not null) return;
        _ledTimer = Dispatcher.CreateTimer();
        _ledTimer.Interval = TimeSpan.FromMilliseconds(1000);
        _ledTimer.Tick += async (_, _) => await PollLedsAsync();
        _ledTimer.Start();
    }

    void StopLedPolling()
    {
        _ledTimer?.Stop();
        _ledTimer = null;
        ResetLedBars();
    }

    void ResetLedBars()
    {
        _panelColor[0] = _panelColor[1] = _panelColor[2] = null;
        foreach (var b in _slotViews.Values) AnimateSlotGlow(b, Colors.Transparent);
        ApplyPanelGlow(_glowL, 1);
        ApplyPanelGlow(_glowR, 2);
    }

    async Task PollLedsAsync()
    {
        if (_ledBusy || !_connected) return;
        _ledBusy = true;
        try
        {
            var colors = await _ps3.ReadLedsAsync();   // [center, left, right] or null
            if (colors is null) return;
            _panelColor[0] = colors[0];   // center
            _panelColor[1] = colors[1];   // left
            _panelColor[2] = colors[2];   // right
            foreach (var id in _slots.Keys) ApplyLedToSlot(id);
            ApplyPanelGlow(_glowL, 1);    // left area fill
            ApplyPanelGlow(_glowR, 2);    // right area fill
        }
        catch { /* transient FTP errors: keep last colors */ }
        finally { _ledBusy = false; }
    }

    static bool ColorsClose(Color a, Color b)
    {
        return Math.Abs(a.Red - b.Red) < 0.004 && Math.Abs(a.Green - b.Green) < 0.004
            && Math.Abs(a.Blue - b.Blue) < 0.004 && Math.Abs(a.Alpha - b.Alpha) < 0.004;
    }

    static Color Lerp(Color a, Color b, float t)
    {
        return new Color(
            a.Red   + (b.Red   - a.Red)   * t,
            a.Green + (b.Green - a.Green) * t,
            a.Blue  + (b.Blue  - a.Blue)  * t,
            a.Alpha + (b.Alpha - a.Alpha) * t);
    }

    static Color ParseLed(string hex)
    {
        try
        {
            return MapToypadColor(hex.TrimStart('#').ToLowerInvariant());
        }
        catch { return Color.FromArgb("#000000"); }
    }

    // The game drives the pad with "raw" values that the real Toy Pad shows as
    // much brighter/saturated colors. There is no official formula (node-ld's
    // table is empirical and incomplete), but the behavior is consistent:
    //   - a single dominant channel -> that color at full brightness
    //   - "amber" (R high, G ~half of R, B low) -> white  (this is how the pad
    //     produces white; e.g. idle 99420e and 7f360b both -> white)
    // So: try the known exact table first, else amber->white, else normalize
    // to the brightest channel (saturate + brighten).
    static Color MapToypadColor(string raw)
    {
        if (raw.Length != 6) return Color.FromArgb("#000000");
        int r = Convert.ToInt32(raw.Substring(0, 2), 16);
        int g = Convert.ToInt32(raw.Substring(2, 2), 16);
        int b = Convert.ToInt32(raw.Substring(4, 2), 16);

        // exact table (node-ld / Berny23) for the special keystone hues
        if (ExactColor.TryGetValue(raw, out var exact))
            return Color.FromArgb("#" + exact);

        int max = Math.Max(r, Math.Max(g, b));
        if (max == 0) return Color.FromArgb("#000000"); // truly off

        // amber detection -> white: R is the brightest, green is roughly a
        // third to two-thirds of red, blue is small.
        if (r == max && r > 0 && g >= r * 0.30 && g <= r * 0.62 && b <= r * 0.30)
            return Color.FromArgb("#ffffff");

        // otherwise saturate/brighten: scale all channels so the brightest
        // becomes 255, preserving the hue the game intended.
        int nr = Math.Min(255, r * 255 / max);
        int ng = Math.Min(255, g * 255 / max);
        int nb = Math.Min(255, b * 255 / max);
        return Color.FromRgb(nr, ng, nb);
    }

    // Special-case raw->shown colors from node-ld (keystones, scanners, idle).
    static readonly Dictionary<string, string> ExactColor = new()
    {
        ["99420e"] = "ffffff", // idle (full white)
        ["ff6e00"] = "ffff00", // yellow
        ["006e00"] = "00ff00", // green
        ["006e18"] = "00ffff", // cyan
        ["000018"] = "0000ff", // blue
        ["ff0018"] = "ff00ff", // pink
        ["f00016"] = "ff2de6", // wyldstyle scanner
        ["002007"] = "007575", // shift keystone
        ["4c2000"] = "757500",
        ["4c0007"] = "750075",
        ["3f1b05"] = "b0b0b0", // chroma keystone
        ["4c2007"] = "757575",
        ["3f1b00"] = "b0b000",
        ["3f0000"] = "b00000",
        ["000005"] = "0000b0",
        ["001b00"] = "00b000",
        ["ff2700"] = "ffa200",
        ["3f0900"] = "b06f00",
        ["44000d"] = "d500ff",
        ["110003"] = "9300b0",
        ["000016"] = "0000ff", // element keystone (blue)
        ["006700"] = "00ff00", // element keystone (green)
        ["ff1e00"] = "ffa200", // scale keystone
        ["f06716"] = "ffffff",
        ["003700"] = "00ff00", // hack minigame green
        ["ff6e18"] = "ffffff",
    };

    async void OnTogglePlatform(object? sender, EventArgs e)
    {
        // simple toggle PS3 <-> PS4; lets the user switch without re-running
        // the connect flow.
        string choice = await DisplayActionSheet(Loc.T("platform.title"), Loc.T("cancel"), null,
            Loc.T("console.ps3"), Loc.T("console.ps4"));
        if (choice is null || choice == Loc.T("cancel")) return;
        bool ps4 = choice.StartsWith("PS4");
        AppSettings.Platform = ps4 ? "ps4" : "ps3";
        AppSettings.FtpPort = 0; // use platform default (PS3=21, PS4=2121)
        RefreshPlatformUi();
        SetConnected(false);
        await Toast(Loc.T(ps4 ? "toast.mode.ps4" : "toast.mode.ps3"));
    }

    bool _isWide = true;
    bool _sheetOpen;

    void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0) return;
        bool wide = Width >= Height;
        if (wide == _isWide && BodyGrid.ColumnDefinitions.Count > 0) return;
        _isWide = wide;
        ApplyLayout(wide);
    }

    void ApplyLayout(bool wide)
    {
        BodyGrid.RowDefinitions.Clear();
        BodyGrid.ColumnDefinitions.Clear();

        // Detach LibPanel from whatever parent it's in.
        if (LibPanel.Parent is Grid pg) pg.Children.Remove(LibPanel);

        if (wide)
        {
            // Docked beside the pad. Hide sheet chrome.
            SheetHandle.IsVisible = false;
            FabFiguras.IsVisible = false;
            Scrim.IsVisible = false;
            _sheetOpen = false;

            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            BodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });

            Grid.SetRow(PadScroll, 0); Grid.SetColumn(PadScroll, 0);
            LibPanel.IsVisible = true;
            LibPanel.TranslationY = 0;
            LibPanel.Margin = 0;
            LibPanel.HeightRequest = -1;       // auto-fill column
            LibPanel.VerticalOptions = LayoutOptions.Fill;
            LibPanel.HorizontalOptions = LayoutOptions.Fill;
            BodyGrid.Add(LibPanel, 1, 0);
        }
        else
        {
            // Pad fills the body; library becomes a bottom sheet overlay.
            SheetHandle.IsVisible = true;
            FabFiguras.IsVisible = true;

            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            BodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
            Grid.SetRow(PadScroll, 0); Grid.SetColumn(PadScroll, 0);

            // Sheet covers ~75% of height, anchored to bottom, hidden below screen.
            LibPanel.VerticalOptions = LayoutOptions.End;
            LibPanel.HorizontalOptions = LayoutOptions.Fill;
            LibPanel.HeightRequest = Math.Max(320, Height * 0.72);
            LibPanel.Margin = new Thickness(6, 0, 6, 0);
            RootGrid.Add(LibPanel);
            // Ensure scrim + sheet are on top.
            RootGrid.Children.Remove(Scrim);
            RootGrid.Add(Scrim);
            RootGrid.Children.Remove(LibPanel);
            RootGrid.Add(LibPanel);
            RootGrid.Children.Remove(FabFiguras);
            RootGrid.Add(FabFiguras);

            CloseSheetInstant();
        }
    }

    void CloseSheetInstant()
    {
        _sheetOpen = false;
        LibPanel.TranslationY = Math.Max(360, Height);
        LibPanel.IsVisible = false;
        Scrim.IsVisible = false;
        Scrim.Opacity = 0;
        FabFiguras.IsVisible = !_isWide;
    }

    async void OnOpenSheet(object? sender, EventArgs e)
    {
        if (_isWide) return;
        _sheetOpen = true;
        LibPanel.IsVisible = true;
        Scrim.IsVisible = true;
        FabFiguras.IsVisible = false;
        LibPanel.TranslationY = LibPanel.HeightRequest;
        var t1 = Scrim.FadeTo(0.55, 200);
        var t2 = LibPanel.TranslateTo(0, 0, 250, Easing.CubicOut);
        await Task.WhenAll(t1, t2);
    }

    async void OnCloseSheet(object? sender, EventArgs e)
    {
        if (_isWide) { return; }
        var t1 = Scrim.FadeTo(0, 200);
        var t2 = LibPanel.TranslateTo(0, LibPanel.HeightRequest, 250, Easing.CubicIn);
        await Task.WhenAll(t1, t2);
        _sheetOpen = false;
        LibPanel.IsVisible = false;
        Scrim.IsVisible = false;
        FabFiguras.IsVisible = true;
    }

    async Task InitAsync()
    {
        ApplyLayout(true);   // default wide; SizeChanged adjusts to portrait
        await LoadImagesAsync();
        await _lib.LoadAsync();
        RenderLibrary();
        SelectCat(CatAll, "all");
    }

    bool _firstRunDone;
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Subscribe to files opened/shared into the app (iOS "Open with").
        IncomingFile.ZipReceived -= OnIncomingZip;
        IncomingFile.ZipReceived += OnIncomingZip;
        await IncomingFile.FlushPendingAsync();

        if (_firstRunDone) return;
        _firstRunDone = true;

        // Los DisplayAlert no pueden dispararse hasta que la página esté
        // realmente montada en el árbol visual. En Windows, llamarlos desde
        // OnAppearing (incluso con Task.Delay) provoca "This element does not
        // have a XamlRoot" -> fail-fast (0xC000027B). Encolamos el flujo en el
        // Dispatcher para que corra tras el primer render, y lo protegemos.
        Dispatcher.Dispatch(async () =>
        {
            try { await FirstRunAsync(); }
            catch { /* nunca tumbar la app por los prompts de bienvenida */ }
        });
    }

    async Task FirstRunAsync()
    {
        // Pequeña espera para asegurar que la ventana ya tiene XamlRoot.
        await Task.Delay(400);

        if (!_lib.HasFigures)
        {
            string extra = DeviceInfo.Platform == DevicePlatform.iOS ? Loc.T("import.ios") : "";
            bool pick = await DisplayAlert(Loc.T("import.title"),
                Loc.T("import.body", extra),
                Loc.T("import.pick"), Loc.T("later"));
            if (pick) await ImportFlowAsync();
        }

        if (!string.IsNullOrWhiteSpace(AppSettings.Host))
        {
            if (await _ps3.TestAsync()) { SetConnected(true); await SyncAsync(); }
            else SetConnected(false);
        }
        else
        {
            await ChooseConsoleAndConnectAsync();
        }
    }

    // First-run / manual: pick the target console, show that console's setup
    // instructions, then offer to connect. The platform toggle in the toolbar
    // stays in sync. (Connect itself no longer asks for the console.)
    async Task ChooseConsoleAndConnectAsync()
    {
        string choice = await DisplayActionSheet(Loc.T("console.which"), Loc.T("cancel"), null,
            Loc.T("console.ps3"), Loc.T("console.ps4"));
        if (choice is null || choice == Loc.T("cancel")) return;

        bool ps4 = choice.StartsWith("PS4");
        AppSettings.Platform = ps4 ? "ps4" : "ps3";
        AppSettings.FtpPort = 0;            // platform default (PS3=21, PS4=2121)
        RefreshPlatformUi();
        SetConnected(false);

        string title = Loc.T(ps4 ? "connect.ps4.title" : "connect.ps3.title");
        string body  = Loc.T(ps4 ? "connect.ps4.body"  : "connect.ps3.body");
        bool conn = await DisplayAlert(title, body, Loc.T("connect"), Loc.T("later"));
        if (conn) await ConnectFlowAsync();
    }

    async Task LoadImagesAsync()
    {
        try
        {
            BgImage.Source = ImageSource.FromStream(() =>
                FileSystem.OpenAppPackageFileAsync("wallpaper.jpg").Result);
            LogoImage.Source = ImageSource.FromStream(() =>
                FileSystem.OpenAppPackageFileAsync("legopatcher.png").Result);
            PadImage.Source = ImageSource.FromStream(() =>
                FileSystem.OpenAppPackageFileAsync("legoportal.png").Result);
        }
        catch { /* assets optional */ }
        await Task.CompletedTask;
    }

    void OnPadWrapSizeChanged(object? sender, EventArgs e)
    {
        double w = PadImageWrap.Width;
        if (w <= 0) return;
        double targetH = w * 0.75;          // 4:3 image
        if (Math.Abs(PadImageWrap.HeightRequest - targetH) > 0.5)
            PadImageWrap.HeightRequest = targetH;
        LayoutPadZones();
    }

    void LayoutPadZones()
    {
        double w = PadImageWrap.Width;
        double h = PadImageWrap.Height > 0 ? PadImageWrap.Height : w * 0.75;
        if (w <= 0 || h <= 0) return;
        LayoutGlow(_glowL, ContourL, w, h);
        LayoutGlow(_glowR, ContourR, w, h);
        foreach (var id in _slots.Keys) LayoutSlot(id, w, h);
    }

    // Slot center position (fraction of pad) from the fixed factory calibration.
    (double x, double y) SlotPos(string id) { var c = Calib(id); return (c.x, c.y); }
    double SlotW(string id) => Calib(id).w;
    double SlotH(string id) => Calib(id).h;

    void LayoutSlot(string id, double w, double h)
    {
        if (!_slotViews.TryGetValue(id, out var b)) return;
        var c = Calib(id);
        double sw = c.w * w;
        double sh = c.h * w;   // both relative to width so they stay stable
        b.WidthRequest = sw; b.HeightRequest = sh;
        b.RotationX = c.rx;
        b.RotationY = c.ry;
        b.Rotation  = c.rz;
        AbsoluteLayout.SetLayoutFlags(b, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(b, new Rect(c.x * w - sw / 2, c.y * h - sh / 2, sw, sh));
    }

    // ---- per-slot panel lighting --------------------------------------------
    // Each figure lights up with its panel's color: L*=left, C=center, R*=right.
    // colors from ReadLeds are ordered [center, left, right].
    readonly string?[] _panelColor = new string?[3]; // 0=center,1=left,2=right

    // Panel-fill polygons: the whole white L-area of each side lights up so the
    // gaps between figures are colored too. Contours traced from legoportal.png.
    Microsoft.Maui.Controls.Shapes.Polygon? _glowL, _glowR;
    static readonly double[] ContourL = {
        0.339,0.434, 0.340,0.445, 0.340,0.453, 0.339,0.462, 0.338,0.470, 0.337,0.479,
        0.336,0.487, 0.335,0.496, 0.334,0.504, 0.333,0.513, 0.332,0.521, 0.331,0.529,
        0.330,0.538, 0.329,0.546, 0.328,0.555, 0.327,0.562, 0.326,0.572, 0.326,0.579,
        0.325,0.589, 0.326,0.596, 0.390,0.605, 0.404,0.613, 0.418,0.621, 0.435,0.630,
        0.448,0.638, 0.465,0.647, 0.466,0.655, 0.466,0.664, 0.466,0.672, 0.465,0.681,
        0.465,0.689, 0.465,0.697, 0.465,0.706, 0.464,0.714, 0.464,0.723, 0.464,0.730,
        0.464,0.740, 0.464,0.747, 0.464,0.757, 0.463,0.764, 0.194,0.777, 0.146,0.777,
        0.148,0.764, 0.150,0.757, 0.152,0.747, 0.154,0.740, 0.156,0.730, 0.157,0.723,
        0.159,0.714, 0.161,0.706, 0.163,0.697, 0.165,0.689, 0.166,0.681, 0.168,0.672,
        0.170,0.664, 0.172,0.655, 0.174,0.647, 0.176,0.638, 0.177,0.630, 0.179,0.621,
        0.181,0.613, 0.183,0.605, 0.185,0.596, 0.187,0.589, 0.188,0.579, 0.189,0.572,
        0.191,0.562, 0.193,0.555, 0.194,0.546, 0.196,0.538, 0.198,0.529, 0.200,0.521,
        0.202,0.513, 0.204,0.504, 0.205,0.496, 0.207,0.487, 0.209,0.479, 0.211,0.470,
        0.212,0.462, 0.214,0.453, 0.216,0.445, 0.222,0.434 };
    static readonly double[] ContourR = {
        0.785,0.431, 0.791,0.443, 0.792,0.451, 0.794,0.460, 0.796,0.467, 0.798,0.477,
        0.800,0.484, 0.802,0.493, 0.804,0.501, 0.806,0.510, 0.808,0.518, 0.810,0.526,
        0.812,0.535, 0.813,0.543, 0.815,0.552, 0.817,0.560, 0.819,0.569, 0.821,0.577,
        0.823,0.586, 0.825,0.594, 0.827,0.603, 0.829,0.611, 0.830,0.618, 0.832,0.628,
        0.834,0.635, 0.836,0.645, 0.838,0.652, 0.840,0.661, 0.842,0.669, 0.844,0.678,
        0.846,0.686, 0.848,0.694, 0.850,0.703, 0.852,0.711, 0.854,0.720, 0.855,0.728,
        0.857,0.737, 0.859,0.745, 0.861,0.754, 0.863,0.762, 0.582,0.775, 0.548,0.775,
        0.547,0.762, 0.547,0.754, 0.547,0.745, 0.546,0.737, 0.546,0.728, 0.546,0.720,
        0.545,0.711, 0.545,0.703, 0.545,0.694, 0.544,0.686, 0.544,0.678, 0.544,0.669,
        0.543,0.661, 0.543,0.652, 0.543,0.645, 0.554,0.635, 0.568,0.628, 0.585,0.618,
        0.599,0.611, 0.613,0.603, 0.680,0.594, 0.681,0.586, 0.681,0.577, 0.680,0.569,
        0.679,0.560, 0.678,0.552, 0.677,0.543, 0.676,0.535, 0.675,0.526, 0.674,0.518,
        0.673,0.510, 0.672,0.501, 0.671,0.493, 0.670,0.484, 0.669,0.477, 0.668,0.467,
        0.667,0.460, 0.666,0.451, 0.665,0.443, 0.669,0.431 };

    void BuildPanelGlows()
    {
        Microsoft.Maui.Controls.Shapes.Polygon Make()
        {
            var p = new Microsoft.Maui.Controls.Shapes.Polygon
            {
                Stroke = new SolidColorBrush(Colors.Transparent),
                StrokeThickness = 0,
                Fill = new SolidColorBrush(Colors.Transparent),
                InputTransparent = true,
            };
            AbsoluteLayout.SetLayoutFlags(p, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
            AbsoluteLayout.SetLayoutBounds(p, new Rect(0, 0, 1, 1));
            PadOverlay.Add(p);   // added before slots -> sits behind them
            return p;
        }
        _glowL = Make();
        _glowR = Make();
    }

    void LayoutGlow(Microsoft.Maui.Controls.Shapes.Polygon? poly, double[] c, double w, double h)
    {
        if (poly is null) return;
        var pc = poly.Points;
        pc.Clear();
        for (int i = 0; i + 1 < c.Length; i += 2) pc.Add(new Point(c[i] * w, c[i + 1] * h));
    }

    void ApplyPanelGlow(Microsoft.Maui.Controls.Shapes.Polygon? poly, int panel)
    {
        if (poly is null) return;
        var hex = _panelColor[panel];
        Color c = hex is null ? Colors.Black : ParseLed(hex);
        bool on = !(c.Red == 0 && c.Green == 0 && c.Blue == 0);
        var to = on ? new Color(c.Red, c.Green, c.Blue, 0.42f) : Colors.Transparent;
        var from = (poly.Fill as SolidColorBrush)?.Color ?? Colors.Transparent;
        if (ColorsClose(from, to)) return;
        poly.AbortAnimation("pg");
        var anim = new Animation(t => poly.Fill = new SolidColorBrush(Lerp(from, to, (float)t)),
                                 0, 1, Easing.CubicInOut);
        anim.Commit(poly, "pg", length: 450);
    }

    static int SlotPanel(string id) => id == "C" ? 0 : (id[0] == 'L' ? 1 : 2);

    void ApplyLedToSlot(string id)
    {
        if (!_slotViews.TryGetValue(id, out var b)) return;
        var hex = _panelColor[SlotPanel(id)];
        Color c = hex is null ? Colors.Black : ParseLed(hex);
        bool on = !(c.Red == 0 && c.Green == 0 && c.Blue == 0);
        AnimateSlotGlow(b, on ? new Color(c.Red, c.Green, c.Blue, 0.55f) : Colors.Transparent);
    }

    void AnimateSlotGlow(Border panel, Color toFill)
    {
        const uint ms = 450; string name = "ledfade";
        var from = panel.BackgroundColor ?? Colors.Transparent;
        if (ColorsClose(from, toFill)) return;
        panel.AbortAnimation(name);
        var anim = new Animation(t => panel.BackgroundColor = Lerp(from, toFill, (float)t),
                                 0, 1, Easing.CubicInOut);
        anim.Commit(panel, name, length: ms);
    }

    // ---- slot construction --------------------------------------------------

    void BuildSlots()
    {
        AddSlot("L1", "1"); AddSlot("L2", "2"); AddSlot("L3", "3");
        AddSlot("C", "C", center: true);
        AddSlot("R1", "1"); AddSlot("R2", "2"); AddSlot("R3", "3");
    }

    // Default center positions (fraction of the pad) for each slot. The three
    // figures on a side are SEPARATED vertically; all are editable.
    static (double x, double y) SlotDefault(string id)
    {
        var c = Calib(id);
        return (c.x, c.y);
    }

    // Factory calibration per slot: position, size and 3D perspective. Measured
    // by hand on legoportal.png and symmetrized left/right. Used as the default
    // when there's no saved value; the editor + "Pegar valores" can override.
    static (double x, double y, double w, double h, double rx, double ry, double rz) Calib(string id) => id switch
    {
        // Derived from the white-area contour of legoportal.png: each side is a
        // foreshortened trapezoid that widens downward, so two figures sit on
        // the wide lower part and one on the narrower upper part. Symmetrized.
        // L1/R1/C kept (verified good); the 4 lower slots restored to the
        // hand-validated values (sit higher, y~0.68, on the wide lower band).
        "L1" => (0.266, 0.510, 0.140, 0.150, 35, -16, 0),  // upper-outer
        "L2" => (0.243, 0.686, 0.157, 0.164, 39, -14, 0),  // lower-outer
        "L3" => (0.395, 0.685, 0.147, 0.157, 33,  -9, 0),  // lower-inner
        "C"  => (0.500, 0.449, 0.212, 0.178, 28,   0, 0),
        "R1" => (0.734, 0.510, 0.140, 0.150, 35,  16, 0),
        "R2" => (0.767, 0.676, 0.160, 0.180, 43,  15, 0),
        "R3" => (0.614, 0.682, 0.147, 0.153, 31,   9, 0),
        _    => (0.5, 0.5, 0.075, 0.075, 0, 0, 0),
    };

    void AddSlot(string id, string tag, bool center = false)
    {
        var vm = new SlotVm { Id = id, Tag = tag };
        _slots[id] = vm;

        double size = center ? 54 : 44;

        var img = new Image { Aspect = Aspect.AspectFit, WidthRequest = size * 0.95, HeightRequest = size * 0.7, InputTransparent = true };
        img.SetBinding(Image.SourceProperty, new Binding(nameof(SlotVm.Thumb)));
        img.SetBinding(IsVisibleProperty, new Binding(nameof(SlotVm.HasThumb)));

        var nameLabel = new Label
        {
            FontSize = 8, TextColor = Color.FromArgb("#1a1a1a"),
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1,
            InputTransparent = true,
        };
        nameLabel.SetBinding(Label.TextProperty, new Binding(nameof(SlotVm.DisplayName)));

        // Remove (X) button, top-right, visible only when slot is filled.
        var removeBtn = new Button
        {
            Text = "✕", FontSize = 8, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#ff8a8a"),
            BackgroundColor = Color.FromArgb("#cc1a1020"),
            CornerRadius = 8, Padding = 0,
            WidthRequest = 15, HeightRequest = 15,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 1, 1, 0),
        };
        removeBtn.SetBinding(IsVisibleProperty, new Binding(nameof(SlotVm.Filled)));
        removeBtn.Clicked += async (_, _) => await RemoveSlotAsync(id);

        var stack = new VerticalStackLayout
        {
            Spacing = 1, Padding = 2,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            InputTransparent = true, CascadeInputTransparent = true,
            Children = { img, nameLabel },
        };

        // Slot sits ON TOP of the Toy Pad image, so keep it transparent: the
        // pad's white zone shows through, and the figure thumb floats on it.
        var inner = new Grid { InputTransparent = false, Children = { stack, removeBtn } };

        var border = new Border
        {
            HeightRequest = size, WidthRequest = size,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            StrokeShape = center
                ? new Microsoft.Maui.Controls.Shapes.Ellipse()
                : new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Stroke = Color.FromArgb("#00000000"), StrokeThickness = 1,
            BackgroundColor = Color.FromArgb("#00000000"),
            BindingContext = vm,
            Content = inner,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OnSlotTapped(id);
        border.GestureRecognizers.Add(tap);

        // Drag a filled slot -> drop on another slot (move/swap).
        var drag = new DragGestureRecognizer();
        drag.CanDrag = true;
        drag.DragStarting += (_, e) =>
        {
            if (!_slots[id].Filled) { e.Cancel = true; return; }
            e.Data.Properties["kind"] = "slot";
            e.Data.Properties["slot"] = id;
        };
        border.GestureRecognizers.Add(drag);

        // Accept drops from library or another slot.
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.DragOver += (_, e) => e.AcceptedOperation = DataPackageOperation.Copy;
        drop.Drop += async (_, e) => await OnDropAsync(id, e);
        border.GestureRecognizers.Add(drop);

        _slotViews[id] = border;
        PadOverlay.Add(border);

        vm.PropertyChanged += (_, _) => UpdateSlotVisual(id);
    }

    void UpdateSlotVisual(string id)
    {
        var vm = _slots[id];
        var b = _slotViews[id];
        bool selected = _selectedSlot == id;
        // Only the STROKE conveys selection/filled state; the BackgroundColor is
        // owned by the per-slot panel light (ApplyLedToSlot), so we never touch
        // it here or we'd clobber the glow.
        if (selected)
            b.Stroke = new SolidColorBrush(Color.FromArgb("#f5c518"));
        else if (vm.Filled)
            b.Stroke = new SolidColorBrush(Color.FromArgb("#aaf5c518"));
        else
            b.Stroke = new SolidColorBrush(Colors.Transparent);
    }

    // ---- interactions -------------------------------------------------------

    async void OnSlotTapped(string id)
    {
        // Tap-to-move: if a FILLED slot is already selected and we tap a
        // DIFFERENT slot, move/swap between them (works on touch where drag
        // is unreliable).
        if (_selectedSlot is not null && _selectedSlot != id
            && _slots[_selectedSlot].Filled)
        {
            await MoveOrSwapAsync(_selectedSlot, id);
            _selectedSlot = null;
            RefreshAllSlotVisuals();
            if (!_isWide && _sheetOpen) OnCloseSheet(this, EventArgs.Empty);
            return;
        }

        // Otherwise toggle selection of this slot.
        _selectedSlot = (_selectedSlot == id) ? null : id;
        RefreshAllSlotVisuals();

        // Portrait: open the figure sheet only when selecting an EMPTY slot
        // (to pick a figure). Selecting a filled slot just arms it for a move.
        if (!_isWide)
        {
            bool emptySelected = _selectedSlot is not null && !_slots[_selectedSlot].Filled;
            if (emptySelected && !_sheetOpen) OnOpenSheet(this, EventArgs.Empty);
            else if (_selectedSlot is null && _sheetOpen) OnCloseSheet(this, EventArgs.Empty);
        }
    }

    // Move (dst empty) or swap (dst filled) the figures between two slots.
    async Task MoveOrSwapAsync(string srcSlot, string dstSlot)
    {
        if (!await EnsureConnectedAsync()) return;
        var src = _slots[srcSlot];
        var dst = _slots[dstSlot];
        if (!src.Filled) return;

        try
        {
            if (dst.Filled)
            {
                var srcFig = _lib.Figures.FirstOrDefault(x => x.RelPath == src.RelPath);
                var dstFig = _lib.Figures.FirstOrDefault(x => x.RelPath == dst.RelPath);
                if (srcFig is null || dstFig is null)
                {
                    await Toast(Loc.T("swap.fail"));
                    return;
                }
                await _ps3.PlaceAsync(dstSlot, _lib.ReadBytes(srcFig));
                await _ps3.PlaceAsync(srcSlot, _lib.ReadBytes(dstFig));
                var sN = src.Name; var sP = src.RelPath; var sT = src.Thumb;
                src.Set(dst.Name, dst.RelPath, dst.Thumb);
                dst.Set(sN, sP, sT);
                await Toast(Loc.T("swapped", srcSlot, dstSlot));
            }
            else
            {
                var srcFig = _lib.Figures.FirstOrDefault(x => x.RelPath == src.RelPath);
                if (srcFig is null) { await Toast(Loc.T("fig.notfound")); return; }
                // Remove the source FIRST so the old and new figureN.bin never
                // coexist with the same UID (that confused the pad's detection
                // when moving a figure between panels).
                await _ps3.RemoveAsync(srcSlot);
                await _ps3.PlaceAsync(dstSlot, _lib.ReadBytes(srcFig));
                dst.Set(src.Name, src.RelPath, src.Thumb);
                src.Clear();
                await Toast(Loc.T("moved", srcSlot, dstSlot));
            }
        }
        catch (Exception ex) { await Toast(Loc.T("err.fmt", ex.Message)); }
    }

    async Task RemoveSlotAsync(string id)
    {
        var vm = _slots[id];
        if (!vm.Filled) return;
        if (!await EnsureConnectedAsync()) return;
        try
        {
            await _ps3.RemoveAsync(id);
            vm.Clear();
            if (_selectedSlot == id) _selectedSlot = null;
            RefreshAllSlotVisuals();
            await Toast(Loc.T("removed", id));
        }
        catch (Exception ex) { await Toast(Loc.T("err.fmt", ex.Message)); }
    }

    void RefreshAllSlotVisuals()
    {
        foreach (var id in _slots.Keys) UpdateSlotVisual(id);
    }

    async void OnFigureTapped(Figure f)
    {
        if (_selectedSlot is null)
        {
            await Toast(Loc.T("slot.firstTapEmpty"));
            return;
        }

        var slot = _selectedSlot;

        // Optimistic UI: update the slot and close the sheet immediately,
        // then upload in the background. This makes the sheet drop instantly
        // instead of waiting ~1-2s for the FTP transfer.
        var prev = (_slots[slot].Name, _slots[slot].RelPath, _slots[slot].Thumb,
                    _slots[slot].Filled);
        _slots[slot].Set(f.Name, f.RelPath, f.ThumbFile);
        _selectedSlot = null;
        RefreshAllSlotVisuals();
        if (!_isWide && _sheetOpen) OnCloseSheet(this, EventArgs.Empty);

        if (!await EnsureConnectedAsync())
        {
            // revert
            if (prev.Filled) _slots[slot].Set(prev.Name, prev.RelPath, prev.Thumb);
            else _slots[slot].Clear();
            RefreshAllSlotVisuals();
            return;
        }

        try
        {
            var bytes = _lib.ReadBytes(f);
            await _ps3.PlaceAsync(slot, bytes);
            await Toast(Loc.T("placed", f.Name, slot));
        }
        catch (Exception ex)
        {
            if (prev.Filled) _slots[slot].Set(prev.Name, prev.RelPath, prev.Thumb);
            else _slots[slot].Clear();
            RefreshAllSlotVisuals();

            var detail = ex.Message;
            var inner = ex.InnerException;
            int depth = 0;
            while (inner is not null && depth++ < 5)
            {
                detail += $"\n→ {inner.GetType().Name}: {inner.Message}";
                inner = inner.InnerException;
            }
            await DisplayAlert(Loc.T("err.place.title"),
                $"{ex.GetType().Name}: {detail}", Loc.T("ok"));
        }
    }

    // Handles dropping a library figure or another slot onto this slot.
    async Task OnDropAsync(string dstSlot, DropEventArgs e)
    {
        var props = e.Data.Properties;
        var kind = props.TryGetValue("kind", out var k) ? k as string : null;
        if (kind is null) return;

        if (!await EnsureConnectedAsync()) return;

        try
        {
            if (kind == "lib")
            {
                var relPath = props["relpath"] as string ?? "";
                var f = _lib.Figures.FirstOrDefault(x => x.RelPath == relPath);
                if (f is null) return;
                var bytes = _lib.ReadBytes(f);
                await _ps3.PlaceAsync(dstSlot, bytes);
                _slots[dstSlot].Set(f.Name, f.RelPath, f.ThumbFile);
                _selectedSlot = null;
                RefreshAllSlotVisuals();
                await Toast(Loc.T("placed", f.Name, dstSlot));
            }
            else if (kind == "slot")
            {
                var srcSlot = props["slot"] as string ?? "";
                if (srcSlot == dstSlot) return;
                var src = _slots[srcSlot];
                var dst = _slots[dstSlot];
                if (!src.Filled) return;

                if (dst.Filled)
                {
                    // SWAP: need bytes of both figures.
                    var srcFig = _lib.Figures.FirstOrDefault(x => x.RelPath == src.RelPath);
                    var dstFig = _lib.Figures.FirstOrDefault(x => x.RelPath == dst.RelPath);
                    if (srcFig is null || dstFig is null)
                    {
                        await Toast(Loc.T("swap.fail"));
                        return;
                    }
                    await _ps3.PlaceAsync(dstSlot, _lib.ReadBytes(srcFig));
                    await _ps3.PlaceAsync(srcSlot, _lib.ReadBytes(dstFig));
                    var sName = src.Name; var sPath = src.RelPath; var sThumb = src.Thumb;
                    src.Set(dst.Name, dst.RelPath, dst.Thumb);
                    dst.Set(sName, sPath, sThumb);
                    await Toast(Loc.T("swapped", srcSlot, dstSlot));
                }
                else
                {
                    // MOVE
                    var srcFig = _lib.Figures.FirstOrDefault(x => x.RelPath == src.RelPath);
                    if (srcFig is null) { await Toast(Loc.T("fig.notfound")); return; }
                    // Remove source first (see note in MoveOrSwapAsync): avoids
                    // the old/new .bin coexisting with the same UID.
                    await _ps3.RemoveAsync(srcSlot);
                    await _ps3.PlaceAsync(dstSlot, _lib.ReadBytes(srcFig));
                    dst.Set(src.Name, src.RelPath, src.Thumb);
                    src.Clear();
                    await Toast(Loc.T("moved", srcSlot, dstSlot));
                }
                _selectedSlot = null;
                RefreshAllSlotVisuals();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert(Loc.T("error"), $"{ex.GetType().Name}: {ex.Message}", Loc.T("ok"));
        }
    }

    // ---- library render -----------------------------------------------------

    void RenderLibrary()
    {
        var q = _curQuery.Trim().ToLowerInvariant();
        var items = _lib.Figures
            .Where(f => _curCat == "all" || MatchCat(f.Category, _curCat))
            .Where(f => q.Length == 0 || f.Name.ToLowerInvariant().Contains(q))
            .Select(FigureVm.From)
            .ToList();
        LibCollection.ItemsSource = items;
    }

    static bool MatchCat(FigureCategory cat, string key) => key switch
    {
        "characters" => cat == FigureCategory.Character,
        "vehicles"   => cat == FigureCategory.Vehicle,
        "gadgets"    => cat == FigureCategory.Gadget,
        _ => true,
    };

    void OnFigureCardTapped(object? sender, EventArgs e)
    {
        // Dismiss keyboard (stays open from search bar on mobile).
        SearchBox.Unfocus();

        if (sender is Element el && el.BindingContext is FigureVm vm)
        {
            var f = _lib.Figures.FirstOrDefault(x => x.RelPath == vm.RelPath);
            if (f is not null) OnFigureTapped(f);
        }
    }

    void OnFigureDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (sender is Element el && el.BindingContext is FigureVm vm)
        {
            e.Data.Properties["kind"] = "lib";
            e.Data.Properties["relpath"] = vm.RelPath;
        }
    }

    // ---- toolbar handlers ---------------------------------------------------

    void OnCat(object? sender, EventArgs e)
    {
        var btn = (Button)sender!;
        var cat = btn == CatChar ? "characters"
                : btn == CatVeh ? "vehicles"
                : btn == CatGad ? "gadgets" : "all";
        SelectCat(btn, cat);
    }

    void SelectCat(Button active, string cat)
    {
        _curCat = cat;
        foreach (var b in new[] { CatAll, CatChar, CatVeh, CatGad })
        {
            bool on = b == active;
            b.BackgroundColor = on ? Color.FromArgb("#f5c518") : Colors.Transparent;
            b.TextColor = on ? Color.FromArgb("#1a1a1a") : Color.FromArgb("#8a8aa0");
        }
        RenderLibrary();
    }

    void OnSearch(object? sender, TextChangedEventArgs e)
    {
        _curQuery = e.NewTextValue ?? "";
        RenderLibrary();
    }

    async void OnSync(object? sender, EventArgs e)
    {
        if (!await EnsureConnectedAsync()) return;
        await SyncAsync();
    }

    async Task SyncAsync()
    {
        try
        {
            var state = await _ps3.StateAsync();
            foreach (var (slot, present) in state)
            {
                var vm = _slots[slot];
                if (present && !vm.Filled) vm.Set("(figura)", "present", null);
                else if (!present && vm.Filled) vm.Clear();
            }
            RefreshAllSlotVisuals();
        }
        catch (Exception ex) { await Toast(Loc.T("err.fmt", ex.Message)); }
    }

    async void OnClear(object? sender, EventArgs e)
    {
        if (!await EnsureConnectedAsync()) return;
        bool ok = await DisplayAlert(Loc.T("clear.title"), Loc.T("clear.body"), Loc.T("yes"), Loc.T("no"));
        if (!ok) return;
        try
        {
            await _ps3.ClearAsync();
            foreach (var vm in _slots.Values) vm.Clear();
            _selectedSlot = null;
            RefreshAllSlotVisuals();
            await Toast(Loc.T("clear.done"));
        }
        catch (Exception ex) { await Toast(Loc.T("err.fmt", ex.Message)); }
    }

    async void OnConnect(object? sender, EventArgs e) => await ConnectFlowAsync();
    async void OnImport(object? sender, EventArgs e) => await ImportFlowAsync();

    async void OnInstall(object? sender, EventArgs e)
    {
        if (!await EnsureConnectedAsync()) return;

        if (AppSettings.IsPs4)
        {
            await InstallPs4FlowAsync();
            return;
        }

        bool go = await DisplayAlert(Loc.T("install.ps3.title"),
            Loc.T("install.ps3.body"), Loc.T("install"), Loc.T("cancel"));
        if (!go) return;

        try
        {
            HintLabel.Text = Loc.T("install.ps3.uploadSprx");
            var sprx = await FigureLibrary.ReadAssetAsync("toypad_emu.sprx");
            await _ps3.InstallSprxAsync(sprx);

            HintLabel.Text = Loc.T("install.ps3.uploadEboot");
            var eboot = await FigureLibrary.ReadAssetAsync("EBOOT.BIN");
            await _ps3.InstallEbootAsync(eboot);

            await DisplayAlert(Loc.T("done"), Loc.T("install.ps3.done"), Loc.T("ok"));
            HintLabel.Text = DefaultHint;
        }
        catch (Exception ex)
        {
            HintLabel.Text = DefaultHint;
            await DisplayAlert(Loc.T("error"), ex.Message, Loc.T("ok"));
        }
    }

    async Task InstallPs4FlowAsync()
    {
        bool go = await DisplayAlert(Loc.T("install.ps4.title"),
            Loc.T("install.ps4.body"), Loc.T("install"), Loc.T("cancel"));
        if (!go) return;

        try
        {
            HintLabel.Text = Loc.T("install.ps4.uploading");
            var prx = await FigureLibrary.ReadAssetAsync("toypad_emu.prx");
            await _ps3.InstallPs4Async(prx);

            await DisplayAlert(Loc.T("done"), Loc.T("install.ps4.done"), Loc.T("ok"));
            HintLabel.Text = DefaultHint;
        }
        catch (Exception ex)
        {
            HintLabel.Text = DefaultHint;
            await DisplayAlert(Loc.T("error"), ex.Message, Loc.T("ok"));
        }
    }

    async void OnAbout(object? sender, EventArgs e)
    {
        await DisplayAlert(Loc.T("about.title"), Loc.T("about.body"), Loc.T("about.faqbtn"));
        await OnFaq();
    }

    async Task OnFaq()
    {
        await DisplayAlert(Loc.T("faq.title"), Loc.T("faq.body"), Loc.T("close"));
    }

    async void OnCredits(object? sender, EventArgs e)
    {
        await DisplayAlert(Loc.T("credits.title"), Loc.T("credits.body"), Loc.T("close"));
    }

    // ---- flows --------------------------------------------------------------

    async Task ConnectFlowAsync()
    {
        // Platform is chosen by the toolbar toggle / first-run console picker,
        // not here. Just connect to the currently selected console.
        bool ps4 = AppSettings.IsPs4;

        string label = Loc.T(ps4 ? "connect.ps4.label" : "connect.ps3.label");
        string ipHint = Loc.T(ps4 ? "connect.ip.ps4" : "connect.ip.ps3");

        string host = await DisplayPromptAsync(label, ipHint,
            initialValue: AppSettings.Host, placeholder: "192.168.1.10",
            keyboard: Keyboard.Url);
        if (host is null) return;
        host = host.Trim();
        if (host.Length == 0) return;

        string user = await DisplayPromptAsync(label,
            Loc.T("connect.user"), initialValue: string.IsNullOrEmpty(AppSettings.User) ? "anonymous" : AppSettings.User);
        if (user is null) user = "anonymous";

        string pass = await DisplayPromptAsync(label,
            Loc.T("connect.pass"), initialValue: AppSettings.Pass);
        pass ??= "";

        AppSettings.Host = host;
        AppSettings.User = string.IsNullOrWhiteSpace(user) ? "anonymous" : user.Trim();
        AppSettings.Pass = pass;

        await Toast(Loc.T("connect.testing"));
        bool ok = await _ps3.TestAsync();
        SetConnected(ok);
        await Toast(ok ? Loc.T("connect.ok", host, ps4 ? "PS4" : "PS3") : Loc.T("connect.fail"));
        if (ok) await SyncAsync();
    }

    // Handles a .zip opened/shared into the app from the OS.
    async Task OnIncomingZip(byte[] bytes)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await Toast(Loc.T("import.importing"));
                using var ms = new MemoryStream(bytes);
                int n = await _lib.ImportZipAsync(ms);
                RenderLibrary();
                await DisplayAlert(Loc.T("import.done.title"), Loc.T("import.done.body", n), Loc.T("ok"));
            }
            catch (Exception ex)
            {
                await DisplayAlert(Loc.T("import.err.title"), ex.Message, Loc.T("ok"));
            }
        });
    }

    async Task ImportFlowAsync()
    {
        try
        {
            // Android < 13 needs storage read permission to open files from
            // shared folders like Download/.
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
                if (status != PermissionStatus.Granted)
                    status = await Permissions.RequestAsync<Permissions.StorageRead>();
            }

            // iOS: let any dismissing modal (the prompt alert) finish its
            // animation before presenting the document picker, otherwise the
            // picker shows but taps don't register / can't select.
            if (DeviceInfo.Platform == DevicePlatform.iOS
                || DeviceInfo.Platform == DevicePlatform.macOS)
                await Task.Delay(400);

            // File type filter. On iOS we deliberately DON'T restrict types:
            // passing specific UTIs greys out zips coming from iCloud/Drive/
            // other providers. We allow everything and validate it's a real
            // zip when opening it.
            PickOptions options;
            if (DeviceInfo.Platform == DevicePlatform.iOS
                || DeviceInfo.Platform == DevicePlatform.macOS)
            {
                options = new PickOptions { PickerTitle = Loc.T("import.pick") };
            }
            else
            {
                var zipType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.Android] = new[] { "application/zip", "application/octet-stream", "*/*" },
                    [DevicePlatform.WinUI]   = new[] { ".zip" },
                });
                options = new PickOptions { PickerTitle = "Selecciona tu Dimensions.zip", FileTypes = zipType };
            }

            FileResult? result = null;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                result = await FilePicker.Default.PickAsync(options);
            });
            if (result is null)
            {
                // iOS 26 picker bug: Select may not work. Tell the user the
                // manual route via the Files app.
                if (DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    await DisplayAlert(Loc.T("import.ios.title"), Loc.T("import.ios.body"), Loc.T("ok"));
                }
                return;
            }

            await Toast(Loc.T("import.importing"));

            // Read via the SAF/stream API (avoids needing the raw file path).
            using var ms = new MemoryStream();
            using (var stream = await result.OpenReadAsync())
                await stream.CopyToAsync(ms);
            ms.Position = 0;

            int n = await _lib.ImportZipAsync(ms);
            RenderLibrary();
            await Toast(Loc.T("import.done.body", n));
        }
        catch (Exception ex)
        {
            await DisplayAlert(Loc.T("import.err.title"), ex.Message, Loc.T("ok"));
        }
    }

    // ---- helpers ------------------------------------------------------------

    async Task<bool> EnsureConnectedAsync()
    {
        if (_connected) return true;
        if (string.IsNullOrWhiteSpace(AppSettings.Host))
        {
            await ConnectFlowAsync();
            return _connected;
        }
        bool ok = await _ps3.TestAsync();
        SetConnected(ok);
        if (!ok) await Toast(Loc.T("connect.fail.ps3"));
        return ok;
    }

    void SetConnected(bool ok)
    {
        _connected = ok;
        ConnDot.Fill = ok ? Color.FromArgb("#4ade80") : Color.FromArgb("#ef4444");
        ConnText.Text = ok ? AppSettings.Host
            : (string.IsNullOrWhiteSpace(AppSettings.Host) ? Loc.T("status.none") : Loc.T("status.disc"));
        if (InstallButton is not null)
            InstallButton.Text = Loc.T(AppSettings.IsPs4 ? "btn.install.ps4" : "btn.install.ps3");
        if (ok) StartLedPolling();
        else StopLedPolling();
    }

    Task Toast(string msg) => DisplayAlertShort(msg);

    int _toastSeq;
    // Lightweight transient notice: shows msg in the hint label, then restores
    // the default hint after a delay.
    async Task DisplayAlertShort(string msg)
    {
        HintLabel.Text = msg;
        int seq = ++_toastSeq;
        await Task.Delay(2500);
        if (seq == _toastSeq) HintLabel.Text = DefaultHint;
    }
}
