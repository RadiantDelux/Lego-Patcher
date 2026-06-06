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

    const string DefaultHint =
        "Arrastra una figura a un espacio, o toca un espacio y luego una figura. " +
        "Arrastra entre espacios para mover o intercambiar. Toca un espacio lleno para quitar.";

    public MainPage(Ps3Service ps3, FigureLibrary lib)
    {
        InitializeComponent();
        _ps3 = ps3;
        _lib = lib;
        BuildSlots();
        SizeChanged += OnPageSizeChanged;
        _ = InitAsync();
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
            LibPanel.TranslationY = 0;
            LibPanel.Margin = 0;
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

        if (!_lib.HasFigures)
        {
            bool pick = await DisplayAlert("Importar figuras (.zip)",
                "Para colocar figuras necesitas un archivo .zip con los volcados NFC (.bin) " +
                "organizados en carpetas Characters, Vehicles y Gadgets.\n\n" +
                "Por motivos legales estos archivos NO se incluyen en la app: son contenido " +
                "del juego y cada quien debe aportar los suyos. Se guardan solo en este " +
                "dispositivo, nunca se suben a ningún servidor.\n\n" +
                "¿Quieres seleccionar tu Dimensions.zip ahora?",
                "Seleccionar zip", "Más tarde");
            if (pick) await ImportFlowAsync();
        }

        if (!string.IsNullOrWhiteSpace(AppSettings.Host))
        {
            if (await _ps3.TestAsync()) { SetConnected(true); await SyncAsync(); }
            else SetConnected(false);
        }
        else
        {
            bool conn = await DisplayAlert("Conectar a la PS3",
                "Para colocar figuras la app se conecta a tu PS3 por FTP (webMAN MOD).\n\n" +
                "Necesitas:\n" +
                "• PS3 con jailbreak (HEN/CFW) y webMAN MOD con FTP activo.\n" +
                "• La IP local de la consola (la ves en webMAN o en ajustes de red).\n\n" +
                "¿Quieres configurar la conexión ahora?",
                "Conectar", "Más tarde");
            if (conn) await ConnectFlowAsync();
        }
    }

    async Task LoadImagesAsync()
    {
        try
        {
            BgImage.Source = ImageSource.FromStream(() =>
                FileSystem.OpenAppPackageFileAsync("wallpaper.jpg").Result);
            LogoImage.Source = ImageSource.FromStream(() =>
                FileSystem.OpenAppPackageFileAsync("legopatcher.png").Result);
        }
        catch { /* assets optional */ }
        await Task.CompletedTask;
    }

    // ---- slot construction --------------------------------------------------

    void BuildSlots()
    {
        AddSlot(ZoneL, "L1", "1"); AddSlot(ZoneL, "L2", "2"); AddSlot(ZoneL, "L3", "3");
        AddSlot(ZoneC, "C", "C", center: true);
        AddSlot(ZoneR, "R1", "1"); AddSlot(ZoneR, "R2", "2"); AddSlot(ZoneR, "R3", "3");
    }

    void AddSlot(Layout parent, string id, string tag, bool center = false)
    {
        var vm = new SlotVm { Id = id, Tag = tag };
        _slots[id] = vm;

        double size = center ? 96 : 88;

        var img = new Image { Aspect = Aspect.AspectFit, WidthRequest = size * 0.78, HeightRequest = size * 0.55, InputTransparent = true };
        img.SetBinding(Image.SourceProperty, new Binding(nameof(SlotVm.Thumb)));
        img.SetBinding(IsVisibleProperty, new Binding(nameof(SlotVm.HasThumb)));

        var nameLabel = new Label
        {
            FontSize = 11, TextColor = Color.FromArgb("#ececf2"),
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap, MaxLines = 2,
            InputTransparent = true,
        };
        nameLabel.SetBinding(Label.TextProperty, new Binding(nameof(SlotVm.DisplayName)));

        var tagLabel = new Label
        {
            Text = tag, FontSize = 10, TextColor = Color.FromArgb("#8a8aa0"),
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(8, 6, 0, 0), InputTransparent = true,
        };

        var stack = new VerticalStackLayout
        {
            Spacing = 4, Padding = 8,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            InputTransparent = true, CascadeInputTransparent = true,
            Children = { img, nameLabel },
        };

        var inner = new Grid { InputTransparent = true, Children = { stack, tagLabel } };

        var border = new Border
        {
            HeightRequest = size, MinimumWidthRequest = 70, MaximumWidthRequest = size,
            HorizontalOptions = LayoutOptions.Fill,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Stroke = Color.FromArgb("#3c3c4d"), StrokeThickness = 1,
            BackgroundColor = Color.FromArgb("#59000000"),
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
        parent.Add(border);

        vm.PropertyChanged += (_, _) => UpdateSlotVisual(id);
    }

    void UpdateSlotVisual(string id)
    {
        var vm = _slots[id];
        var b = _slotViews[id];
        bool selected = _selectedSlot == id;
        if (selected)
        {
            b.Stroke = Color.FromArgb("#f5c518");
            b.BackgroundColor = Color.FromArgb("#26f5c518");
        }
        else if (vm.Filled)
        {
            b.Stroke = Color.FromArgb("#f5c518");
            b.BackgroundColor = Color.FromArgb("#0ff5c518");
        }
        else
        {
            b.Stroke = Color.FromArgb("#3c3c4d");
            b.BackgroundColor = Color.FromArgb("#59000000");
        }
    }

    // ---- interactions -------------------------------------------------------

    async void OnSlotTapped(string id)
    {
        var vm = _slots[id];
        if (vm.Filled)
        {
            // tap filled slot -> remove
            if (!await EnsureConnectedAsync()) return;
            try
            {
                await _ps3.RemoveAsync(id);
                vm.Clear();
                if (_selectedSlot == id) _selectedSlot = null;
                RefreshAllSlotVisuals();
            }
            catch (Exception ex) { await Toast($"Error: {ex.Message}"); }
        }
        else
        {
            // select empty slot, waiting for a figure tap
            _selectedSlot = (_selectedSlot == id) ? null : id;
            RefreshAllSlotVisuals();
            // In portrait, opening the figure sheet right away speeds up the flow.
            if (!_isWide && _selectedSlot is not null && !_sheetOpen)
                OnOpenSheet(this, EventArgs.Empty);
        }
    }

    void RefreshAllSlotVisuals()
    {
        foreach (var id in _slots.Keys) UpdateSlotVisual(id);
    }

    async void OnFigureTapped(Figure f)
    {
        if (_selectedSlot is null)
        {
            await Toast("Primero toca un espacio del Toy Pad");
            return;
        }
        if (!await EnsureConnectedAsync()) return;

        var slot = _selectedSlot;
        try
        {
            var bytes = _lib.ReadBytes(f);
            await _ps3.PlaceAsync(slot, bytes);
            _slots[slot].Set(f.Name, f.RelPath, f.ThumbFile);
            _selectedSlot = null;
            RefreshAllSlotVisuals();
            await Toast($"{f.Name} → {slot}");
            if (!_isWide && _sheetOpen) OnCloseSheet(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            var detail = ex.Message;
            var inner = ex.InnerException;
            int depth = 0;
            while (inner is not null && depth++ < 5)
            {
                detail += $"\n→ {inner.GetType().Name}: {inner.Message}";
                inner = inner.InnerException;
            }
            await DisplayAlert("Error al colocar figura",
                $"{ex.GetType().Name}: {detail}", "OK");
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
                await Toast($"{f.Name} → {dstSlot}");
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
                        await Toast("No puedo intercambiar (figura no encontrada)");
                        return;
                    }
                    await _ps3.PlaceAsync(dstSlot, _lib.ReadBytes(srcFig));
                    await _ps3.PlaceAsync(srcSlot, _lib.ReadBytes(dstFig));
                    var sName = src.Name; var sPath = src.RelPath; var sThumb = src.Thumb;
                    src.Set(dst.Name, dst.RelPath, dst.Thumb);
                    dst.Set(sName, sPath, sThumb);
                    await Toast($"Intercambio {srcSlot} ↔ {dstSlot}");
                }
                else
                {
                    // MOVE
                    var srcFig = _lib.Figures.FirstOrDefault(x => x.RelPath == src.RelPath);
                    if (srcFig is null) { await Toast("Figura no encontrada"); return; }
                    await _ps3.PlaceAsync(dstSlot, _lib.ReadBytes(srcFig));
                    await _ps3.RemoveAsync(srcSlot);
                    dst.Set(src.Name, src.RelPath, src.Thumb);
                    src.Clear();
                    await Toast($"Movido {srcSlot} → {dstSlot}");
                }
                _selectedSlot = null;
                RefreshAllSlotVisuals();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"{ex.GetType().Name}: {ex.Message}", "OK");
        }
    }

    // ---- library render -----------------------------------------------------

    void RenderLibrary()
    {
        LibLayout.Children.Clear();
        var q = _curQuery.Trim().ToLowerInvariant();

        foreach (var f in _lib.Figures)
        {
            if (_curCat != "all" && f.Category.ToString().ToLowerInvariant() + "s" != _curCat
                && !MatchCat(f.Category, _curCat)) continue;
            if (q.Length > 0 && !f.Name.ToLowerInvariant().Contains(q)) continue;
            LibLayout.Children.Add(BuildFigureCard(f));
        }
    }

    static bool MatchCat(FigureCategory cat, string key) => key switch
    {
        "characters" => cat == FigureCategory.Character,
        "vehicles"   => cat == FigureCategory.Vehicle,
        "gadgets"    => cat == FigureCategory.Gadget,
        _ => true,
    };

    View BuildFigureCard(Figure f)
    {
        var img = new Image
        {
            Aspect = Aspect.AspectFit, HeightRequest = 84,
            Source = f.ThumbFile, InputTransparent = true,
        };
        var name = new Label
        {
            Text = f.Name, FontSize = 11, TextColor = Color.FromArgb("#ececf2"),
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap, MaxLines = 2,
            InputTransparent = true,
        };

        var content = new VerticalStackLayout
        {
            Spacing = 4, Padding = 8, InputTransparent = true,
            CascadeInputTransparent = true,
            Children = { img, name },
        };

        var border = new Border
        {
            WidthRequest = 120, HeightRequest = 130, Margin = 4,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Stroke = Color.FromArgb("#2c2c38"), StrokeThickness = 1,
            BackgroundColor = Color.FromArgb("#1f1f2a"),
            Content = content,
        };

        // Tap to place into selected slot.
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OnFigureTapped(f);
        border.GestureRecognizers.Add(tap);

        // Drag a library figure -> drop on a slot to place it.
        var drag = new DragGestureRecognizer();
        drag.DragStarting += (_, e) =>
        {
            e.Data.Properties["kind"] = "lib";
            e.Data.Properties["relpath"] = f.RelPath;
        };
        border.GestureRecognizers.Add(drag);

        return border;
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
        catch (Exception ex) { await Toast($"Error: {ex.Message}"); }
    }

    async void OnClear(object? sender, EventArgs e)
    {
        if (!await EnsureConnectedAsync()) return;
        bool ok = await DisplayAlert("Limpiar todo", "¿Quitar las 7 figuras del Toy Pad?", "Sí", "No");
        if (!ok) return;
        try
        {
            await _ps3.ClearAsync();
            foreach (var vm in _slots.Values) vm.Clear();
            _selectedSlot = null;
            RefreshAllSlotVisuals();
            await Toast("Limpio");
        }
        catch (Exception ex) { await Toast($"Error: {ex.Message}"); }
    }

    async void OnConnect(object? sender, EventArgs e) => await ConnectFlowAsync();
    async void OnImport(object? sender, EventArgs e) => await ImportFlowAsync();

    async void OnInstall(object? sender, EventArgs e)
    {
        if (!await EnsureConnectedAsync()) return;
        bool go = await DisplayAlert("Instalar en la PS3",
            "Se subirá el plugin (toypad_emu.sprx) y se reemplazará el EBOOT.BIN del juego " +
            "por el parcheado (se hace un respaldo del original). ¿Continuar?",
            "Instalar", "Cancelar");
        if (!go) return;

        try
        {
            HintLabel.Text = "Subiendo plugin...";
            var sprx = await FigureLibrary.ReadAssetAsync("toypad_emu.sprx");
            await _ps3.InstallSprxAsync(sprx);

            HintLabel.Text = "Subiendo EBOOT.BIN (38 MB, tarda)...";
            var eboot = await FigureLibrary.ReadAssetAsync("EBOOT.BIN");
            await _ps3.InstallEbootAsync(eboot);

            await DisplayAlert("Listo",
                "Instalación completada. Reinicia el juego desde el XMB para cargar el plugin.", "OK");
            HintLabel.Text = DefaultHint;
        }
        catch (Exception ex)
        {
            HintLabel.Text = DefaultHint;
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    async void OnAbout(object? sender, EventArgs e)
    {
        await DisplayAlert("¿Qué es esto?",
            "Un emulador por software del Toy Pad de LEGO Dimensions para PS3. " +
            "Pones figuras, vehículos y gadgets en el juego sin el portal físico ni las figuras reales.\n\n" +
            "CÓMO INSTALAR\n" +
            "1. Enciende la PS3 y activa el FTP en webMAN.\n" +
            "2. Toca Conectar y escribe la IP local de tu PS3. Prueba la conexión.\n" +
            "3. Toca Instalar PS3 y espera a que termine.\n" +
            "4. Abre el juego y pon las figuras cuando quieras.\n\n" +
            "REQUISITOS\n" +
            "PS3 con jailbreak (HEN/CFW) + webMAN MOD, juego en formato carpeta o ISO, " +
            "versión 1.22, edición americana (BLUS31473).",
            "Ver preguntas frecuentes");

        await OnFaq();
    }

    async Task OnFaq()
    {
        await DisplayAlert("Preguntas frecuentes",
            "¿Necesito el portal o las figuras físicas?\n" +
            "No. Reemplaza el hardware por completo.\n\n" +
            "¿De dónde salen los archivos de las figuras?\n" +
            "Tú los aportas (un .zip con los volcados .bin). Por motivos legales no se " +
            "distribuyen; se guardan solo en este dispositivo.\n\n" +
            "¿Sirve en cualquier PS3?\n" +
            "Solo con jailbreak (HEN/CFW) y webMAN MOD. Por ahora únicamente la edición " +
            "americana (BLUS31473), versión 1.22.\n\n" +
            "¿Por qué los vehículos y gadgets salen en blanco?\n" +
            "Es normal. El juego los muestra genéricos hasta su primera construcción dentro " +
            "del juego, igual que con las figuras reales.\n\n" +
            "¿Por qué importan los 7 espacios?\n" +
            "El Toy Pad tiene 1 espacio central, 3 a la izquierda y 3 a la derecha. El juego " +
            "los usa de forma específica, así que izquierda y derecha no son intercambiables.\n\n" +
            "¿Es seguro? ¿Toca mi juego?\n" +
            "Reemplaza el ejecutable del juego por uno modificado, pero hace un respaldo del " +
            "original primero (EBOOT.BIN.original). Puedes revertirlo restaurándolo.",
            "Cerrar");
    }

    async void OnCredits(object? sender, EventArgs e)
    {
        await DisplayAlert("Créditos",
            "Desarrollo: RadiantDelux — plugin, parche del EBOOT, protocolo e interfaz.\n\n" +
            "Protocolo: basado en node-ld / ToyPadEmu.\n\n" +
            "Herramientas: TrueAncestor SELF Resigner, scetool (naehrwert/flatz), " +
            "SPRXPatcher (modificado), Cell SDK PPU, webMAN MOD / HEN.\n\n" +
            "Imágenes: LEGO Dimensions Wiki (Fandom). Los retratos son los mugshots oficiales de LEGO.com.\n" +
            "LEGO y LEGO Dimensions son marcas de The LEGO Group. Proyecto no afiliado, sin fines de lucro.\n\n" +
            "Los archivos .bin no se distribuyen; cada usuario importa los suyos y se guardan solo en este dispositivo.",
            "Cerrar");
    }

    // ---- flows --------------------------------------------------------------

    async Task ConnectFlowAsync()
    {
        string host = await DisplayPromptAsync("Conectar a la PS3",
            "IP local de la PS3 (la ves en webMAN o ajustes de red):",
            initialValue: AppSettings.Host, placeholder: "192.168.1.10",
            keyboard: Keyboard.Url);
        if (host is null) return;
        host = host.Trim();
        if (host.Length == 0) return;

        string user = await DisplayPromptAsync("Conectar a la PS3",
            "Usuario FTP:", initialValue: string.IsNullOrEmpty(AppSettings.User) ? "anonymous" : AppSettings.User);
        if (user is null) user = "anonymous";

        string pass = await DisplayPromptAsync("Conectar a la PS3",
            "Contraseña FTP (vacío suele bastar):", initialValue: AppSettings.Pass);
        pass ??= "";

        AppSettings.Host = host;
        AppSettings.User = string.IsNullOrWhiteSpace(user) ? "anonymous" : user.Trim();
        AppSettings.Pass = pass;

        await Toast("Probando conexión...");
        bool ok = await _ps3.TestAsync();
        SetConnected(ok);
        await Toast(ok ? $"Conectado a {host}" : "No se pudo conectar");
        if (ok) await SyncAsync();
    }

    async Task ImportFlowAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecciona tu Dimensions.zip",
            });
            if (result is null) return;

            await Toast("Importando...");
            using var stream = await result.OpenReadAsync();
            // copy to memory so ZipArchive can seek
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            int n = await _lib.ImportZipAsync(ms);
            RenderLibrary();
            await Toast($"Importadas {n} figuras");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error al importar", ex.Message, "OK");
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
        if (!ok) await Toast("Sin conexión a la PS3");
        return ok;
    }

    void SetConnected(bool ok)
    {
        _connected = ok;
        ConnDot.Fill = ok ? Color.FromArgb("#4ade80") : Color.FromArgb("#ef4444");
        ConnText.Text = ok ? AppSettings.Host
            : (string.IsNullOrWhiteSpace(AppSettings.Host) ? "sin conexión" : "desconectado");
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
