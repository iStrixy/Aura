// Base de conhecimento offline: quem é o dono de um domínio/IP, para que serve uma porta, e o veredito em português simples.
// Para ensinar algo novo ao app, basta acrescentar uma linha em HostData / IpData / PortData / AppData.
using System;
using System.Collections.Generic;
using System.Net;
using System.Windows.Media;

namespace Aura {

class Org { public string Name, Cat, What; }               // Cat: ok | tracker | telemetria
class Verdict { public int Level; public string Badge, Dest, Text; } // Level: 0 seguro, 1 atenção, 2 suspeito

static class Kb {
    public static readonly Brush Green = B("#30D158"), Yellow = B("#FFD60A"), Red = B("#FF453A"), Gray = B("#98989F"), Blue = B("#64D2FF");
    static Brush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    public static Brush LevelColor(int level) { return level == 2 ? Red : level == 1 ? Yellow : Green; }

    // Nome | categoria | o que é | domínios (sufixos)
    static readonly string[] HostData = {
        "Google|ok|serviços do Google (busca, contas, Gmail, Drive)|google.com google.com.br googleapis.com gstatic.com 1e100.net googleusercontent.com gvt1.com gvt2.com gvt3.com ggpht.com google.dev withgoogle.com pki.goog",
        "YouTube|ok|vídeos do YouTube|youtube.com ytimg.com googlevideo.com youtu.be youtube-nocookie.com",
        "Google Anúncios|tracker|rede de anúncios e medição de audiência da Google|doubleclick.net googlesyndication.com googleadservices.com google-analytics.com googletagmanager.com googletagservices.com app-measurement.com adservice.google.com 2mdn.net",
        "Microsoft|ok|serviços da Microsoft e do Windows (contas, loja, Office, Xbox)|microsoft.com windows.com windowsupdate.com live.com office.com office.net microsoftonline.com msftconnecttest.com msftncsi.com windows.net azure.com azureedge.net azurewebsites.net msedge.net trafficmanager.net bing.com msn.com skype.com xboxlive.com xbox.com sharepoint.com onedrive.com s-microsoft.com msauth.net msftauth.net visualstudio.com vsassets.io aka.ms",
        "Telemetria Microsoft|telemetria|envio de dados de uso e diagnóstico do Windows para a Microsoft|events.data.microsoft.com vortex.data.microsoft.com telemetry.microsoft.com watson.microsoft.com",
        "GitHub|ok|hospedagem de código (GitHub)|github.com githubusercontent.com githubassets.com github.io",
        "Cloudflare|ok|rede que protege e acelera sites (CDN); milhares de sites passam por ela|cloudflare.com cloudflare.net cloudflare-dns.com one.one.one.one cdnjs.com workers.dev pages.dev",
        "Amazon AWS|ok|nuvem da Amazon, onde muitos apps e jogos hospedam seus servidores|amazonaws.com cloudfront.net awsstatic.com amazon.com amazon.com.br awsglobalaccelerator.com",
        "Akamai|ok|rede de distribuição de conteúdo (CDN) usada para downloads e atualizações|akamai.net akamaiedge.net akamaihd.net akamaized.net akamaitechnologies.com edgekey.net edgesuite.net akadns.net",
        "Fastly|ok|rede de distribuição de conteúdo (CDN) usada por muitos sites|fastly.net fastlylb.net fastly.com",
        "Steam (Valve)|ok|loja e serviços online da Steam|steampowered.com steamcommunity.com steamstatic.com steamcontent.com steamserver.net valvesoftware.com steamusercontent.com steam-chat.com",
        "Riot Games|ok|servidores da Riot (LoL, Valorant, TFT e o anti-cheat Vanguard)|riotgames.com leagueoflegends.com riotcdn.net pvp.net playvalorant.com riotgames.net",
        "Epic Games|ok|loja e serviços online da Epic|epicgames.com epicgames.dev unrealengine.com fortnite.com epicgames.net",
        "Roblox|ok|servidores do Roblox|roblox.com rbxcdn.com rbx.com",
        "Discord|ok|chat e voz do Discord|discord.com discord.gg discordapp.com discordapp.net discord.media",
        "Brave|ok|serviços do navegador Brave (atualizações e listas de bloqueio)|brave.com bravesoftware.com",
        "Anthropic (Claude)|ok|servidores do Claude, a IA que você usa|anthropic.com claude.ai claude.com",
        "OpenAI|ok|ChatGPT / OpenAI|openai.com chatgpt.com oaistatic.com oaiusercontent.com",
        "Meta|ok|Facebook, Instagram e WhatsApp|facebook.com fbcdn.net instagram.com cdninstagram.com whatsapp.com whatsapp.net fb.com messenger.com meta.com",
        "Meta Pixel|tracker|rastreador da Meta embutido em sites para medir anúncios|connect.facebook.net pixel.facebook.com",
        "Apple|ok|serviços da Apple (iCloud, App Store)|apple.com icloud.com mzstatic.com apple-dns.net cdn-apple.com icloud-content.com",
        "Netflix|ok|streaming da Netflix|netflix.com nflxvideo.net nflxso.net nflximg.net nflxext.com",
        "Spotify|ok|música do Spotify|spotify.com scdn.co spotifycdn.com",
        "Twitch|ok|lives da Twitch|twitch.tv ttvnw.net jtvnw.net twitchcdn.net",
        "TikTok|ok|vídeos do TikTok|tiktok.com tiktokcdn.com tiktokv.com byteoversea.com ibytedtos.com",
        "X (Twitter)|ok|rede social X/Twitter|twitter.com x.com twimg.com t.co",
        "Telegram|ok|mensagens do Telegram|telegram.org t.me telegram.me",
        "AMD|ok|site e atualizações de driver da AMD|amd.com",
        "npm / Node.js|ok|pacotes de programação (npm)|npmjs.org npmjs.com nodejs.org",
        "Python|ok|pacotes de programação (PyPI)|pypi.org pythonhosted.org python.org",
        "Certificados|ok|verificação dos certificados de segurança (o cadeado) dos sites|letsencrypt.org lencr.org digicert.com sectigo.com globalsign.com",
        "Rede de anúncios|tracker|empresa de publicidade que acompanha sua navegação entre sites|scorecardresearch.com adnxs.com criteo.com criteo.net taboola.com outbrain.com rubiconproject.com pubmatic.com openx.net casalemedia.com adsrvr.org amazon-adsystem.com moatads.com quantserve.com bidswitch.net smartadserver.com adform.net media.net",
        "Análise de uso|tracker|ferramenta que registra como você usa sites e apps|hotjar.com mixpanel.com amplitude.com segment.io segment.com appsflyer.com adjust.com branch.io clarity.ms fullstory.com mouseflow.com braze.com onesignal.com cloudflareinsights.com",
        "Datadog|telemetria|serviço que recebe registros de uso e relatórios de falha de aplicativos|datadoghq.com",
        "Sentry|telemetria|serviço que recebe relatórios de falha de aplicativos|sentry.io",
        "New Relic|telemetria|serviço que recebe registros de uso e desempenho de aplicativos|newrelic.com nr-data.net",
        "Crashlytics / Bugsnag|telemetria|serviço que recebe relatórios de falha de aplicativos|bugsnag.com crashlytics.com",
    };

