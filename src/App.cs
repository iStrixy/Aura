// Aura: widgets de desktop + monitor de rede. Um processo só; a janela de rede só trabalha quando está aberta.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using IO = System.IO;

namespace Aura {

static class Settings {
    static readonly Dictionary<string, string> d = new Dictionary<string, string>();
    static string File { get { return IO.Path.Combine(Usage.Dir, "settings.txt"); } }
    public static void Load() {
        try { foreach (var l in IO.File.ReadAllLines(File)) { int i = l.IndexOf('='); if (i > 0) d[l.Substring(0, i)] = l.Substring(i + 1); } } catch { }
    }
    public static string Get(string k, string def) { string v; return d.TryGetValue(k, out v) ? v : def; }
    public static void Set(string k, string v) {
        d[k] = v;
        try { IO.Directory.CreateDirectory(Usage.Dir); IO.File.WriteAllLines(File, d.Select(p => p.Key + "=" + p.Value)); } catch { }
    }
}

static class Autostart {
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool On {
        get { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return k != null && k.GetValue("Aura") != null; }
        set {
            using (var k = Registry.CurrentUser.CreateSubKey(Key)) {
                if (value) k.SetValue("Aura", "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                else k.DeleteValue("Aura", false);
            }
        }
    }
}

static class Program {
    public static WidgetWin Widget; public static NetWin Net;
    static Application app; static DispatcherTimer taskbarTimer;

    public static void Log(Exception e) {
        try { IO.Directory.CreateDirectory(Usage.Dir); IO.File.AppendAllText(IO.Path.Combine(Usage.Dir, "error.log"), DateTime.Now + " " + e + "\r\n"); } catch { }
    }

    [STAThread]
    static int Main(string[] args) {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
        if (args.Contains("--selftest")) return SelfTest.Run();
        if (args.Length == 2 && args[0] == "--roteador-pagina") return Router.DumpPage(args[1]);
        int wait; if (args.Contains("--roteador")) return Router.ReadOnce(int.TryParse(args.Last(), out wait) ? wait : 16); // lê o roteador sem abrir janela (teste/diagnóstico)
        var open = new EventWaitHandle(false, EventResetMode.AutoReset, "AuraOpenNet");
        bool first; var mutex = new Mutex(true, "AuraSingleInstance", out first);
        // Já está rodando: pede para abrir a janela de rede. Quem tem o direito de foco é este processo (o usuário acabou
        // de clicar no atalho), então ele repassa esse direito; sem isso a janela abriria atrás das outras.
        if (!first) { Native.AllowSetForegroundWindow(-1); open.Set(); return 0; }

        app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (s, e) => { Log(e.Exception); e.Handled = true; };
        app.SessionEnding += delegate { Usage.Save(); };
        Settings.Load(); Usage.Load(); HomeDns.Start();
        Widget = new WidgetWin();
        Tray.Create();
        ThreadPool.RegisterWaitForSingleObject(open, (s, t) => app.Dispatcher.BeginInvoke(new Action(OpenNet)), null, -1, false);
        taskbarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        taskbarTimer.Tick += delegate { Widget.Pin(); if (TaskbarOn) Taskbar(true); if (++ticks % 4 == 0 && ExplorerOn) Explorer(true); };
        taskbarTimer.Start();
        // BeginInvoke (e não chamada direta): antes do Run() não existe contexto de UI e o await da coleta voltaria em outra thread
        if (args.Contains("--rede")) app.Dispatcher.BeginInvoke(new Action(OpenNet));
        app.Run();
        GC.KeepAlive(mutex);
        return 0;
    }

    // A barra de tarefas volta ao normal sozinha a cada evento do Explorer, por isso o reforço periódico (mesma técnica do TranslucentTB)
    public static bool TaskbarOn {
        get { return Settings.Get("taskbar", "1") == "1"; }
        set { Settings.Set("taskbar", value ? "1" : "0"); if (!value) Taskbar(false); }
    }
    static void Taskbar(bool glass) { Native.Accent(Native.FindWindow("Shell_TrayWnd", null), glass ? 3 : 0, 0); }

