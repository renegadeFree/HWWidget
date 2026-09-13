using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace HWWidget.Setup;

/// <summary>Small installer window: dark, rounded, no dependencies.</summary>
sealed class SetupWindow : Window
{
    readonly CheckBox _startup = new() { Content = "Avvia con Windows", IsChecked = true };
    readonly CheckBox _desktop = new() { Content = "Crea un collegamento sul desktop", IsChecked = true };
    readonly CheckBox _copySettings = new() { Content = "Copia la configurazione dei widget di questo PC", IsChecked = true };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Margin = new Thickness(0, 10, 0, 0) };
    readonly Button _install = new();
    readonly Button _uninstall = new();
    readonly Button _launch = new();

    public SetupWindow()
    {
        Title = "Installa HW Widget";
        Width = 520;
        Height = 430;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
        Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF4));
        Content = Build();
        Refresh();
    }

    FrameworkElement Build()
    {
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        root.Children.Add(new TextBlock
        {
            Text = "HW Widget",
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
        });
        root.Children.Add(new TextBlock
        {
            Text = "Widget di monitoraggio (CPU, GPU, RAM, disco, rete) in stile acrylic/Mica per Windows 11.\n" +
                   "Installazione per l'utente corrente: non serve l'amministratore.",
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 14),
        });

        root.Children.Add(Card(_startup, _desktop, _copySettings));

        _install.Content = "Installa";
        _install.Padding = new Thickness(18, 8, 18, 8);
        _install.Margin = new Thickness(0, 14, 8, 0);
        _install.Click += (_, _) => DoInstall();

        _launch.Content = "Avvia HW Widget";
        _launch.Padding = new Thickness(18, 8, 18, 8);
        _launch.Margin = new Thickness(0, 14, 8, 0);
        _launch.IsEnabled = Installer.Installed;
        _launch.Click += (_, _) => Launch();

        _uninstall.Content = "Disinstalla";
        _uninstall.Padding = new Thickness(18, 8, 18, 8);
        _uninstall.Margin = new Thickness(0, 14, 0, 0);
        _uninstall.Click += (_, _) =>
        {
            Installer.Uninstall(silent: false);
            Refresh();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_install);
        buttons.Children.Add(_launch);
        buttons.Children.Add(_uninstall);
        root.Children.Add(buttons);
        root.Children.Add(_status);
        return root;
    }

    static Border Card(params UIElement[] items)
    {
        var sp = new StackPanel();
        foreach (var i in items) sp.Children.Add(i);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Child = sp,
        };
    }

    void Refresh()
    {
        bool installed = Installer.Installed;
        _uninstall.IsEnabled = installed;
        _launch.IsEnabled = installed;
        _status.Text = installed
            ? $"HW Widget risulta installato in:\n{Installer.TargetDir}"
            : "HW Widget non è installato su questo PC.";
    }

    void DoInstall()
    {
        try
        {
            Installer.Install(_startup.IsChecked == true, _desktop.IsChecked == true, _copySettings.IsChecked == true, out string report);
            Refresh();
            _status.Text += "\n\n" + report;
            var answer = MessageBox.Show(report + "\n\nAvviare HW Widget adesso?", "Installazione completata",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes) Launch();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Installazione non riuscita:\n" + ex.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    static void Launch()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Installer.TargetExe,
                WorkingDirectory = Installer.TargetDir,
                UseShellExecute = true,
            });
        }
        catch { }
    }
}