    // Nome | o que é | faixas de IP (CIDR)
    static readonly string[] IpData = {
        "Google DNS|servidor DNS público da Google (traduz nomes de sites em endereços)|8.8.8.8/32 8.8.4.4/32 2001:4860:4860::/48",
        "Cloudflare DNS|servidor DNS público da Cloudflare, focado em privacidade|1.1.1.1/32 1.0.0.1/32 2606:4700:4700::/48",
        "Quad9 DNS|servidor DNS público que bloqueia sites maliciosos|9.9.9.9/32 149.112.112.112/32",
        "OpenDNS|servidor DNS público da Cisco|208.67.222.222/32 208.67.220.220/32",
        "AdGuard DNS|servidor DNS que bloqueia anúncios|94.140.14.14/32 94.140.15.15/32",
        "Cloudflare|rede que protege e acelera sites (CDN); milhares de sites passam por ela|104.16.0.0/13 104.24.0.0/14 172.64.0.0/13 162.158.0.0/15 188.114.96.0/20 141.101.64.0/18 108.162.192.0/18 190.93.240.0/20 198.41.128.0/17 173.245.48.0/20 2606:4700::/32 2a06:98c0::/29",
        "Google|serviços do Google (busca, YouTube, contas)|142.250.0.0/15 172.217.0.0/16 216.58.192.0/19 74.125.0.0/16 172.253.0.0/16 64.233.160.0/19 173.194.0.0/16 209.85.128.0/17 216.239.32.0/19 2607:f8b0::/32 2800:3f0::/32 2a00:1450::/32 2404:6800::/32",
        "Google Cloud|nuvem da Google, onde apps e jogos hospedam servidores|34.64.0.0/10 34.128.0.0/10 35.184.0.0/13 35.192.0.0/11 35.224.0.0/12 35.240.0.0/13",
        "Microsoft|serviços e nuvem da Microsoft (Windows, Office, Xbox, Azure)|20.0.0.0/8 40.64.0.0/10 13.64.0.0/11 13.104.0.0/14 52.96.0.0/12 52.112.0.0/14 52.136.0.0/13 52.144.0.0/12 52.160.0.0/11 52.224.0.0/11 204.79.195.0/24 2620:1ec::/36 2603:1000::/24 2a01:111::/32",
        "Amazon AWS|nuvem da Amazon, onde muitos apps e jogos hospedam seus servidores|3.0.0.0/8 18.0.0.0/8 54.0.0.0/8 52.0.0.0/10 52.64.0.0/11 52.192.0.0/11 34.192.0.0/10 44.192.0.0/10 13.32.0.0/15 13.224.0.0/14 13.248.0.0/14 15.228.0.0/15 99.84.0.0/16 99.86.0.0/16 143.204.0.0/16 177.71.128.0/17 2600:1f00::/24 2600:9000::/28",
        "Akamai|rede de distribuição de conteúdo (CDN) usada para downloads e atualizações|23.0.0.0/12 23.32.0.0/11 23.64.0.0/14 23.72.0.0/13 23.192.0.0/11 2.16.0.0/13 104.64.0.0/10 184.24.0.0/13 184.50.0.0/15 184.84.0.0/14 96.6.0.0/15 96.16.0.0/15 2600:1400::/24 2a02:26f0::/32",
        "Fastly|rede de distribuição de conteúdo (CDN) usada por muitos sites|151.101.0.0/16 199.232.0.0/16 146.75.0.0/16 2a04:4e42::/32",
        "Steam (Valve)|servidores da Steam|155.133.224.0/19 162.254.192.0/21 208.64.200.0/22 185.25.180.0/22 146.66.152.0/21 208.78.164.0/22 205.196.6.0/24 190.217.33.0/24 103.10.124.0/23 103.28.54.0/23",
        "Riot Games|servidores de jogo da Riot (LoL, Valorant)|104.160.128.0/19 162.249.72.0/21 192.64.168.0/21 45.7.36.0/22",
        "Meta|Facebook, Instagram e WhatsApp|157.240.0.0/16 31.13.24.0/21 31.13.64.0/18 179.60.192.0/22 185.60.216.0/22 163.70.128.0/17 2a03:2880::/32",
        "Anthropic (Claude)|servidores do Claude, a IA que você usa|160.79.104.0/23 2607:6bc0::/48",
        "Roblox|servidores do Roblox|128.116.0.0/17",
        "Apple|serviços da Apple|17.0.0.0/8",
        "Telegram|mensagens do Telegram|149.154.160.0/20 91.108.4.0/22 91.108.56.0/22",
        "Netflix|streaming da Netflix|45.57.0.0/17 23.246.0.0/18 198.38.96.0/19 198.45.48.0/20",
        "Twitch|lives da Twitch|52.223.192.0/18 99.181.64.0/18 185.42.204.0/22",
        "Discord (voz)|servidores de voz do Discord|66.22.192.0/18",
        "GitHub|hospedagem de código (GitHub)|140.82.112.0/20 185.199.108.0/22",
    };