    // Explorador com vidro: desfoque com tinta escura (mesma cor da janela de rede do Aura) em cada janela do Explorador.
    // Só funciona no tema escuro: o Explorador pinta o fundo de preto, e o preto vira "transparente" sobre o desfoque.
    // Cantos arredondados não dá: o Windows 10 ignora o recorte em janelas com moldura nativa (testado).
    public static bool ExplorerOn {
        get { return Settings.Get("explorer", "1") == "1"; }
        set { Settings.Set("explorer", value ? "1" : "0"); Explorer(value); }
    }
    static readonly HashSet<IntPtr> glassed = new HashSet<IntPtr>(); static int ticks;
    static void Explorer(bool on) {
        var open = on ? Native.ExplorerWindows() : new List<IntPtr>();
        foreach (var w in open) if (glassed.Add(w)) Native.Accent(w, 3, 0xB8141418); // aplica uma vez por janela nova
        foreach (var w in glassed.Where(w => !open.Contains(w)).ToList()) { if (!on) Native.Accent(w, 0, 0); glassed.Remove(w); }
    }

    public static void OpenNet() {
        if (Net == null) Net = new NetWin();
        else if (Net.W.WindowState == WindowState.Minimized) Net.W.WindowState = WindowState.Normal;
        Net.W.Topmost = true; Net.W.Topmost = false; // sobe para o topo mesmo quando o Windows nega o foco
        Net.W.Activate();
    }

    public static void Quit() {
        Usage.Save(); Tray.Dispose(); Taskbar(false); Explorer(false); HomeDns.Stop();
        app.Shutdown();
    }
}

static class Ui {
    public static object Load(string name) {
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) return XamlReader.Load(s);
    }
    public static void Animate(IAnimatable target, DependencyProperty prop, double from, double to, int ms) {
        target.BeginAnimation(prop, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase() });
    }
    // Chamar em SourceInitialized: fundo do WPF transparente + desfoque do Windows atrás
    public static IntPtr Glass(Window w) {
        IntPtr h = new WindowInteropHelper(w).Handle;
        HwndSource.FromHwnd(h).CompositionTarget.BackgroundColor = Colors.Transparent;
        Native.Glass(h);
        return h;
    }
}

// ---------------------------------------------------------------- Widget da área de trabalho
class WidgetWin {
    public readonly Window W;
    readonly TextBlock clock, date, cpuT, ramT, ramL, tmpT, down, up, today;
    readonly Ellipse cpuR, ramR, tmpR;
    readonly Dictionary<Ellipse, double> shown = new Dictionary<Ellipse, double>();
    static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
    static readonly CultureInfo inv = CultureInfo.InvariantCulture, pt = new CultureInfo("pt-BR");
    const double C = 29.17; // circunferência do anel em unidades de traço (ver Widget.xaml)
    int tick; IntPtr hwnd;

    T F<T>(string n) where T : class { return W.FindName(n) as T; }

