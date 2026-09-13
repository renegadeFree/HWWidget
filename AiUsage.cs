using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HWWidget;

/// <summary>Snapshot of the AI usage sources. Everything is optional: a provider shows
/// "n/d" when the key or the data source is missing.</summary>
sealed class AiSnapshot
{
    public string DeepSeekBalance = "";     // "12,34 USD" oppure "" se non disponibile
    public double DeepSeekValue;            // valore numerico per il grafico
    public string OpenAiWeek = "", OpenAiMonth = "";
    public double OpenAiWeekValue, OpenAiMonthValue;
    public string AnthropicWeek = "", AnthropicMonth = "";
    public double AnthropicWeekValue, AnthropicMonthValue;
    public string CodexTokens = "", ClaudeTokens = "";
    public double CodexWeekTokens;
    public double CodexTokensValue, ClaudeTokensValue;
    public string Status = "";
    public DateTime FetchedUtc;
}

/// <summary>API keys, stored with DPAPI (only this Windows user can read them).</summary>
sealed class AiKeys
{
    public string DeepSeek { get; set; } = "";
    public string OpenAi { get; set; } = "";
    public string Anthropic { get; set; } = "";
    public string GitHub { get; set; } = "";      // per l'auto-update dalla repo privata
    public int RefreshMinutes { get; set; } = 15; // ogni quanto rileggere le API AI
    public bool CheckUpdatesOnStartup { get; set; } = true;

    static string File => Path.Combine(WidgetConfig.Dir, "keys.dat");

    public static AiKeys Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new AiKeys();
            var json = Secret.Unprotect(System.IO.File.ReadAllBytes(File));
            return JsonSerializer.Deserialize<AiKeys>(json) ?? new AiKeys();
        }
        catch { return new AiKeys(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(WidgetConfig.Dir);
            System.IO.File.WriteAllBytes(File, Secret.Protect(JsonSerializer.Serialize(this)));
        }
        catch { }
    }
}

/// <summary>DPAPI wrappers (no extra package: crypt32 via P/Invoke).</summary>
static class Secret
{
    [StructLayout(LayoutKind.Sequential)]
    struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptProtectData(ref DATA_BLOB input, string? description, IntPtr entropy,
                                        IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB output);

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref DATA_BLOB input, IntPtr description, IntPtr entropy,
                                          IntPtr reserved, IntPtr prompt, int flags, out DATA_BLOB output);

    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);

    static byte[] Run(byte[] data, bool protect)
    {
        var input = new DATA_BLOB();
        input.pbData = Marshal.AllocHGlobal(data.Length);
        input.cbData = data.Length;
        Marshal.Copy(data, 0, input.pbData, data.Length);
        try
        {
            DATA_BLOB output;
            bool ok = protect
                ? CryptProtectData(ref input, "HWWidget", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output);
            if (!ok) throw new InvalidOperationException("DPAPI");
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            LocalFree(output.pbData);
            return result;
        }
        finally { Marshal.FreeHGlobal(input.pbData); }
    }

    public static byte[] Protect(string text) => Run(Encoding.UTF8.GetBytes(text), true);
    public static string Unprotect(byte[] data) => Encoding.UTF8.GetString(Run(data, false));
}

