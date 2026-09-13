using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace HWWidget;

/// <summary>Finestra di aggiornamento: chiede conferma, scarica con barra di avanzamento
/// e lancia l'installer in modalità aggiornamento.</summary>
sealed class UpdateWindow : Window
{
    readonly UpdateInfo _info;
    readonly string _token;
    readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Value = 0, Width = 380 };
    readonly TextBlock _status = new() { Margin = new Thickness(0, 10, 0, 0), Opacity = 0.8 };
    readonly CancellationTokenSource _cancel = new();

    UpdateWindow(UpdateInfo info, string token)
    {
        _info = info;
        _token = token;
        Title = "Aggiornamento HW Widget";
        Width = 460;
        Height = 220;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 12.5;
        Background = new SolidColorBrush(Color.FromArgb(0x01, 0x12, 0x12, 0x16));
        Foreground = new SolidColorBrush(Colors.White);
        Resources = HubTheme.Create();

        var icon = AppIcon.Wpf();
        if (icon != null) Icon = icon;

        _bar.Style = (Style)Resources["HubProgress"];
        _status.Style = (Style)Resources["HubCardDesc"];

        var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        panel.Children.Add(new TextBlock
        {
            Text = $"HW Widget {_info.Tag}",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"È disponibile una nuova versione ({_info.Size / 1048576.0:0} MB). L'applicazione verrà aggiornata e riavviata.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 6, 0, 16),
        });
        panel.Children.Add(_bar);
        panel.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Annulla", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Style = (Style)Resources["HubButton"];
        cancel.Click += (_, _) => { _cancel.Cancel(); Close(); };
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x1E, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = panel,
        };
        border.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) { try { DragMove(); } catch { } } };
        Content = border;

        Loaded += (_, _) => Backdrop.Apply(new WindowInteropHelper(this).Handle, "blur", Palette.For("dark"));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
    }

    /// <summary>Chiede conferma e, se accettata, esegue l'aggiornamento.</summary>
    public static void Start(UpdateInfo info, string token)
    {
        var answer = MessageBox.Show(
            $"Nuova versione {info.Tag} disponibile ({info.Size / 1048576.0:0} MB).\n\nScaricare e installare adesso?",
            "Aggiornamento HW Widget", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var window = new UpdateWindow(info, token);
        window.Show();
        _ = window.RunAsync();
    }

    async Task RunAsync()
    {
        try
        {
            _status.Text = "Download in corso…";
            var progress = new Progress<double>(p =>
            {
                _bar.Value = p;
                _status.Text = $"Download in corso… {p:P0}";
            });
            string path = await Updater.DownloadAsync(_info, _token, progress, _cancel.Token);
            _status.Text = "Installazione e riavvio…";
            _bar.Value = 1;
            await Task.Delay(600);
            Updater.RunInstaller(path);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _status.Text = "Aggiornamento non riuscito: " + ex.Message;
        }
    }
}