    public WidgetWin() {
        W = (Window)Ui.Load("Widget.xaml");
        clock = F<TextBlock>("Clock"); date = F<TextBlock>("Date"); cpuT = F<TextBlock>("CpuText"); ramT = F<TextBlock>("RamText"); ramL = F<TextBlock>("RamLabel");
        tmpT = F<TextBlock>("TempText"); down = F<TextBlock>("Down"); up = F<TextBlock>("Up"); today = F<TextBlock>("Today");
        cpuR = F<Ellipse>("CpuRing"); ramR = F<Ellipse>("RamRing"); tmpR = F<Ellipse>("TempRing");

        Rect wa = SystemParameters.WorkArea; double x, y;
        if (!double.TryParse(Settings.Get("x", ""), NumberStyles.Float, inv, out x) || !double.TryParse(Settings.Get("y", ""), NumberStyles.Float, inv, out y)
            || x < SystemParameters.VirtualScreenLeft || y < SystemParameters.VirtualScreenTop
            || x > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60 || y > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60) {
            x = wa.Right - W.Width - 28; y = wa.Top + 28; // padrão: canto superior direito (também se a posição salva caiu fora da tela)
        }
        W.Left = x; W.Top = y;

        W.SourceInitialized += delegate {
            IntPtr h = new WindowInteropHelper(W).Handle;
            HwndSource.FromHwnd(h).AddHook(Hook);
            Native.HideFromAltTab(h); Native.PinToDesktop(h, true); hwnd = h;
        };
        W.MouseLeftButtonDown += (s, e) => {
            if (e.ClickCount == 2) { Program.OpenNet(); return; }
            try { W.DragMove(); } catch { }
            Settings.Set("x", W.Left.ToString(inv)); Settings.Set("y", W.Top.ToString(inv));
        };

        var menu = new ContextMenu();
        var mNet = new MenuItem { Header = "Abrir monitor de rede" }; mNet.Click += delegate { Program.OpenNet(); };
        var mBar = new MenuItem { Header = "Barra de tarefas transparente" }; mBar.Click += delegate { Program.TaskbarOn = !Program.TaskbarOn; };
        var mExp = new MenuItem { Header = "Explorador com vidro" }; mExp.Click += delegate { Program.ExplorerOn = !Program.ExplorerOn; };
        var mDns = new MenuItem { Header = "DNS da casa (sites dos aparelhos)" }; mDns.Click += delegate { HomeDns.On = !HomeDns.On; };
        var mBlk = new MenuItem { Header = "Bloquear anúncios e rastreadores" }; mBlk.Click += delegate { HomeDns.BlockOn = !HomeDns.BlockOn; };
        var mAuto = new MenuItem { Header = "Iniciar com o Windows" }; mAuto.Click += delegate { Autostart.On = !Autostart.On; };
        var mQuit = new MenuItem { Header = "Sair do Aura" }; mQuit.Click += delegate { Program.Quit(); };
        menu.Items.Add(mNet); menu.Items.Add(mBar); menu.Items.Add(mExp); menu.Items.Add(mDns); menu.Items.Add(mBlk); menu.Items.Add(mAuto); menu.Items.Add(mQuit);
        menu.Opened += delegate { mBar.IsChecked = Program.TaskbarOn; mExp.IsChecked = Program.ExplorerOn; mDns.IsChecked = HomeDns.On; mBlk.IsChecked = HomeDns.BlockOn; mAuto.IsChecked = Autostart.On; };
        W.ContextMenu = menu;
        W.ToolTip = "Clique duplo: monitor de rede  ·  Botão direito: opções  ·  Arraste para mover";

        W.Show();
        Native.PinToDesktop(hwnd, true); // o Show() traz a janela para a frente; devolve para trás das outras
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        t.Tick += delegate { Tick(); };
        t.Start(); Tick();
    }

    IntPtr Hook(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) {
        // WM_ACTIVATE: clicar traria o widget para a frente das outras janelas; devolve para o lugar logo em seguida.
        if (msg == 0x06 && (wp.ToInt64() & 0xFFFF) != 0 && hwnd != IntPtr.Zero) W.Dispatcher.BeginInvoke(new Action(delegate { Native.PinToDesktop(h, true); }));
        return IntPtr.Zero;
    }
    // Chamado 4x por segundo: acompanha a área de trabalho quando o Windows troca a janela dos ícones (Win+D)
    public void Pin() { if (hwnd != IntPtr.Zero) Native.PinToDesktop(hwnd, false); }

    void Ring(Ellipse e, double p) {
        p = Math.Max(0, Math.Min(1, p)); double last;
        if (shown.TryGetValue(e, out last) && Math.Abs(last - p) < 0.02) return; // mudança invisível: não redesenha (CPU zero)
        shown[e] = p;
        var a = new DoubleAnimation(C * (1 - p), TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase() };
        Timeline.SetDesiredFrameRate(a, 30); // 30 quadros/s bastam para um anel pequeno e custam metade
        e.BeginAnimation(Shape.StrokeDashOffsetProperty, a);
    }
    static void Set(TextBlock t, string s) { if (t.Text != s) t.Text = s; }

