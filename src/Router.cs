// Integração com o roteador Nokia (GPON) da casa. SÓ LEITURA: nunca envia nada que altere configuração.
// 1) Sem login: UPnP dá o total de bytes da internet da casa toda.
// 2) Com login: entra no painel web do mesmo jeito que o navegador (RSA + AES) e lê as páginas de status.
//    A senha fica no PC protegida pelo Windows (DPAPI, só este usuário do Windows consegue abrir).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Aura {
static class Router {
    public static string Host = "10.0.0.1"; // trocado pelo gateway detectado na coleta
    static readonly object gate = new object();

    // ------------------------------------------------------------ UPnP: internet da casa toda (sem login)
    public static double HouseDown, HouseUp; public static bool UpnpOk;
    static long lastRx = -1, lastTx; static DateTime lastT, upnpRetry;

    // Diferença entre duas leituras de um contador de 32 bits do roteador. -1 = roteador reiniciou (contador zerou).
    public static long Delta(long now, long before) {
        if (now >= before) return now - before;
        if (before > 3000000000L) return now + 4294967296L - before; // deu a volta em 4 GB
        return -1;
    }

    static long Soap(string action) {
        const string svc = "urn:schemas-upnp-org:service:WANCommonInterfaceConfig:1";
        // Proxy = null: o roteador é local, e procurar proxy atrasa a primeira consulta em segundos.
        // KeepAlive = false: o UPnP do roteador fecha a conexão depois de cada resposta (reaproveitar dava erro).
        var rq = (HttpWebRequest)WebRequest.Create("http://" + Host + ":49152/upnp/control/WANCommonIFC1");
        rq.Proxy = null; rq.KeepAlive = false; rq.Method = "POST"; rq.Timeout = 1500; rq.ContentType = "text/xml; charset=\"utf-8\"";
        rq.Headers["SOAPACTION"] = "\"" + svc + "#" + action + "\"";
        byte[] b = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:" + action + " xmlns:u=\"" + svc + "\"/></s:Body></s:Envelope>");
        using (var s = rq.GetRequestStream()) s.Write(b, 0, b.Length);
        using (var r = rq.GetResponse()) using (var sr = new StreamReader(r.GetResponseStream())) {
            var m = Regex.Match(sr.ReadToEnd(), @"<New\w+>(\d+)</New\w+>");
            return m.Success ? long.Parse(m.Groups[1].Value) : -1;
        }
    }

    public static void TickUpnp() {
        if (DateTime.UtcNow < upnpRetry) return;
        long rx, tx;
        try { rx = Soap("GetTotalBytesReceived"); tx = Soap("GetTotalBytesSent"); }
        catch (Exception e) { Program.Log(new Exception("UPnP do roteador falhou (nova tentativa em 1 min)", e)); rx = tx = -1; }
        if (rx < 0 || tx < 0) { UpnpOk = false; upnpRetry = DateTime.UtcNow.AddSeconds(60); lastRx = -1; return; } // sem UPnP: tenta de novo em 1 min
        DateTime now = DateTime.UtcNow;
        if (lastRx >= 0) {
            long dr = Delta(rx, lastRx), dt = Delta(tx, lastTx); double s = (now - lastT).TotalSeconds;
            if (dr >= 0 && dt >= 0 && s > 0.5 && s < 60) { HouseDown = dr / s; HouseUp = dt / s; UpnpOk = true; }
        }
        lastRx = rx; lastTx = tx; lastT = now;
    }

    // ------------------------------------------------------------ Login guardado
    static string PassFile { get { return Path.Combine(Usage.Dir, "roteador.dat"); } }
    public static string User { get { return Settings.Get("router_user", ""); } }
    public static bool HasLogin { get { return User != "" && File.Exists(PassFile); } }
    public static string Status = "";
    // Etapa atual da leitura, mostrada na aba Roteador ("" = parado). Lida pela tela sem trava: só troca a referência.
    public static volatile string Phase = "Conectando ao roteador…"; public static bool LastOk; public static DateTime OkAt;
    static bool loggedIn, refused; // refused: não tenta de novo sozinho (evita bloquear a conta por tentativas)

    public static void SaveLogin(string user, string pass) {
        lock (gate) {
            Settings.Set("router_user", user.Trim());
            Directory.CreateDirectory(Usage.Dir);
            File.WriteAllBytes(PassFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(pass), null, DataProtectionScope.CurrentUser));
            loggedIn = refused = stuck = false; info = new List<Row>(); readAt = DateTime.MinValue; Status = ""; Phase = "Conectando ao roteador…";
        }
    }
    public static void Forget() {
        lock (gate) { try { File.Delete(PassFile); } catch { } Settings.Set("router_user", ""); loggedIn = stuck = false; info = new List<Row>(); readAt = DateTime.MinValue; Status = ""; }
    }
    static string Pass() {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PassFile), null, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }

    // ------------------------------------------------------------ Criptografia igual à do painel (crypto_page.js + sjcl + JSEncrypt)
    static readonly RandomNumberGenerator rng = RandomNumberGenerator.Create();
    static byte[] Rand(int n) { var b = new byte[n]; rng.GetBytes(b); return b; }
    public static string Esc(string b64) { return b64.Replace('+', '-').Replace('/', '_').Replace('=', '.'); }        // base64url_escape
    static string B64Url(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); } // sjcl base64url (sem '=')

    static int Len(byte[] d, ref int i) { int l = d[i++]; if (l < 0x80) return l; int n = l & 0x7F; l = 0; while (n-- > 0) l = (l << 8) | d[i++]; return l; }
    static byte[] Int(byte[] d, ref int i) {
        if (d[i++] != 0x02) throw new FormatException("chave publica inesperada");
        int l = Len(d, ref i);
        while (l > 1 && d[i] == 0) { i++; l--; }
        var r = new byte[l]; Buffer.BlockCopy(d, i, r, 0, l); i += l; return r;
    }
    // Chave pública PEM (SubjectPublicKeyInfo) -> módulo e expoente
    public static RSAParameters ParsePem(string b64) {
        byte[] d = Convert.FromBase64String(b64); int i = 0, l;
        i++; Len(d, ref i);                 // SEQUENCE
        i++; l = Len(d, ref i); i += l;     // AlgorithmIdentifier (pula)
        i++; Len(d, ref i); i++;            // BIT STRING + byte de bits não usados
        i++; Len(d, ref i);                 // SEQUENCE { n, e }
        return new RSAParameters { Modulus = Int(d, ref i), Exponent = Int(d, ref i) };
    }

    public static string EncryptPost(string pemB64, string plain) {
        byte[] key = Rand(16), iv = Rand(16), ct, ck;
        using (var aes = new AesCryptoServiceProvider { Key = key, IV = iv, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
        using (var enc = aes.CreateEncryptor()) { var p = Encoding.UTF8.GetBytes(plain); ct = enc.TransformFinalBlock(p, 0, p.Length); }
        using (var rsa = new RSACryptoServiceProvider()) {
            rsa.ImportParameters(ParsePem(pemB64));
            ck = rsa.Encrypt(Encoding.UTF8.GetBytes(Convert.ToBase64String(key) + " " + Convert.ToBase64String(iv)), false); // PKCS#1 v1.5, igual ao JSEncrypt
        }
        return "encrypted=1&ct=" + B64Url(ct) + "&ck=" + Esc(Convert.ToBase64String(ck));
    }

    static byte[] decKey, decIv; // chave que mandamos no login para o roteador cifrar as respostas
    public static string Decrypt(string text, byte[] key, byte[] iv) {
        string t = text.Trim();
        if (key == null || t.Length < 24 || t.Length % 4 != 0 || !Regex.IsMatch(t, "^[A-Za-z0-9+/=]+$")) return text;
        try {
            using (var aes = new AesCryptoServiceProvider { Key = key, IV = iv, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
            using (var dec = aes.CreateDecryptor()) { var b = Convert.FromBase64String(t); return Encoding.UTF8.GetString(dec.TransformFinalBlock(b, 0, b.Length)); }
        } catch { return text; }
    }
    // Respostas podem vir inteiras cifradas, ou com trechos `aes_decrypt(sid, "...")` dentro do JavaScript
    static string Plain(string s) {
        s = Decrypt(s, decKey, decIv);
        return Regex.Replace(s, @"aes_decrypt\([^,()]*,\s*[""']([A-Za-z0-9+/=]+)[""']\)", m => "\"" + Decrypt(m.Groups[1].Value, decKey, decIv).Replace("\"", "\\\"") + "\"");
    }

    // ------------------------------------------------------------ HTTP com sessão
    // Cookies guardados à mão: o CookieContainer do .NET descarta em silêncio cookies com atributos fora do padrão
    static Dictionary<string, string> jar = new Dictionary<string, string>();
    static string lastHeaders = "";
    static string Http(string path, string post, out int status) {
        var rq = (HttpWebRequest)WebRequest.Create("http://" + Host + path);
        rq.Proxy = null; rq.KeepAlive = false; rq.Timeout = 8000; rq.AllowAutoRedirect = false;
        rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Aura"; rq.Referer = "http://" + Host + "/";
        if (jar.Count > 0) rq.Headers["Cookie"] = string.Join("; ", jar.Select(kv => kv.Key + "=" + kv.Value));
        if (post != null) {
            rq.Method = "POST"; rq.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
            rq.Headers["X-Requested-With"] = "XMLHttpRequest";
            byte[] b = Encoding.ASCII.GetBytes(post);
            using (var s = rq.GetRequestStream()) s.Write(b, 0, b.Length);
        }
        HttpWebResponse r;
        try { r = (HttpWebResponse)rq.GetResponse(); }
        catch (WebException e) { r = e.Response as HttpWebResponse; if (r == null) throw; }
        using (r) using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) {
            status = (int)r.StatusCode;
            var cookies = r.Headers.GetValues("Set-Cookie");
            if (cookies != null) foreach (var c in cookies) {
                string kv = c.Split(';')[0]; int eq = kv.IndexOf('=');
                if (eq > 0) jar[kv.Substring(0, eq).Trim()] = kv.Substring(eq + 1).Trim();
            }
            // Para diagnóstico: nomes dos cabeçalhos e dos cookies, nunca os valores
            lastHeaders = status + " " + path + " | " + string.Join(", ", r.Headers.AllKeys) + " | cookies: " + string.Join(", ", jar.Keys) + " | location: " + r.Headers["Location"];
            return sr.ReadToEnd();
        }
    }
    static void Diag(string line) {
        try { Directory.CreateDirectory(DumpDir); File.AppendAllText(Path.Combine(DumpDir, "_diag.txt"), DateTime.Now.ToString("HH:mm:ss ") + line + "\r\n"); } catch { }
    }
    static string Grab(string s, string pattern) { var m = Regex.Match(s ?? "", pattern); return m.Success ? m.Groups[1].Value : null; }
    // A página inicial depois do login também traz a chave pública (para cifrar formulários): o que distingue é o formulário de login
    static bool IsLoginPage(string s) { return s != null && s.Contains("id=\"loginform\"") && s.Contains("var nonce"); }

    static bool Login() {
        string user = User, pass = Pass();
        if (user == "" || pass == null) { Status = "Nenhum login salvo."; return false; }
        if (refused) return false;
        Phase = "Entrando no painel do roteador (login)…";
        jar = new Dictionary<string, string>(); int st;
        string page = Http("/", null, out st);
        string nonce = Grab(page, @"var nonce\s*=\s*""([^""]*)"""), token = Grab(page, @"var token\s*=\s*""([^""]*)""");
        string pem = Grab(page, @"-----BEGIN PUBLIC KEY-----([\s\S]*?)-----END");
        if (nonce == null || token == null || pem == null) { Status = "A página de login do roteador é diferente da esperada (o provedor pode ter atualizado o roteador)."; refused = true; return false; }
        pem = Regex.Replace(pem, @"[\\\s]", "");
        decKey = Rand(16); decIv = Rand(16);
        string plain = "&username=" + user + "&password=" + Uri.EscapeDataString(pass) + "&csrf_token=" + token + "&nonce=" + nonce
                     + "&enckey=" + Esc(Convert.ToBase64String(decKey)) + "&enciv=" + Esc(Convert.ToBase64String(decIv));
        string resp = Http("/login.cgi", EncryptPost(pem, plain), out st);
        Diag("login: " + lastHeaders);
        if (st == 299) { loggedIn = true; Status = "Conectado ao roteador como " + user + "."; return true; }
        refused = true; loggedIn = false;
        // A página conta as tentativas erradas em `var err_t=[n]`; o painel bloqueia depois de 4
        string n = Grab(resp, @"var err_t\s*=\s*\[\s*(\d+)");
        Status = "O roteador recusou o login" + (n == null ? "" : " (tentativa errada " + n + " de 4; depois da 4ª o roteador bloqueia o login por um tempo)")
               + ". O Aura não tenta de novo sozinho: confira usuário e senha e clique em Entrar.";
        return false;
    }

    static bool stuck; // entrou mas as páginas continuam pedindo login: para de tentar até salvar o login de novo
    static string Get(string path) {
        if (stuck || (!loggedIn && !Login())) return null;
        int st; string s = Http(path, null, out st);
        if (IsLoginPage(s)) { // sessão expirou: entra de novo uma vez
            Diag("pediu login de novo: " + lastHeaders); Dump("_pediu_login", s);
            loggedIn = false;
            if (!Login()) return null;
            s = Http(path, null, out st);
            if (IsLoginPage(s)) {
                Diag("continua pedindo login: " + lastHeaders);
                stuck = true;
                Status = "O roteador aceitou o login, mas não abriu as páginas para o Aura. O Aura parou de tentar para não encher o roteador de logins.";
                return null;
            }
        }
        return Plain(s);
    }

    // ------------------------------------------------------------ Descoberta das páginas (só as de status)
    // Qualquer página com cara de ação fica de fora: um GET em "reboot.cgi" reiniciaria o roteador.
    static readonly string[] Danger = { "set", "save", "apply", "reboot", "reset", "restore", "upgrade", "logout", "del", "add", "factory", "backup",
        "commit", "write", "restart", "wps", "ping", "trace", "diag", "change", "modify", "edit", "remove", "clear", "update", "cfg", "config", "upload", "download", "action", "login", "out" };
    public static bool SafePage(string p) { string low = p.ToLowerInvariant(); return !Danger.Any(low.Contains); }
    // Páginas de status do menu do Nokia G-1425G-A (lista fixa: é mais seguro que adivinhar pelo nome o que é só leitura)
    public static readonly string[] StatusPages = { "/device_status.cgi", "/wan_status.cgi?gpon", "/show_wan_status.cgi?ipv4", "/lan_status.cgi?wlan", "/sta_info.cgi" };
    static List<Row> info = new List<Row>(); static DateTime readAt; static bool dumped;
    public static Dictionary<string, string> Names = new Dictionary<string, string>(); // IP -> nome no roteador (usado na aba Dispositivos)
    static Dictionary<string, long[]> lastWifi = new Dictionary<string, long[]>(); static DateTime lastWifiAt;
    public static string DumpDir { get { return Path.Combine(Usage.Dir, "roteador"); } }

    // Tira senhas e chaves do que é salvo em disco para análise
    public static string Redact(string s) {
        s = Regex.Replace(s, @"((?:pass|psk|pwd|secret|key)\w*[""']?\s*[:=]\s*[""'])([^""']*)([""'])", "$1***$3", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(<input[^>]*(?:type|name|id)\s*=\s*[""']?[^""'>]*(?:pass|psk|pwd|key)[^>]*value\s*=\s*[""'])([^""']*)", "$1***", RegexOptions.IgnoreCase);
        return s;
    }
    static void Dump(string name, string s) {
        try { Directory.CreateDirectory(DumpDir); File.WriteAllText(Path.Combine(DumpDir, Regex.Replace(name, @"[^\w\-]", "_") + ".txt"), Redact(s), Encoding.UTF8); } catch { }
    }

    // ------------------------------------------------------------ Leitura dos dados: objetos JavaScript dentro das páginas (não é JSON)
    // Recorta `var nome = {...}` ou `[...]` contando chaves e pulando o que está entre aspas
    public static string Block(string s, string name) {
        var m = Regex.Match(s ?? "", @"var\s+" + name + @"\s*=\s*([\[\{])");
        if (!m.Success) return null;
        int depth = 0, start = m.Groups[1].Index; char q = '\0';
        for (int j = start; j < s.Length; j++) {
            char c = s[j];
            if (q != '\0') { if (c == '\\') j++; else if (c == q) q = '\0'; continue; }
            if (c == '"' || c == '\'') q = c;
            else if (c == '{' || c == '[') depth++;
            else if ((c == '}' || c == ']') && --depth == 0) return s.Substring(start, j - start + 1);
        }
        return null;
    }
    public static string Str(string s, string key) { var m = Regex.Match(s ?? "", @"[""']?\b" + key + @"[""']?\s*:\s*([""'])(.*?)\1"); return m.Success ? m.Groups[2].Value : null; }
    public static double Num(string s, string key) {
        var m = Regex.Match(s ?? "", @"[""']?\b" + key + @"[""']?\s*:\s*[""']?(-?\d+(?:\.\d+)?)");
        return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : double.NaN;
    }
    // Divide uma lista de objetos em pedaços, um por ocorrência de `key` (o campo que abre cada item)
    public static List<string> Items(string block, string key) {
        var l = new List<string>(); if (block == null) return l;
        var ms = Regex.Matches(block, @"[""']?\b" + key + @"[""']?\s*:");
        for (int i = 0; i < ms.Count; i++) l.Add(block.Substring(ms[i].Index, (i + 1 < ms.Count ? ms[i + 1].Index : block.Length) - ms[i].Index));
        return l;
    }
    public static double Dbm(double raw) { return 10 * Math.Log10(raw * 0.00001); } // mesma conta do painel (to_dbm)
    public static string Dur(double sec) {
        if (double.IsNaN(sec)) return "?";
        if (sec >= 172800) return Math.Floor(sec / 86400) + " dias";
        if (sec >= 86400) return "1 dia e " + Math.Floor(sec % 86400 / 3600) + " h";
        if (sec >= 3600) return Math.Floor(sec / 3600) + " h " + Math.Floor(sec % 3600 / 60) + " min";
        return Math.Max(1, Math.Floor(sec / 60)) + " min";
    }
    public static bool Cgnat(string ip) { var p = (ip ?? "").Split('.'); int b; return p.Length == 4 && p[0] == "100" && int.TryParse(p[1], out b) && b >= 64 && b <= 127; }

    public static List<Row> Parse(Dictionary<string, string> pg, double dt) {
        var rows = new List<Row>(); var names = new Dictionary<string, string>();
        // ---- Fibra óptica
        string gpon = Block(pg["/wan_status.cgi?gpon"], "gpon_status");
        double rawRx = Num(gpon, "RXPower"), rx = Dbm(rawRx), tx = Dbm(Num(gpon, "TXPower")), ot = Num(gpon, "TransceiverTemperature") / 256;
        string fv; int fl;
        if (!(rawRx > 0)) { fv = "Sem sinal"; fl = 2; }
        else if (rx > -8) { fv = "Forte demais"; fl = 1; }
        else if (rx >= -25) { fv = "Bom"; fl = 0; }
        else if (rx >= -27) { fv = "Fraco"; fl = 1; }
        else { fv = "Ruim"; fl = 2; }
        rows.Add(R("Sinal da fibra óptica", rawRx > 0 ? "Recebendo " + rx.ToString("0.0") + " dBm  ·  enviando " + tx.ToString("0.0") + " dBm  ·  módulo a " + ot.ToString("0") + " °C" : "Nenhuma luz chegando",
            "É a luz que chega do provedor pela fibra. Faixa boa: de -8 a -25 dBm (quanto mais perto de -8, mais forte). Abaixo de -27 a internet cai ou fica instável: "
            + "aí é fibra dobrada, suja ou conector ruim e precisa de técnico. " + (fl == 0 ? "O seu está ótimo, a fibra não é a causa de nenhuma lentidão." : fl == 1 && rx > -8 ? "Sinal acima de -8 pode saturar o receptor: vale avisar o suporte." : "O seu está fora da faixa boa: vale chamar o suporte."),
            fv, Kb.LevelColor(fl), fl));
        // ---- Internet (WAN)
        string wan = Items(Block(pg["/show_wan_status.cgi?ipv4"], "wan_conns"), "_type").FirstOrDefault(c => Str(c, "ConnectionStatus") == "Connected" && !string.IsNullOrEmpty(Str(c, "ExternalIPAddress")));
        if (wan == null) rows.Add(R("Internet do provedor", "Desconectada", "O roteador não está com a conexão de internet ativa agora.", "Caiu", Kb.Red, 2));
        else {
            string ip = Str(wan, "ExternalIPAddress"), v6 = Str(wan, "X_CT_COM_IPv6IPAddress"), dns = Str(wan, "DNSServers");
            rows.Add(R("Internet do provedor", "IP " + ip + "  ·  conectada há " + Dur(Num(wan, "Uptime")) + (string.IsNullOrEmpty(v6) ? "" : "  ·  IPv6 ativo"),
                (Cgnat(ip) ? "Seu IP de internet é compartilhado com outros clientes do provedor (CGNAT, faixa 100.64 a 100.127). Bom para segurança: ninguém de fora consegue abrir conexão direto para a sua casa. "
                           + "Lado ruim: abrir portas (servidor de jogo, câmera, acesso remoto) não funciona por IPv4; para isso é preciso pedir IP público ao provedor ou usar IPv6."
                           : "IP público: aparelhos que abrirem portas no roteador (UPnP ou redirecionamento) ficam acessíveis pela internet.")
                + (string.IsNullOrEmpty(dns) ? "" : " DNS entregue pelo provedor: " + dns.Replace(",", ", ") + "."),
                "Conectada", Kb.Green, 0));
        }
        // ---- O próprio roteador
        string dev = pg["/device_status.cgi"], di = Block(dev, "dev_info"), mem = Block(dev, "mem_info");
        double cpuT = Num(Block(dev, "cpu_temperatureinfo"), "CPUTemp"), total = Num(mem, "Total"), used = 100 * (total - Num(mem, "Free")) / total;
        rows.Add(R("Roteador " + (Str(di, "Manufacturer") == "ALCL" ? "Nokia " : "") + Str(di, "ModelName"),
            "Ligado há " + Dur(Num(di, "UpTime")) + (double.IsNaN(cpuT) ? "" : "  ·  processador a " + cpuT.ToString("0") + " °C") + (double.IsNaN(used) ? "" : "  ·  memória " + used.ToString("0") + "% em uso"),
            "Modem de fibra com Wi-Fi instalado pelo provedor. Ficar dias sem reiniciar é normal: só reinicie se algo estiver errado. "
            + (cpuT > 85 ? "O processador está quente demais: deixe o aparelho num lugar ventilado, longe de outros eletrônicos." : "Temperatura normal para esse aparelho (preocupa acima de uns 85 °C)."),
            cpuT > 85 ? "Quente" : "Ok", cpuT > 85 ? Kb.Yellow : Kb.Green, cpuT > 85 ? 1 : 0));
        // ---- Aparelhos (lista do roteador + sinal e consumo de quem está no Wi-Fi)
        var wifi = new Dictionary<string, string>(); var now = new Dictionary<string, long[]>();
        foreach (var c in Items(Block(pg["/sta_info.cgi"], "wlan_client"), "SSID")) {
            string mac = (Str(c, "AssociatedDeviceMACAddress") ?? "").ToLowerInvariant(); if (mac == "") continue;
            wifi[mac] = c; now[mac] = new[] { (long)Num(c, "data_sent_next"), (long)Num(c, "data_receive_next") };
        }
        var devs = new List<Row>();
        foreach (var d in Items(Block(pg["/lan_status.cgi?wlan"], "device_cfg"), "HostName")) {
            string host = Str(d, "HostName"), ip = Str(d, "IPAddress"), mac = (Str(d, "MACAddress") ?? "").ToLowerInvariant(), type = Str(d, "InterfaceType") ?? "";
            if (ip == null) continue;
            bool online = Regex.IsMatch(d, @"AddressSource\s*:\s*'\w*'\s*,\s*Active\s*:\s*1"), cable = type == "Ethernet";
            bool noName = string.IsNullOrEmpty(host) || host.StartsWith("Unknown"), me = !noName && host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            bool random = mac.Length > 1 && (Convert.ToInt32(mac.Substring(0, 2), 16) & 2) != 0;
            if (!noName) names[ip] = host;
            string w; wifi.TryGetValue(mac, out w); if (!online) w = null;
            double sig = Num(w, "SignalStrength"), ch = Num(w, "Channel");
            string sub = ip + "  ·  " + (cable ? "cabo" : "Wi-Fi " + (ch > 14 ? "5 GHz" : type == "802.11ac" ? "5 GHz" : "2,4 GHz"));
            string text = me ? "O computador que você está usando agora. " :
                Regex.IsMatch(host ?? "", "iphone|ipad|macbook", RegexOptions.IgnoreCase) ? "Aparelho da Apple. " :
                Regex.IsMatch(host ?? "", "android|galaxy|redmi|xiaomi|motorola|moto", RegexOptions.IgnoreCase) ? "Celular Android. " :
                noName && random ? "Aparelho que não informa o nome e usa endereço físico aleatório: celulares e tablets modernos fazem isso por privacidade. Provavelmente um celular ou tablet da casa; se ninguém reconhecer, troque a senha do Wi-Fi. " :
                noName ? "Aparelho que não informa o nome (TV, lâmpada, câmera, tomada inteligente costumam ser assim). Se ninguém reconhecer, troque a senha do Wi-Fi. " :
                cable ? "Computador ou aparelho ligado por cabo. " : "";
            string badge = !online ? "Offline" : cable ? "Cabo" : "Wi-Fi"; var color = !online ? Kb.Gray : cable ? Kb.Blue : Kb.Green; int level = 0;
            if (!online) text += "Não está conectado agora; o roteador ainda lembra dele.";
            if (w != null && sig < 0) {
                sub += "  ·  sinal " + sig.ToString("0") + " dBm";
                text += sig >= -50 ? "Sinal Wi-Fi ótimo" : sig >= -65 ? "Sinal Wi-Fi bom" : sig >= -75 ? "Sinal Wi-Fi fraco: longe do roteador ou com paredes no caminho, pode ficar lento" : "Sinal Wi-Fi muito fraco: vai ficar lento e cair";
                text += " (rede \"" + Str(w, "SSID") + "\", canal " + ch + ", link de " + (Num(w, "LastDataDownlinkRate") / 1000).ToString("0") + " Mbps). ";
                if (sig < -65) { badge = "Sinal fraco"; color = Kb.Yellow; level = 1; }
                long[] a = now[mac], b;
                text += "Desde que conectou (" + Dur(Num(w, "X_ASB_COM_ConnectedTime")) + "): baixou " + Fmt.Size(a[0]) + " e enviou " + Fmt.Size(a[1]) + ".";
                if (dt > 5 && dt < 120 && lastWifi.TryGetValue(mac, out b)) {
                    long dd = Delta(a[0], b[0]), du = Delta(a[1], b[1]);
                    if (dd >= 0 && du >= 0) sub += "  ·  ↓ " + Fmt.Speed(dd / dt) + "  ↑ " + Fmt.Speed(du / dt);
                }
            } else if (online && !cable) text += "Consumo só aparece para quem o roteador lista no Wi-Fi.";
            else if (cable) text += "O roteador não informa consumo por aparelho no cabo, só no Wi-Fi.";
            devs.Add(R((noName ? "Aparelho sem nome" : host) + (me ? " (este PC)" : ""), sub + "  ·  MAC " + mac, text, badge, color, level));
        }
        rows.AddRange(devs.OrderBy(r => r.Badge == "Offline").ThenBy(r => r.Title));
        lastWifi = now; Names = names;
        return rows;
    }

    // Lê o roteador (no máximo a cada 15 s) e devolve as linhas da aba Roteador
    public static List<Row> Read() {
        lock (gate) {
            var rows = new List<Row>();
            if (UpnpOk) rows.Add(R("Internet da casa toda agora", "↓ " + Fmt.Speed(HouseDown) + "   ↑ " + Fmt.Speed(HouseUp), "Soma de todos os aparelhos da casa (PC, celulares, TV), medida pelo próprio roteador. Não precisa de login.", "Ao vivo", Kb.Blue, 0));
            if (!HasLogin) return rows;
            try {
                if ((DateTime.UtcNow - readAt).TotalSeconds > 15) {
                    readAt = DateTime.UtcNow; // marca antes: se falhar, não tenta de novo a cada atualização da tela
                    var pg = new Dictionary<string, string>();
                    foreach (var p in StatusPages.Where(SafePage)) {
                        Phase = "Lendo dados do painel (" + (pg.Count + 1) + " de " + StatusPages.Length + ")…";
                        string s = Get(p); if (s == null) break; pg[p] = s; if (!dumped) Dump(p, s);
                    }
                    Phase = ""; LastOk = pg.Count == StatusPages.Length;
                    if (LastOk) {
                        OkAt = DateTime.Now;
                        dumped = true;
                        info = Parse(pg, (readAt - lastWifiAt).TotalSeconds); lastWifiAt = readAt;
                    }
                }
            } catch (Exception e) { Status = "Não consegui falar com o roteador: " + e.Message; loggedIn = false; Phase = ""; LastOk = false; Program.Log(e); }
            if (!loggedIn || info.Count == 0)
                rows.Add(R(loggedIn ? "Conectado ao roteador" : "Sem conexão com o painel", User + "  ·  " + Host, loggedIn ? "Lendo os dados do painel…" : Status, loggedIn ? "Ok" : "Erro", loggedIn ? Kb.Green : Kb.Red, loggedIn ? 0 : 1));
            rows.AddRange(info);
            return rows;
        }
    }
    // Aura.exe --roteador: uma leitura, grava as linhas em roteador\_linhas.txt. Código de saída 0 = conectou.
    public static int ReadOnce() {
        Settings.Load();
        Read(); System.Threading.Thread.Sleep(16000); // duas leituras: a segunda já mostra a velocidade de cada aparelho no Wi-Fi
        var rows = Read();
        Directory.CreateDirectory(DumpDir);
        File.WriteAllLines(Path.Combine(DumpDir, "_linhas.txt"), rows.Select(r => r.Title + " | " + r.Sub + " | " + r.Badge + " | " + r.Text), Encoding.UTF8);
        return loggedIn ? 0 : 1;
    }
    static Row R(string title, string sub, string text, string badge, System.Windows.Media.Brush c, int level) {
        return new Row { Title = title, Sub = sub, Text = text, Badge = badge, Color = c, Level = level };
    }
}
}
