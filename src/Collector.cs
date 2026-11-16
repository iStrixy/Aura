// Coleta o estado da rede e transforma em linhas explicadas (roda em thread de fundo, só com a janela de rede aberta).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Aura {

class Row {
    public string Title { get; set; }
    public string Sub { get; set; }
    public string Text { get; set; }
    public string Badge { get; set; }
    public Brush Color { get; set; }
    public int Level, Count = 1;
}

class Snapshot {
    public List<Row> Conns = new List<Row>(), Domains = new List<Row>(), Ports = new List<Row>(), Devices = new List<Row>(), Dns = new List<Row>();
    public int Total, Apps, Sus, Warn, Trackers, OpenPorts;
}

// Consumo total do PC (contadores da placa de rede) + histórico por dia em %LOCALAPPDATA%\Aura\usage.txt
static class Usage {
    public static double DownBps, UpBps;
    public static long SessionRx, SessionTx;
    public static readonly double[] HistDown = new double[60], HistUp = new double[60];
    public static int HistPos;
    static readonly SortedDictionary<string, long[]> days = new SortedDictionary<string, long[]>();
    static long lastRx = -1, lastTx; static DateTime lastT;
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura");
    static string File { get { return Path.Combine(Dir, "usage.txt"); } }

    public static void Load() {
        try {
            foreach (var l in System.IO.File.ReadAllLines(File)) { var p = l.Split(' '); if (p.Length == 3) days[p[0]] = new[] { long.Parse(p[1]), long.Parse(p[2]) }; }
        } catch { } // primeiro uso: arquivo ainda não existe
    }
    public static void Save() {
        try { Directory.CreateDirectory(Dir); System.IO.File.WriteAllLines(File, days.Select(d => d.Key + " " + d.Value[0] + " " + d.Value[1])); } catch { }
    }
    static long[] Day(string key) { long[] d; if (!days.TryGetValue(key, out d)) { d = new long[2]; days[key] = d; } return d; }
    public static long[] Today() { return Day(DateTime.Now.ToString("yyyy-MM-dd")); }
    public static long[] Month() {
        string m = DateTime.Now.ToString("yyyy-MM"); var r = new long[2];
        foreach (var d in days) if (d.Key.StartsWith(m)) { r[0] += d.Value[0]; r[1] += d.Value[1]; }
        return r;
    }

    static List<NetworkInterface> nics; static DateTime nicsAt;

    public static void Tick() {
        long rx = 0, tx = 0; DateTime now = DateTime.UtcNow;
        try {
            // Listar as placas custa ~12 ms de CPU; ler o contador de uma placa já conhecida custa quase nada.
            // Por isso a lista é refeita só a cada 30 s (tempo máximo para notar troca de cabo/Wi-Fi).
            if (nics == null || (now - nicsAt).TotalSeconds > 30) {
                nics = NetworkInterface.GetAllNetworkInterfaces().Where(ni =>
                    ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                    && ni.GetIPProperties().GatewayAddresses.Count > 0).ToList(); // só placas com saída para a internet
                nicsAt = now;
            }
            foreach (var ni in nics) { var s = ni.GetIPv4Statistics(); rx += s.BytesReceived; tx += s.BytesSent; }
        } catch { nics = null; return; }
        if (lastRx >= 0) {
            long dr = rx - lastRx, dt = tx - lastTx; double sec = (now - lastT).TotalSeconds;
            if (dr >= 0 && dt >= 0 && sec > 0.2 && sec < 30) { // negativo = placa reiniciou; >30s = PC dormiu
                DownBps = dr / sec; UpBps = dt / sec;
                SessionRx += dr; SessionTx += dt;
                long[] d = Today(); d[0] += dr; d[1] += dt;
            } else { DownBps = UpBps = 0; }
            HistPos = (HistPos + 1) % 60; HistDown[HistPos] = DownBps; HistUp[HistPos] = UpBps;
        }
        lastRx = rx; lastTx = tx; lastT = now;
    }
}