    // porta | para que serve
    static readonly string[] PortData = {
        "80|HTTP, web sem criptografia", "443|HTTPS, web segura (criptografada)", "53|DNS, tradução de nomes de sites", "853|DNS criptografado",
        "123|NTP, acerto do relógio", "22|SSH, acesso remoto por terminal", "21|FTP, transferência de arquivos sem criptografia", "23|Telnet, acesso remoto antigo e inseguro",
        "25|envio de e-mail", "465|envio de e-mail", "587|envio de e-mail", "110|leitura de e-mail", "143|leitura de e-mail", "993|leitura de e-mail", "995|leitura de e-mail",
        "135|RPC, comunicação interna de serviços do Windows", "137|NetBIOS, compartilhamento antigo do Windows", "138|NetBIOS, compartilhamento antigo do Windows", "139|NetBIOS, compartilhamento antigo do Windows",
        "445|SMB, compartilhamento de arquivos e impressoras do Windows", "1900|SSDP/UPnP, descoberta de aparelhos na rede (TVs, roteador)", "5353|mDNS, descoberta de aparelhos na rede",
        "5355|LLMNR, descoberta de nomes na rede local", "67|DHCP, obtenção de endereço IP", "68|DHCP, obtenção de endereço IP", "500|VPN (IPsec)", "4500|VPN (IPsec)",
        "3389|Área de Trabalho Remota (controle remoto do PC)", "5900|VNC (controle remoto do PC)", "5040|serviço de conexão de dispositivos do Windows", "5357|descoberta de dispositivos do Windows",
        "7680|Otimização de Entrega (troca de atualizações entre PCs)", "3478|STUN, ajuda chamadas de voz e jogos a se conectarem", "3479|STUN, ajuda chamadas de voz e jogos a se conectarem", "3480|STUN, ajuda chamadas de voz e jogos a se conectarem",
        "3074|Xbox Live", "5222|chat em tempo real (usado pela Riot e por mensageiros)", "5223|chat em tempo real (usado pela Riot e por mensageiros)", "2099|chat e lobby do League of Legends",
        "8080|web alternativa", "8443|web segura alternativa", "9001|rede Tor", "9030|rede Tor",
        "6667|IRC, chat antigo muito usado por vírus para receber comandos", "6697|IRC, chat antigo muito usado por vírus para receber comandos",
        "4444|porta clássica de ferramentas de invasão (backdoor)", "1337|porta clássica de ferramentas de invasão (backdoor)", "31337|porta clássica de ferramentas de invasão (backdoor)",
        "12345|porta clássica de ferramentas de invasão (backdoor)", "54321|porta clássica de ferramentas de invasão (backdoor)",
        "3333|porta típica de mineração de criptomoedas", "14444|porta típica de mineração de criptomoedas",
    };
    static readonly HashSet<int> Bad = new HashSet<int> { 23, 6667, 6697, 4444, 1337, 31337, 12345, 54321, 3333, 14444 };
    static readonly HashSet<int> RiskyOpen = new HashSet<int> { 21, 22, 23, 135, 139, 445, 3389, 5900 };

