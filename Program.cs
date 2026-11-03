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
        if (args.Length > 0 && args[0] == "--dstest") { SelfTest.DeepSeekTest(); return; }
        if (args.Length > 0 && args[0] == "--dslogin-test") { SelfTest.DsLoginTest(); return; }
        // aggiornamento senza interfaccia: prova il giro completo release → installazione
        if (args.Contains("--update-silent"))
        {
            Updater.SilentUpdate(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hwwidget-update.txt"));
            return;
        }

        // l'evento esiste anche per il secondo processo: gli permette di chiedere all'istanza
        // già attiva di mostrare l'hub (la scorciatoia HUB altrimenti non farebbe nulla)
        using var showHubEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HWWidget.ShowHub");
        using var mutex = new Mutex(true, @"Local\HWWidget.SingleInstance", out bool first);
        if (!first)
        {
            try { showHubEvent.Set(); } catch { }
            return;
        }

        // no main window: the app lives in the tray and can hold several widgets
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var controller = new AppController();
        controller.Start();
        var listener = new Thread(() =>
        {
            while (showHubEvent.WaitOne())
            {
                try { app.Dispatcher.BeginInvoke(new Action(controller.ShowHub)); }
                catch { }   // dispatcher già chiuso
            }
        }) { IsBackground = true, Name = "HWWidget.ShowHub" };
        listener.Start();
        if (args.Contains("--hub")) controller.ShowHub();   // shortcut/HUB diretto
        app.Run();
        controller.Dispose();
    }
}
