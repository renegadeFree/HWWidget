using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace HWWidget;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest") { SelfTest.Run(); return; }
        if (args.Length > 0 && args[0] == "--updatetest") { SelfTest.UpdateDownloadTest(); return; }
        // aggiornamento senza interfaccia: prova il giro completo release → installazione
        if (args.Contains("--update-silent"))
        {
            Updater.SilentUpdate(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hwwidget-update.txt"));
            return;
        }

        using var mutex = new Mutex(true, @"Local\HWWidget.SingleInstance", out bool first);
        if (!first) return;

        // no main window: the app lives in the tray and can hold several widgets
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var controller = new AppController();
        controller.Start();
        if (args.Contains("--hub")) controller.ShowHub();   // shortcut/HUB diretto
        app.Run();
        controller.Dispose();
    }
}