    // exe | nome amigável | por que ele usa a rede
    static readonly string[] AppData = {
        "brave.exe|Brave|O navegador mantém conexões com os sites das abas abertas, mesmo em segundo plano.",
        "chrome.exe|Chrome|O navegador mantém conexões com os sites das abas abertas, mesmo em segundo plano.",
        "msedge.exe|Edge|O navegador mantém conexões com os sites das abas abertas, mesmo em segundo plano.",
        "firefox.exe|Firefox|O navegador mantém conexões com os sites das abas abertas, mesmo em segundo plano.",
        "msedgewebview2.exe|WebView2|É a janela web embutida dentro de algum aplicativo instalado.",
        "claude.exe|Claude|É o app que você usa para conversar com o Claude.",
        "svchost.exe|Serviço do Windows|Serviços internos do Windows (hora, rede, certificados, loja).",
        "system|Windows (núcleo)|É o próprio sistema operacional.",
        "lsass.exe|Segurança do Windows|Cuida de logins e senhas do sistema.",
        "services.exe|Serviços do Windows|Gerencia os serviços do sistema.",
        "wininit.exe|Inicialização do Windows|Parte do sistema.",
        "spoolsv.exe|Spooler de impressão|Serviço de impressoras do Windows.",
        "explorer.exe|Explorador de Arquivos|Parte da área de trabalho do Windows.",
        "searchapp.exe|Pesquisa do Windows|A busca do menu Iniciar consulta a internet.",
        "steam.exe|Steam|A Steam fica conectada para amigos, loja e atualizações.",
        "steamwebhelper.exe|Steam|A Steam fica conectada para amigos, loja e atualizações.",
        "riotclientservices.exe|Riot Client|Fica em segundo plano para login e atualizações dos jogos da Riot.",
        "riotclientux.exe|Riot Client|Fica em segundo plano para login e atualizações dos jogos da Riot.",
        "riotclientuxrender.exe|Riot Client|Fica em segundo plano para login e atualizações dos jogos da Riot.",
        "leagueclient.exe|League of Legends|Cliente do jogo.", "leagueclientux.exe|League of Legends|Cliente do jogo.", "leagueclientuxrender.exe|League of Legends|Cliente do jogo.",
        "league of legends.exe|League of Legends|Partida em andamento.",
        "valorant.exe|Valorant|Jogo em execução.", "valorant-win64-shipping.exe|Valorant|Partida em andamento.",
        "vgc.exe|Vanguard (anti-cheat da Riot)|O anti-cheat conversa com a Riot enquanto o Windows está ligado.",
        "vgtray.exe|Vanguard (anti-cheat da Riot)|O anti-cheat conversa com a Riot enquanto o Windows está ligado.",
        "robloxplayerbeta.exe|Roblox|Jogo em execução ou esperando na bandeja.", "robloxstudiobeta.exe|Roblox Studio|Editor do Roblox.",
        "epicgameslauncher.exe|Epic Games|A loja fica conectada para login e atualizações.", "epicwebhelper.exe|Epic Games|A loja fica conectada para login e atualizações.",
        "discord.exe|Discord|Fica conectado para mensagens e chamadas.",
        "radeonsoftware.exe|AMD Software|Painel da placa de vídeo; verifica drivers e novidades.", "amdrsserv.exe|AMD Software|Painel da placa de vídeo; verifica drivers e novidades.",
        "cncmd.exe|AMD Software|Painel da placa de vídeo; verifica drivers e novidades.",
        "node.exe|Node.js|Ferramenta de programação; normal se você está desenvolvendo ou usando o Claude Code.",
        "python.exe|Python|Ferramenta de programação; normal se você está rodando scripts.",
        "git.exe|Git|Sincroniza código com servidores como o GitHub.", "git-remote-https.exe|Git|Sincroniza código com servidores como o GitHub.",
        "antigravity.exe|Antigravity|Editor de código da Google.", "code.exe|VS Code|Editor de código da Microsoft.",
        "spotify.exe|Spotify|Tocando ou sincronizando músicas.", "whatsapp.exe|WhatsApp|Fica conectado para receber mensagens.", "telegram.exe|Telegram|Fica conectado para receber mensagens.",
        "ditto.exe|Ditto|Gerenciador de área de transferência.", "aura.exe|Aura|Este próprio monitor.",
    };
    // Programas do Windows que rodam scripts: legítimos, mas é o caminho favorito de vírus para acessar a internet
    static readonly HashSet<string> ScriptHosts = new HashSet<string> { "powershell.exe", "pwsh.exe", "cmd.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe" };

