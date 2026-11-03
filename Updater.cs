using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace HWWidget;

sealed class UpdateInfo
{
    public Version Version = new(0, 0);
    public string Tag = "";
    public string AssetName = "";
    public string AssetUrl = "";      // API url della release pubblica
    public string AssetHashUrl = "";  // asset <nome>.sha256 / SHA256SUMS.txt, se pubblicato
    public long Size;
}

/// <summary>Auto-update dalle release pubbliche GitHub, senza credenziali.</summary>
static class Updater
{
    public const string Repo = "renegadeFree/HWWidget";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public static Version Current =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Add("User-Agent", "HWWidget");
        req.Headers.Add("Accept", "application/vnd.github+json");
        using var res = await Http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;

        var info = new UpdateInfo { Version = version, Tag = tag };
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                // solo l'eseguibile: anche "HWWidgetSetup.exe.sha256" inizia con lo stesso prefisso
                if (!name.StartsWith("HWWidgetSetup", StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                info.AssetName = name;
                info.AssetUrl = a.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                info.Size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                break;
            }
            // verifica facoltativa: se la release pubblica un hash, usalo
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name != info.AssetName + ".sha256" && name != "SHA256SUMS.txt" && name != "SHA256SUMS") continue;
                info.AssetHashUrl = a.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                break;
            }
        }
        return info.AssetUrl.Length > 0 ? info : null;
    }

    public static bool IsNewer(UpdateInfo info) => info.Version > Current;

    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress,
                                                   CancellationToken ct = default)
    {
        string path = Path.Combine(Path.GetTempPath(), $"HWWidgetSetup-{info.Tag}.exe");
        using var req = new HttpRequestMessage(HttpMethod.Get, info.AssetUrl);
        req.Headers.Add("User-Agent", "HWWidget");
        req.Headers.Add("Accept", "application/octet-stream");
        using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        long total = res.Content.Headers.ContentLength ?? info.Size;
        using var src = await res.Content.ReadAsStreamAsync(ct);
        using var dst = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0) progress?.Report((double)done / total);
        }
        dst.Close();

        if (info.AssetHashUrl.Length > 0)
        {
            string expected = await FetchShaAsync(info.AssetHashUrl, info.AssetName, ct);
            string actual;
            using (var fs = File.OpenRead(path))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidOperationException("hash del file scaricato non corrispondente: aggiornamento annullato");
            }
        }
        return path;
    }

    /// <summary>Hash dell'asset: prima riga valida per quel nome, o l'unica del file .sha256.</summary>
    static async Task<string> FetchShaAsync(string url, string assetName, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent", "HWWidget");
        req.Headers.Add("Accept", "application/octet-stream");
        using var res = await Http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        string body = await res.Content.ReadAsStringAsync(ct);

        string? only = null;
        int found = 0;
        foreach (var line in body.Split('\n', '\r'))
        {
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            string hash = parts[0];
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) continue;
            found++;
            only = hash;
            if (parts.Skip(1).Any(p => p.TrimStart('*').Equals(assetName, StringComparison.OrdinalIgnoreCase)))
                return hash;
        }
        return found == 1 && only != null ? only
            : throw new InvalidOperationException("file di hash non leggibile per " + assetName);
    }

    /// <summary>Lancia l'installer in modalità aggiornamento e (se c'è) chiude l'app.</summary>
    public static void RunInstaller(string setupPath, bool exitApp = true)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "--update",
            UseShellExecute = true,
        });
        if (exitApp) AppController.Current?.ExitAll();
    }

    /// <summary>Controlla, scarica e installa senza interfaccia: usato per verificare il
    /// giro completo "nuova release → programma installato aggiornato" e per gli
    /// aggiornamenti silenziosi.</summary>
    public static string SilentUpdate(string logPath)
    {
        void Log(string msg)
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n"); } catch { }
        }
        try
        {
            Log($"versione installata {CurrentText}");
            var info = CheckAsync().GetAwaiter().GetResult();
            if (info == null) { Log("nessuna release leggibile"); return "no-release"; }
            Log($"release trovata {info.Tag} · {info.AssetName} ({info.Size / 1048576.0:0} MB) · " +
                $"hash {(info.AssetHashUrl.Length > 0 ? "pubblicato" : "non pubblicato")}");
            if (!IsNewer(info)) { Log("sei aggiornato"); return "updated"; }
            string path = DownloadAsync(info, null).GetAwaiter().GetResult();
            Log($"scaricato {new FileInfo(path).Length / 1048576.0:0.0} MB in {path}");
            RunInstaller(path, exitApp: false);
            Log("installer avviato");
            return "installing";
        }
        catch (Exception ex)
        {
            Log("FALLITO: " + ex.Message);
            return "error";
        }
    }
}
