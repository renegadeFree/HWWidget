using System;
using System.Linq;
using System.Windows;

namespace HWWidget.Setup;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--uninstall"))
        {
            Installer.Uninstall(silent: true);
            return;
        }

        // used by the build self-check: install without UI and without touching autostart
        if (args.Contains("--install-silent"))
        {
            Installer.Install(false, false, false, out string report);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hwwidget-setup.txt"), report);
            return;
        }

        if (args.Contains("--update"))
        {
            Installer.Update();
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new SetupWindow());
    }
}