    class Range { public byte[] Net; public int Bits; public Org Org; }
    static readonly Dictionary<string, Org> hosts = new Dictionary<string, Org>();
    static readonly List<Range> ranges = new List<Range>();
    static readonly Dictionary<int, string> ports = new Dictionary<int, string>();
    static readonly Dictionary<string, string[]> apps = new Dictionary<string, string[]>();

    static Kb() {
        foreach (var l in HostData) { var p = l.Split('|'); var o = new Org { Name = p[0], Cat = p[1], What = p[2] }; foreach (var h in p[3].Split(' ')) hosts[h] = o; }
        foreach (var l in IpData) {
            var p = l.Split('|'); var o = new Org { Name = p[0], Cat = "ok", What = p[1] };
            foreach (var c in p[2].Split(' ')) { var s = c.Split('/'); ranges.Add(new Range { Net = IPAddress.Parse(s[0]).GetAddressBytes(), Bits = int.Parse(s[1]), Org = o }); }
        }
        foreach (var l in PortData) { var p = l.Split('|'); ports[int.Parse(p[0])] = p[1]; }
        foreach (var l in AppData) { var p = l.Split('|'); apps[p[0]] = new[] { p[1], p[2] }; }
    }

    public static Org ByHost(string host) {
        if (string.IsNullOrEmpty(host)) return null;
        host = host.ToLowerInvariant().TrimEnd('.');
        for (int i = 0; ; ) { // testa o nome inteiro, depois vai cortando rótulos da esquerda (sufixo mais longo ganha)
            Org o; if (hosts.TryGetValue(host.Substring(i), out o)) return o;
            i = host.IndexOf('.', i) + 1;
            if (i <= 0) return null;
        }
    }

