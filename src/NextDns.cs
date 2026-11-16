// Histórico do NextDNS (DNS na nuvem que filtra anúncios para a casa toda, mesmo com o PC desligado).
// O Aura só LÊ o registro de consultas pela API oficial (api.nextdns.io) e explica cada site com a base do Aura.
// A chave da API fica no PC protegida pelo Windows (DPAPI), igual à senha do roteador.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Aura {
static class NextDns {
    static string KeyFile { get { return Path.Combine(Usage.Dir, "nextdns.dat"); } }
    public static string Profile { get { return Settings.Get("nextdns_profile", ""); } }
    public static bool HasKey { get { return Profile != "" && File.Exists(KeyFile); } }
    public static string Status = "";
    static readonly object gate = new object();
    static List<Row> rows = new List<Row>(); static DateTime readAt;

    // O ID do perfil aparece no painel do NextDNS (ex.: "abc123") e também dentro dos endereços dele (abc123.dns.nextdns.io)
    public static string CleanProfile(string s) {
        var m = Regex.Match((s ?? "").ToLowerInvariant(), @"\b([a-f0-9]{6})\b");
        return m.Success ? m.Groups[1].Value : "";
    }

    public static void Save(string profile, string key) {
        lock (gate) {
            Settings.Set("nextdns_profile", CleanProfile(profile));
            Directory.CreateDirectory(Usage.Dir);
            File.WriteAllBytes(KeyFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
            rows = new List<Row>(); readAt = DateTime.MinValue; Status = "";
        }
    }
    public static void Forget() {
        lock (gate) { try { File.Delete(KeyFile); } catch { } Settings.Set("nextdns_profile", ""); rows = new List<Row>(); Status = ""; }
    }
    static string Key() {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyFile), null, DataProtectionScope.CurrentUser)); } catch { return null; }
    }

    // Lê as últimas 500 consultas (no máximo a cada 30 s, só com a aba aberta)
    public static List<Row> Read() {
        lock (gate) {
            if (!HasKey) return new List<Row>();
            if ((DateTime.UtcNow - readAt).TotalSeconds < 30) return rows;
            readAt = DateTime.UtcNow;
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var rq = (HttpWebRequest)WebRequest.Create("https://api.nextdns.io/profiles/" + Profile + "/logs?limit=500");
                rq.Headers["X-Api-Key"] = Key(); rq.Timeout = 10000; rq.UserAgent = "Aura";
                string json;
                using (var r = rq.GetResponse()) using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();
                Status = ""; rows = Parse(json);
            } catch (WebException e) {
                var r = e.Response as HttpWebResponse; int code = r == null ? 0 : (int)r.StatusCode;
                Status = code == 401 || code == 403 ? "O NextDNS recusou a chave da API. Confira a chave em my.nextdns.io → Conta → API e salve de novo."
                       : code == 404 ? "Perfil \"" + Profile + "\" não encontrado. O ID do perfil fica na aba Configuração do NextDNS (6 letras/números)."
                       : "Não consegui falar com o NextDNS: " + e.Message;
                rows = new List<Row> { new Row { Title = "Histórico do NextDNS indisponível", Sub = "Perfil " + Profile, Text = Status, Badge = "Erro", Color = Kb.Red, Level = 1 } };
            }
            return rows;
        }
    }

    class Agg { public string Who, Site, Example, Reason; public int Count, Blocked; public DateTime Last; }

    // Resposta da API: {"data":[{"timestamp","domain","root","status":"default|blocked|allowed","reasons":[{"name"}],"device":{"name"},"clientIp"}]}
    public static List<Row> Parse(string json) {
        var root = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
        var agg = new Dictionary<string, Agg>(); int total = 0, blocked = 0;
        object data; root.TryGetValue("data", out data);
        foreach (var o in (data as IEnumerable) ?? new object[0]) {
            var q = o as Dictionary<string, object>; if (q == null) continue;
            string domain = S(q, "domain").ToLowerInvariant(); if (domain == "") continue;
            string site = S(q, "root"); if (site == "") site = HomeDns.Site(domain);
            var dev = q.ContainsKey("device") ? q["device"] as Dictionary<string, object> : null;
            string who = dev != null && S(dev, "name") != "" ? S(dev, "name") : "Aparelho não identificado";
            bool isBlocked = S(q, "status") == "blocked";
            DateTime ts; if (!DateTime.TryParse(S(q, "timestamp"), null, System.Globalization.DateTimeStyles.RoundtripKind, out ts)) ts = DateTime.UtcNow;
            ts = ts.ToLocalTime();
            total++; if (isBlocked) blocked++;
            Agg a; string key = who + " " + site;
            if (!agg.TryGetValue(key, out a)) agg[key] = a = new Agg { Who = who, Site = site, Example = domain };
            a.Count++; if (ts > a.Last) a.Last = ts;
            if (isBlocked) {
                a.Blocked++;
                var reasons = q.ContainsKey("reasons") ? q["reasons"] as IEnumerable : null;
                if (reasons != null) a.Reason = string.Join(", ", reasons.OfType<Dictionary<string, object>>().Select(x => S(x, "name")).Where(x => x != ""));
            }
        }
        var list = new List<Row> {
            new Row { Title = "NextDNS: últimas " + total + " consultas da casa", Sub = "Perfil " + "{0}" + "  ·  " + blocked + " bloqueadas" + (total > 0 ? " (" + (100 * blocked / total) + "%)" : "") + "  ·  atualiza a cada 30 s",
                Text = "Histórico lido do NextDNS, o DNS na nuvem que filtra para os aparelhos configurados nele, mesmo com o PC desligado. Mostra só o nome dos sites, nunca o conteúdo. "
                     + "Aparelho \"não identificado\" = usa o NextDNS sem o perfil instalado (pelo roteador, por exemplo); com o perfil do NextDNS no iPhone, aparece o nome dele.",
                Badge = "Ligado", Color = Kb.Green, Level = 0 } };
        list[0].Sub = list[0].Sub.Replace("{0}", Profile == "" ? "?" : Profile);
        if (total == 0) list.Add(new Row { Title = "Nenhuma consulta no histórico ainda", Sub = "Aguardando", Text = "Configure um aparelho para usar o NextDNS e navegue um pouco. Se já configurou, confira em my.nextdns.io → Configuração se o registro (Logs) está ligado.", Badge = "Vazio", Color = Kb.Gray, Level = 0 });
        list.AddRange(agg.Values.OrderByDescending(a => a.Last).Take(300)
            .Select(a => HomeDns.SiteRow(a.Site, a.Example, a.Who, a.Count, a.Blocked, a.Last, "Bloqueado pelo NextDNS" + (string.IsNullOrEmpty(a.Reason) ? "" : " (" + a.Reason + ")"))));
        return list;
    }
    static string S(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }
}
}
