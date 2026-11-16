// Ícone na bandeja (ao lado do relógio do Windows). Usa o ícone embutido no exe (gerado pelo build.ps1).
using System.Drawing;
using System.Reflection;
using WF = System.Windows.Forms;

namespace Aura {
static class Tray {
    static WF.NotifyIcon icon;

    public static void Create() {
        var bar = new WF.MenuItem("Barra de tarefas transparente", delegate { Program.TaskbarOn = !Program.TaskbarOn; });
        var exp = new WF.MenuItem("Explorador com vidro", delegate { Program.ExplorerOn = !Program.ExplorerOn; });
        var dns = new WF.MenuItem("DNS da casa (sites dos aparelhos)", delegate { HomeDns.On = !HomeDns.On; });
        var blk = new WF.MenuItem("Bloquear anúncios e rastreadores", delegate { HomeDns.BlockOn = !HomeDns.BlockOn; });
        var auto = new WF.MenuItem("Iniciar com o Windows", delegate { Autostart.On = !Autostart.On; });
        var menu = new WF.ContextMenu(new[] {
            new WF.MenuItem("Abrir monitor de rede", delegate { Program.OpenNet(); }), bar, exp, dns, blk, auto,
            new WF.MenuItem("-"), new WF.MenuItem("Sair do Aura", delegate { Program.Quit(); }),
        });
        menu.Popup += delegate { bar.Checked = Program.TaskbarOn; exp.Checked = Program.ExplorerOn; dns.Checked = HomeDns.On; blk.Checked = HomeDns.BlockOn; auto.Checked = Autostart.On; };
        icon = new WF.NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location), Text = "Aura: clique para abrir o monitor de rede", ContextMenu = menu, Visible = true };
        icon.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) Program.OpenNet(); };
    }

    public static void Dispose() { if (icon != null) { icon.Visible = false; icon.Dispose(); } }
}
}