    public static Org ByIp(IPAddress ip) {
        byte[] a = ip.GetAddressBytes(); Range best = null;
        foreach (var r in ranges) {
            if (r.Net.Length != a.Length || (best != null && r.Bits <= best.Bits)) continue;
            bool ok = true;
            for (int bit = 0; bit < r.Bits && ok; bit += 8) {
                int mask = r.Bits - bit >= 8 ? 0xFF : (0xFF << (8 - (r.Bits - bit))) & 0xFF;
                ok = (a[bit / 8] & mask) == (r.Net[bit / 8] & mask);
            }
            if (ok) best = r;
        }
        return best == null ? null : best.Org;
    }

    public static bool IsLocal(IPAddress ip) {
        byte[] a = ip.GetAddressBytes();
        if (a.Length == 4) return a[0] == 10 || (a[0] == 172 && a[1] >= 16 && a[1] <= 31) || (a[0] == 192 && a[1] == 168) || (a[0] == 169 && a[1] == 254);
        return ip.IsIPv6LinkLocal || (a[0] & 0xFE) == 0xFC;
    }
    public static bool IsMulticast(IPAddress ip) {
        byte[] a = ip.GetAddressBytes();
        return a.Length == 4 ? (a[0] >= 224 && a[0] <= 239) || a[0] == 255 : ip.IsIPv6Multicast;
    }

    public static string PortName(int port) {
        string s; if (ports.TryGetValue(port, out s)) return s;
        if (port >= 27000 && port <= 27100) return "faixa de portas da Steam";
        if (port >= 5000 && port <= 5500) return "faixa de portas usada pelo League of Legends";
        if (port >= 8393 && port <= 8400) return "atualizador da Riot";
        if (port >= 7000 && port <= 8000) return "faixa de portas comum em jogos (Valorant, Unreal)";
        if (port >= 6881 && port <= 6889) return "BitTorrent";
        if (port >= 49152) return "porta temporária";
        return "porta sem uso padrão conhecido";
    }
    static bool NormalPort(int port) {
        return (ports.ContainsKey(port) && !Bad.Contains(port)) || (port >= 27000 && port <= 27100) || (port >= 5000 && port <= 5500) || (port >= 7000 && port <= 8400);
    }