/// <summary>Reads the AI usage sources. DeepSeek/OpenAI/Anthropic need API keys;
/// the token totals come from the local CLI logs (no keys).</summary>
static class AiUsage
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<AiSnapshot> FetchAsync(AiKeys keys)
    {
        var s = new AiSnapshot { FetchedUtc = DateTime.UtcNow };
        var problems = new List<string>();

        if (keys.DeepSeek.Length > 0)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
                req.Headers.Add("Authorization", "Bearer " + keys.DeepSeek);
                using var res = await Http.SendAsync(req);
                string body = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    var (value, currency) = ParseDeepSeekBalance(body);
                    if (value.HasValue)
                    {
                        s.DeepSeekValue = value.Value;
                        s.DeepSeekBalance = value.Value.ToString("0.00", CultureInfo.CurrentCulture) + " " + currency;
                    }
                }
                else problems.Add($"DeepSeek {(int)res.StatusCode}");
            }
            catch (Exception ex) { problems.Add("DeepSeek: " + ex.Message); }
        }

        if (keys.OpenAi.Length > 0)
        {
            try
            {
                var start = DateTime.UtcNow.Date.AddDays(-30).ToString("yyyy-MM-dd");
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.openai.com/v1/organization/costs?start_time={start}&limit=31&bucket_width=1d");
                req.Headers.Add("Authorization", "Bearer " + keys.OpenAi);
                using var res = await Http.SendAsync(req);
                string body = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    var daily = ParseDailyAmounts(body);
                    s.OpenAiMonthValue = daily.Values.Sum();
                    s.OpenAiWeekValue = daily.Where(kv => kv.Key >= DateTime.UtcNow.Date.AddDays(-7)).Sum(kv => kv.Value);
                    s.OpenAiMonth = Money(s.OpenAiMonthValue);
                    s.OpenAiWeek = Money(s.OpenAiWeekValue);
                }
                else problems.Add($"OpenAI {(int)res.StatusCode}");
            }
            catch (Exception ex) { problems.Add("OpenAI: " + ex.Message); }
        }

        if (keys.Anthropic.Length > 0)
        {
            try
            {
                var start = DateTime.UtcNow.Date.AddDays(-30).ToString("yyyy-MM-dd");
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.anthropic.com/v1/organizations/cost_report?starting_at={start}&bucket_width=1d&limit=31");
                req.Headers.Add("x-api-key", keys.Anthropic);
                req.Headers.Add("anthropic-version", "2023-06-01");
                using var res = await Http.SendAsync(req);
                string body = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    var daily = ParseDailyAmounts(body);
                    s.AnthropicMonthValue = daily.Values.Sum();
                    s.AnthropicWeekValue = daily.Where(kv => kv.Key >= DateTime.UtcNow.Date.AddDays(-7)).Sum(kv => kv.Value);
                    s.AnthropicMonth = Money(s.AnthropicMonthValue);
                    s.AnthropicWeek = Money(s.AnthropicWeekValue);
                }
                else problems.Add($"Anthropic {(int)res.StatusCode}");
            }
            catch (Exception ex) { problems.Add("Anthropic: " + ex.Message); }
        }

        // token totali dai log locali dei CLI (nessuna chiave)
        try
        {
            var (total, week) = CodexLogs.SumTokens();
            if (total > 0)
            {
                s.CodexTokensValue = total;
                s.CodexTokens = Tokens(total);
                s.CodexWeekTokens = week;
            }
        }
        catch { }
        try
        {
            var (total, _) = ClaudeLogs.SumTokens();
            if (total > 0)
            {
                s.ClaudeTokensValue = total;
                s.ClaudeTokens = Tokens(total);
            }
        }
        catch { }

        s.Status = problems.Count > 0 ? string.Join(" · ", problems) : "";
        return s;
    }

    public static string Tokens(double v) => v >= 1_000_000_000 ? $"{v / 1e9:0.00} G"
        : v >= 1_000_000 ? $"{v / 1e6:0.0} M"
        : v >= 1_000 ? $"{v / 1e3:0.0} K"
        : $"{v:0}";

    static string Money(double v) => v.ToString("0.00", CultureInfo.CurrentCulture) + " $";

    // ---- parser tolleranti: cercano i campi utili invece di fissare lo schema ----

    public static (double? value, string currency) ParseDeepSeekBalance(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("balance_infos", out var infos) && infos.GetArrayLength() > 0)
        {
            var first = infos[0];
            double? value = first.TryGetProperty("total_balance", out var t) ? ToNumber(t) : null;
            string currency = first.TryGetProperty("currency", out var c) ? c.GetString() ?? "" : "";
            return (value, currency);
        }
        return (null, "");
    }

    /// <summary>Somma gli importi per giorno: funziona sia col formato OpenAI
    /// (data[].results[].amount.value) sia con quello Anthropic (data[].results[].amount).</summary>
    public static Dictionary<DateTime, double> ParseDailyAmounts(string json)
    {
        var result = new Dictionary<DateTime, double>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var buckets) || buckets.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var bucket in buckets.EnumerateArray())
        {
            DateTime day = DateTime.UtcNow.Date;
            if (bucket.TryGetProperty("start_time", out var st)) day = ToDate(st) ?? day;
            else if (bucket.TryGetProperty("starting_at", out var sa)) day = ToDate(sa) ?? day;
            double sum = 0;
            if (bucket.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                foreach (var r in results.EnumerateArray())
                {
                    if (r.TryGetProperty("amount", out var amount))
                    {
                        if (amount.ValueKind == JsonValueKind.Object && amount.TryGetProperty("value", out var av))
                            sum += ToNumber(av) ?? 0;
                        else sum += ToNumber(amount) ?? 0;
                    }
                }
            result[day] = result.TryGetValue(day, out var old) ? old + sum : sum;
        }
        return result;
    }

    static double? ToNumber(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.String => double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null,
        _ => null,
    };

    static DateTime? ToDate(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number)
            return DateTimeOffset.FromUnixTimeSeconds(e.GetInt64()).UtcDateTime.Date;
        if (e.ValueKind == JsonValueKind.String && DateTime.TryParse(e.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var d))
            return d.Date;
        return null;
    }
}

