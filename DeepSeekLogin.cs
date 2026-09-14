using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HWWidget;

/// <summary>Accesso a platform.deepseek.com dentro l'app per prendere il "token di utilizzo".
/// Come il monitor di riferimento: la pagina viene aperta in una WebView con uno script che
/// legge localStorage.userToken e, se serve, intercetta l'header Authorization delle chiamate
/// fetch/XHR; il token viene poi verificato sull'endpoint di uso e salvato con DPAPI.
/// La cartella dati è fissa, quindi l'accesso resta valido anche alle aperture successive.</summary>
sealed class DeepSeekLogin : Window
{
    const string Prefix = "DSM_USAGE_TOKEN:";

    readonly WebView2 _web = new();
    readonly TextBlock _status = new();
    string _last = "";
    bool _saved;

    public DeepSeekLogin()
    {
        Title = "DeepSeek — token di utilizzo";
        Width = 1020;
        Height = 740;
        MinWidth = 620;
        MinHeight = 420;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 12.5;
        Background = new SolidColorBrush(Color.FromArgb(0x01, 0x12, 0x12, 0x16));
        Foreground = new SolidColorBrush(Colors.White);
        Resources = HubTheme.Create();
        var icon = AppIcon.Wpf();
        if (icon != null) Icon = icon;

        Content = BuildShell();
        _status.Style = (Style)Resources["HubCardDesc"];

        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => { try { _web.Dispose(); } catch { } };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
        Backdrop.Apply(new WindowInteropHelper(this).Handle, "blur", Palette.For("dark"));
    }

