// "DNS da casa": o Aura atende as perguntas de DNS dos aparelhos que apontarem para este PC (porta 53),
// anota quem perguntou o quê (ex.: iPhone -> instagram.com), bloqueia anúncios/rastreadores/telemetria e repassa o resto
// para o DNS de antes (provedor). Só vê o NOME do site, nunca o conteúdo (tudo é criptografado).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Aura {
static class HomeDns {
    public static bool On {
        get { return Settings.Get("dns_server", "1") == "1"; }
        set { Settings.Set("dns_server", value ? "1" : "0"); if (value) Start(); else Stop(); }
    }
    public static bool BlockOn { get { return Settings.Get("dns_block", "1") == "1"; } set { Settings.Set("dns_block", value ? "1" : "0"); } }

    static UdpClient udp; static IPEndPoint[] upstream = new IPEndPoint[0];
    public static string Status = ""; public static long Queries, Blocked;
    static HashSet<string> extraBlock = new HashSet<string>(), allow = new HashSet<string>(), local = new HashSet<string>();
    static string BlockFile { get { return Path.Combine(Usage.Dir, "bloquear.txt"); } }
    static string AllowFile { get { return Path.Combine(Usage.Dir, "liberar.txt"); } }

    public static void Start() {
        if (udp != null || !On) return;
        try {
            LoadLists();
            var ups = new List<IPEndPoint>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)) {
                var ip = ni.GetIPProperties();
                foreach (var a in ip.UnicastAddresses) local.Add(a.Address.ToString());
                foreach (var d in ip.DnsAddresses) if (d.AddressFamily == AddressFamily.InterNetwork) ups.Add(new IPEndPoint(d, 53));
            }
            ups.RemoveAll(e => local.Contains(e.Address.ToString()) || IPAddress.IsLoopback(e.Address)); // nunca repassar para si mesmo (laço)
            IPAddress gw; if (IPAddress.TryParse(Router.Host, out gw)) ups.Add(new IPEndPoint(gw, 53)); // roteador como reserva
            upstream = ups.GroupBy(e => e.ToString()).Select(g => g.First()).ToArray();
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, 53));
            udp.Client.IOControl(-1744830452, new byte[] { 0 }, null); // SIO_UDP_CONNRESET: sem isso, um aparelho que sumiu derruba o Receive
            new Thread(Loop) { IsBackground = true, Name = "dns" }.Start();
            Status = "";
        } catch (Exception e) {
            Status = "Não consegui abrir a porta 53 (" + e.Message + "). Outro programa pode estar usando-a.";
            Program.Log(e); udp = null;
        }
    }
    public static void Stop() { var u = udp; udp = null; if (u != null) u.Close(); }

    static void Loop() {
        var u = udp;
        while (u == udp && u != null) {
            try {
                var ep = new IPEndPoint(IPAddress.Any, 0); byte[] q = u.Receive(ref ep);
                ThreadPool.QueueUserWorkItem(delegate { Handle(u, q, ep); });
            } catch (ObjectDisposedException) { return; }
            catch (SocketException) { if (u != udp) return; }
        }
    }

    static void Handle(UdpClient u, byte[] q, IPEndPoint from) {
        try {
            string name; int type;
            if (!Parse(q, out name, out type)) return;
            bool block = BlockOn && IsBlocked(name);
            Record(from.Address.ToString(), name, type, block);
            if (block) { var r = BlockedReply(q); u.Send(r, r.Length, from); return; }
            foreach (var up in upstream) {
                try {
                    using (var c = new UdpClient()) {
                        c.Client.ReceiveTimeout = 2000;
                        c.Send(q, q.Length, up);
                        var any = new IPEndPoint(IPAddress.Any, 0); byte[] r = c.Receive(ref any);
                        u.Send(r, r.Length, from); return;
                    }
                } catch (SocketException) { } // não respondeu: tenta o próximo
            }
        } catch (Exception e) { Program.Log(e); }
        // shortcut: só UDP; respostas grandes que pedem TCP (raras) falham e o aparelho tenta de novo pelo DNS reserva dele
    }

    // ------------------------------------------------------------ Pacote DNS
    public static bool Parse(byte[] q, out string name, out int type) {
        name = null; type = 0;
        if (q.Length < 17 || (q[2] & 0x80) != 0 || (q[4] << 8 | q[5]) < 1) return false; // precisa ser pergunta com 1+ questão
        var sb = new StringBuilder(); int i = 12;
        while (i < q.Length && q[i] != 0) {
            int len = q[i]; if (len > 63 || i + 1 + len >= q.Length) return false;
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(q, i + 1, len)); i += 1 + len;
        }
        if (i + 4 >= q.Length) return false;
        type = q[i + 1] << 8 | q[i + 2];
        name = sb.ToString().ToLowerInvariant();
        return true;
    }
    // Resposta "este site não existe aqui": A -> 0.0.0.0, AAAA -> ::, outros tipos -> resposta vazia
    public static byte[] BlockedReply(byte[] q) {
        int i = 12; while (q[i] != 0) i += q[i] + 1;
        int qEnd = i + 5, type = q[i + 1] << 8 | q[i + 2];
        int dlen = type == 1 ? 4 : type == 28 ? 16 : 0;
        var r = new List<byte>(q.Take(qEnd));
        r[2] = (byte)(0x80 | (q[2] & 0x79)); r[3] = 0x80;          // é resposta, mantém opcode e RD; RA=1, sem erro
        r[4] = 0; r[5] = 1; r[6] = 0; r[7] = (byte)(dlen > 0 ? 1 : 0); r[8] = r[9] = r[10] = r[11] = 0;
        if (dlen > 0) {
            r.AddRange(new byte[] { 0xC0, 0x0C, 0, (byte)type, 0, 1, 0, 0, 0, 60, 0, (byte)dlen });
            r.AddRange(new byte[dlen]);
        }
        return r.ToArray();
    }

    // ------------------------------------------------------------ Bloqueio
    static void LoadLists() {
        extraBlock = ReadList(BlockFile); allow = ReadList(AllowFile);
        if (!File.Exists(AllowFile)) try {
            Directory.CreateDirectory(Usage.Dir);
            File.WriteAllText(AllowFile, "# Um domínio por linha que o Aura NUNCA deve bloquear (ex.: um site que parou de funcionar).\r\n", Encoding.UTF8);
        } catch { }
    }
    // Aceita lista simples (um domínio por linha) ou formato "hosts" (0.0.0.0 dominio)
    static HashSet<string> ReadList(string f) {
        var s = new HashSet<string>();
        try {
            foreach (var l in File.ReadLines(f)) {
                string t = l.Split('#')[0].Trim(); if (t == "") continue;
                var p = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string d = p[p.Length - 1].Trim('.').ToLowerInvariant();
                if (d.Contains('.') && d != "localhost") s.Add(d);
            }
        } catch { }
        return s;
    }
    static bool InSet(HashSet<string> set, string name) {
        for (string n = name; ; ) { if (set.Contains(n)) return true; int d = n.IndexOf('.'); if (d < 0) return false; n = n.Substring(d + 1); }
    }
    public static bool IsBlocked(string name) {
        if (InSet(allow, name)) return false;
        var o = Kb.ByHost(name);
        return (o != null && (o.Cat == "tracker" || o.Cat == "telemetria")) || InSet(extraBlock, name);
    }
    public static int ExtraCount { get { return extraBlock.Count; } }

    // ------------------------------------------------------------ Registro (só na memória: some ao fechar o Aura)
    class Hit { public string Client, Site, Example; public int Count, BlockedCount; public DateTime Last; }
    static readonly Dictionary<string, Hit> hits = new Dictionary<string, Hit>();

    // "a.b.instagram.com" -> "instagram.com"; "www.globo.com.br" -> "globo.com.br"
    public static string Site(string name) {
        var p = name.Split('.'); if (p.Length <= 2) return name;
        bool two = p[p.Length - 1].Length == 2 && new[] { "com", "net", "org", "gov", "edu", "co" }.Contains(p[p.Length - 2]);
        return string.Join(".", p.Skip(p.Length - (two ? 3 : 2)));
    }

    static void Record(string client, string name, int type, bool blocked) {
        if (type == 12 || name.EndsWith(".arpa") || name.EndsWith(".local") || !name.Contains('.')) return; // perguntas internas, não são sites
        lock (hits) {
            Queries++; if (blocked) Blocked++;
            string site = Site(name), key = client + " " + site; Hit h;
            if (!hits.TryGetValue(key, out h)) {
                if (hits.Count >= 3000) hits.Remove(hits.OrderBy(kv => kv.Value.Last).First().Key); // shortcut: guarda os 3000 mais recentes
                hits[key] = h = new Hit { Client = client, Site = site, Example = name };
            }
            h.Count++; if (blocked) h.BlockedCount++; h.Last = DateTime.Now;
        }
    }

    static string ClientName(string ip) {
        string n;
        if (local.Contains(ip) || ip == "127.0.0.1") return "Este PC";
        return Router.Names.TryGetValue(ip, out n) ? n + " (" + ip + ")" : ip;
    }

    public static string MyIp { get { return local.FirstOrDefault(a => a.StartsWith(Router.Host.Substring(0, Router.Host.LastIndexOf('.') + 1))) ?? "o IP deste PC"; } }

    // Uma linha "site visitado" (usada pelo DNS do Aura e pelo histórico do NextDNS)
    public static Row SiteRow(string site, string example, string who, int count, int blockedCount, DateTime last, string blockedBy) {
        var o = Kb.ByHost(example);
        bool bad = o != null && (o.Cat == "tracker" || o.Cat == "telemetria"), blocked = blockedCount > 0;
        string what = o != null ? char.ToUpper(o.What[0]) + o.What.Substring(1) + " (" + o.Name + ")." : "Site ou serviço que não está na base do Aura. Exemplo do endereço pedido: " + example + ".";
        if (blocked) what += " " + blockedBy + ": o aparelho recebeu \"não existe\"" + (blockedCount < count ? " em " + blockedCount + " de " + count + " consultas." : ".");
        else if (bad) what += " É " + (o.Cat == "tracker" ? "rastreador/anúncio" : "telemetria") + ", mas não foi bloqueado.";
        return new Row { Title = site, Sub = who + "  ·  " + count + (count == 1 ? " consulta" : " consultas") + "  ·  última às " + last.ToString("HH:mm:ss"),
            Text = what, Badge = blocked ? "Bloqueado" : bad ? (o.Cat == "tracker" ? "Rastreador" : "Telemetria") : o != null ? o.Name : "Site",
            Color = blocked ? Kb.Blue : bad ? Kb.Yellow : Kb.Green, Level = bad && !blocked ? 1 : 0 };
    }

    public static List<Row> Rows() {
        var rows = new List<Row>();
        string how = "Para ver e filtrar um aparelho, troque o DNS dele para " + MyIp + ". No iPhone: Ajustes → Wi-Fi → (i) da rede → Configurar DNS → Manual → apague os servidores e adicione " + MyIp
                   + ". Para desfazer, volte para Automático. Atenção: com o PC desligado, esse aparelho fica sem internet até voltar para Automático.";
        if (udp == null)
            rows.Add(new Row { Title = On ? "DNS da casa com problema" : "DNS da casa desligado", Sub = "Porta 53", Text = On ? Status : "Ligue no menu do Aura (botão direito no widget ou no ícone perto do relógio): \"DNS da casa\".",
                Badge = On ? "Erro" : "Desligado", Color = On ? Kb.Red : Kb.Gray, Level = On ? 1 : 0 });
        else
            rows.Add(new Row { Title = "DNS da casa ligado em " + MyIp,
                Sub = Queries + " consultas  ·  " + Blocked + " bloqueadas" + (Queries > 0 ? " (" + (100 * Blocked / Queries) + "%)" : "") + "  ·  bloqueio " + (BlockOn ? "ligado" : "desligado")
                    + (ExtraCount > 0 ? "  ·  lista extra com " + ExtraCount.ToString("N0") + " domínios" : ""),
                Text = how + " Bloqueio: anúncios, rastreadores e telemetria " + (BlockOn ? "são respondidos com \"não existe\" (o app ou site segue funcionando, só sem a propaganda)." : "estão liberados (bloqueio desligado no menu do Aura).")
                    + " Se um site parar de funcionar, ponha o domínio dele em " + AllowFile + ".",
                Badge = "Ligado", Color = Kb.Green, Level = 0 });
        List<Hit> list; lock (hits) list = hits.Values.OrderByDescending(h => h.Last).Take(300).ToList();
        foreach (var h in list) rows.Add(SiteRow(h.Site, h.Example, ClientName(h.Client), h.Count, h.BlockedCount, h.Last, "Bloqueado pelo Aura"));
        if (list.Count == 0 && udp != null)
            rows.Add(new Row { Title = "Nenhum aparelho usando o DNS da casa ainda", Sub = "Aguardando", Text = how, Badge = "Vazio", Color = Kb.Gray, Level = 0 });
        return rows;
    }
}
}
