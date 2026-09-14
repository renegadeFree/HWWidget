using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace HWWidget;

/// <summary>Runnable sanity check for samplers, graphs, config and autostart:
/// HWWidget.exe --selftest</summary>
static class SelfTest
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    static readonly StringBuilder Log = new();

    public static void Run()
    {
        AttachConsole(-1);
        try
        {
            var cpu = new CpuSampler(); cpu.LoadStatic(); cpu.Prime();
            var ram = new RamSampler(); ram.LoadStatic(); ram.Sample();
            var net = new NetSampler();
            var disk = new DiskSampler(); disk.Open();
            using var gpu = new GpuSampler(); bool gpuOk = gpu.Open(0);

            System.Threading.Thread.Sleep(1100);
            cpu.Sample(); ram.Sample(); net.Sample(); gpu.Sample(); disk.Sample();
            System.Threading.Thread.Sleep(1100);
            disk.Sample();

            Say("cpu", $"{cpu.Name} | base={cpu.BaseMhz}MHz cur={cpu.CurrentMhz}MHz | usage={cpu.Usage:0.0}%");
            Say("ram", $"{ram.UsedGb:0.0}/{ram.TotalGb:0.0} GB ({ram.UsagePct:0.0}%) | {ram.SpeedText}");
            Say("net", $"{net.AdapterName} | ↓{net.DownBps / 1048576:0.00} MB/s ↑{net.UpBps / 1048576:0.00} MB/s | link {net.LinkBps / 1e6:0} Mbps");
            Say("gpu", gpuOk
                ? $"{gpu.Name} | util={gpu.Util}% vram={gpu.VramUsedGb:0.0}/{gpu.VramTotalGb:0.0}GB {gpu.MemUtil}% | {gpu.Watts:0.0}/{gpu.WattsLimit:0}W | {gpu.TempC:0}C {gpu.ClockMhz:0}MHz"
                : "NVML non disponibile");
            Say("disco", disk.Available
                ? $"lettura {disk.ReadBps / 1048576:0.00} MB/s · scrittura {disk.WriteBps / 1048576:0.00} MB/s"
                : "contatori disco non disponibili");

            Check(cpu.Usage is >= 0 and <= 100, "cpu usage fuori range");
            Check(cpu.BaseMhz > 0, "clock CPU non letto");
            Check(ram.TotalGb > 1 && ram.UsedGb <= ram.TotalGb, "RAM incoerente");
            Check(net.DownBps >= 0 && net.UpBps >= 0, "throughput negativo");
            if (disk.Available)
                Check(disk.ReadBps >= 0 && disk.WriteBps >= 0 && disk.ReadBps < 5e9, "valori disco implausibili");

            // guards the hand-written MIB_IF_ROW2 layout: totals must be >= the IPv4-only subset
            ulong total = 0;
            foreach (var r in NetSampler.Read()) total += r.rx + r.tx;
            ulong v4 = 0;
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                    or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
                if (ni.Name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)) continue;
                try { var st = ni.GetIPStatistics(); v4 += (ulong)st.BytesReceived + (ulong)st.BytesSent; } catch { }
            }
            Say("net-counters", $"GetIfTable2={total / 1e6:0.0} MB | sottoinsieme IPv4={v4 / 1e6:0.0} MB");
            // the two reads happen a moment apart, so live traffic can make the second
            // slightly larger: allow a small slack while still catching a broken layout
            Check(total + 8_000_000 >= v4, "contatori GetIfTable2 < IPv4: struct MIB_IF_ROW2 disallineata");
            Say("net-struct", $"rowSize={NetSampler.RowSize}");
            Check(NetSampler.RowSize == 1352, $"dimensione MIB_IF_ROW2 inattesa: {NetSampler.RowSize}");

            if (gpuOk)
            {
                Check(gpu.Util <= 100 && gpu.MemUtil <= 100, "util GPU fuori range");
                Check(gpu.VramUsedGb <= gpu.VramTotalGb && gpu.VramTotalGb > 1, "VRAM incoerente");
                Check(gpu.Watts is > 0 and < 2000, "watt GPU implausibili");
            }

            // ring buffer: capacity wraps and the visible window slides correctly
            foreach (GraphStyle style in Enum.GetValues<GraphStyle>())
            {
                var sp = new Sparkline { WindowSamples = 60, VMax = 0, GraphStyle = style };
                for (int i = 0; i < 1500; i++) sp.Push(i);
                Check(sp.Count == 1200, $"ring buffer ({style}): attesi 1200 campioni, trovati {sp.Count}");
                Check(sp.ProbeLast() == 1499, $"ultimo campione errato ({style})");
                Check(sp.ProbeOldest(60) == 1440, $"finestra visibile errata ({style})");
            }
            Say("grafici", "1200 campioni, finestra scorrevole OK su 4 stili (area, linea, barre, scalini)");

            // config: a broken file must be repaired, and element presets must stay valid
            var broken = new WidgetConfig
            {
                PanelOpacity = 5, TextScale = 40, RowScale = 0.01, GraphWidthScale = 99,
                IntervalSeconds = 5, GraphSeconds = 7, Backdrop = "boh", Theme = "x", Layout = "y",
                ShowNet = false, ShowCpu = false, ShowGpu = false, ShowRam = false, ShowDisk = false,
            }.Sanitized();
            Check(broken.PanelOpacity <= 1 && broken.TextScale <= 2.2 && broken.RowScale >= 0.6, "clamp scale fallito");
            Check(broken.GraphWidthScale is > 0 and <= 4, "clamp larghezza grafici fallito");
            Check(broken.IntervalSeconds == 1 && broken.GraphSeconds == 60, "valori di default non ripristinati");
            Check(broken.Backdrop == "blur" && broken.Theme == "system" && broken.Layout == "rows", "stringhe non valide non sanate");
            Check(new WidgetConfig { Backdrop = "blur" }.Sanitized().Backdrop == "blur", "backdrop blur non accettato");
            Check(broken.Elements.Any(), "nessun elemento visibile dopo la sanificazione");
            Say("config", $"sanificata: opacity={broken.PanelOpacity} text={broken.TextScale:0.0} righe={broken.RowScale:0.0} larghezzaGrafici={broken.GraphWidthScale:0.0} intervallo={broken.IntervalSeconds}s");

            var onlyGpu = new WidgetConfig { ShowNet = false, ShowCpu = false, ShowGpu = true, ShowRam = false, ShowDisk = false };
            Check(onlyGpu.Elements.SequenceEqual(new[] { "gpu" }), "preset solo GPU errato");
            var two = new WidgetConfig { ShowNet = false, ShowCpu = true, ShowGpu = true, ShowRam = false, ShowDisk = false };
            Check(two.Elements.SequenceEqual(new[] { "cpu", "gpu" }), "preset CPU+GPU errato");
            var all = new WidgetConfig();
            Check(all.Elements.SequenceEqual(new[] { "net", "cpu", "gpu", "ram", "disk" }), "preset Tutto errato");
            Check(new WidgetConfig { Layout = "panel" }.Sanitized().Layout == "panel", "layout pannello non accettato");
            Check(new WidgetConfig { Layout = "panelgraph" }.Sanitized().Layout == "panelgraph", "layout pannello+grafici non accettato");
            Say("preset", $"solo GPU = [{string.Join(",", onlyGpu.Elements)}] · CPU+GPU = [{string.Join(",", two.Elements)}] · tutto = [{string.Join(",", all.Elements)}]");

            // layouts must build and produce binding targets for every metric row
            foreach (string layout in new[] { "rows", "cards", "tiles", "panel", "panelgraph" })
            {
                var cfg = new WidgetConfig { Layout = layout };
                int elements = cfg.Elements.Count();
                int rows = cfg.Elements.Sum(el => WidgetView.LineCount(cfg, el));
                var view = new WidgetView(cfg, Palette.For("dark"), new Dictionary<string, Series>(), 300);
                view.Bind(new Metrics { GpuOk = true, NetLink = 1e9, RamTotal = 64, VramTotal = 12, DiskOk = true, DiskRead = 1e6, DiskWrite = 2e6 });
                // rows/cards/tiles bind once per element; the panel layouts bind once per metric row
                int expect = layout is "panel" or "panelgraph"
                    ? cfg.Elements.Sum(el => el == "gpu" ? 3 : 1)
                    : elements;
                Check(view.BoundCount == expect, $"layout {layout}: bind {view.BoundCount} != attesi {expect}");
                Check(rows >= elements, $"layout {layout}: conteggio righe incoerente");
                view.Root.Measure(new System.Windows.Size(274, double.PositiveInfinity));
                double wanted = view.Root.DesiredSize.Height;
                Check(wanted > 60, $"layout {layout}: contenuto troppo basso ({wanted:0})");
                Say("layout", $"{layout}: {elements} elementi, {rows} righe, {view.BoundCount} bind, contenuto {wanted:0} DIP a 300 di larghezza");
            }

            var light = Palette.For("light");
            var dark = Palette.For("dark");
            Check(light.Text != dark.Text, "palette chiaro/scuro identiche: il testo non seguirebbe il tema");
            Check(light.IsLight && !dark.IsLight, "flag tema errati");
            Say("palette", $"scuro testo=#{dark.Text.R:X2}{dark.Text.G:X2}{dark.Text.B:X2} chiaro testo=#{light.Text.R:X2}{light.Text.G:X2}{light.Text.B:X2}");

            // posizione e dimensioni: giro completo su finestre vere (salva → riapri → stesso rettangolo)
            WindowGeometryTest();

            // per-meter colours and the global scale
            var colored = new WidgetConfig
            {
                Colors = new Dictionary<string, string> { ["gpu"] = "#FF00FF", ["cpu"] = "non-un-colore" },
            }.Sanitized();
            Check(colored.ColorOf("gpu") == "#FF00FF", "colore fisso del misuratore perso");
            Check(colored.ColorOf("cpu") == "auto", "colore non valido non scartato");
            Check(colored.ColorOf("ram") == "auto", "misuratore senza colore non automatico");
            Check(new WidgetConfig { UiScale = 9 }.Sanitized().UiScale == 2.5, "clamp scala interfaccia fallito");
            Say("colori", $"gpu={colored.ColorOf("gpu")} cpu={colored.ColorOf("cpu")} ram={colored.ColorOf("ram")}");

            var at100 = new WidgetView(new WidgetConfig { Layout = "panel" }, Palette.For("dark"), new Dictionary<string, Series>(), 300);
            var at125 = new WidgetView(new WidgetConfig { Layout = "panel", UiScale = 1.25 }, Palette.For("dark"), new Dictionary<string, Series>(), 300);
            at100.Root.Measure(new System.Windows.Size(274, double.PositiveInfinity));
            at125.Root.Measure(new System.Windows.Size(274, double.PositiveInfinity));
            Say("scala", $"pannello 100% = {at100.Root.DesiredSize.Height:0} DIP · 125% = {at125.Root.DesiredSize.Height:0} DIP");
            Check(at125.Root.DesiredSize.Height > at100.Root.DesiredSize.Height * 1.1, "la scala generale non ingrandisce il contenuto");

            // "altezza elementi" (RowScale) deve distanziare le righe del pannello,
            // non solo ingrandire il testo
            var rows150 = new WidgetView(new WidgetConfig { Layout = "panel", RowScale = 1.5 },
                                         Palette.For("dark"), new Dictionary<string, Series>(), 300);
            rows150.Root.Measure(new System.Windows.Size(274, double.PositiveInfinity));
            double h100 = at100.Root.DesiredSize.Height, h150 = rows150.Root.DesiredSize.Height;
            Say("altezza-righe", $"pannello righe 100% = {h100:0} DIP · 150% = {h150:0} DIP (+{(h150 / h100 - 1) * 100:0}%)");
            Check(h150 > h100 * 1.08, "la scala delle righe non distanzia gli elementi");

            // sezione AI: solo testo (nessun grafico) e un fornitore spento sparisce dal widget
            var ai3 = AiView(new List<string> { "deepseek", "openai", "anthropic" });
            var ai1 = AiView(new List<string> { "deepseek" });
            ai3.Root.Measure(new System.Windows.Size(304, double.PositiveInfinity));
            ai1.Root.Measure(new System.Windows.Size(304, double.PositiveInfinity));
            Check(CountSparklines(ai3.Root) == 0, $"la sezione AI non deve avere grafici ({CountSparklines(ai3.Root)} trovati)");
            Check(ai3.BoundCount == 9 && ai1.BoundCount == 3, $"righe AI: {ai3.BoundCount} e {ai1.BoundCount} invece di 9 e 3");
            Check(ai3.Root.DesiredSize.Height > ai1.Root.DesiredSize.Height + 40,
                  "spegnere due fornitori non riduce il widget");
            Say("ai-widget", $"3 fornitori = {ai3.BoundCount} righe/{ai3.Root.DesiredSize.Height:0} DIP · " +
                             $"1 fornitore = {ai1.BoundCount} righe/{ai1.Root.DesiredSize.Height:0} DIP · grafici: {CountSparklines(ai3.Root)}");
            Check(CountSparklines(new WidgetView(new WidgetConfig { Layout = "panelgraph" }, Palette.For("dark"),
                                                new Dictionary<string, Series>(), 300).Root) > 0,
                  "il pannello con grafici non disegna più alcun grafico");

            // budget mensile: rimasto = budget − speso (e il budget vive nel file del widget)
            var budgetCfg = new WidgetConfig
            {
                Layout = "panelgraph", ShowNet = false, ShowCpu = false, ShowGpu = false, ShowRam = false,
                ShowDisk = false, ShowAi = true, AiProviders = new List<string> { "openai" },
                AiBudgets = new Dictionary<string, double> { ["openai"] = 25, ["anthropic"] = -3, ["boh"] = 10 },
            }.Sanitized();
            Check(budgetCfg.AiBudget("openai") == 25 && budgetCfg.AiBudget("anthropic") == 0 && budgetCfg.AiBudgets.Count == 1,
                  "budget AI non sanato");
            var budgetView = new WidgetView(budgetCfg, Palette.For("dark"), new Dictionary<string, Series>(), 330);
            budgetView.Bind(new Metrics { Ai = new AiSnapshot { OpenAiMonth = "12,00 $", OpenAiMonthValue = 12 } });
            var texts = new List<string>();
            CollectTexts(budgetView.Root, texts);
            Check(texts.Any(t => t.Contains("13,00")), "budget rimasto non calcolato: " + string.Join(" | ", texts));
            Say("ai-budget", $"budget 25 $ − speso 12 $ = {texts.First(t => t.Contains("13,00"))}");

            // --- DeepSeek: uso e spesa del mese (API interne della piattaforma) ---
            var ds = new DeepSeekUsage { Currency = "CNY", Available = true, Balance = "877,70 CNY", BalanceValue = 877.70 };
            DeepSeekUsage.Parse(DsAmountSample, DsCostSample, ds, new DateTime(2026, 9, 1));
            Check(ds.Models.Count == 2, $"modelli DeepSeek: {ds.Models.Count} invece di 2");
            Check(Math.Abs(ds.Models[0].Tokens - 325_000_000) < 1, $"token DeepSeek {ds.Models[0].Tokens}");
            Check(Math.Abs(ds.Models[0].Requests - 6384) < 1, $"richieste DeepSeek {ds.Models[0].Requests}");
            Check(Math.Abs(ds.Models[0].HitPct - 92.3) < 0.2, $"cache hit DeepSeek {ds.Models[0].HitPct:0.0}%");
            Check(Math.Abs(ds.MonthValue - 14.15) < 0.001, $"spesa del mese DeepSeek {ds.MonthValue}");
            Check(ds.Days.Count == 7 && Math.Abs(ds.Days[1].Hit - 2_000_000) < 1, "token per giorno DeepSeek");
            Check(Math.Abs(ds.Days[0].Cost - 1.50) < 0.001, $"costo del giorno DeepSeek {ds.Days[0].Cost}");
            Check(ds.Currency == "CNY" && ds.Available, "valuta/disponibilità DeepSeek");
            ds.TodayCost = "0,60 CNY";                       // solo per il disegno di controllo
            Say("deepseek", $"2 modelli ({string.Join(", ", ds.Models.Select(m => $"{m.Name} {AiUsage.Tokens(m.Tokens)} · {m.HitPct:0}% hit · {m.Cost:0.00}"))}) · " +
                           $"mese {ds.MonthValue:0.00} {ds.Currency} · {ds.Days.Count} giorni · saldo {ds.Balance}");

            // rendering della sezione DeepSeek per il controllo a occhio
            var dsCfg = new WidgetConfig
            {
                Layout = "panelgraph", ShowNet = false, ShowCpu = false, ShowGpu = false, ShowRam = false,
                ShowDisk = false, ShowAi = false, ShowDs = true,
            }.Sanitized();
            var dsView = new WidgetView(dsCfg, Palette.For("dark"), new Dictionary<string, Series>(), 340);
            dsView.Bind(new Metrics { Ai = new AiSnapshot { Deep = ds } });
            string png = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hwwidget-deepseek.png");
            SavePng(dsView, 340, png);
            Say("deepseek-render", $"sezione DeepSeek disegnata in {png} ({dsView.BoundCount} bind)");
            Check(HasRefreshButton(dsView.Root), "manca il pulsante di aggiornamento nella sezione DeepSeek");
            foreach (var sp in Sparklines(dsView.Root))
            {
                Check(Math.Abs(sp.ActualHeight - sp.Height) < 1, "il grafico DeepSeek non rispetta l'altezza richiesta");
                Check(sp.Count == 7, $"giorni nel grafico DeepSeek: {sp.Count} invece di 7");
                Say("deepseek-spark", $"grafico {sp.ActualWidth:0}×{sp.ActualHeight:0} con {sp.Count} giorni impilati");
            }

            // il pulsante rilegge davvero i dati (saldo dall'API)
            string firstBalance = SensorHub.Current.Ai.Deep.Balance;
            WidgetView.RefreshAll();
            for (int i = 0; i < 80 && SensorHub.Current.Ai.Deep.Balance == firstBalance; i++)
            {
                System.Threading.Thread.Sleep(250);
                Pump();
            }
            string afterBalance = SensorHub.Current.Ai.Deep.Balance;
            Say("aggiorna", $"saldo prima='{firstBalance}' dopo='{afterBalance}'");
            Check(afterBalance.Length > 0, "il pulsante di aggiornamento non ha letto il saldo DeepSeek");

            // pannello completo (per il controllo a occhio di spaziature e sfumature)
            var full = new WidgetView(new WidgetConfig { Layout = "panelgraph" }, Palette.For("dark"),
                                      new Dictionary<string, Series>(), 340);
            var rnd = new Random(7);
            for (int i = 0; i < 90; i++)
                full.Bind(new Metrics
                {
                    GpuOk = true, GpuUtil = 20 + rnd.Next(50), TempC = 45 + rnd.Next(15),
                    VramUsed = 3 + rnd.Next(6), VramTotal = 12, Watts = 180, WattsLimit = 370,
                    CpuUsage = 10 + rnd.Next(40), CpuMhz = 4401, CpuBaseMhz = 4401,
                    RamPct = 30 + rnd.Next(20), RamUsed = 24, RamTotal = 64, RamSpeed = "6000 MT/s",
                    DiskRead = 1e6 * rnd.Next(20), DiskWrite = 2e6 * rnd.Next(20),
                    NetDown = 1e6 * rnd.Next(60), NetUp = 1e5 * rnd.Next(50), NetLink = 1e9, NetName = "Ethernet",
                });
            SavePng(full, 340, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hwwidget-panel.png"));
            Say("panel-render", "pannello completo disegnato in %TEMP%\\hwwidget-panel.png");

            Say("config-dir", WidgetConfig.Dir);

            // --- AI: parser e log locali ---
            var (dsValue, dsSpent, dsCurrency) = AiUsage.ParseDeepSeekBalance(
                """{"is_available":true,"balance_infos":[{"currency":"USD","total_balance":"12.34","granted_balance":"0.00","topped_up_balance":"20.00"}]}""");
            Check(dsValue is > 12.3 and < 12.4 && dsCurrency == "USD", "saldo DeepSeek non letto");
            Check(dsSpent is > 7.6 and < 7.7, "speso DeepSeek non calcolato");
            Say("ai-deepseek", $"saldo={dsValue:0.00} {dsCurrency} · speso={dsSpent:0.00}");

            var openai = AiUsage.ParseDailyAmounts(
                """{"object":"page","data":[{"start_time":1757800000,"results":[{"amount":{"value":1.50,"currency":"usd"}}]},{"start_time":1757900000,"results":[{"amount":{"value":2.25,"currency":"usd"}}]}]}""");
            Check(Math.Abs(openai.Values.Sum() - 3.75) < 0.001, "spesa OpenAI non sommata");
            var anthropic = AiUsage.ParseDailyAmounts(
                """{"data":[{"starting_at":"2026-09-13T00:00:00Z","results":[{"amount":"4.10","currency":"USD"}]},{"starting_at":"2026-09-14T00:00:00Z","results":[{"amount":"1.90","currency":"USD"}]}]}""");
            Check(Math.Abs(anthropic.Values.Sum() - 6.00) < 0.001, "spesa Anthropic non sommata");
            Say("ai-spese", $"OpenAI {openai.Values.Sum():0.00} $ · Anthropic {anthropic.Values.Sum():0.00} $ (da JSON di esempio)");

            var (codexTokens, codexWeek) = CodexLogs.SumTokens();
            var (claudeTokens, _) = ClaudeLogs.SumTokens();
            Say("ai-token", $"log locali: Codex {AiUsage.Tokens(codexTokens)} (7g {AiUsage.Tokens(codexWeek)}) · Claude {AiUsage.Tokens(claudeTokens)}");

            // --- aggiornamenti: confronto versioni ---
            var nextBuild = new UpdateInfo
            {
                Version = new Version(Updater.Current.Major, Updater.Current.Minor, Updater.Current.Build + 1),
            };
            Check(Updater.IsNewer(nextBuild), "una build successiva deve risultare più nuova di quella installata");
            Check(!Updater.IsNewer(new UpdateInfo { Version = Updater.Current }), "la versione installata non può essere più nuova di se stessa");
            Check(AiUsage.Tokens(254976079).EndsWith("M"), "formattazione token errata");
            Say("update", $"versione corrente {Updater.CurrentText} · repo {Updater.Repo}");
            var ghKeys = AiKeys.Load();
            if (ghKeys.GitHub.Length > 0)
            {
                var release = Updater.CheckAsync(ghKeys.GitHub).GetAwaiter().GetResult();
                if (release == null) Say("update-check", "nessuna release leggibile (token o repo?)");
                else Say("update-check", $"ultima release {release.Tag} ({release.AssetName}, {release.Size / 1048576.0:0} MB) · " +
                                         $"aggiornamento disponibile: {(Updater.IsNewer(release) ? "sì" : "no")}");
            }
            else Say("update-check", "nessun token GitHub salvato: controllo aggiornamenti non testato");

            // autostart: same registry value the menus write
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using (var k = Registry.CurrentUser.OpenSubKey(runKey, true))
            {
                var before = k?.GetValue("HWWidget");
                k?.SetValue("HWWidget", "\"C:\\percorso\\di\\prova\\HWWidget.exe\"");
                bool wrote = k?.GetValue("HWWidget") != null;
                if (before == null) k?.DeleteValue("HWWidget", false);
                else k?.SetValue("HWWidget", before);
                bool restored = (k?.GetValue("HWWidget")?.ToString() ?? "") == (before?.ToString() ?? "");
                Say("autostart", $"scrittura={wrote} ripristino={restored}");
                Check(wrote, "chiave di avvio automatico non scrivibile");
                Check(restored, "ripristino della chiave di avvio fallito");
            }

            Say("esito", "OK");
        }
        catch (Exception ex)
        {
            Say("esito", "FALLITO: " + ex.Message);
            Environment.ExitCode = 1;
        }
        Console.Out.Write(Log.ToString());
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hwwidget-selftest.txt"), Log.ToString());
        }
        catch { }
    }

    static void Say(string tag, string msg) => Log.AppendLine($"[{tag}] {msg}");

    /// <summary>Prova la catena di acquisizione del token (finestra + arrivo dallo script +
    /// verifica sull'API): HWWidget.exe --dslogin-test</summary>
    public static void DsLoginTest()
    {
        AttachConsole(-1);
        try
        {
            var w = new DeepSeekLogin();                 // non mostrata: la WebView non parte
            w.FeedForTest(new string('x', 80));          // come se lo script avesse mandato un token
            for (int i = 0; i < 40 && !w.StatusText.StartsWith("Token non valido"); i++)
            {
                System.Threading.Thread.Sleep(250);
                Pump();
            }
            Say("dslogin", $"stato dopo un token finto: {w.StatusText}");
            Check(w.StatusText.StartsWith("Token non valido"), "un token finto deve essere rifiutato e segnalato");
            w.Close();
            Say("esito", "OK");
        }
        catch (Exception ex) { Say("esito", "FALLITO: " + ex.Message); Environment.ExitCode = 1; }
        Console.Out.Write(Log.ToString());
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hwwidget-dslogin.txt"), Log.ToString());
        }
        catch { }
    }

    /// <summary>Prova la lettura DeepSeek (saldo + uso) e salva le risposte grezze:
    /// HWWidget.exe --dstest → %TEMP%\hwwidget-dstest.txt (+ i due JSON grezzi).</summary>
    public static void DeepSeekTest()
    {
        AttachConsole(-1);
        try
        {
            var keys = AiKeys.Load();
            Say("chiavi", $"API key DeepSeek: {(keys.DeepSeek.Length > 0 ? $"presente ({keys.DeepSeek.Length} caratteri)" : "ASSENTE")} · " +
                          $"token di utilizzo: {(keys.DeepSeekUsage.Length > 0 ? $"presente ({keys.DeepSeekUsage.Length} caratteri)" : "ASSENTE")}");
            var u = DeepSeekUsage.FetchAsync(keys.DeepSeek, keys.DeepSeekUsage).GetAwaiter().GetResult();
            Say("saldo", $"{u.Balance} · disponibile={u.Available} · speso={u.Spent}");
            Say("uso", $"{u.Models.Count} modelli · {u.Days.Count} giorni · mese {u.MonthCost} · oggi {u.TodayCost}");
            Say("stato", u.Status.Length > 0 ? u.Status : "(nessun problema)");
            foreach (var m in u.Models)
                Say("modello", $"{m.Key} → {m.Name} · {AiUsage.Tokens(m.Tokens)} token · {m.Requests:0} richieste · " +
                               $"hit {m.HitPct:0}% miss {m.MissPct:0}% out {m.OutPct:0}% · costo {m.Cost:0.0000}");
            foreach (var d in u.Days.Take(5))
                Say("giorno", $"{d.Date} · hit {AiUsage.Tokens(d.Hit)} · miss {AiUsage.Tokens(d.Miss)} · out {AiUsage.Tokens(d.Out)} · costo {d.Cost:0.0000}");
            string temp = System.IO.Path.GetTempPath();
            if (u.RawAmount.Length > 0)
                System.IO.File.WriteAllText(System.IO.Path.Combine(temp, "hwwidget-ds-amount.json"), u.RawAmount);
            if (u.RawCost.Length > 0)
                System.IO.File.WriteAllText(System.IO.Path.Combine(temp, "hwwidget-ds-cost.json"), u.RawCost);
            Say("raw", $"amount {u.RawAmount.Length} byte · cost {u.RawCost.Length} byte in {temp}");
        }
        catch (Exception ex) { Say("esito", "FALLITO: " + ex.Message); Environment.ExitCode = 1; }
        Console.Out.Write(Log.ToString());
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hwwidget-dstest.txt"), Log.ToString());
        }
        catch { }
    }

    /// <summary>Costruisce due widget veri: il primo viene spostato e ridimensionato e poi
    /// chiuso (che è il momento in cui salva), il secondo deve riaprire esattamente nello
    /// stesso rettangolo, sullo stesso monitor.</summary>
    static void WindowGeometryTest()
    {
        const string id = "selftest";
        try
        {
            new WidgetConfig { Id = id }.Delete();
            var win = new MainWindow(WidgetConfig.Load(id));
            win.Show();
            Pump();
            win.Left = 180;
            win.Top = 120;
            win.Width = 340;
            win.Height = 300;
            Pump();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            Check(Native.GetWindowRect(hwnd, out var before), "GetWindowRect fallito");
            win.Close();
            Pump();

            var saved = WidgetConfig.Load(id);
            int w = before.R - before.L, h = before.B - before.T;
            Check(saved.HasPos && saved.Monitor is { Length: > 0 }, "posizione non salvata");
            Check(Math.Abs(saved.OffX - (before.L - ScreenOf(before).X)) <= 1 &&
                  Math.Abs(saved.OffY - (before.T - ScreenOf(before).Y)) <= 1, "offset salvato errato");
            Check(Math.Abs(saved.Width - win.Width) <= 1 && Math.Abs(saved.Height - win.Height) <= 1,
                  $"dimensioni salvate {saved.Width:0}x{saved.Height:0} invece di {win.Width:0}x{win.Height:0}");

            var again = new MainWindow(WidgetConfig.Load(id));
            again.Show();
            Pump();
            var hwnd2 = new System.Windows.Interop.WindowInteropHelper(again).Handle;
            Check(Native.GetWindowRect(hwnd2, out var after), "GetWindowRect fallito al secondo giro");
            double dpi = Native.GetDpiForWindow(hwnd2) / 96.0;
            again.Close();
            Pump();

            var s2 = WidgetConfig.Load(id);
            int dx = Math.Abs(after.L - before.L), dy = Math.Abs(after.T - before.T);
            int dw = Math.Abs((after.R - after.L) - w), dh = Math.Abs((after.B - after.T) - h);
            Check(dx <= 2 && dy <= 2, $"posizione ripristinata diversa: Δ{dx},{dy} px");
            Check(dw <= 2 && dh <= 2, $"dimensioni ripristinate diverse: Δ{dw},{dh} px");
            Say("geometria", $"{s2.Monitor} offset {s2.OffX:0},{s2.OffY:0} · {w}×{h} px → riaperto a Δ{dx},{dy} Δ{dw}×{dh}");
            Say("geometria-dip", $"{s2.Width:0}×{s2.Height:0} DIP salvati, finestra {w}×{h} px (scala schermo {dpi:0.##})");
            s2.Delete();
        }
        catch (Exception ex)
        {
            new WidgetConfig { Id = id }.Delete();
            throw new Exception("geometria: " + ex.Message);
        }
    }

    static (int X, int Y, int W, int H) ScreenOf(Native.RECT r)
    {
        var s = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(r.L + 5, r.T + 5));
        return (s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height);
    }

    /// <summary>Widget con la sola sezione AI e i fornitori indicati.</summary>
    static WidgetView AiView(List<string> providers)
    {
        var cfg = new WidgetConfig
        {
            Layout = "panelgraph",
            ShowNet = false, ShowCpu = false, ShowGpu = false, ShowRam = false, ShowDisk = false,
            ShowAi = true,
            AiProviders = providers,
        };
        return new WidgetView(cfg.Sanitized(), Palette.For("dark"), new Dictionary<string, Series>(), 330);
    }

    /// <summary>Conta i grafici nell'albero logico (la sezione AI non deve averne).</summary>
    static int CountSparklines(System.Windows.DependencyObject o)
    {
        int n = o is Sparkline ? 1 : 0;
        foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(o))
            if (c is System.Windows.DependencyObject d) n += CountSparklines(d);
        return n;
    }

    static IEnumerable<Sparkline> Sparklines(System.Windows.DependencyObject o)
    {
        if (o is Sparkline sp) yield return sp;
        foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(o))
            if (c is System.Windows.DependencyObject d)
                foreach (var x in Sparklines(d)) yield return x;
    }

    /// <summary>Il pulsante di aggiornamento del widget DeepSeek (unico, in alto a destra).</summary>
    static bool HasRefreshButton(System.Windows.DependencyObject o)
    {
        if (o is System.Windows.Controls.Border b && b.ToolTip as string == "Aggiorna saldo, token e API") return true;
        foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(o))
            if (c is System.Windows.DependencyObject d && HasRefreshButton(d)) return true;
        return false;
    }

    static void CollectTexts(System.Windows.DependencyObject o, List<string> into)
    {
        if (o is System.Windows.Controls.TextBlock t) into.Add(t.Text);
        foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(o))
            if (c is System.Windows.DependencyObject d) CollectTexts(d, into);
    }

    /// <summary>Disegna la vista su sfondo scuro e la salva in PNG (controllo visivo).</summary>
    static void SavePng(WidgetView view, double width, string path)
    {
        var host = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1E)),
            Padding = new System.Windows.Thickness(12, 10, 12, 12),
            Child = view.Root,
        };
        host.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        host.Arrange(new System.Windows.Rect(0, 0, width, host.DesiredSize.Height));
        host.UpdateLayout();
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(width), (int)Math.Ceiling(host.DesiredSize.Height), 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(host);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }

    /// <summary>Svuota la coda del dispatcher fino alle operazioni inattive (Layout/Loaded
    /// compresi): serve perché Show() e RestorePosition non sono sincroni.</summary>
    static void Pump()
    {
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
            () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Scarica davvero l'ultimo asset della release (verifica del percorso
    /// autenticato verso la repo privata) e poi cancella il file.</summary>
    public static void UpdateDownloadTest()
    {
        AttachConsole(-1);
        try
        {
            var keys = AiKeys.Load();
            var info = Updater.CheckAsync(keys.GitHub).GetAwaiter().GetResult();
            if (info == null) { Say("update", "nessuna release leggibile"); return; }
            Say("update", $"release {info.Tag}, asset {info.AssetName} ({info.Size / 1048576.0:0} MB)");
            var progress = new Progress<double>(p => { if (Math.Abs(p * 100 % 10) < 0.001) Say("update", $"  {p:P0}"); });
            string path = Updater.DownloadAsync(info, keys.GitHub, progress).GetAwaiter().GetResult();
            var fi = new System.IO.FileInfo(path);
            Say("update", $"scaricato {fi.Name}: {fi.Length / 1048576.0:0.0} MB in {path}");
            System.IO.File.Delete(path);
            Say("update", "file di prova cancellato");
            Say("esito", "OK");
        }
        catch (Exception ex) { Say("esito", "FALLITO: " + ex.Message); Environment.ExitCode = 1; }
        Console.Out.Write(Log.ToString());
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hwwidget-updatetest.txt"), Log.ToString());
        }
        catch { }
    }

    static void Check(bool ok, string msg)
    {
        if (!ok) throw new Exception(msg);
    }

    // JSON di esempio con la struttura delle API interne DeepSeek (platform.deepseek.com),
    // la stessa che usa il monitor di riferimento Joyi-code/DeepSeekMonitorWindows
    const string DsAmountSample = """
{"data":{"biz_data":{
 "total":[
  {"model":"deepseek-chat","usage":[
    {"type":"REQUEST","amount":"6384"},
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"300000000"},
    {"type":"PROMPT_CACHE_MISS_TOKEN","amount":"20000000"},
    {"type":"RESPONSE_TOKEN","amount":"5000000"}]},
  {"model":"deepseek-reasoner","usage":[
    {"type":"REQUEST","amount":"112"},
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"10000000"},
    {"type":"PROMPT_CACHE_MISS_TOKEN","amount":"1000000"},
    {"type":"RESPONSE_TOKEN","amount":"500000"}]}],
 "days":[
  {"date":"2026-09-12","data":[{"model":"deepseek-chat","usage":[
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"1000000"},
    {"type":"PROMPT_CACHE_MISS_TOKEN","amount":"100000"},
    {"type":"RESPONSE_TOKEN","amount":"50000"}]}]},
  {"date":"2026-09-13","data":[{"model":"deepseek-chat","usage":[
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"2000000"},
    {"type":"RESPONSE_TOKEN","amount":"90000"}]}]},
  {"date":"2026-09-14","data":[{"model":"deepseek-chat","usage":[
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"125000000"},
    {"type":"PROMPT_CACHE_MISS_TOKEN","amount":"4000000"},
    {"type":"RESPONSE_TOKEN","amount":"1200000"}]}]},
  {"date":"2026-09-15","data":[{"model":"deepseek-chat","usage":[
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"37800000"},
    {"type":"RESPONSE_TOKEN","amount":"900000"}]},
   {"model":"deepseek-reasoner","usage":[
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"5000000"},
    {"type":"RESPONSE_TOKEN","amount":"400000"}]}]},
  {"date":"2026-09-16","data":[{"model":"deepseek-reasoner","usage":[
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"137000000"},
    {"type":"RESPONSE_TOKEN","amount":"2000000"}]}]},
  {"date":"2026-09-17","data":[{"model":"deepseek-chat","usage":[
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"32700000"},
    {"type":"PROMPT_CACHE_MISS_TOKEN","amount":"800000"}]}]},
  {"date":"2026-09-18","data":[{"model":"deepseek-chat","usage":[
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"8000000"}]}]}]}}}
""";

    const string DsCostSample = """
{"data":{"biz_data":[{
 "total":[
  {"model":"deepseek-chat","usage":[
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"10.50"},
    {"type":"RESPONSE_TOKEN","amount":"2.25"}]},
  {"model":"deepseek-reasoner","usage":[
    {"type":"PROMPT_CACHE_HIT_TOKEN","amount":"1.00"},
    {"type":"RESPONSE_TOKEN","amount":"0.40"}]}],
 "days":[
  {"date":"2026-09-12","data":[{"model":"deepseek-chat","usage":[{"type":"PROMPT_CACHE_HIT_TOKEN","amount":"1.50"}]}]},
  {"date":"2026-09-13","data":[{"model":"deepseek-chat","usage":[{"type":"PROMPT_CACHE_HIT_TOKEN","amount":"2.00"}]}]},
  {"date":"2026-09-14","data":[{"model":"deepseek-chat","usage":[{"type":"PROMPT_CACHE_HIT_TOKEN","amount":"4.60"}]}]},
  {"date":"2026-09-15","data":[{"model":"deepseek-chat","usage":[{"type":"PROMPT_CACHE_HIT_TOKEN","amount":"1.40"}]}]},
  {"date":"2026-09-16","data":[{"model":"deepseek-reasoner","usage":[{"type":"PROMPT_CACHE_HIT_TOKEN","amount":"5.10"}]}]},
  {"date":"2026-09-17","data":[{"model":"deepseek-chat","usage":[{"type":"PROMPT_CACHE_HIT_TOKEN","amount":"1.20"}]}]},
  {"date":"2026-09-18","data":[{"model":"deepseek-chat","usage":[{"type":"PROMPT_CACHE_HIT_TOKEN","amount":"0.30"}]}]}]}]}}
""";
}