/// <summary>Token totali dai log della CLI Codex (~/.codex/sessions/*.jsonl):
/// l'ultimo evento token_count di ogni sessione porta il totale cumulativo.</summary>
static class CodexLogs
{
    static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    public static (double total, double week) SumTokens()
    {
        double total = 0, week = 0;
        if (!Directory.Exists(Root)) return (0, 0);
        foreach (var file in Directory.GetFiles(Root, "*.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                bool inWeek = info.LastWriteTime >= DateTime.Now.AddDays(-7);
                double? sessionTotal = LastSessionTotal(file);
                if (sessionTotal.HasValue)
                {
                    total += sessionTotal.Value;
                    if (inWeek) week += sessionTotal.Value;
                }
            }
            catch { }
        }
        return (total, week);
    }

    /// <summary>Legge solo la coda del file: i log possono essere enormi.</summary>
    static double? LastSessionTotal(string file)
    {
        const int tail = 512 * 1024;
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long start = Math.Max(0, fs.Length - tail);
        fs.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(fs);
        string? line;
        double? found = null;
        while ((line = reader.ReadLine()) != null)
        {
            if (!line.Contains("\"token_count\"")) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("payload", out var p) &&
                    p.TryGetProperty("info", out var info) &&
                    info.TryGetProperty("total_token_usage", out var usage) &&
                    usage.TryGetProperty("total_tokens", out var t))
                    found = t.GetDouble();
            }
            catch { }
        }
        return found;
    }
}

/// <summary>Token totali dai log di Claude Code (~/.claude/projects/*.jsonl):
/// ogni messaggio assistant porta il proprio usage.</summary>
static class ClaudeLogs
{
    static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    public static (double total, double week) SumTokens()
    {
        double total = 0, week = 0;
        if (!Directory.Exists(Root)) return (0, 0);
        var from = DateTime.Now.AddDays(-30);
        foreach (var file in Directory.GetFiles(Root, "*.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTime < from) continue;          // ponytail: solo i file recenti
                bool inWeek = info.LastWriteTime >= DateTime.Now.AddDays(-7);
                foreach (var line in ReadLines(file))
                {
                    if (!line.Contains("\"usage\"")) continue;
                    double t = TokensOf(line);
                    if (t <= 0) continue;
                    total += t;
                    if (inWeek) week += t;
                }
            }
            catch { }
        }
        return (total, week);
    }

    static IEnumerable<string> ReadLines(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        string? line;
        while ((line = reader.ReadLine()) != null) yield return line;
    }

    static double TokensOf(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("message", out var msg)) return 0;
            if (!msg.TryGetProperty("usage", out var usage)) return 0;
            double sum = 0;
            foreach (var name in new[] { "input_tokens", "output_tokens", "cache_creation_input_tokens", "cache_read_input_tokens" })
                if (usage.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
                    sum += v.GetDouble();
            return sum;
        }
        catch { return 0; }
    }
}