    void Tick() {
        Usage.Tick();
        if (++tick % 60 == 0) Usage.Save();
        double cpu = Native.CpuPercent();
        if (Native.Fullscreen()) return; // jogo em tela cheia: continua contando o consumo, mas não desenha nada

        DateTime now = DateTime.Now;
        Set(clock, now.ToString("HH:mm"));
        string d = now.ToString("dddd, d 'de' MMMM", pt);
        Set(date, char.ToUpper(d[0]) + d.Substring(1));
        Ring(cpuR, cpu / 100); Set(cpuT, cpu.ToString("0") + "%");
        double used, total; Native.Memory(out used, out total);
        Ring(ramR, used / total); Set(ramT, (100 * used / total).ToString("0") + "%"); Set(ramL, "RAM " + used.ToString("0.0") + " GB");
        if (tick % 2 == 1) {
            int t = Gpu.Temp();
            Set(tmpT, t > 0 ? t + "°" : "—"); Ring(tmpR, t / 100.0);
            tmpR.Stroke = t >= 85 ? Kb.Red : t >= 72 ? Orange : Kb.Green;
        }
        Set(down, "↓ " + Fmt.Speed(Usage.DownBps)); Set(up, "↑ " + Fmt.Speed(Usage.UpBps));
        long[] td = Usage.Today(); Set(today, "hoje " + Fmt.Size(td[0] + td[1]));
    }
}

// ---------------------------------------------------------------- Janela do monitor de rede
class NetWin {
    public readonly Window W;
    Snapshot snap; int page; bool busy, sweep; string sig = "";
    readonly DispatcherTimer t1, t3;
    readonly TextBlock title, hint, down, up, peak, tSession, tToday, tMonth, status, statusDetail;
    readonly ScrollViewer overview, listScroll; readonly ItemsControl list, dnsList; readonly Button scan;
    readonly Polyline gDown, gUp; readonly Grid graphHost, content; readonly Ellipse statusDot; readonly Border root;
    readonly ScrollViewer routerScroll; readonly ItemsControl routerList; readonly Border routerLogin; readonly TextBlock houseDown, houseUp;
    readonly TextBox rUser; readonly PasswordBox rPass; readonly Button rForget; List<Row> routerRows, siteRows;
    readonly Border rState; readonly TextBlock rStateText; readonly ProgressBar rBar;
    readonly Border ndCard; readonly TextBox ndProfile; readonly PasswordBox ndKey; readonly Button ndForget;

    static readonly string[] Titles = { "Visão geral", "Conexões", "Domínios", "Portas", "Dispositivos", "Roteador", "Sites da casa" };
    static readonly string[] Hints = {
        "Quanto de internet este PC está usando agora e se há algo estranho acontecendo.",
        "Cada linha é um programa do seu PC falando com um servidor na internet: com quem, para quê e se é confiável. O que merece atenção fica no topo.",
        "Sites e serviços que este PC procurou. O histórico vai acumulando enquanto esta janela fica aberta. Rastreadores e telemetria aparecem no topo.",
        "Portas são as entradas do PC. Estas estão abertas esperando conexão, com o programa que abriu cada uma.",
        "Aparelhos encontrados na sua rede. O PC só enxerga o próprio tráfego: o consumo do celular e dos outros aparelhos só o roteador consegue medir.",
        "O que o próprio roteador da casa informa: internet de todos os aparelhos juntos e, com o seu login, os dados do painel dele.",
        "Sites que os aparelhos da casa procuraram (celular, TV, outros PCs), pelo NextDNS ou pelo DNS do próprio Aura. Mostra só o nome do site, nunca o conteúdo. Anúncios, rastreadores e telemetria são bloqueados.",
    };

    T F<T>(string n) where T : class { return W.FindName(n) as T; }