    FrameworkElement BuildShell()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // barra del titolo (si trascina)
        var titleBar = new Grid { Height = 42, Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)) };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.Children.Add(new TextBlock
        {
            Text = "\uE9D2",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = (Brush)Resources["Accent"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        title.Children.Add(new TextBlock { Text = "Token di utilizzo DeepSeek", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(title, 0);
        titleBar.Children.Add(title);
        var close = new Button { Content = "\uE8BB", ToolTip = "Chiudi", Style = (Style)Resources["HubIconButton"], Margin = new Thickness(0, 0, 6, 0) };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };
        Grid.SetRow(titleBar, 0);
        grid.Children.Add(titleBar);

        var info = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        info.Children.Add(new TextBlock
        {
            Text = "Accedi a DeepSeek qui sotto (o usa un profilo già collegato): il token viene preso e verificato da solo.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
        });
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var retry = new Button { Content = "Rileggi il token ora", Style = (Style)Resources["HubAccentButton"], Margin = new Thickness(0, 0, 8, 0) };
        retry.Click += async (_, _) => await TryLocalStorageAsync();
        buttons.Children.Add(retry);
        var manual = new Button { Content = "Metodo manuale", Style = (Style)Resources["HubButton"] };
        manual.Click += (_, _) => MessageBox.Show(
            "Apri platform.deepseek.com nel browser e accedi, poi premi F12 → Console, incolla\n\n" +
            "JSON.parse(localStorage.userToken).value\n\n" +
            "e copia la stringa restituita (è lunga, inizia con eyJ). Incollala in Hub → Impostazioni app → " +
            "Chiavi → \"Token di utilizzo DeepSeek\" e premi Verifica.",
            "Token di utilizzo DeepSeek", MessageBoxButton.OK, MessageBoxImage.Information);
        buttons.Children.Add(manual);
        info.Children.Add(buttons);
        _status.Margin = new Thickness(0, 8, 0, 0);
        info.Children.Add(_status);
        Grid.SetRow(info, 1);
        grid.Children.Add(info);

        Grid.SetRow(_web, 2);
        grid.Children.Add(_web);

        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x10, 0x10, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = grid,
        };
    }

    async Task StartAsync()
    {
        try
        {
            // profilo fisso: il login resta salvato fra un'apertura e l'altra
            string folder = System.IO.Path.Combine(WidgetConfig.Dir, "webview");
            var env = await CoreWebView2Environment.CreateAsync(null, folder);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(Hook);
            core.WebMessageReceived += (_, e) =>
            {
                try { Received(e.TryGetWebMessageAsString()); } catch { }
            };
            core.DocumentTitleChanged += (_, _) =>
            {
                if (core.DocumentTitle.StartsWith(Prefix, StringComparison.Ordinal)) Received(core.DocumentTitle);
            };
            core.NavigationCompleted += async (_, _) => await TryLocalStorageAsync();
            _web.Source = new Uri("https://platform.deepseek.com/");
            Status("Apri l'accesso se serve: il token viene letto automaticamente.");
        }
        catch (Exception ex)
        {
            Status("WebView2 non disponibile (" + ex.Message + "): usa il metodo manuale (pulsante qui sopra).");
        }
    }

    /// <summary>Legge localStorage.userToken dalla pagina già aperta.</summary>
    async Task TryLocalStorageAsync()
    {
        if (_saved || _web.CoreWebView2 == null) return;
        try
        {
            string value = await _web.CoreWebView2.ExecuteScriptAsync(
                "(function(){try{var r=localStorage.getItem('userToken');if(!r)return '';var o=JSON.parse(r);return (o&&o.value)||'';}catch(e){return '';}})()");
            if (!string.IsNullOrEmpty(value) && value != "\"\"") Received(value.Trim('"'));
            else Status("Token non ancora disponibile: fai l'accesso nella pagina, poi premi “Rileggi il token ora”.");
        }
        catch { }
    }

    void Received(string raw)
    {
        if (_saved || raw.Length == 0) return;
        string token = AiKeys.Normalize(raw.StartsWith(Prefix, StringComparison.Ordinal) ? raw[Prefix.Length..] : raw);
        if (token.Length < 40 || token == _last) return;
        _last = token;
        _ = VerifyAsync(token);
    }

    async Task VerifyAsync(string token)
    {
        Status("Token trovato, verifica in corso…");
        var keys = AiKeys.Load();
        var r = await DeepSeekUsage.FetchAsync(keys.DeepSeek, token);
        if (!r.UsageOk)
        {
            Status("Token non valido: " + (r.Status.Length > 0 ? r.Status : "l'API non lo accetta"));
            return;
        }
        keys.DeepSeekUsage = token;
        keys.Save();
        _saved = true;
        Status($"✓ token salvato · saldo {r.Balance} · {r.Models.Count} modelli · mese {r.MonthCost} · {r.Days.Count} giorni");
        _ = SensorHub.RefreshAiAsync();
        await Task.Delay(1500);
        Close();
    }

    void Status(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => _status.Text = text);
            return;
        }
        _status.Text = text;
    }

    /// <summary>Diagnostica (--dslogin-test): simula l'arrivo del token dallo script.</summary>
    internal void FeedForTest(string raw) => Received(raw);
    internal string StatusText => _status.Text;

    /// <summary>Hook su fetch/XHR + lettura periodica di localStorage (come il riferimento).</summary>
    const string Hook = """
(function () {
  if (window.__dsHook) return; window.__dsHook = true;
  var prefix = 'DSM_USAGE_TOKEN:';
  function grab(t) {
    if (!t || t.length < 40) return;
    try { document.title = prefix + t; } catch (e) {}
    try { window.chrome.webview.postMessage(prefix + t); } catch (e) {}
  }
  function authOf(h) {
    if (!h) return null;
    try {
      if (typeof h.get === 'function') return h.get('Authorization') || h.get('authorization');
      if (Array.isArray(h)) {
        for (var i = 0; i < h.length; i++) if (String(h[i][0]).toLowerCase() === 'authorization') return h[i][1];
        return null;
      }
      for (var k in h) if (String(k).toLowerCase() === 'authorization') return h[k];
    } catch (e) {}
    return null;
  }
  function take(v) { if (typeof v === 'string' && v.indexOf('Bearer ') === 0) grab(v.slice(7)); }
  var of = window.fetch;
  if (of) window.fetch = function (input, init) {
    try { take(authOf(init && init.headers) || authOf(input && input.headers)); } catch (e) {}
    return of.apply(this, arguments);
  };
  var os = XMLHttpRequest.prototype.setRequestHeader;
  XMLHttpRequest.prototype.setRequestHeader = function (k, v) {
    try { if (String(k).toLowerCase() === 'authorization') take(v); } catch (e) {}
    return os.apply(this, arguments);
  };
  function ls() { try { var r = localStorage.getItem('userToken'); if (!r) return null; var o = JSON.parse(r); return o && o.value; } catch (e) { return null; } }
  var t0 = ls(); if (t0) grab(t0);
  setInterval(function () { var t = ls(); if (t) grab(t); }, 2500);
})();
""";
}