    public static bool KnownApp(string exe) { return apps.ContainsKey(exe); }
    public static string AppName(string exe) {
        string[] a; if (apps.TryGetValue(exe, out a)) return a[0];
        return exe.EndsWith(".exe") ? exe.Substring(0, exe.Length - 4) : exe;
    }
    static string AppHint(string exe) { string[] a; return apps.TryGetValue(exe, out a) ? " " + a[1] : ""; }

    static Verdict V(int level, string badge, string dest, string text) { return new Verdict { Level = level, Badge = badge, Dest = dest, Text = text }; }

    // Explica uma conexão de saída: programa -> servidor
    public static Verdict ExplainConn(string exe, string path, bool signed, IPAddress ip, int port, string host, IPAddress gateway) {
        string app = AppName(exe), portTxt = PortName(port);
        if (IPAddress.IsLoopback(ip)) return V(0, "Interno", "este PC", app + " está conversando com outro programa dentro do próprio PC. Nada sai para a internet.");
        if (IsLocal(ip)) {
            bool gw = ip.Equals(gateway);
            string who = gw ? "seu roteador" : "outro aparelho da sua rede";
            if (!gw && RiskyOpen.Contains(port))
                return V(1, "Atenção", who, app + " está acessando " + who + " por " + portTxt + ". Normal se você usa compartilhamento ou acesso remoto em casa; se não usa, vale investigar.");
            return V(0, "Rede local", who, app + " está falando com " + who + " (" + portTxt + "). Fica dentro da sua casa, não vai para a internet.");
        }
        if (IsMulticast(ip)) return V(0, "Rede local", "aparelhos da rede", app + " está anunciando ou procurando aparelhos na rede local. Normal.");

        Org o = ByHost(host) ?? ByIp(ip);
        string dest = o != null ? o.Name : (host ?? ip.ToString());
        string p = (path ?? "").ToLowerInvariant();
        bool sys = p.StartsWith(@"c:\windows\") || exe == "system";
        bool trusted = KnownApp(exe) || signed || sys;
        bool temp = p.Contains(@"\appdata\local\temp\") || p.Contains(@"\windows\temp\") || p.Contains(@"\downloads\");

        if (Bad.Contains(port))
            return V(2, "Suspeito", dest, app + " está conectado na porta " + port + ": " + portTxt + ". Programas comuns quase nunca usam essa porta. Se você não reconhece o " + exe + ", feche-o e passe um antivírus.");
        if (exe == "system" && (port == 445 || port == 139))
            return V(2, "Suspeito", dest, "O Windows está enviando compartilhamento de arquivos para fora da sua rede (" + dest + "). Isso não é normal e pode vazar dados.");
        if (temp && !signed && !KnownApp(exe))
            return V(2, "Suspeito", dest, "O programa " + exe + " roda de uma pasta temporária, não tem assinatura digital e está falando com a internet (" + dest + "). Muitos vírus se comportam assim. Se você não abriu isso de propósito, investigue.");
        if (ScriptHosts.Contains(exe))
            return V(1, "Atenção", dest, "Um script (" + exe + ") está acessando a internet (" + dest + "). Normal se você ou o Claude Code estão rodando comandos agora; estranho se aparecer sozinho com o PC parado.");
        if (o != null && o.Cat == "tracker")
            return V(1, "Rastreador", dest, app + " está falando com " + o.Name + ": " + o.What + ". Não é vírus, é coleta de dados de navegação. Bloqueadores (o Brave já tem um) reduzem isso.");
        if (o != null && o.Cat == "telemetria")
            return V(1, "Telemetria", dest, app + " está enviando dados para " + o.Name + ", " + o.What + ". Não é perigoso, mas é informação de uso saindo do PC.");
        if (o != null) {
            string t = app + " conectado a " + o.Name + ": " + o.What + "." + AppHint(exe);
            if (!trusted) return V(1, "Atenção", dest, t + " Porém o " + exe + " não tem assinatura digital e não está na minha lista. Confirme se é um programa que você instalou.");
            if (port == 80) t += " (Conexão sem criptografia, comum em verificações rápidas.)";
            return V(0, "Seguro", dest, t);
        }
        string unk = app + " conectado a um servidor que não identifiquei" + (host != null ? " (" + host + ")" : "") + ", via " + portTxt + ".";
        if (trusted && NormalPort(port)) return V(0, "Seguro", dest, unk + " O programa é conhecido e a porta é comum: provavelmente é só um site ou servidor de conteúdo." + AppHint(exe));
        if (trusted) return V(0, "Seguro", dest, unk + " A porta é fora do padrão, o que é comum em jogos e apps de voz. O programa é conhecido." + AppHint(exe));
        if (NormalPort(port)) return V(1, "Atenção", dest, unk + " O " + exe + " não tem assinatura digital e não está na minha lista. Se você reconhece o programa, tudo bem.");
        return V(1, "Atenção", dest, unk + " Programa sem assinatura digital, destino desconhecido e porta incomum: se você não reconhece o " + exe + ", vale investigar.");
    }

    // Explica uma porta aberta esperando conexão
    public static Verdict ExplainPort(string exe, bool trusted, int port, bool exposed) {
        string app = AppName(exe), portTxt = PortName(port);
        if (Bad.Contains(port) && exposed)
            return V(2, "Suspeito", "", app + " abriu a porta " + port + " para a rede: " + portTxt + ". Se você não sabe o que é o " + exe + ", investigue.");
        if (!exposed) return V(0, "Só este PC", "", app + " usa esta porta apenas para conversar com outros programas do próprio PC. Ninguém de fora consegue acessar.");
        if (RiskyOpen.Contains(port))
            return V(1, "Atenção", "", "Aberta para a rede: " + portTxt + ". É padrão do Windows e o firewall costuma proteger, mas é o tipo de porta que ataques exploram. Como este Windows não recebe atualizações de segurança, evite usar o PC em Wi-Fi público.");
        if (!trusted) return V(1, "Atenção", "", "O " + exe + " (sem assinatura digital e fora da minha lista) aceita conexões de outros aparelhos nesta porta. Confirme se é um programa que você instalou.");
        return V(0, "Rede local", "", app + " aceita conexões de outros aparelhos da sua rede nesta porta (" + portTxt + "). Normal para este programa.");
    }

    // Explica um servidor DNS configurado
    public static Verdict ExplainDns(IPAddress ip, IPAddress gateway) {
        Org o = ByIp(ip);
        if (o != null && (o.Name.StartsWith("Cloudflare") || o.Name.StartsWith("Quad9") || o.Name.StartsWith("AdGuard")))
            return V(0, "Privado", o.Name, o.Name + ": " + o.What + ". Boa escolha para privacidade.");
        if (o != null) return V(1, "Registra dados", o.Name, o.Name + ": " + o.What + ". Rápido e confiável, mas a empresa registra as consultas.");
        if (IsLocal(ip)) return V(0, "Roteador", "Seu roteador", "O roteador responde as consultas e repassa para o DNS do provedor de internet.");
        return V(1, "Pouco privado", "DNS do provedor", "É o DNS padrão do seu provedor de internet. Funciona bem, mas o provedor consegue ver a lista de sites que você acessa (só os nomes, não o conteúdo). Para mais privacidade dá para trocar por 1.1.1.1 (Cloudflare) ou 9.9.9.9 (Quad9).");
    }
}
}