    public NetWin() {
        W = (Window)Ui.Load("Net.xaml");
        title = F<TextBlock>("PageTitle"); hint = F<TextBlock>("PageHint"); down = F<TextBlock>("Down"); up = F<TextBlock>("Up"); peak = F<TextBlock>("Peak");
        tSession = F<TextBlock>("TSession"); tToday = F<TextBlock>("TToday"); tMonth = F<TextBlock>("TMonth"); status = F<TextBlock>("Status"); statusDetail = F<TextBlock>("StatusDetail");
        overview = F<ScrollViewer>("Overview"); listScroll = F<ScrollViewer>("ListScroll"); list = F<ItemsControl>("List"); dnsList = F<ItemsControl>("DnsList"); scan = F<Button>("Scan");
        routerScroll = F<ScrollViewer>("RouterScroll"); routerList = F<ItemsControl>("RouterList"); routerLogin = F<Border>("RouterLogin");
        houseDown = F<TextBlock>("HouseDown"); houseUp = F<TextBlock>("HouseUp"); rUser = F<TextBox>("RUser"); rPass = F<PasswordBox>("RPass"); rForget = F<Button>("RForget");
        rState = F<Border>("RState"); rStateText = F<TextBlock>("RStateText"); rBar = F<ProgressBar>("RBar");
        ndCard = F<Border>("NdCard"); ndProfile = F<TextBox>("NdProfile"); ndKey = F<PasswordBox>("NdKey"); ndForget = F<Button>("NdForget");
        gDown = F<Polyline>("GDown"); gUp = F<Polyline>("GUp"); graphHost = F<Grid>("GraphHost"); content = F<Grid>("Content"); statusDot = F<Ellipse>("StatusDot"); root = F<Border>("Root");

        // Maximizar sem cobrir a barra de tarefas. O Windows posiciona a janela maximizada com a moldura (7 px) para fora da tela,
        // então o limite inclui a moldura e o conteúdo ganha essa margem para não ficar cortado.
        double frame = SystemParameters.WindowResizeBorderThickness.Left + SystemParameters.FixedFrameVerticalBorderWidth;
        W.MaxHeight = SystemParameters.WorkArea.Height + 2 * frame; W.MaxWidth = SystemParameters.WorkArea.Width + 2 * frame;
        // WindowChrome: remove a moldura nativa mas mantém redimensionar pelas bordas, encaixe (Win+setas) e maximizar
        System.Windows.Shell.WindowChrome.SetWindowChrome(W, new System.Windows.Shell.WindowChrome {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(-1), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        W.SourceInitialized += delegate { Ui.Glass(W); };
        W.SizeChanged += delegate { Graph(); };
        var btnMax = F<Button>("BtnMax");
        W.StateChanged += delegate {
            bool max = W.WindowState == WindowState.Maximized;
            root.Margin = new Thickness(max ? frame : 0);
            btnMax.Content = max ? "" : ""; // restaurar / maximizar
        };
        root.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed && W.WindowState == WindowState.Normal) try { W.DragMove(); } catch { } };
        F<Button>("BtnClose").Click += delegate { W.Close(); };
        F<Button>("BtnMin").Click += delegate { W.WindowState = WindowState.Minimized; };
        F<Button>("BtnMax").Click += delegate { W.WindowState = W.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
        for (int i = 0; i < 7; i++) { int p = i; F<RadioButton>("Nav" + i).Checked += delegate { Show(p); }; }
        scan.Click += delegate { sweep = true; scan.IsEnabled = false; scan.Content = "Procurando…"; Refresh(); };
        RoutedEventHandler save = delegate {
            if (rUser.Text.Trim() == "" || rPass.Password == "") return;
            Router.SaveLogin(rUser.Text, rPass.Password); rPass.Clear(); RouterUi(); Refresh();
        };
        F<Button>("RSave").Click += save;
        rPass.KeyDown += (s, e) => { if (e.Key == Key.Enter) save(s, e); };
        rForget.Click += delegate { Router.Forget(); RouterUi(); Refresh(); };
        RoutedEventHandler ndSave = delegate {
            if (NextDns.CleanProfile(ndProfile.Text) == "" || ndKey.Password.Trim() == "") { ndProfile.Text = "ID inválido (6 letras/números)"; return; }
            NextDns.Save(ndProfile.Text, ndKey.Password); ndKey.Clear(); NdUi(); Refresh();
        };
        F<Button>("NdSave").Click += ndSave;
        ndKey.KeyDown += (s, e) => { if (e.Key == Key.Enter) ndSave(s, e); };
        ndForget.Click += delegate { NextDns.Forget(); NdUi(); Refresh(); };

        t1 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; t1.Tick += delegate { Live(); RouterState(); };
        t3 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; t3.Tick += delegate { Refresh(); };
        W.Closed += delegate { t1.Stop(); t3.Stop(); Program.Net = null; };

        Show(0);
        W.Show();
        Ui.Animate(W, UIElement.OpacityProperty, 0, 1, 220);
        var sc = (ScaleTransform)root.RenderTransform;
        Ui.Animate(sc, ScaleTransform.ScaleXProperty, 0.97, 1, 260); Ui.Animate(sc, ScaleTransform.ScaleYProperty, 0.97, 1, 260);
        t1.Start(); t3.Start(); Live(); Refresh();
    }

    void Show(int p) {
        page = p; sig = "";
        title.Text = Titles[p]; hint.Text = Hints[p];
        overview.Visibility = p == 0 ? Visibility.Visible : Visibility.Collapsed;
        listScroll.Visibility = (p >= 1 && p <= 4) || p == 6 ? Visibility.Visible : Visibility.Collapsed;
        routerScroll.Visibility = p == 5 ? Visibility.Visible : Visibility.Collapsed;
        if (p == 5) RouterUi();
        NdUi();
        scan.Visibility = p == 4 ? Visibility.Visible : Visibility.Collapsed;
        listScroll.ScrollToTop();
        Bind(); Live();
        Ui.Animate(content, UIElement.OpacityProperty, 0, 1, 260);
        Ui.Animate((TranslateTransform)content.RenderTransform, TranslateTransform.YProperty, 14, 0, 300);
    }

    // Números ao vivo (1x por segundo, só na Visão geral e com a janela visível)
    void Live() {
        if (page != 0 || W.WindowState == WindowState.Minimized) return;
        down.Text = Fmt.Speed(Usage.DownBps); up.Text = Fmt.Speed(Usage.UpBps);
        var hv = Router.UpnpOk ? Visibility.Visible : Visibility.Collapsed; houseDown.Visibility = houseUp.Visibility = hv;
        houseDown.Text = "Casa toda: " + Fmt.Speed(Router.HouseDown); houseUp.Text = "Casa toda: " + Fmt.Speed(Router.HouseUp);
        long[] td = Usage.Today(), mo = Usage.Month();
        tSession.Text = "↓ " + Fmt.Size(Usage.SessionRx) + "\n↑ " + Fmt.Size(Usage.SessionTx);
        tToday.Text = "↓ " + Fmt.Size(td[0]) + "\n↑ " + Fmt.Size(td[1]);
        tMonth.Text = "↓ " + Fmt.Size(mo[0]) + "\n↑ " + Fmt.Size(mo[1]);
        Graph();
    }

    void Graph() {
        double w = graphHost.ActualWidth, h = graphHost.ActualHeight;
        if (page != 0 || w < 10 || h < 10) return;
        double max = 10 * 1024;
        for (int i = 0; i < 60; i++) max = Math.Max(max, Math.Max(Usage.HistDown[i], Usage.HistUp[i]));
        var pd = new PointCollection(60); var pu = new PointCollection(60);
        for (int i = 0; i < 60; i++) {
            int idx = (Usage.HistPos + 1 + i) % 60; double x = w * i / 59.0;
            pd.Add(new Point(x, h - 2 - (h - 4) * Usage.HistDown[idx] / max));
            pu.Add(new Point(x, h - 2 - (h - 4) * Usage.HistUp[idx] / max));
        }
        gDown.Points = pd; gUp.Points = pu;
        peak.Text = "PICO " + Fmt.Speed(max).ToUpper();
    }

    async void Refresh() {
        if (busy || W.WindowState == WindowState.Minimized) return;
        busy = true; bool sw = sweep; sweep = false;
        bool rt = page == 5;
        try {
            snap = await Task.Run(() => Collector.Collect(sw));
            if (rt) routerRows = await Task.Run(() => Router.Read()); // painel do roteador só é lido com a aba aberta
            if (page == 6) siteRows = await Task.Run(() => NextDns.HasKey ? NextDns.Read().Concat(HomeDns.Queries > 0 ? HomeDns.Rows() : new List<Row>()).ToList() : HomeDns.Rows());
            Bind();
        }
        catch (Exception ex) { Program.Log(ex); }
        finally { busy = false; if (sw) { scan.IsEnabled = true; scan.Content = "Procurar aparelhos"; } }
    }

    void Bind() {
        if (snap == null) return;
        if (page == 0) {
            if (snap.Sus > 0) { statusDot.Fill = Kb.Red; status.Text = "Encontrei " + snap.Sus + (snap.Sus == 1 ? " item suspeito." : " itens suspeitos.") + " Veja as abas Conexões e Portas: eles aparecem no topo."; }
            else if (snap.Warn + snap.Trackers > 0) { statusDot.Fill = Kb.Yellow; status.Text = "Nada perigoso. " + snap.Warn + (snap.Warn == 1 ? " ponto de atenção" : " pontos de atenção") + " e " + snap.Trackers + (snap.Trackers == 1 ? " rastreador." : " rastreadores."); }
            else { statusDot.Fill = Kb.Green; status.Text = "Tudo tranquilo. Nenhuma conexão suspeita no momento."; }
            statusDetail.Text = snap.Total + " conexões ativas em " + snap.Apps + " programas  ·  " + snap.OpenPorts + " portas abertas para a rede  ·  " + snap.Devices.Count + " aparelhos conhecidos na rede";
            SetList(dnsList, snap.Dns);
        } else if (page == 5) { if (routerRows != null) { SetList(routerList, routerRows); RouterUi(); } }
        else if (page == 6) { if (siteRows != null) SetList(list, siteRows); }
        else SetList(list, page == 1 ? snap.Conns : page == 2 ? snap.Domains : page == 3 ? snap.Ports : snap.Devices);
    }

    void NdUi() {
        bool has = NextDns.HasKey;
        ndCard.Visibility = page == 6 && !has ? Visibility.Visible : Visibility.Collapsed;
        ndForget.Visibility = page == 6 && has ? Visibility.Visible : Visibility.Collapsed;
    }

    void RouterUi() {
        bool has = Router.HasLogin;
        routerLogin.Visibility = has || !Router.CanLogin ? Visibility.Collapsed : Visibility.Visible; // marca sem leitura: não pede senha à toa
        rForget.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has && rUser.Text == "") rUser.Text = Router.User;
        RouterState();
    }

