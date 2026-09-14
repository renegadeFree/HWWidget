using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace HWWidget;

/// <summary>Control hub built like the Windows 11 settings window: acrylic frame, left
/// navigation, one page per widget and cards with icon + title + description + control.</summary>
sealed class ControlHub : Window
{
    static readonly string[] Layouts = { "rows", "cards", "tiles", "panel", "panelgraph" };
    static readonly string[] LayoutNames = { "Righe compatte", "Card per elemento", "Tessere", "Pannello (barre)", "Pannello + grafici" };
    static readonly string[] Themes = { "system", "dark", "light" };
    static readonly string[] ThemeNames = { "Come Windows", "Scuro", "Chiaro" };
    static readonly string[] Backdrops = { "blur", "mica", "micaalt", "acrylic", "none" };
    static readonly string[] BackdropNames = { "Acrylic sfocato", "Mica", "Mica Alt", "Acrylic DWM", "Pannello pieno" };
    static readonly string[] GraphStyles = { "area", "line", "bars", "step" };
    static readonly string[] GraphStyleNames = { "Area", "Linea", "Barre", "Scalini" };
    static readonly string[] Durations = { "30", "60", "180", "300", "600" };
    static readonly string[] DurationNames = { "30 secondi", "1 minuto", "3 minuti", "5 minuti", "10 minuti" };
    static readonly string[] Intervals = { "0.5", "1", "2" };
    static readonly string[] IntervalNames = { "0,5 s", "1 s", "2 s" };
    static readonly string[] Swatches =
    {
        "#4CC2FF", "#5CD66E", "#FFB84D", "#FF5B5B", "#A78BFA", "#4FD1C5", "#FF7AC6", "#B0B6BF",
    };
    static readonly (string Key, string Name, string Glyph)[] Meters =
    {
        ("cpu", "CPU", "\uE950"),
        ("gpu", "GPU", "\uE7FC"),
        ("ram", "RAM", "\uE964"),
        ("disk", "Disco", "\uEDA2"),
        ("net", "Rete", "\uE968"),
        ("ds", "DeepSeek", "\uE9D2"),
    };

    readonly AppController _app;
    readonly Palette _p = Palette.For("dark");
    readonly StackPanel _nav = new();
    readonly StackPanel _content = new();
    readonly DispatcherTimer _watch = new();
    readonly List<Button> _navButtons = new();
    IntPtr _hwnd;
    int _page;

    public ControlHub(AppController app)
    {
        _app = app;
        Title = "Hub di controllo — HW Widget";
        Width = 980;
        Height = 720;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 12.5;
        Background = new SolidColorBrush(Color.FromArgb(0x01, 0x12, 0x12, 0x16));
        Foreground = new SolidColorBrush(Colors.White);
        Resources = HubTheme.Create();
        var wpfIcon = AppIcon.Wpf();
        if (wpfIcon != null) Icon = wpfIcon;

        Content = BuildShell();

        Loaded += (_, _) => Backdrop.Apply(_hwnd, "blur", _p);
        Activated += (_, _) => Backdrop.Apply(_hwnd, "blur", _p);

        _watch.Interval = TimeSpan.FromSeconds(2);
        _watch.Tick += (_, _) => { if (_navButtons.Count != _app.Widgets.Count + 2) Rebuild(); };
        _watch.Start();
        Rebuild();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(_hwnd);
        if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
        Backdrop.Apply(_hwnd, "blur", _p);
    }

    // ---------- shell ----------

