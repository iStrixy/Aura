// Chamadas nativas do Windows: CPU, RAM, tabela de conexões, vidro (blur), fixar no desktop, temperatura da GPU AMD.
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace Aura {

class Conn {
    public bool Tcp, Listen;
    public IPAddress Local, Remote;
    public int LocalPort, RemotePort, Pid;
}

static class Native {
    // ---------- CPU / RAM (sem PerformanceCounter: os contadores deste Windows estão quebrados)
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    static long pIdle, pKernel, pUser;
    public static double CpuPercent() {
        long i, k, u;
        if (!GetSystemTimes(out i, out k, out u)) return 0;
        long di = i - pIdle, dt = (k - pKernel) + (u - pUser);
        pIdle = i; pKernel = k; pUser = u;
        if (dt <= 0) return 0;
        return Math.Max(0, Math.Min(100, 100.0 * (dt - di) / dt));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    public static void Memory(out double usedGb, out double totalGb) {
        var m = new MEMORYSTATUSEX(); m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        GlobalMemoryStatusEx(ref m);
        totalGb = m.ullTotalPhys / 1073741824.0;
        usedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
    }

    // ---------- Tabela de conexões TCP/UDP com PID
    [DllImport("iphlpapi.dll")] static extern int GetExtendedTcpTable(IntPtr t, ref int size, bool order, int af, int cls, int reserved);
    [DllImport("iphlpapi.dll")] static extern int GetExtendedUdpTable(IntPtr t, ref int size, bool order, int af, int cls, int reserved);

    static byte[] Table(bool tcp, int af) {
        int size = 0;
        for (int tries = 0; tries < 4; tries++) {
            IntPtr buf = size > 0 ? Marshal.AllocHGlobal(size) : IntPtr.Zero;
            try {
                int r = tcp ? GetExtendedTcpTable(buf, ref size, false, af, 5, 0) : GetExtendedUdpTable(buf, ref size, false, af, 1, 0);
                if (r == 0 && buf != IntPtr.Zero) { var b = new byte[size]; Marshal.Copy(buf, b, 0, size); return b; }
                if (r != 122 || size < 4) return null; // 122 = buffer pequeno: tenta de novo com o tamanho pedido
            } finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }
        return null;
    }
    static int Port(byte[] b, int o) { return (b[o] << 8) | b[o + 1]; }
    static IPAddress Ip(byte[] b, int o, int len) {
        var a = new byte[len]; Buffer.BlockCopy(b, o, a, 0, len);
        var ip = new IPAddress(a);
        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    }

    public static List<Conn> Connections() {
        var list = new List<Conn>();
        byte[] b = Table(true, 2);
        if (b != null) for (int n = BitConverter.ToInt32(b, 0), i = 0, o = 4; i < n && o + 24 <= b.Length; i++, o += 24) {
            int st = BitConverter.ToInt32(b, o);
            if (st != 2 && st != 5) continue; // só LISTEN e ESTABLISHED
            list.Add(new Conn { Tcp = true, Listen = st == 2, Local = Ip(b, o + 4, 4), LocalPort = Port(b, o + 8), Remote = Ip(b, o + 12, 4), RemotePort = Port(b, o + 16), Pid = BitConverter.ToInt32(b, o + 20) });
        }
        b = Table(true, 23);
        if (b != null) for (int n = BitConverter.ToInt32(b, 0), i = 0, o = 4; i < n && o + 56 <= b.Length; i++, o += 56) {
            int st = BitConverter.ToInt32(b, o + 48);
            if (st != 2 && st != 5) continue;
            list.Add(new Conn { Tcp = true, Listen = st == 2, Local = Ip(b, o, 16), LocalPort = Port(b, o + 20), Remote = Ip(b, o + 24, 16), RemotePort = Port(b, o + 44), Pid = BitConverter.ToInt32(b, o + 52) });
        }
        b = Table(false, 2);
        if (b != null) for (int n = BitConverter.ToInt32(b, 0), i = 0, o = 4; i < n && o + 12 <= b.Length; i++, o += 12)
            list.Add(new Conn { Listen = true, Local = Ip(b, o, 4), LocalPort = Port(b, o + 4), Pid = BitConverter.ToInt32(b, o + 8) });
        b = Table(false, 23);
        if (b != null) for (int n = BitConverter.ToInt32(b, 0), i = 0, o = 4; i < n && o + 28 <= b.Length; i++, o += 28)
            list.Add(new Conn { Listen = true, Local = Ip(b, o, 16), LocalPort = Port(b, o + 20), Pid = BitConverter.ToInt32(b, o + 24) });
        return list;
    }

    // ---------- Caminho do executável de um PID (funciona sem admin para quase tudo)
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    public static string ProcessPath(int pid) {
        IntPtr h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try { var sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null; }
        finally { CloseHandle(h); }
    }

    // ---------- Vidro: blur atrás da janela (mesma API que a barra de tarefas/TranslucentTB usam)
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int State, Flags, Color, Anim; }
    [StructLayout(LayoutKind.Sequential)] struct WinCompAttr { public int Attribute; public IntPtr Data; public int Size; }
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttr data);
    // state: 0 = normal, 2 = transparente com cor, 3 = blur, 4 = acrílico. abgr = cor de tinta (AABBGGRR)
    public static void Accent(IntPtr hwnd, int state, uint abgr) {
        if (hwnd == IntPtr.Zero) return;
        var a = new AccentPolicy { State = state, Flags = 2, Color = unchecked((int)abgr) };
        int sz = Marshal.SizeOf(typeof(AccentPolicy));
        IntPtr p = Marshal.AllocHGlobal(sz);
        try { Marshal.StructureToPtr(a, p, false); var d = new WinCompAttr { Attribute = 19, Data = p, Size = sz }; SetWindowCompositionAttribute(hwnd, ref d); }
        finally { Marshal.FreeHGlobal(p); }
    }

    [StructLayout(LayoutKind.Sequential)] struct Margins { public int L, R, T, B; }
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
    // Janela de vidro: o fundo transparente da janela inteira passa a mostrar o desfoque do que está atrás.
    // Limite do Windows 10 (testado com 6 combinações): o desfoque é sempre retangular, ignora SetWindowRgn e
    // DwmEnableBlurBehindWindow. Por isso só a janela de rede usa desfoque (raio de canto pequeno) e o widget não.
    public static void Glass(IntPtr hwnd) {
        var m = new Margins { L = -1, R = -1, T = -1, B = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref m);
        Accent(hwnd, 3, 0);
    }

    // ---------- Fixar o widget na área de trabalho (atrás das janelas, sobrevive ao Win+D)
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, int cmd);
    // Janelas do Explorador de Arquivos abertas agora
    public static List<IntPtr> ExplorerWindows() {
        var l = new List<IntPtr>();
        for (IntPtr w = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "CabinetWClass", null); w != IntPtr.Zero; w = FindWindowEx(IntPtr.Zero, w, "CabinetWClass", null)) l.Add(w);
        return l;
    }
    public static void HideFromAltTab(IntPtr hwnd) { SetWindowLongPtr(hwnd, -20, (IntPtr)(GetWindowLongPtr(hwnd, -20).ToInt64() | 0x80)); }

    // Janela que hospeda os ícones da área de trabalho. Normalmente é o Progman; no Win+D ("mostrar área de trabalho")
    // o Windows move os ícones para um WorkerW e o traz para a frente de tudo.
    static IntPtr DesktopHost() {
        IntPtr p = FindWindow("Progman", null);
        if (FindWindowEx(p, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return p;
        for (IntPtr w = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "WorkerW", null); w != IntPtr.Zero; w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null))
            if (FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return w;
        return p;
    }
    // Mantém o widget "colado" na área de trabalho: o dono é quem hospeda os ícones (o Windows mantém janelas acima do dono).
    // Modo normal: fundo da pilha, atrás de todas as janelas. Win+D: logo acima da área de trabalho que veio para a frente.
    public static void PinToDesktop(IntPtr hwnd, bool force) {
        IntPtr host = DesktopHost();
        bool changed = GetWindow(hwnd, 4) != host;
        if (changed) SetWindowLongPtr(hwnd, -8, host);
        if (changed || force) SetWindowPos(hwnd, host == FindWindow("Progman", null) ? (IntPtr)1 : IntPtr.Zero, 0, 0, 0, 0, 0x13);
    }

    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);

    // ---------- Jogo em tela cheia? (pausa a atualização visual dos widgets)
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    public static bool Fullscreen() { int s; return SHQueryUserNotificationState(out s) == 0 && (s == 2 || s == 3 || s == 4); }
}

