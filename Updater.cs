using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
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
    public string AssetUrl = "";      // API url (funziona con le repo private)
    public long Size;
}

/// <summary>Auto-update dalle release GitHub. La repo è privata, quindi serve un token
/// (salvato con DPAPI come le altre chiavi): viene usato solo per leggere le release.</summary>
static class Updater
{
    public const string Repo = "renegadeFree/HWWidget";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public static Version Current =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    public static async Task<UpdateInfo?> CheckAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Add("Authorization", "token " + token.Trim());
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
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (!name.StartsWith("HWWidgetSetup", StringComparison.OrdinalIgnoreCase)) continue;
                info.AssetName = name;
                info.AssetUrl = a.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                info.Size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                break;
            }
        return info.AssetUrl.Length > 0 ? info : null;
    }

    public static bool IsNewer(UpdateInfo info) => info.Version > Current;

    public static async Task<string> DownloadAsync(UpdateInfo info, string token, IProgress<double>? progress,
                                                   CancellationToken ct = default)
    {
        string path = Path.Combine(Path.GetTempPath(), $"HWWidgetSetup-{info.Tag}.exe");
        using var req = new HttpRequestMessage(HttpMethod.Get, info.AssetUrl);
        req.Headers.Add("Authorization", "token " + token.Trim());
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
        return path;
    }

    /// <summary>Lancia l'installer in modalità aggiornamento e chiude l'app.</summary>
    public static void RunInstaller(string setupPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "--update",
            UseShellExecute = true,
        });
        AppController.Current.ExitAll();
    }
}