    FrameworkElement BuildShell()
    {
        var root = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x10, 0x10, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = BuildBody(),
        };
        return root;
    }

    FrameworkElement BuildBody()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // title bar
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // nav + content

        var titleBar = BuildTitleBar();
        Grid.SetRow(titleBar, 0);
        grid.Children.Add(titleBar);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(226) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var navHost = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10, 8, 6, 10),
            Content = _nav,
        };
        var navBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = navHost,
        };
        Grid.SetColumn(navBorder, 0);
        body.Children.Add(navBorder);

        var contentHost = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(26, 18, 26, 26),
            Content = _content,
        };
        Grid.SetColumn(contentHost, 1);
        body.Children.Add(contentHost);

        Grid.SetRow(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    FrameworkElement BuildTitleBar()
    {
        var grid = new Grid { Height = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = "\uE945",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = (Brush)Resources["Accent"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        left.Children.Add(new TextBlock
        {
            Text = "Hub di controllo",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        buttons.Children.Add(IconButton("\uE921", "Riduci", () => WindowState = WindowState.Minimized));
        buttons.Children.Add(IconButton("\uE8BB", "Chiudi", Close));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        grid.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; return; }
            if (e.LeftButton == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
        };

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            Child = grid,
        };
    }

    Button IconButton(string glyph, string tip, Action act)
    {
        var b = new Button { Content = glyph, ToolTip = tip, Style = (Style)Resources["HubIconButton"] };
        b.Click += (_, _) => act();
        return b;
    }

    // ---------- pages ----------

    void Rebuild()
    {
        _nav.Children.Clear();
        _navButtons.Clear();

        _nav.Children.Add(NavItem("\uE710", "Aggiungi widget", "Nuovi pannelli", 0));
        _nav.Children.Add(new TextBlock
        {
            Text = "WIDGET",
            FontSize = 10.5,
            Opacity = 0.55,
            Margin = new Thickness(10, 14, 0, 4),
        });
        for (int i = 0; i < _app.Widgets.Count; i++)
        {
            var w = _app.Widgets[i];
            _nav.Children.Add(NavItem("\uE9D9", w.Config.DisplayName, LayoutName(w.Config.Layout), i + 1));
        }
        _nav.Children.Add(new TextBlock
        {
            Text = "APPLICAZIONE",
            FontSize = 10.5,
            Opacity = 0.55,
            Margin = new Thickness(10, 14, 0, 4),
        });
        _nav.Children.Add(NavItem("\uE713", "Impostazioni app", "Avvio, tray, uscita", _app.Widgets.Count + 1));

        ShowPage(Math.Min(_page, _app.Widgets.Count + 1));
    }

    Button NavItem(string glyph, string title, string subtitle, int page)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Width = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Resources["TextSecondary"],
        });
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = subtitle, FontSize = 10.5, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(texts);

        var b = new Button { Content = stack, Style = (Style)Resources["HubNavButton"], Tag = page };
        b.Click += (_, _) => ShowPage((int)b.Tag);
        _navButtons.Add(b);
        return b;
    }

    void ShowPage(int page)
    {
        _page = page;
        for (int i = 0; i < _navButtons.Count; i++)
            _navButtons[i].Background = i == page
                ? new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))
                : Brushes.Transparent;

        _content.Children.Clear();
        if (page == 0) BuildAddPage();
        else if (page == _app.Widgets.Count + 1) BuildAppPage();
        else if (page - 1 < _app.Widgets.Count) BuildWidgetPage(_app.Widgets[page - 1]);
        else BuildAddPage();
    }

    void BuildAddPage()
    {
        PageHeader("Aggiungi widget", "Crea un nuovo pannello: puoi averne quanti vuoi, ognuno con posizione, dimensioni e contenuto propri.");
        SubTitle("Scegli il widget da creare");
        _content.Children.Add(Card("\uE9D9", "TUTTO", "CPU, GPU, RAM, disco e rete in un unico pannello.",
            TextButton("Crea", () => Add(null), accent: true)));
        _content.Children.Add(Card("\uE950", "CPU", "Utilizzo e frequenza della CPU.",
            TextButton("Crea", () => Add("cpu"))));
        _content.Children.Add(Card("\uE7FC", "GPU", "Utilizzo, temperatura e VRAM della scheda video.",
            TextButton("Crea", () => Add("gpu"))));
        _content.Children.Add(Card("\uE964", "RAM", "Memoria utilizzata e frequenza.",
            TextButton("Crea", () => Add("ram"))));
        _content.Children.Add(Card("\uE968", "RETE", "Upload e download dell'adattatore attivo.",
            TextButton("Crea", () => Add("net"))));
        _content.Children.Add(Card("\uE9D9", "CPU+GPU", "Due schede con i dati principali.",
            TextButton("Crea", () => Add("cpugpu"))));
        _content.Children.Add(Card("\uE945", "AI", "Budget rimasto, budget consumato e token totali di DeepSeek, ChatGPT e Claude (solo numeri).",
            TextButton("Crea", () => Add("ai"))));
        _content.Children.Add(Card("\uE9D2", "DEEPSEEK", "Saldo, spesa di oggi e del mese, token e cache hit per modello, con grafico giornaliero.",
            TextButton("Crea", () => Add("ds"), accent: true)));
    }

    void BuildAppPage()
    {
        PageHeader("Impostazioni app", "Comportamento generale di HW Widget su questo PC.");

        var keys = AiKeys.Load();
        SubTitle("AI e chiavi API");
        _content.Children.Add(CardFull("\uE945", "Questa sezione",
            "DeepSeek → saldo API · OpenAI/Anthropic → spesa API (servono chiavi con permessi di fatturazione/amministratore) · " +
            "token totali di ChatGPT e Claude → letti dai log locali delle rispettive CLI, senza chiavi. " +
            "I limiti di reset degli abbonamenti (ChatGPT/Claude) non sono esposti da nessuna API pubblica.",
            new TextBlock { Text = "", Width = 0 }));
        _content.Children.Add(CardFull("\uE9D2", "Token di utilizzo DeepSeek (per token, spesa e cache hit)",
            "L'API ufficiale DeepSeek espone solo il saldo: token usati, spesa e cache hit si leggono dalle API interne " +
            "di platform.deepseek.com, le stesse della dashboard web, e servono il token di sessione del sito (NON la API key). " +
            "Come prenderlo: apri platform.deepseek.com nel browser e accedi → F12 → scheda Console → incolla " +
            "JSON.parse(localStorage.userToken).value e premi Invio → copia la stringa restituita (è lunga, inizia con eyJ). " +
            "Incollala nel campo \"Token di utilizzo DeepSeek\", premi Salva chiavi e poi Verifica. Il token scade: se i dati " +
            "tornano n/d, ripeti la procedura.",
            new TextBlock { Text = "", Width = 0 }));
        (FrameworkElement deepSeekRow, PasswordBox deepSeek) = KeyRow("DeepSeek API key", keys.DeepSeek);
        (FrameworkElement deepSeekUsageRow, PasswordBox deepSeekUsage) = KeyRow("Token di utilizzo DeepSeek", keys.DeepSeekUsage);
        (FrameworkElement openAiRow, PasswordBox openAi) = KeyRow("OpenAI admin key", keys.OpenAi);
        (FrameworkElement anthropicRow, PasswordBox anthropic) = KeyRow("Anthropic admin key", keys.Anthropic);
        (FrameworkElement githubRow, PasswordBox github) = KeyRow("Token GitHub (aggiornamenti, repo privata)", keys.GitHub);
        var interval = Combo(new[] { "5", "15", "60", "180" },
            new[] { "5 minuti", "15 minuti", "1 ora", "3 ore" },
            () => keys.RefreshMinutes.ToString(),
            v => { keys.RefreshMinutes = int.Parse(v); keys.Save(); });
        var saveRow = new WrapPanel();
        saveRow.Children.Add(TextButton("Salva chiavi", () =>
        {
            keys.DeepSeek = deepSeek.Password.Trim();
            keys.DeepSeekUsage = AiKeys.Normalize(deepSeekUsage.Password);
            keys.OpenAi = openAi.Password.Trim();
            keys.Anthropic = anthropic.Password.Trim();
            keys.GitHub = github.Password.Trim();
            keys.Save();
            _ = SensorHub.RefreshAiAsync();
            Rebuild();
        }, accent: true));
        saveRow.Children.Add(TextButton("Rileggi ora", () => _ = SensorHub.RefreshAiAsync()));
        var dsStatus = new TextBlock
        {
            Style = (Style)Resources["HubCardDesc"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = (Brush)Resources["TextSecondary"],
        };
        var verifyRow = new WrapPanel();
        verifyRow.Children.Add(TextButton("Accedi e prendi il token (automatico)", () =>
        {
            var w = new DeepSeekLogin();
            w.Closed += (_, _) => { keys.DeepSeekUsage = AiKeys.Load().DeepSeekUsage; deepSeekUsage.Password = keys.DeepSeekUsage; };
            w.Show();
            w.Activate();
        }, accent: true));
        verifyRow.Children.Add(TextButton("Verifica token DeepSeek", () =>
        {
            keys.DeepSeek = deepSeek.Password.Trim();
            keys.DeepSeekUsage = AiKeys.Normalize(deepSeekUsage.Password);
            keys.Save();
            dsStatus.Text = "Verifica in corso…";
            _ = Task.Run(async () =>
            {
                var r = await DeepSeekUsage.FetchAsync(keys.DeepSeek, keys.DeepSeekUsage);
                Dispatcher.Invoke(() => dsStatus.Text = r.Status.Length > 0
                    ? "⚠ " + r.Status
                    : $"✓ ok · saldo {r.Balance} · {r.Models.Count} modelli · mese {r.MonthCost} · {r.Days.Count} giorni");
            });
        }));
        foreach (var b in verifyRow.Children.OfType<Button>())
            if (b.Content?.ToString()?.StartsWith("Verifica") == true) b.Margin = new Thickness(8, 0, 0, 0);
        // CardFull: con la card normale i campi stretti schiacciano la descrizione in verticale
        _content.Children.Add(CardFull("\uE72C", "Chiavi", "Salvate cifrate (DPAPI) in " + Path.Combine(WidgetConfig.Dir, "keys.dat"),
            NewColumn(deepSeekRow, deepSeekUsageRow, openAiRow, anthropicRow, githubRow, interval, saveRow, verifyRow, dsStatus)));

        SubTitle("Aggiornamenti");
        var updateInfo = new TextBlock
        {
            Text = $"Versione installata: {Updater.CurrentText}",
            Style = (Style)Resources["HubCardDesc"],
            Margin = new Thickness(0, 0, 0, 8),
        };
        var updateRow = new WrapPanel();
        updateRow.Children.Add(TextButton("Controlla aggiornamenti", () => _ = CheckUpdates(updateInfo)));
        updateRow.Children.Add(new CheckBox
        {
            Content = "Controlla all'avvio",
            IsChecked = keys.CheckUpdatesOnStartup,
            Style = (Style)Resources["HubToggle"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        });
        if (updateRow.Children[1] is CheckBox startupBox)
            startupBox.Click += (_, _) => { keys.CheckUpdatesOnStartup = startupBox.IsChecked == true; keys.Save(); };
        _content.Children.Add(CardFull("\uE895", "Aggiornamento automatico",
            "Legge l'ultima release della repo privata con il token GitHub qui sopra, scarica l'installer e aggiorna.",
            NewColumn(updateInfo, updateRow)));

        SubTitle("Avvio e accesso");
        _content.Children.Add(Card("\uE7E8", "Avvia con Windows", "Il widget parte da solo all'accesso.",
            Switch(() => _app.StartupEnabled, v => _app.SetStartup(v))));
        _content.Children.Add(Card("\uE8FD", "Hub dal tray", "Doppio clic sull'icona nella barra delle applicazioni.",
            new TextBlock { Text = "Attivo", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center }));
        _content.Children.Add(Card("\uE765", "Scorciatoia HUD", "Mostra/nascondi i clic attraverso sui widget.",
            new TextBlock
            {
                Text = _app.HotkeyOk ? "Ctrl+Alt+H" : "non disponibile",
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
            }));

        SubTitle("Manutenzione");
        _content.Children.Add(Card("\uE74D", "Chiudi tutti i widget", "Restano salvati: puoi riaprirli da qui.",
            TextButton("Chiudi tutti", () =>
            {
                foreach (var w in _app.Widgets.ToList()) w.Close();
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Rebuild));
            })));
        _content.Children.Add(Card("\uE7E8", "Uscita", "Termina HW Widget e rimuove l'icona dal tray.",
            TextButton("Esci", () => _app.ExitAll())));

        SubTitle("Informazioni");
        _content.Children.Add(Card("\uE946", "Cartella impostazioni", WidgetConfig.Dir,
            new TextBlock { Text = "", Width = 0 }));
    }

    void BuildWidgetPage(MainWindow w)
    {
        var c = w.Config;
        PageHeader($"Widget “{c.DisplayName}”", "Modifiche applicate subito: quello che cambi qui si vede immediatamente sul widget.");

        SubTitle("Azioni");
        _content.Children.Add(Card("\uE8A7", "Porta in primo piano", "Mostra e attiva questo widget.",
            TextButton("Porta davanti", () => { w.Show(); w.Activate(); })));
        _content.Children.Add(Card("\uE7C4", "Adatta l'altezza al contenuto", "Ridimensiona la finestra per contenere tutte le sezioni.",
            TextButton("Adatta", () => { w.FitHeightNow(); })));
        _content.Children.Add(Card("\uE74D", "Chiudi questo widget", "La configurazione resta salvata.",
            TextButton("Chiudi", () =>
            {
                w.Close();
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Rebuild));
            })));

        SubTitle("Composizione");
        _content.Children.Add(Card("\uE8AC", "Nome del widget", "Compare nell'elenco a sinistra. Vuoto = nome dedotto dagli elementi visibili.",
            NameBox(c)));
        _content.Children.Add(Card("\uE8A9", "Layout", "Come sono disegnati i dati.",
            Combo(Layouts, LayoutNames, () => c.Layout, v => { c.Layout = v; Apply(w); })));
        _content.Children.Add(Card("\uE790", "Tema", "Chiaro, scuro o come Windows.",
            Combo(Themes, ThemeNames, () => c.Theme, v => { c.Theme = v; Apply(w); })));
        _content.Children.Add(Card("\uE7E6", "Materiale", "Acrylic sfocato è quello che resta sfocato anche senza focus.",
            Combo(Backdrops, BackdropNames, () => c.Backdrop, v => { c.Backdrop = v; Apply(w); })));
        _content.Children.Add(Expander("\uE71D", "Elementi visibili", "Scegli cosa mostrare in questo widget.",
            ("\uE968", "Rete", Switch(() => c.ShowNet, v => { c.ShowNet = v; Apply(w); })),
            ("\uE950", "CPU", Switch(() => c.ShowCpu, v => { c.ShowCpu = v; Apply(w); })),
            ("\uE7FC", "GPU", Switch(() => c.ShowGpu, v => { c.ShowGpu = v; Apply(w); })),
            ("\uE964", "RAM", Switch(() => c.ShowRam, v => { c.ShowRam = v; Apply(w); })),
            ("\uEDA2", "Disco", Switch(() => c.ShowDisk, v => { c.ShowDisk = v; Apply(w); })),
            ("\uE9D2", "DeepSeek (saldo, uso e spesa del mese)", Switch(() => c.ShowDs, v => { c.ShowDs = v; Apply(w); })),
            ("\uE9D2", "Metriche secondarie", Switch(() => c.ShowSecondary, v => { c.ShowSecondary = v; Apply(w); })),
            ("\uE8A7", "Titolo “HW Widget” nel pannello", Switch(() => c.ShowTitle, v => { c.ShowTitle = v; Apply(w); }))));
        _content.Children.Add(Expander("\uE945", "Sezione AI", "Solo numeri, senza grafici: budget rimasto, budget consumato e token totali. " +
                                                                 "Spegnendo un fornitore le sue righe spariscono dal widget.",
            ("\uE945", "Mostra la sezione AI", Switch(() => c.ShowAi, v => { c.ShowAi = v; Apply(w); })),
            ("\uE9D9", "DeepSeek (saldo API)", Switch(() => c.AiProviders.Contains("deepseek"),
                v => { ToggleProvider(c, "deepseek", v); Apply(w); })),
            ("\uE9D9", "ChatGPT (spesa API + token dei log)", Switch(() => c.AiProviders.Contains("openai"),
                v => { ToggleProvider(c, "openai", v); Apply(w); })),
            ("\uE9D9", "Claude (spesa API + token dei log)", Switch(() => c.AiProviders.Contains("anthropic"),
                v => { ToggleProvider(c, "anthropic", v); Apply(w); })),
            ("\uE8C7", "Budget mensile DeepSeek (USD)", BudgetBox(c, "deepseek", w)),
            ("\uE8C7", "Budget mensile ChatGPT (USD)", BudgetBox(c, "openai", w)),
            ("\uE8C7", "Budget mensile Claude (USD)", BudgetBox(c, "anthropic", w))));
        _content.Children.Add(Expander("\uE8CB", "Ordine degli elementi", "Sposta le sezioni su e giù (primo = in alto).",
            OrderRows(w).ToArray()));

        SubTitle("Dimensioni");
        _content.Children.Add(Card("\uE8A9", "Scala generale", "Ingrandisce o riduce tutto il widget.",
            Slider(0.5, 2.5, 0.05, () => c.UiScale, v => { c.UiScale = v; Apply(w); })));
        _content.Children.Add(Card("\uE8D3", "Dimensione testo", "Solo i testi.",
            Slider(0.6, 2.2, 0.05, () => c.TextScale, v => { c.TextScale = v; Apply(w); })));
        _content.Children.Add(Card("\uE7C4", "Altezza elementi", "Spazio verticale delle righe.",
            Slider(0.6, 3, 0.05, () => c.RowScale, v => { c.RowScale = v; Apply(w); })));
        _content.Children.Add(Card("\uE7C4", "Larghezza finestra", "In pixel.",
            Slider(180, 1200, 10, () => w.Width, v => w.Width = v)));
        _content.Children.Add(Card("\uE7C4", "Altezza finestra", "In pixel.",
            Slider(120, 1400, 10, () => w.Height, v => w.Height = v)));
        _content.Children.Add(Card("\uE790", "Opacità pannello", "Da trasparente a pannello pieno.",
            Slider(0, 1, 0.05, () => c.PanelOpacity, v => { c.PanelOpacity = v; Apply(w); })));

        SubTitle("Grafici");
        _content.Children.Add(Card("\uE9D2", "Stile", "Come viene disegnato l'andamento.",
            Combo(GraphStyles, GraphStyleNames, () => c.GraphStyleName, v => { c.GraphStyleName = v; Apply(w); })));
        _content.Children.Add(Card("\uE916", "Durata", "Quanti secondi mostrare.",
            Combo(Durations, DurationNames, () => c.GraphSeconds.ToString(), v => { c.GraphSeconds = int.Parse(v); Apply(w); })));
        _content.Children.Add(Card("\uE895", "Aggiornamento", "Intervallo di campionamento.",
            Combo(Intervals, IntervalNames, () => Num(c.IntervalSeconds), v =>
            {
                c.IntervalSeconds = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
                Apply(w);
            })));
        _content.Children.Add(Card("\uE8A9", "Larghezza grafici", "Rapporto rispetto ai testi.",
            Slider(0.3, 4, 0.1, () => c.GraphWidthScale, v => { c.GraphWidthScale = v; Apply(w); })));
        _content.Children.Add(Card("\uE8A9", "Altezza grafici", "Altezza dei mini-grafici.",
            Slider(0.4, 3, 0.1, () => c.GraphHeightScale, v => { c.GraphHeightScale = v; Apply(w); })));

        SubTitle("Colori dei misuratori");
        foreach (var (key, name, glyph) in Meters)
            _content.Children.Add(Card(glyph, name, "Automatico = verde/ambra/rosso in base al valore.",
                ColorPicker(key, w)));

        SubTitle("Comportamento");
        _content.Children.Add(Card("\uE840", "Sempre in primo piano", "Resta sopra le altre finestre.",
            Switch(() => c.Topmost, v => { c.Topmost = v; Apply(w); })));
        _content.Children.Add(Card("\uE72E", "Blocca posizione", "Impedisce spostamento e ridimensionamento.",
            Switch(() => c.Locked, v => { c.Locked = v; Apply(w); })));
        _content.Children.Add(Card("\uE8F4", "HUD: clic attraverso", "Il widget non riceve più i clic (Ctrl+Alt+H per uscire).",
            Switch(() => c.ClickThrough, v => { c.ClickThrough = v; w.SetClickThrough(v); Apply(w); })));
        _content.Children.Add(Card("\uE950", "Rendering CPU", "Soluzione se compare l'overlay FPS di NVIDIA.",
            Switch(() => c.SoftwareRender, v => { c.SoftwareRender = v; Apply(w); _app.ApplyRenderMode(); })));
        _content.Children.Add(Card("\uE7F4", "Monitor", "Su quale schermo posizionarlo.",
            MonitorButtons(w)));
    }

    void Add(string? preset)
    {
        _app.NewWidget(preset);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { Rebuild(); _page = _app.Widgets.Count; ShowPage(_page); }));
    }

    /// <summary>Modifiche live: si applicano subito, senza toccare la misura della finestra.</summary>
    static void Apply(MainWindow w) => w.ApplyConfig();

    // ---------- building blocks ----------

    void PageHeader(string title, string description)
    {
        _content.Children.Add(new TextBlock { Text = title, Style = (Style)Resources["HubPageTitle"] });
        _content.Children.Add(new TextBlock { Text = description, Style = (Style)Resources["HubPageDesc"] });
    }

    void SubTitle(string text) => _content.Children.Add(new TextBlock { Text = text, Style = (Style)Resources["HubSubTitle"] });

    FrameworkElement Card(string glyph, string title, string description, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 58 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock { Text = glyph, Style = (Style)Resources["HubGlyph"] };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        texts.Children.Add(new TextBlock { Text = title, Style = (Style)Resources["HubCardTitle"] });
        if (description.Length > 0)
            texts.Children.Add(new TextBlock { Text = description, Style = (Style)Resources["HubCardDesc"] });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 2);
        grid.Children.Add(control);

        return new Border
        {
            Style = (Style)Resources["HubCard"],
            Padding = new Thickness(16, 10, 16, 10),
            Child = grid,
        };
    }

    /// <summary>Card con il contenuto sotto il titolo (per chiavi e form).</summary>
    FrameworkElement CardFull(string glyph, string title, string description, FrameworkElement content)
    {
        var stack = new StackPanel();
        // griglia (e non StackPanel orizzontale): così la descrizione va a capo dentro la
        // card invece di allargarla oltre la finestra e ridursi a una lettera per riga
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new TextBlock { Text = glyph, Style = (Style)Resources["HubGlyph"] };
        Grid.SetColumn(icon, 0);
        head.Children.Add(icon);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, Style = (Style)Resources["HubCardTitle"] });
        if (description.Length > 0)
            texts.Children.Add(new TextBlock { Text = description, Style = (Style)Resources["HubCardDesc"] });
        Grid.SetColumn(texts, 1);
        head.Children.Add(texts);
        stack.Children.Add(head);
        content.Margin = new Thickness(22, 10, 0, 0);
        stack.Children.Add(content);
        return new Border
        {
            Style = (Style)Resources["HubCard"],
            Padding = new Thickness(16, 12, 16, 14),
            Child = stack,
        };
    }

    static FrameworkElement NewColumn(params UIElement[] items)
    {
        var sp = new StackPanel();
        foreach (var i in items) sp.Children.Add(i);
        return sp;
    }

    /// <summary>Etichetta a sinistra, controllo a destra.</summary>
    FrameworkElement Labelled(string label, FrameworkElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lab = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Resources["HubCardDesc"],
        };
        Grid.SetColumn(lab, 0);
        row.Children.Add(lab);
        control.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    (FrameworkElement Row, PasswordBox Box) KeyRow(string label, string value)
    {
        var box = new PasswordBox
        {
            Password = value,
            Width = 320,
            Style = (Style)Resources["HubPassword"],
        };
        return (Labelled(label, box), box);
    }

    /// <summary>Campo numerico con conferma su Invio o quando perde il fuoco.</summary>
    FrameworkElement NumberBox(Func<double> get, Action<double> set, string tooltip = "")
    {
        var box = new TextBox
        {
            Text = Num(get()),
            Width = 92,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Resources["HubTextBox"],
            ToolTip = tooltip,
        };
        void Commit()
        {
            string text = box.Text.Replace(',', '.').Trim();
            if (text.Length > 0 && double.TryParse(text, System.Globalization.NumberStyles.Float,
                                                  System.Globalization.CultureInfo.InvariantCulture, out double v))
                set(v);
            box.Text = Num(get());
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        box.LostFocus += (_, _) => Commit();
        return box;
    }

    void ToggleProvider(WidgetConfig c, string provider, bool on)
    {
        if (on) { if (!c.AiProviders.Contains(provider)) c.AiProviders.Add(provider); }
        else c.AiProviders.Remove(provider);
    }

    /// <summary>Nome del widget. Si salva su Invio o uscendo dal campo; l'elenco a sinistra
    /// si aggiorna dopo, così il clic sul controllo successivo non si perde.</summary>
    FrameworkElement NameBox(WidgetConfig c)
    {
        var box = new TextBox
        {
            Text = c.Name,
            Width = 176,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Resources["HubTextBox"],
        };
        void Commit()
        {
            if (c.Name == box.Text.Trim()) return;
            c.Name = box.Text.Trim();
            c.Save();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Rebuild));
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        box.LostFocus += (_, _) => Commit();
        return box;
    }

    /// <summary>Budget mensile (USD) usato dal widget per il "budget rimasto". 0 = non impostato.</summary>
    FrameworkElement BudgetBox(WidgetConfig c, string provider, MainWindow w)
        => NumberBox(() => c.AiBudget(provider),
                     v => { c.AiBudgets[provider] = Math.Clamp(v, 0, 1e6); Apply(w); },
                     "Importo mensile in USD che vuoi tenere sotto controllo (0 = non impostato). Il widget mostra budget rimasto = budget − speso del mese.");

    async Task CheckUpdates(TextBlock status)
    {
        var keys = AiKeys.Load();
        status.Text = "Controllo in corso…";
        var info = await Updater.CheckAsync(keys.GitHub);
        if (info == null)
        {
            status.Text = keys.GitHub.Length == 0
                ? "Serve un token GitHub per leggere le release della repo privata."
                : $"Nessuna release trovata (versione installata {Updater.CurrentText}).";
            return;
        }
        if (!Updater.IsNewer(info))
        {
            status.Text = $"Sei aggiornato: {Updater.CurrentText} (ultima release {info.Tag}).";
            return;
        }
        status.Text = $"Nuova versione {info.Tag} disponibile.";
        UpdateWindow.Start(info, keys.GitHub);
    }

    FrameworkElement Expander(string glyph, string title, string description,
                              params (string Glyph, string Title, FrameworkElement Control)[] rows)
    {
        var body = new StackPanel
        {
            Margin = new Thickness(14, 2, 0, 8),
            Visibility = Visibility.Collapsed,
        };
        foreach (var r in rows) body.Children.Add(Card(r.Glyph, r.Title, "", r.Control));

        var chevron = IconButton("\uE70D", "Espandi", () => { });
        chevron.Click += (_, _) =>
        {
            body.Visibility = body.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            chevron.Content = body.Visibility == Visibility.Visible ? "\uE70E" : "\uE70D";
        };

        var stack = new StackPanel();
        stack.Children.Add(Card(glyph, title, description, chevron));
        stack.Children.Add(body);
        return stack;
    }

    FrameworkElement Switch(Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox { IsChecked = get(), Style = (Style)Resources["HubToggle"] };
        cb.Click += (_, _) => set(cb.IsChecked == true);
        return cb;
    }

    FrameworkElement SwitchRow(string label, Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox { Content = label, IsChecked = get(), Style = (Style)Resources["HubToggle"] };
        cb.Click += (_, _) => set(cb.IsChecked == true);
        return cb;
    }

    /// <summary>One row per element with up/down buttons; the order is stored per widget.</summary>
    IEnumerable<(string Glyph, string Title, FrameworkElement Control)> OrderRows(MainWindow w)
    {
        var c = w.Config;
        foreach (var key in c.EffectiveOrder())
        {
            var name = Meters.FirstOrDefault(m => m.Key == key).Name ?? key;
            var glyph = Meters.FirstOrDefault(m => m.Key == key).Glyph ?? "\uE9D9";
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var up = new Button { Content = "\uE70E", Style = (Style)Resources["HubIconButton"], Margin = new Thickness(0, 0, 6, 0), ToolTip = "Sposta su" };
            var down = new Button { Content = "\uE70D", Style = (Style)Resources["HubIconButton"], ToolTip = "Sposta giù" };
            string k = key;
            up.Click += (_, _) => { c.MoveElement(k, -1); Apply(w); Rebuild(); };
            down.Click += (_, _) => { c.MoveElement(k, +1); Apply(w); Rebuild(); };
            row.Children.Add(up);
            row.Children.Add(down);
            yield return (glyph, name, row);
        }
    }

    FrameworkElement Slider(double min, double max, double step, Func<double> get, Action<double> set)
    {
        var value = new TextBox
        {
            Text = get().ToString("0.##"),
            Width = 64,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Resources["HubTextBox"],
            ToolTip = "Scrivi un valore e premi Invio",
        };
        var sl = new Slider
        {
            Minimum = min,
            Maximum = max,
            SmallChange = step,
            LargeChange = step * 4,
            Value = Math.Clamp(get(), min, max),
            Width = 220,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Resources["HubSlider"],
        };
        sl.ValueChanged += (_, e) =>
        {
            if (!value.IsFocused) value.Text = e.NewValue.ToString("0.##");
            set(Math.Round(e.NewValue, 3));
        };
        void Commit()
        {
            string text = value.Text.Replace(',', '.').Trim();
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                v = Math.Clamp(v, min, max);
                value.Text = v.ToString("0.##");
                sl.Value = v;
            }
            else value.Text = get().ToString("0.##");
        }
        value.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        value.LostFocus += (_, _) => Commit();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(sl);
        row.Children.Add(value);
        return row;
    }

    FrameworkElement Combo(string[] values, string[] names, Func<string> get, Action<string> set)
    {
        var combo = new ComboBox { Style = (Style)Resources["HubCombo"], MinWidth = 170 };
        for (int i = 0; i < values.Length; i++) combo.Items.Add(new ComboBoxItem { Content = names[i], Tag = values[i] });
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is string v) set(v);
        };
        string current = get();
        int index = Array.IndexOf(values, current);
        combo.SelectedIndex = index >= 0 ? index : 0;
        return combo;
    }

    Button TextButton(string text, Action act, bool accent = false)
    {
        var b = new Button
        {
            Content = text,
            Style = (Style)Resources[accent ? "HubAccentButton" : "HubButton"],
            MinWidth = 92,
        };
        b.Click += (_, _) => act();
        return b;
    }

    FrameworkElement ColorPicker(string key, MainWindow w)
    {
        var c = w.Config;
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var auto = TextButton("Automatico", () => { c.Colors.Remove(key); Apply(w); });
        auto.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(auto);
        foreach (var hex in Swatches)
        {
            var chip = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 5, 0),
                ToolTip = hex,
                Style = (Style)Resources["HubIconButton"],
            };
            chip.Content = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = WidgetView.BrushFromHex(hex),
            };
            chip.Click += (_, _) => { c.Colors[key] = hex; Apply(w); };
            row.Children.Add(chip);
        }
        // inserimento manuale del codice colore
        var hexBox = new TextBox
        {
            Width = 92,
            Text = c.ColorOf(key) == "auto" ? "auto" : c.ColorOf(key),
            Style = (Style)Resources["HubTextBox"],
            Margin = new Thickness(4, 0, 6, 0),
            ToolTip = "Codice colore esadecimale, es. #4CC2FF (oppure “auto”)",
            VerticalAlignment = VerticalAlignment.Center,
        };
        void CommitHex()
        {
            string text = hexBox.Text.Trim();
            if (text.Length == 0 || text.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                c.Colors.Remove(key);
                Apply(w);
                return;
            }
            if (!text.StartsWith('#')) text = "#" + text;
            if (text.Length == 7 && text.Skip(1).All(Uri.IsHexDigit))
            {
                c.Colors[key] = text.ToUpperInvariant();
                Apply(w);
            }
            else hexBox.Text = c.ColorOf(key) == "auto" ? "auto" : c.ColorOf(key);
        }
        hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitHex(); };
        hexBox.LostFocus += (_, _) => CommitHex();
        row.Children.Add(hexBox);
        return row;
    }

    FrameworkElement MonitorButtons(MainWindow w)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            string device = screens[i].DeviceName;
            string label = $"Monitor {i + 1}" + (screens[i].Primary ? " ★" : "");
            var b = TextButton(label, () => w.MoveToMonitorPublic(device));
            b.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(b);
        }
        return row;
    }

    static string LayoutName(string layout)
    {
        int i = Array.IndexOf(Layouts, layout);
        return i >= 0 ? LayoutNames[i] : layout;
    }

    static string Num(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
