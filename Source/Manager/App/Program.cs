using BLCA.Admin;

namespace BLCA.Admin.Manager;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, Branding.Product, MessageBoxButtons.OK, MessageBoxIcon.Error);
        string Arg(string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : ""; }
        if (args.Contains("--agent"))
        {
            var profile = Arg("--profile");
            if (profile.Length == 0) { MessageBox.Show("استخدم --agent --profile مع مسار ملف تعريف الخادم."); return; }
            using var agent = new SuiteAgent(new SuiteStore(profile)) { NativeLogsFolder = Arg("--logs") };
            using var context = new ApplicationContext();
            using var tray = new NotifyIcon { Icon = Branding.Icon, Text = Branding.Product + " • وكيل الخادم", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
            tray.ContextMenuStrip.Items.Add("إيقاف الوكيل", null, (_, _) => context.ExitThread());
            using var timer = new System.Windows.Forms.Timer { Interval = 1000 }; bool busy = false;
            timer.Tick += async (_, _) => { if (busy) return; busy = true; try { await Task.Run(agent.Tick); } finally { busy = false; } };
            timer.Start(); Application.Run(context); tray.Visible = false; return;
        }
        var capture = Arg("--capture");
        var form = new MainForm(Arg("--profile"), capture.Length > 0);
        if (capture.Length > 0)
        {
            if (int.TryParse(Arg("--capture-width"), out var width) && int.TryParse(Arg("--capture-height"), out var height)
                && width >= 1120 && width <= 3840 && height >= 730 && height <= 2160)
                form.Size = new Size(width, height);
            form.Opacity = 0; form.ShowInTaskbar = false;
            form.Shown += async (_, _) => { await Task.Delay(400); try { await form.CapturePages(capture); } finally { form.Close(); } };
        }
        Application.Run(form);
    }
}