// Temperatura da GPU AMD via ADL (atiadlxx.dll vem com o driver). CPU não tem sensor acessível sem driver ring0.
static class Gpu {
    delegate IntPtr AdlMalloc(int size);
    static readonly AdlMalloc malloc = delegate(int s) { return Marshal.AllocCoTaskMem(s); };
    [DllImport("atiadlxx.dll")] static extern int ADL2_Main_Control_Create(AdlMalloc cb, int connectedOnly, out IntPtr ctx);
    [DllImport("atiadlxx.dll")] static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr ctx, ref int n);
    [DllImport("atiadlxx.dll")] static extern int ADL2_OverdriveN_Temperature_Get(IntPtr ctx, int adapter, int type, ref int milli);
    [DllImport("atiadlxx.dll")] static extern int ADL2_New_QueryPMLogData_Get(IntPtr ctx, int adapter, IntPtr data);

    static IntPtr ctx; static int adapter = -2, mode; // adapter -2 = ainda não procurou, -1 = sem suporte
    public static string Source = "";

    static int Read(int a, int m) {
        if (m == 0) { int t = 0; return ADL2_OverdriveN_Temperature_Get(ctx, a, 1, ref t) == 0 ? t / 1000 : 0; }
        IntPtr p = Marshal.AllocHGlobal(4 + 256 * 8); // ADLPMLogDataOutput: size + 256 x {supported, value}
        try {
            for (int i = 0; i < 4 + 256 * 8; i += 4) Marshal.WriteInt32(p, i, 0);
            if (ADL2_New_QueryPMLogData_Get(ctx, a, p) != 0) return 0;
            return Marshal.ReadInt32(p, 4 + 8 * 8) != 0 ? Marshal.ReadInt32(p, 4 + 8 * 8 + 4) : 0; // sensor 8 = TEMPERATURE_EDGE
        } finally { Marshal.FreeHGlobal(p); }
    }

    // Retorna °C ou 0 se indisponível
    public static int Temp() {
        if (adapter == -1) return 0;
        try {
            if (adapter == -2) {
                adapter = -1;
                if (ADL2_Main_Control_Create(malloc, 1, out ctx) != 0) return 0;
                int n = 0; ADL2_Adapter_NumberOfAdapters_Get(ctx, ref n);
                for (int m = 0; m < 2 && adapter < 0; m++)
                    for (int a = 0; a < n && adapter < 0; a++) {
                        int t = 0; try { t = Read(a, m); } catch (EntryPointNotFoundException) { }
                        if (t > 5 && t < 125) { adapter = a; mode = m; Source = m == 0 ? "ADL OverdriveN" : "ADL PMLog"; }
                    }
                if (adapter < 0) return 0;
            }
            int v = Read(adapter, mode);
            return v > 5 && v < 125 ? v : 0;
        } catch { adapter = -1; return 0; } // dll ausente (GPU não-AMD) ou driver sem a função
    }
}
}