    // Cartão de estado da aba Roteador (1x por segundo): mostra a etapa enquanto lê, a hora da última leitura ou o erro
    void RouterState() {
        if (page != 5) return;
        if (!Router.HasLogin || !Router.CanLogin) { rState.Visibility = Visibility.Collapsed; return; }
        string ph = Router.Phase; bool work = ph != "";
        rState.Visibility = Visibility.Visible;
        rBar.Visibility = work ? Visibility.Visible : Visibility.Collapsed;
        rStateText.Foreground = work || Router.LastOk ? (Brush)new SolidColorBrush(Color.FromArgb(0xD9, 255, 255, 255)) : Kb.Red;
        rStateText.Text = work ? ph
            : Router.LastOk ? "✓ Painel lido às " + Router.OkAt.ToString("HH:mm:ss") + "  ·  atualiza a cada 15 s"
            : "✕ Falhou: " + (Router.Status != "" ? Router.Status : "o roteador não respondeu.");
    }

    // Só troca a lista se algo mudou: evita piscar e gastar CPU a cada 3 s
    void SetList(ItemsControl target, List<Row> rows) {
        string s = page + "#" + string.Join("|", rows.Select(r => r.Title + r.Sub + r.Badge));
        if (s == sig) return;
        sig = s; target.ItemsSource = rows;
    }
}
}
