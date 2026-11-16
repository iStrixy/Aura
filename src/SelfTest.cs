// Aura.exe --selftest  ->  grava selftest.log ao lado do exe e devolve o número de falhas como código de saída.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

namespace Aura {
static class SelfTest {
    static int fails; static readonly StringBuilder log = new StringBuilder();
    static void Check(string name, bool ok) { log.AppendLine((ok ? "OK     " : "FALHA  ") + name); if (!ok) fails++; }
    static IPAddress I(string s) { return IPAddress.Parse(s); }
    static void Time(string name, int n, Action a) {
        var p = System.Diagnostics.Process.GetCurrentProcess(); double c0 = p.TotalProcessorTime.TotalMilliseconds;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < n; i++) a();
        sw.Stop(); p.Refresh();
        log.AppendLine("TEMPO  " + name + ": " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.00") + " ms por chamada, CPU " + ((p.TotalProcessorTime.TotalMilliseconds - c0) / n).ToString("0.00") + " ms");
    }

    public static int Run() {
        try { Tests(); } catch (Exception e) { fails++; log.AppendLine("EXCECAO " + e); }
        log.AppendLine(fails == 0 ? "TUDO OK" : fails + " FALHA(S)");
        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "selftest.log"), log.ToString(), Encoding.UTF8);
        return fails;
    }

    static void Tests() {
        IPAddress gw = I("10.0.0.1");
        // --- base de conhecimento
        Check("host google", Kb.ByHost("www.google.com").Name == "Google");
        Check("host com ponto final e maiusculas", Kb.ByHost("WWW.YouTube.com.").Name == "YouTube");
        Check("sufixo mais longo ganha", Kb.ByHost("v10.events.data.microsoft.com").Cat == "telemetria" && Kb.ByHost("login.microsoft.com").Cat == "ok");
        Check("tracker", Kb.ByHost("stats.g.doubleclick.net").Cat == "tracker");
        Check("host desconhecido / vazio", Kb.ByHost("exemplo-qualquer.xyz") == null && Kb.ByHost("com") == null && Kb.ByHost(null) == null && Kb.ByHost("") == null);
        Check("nao casa pedaco de nome", Kb.ByHost("notgoogle.com") == null);
        Check("ip google dns", Kb.ByIp(I("8.8.8.8")).Name == "Google DNS");
        Check("ip cloudflare", Kb.ByIp(I("104.18.32.47")).Name == "Cloudflare" && Kb.ByIp(I("104.32.0.1")) == null);
        Check("prefixo mais longo ganha", Kb.ByIp(I("52.223.200.1")).Name == "Twitch" && Kb.ByIp(I("52.200.0.1")).Name == "Amazon AWS");
        Check("ipv6 google", Kb.ByIp(I("2800:3f0:4001:80a::200e")).Name == "Google");
        Check("ip desconhecido", Kb.ByIp(I("189.124.132.32")) == null);
        Check("ip local", Kb.IsLocal(I("10.0.0.5")) && Kb.IsLocal(I("192.168.1.1")) && Kb.IsLocal(I("172.20.1.1")) && Kb.IsLocal(I("fe80::1")) && !Kb.IsLocal(I("172.32.0.1")) && !Kb.IsLocal(I("8.8.8.8")));
        Check("multicast", Kb.IsMulticast(I("239.255.255.250")) && Kb.IsMulticast(I("ff02::fb")) && !Kb.IsMulticast(I("10.0.0.1")));

        // --- vereditos de conexão
        string pf = @"C:\Program Files\X\";
        Check("navegador -> google = seguro", Kb.ExplainConn("brave.exe", pf + "brave.exe", true, I("142.250.1.1"), 443, "www.google.com", gw).Level == 0);
        Check("navegador -> tracker = rastreador", Kb.ExplainConn("brave.exe", pf + "brave.exe", true, I("142.250.1.1"), 443, "stats.g.doubleclick.net", gw).Badge == "Rastreador");
        Check("exe sem assinatura em pasta temp = suspeito", Kb.ExplainConn("abc.exe", @"C:\Users\User\AppData\Local\Temp\abc.exe", false, I("185.1.2.3"), 443, null, gw).Level == 2);
        Check("porta 4444 = suspeito", Kb.ExplainConn("brave.exe", pf + "brave.exe", true, I("185.1.2.3"), 4444, null, gw).Level == 2);
        Check("loopback = interno", Kb.ExplainConn("brave.exe", null, true, I("127.0.0.1"), 9222, null, gw).Badge == "Interno");
        Check("roteador = rede local", Kb.ExplainConn("svchost.exe", @"C:\Windows\System32\svchost.exe", false, gw, 80, null, gw).Level == 0);
        Check("SMB para outro aparelho = atencao", Kb.ExplainConn("system", null, true, I("10.0.0.7"), 445, null, gw).Level == 1);
        Check("SMB para a internet = suspeito", Kb.ExplainConn("system", null, true, I("185.1.2.3"), 445, null, gw).Level == 2);
        Check("powershell na internet = atencao", Kb.ExplainConn("powershell.exe", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", false, I("160.79.104.10"), 443, null, gw).Level == 1);
        Check("exe desconhecido assinado em porta comum = seguro", Kb.ExplainConn("foo.exe", pf + "foo.exe", true, I("185.1.2.3"), 443, null, gw).Level == 0);
        Check("exe desconhecido sem assinatura = atencao", Kb.ExplainConn("foo.exe", pf + "foo.exe", false, I("185.1.2.3"), 9999, null, gw).Level == 1);
        Check("exe desconhecido sem assinatura falando com Google = atencao", Kb.ExplainConn("foo.exe", pf + "foo.exe", false, I("8.8.8.8"), 443, null, gw).Level == 1);
        Check("ip sem nome usa faixa conhecida", Kb.ExplainConn("claude.exe", pf + "claude.exe", true, I("160.79.104.10"), 443, null, gw).Dest == "Anthropic (Claude)");

        // --- portas abertas e DNS
        Check("445 exposta = atencao", Kb.ExplainPort("system", true, 445, true).Level == 1);
        Check("porta so local = seguro", Kb.ExplainPort("brave.exe", true, 445, false).Level == 0);
        Check("4444 exposta = suspeito", Kb.ExplainPort("foo.exe", false, 4444, true).Level == 2);
        Check("exe desconhecido escutando = atencao", Kb.ExplainPort("foo.exe", false, 8123, true).Level == 1);
        Check("dns cloudflare = privado", Kb.ExplainDns(I("1.1.1.1"), gw).Badge == "Privado");
        Check("dns google = registra", Kb.ExplainDns(I("8.8.8.8"), gw).Level == 1);
        Check("dns roteador", Kb.ExplainDns(gw, gw).Badge == "Roteador");
        Check("dns provedor = pouco privado", Kb.ExplainDns(I("189.124.132.32"), gw).Badge == "Pouco privado");

        // --- parsers (saída real do Windows em pt-BR)
        string dns = "\r\nConfiguração de IP do Windows\r\n\r\n    www.exemplo.com\r\n    ----------------------------------------\r\n    Nome do Registro. . . : www.exemplo.com\r\n    Tipo de Registro. . . : 5\r\n    Tempo de Vida . . . . : 100\r\n    Comprimento dos Dados : 8\r\n    Seção . . . . . . . . : Resposta\r\n    Registro CNAME  . . . : cdn.outro.net\r\n\r\n\r\n    Nome do Registro. . . : cdn.outro.net\r\n    Tipo de Registro. . . : 1\r\n    Tempo de Vida . . . . : 100\r\n    Comprimento dos Dados : 4\r\n    Seção . . . . . . . . : Resposta\r\n    Registro (Host) A . . : 93.184.216.34\r\n\r\n\r\n    v6.teste.org\r\n    ----------------------------------------\r\n    Nome do Registro. . . : v6.teste.org\r\n    Tipo de Registro. . . : 28\r\n    Registro AAAA . . . . : 2800:3f0:4001:80a::200e\r\n\r\n    naoexiste.teste\r\n    ----------------------------------------\r\n    O nome não existe.\r\n";
        var map = new Dictionary<string, string>(); var names = new List<string>();
        Collector.ParseDnsCache(dns, map, names);
        Check("dns cache: CNAME aponta para o nome consultado", map.ContainsKey("93.184.216.34") && map["93.184.216.34"] == "www.exemplo.com");
        Check("dns cache: ipv6", map.ContainsKey("2800:3f0:4001:80a::200e") && map["2800:3f0:4001:80a::200e"] == "v6.teste.org");
        Check("dns cache: numeros soltos nao viram IP", map.Count == 2);
        Check("dns cache: nomes", names.Count == 3 && names[0] == "www.exemplo.com");
        var arp = Collector.ParseArp("\r\nInterface: 10.0.0.10 --- 0x7\r\n  Endereço IP           Endereço físico       Tipo\r\n  10.0.0.1              a4-91-b1-00-11-22     dinâmico  \r\n  10.0.0.255            ff-ff-ff-ff-ff-ff     estático  \r\n  224.0.0.22            01-00-5e-00-00-16     estático  \r\n");
        Check("arp", arp.Count == 3 && arp[0][0] == "10.0.0.1" && arp[0][1] == "A4-91-B1-00-11-22");
        Check("formatos", Fmt.Speed(500) == "0 KB/s" && Fmt.Speed(2048) == "2 KB/s" && Fmt.Speed(1572864) == "1,5 MB/s" && Fmt.Size(3221225472) == "3,00 GB");

        // --- roteador: contador UPnP, criptografia do login, filtro de páginas perigosas, remoção de senhas
        Check("upnp delta normal / volta de 4 GB / reinicio", Router.Delta(150, 100) == 50 && Router.Delta(5, 4294967290L) == 11 && Router.Delta(10, 2000000000L) == -1);
        Check("base64url_escape", Router.Esc("a+b/c=") == "a-b_c.");
        const string pem = "MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDdZxE0/Ixw8MAzTHibHD100HiKvgyyZ1MYTaq0x5199usHxnhrSPX10ndzkULQ1a51FvsURCKjNkSV4mL7Zroo0wlpx0GIk+jKVm/J3rMqR/KpcQu8l73aTHxImsjAzenX18HSk/vuqHUuefmXF+p6XfeurE4xpL1W+eU7YP9FeQIDAQAB";
        var rp = Router.ParsePem(pem);
        Check("chave publica do roteador (1024 bits, e=65537)", rp.Modulus.Length == 128 && rp.Exponent.Length == 3 && rp.Exponent[0] == 1 && rp.Exponent[2] == 1);
        string post = Router.EncryptPost(pem, "&username=x&password=y");
        var ck = post.Substring(post.IndexOf("&ck=") + 4);
        Check("post cifrado no formato do painel", post.StartsWith("encrypted=1&ct=") && ck.Length == 172 && !post.Contains("+") && !post.Contains("/") && !post.Contains("username"));
        byte[] k = new byte[16], v = new byte[16]; new Random(1).NextBytes(k); new Random(2).NextBytes(v);
        string cifrado;
        using (var aes = new System.Security.Cryptography.AesCryptoServiceProvider { Key = k, IV = v })
        using (var e = aes.CreateEncryptor()) { var p = Encoding.UTF8.GetBytes("{\"ok\":1}"); cifrado = Convert.ToBase64String(e.TransformFinalBlock(p, 0, p.Length)); }
        Check("resposta cifrada do roteador volta a texto", Router.Decrypt(cifrado, k, v) == "{\"ok\":1}" && Router.Decrypt("<html>", k, v) == "<html>");
        Check("paginas de acao bloqueadas", !Router.SafePage("/reboot.cgi") && !Router.SafePage("/restore_cfg.cgi") && !Router.SafePage("/logout.cgi") && !Router.SafePage("/login.cgi")
            && Router.SafePage("/device_status.cgi") && Router.SafePage("/lan_status.cgi?wlan"));
        string red = Router.Redact("var wpaKey = 'segredo1'; {\"Password\":\"segredo2\"} <input type=\"password\" name=\"p\" value=\"segredo3\"> var ssid = 'MinhaRede';");
        Check("senhas removidas antes de salvar", !red.Contains("segredo") && red.Contains("MinhaRede"));
        Check("sair do painel bloqueado / paginas de status liberadas", !Router.SafePage("/login.cgi?out") && Router.StatusPages.All(Router.SafePage));
        // Trechos no formato real das páginas do Nokia G-1425G-A (objetos JavaScript, aspas simples e duplas misturadas)
        var pg = new Dictionary<string, string> {
            { "/wan_status.cgi?gpon", "var gpon_status = { Enable:0, Status:'Up', TXPower:173680, RXPower:2020, RXPowerLower:-27, TransceiverTemperature:12544, stats:{BytesSent:1} };" },
            { "/show_wan_status.cgi?ipv4", "var wan_conns = [{wan_num:1, ipConns:[{_type:'ip', ConnectionStatus:'Connected', Uptime:1401311, ExternalIPAddress:'100.100.10.20', DNSServers:'189.124.132.32,189.124.132.33', X_CT_COM_IPv6IPAddress:'2804::1' }]} ,{wan_num:2, ipConns:[{_type:'ip', ConnectionStatus:'Disconnected', ExternalIPAddress:'' }]}];" },
            { "/device_status.cgi", "var dev_info = { \"Manufacturer\":\"ALCL\", \"ModelName\":\"G-1425G-A\", \"UpTime\":1401302 }\nvar mem_info = {\"Total\":400, \"Free\":300 }\nvar cpu_temperatureinfo = {\"CPUTemp\":\"60.9\\n\" }" },
            { "/sta_info.cgi", "var wlan_client=  [[{\"SSID\":\"Casa 2G\", \"Channel\":11, \"AssociatedDeviceMACAddress\":\"36:00:00:aa:bb:01\", \"SignalStrength\":-70, \"LastDataDownlinkRate\":144400, \"X_ASB_COM_ConnectedTime\":3332, \"Stats\":{ \"data_sent_next\":\"99517306\", \"data_receive_next\":\"5691562\" } }] ,[] ];" },
            { "/lan_status.cgi?wlan", "var device_cfg = [{_oid:3,\"HostName\":\"iPhone\", IPAddress:'10.0.0.19', lan_statusV6:[{_oid:1,IPv6Address:'fe80::1', MACAddress:'82:00:00:00:00:11', Active:1 }], MACAddress:'82:00:00:00:00:11', AddressSource:'DHCP', Active:0, InterfaceType:'802.11ac' },"
                + "{_oid:4,\"HostName\":\"Unknown_36:00:00:aa:bb:01\", IPAddress:'10.0.0.33', MACAddress:'36:00:00:aa:bb:01', AddressSource:'DHCP', Active:1, InterfaceType:'802.11' },"
                + "{_oid:5,\"HostName\":\"pc-sala\", IPAddress:'10.0.0.18', MACAddress:'30:5a:3a:00:00:01', AddressSource:'DHCP', Active:1, InterfaceType:'Ethernet' }];" } };
        var rr = Router.Parse(pg, 0);
        Func<string, Row> row = pre => rr.FirstOrDefault(x => x.Title.StartsWith(pre));
        Check("dBm igual ao painel (2020 -> -16,95)", Math.Abs(Router.Dbm(2020) + 16.95) < 0.01);
        Check("fibra -17 dBm = bom", row("Sinal da fibra").Badge == "Bom" && row("Sinal da fibra").Sub.Contains("-16,9") && row("Sinal da fibra").Sub.Contains("49 °C"));
        Check("internet: IP, CGNAT e uptime", row("Internet do provedor").Sub.Contains("100.100.10.20") && row("Internet do provedor").Text.Contains("CGNAT") && row("Internet do provedor").Sub.Contains("16 dias"));
        Check("roteador: modelo, temperatura e memoria", row("Roteador Nokia G-1425G-A") != null && row("Roteador").Sub.Contains("61 °C") && row("Roteador").Sub.Contains("25%"));
        Check("aparelho sem nome no Wi-Fi: sinal fraco e consumo", row("Aparelho sem nome").Badge == "Sinal fraco" && row("Aparelho sem nome").Text.Contains("aleatório") && row("Aparelho sem nome").Text.Contains("94,9 MB"));
        Check("offline usa o Active do aparelho (nao o do IPv6) e vai para o fim", row("iPhone").Badge == "Offline" && rr.Last().Title == "iPhone");
        Check("cabo + nomes para a aba Dispositivos", row("pc-sala").Badge == "Cabo" && Router.Names["10.0.0.18"] == "pc-sala" && !Router.Names.ContainsKey("10.0.0.33"));
        // --- DNS da casa: pacote, bloqueio, agrupamento por site
        var dq = new List<byte> { 0xAB, 0xCD, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 1 };
        foreach (var lb in "www.instagram.com".Split('.')) { dq.Add((byte)lb.Length); dq.AddRange(Encoding.ASCII.GetBytes(lb)); }
        dq.AddRange(new byte[] { 0, 0, 1, 0, 1, 0, 0, 0x29, 0x10, 0, 0, 0, 0, 0, 0, 0 }); // A/IN + registro EDNS (OPT) no fim, como celulares mandam
        string dn; int dt;
        Check("dns: le a pergunta", HomeDns.Parse(dq.ToArray(), out dn, out dt) && dn == "www.instagram.com" && dt == 1);
        byte[] br = HomeDns.BlockedReply(dq.ToArray());
        Check("dns: resposta de bloqueio (mesmo ID, 1 resposta 0.0.0.0, sem EDNS)", br[0] == 0xAB && br[1] == 0xCD && (br[2] & 0x80) != 0 && (br[2] & 1) == 1 && br[7] == 1 && br[11] == 0
            && br.Length == 12 + 19 + 4 + 16 && br.Skip(br.Length - 4).All(b => b == 0) && br[br.Length - 5] == 4);
        Check("dns: pacote quebrado nao derruba", !HomeDns.Parse(new byte[] { 1, 2, 3 }, out dn, out dt) && !HomeDns.Parse(new byte[] { 0, 0, 0x81, 0x80, 0, 1, 0, 0, 0, 0, 0, 0, 3, 97, 98, 99, 0, 0, 1 }, out dn, out dt));
        Check("dns: bloqueia anuncio/telemetria, libera instagram", HomeDns.IsBlocked("stats.g.doubleclick.net") && HomeDns.IsBlocked("v10.events.data.microsoft.com") && !HomeDns.IsBlocked("graph.instagram.com") && !HomeDns.IsBlocked("www.google.com"));
        Check("dns: agrupa por site", HomeDns.Site("scontent.cdninstagram.com") == "cdninstagram.com" && HomeDns.Site("www.globo.com.br") == "globo.com.br" && HomeDns.Site("instagram.com") == "instagram.com");
        // --- NextDNS: formato da API /profiles/{id}/logs
        string nd = "{\"data\":[" +
            "{\"timestamp\":\"2026-11-16T18:30:05.120Z\",\"domain\":\"graph.instagram.com\",\"root\":\"instagram.com\",\"encrypted\":true,\"protocol\":\"DNS-over-HTTPS\",\"clientIp\":\"177.1.2.3\",\"device\":{\"id\":\"ABC12\",\"name\":\"iPhone do Gui\",\"model\":\"iPhone\"},\"status\":\"default\",\"reasons\":[]}," +
            "{\"timestamp\":\"2026-11-16T18:30:01.000Z\",\"domain\":\"i.instagram.com\",\"root\":\"instagram.com\",\"device\":{\"id\":\"ABC12\",\"name\":\"iPhone do Gui\"},\"status\":\"default\",\"reasons\":[]}," +
            "{\"timestamp\":\"2026-11-16T18:29:00.000Z\",\"domain\":\"app-measurement.com\",\"root\":\"app-measurement.com\",\"tracker\":\"google\",\"device\":{\"id\":\"ABC12\",\"name\":\"iPhone do Gui\"},\"status\":\"blocked\",\"reasons\":[{\"id\":\"hagezi-multi-light\",\"name\":\"HaGeZi - Multi Light\"}]}," +
            "{\"timestamp\":\"2026-11-16T18:28:00.000Z\",\"domain\":\"www.google.com\",\"root\":\"google.com\",\"device\":{\"id\":\"__UNIDENTIFIED__\"},\"status\":\"default\",\"reasons\":[]}" +
            "],\"meta\":{\"pagination\":{\"cursor\":null}}}";
        var ndr = NextDns.Parse(nd);
        Func<string, Row> ndRow = pre => ndr.FirstOrDefault(x => x.Title == pre);
        Check("nextdns: resumo (4 consultas, 1 bloqueada)", ndr[0].Title.Contains("últimas 4") && ndr[0].Sub.Contains("1 bloqueadas (25%)"));
        Check("nextdns: agrupa instagram por aparelho", ndRow("instagram.com").Sub.StartsWith("iPhone do Gui") && ndRow("instagram.com").Sub.Contains("2 consultas") && ndRow("instagram.com").Badge == "Meta");
        Check("nextdns: bloqueio com o motivo", ndRow("app-measurement.com").Badge == "Bloqueado" && ndRow("app-measurement.com").Text.Contains("HaGeZi - Multi Light"));
        Check("nextdns: aparelho sem perfil", ndRow("google.com").Sub.StartsWith("Aparelho não identificado"));
        Check("nextdns: id do perfil em qualquer formato", NextDns.CleanProfile(" 8D2A1F ") == "8d2a1f" && NextDns.CleanProfile("https://8d2a1f.dns.nextdns.io") == "8d2a1f" && NextDns.CleanProfile("xyz") == "");
        Check("ip CGNAT", Router.Cgnat("100.64.0.1") && Router.Cgnat("100.127.255.1") && !Router.Cgnat("100.128.0.1") && !Router.Cgnat("189.124.132.32"));

        // --- fumaça no sistema real
        var conns = Native.Connections();
        Check("tabela de conexoes do Windows (" + conns.Count + " entradas)", conns.Count > 0 && conns.Any(c => c.Listen) && conns.All(c => c.LocalPort >= 0 && c.LocalPort <= 65535));
        Native.CpuPercent(); System.Threading.Thread.Sleep(300); double cpu = Native.CpuPercent();
        Check("cpu " + cpu.ToString("0.0") + "%", cpu >= 0 && cpu <= 100);
        double used, total; Native.Memory(out used, out total);
        Check("ram " + used.ToString("0.0") + "/" + total.ToString("0.0") + " GB", total > 1 && used > 0 && used < total);
        Usage.Tick(); System.Threading.Thread.Sleep(400); Usage.Tick();
        Check("contador de rede", Usage.DownBps >= 0 && Usage.UpBps >= 0);
        Snapshot s = Collector.Collect(false);
        Check("coleta completa: " + s.Total + " conexoes, " + s.Ports.Count + " portas, " + s.Domains.Count + " dominios, " + s.Devices.Count + " aparelhos, " + s.Dns.Count + " dns", s.Conns.Count > 0 && s.Ports.Count > 0 && s.Devices.Count > 0 && s.Dns.Count > 0);
        int t = Gpu.Temp();
        log.AppendLine("INFO   temperatura GPU = " + t + " C (" + (t > 0 ? Gpu.Source : "indisponivel") + ")");
        // custo de cada chamada que o widget faz por segundo (para manter o app leve)
        Time("Usage.Tick (contador de rede)", 50, delegate { Usage.Tick(); });
        Time("Gpu.Temp", 50, delegate { Gpu.Temp(); });
        Time("CpuPercent + Memory", 50, delegate { double a, b; Native.CpuPercent(); Native.Memory(out a, out b); });
        Time("Fullscreen", 50, delegate { Native.Fullscreen(); });
        Time("Collector.Collect (so com a janela de rede aberta)", 5, delegate { Collector.Collect(false); });
        log.AppendLine("INFO   resumo: suspeitos=" + s.Sus + " atencao=" + s.Warn + " rastreadores=" + s.Trackers);
        foreach (var r in s.Conns.Take(12)) log.AppendLine("INFO   [" + r.Badge + "] " + r.Title + " | " + r.Sub);
    }
}
}