static class Fmt {
    public static string Speed(double bps) {
        if (bps < 1024) return "0 KB/s";
        if (bps < 1048576) return (bps / 1024).ToString("0") + " KB/s";
        return (bps / 1048576).ToString("0.0") + " MB/s";
    }
    public static string Size(long b) {
        if (b < 1048576) return (b / 1024.0).ToString("0") + " KB";
        if (b < 1073741824) return (b / 1048576.0).ToString("0.0") + " MB";
        return (b / 1073741824.0).ToString("0.00") + " GB";
    }
}

static class Collector {
    class Proc { public string Exe, Path; public bool Signed; }
    static Dictionary<string, string> ipHost = new Dictionary<string, string>();
    static List<string> dnsNames = new List<string>();
    static List<string[]> arp = new List<string[]>();
    static DateTime dnsAt, arpAt;
    static readonly ConcurrentDictionary<string, string> rdns = new ConcurrentDictionary<string, string>();
    static readonly Dictionary<string, bool> signedCache = new Dictionary<string, bool>();
    static int rdnsPending;

    static string Run(string exe, string args) {
        try {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using (var p = Process.Start(psi)) { string s = p.StandardOutput.ReadToEnd(); p.WaitForExit(4000); return s; }
        } catch { return ""; }
    }

    // `ipconfig /displaydns`: o nome consultado é a linha logo acima de "-----"; qualquer valor que seja um IP pertence a ele.
    // Independe do idioma do Windows.
    public static void ParseDnsCache(string text, Dictionary<string, string> map, List<string> names) {
        string header = null, prev = null;
        foreach (var raw in text.Split('\n')) {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("---")) { header = prev == null ? null : prev.ToLowerInvariant(); if (header != null && !names.Contains(header)) names.Add(header); continue; }
            int c = line.LastIndexOf(": ");
            if (c > 0 && header != null) {
                string v = line.Substring(c + 2).Trim(); IPAddress ip;
                if ((v.Contains(":") || v.Split('.').Length == 4) && IPAddress.TryParse(v, out ip)) map[ip.ToString()] = header; // "1" sozinho também faz parse, por isso o filtro
            }
            prev = line;
        }
    }

    public static List<string[]> ParseArp(string text) {
        var list = new List<string[]>();
        foreach (Match m in Regex.Matches(text, @"^\s*(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F]{2}(?:-[0-9a-fA-F]{2}){5})\s", RegexOptions.Multiline))
            list.Add(new[] { m.Groups[1].Value, m.Groups[2].Value.ToUpperInvariant() });
        return list;
    }

    // DNS reverso assíncrono: devolve o que já souber e dispara a consulta para a próxima atualização
    static string Rdns(IPAddress ip) {
        string key = ip.ToString(), h;
        if (rdns.TryGetValue(key, out h)) return h.Length == 0 ? null : h;
        if (rdnsPending >= 8) return null;
        System.Threading.Interlocked.Increment(ref rdnsPending);
        rdns[key] = "";
        Dns.GetHostEntryAsync(ip).ContinueWith(t => {
            System.Threading.Interlocked.Decrement(ref rdnsPending);
            if (t.Status == TaskStatus.RanToCompletion && t.Result.HostName != key) rdns[key] = t.Result.HostName.ToLowerInvariant();
            else { var ignore = t.Exception; }
        });
        return null;
    }

    static Proc GetProc(int pid, Dictionary<int, Proc> cache) {
        Proc p; if (cache.TryGetValue(pid, out p)) return p;
        p = new Proc();
        if (pid == 4 || pid == 0) { p.Exe = "system"; p.Signed = true; }
        else {
            p.Path = Native.ProcessPath(pid);
            if (p.Path != null) {
                p.Exe = Path.GetFileName(p.Path).ToLowerInvariant();
                lock (signedCache) {
                    if (!signedCache.TryGetValue(p.Path, out p.Signed)) {
                        try { X509Certificate.CreateFromSignedFile(p.Path); p.Signed = true; } catch { p.Signed = false; }
                        signedCache[p.Path] = p.Signed;
                    }
                }
            } else {
                try { p.Exe = Process.GetProcessById(pid).ProcessName.ToLowerInvariant() + ".exe"; } catch { p.Exe = "processo " + pid; }
                p.Signed = true; // processo protegido do sistema: não dá para ler o arquivo, não acusar à toa
            }
        }
        cache[pid] = p; return p;
    }

    static Row R(string title, string sub, string text, string badge, Brush color, int level) {
        return new Row { Title = title, Sub = sub, Text = text, Badge = badge, Color = color, Level = level };
    }

    public static Snapshot Collect(bool sweep) {
        var s = new Snapshot();
        IPAddress gateway = null, self = null, mask = null; var dnsServers = new List<IPAddress>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            var props = ni.GetIPProperties();
            var gw = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (gw == null) continue;
            if (gateway == null) {
                gateway = gw.Address;
                var u = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (u != null) { self = u.Address; mask = u.IPv4Mask; }
            }
            foreach (var d in props.DnsAddresses) if (!dnsServers.Contains(d) && !d.IsIPv6SiteLocal) dnsServers.Add(d);
        }

        // O cache DNS do Windows esquece rápido (segundos); por isso o histórico vai acumulando enquanto a janela está aberta
        if (gateway != null) Router.Host = gateway.ToString();
        Router.TickUpnp(); // internet da casa toda (UPnP do roteador)

        if ((DateTime.UtcNow - dnsAt).TotalSeconds > 5) {
            if (dnsNames.Count > 1500) { ipHost = new Dictionary<string, string>(); dnsNames = new List<string>(); }
            ParseDnsCache(Run("ipconfig", "/displaydns"), ipHost, dnsNames);
            dnsAt = DateTime.UtcNow;
        }

        var procs = new Dictionary<int, Proc>();
        var all = Native.Connections();

        // ---- Conexões de saída (agrupadas por programa + destino + porta)
        var agg = new Dictionary<string, Row>(); var appsSeen = new HashSet<string>(); int loop = 0;
        foreach (var c in all) {
            if (!c.Tcp || c.Listen) continue;
            if (IPAddress.IsLoopback(c.Remote)) { loop++; continue; }
            Proc p = GetProc(c.Pid, procs);
            string ipS = c.Remote.ToString(), host;
            if (!ipHost.TryGetValue(ipS, out host)) host = Kb.IsLocal(c.Remote) ? null : Rdns(c.Remote);
            if (ipS.Contains(":")) ipS = "[" + ipS + "]"; // IPv6 entre colchetes para não confundir com a porta
            Verdict v = Kb.ExplainConn(p.Exe, p.Path, p.Signed, c.Remote, c.RemotePort, host, gateway);
            s.Total++; appsSeen.Add(p.Exe);
            string key = p.Exe + "|" + v.Dest + "|" + c.RemotePort + "|" + v.Badge; Row r;
            if (agg.TryGetValue(key, out r)) { r.Count++; continue; }
            r = R(Kb.AppName(p.Exe) + "  →  " + v.Dest, (host ?? "sem nome") + "  ·  " + ipS + ":" + c.RemotePort, v.Text, v.Badge, Kb.LevelColor(v.Level), v.Level);
            agg[key] = r;
        }
        foreach (var r in agg.Values) if (r.Count > 1) r.Sub += "  ·  " + r.Count + " conexões";
        s.Conns = agg.Values.OrderByDescending(r => r.Level).ThenBy(r => r.Title).ToList();
        if (loop > 0) s.Conns.Add(R("Conversas internas do PC", loop + " conexões que não saem do computador", "Programas falando entre si dentro do próprio PC (por exemplo, as partes do navegador ou do Claude). Não usam a internet.", "Interno", Kb.Gray, 0));
        if (s.Conns.Count == 0) s.Conns.Add(R("Nenhuma conexão ativa", "", "Nenhum programa está conectado à internet neste momento.", "Vazio", Kb.Gray, 0));
        s.Apps = appsSeen.Count;

        // ---- Portas abertas (esperando conexão)
        var pagg = new Dictionary<string, Row>(); var exposedKeys = new HashSet<string>(); var info = new Dictionary<string, object[]>(); int ephemeral = 0;
        foreach (var c in all) {
            if (!c.Listen) continue;
            if (!c.Tcp && c.LocalPort >= 49152) { ephemeral++; continue; } // UDP temporária (navegador, chamadas): só ruído
            string key = (c.Tcp ? "TCP" : "UDP") + "|" + c.LocalPort + "|" + c.Pid;
            if (!IPAddress.IsLoopback(c.Local)) exposedKeys.Add(key);
            if (!info.ContainsKey(key)) info[key] = new object[] { c.Tcp, c.LocalPort, c.Pid };
        }
        foreach (var kv in info) {
            bool tcp = (bool)kv.Value[0]; int port = (int)kv.Value[1]; Proc p = GetProc((int)kv.Value[2], procs);
            bool exposed = exposedKeys.Contains(kv.Key);
            bool trusted = Kb.KnownApp(p.Exe) || p.Signed || (p.Path ?? "").ToLowerInvariant().StartsWith(@"c:\windows\");
            Verdict v = Kb.ExplainPort(p.Exe, trusted, port, exposed);
            var r = R("Porta " + port + " " + (tcp ? "TCP" : "UDP") + "  ·  " + Kb.AppName(p.Exe), (exposed ? "Aberta para a rede" : "Só este PC") + "  ·  " + p.Exe + "  ·  " + Kb.PortName(port), v.Text, v.Badge, exposed ? Kb.LevelColor(v.Level) : Kb.Gray, v.Level);
            r.Count = port; pagg[kv.Key] = r;
            if (exposed) s.OpenPorts++;
        }
        s.Ports = pagg.Values.OrderByDescending(r => r.Level).ThenByDescending(r => r.Color == Kb.Gray ? 0 : 1).ThenBy(r => r.Count).ToList();
        if (ephemeral > 0) s.Ports.Add(R("Portas temporárias", ephemeral + " portas UDP de curta duração", "Abertas por navegadores, jogos e chamadas de voz só enquanto precisam. Fecham sozinhas. Normal.", "Normal", Kb.Gray, 0));

        // ---- Domínios consultados (cache DNS do Windows)
        foreach (var name in dnsNames) {
            if (!name.Contains(".") || name.EndsWith(".arpa") || name.EndsWith(".local") || name.EndsWith(".lan")) continue;
            Org o = Kb.ByHost(name);
            if (o == null) s.Domains.Add(R(name, "Não catalogado", "Não está na minha lista. Na maioria das vezes é só um site comum que você ou algum programa acessou.", "Desconhecido", Kb.Gray, 0));
            else if (o.Cat == "tracker") s.Domains.Add(R(name, o.Name, "Rastreamento: " + o.What + ". Não é vírus, mas acompanha o que você faz na internet.", "Rastreador", Kb.Yellow, 1));
            else if (o.Cat == "telemetria") s.Domains.Add(R(name, o.Name, "Telemetria: " + o.What + ".", "Telemetria", Kb.Yellow, 1));
            else s.Domains.Add(R(name, o.Name, "Normal: " + o.What + ".", "Normal", Kb.Green, 0));
        }
        s.Domains = s.Domains.OrderByDescending(r => r.Level).ThenBy(r => r.Color == Kb.Gray ? 0 : 1).ThenBy(r => r.Title).ToList();
        if (s.Domains.Count == 0) s.Domains.Add(R("Nenhum domínio no cache", "", "O Windows não tem consultas DNS guardadas agora. Navegadores como o Brave às vezes usam o próprio DNS e não aparecem aqui.", "Vazio", Kb.Gray, 0));

        // ---- Servidores DNS em uso
        foreach (var d in dnsServers) { // servidores do mesmo tipo viram uma linha só
            Verdict v = Kb.ExplainDns(d, gateway);
            Row same = s.Dns.FirstOrDefault(r => r.Title == v.Dest);
            if (same != null) same.Sub += "  ·  " + d;
            else s.Dns.Add(R(v.Dest, d.ToString(), v.Text, v.Badge, Kb.LevelColor(v.Level), v.Level));
        }

        // ---- Aparelhos na rede local (tabela ARP; "Procurar" faz um ping em cada endereço para preencher a tabela)
        if (self != null && mask != null) {
            byte[] sb = self.GetAddressBytes(), mb = mask.GetAddressBytes();
            if (sweep) {
                var pings = new List<Task>();
                for (int i = 1; i < 255; i++) { // shortcut: varre só o /24 em volta do PC; redes maiores precisariam de varredura completa
                    var a = new[] { sb[0], sb[1], sb[2], (byte)i };
                    try { pings.Add(new Ping().SendPingAsync(new IPAddress(a), 500)); } catch { }
                }
                try { Task.WaitAll(pings.ToArray(), 4000); } catch { }
                arpAt = DateTime.MinValue;
            }
            if ((DateTime.UtcNow - arpAt).TotalSeconds > 15) { arp = ParseArp(Run("arp", "-a")); arpAt = DateTime.UtcNow; }
            s.Devices.Add(R("Este PC", self + "  ·  " + Environment.MachineName, "O computador que você está usando agora.", "Você", Kb.Green, 0));
            var seen = new HashSet<string>();
            foreach (var a in arp) {
                IPAddress ip = IPAddress.Parse(a[0]); byte[] b = ip.GetAddressBytes(); bool same = true, bcast = true;
                for (int i = 0; i < 4; i++) { if ((b[i] & mb[i]) != (sb[i] & mb[i])) same = false; if ((b[i] | mb[i]) != 255) bcast = false; }
                if (!same || bcast || Kb.IsMulticast(ip) || !seen.Add(a[0])) continue;
                string name, sub = a[0] + "  ·  MAC " + a[1];
                if (!Router.Names.TryGetValue(a[0], out name)) name = Rdns(ip); // nome que o roteador conhece (com login), senão DNS reverso
                bool random = (Convert.ToInt32(a[1].Substring(0, 2), 16) & 2) != 0;
                if (ip.Equals(gateway))
                    s.Devices.Insert(1, R("Roteador", sub, "É o aparelho que distribui a internet da casa. Todo o tráfego de todos os aparelhos passa por ele: é no painel dele (normalmente http://" + a[0] + " no navegador) que dá para ver o consumo do celular e dos outros aparelhos.", "Roteador", Kb.Blue, 0));
                else if (random)
                    s.Devices.Add(R(name ?? "Aparelho com endereço privado", sub, "Usa um endereço físico aleatório, padrão de celulares e tablets modernos (Android e iPhone) para proteger a privacidade. Provavelmente é um celular da casa.", "Provável celular", Kb.Gray, 0));
                else
                    s.Devices.Add(R(name ?? "Aparelho", sub, "Aparelho com endereço físico fixo: pode ser outro PC, TV, videogame, câmera ou tomada inteligente. Se não reconhecer, confira a lista de aparelhos no painel do roteador.", "Aparelho", Kb.Gray, 0));
            }
        }
        if (s.Devices.Count == 0) s.Devices.Add(R("Sem rede", "", "Não encontrei uma conexão de rede ativa.", "Vazio", Kb.Gray, 0));

        foreach (var r in s.Conns.Concat(s.Ports)) {
            if (r.Level == 2) s.Sus++;
            else if (r.Level == 1 && r.Badge == "Rastreador") s.Trackers++;
            else if (r.Level == 1) s.Warn++;
        }
        return s;
    }
}
}
