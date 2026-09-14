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
    public string DeepSeekSpent = "";       // ricariche meno saldo = speso finora
    public string OpenAiWeek = "", OpenAiMonth = "";
    public double OpenAiWeekValue, OpenAiMonthValue;
    public string AnthropicWeek = "", AnthropicMonth = "";
    public double AnthropicWeekValue, AnthropicMonthValue;
    public string CodexTokens = "", ClaudeTokens = "";
    public double CodexWeekTokens;
    public double CodexTokensValue, ClaudeTokensValue;
    /// <summary>Dati completi DeepSeek (saldo + uso/spesa del mese) per il widget dedicato.</summary>
    public DeepSeekUsage Deep = new();
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
    /// <summary>Token di sessione di platform.deepseek.com (uso e spesa): l'API ufficiale
    /// DeepSeek espone solo il saldo. Si prende dal browser: console → JSON.parse(localStorage.userToken).value</summary>
    public string DeepSeekUsage { get; set; } = "";
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

    /// <summary>Accetta quello che si copia dal browser in qualunque forma: con virgolette,
    /// oppure l'intero oggetto di localStorage (`{"value":"eyJ..."}`).</summary>
    public static string Normalize(string token)
    {
        string t = token.Trim().Trim('"', '\'');
        if (!t.StartsWith('{')) return t;
        try
        {
            using var doc = JsonDocument.Parse(t);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString()?.Trim() ?? t;
        }
        catch { }
        return t;
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

        if (keys.DeepSeek.Length > 0 || keys.DeepSeekUsage.Length > 0)
        {
            // saldo (API key) + uso e spesa del mese (token di sessione della piattaforma)
            s.Deep = await DeepSeekUsage.FetchAsync(keys.DeepSeek, keys.DeepSeekUsage);
            s.DeepSeekValue = s.Deep.BalanceValue;
            s.DeepSeekBalance = s.Deep.Balance;
            s.DeepSeekSpent = s.Deep.Spent;
            if (keys.DeepSeek.Length > 0 && s.Deep.Balance.Length == 0) problems.Add("DeepSeek saldo n/d");
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

    /// <summary>Saldo e speso (compatibilità: il parsing sta in DeepSeekUsage).</summary>
    public static (double? value, double? spent, string currency) ParseDeepSeekBalance(string json)
    {
        var (total, spent, _, currency, _) = DeepSeekUsage.ParseBalance(json);
        return (total, spent, currency);
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

/// <summary>Uso e spesa DeepSeek del mese. Il saldo arriva dall'API ufficiale
/// (api.deepseek.com/user/balance); token, richieste, cache hit e costo arrivano dalle
/// API interne della piattaforma (platform.deepseek.com/api/v0/usage/amount e /cost),
/// le stesse che usa la dashboard web: servono il token di sessione del sito, non la
/// API key. Struttura dei due JSON (verificata sul monitor open source
/// Joyi-code/DeepSeekMonitorWindows):
///   amount → data.biz_data.total[]  {model, usage[{type, amount}]}
///            data.biz_data.days[]   {date, data[{model, usage[]}]}
///   cost   → data.biz_data[]        {total[...], days[...]}  (stesse forme)
/// tipi: REQUEST, PROMPT_CACHE_HIT_TOKEN, PROMPT_CACHE_MISS_TOKEN, RESPONSE_TOKEN, PROMPT_TOKEN
/// </summary>
sealed class DeepSeekUsage
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    const string Ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                      "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public string Balance = "", Currency = "", Spent = "";
    public double BalanceValue;
    public bool Available;
    public string TodayCost = "", MonthCost = "";
    public double TodayValue, MonthValue;
    public List<DsModel> Models = new();
    public List<DsDay> Days = new();
    public string Status = "";
    public DateTime FetchedUtc;
    /// <summary>Risposte grezze dell'ultima lettura (diagnostica: --dstest).</summary>
    public string RawAmount = "", RawCost = "";

    public bool HasUsage => Models.Count > 0 || Days.Count > 0;
    /// <summary>true quando l'endpoint interno ha accettato il token (usato per validarlo).</summary>
    public bool UsageOk;
    public string HitRate => Days.Count == 0 && Models.Count == 0 ? ""
        : AiUsage.Tokens(Models.Sum(m => m.Hit) + Models.Sum(m => m.Miss) + Models.Sum(m => m.Out));

    public static async Task<DeepSeekUsage> FetchAsync(string apiKey, string usageToken)
    {
        var u = new DeepSeekUsage { FetchedUtc = DateTime.UtcNow };
        var problems = new List<string>();

        if (apiKey.Length > 0)
        {
            try
            {
                string body = await GetAsync("https://api.deepseek.com/user/balance", apiKey, official: true);
                var (total, spent, granted, currency, available) = ParseBalance(body);
                if (total.HasValue)
                {
                    u.BalanceValue = total.Value;
                    u.Currency = currency;
                    u.Available = available;
                    u.Balance = Money(total.Value, currency);
                    if (spent.HasValue) u.Spent = Money(Math.Max(0, spent.Value), currency);
                }
                else problems.Add("saldo non leggibile");
                if (granted is > 0) problems.Add($"crediti omaggio {Money(granted.Value, currency)}");
            }
            catch (Exception ex) { problems.Add("saldo: " + ex.Message); }
        }
        else problems.Add("serve la API key per il saldo");

        if (usageToken.Length > 0)
        {
            try
            {
                if (usageToken == apiKey)
                    throw new Exception("nel campo del token di utilizzo c'è la API key: serve il token di sessione di " +
                                        "platform.deepseek.com (F12 → JSON.parse(localStorage.userToken).value)");
                if (usageToken.Length < 80)
                    problems.Add($"il token di utilizzo è di {usageToken.Length} caratteri: quello del sito è molto più " +
                                 "lungo e inizia con eyJ");
                var now = DateTime.Now;
                u.RawAmount = await GetAsync(
                    $"https://platform.deepseek.com/api/v0/usage/amount?month={now.Month}&year={now.Year}", usageToken, false);
                u.RawCost = await GetAsync(
                    $"https://platform.deepseek.com/api/v0/usage/cost?month={now.Month}&year={now.Year}", usageToken, false);
                // la piattaforma risponde 200 anche quando rifiuta il token: l'errore è nel corpo
                string? err = BodyError(u.RawAmount) ?? BodyError(u.RawCost);
                if (err != null) problems.Add(err);
                else { Parse(u.RawAmount, u.RawCost, u, now); u.UsageOk = true; }
            }
            catch (Exception ex) { problems.Add("uso: " + ex.Message); }
        }
        else problems.Add("serve il token di utilizzo (platform.deepseek.com) per token, richieste e spesa");

        u.Status = string.Join(" · ", problems);
        return u;
    }

    static async Task<string> GetAsync(string url, string token, bool official)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Authorization", "Bearer " + token.Trim());
        if (!official)
        {
            req.Headers.Add("x-app-version", "1.0.0");
            req.Headers.Add("Accept", "*/*");
            req.Headers.TryAddWithoutValidation("User-Agent", Ua);
        }
        using var res = await Http.SendAsync(req);
        string body = await res.Content.ReadAsStringAsync();
        // l'API risponde sempre JSON: il motivo vero sta in "msg" / "error.message", non nel codice HTTP
        if (!res.IsSuccessStatusCode) throw new Exception(Message(body, (int)res.StatusCode));
        return body;
    }

    static string Message(string body, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String)
                    return Friendly(m.GetString() ?? "");
                if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                    && e.TryGetProperty("message", out var em) && em.ValueKind == JsonValueKind.String)
                    return Friendly(em.GetString() ?? "");
            }
        }
        catch { }
        return $"HTTP {status}";
    }

    /// <summary>Errore annidato nel corpo ({"code":40003,"msg":"...","data":null}) oppure null.</summary>
    static string? BodyError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number
                || code.GetInt32() == 0) return null;
            string msg = root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? "" : "";
            return msg.Length > 0 ? Friendly(msg) : $"errore API {code.GetInt32()}";
        }
        catch { return null; }
    }

    static string Friendly(string msg)
        => msg.Contains("invalid token", StringComparison.OrdinalIgnoreCase)
           || msg.Contains("Authorization Failed", StringComparison.OrdinalIgnoreCase)
            ? "token di utilizzo non valido o scaduto: riprendilo da platform.deepseek.com (F12 → " +
              "JSON.parse(localStorage.userToken).value)"
            : msg;

    /// <summary>Importo con il simbolo della valuta davanti (¥ / $ / €), come nel monitor di riferimento.</summary>
    static string Money(double v, string currency)
    {
        string symbol = currency switch
        {
            "CNY" => "¥",
            "USD" => "$",
            "EUR" => "€",
            _ => currency.Length > 0 ? currency + " " : "$",
        };
        return symbol + v.ToString("0.00", CultureInfo.CurrentCulture);
    }

    /// <summary>Importo formattato nella valuta del saldo.</summary>
    public static string Text(double v, string currency) => Money(v, currency);

    /// <summary>Saldo, speso (ricariche − saldo), crediti omaggio, valuta, disponibilità.</summary>
    public static (double? total, double? spent, double? granted, string currency, bool available) ParseBalance(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return (null, null, null, "", false);
        bool available = root.TryGetProperty("is_available", out var av) && av.ValueKind == JsonValueKind.True;
        if (!root.TryGetProperty("balance_infos", out var infos) || infos.ValueKind != JsonValueKind.Array
            || infos.GetArrayLength() == 0)
            return (null, null, null, "", available);
        var first = infos[0];
        if (first.ValueKind != JsonValueKind.Object) return (null, null, null, "", available);
        double? total = first.TryGetProperty("total_balance", out var t) ? Num(t) : null;
        double? topped = first.TryGetProperty("topped_up_balance", out var tp) ? Num(tp) : null;
        double? granted = first.TryGetProperty("granted_balance", out var g) ? Num(g) : null;
        string currency = first.TryGetProperty("currency", out var c) ? c.GetString() ?? "" : "";
        double? spent = total.HasValue && topped.HasValue ? Math.Max(0, topped.Value - total.Value) : null;
        return (total, spent, granted, currency, available);
    }

    /// <summary>Riempie modelli e giorni del mese dalle due risposte JSON.</summary>
    public static void Parse(string amountJson, string costJson, DeepSeekUsage u, DateTime month)
    {
        var costs = CostByModel(costJson);
        var costDays = CostByDay(costJson);
        var total = new List<(string model, List<(string type, double amount)> usage)>();
        var days = new List<(string date, List<(string model, List<(string type, double amount)> usage)> data)>();
        using (var doc = JsonDocument.Parse(amountJson))
        {
            if (!Data(doc.RootElement, out var biz)) return;
            if (biz.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Array)
                foreach (var m in t.EnumerateArray()) total.Add(ModelUsage(m));
            if (biz.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Array)
                foreach (var day in d.EnumerateArray())
                {
                    var list = new List<(string, List<(string, double)>)>();
                    if (day.TryGetProperty("data", out var mlist) && mlist.ValueKind == JsonValueKind.Array)
                        foreach (var m in mlist.EnumerateArray()) list.Add(ModelUsage(m));
                    days.Add((day.TryGetProperty("date", out var ds) ? ds.GetString() ?? "" : "", list));
                }
        }

        foreach (var (model, usage) in total)
        {
            var (tokens, requests, hit, miss, response) = Breakdown(usage);
            u.Models.Add(new DsModel
            {
                Key = model,
                Name = ShortName(model),
                Tokens = tokens,
                Requests = requests,
                Hit = hit,
                Miss = miss,
                Out = response,
                Cost = costs.TryGetValue(model, out var c) ? c : 0,
            });
        }
        u.Models.Sort((a, b) => b.Tokens.CompareTo(a.Tokens));

        foreach (var (date, data) in days)
        {
            var day = new DsDay { Date = date, Label = DayLabel(date, month) };
            foreach (var (model, usage) in data)
            {
                var (tokens, _, hit, miss, response) = Breakdown(usage);
                day.Hit += hit;
                day.Miss += miss;
                day.Out += response;
                day.Tokens += tokens;
            }
            if (costDays.TryGetValue(date, out var cd)) day.Cost = cd;
            u.Days.Add(day);
        }
        u.Days = u.Days.Where(d => d.Tokens > 0 || d.Cost > 0).ToList();
        while (u.Days.Count > 0 && u.Days[^1].Tokens == 0 && u.Days[^1].Cost == 0) u.Days.RemoveAt(u.Days.Count - 1);

        u.MonthValue = u.Models.Sum(m => m.Cost);
        u.MonthCost = Money(u.MonthValue, u.Currency);
        var today = u.Days.FirstOrDefault(d => d.Date == month.ToString("yyyy-MM-dd"));
        if (today != null)
        {
            u.TodayValue = today.Cost;
            u.TodayCost = Money(today.Cost, u.Currency);
        }
    }

    /// <summary>(token totali, richieste, cache hit, cache miss, output).</summary>
    public static (double tokens, double requests, double hit, double miss, double response)
        Breakdown(List<(string type, double amount)> usage)
    {
        double tokens = 0, requests = 0, hit = 0, miss = 0, response = 0;
        foreach (var (type, amount) in usage)
        {
            switch (type)
            {
                case "REQUEST": requests = amount; break;
                case "PROMPT_CACHE_HIT_TOKEN": hit = amount; tokens += amount; break;
                case "PROMPT_CACHE_MISS_TOKEN": miss = amount; tokens += amount; break;
                case "RESPONSE_TOKEN": response = amount; tokens += amount; break;
                case "PROMPT_TOKEN": tokens += amount; break;
            }
        }
        return (tokens, requests, hit, miss, response);
    }

    /// <summary>Nel JSON del costo i valori sono importi: si escludono le richieste.</summary>
    static Dictionary<string, double> CostByModel(string json)
    {
        var res = new Dictionary<string, double>();
        using var doc = JsonDocument.Parse(json);
        if (!Data(doc.RootElement, out var biz) || !biz.TryGetProperty("total", out var t)
            || t.ValueKind != JsonValueKind.Array) return res;
        foreach (var m in t.EnumerateArray())
        {
            string model = m.TryGetProperty("model", out var mv) ? mv.GetString() ?? "" : "";
            double sum = 0;
            if (m.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Array)
                foreach (var e in usage.EnumerateArray())
                    if (!IsRequest(e)) sum += Amount(e);
            res[model] = sum;
        }
        return res;
    }

    static Dictionary<string, double> CostByDay(string json)
    {
        var res = new Dictionary<string, double>();
        using var doc = JsonDocument.Parse(json);
        if (!Data(doc.RootElement, out var biz) || !biz.TryGetProperty("days", out var days)
            || days.ValueKind != JsonValueKind.Array) return res;
        foreach (var day in days.EnumerateArray())
        {
            string date = day.TryGetProperty("date", out var ds) ? ds.GetString() ?? "" : "";
            double sum = 0;
            if (day.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var m in data.EnumerateArray())
                    if (m.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Array)
                        foreach (var e in usage.EnumerateArray())
                            if (!IsRequest(e)) sum += Amount(e);
            res[date] = sum;
        }
        return res;
    }

    /// <summary>data.biz_data è un oggetto in /amount e un array in /cost: accetta entrambi.</summary>
    static bool Data(JsonElement root, out JsonElement biz)
    {
        biz = default;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return false;
        if (!data.TryGetProperty("biz_data", out var b)) return false;
        if (b.ValueKind == JsonValueKind.Array)
        {
            if (b.GetArrayLength() == 0) return false;
            b = b[0];
        }
        if (b.ValueKind != JsonValueKind.Object) return false;
        biz = b;
        return true;
    }

    static (string model, List<(string type, double amount)> usage) ModelUsage(JsonElement m)
    {
        if (m.ValueKind != JsonValueKind.Object) return ("", new List<(string, double)>());
        string model = m.TryGetProperty("model", out var mv) ? mv.GetString() ?? "" : "";
        var usage = new List<(string, double)>();
        if (m.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Array)
            foreach (var e in u.EnumerateArray())
                usage.Add((e.TryGetProperty("type", out var tv) ? tv.GetString() ?? "" : "", Amount(e)));
        return (model, usage);
    }

    static bool IsRequest(JsonElement e)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("type", out var tv) && tv.GetString() == "REQUEST";

    static double Amount(JsonElement e)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("amount", out var a) ? Num(a) ?? 0 : 0;

    static double? Num(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.String => double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null,
        _ => null,
    };

    /// <summary>Nome leggibile per il modello (DeepSeek cambia i nomi con le versioni).</summary>
    public static string ShortName(string model) => model switch
    {
        "deepseek-v4-flash" => "V4 Flash",
        "deepseek-v4-pro" => "V4 Pro",
        "deepseek-chat" => "Chat",
        "deepseek-reasoner" => "Reasoner",
        _ => model.StartsWith("deepseek-", StringComparison.Ordinal) ? model["deepseek-".Length..] : model,
    };

    static string DayLabel(string date, DateTime month)
        => DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? $"{d.Day}/{d.Month}" : date;
}

sealed class DsModel
{
    public string Key = "", Name = "";
    public double Tokens, Requests, Hit, Miss, Out, Cost;
    public double HitPct => Tokens > 0 ? Hit * 100 / Tokens : 0;
    public double MissPct => Tokens > 0 ? Miss * 100 / Tokens : 0;
    public double OutPct => Tokens > 0 ? Out * 100 / Tokens : 0;
}

sealed class DsDay
{
    public string Date = "", Label = "";
    public double Hit, Miss, Out, Cost, Tokens;
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
