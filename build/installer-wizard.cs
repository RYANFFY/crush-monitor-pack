using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

// Crush 好感监控器 · 安装向导（单文件 exe）
// 欢迎页 → 选安装位置 → 进度条解压 → 建快捷方式 + 注册卸载信息 → 可直接运行。
// 不写系统目录、不需要管理员权限；卸载可从「应用和功能」里点，也能双击安装目录里的 卸载.cmd。
internal static class Installer
{
    private const string PayloadResource = "payload.zip";
    private const string AppName = "Crush 好感监控器";
    private const string LauncherName = "好感度监测器.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CrushMonitor";

    private static readonly string DefaultTarget = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CrushMonitor");

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 静默安装（给自己/脚本用）：--target <目录>
        var silentTarget = ParseTarget(args);
        if (silentTarget != null)
        {
            try
            {
                Install(silentTarget, null);
                Launch(silentTarget);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("安装失败：" + error.Message);
                return 1;
            }
        }

        using (var wizard = new WizardForm())
        {
            Application.Run(wizard);
        }
        return 0;
    }

    private static string ParseTarget(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--target")
                return args[i + 1];
        return null;
    }

    private static void Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(target, LauncherName),
                WorkingDirectory = target,
                WindowStyle = ProcessWindowStyle.Normal,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    // ── 安装逻辑 ───────────────────────────────────────────────
    private static void Install(string target, IProgress<int> progress)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), "crush-payload-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
        {
            if (resource == null) throw new Exception("安装包损坏：找不到内嵌数据");
            using (var file = File.Create(zipPath)) resource.CopyTo(file);
        }

        var staging = Path.Combine(Path.GetTempPath(), "crush-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            Report(progress, 15);
            ZipFile.ExtractToDirectory(zipPath, staging);
            Report(progress, 55);
            if (!File.Exists(Path.Combine(staging, "node.exe")))
                throw new Exception("解压结果不完整");

            Directory.CreateDirectory(target);
            CopyTree(staging, target, progress);
            Report(progress, 85);

            CreateShortcut(target, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"));
            CreateShortcut(target, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"));
            RegisterUninstall(target);
            Report(progress, 100);
        }
        finally
        {
            TryDelete(staging);
            TryDelete(zipPath);
        }
    }

    private static void Report(IProgress<int> progress, int value)
    {
        if (progress != null) progress.Report(value);
    }

    private static void CopyTree(string from, string to, IProgress<int> progress)
    {
        foreach (var directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, directory.Substring(from.Length).TrimStart('\\', '/')));
        var files = Directory.GetFiles(from, "*", SearchOption.AllDirectories);
        for (var i = 0; i < files.Length; i++)
        {
            var relative = files[i].Substring(from.Length).TrimStart('\\', '/');
            var destination = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(files[i], destination, true);
            Report(progress, 55 + (int)(30.0 * (i + 1) / Math.Max(1, files.Length)));
        }
    }

    private static void CreateShortcut(string target, string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = Path.Combine(target, LauncherName);
            shortcut.WorkingDirectory = target;
            shortcut.WindowStyle = 1;
            shortcut.Description = AppName;
            shortcut.IconLocation = Path.Combine(target, "app.ico");
            shortcut.Save();
        }
        catch
        {
        }
    }

    private static void RegisterUninstall(string target)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                if (key == null) return;
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", "1.1.1");
                key.SetValue("Publisher", "crush-monitor (开源项目)");
                key.SetValue("InstallLocation", target);
                key.SetValue("DisplayIcon", Path.Combine(target, LauncherName));
                key.SetValue("UninstallString", "\"" + Path.Combine(target, "卸载.cmd") + "\"");
                key.SetValue("QuietUninstallString", "\"" + Path.Combine(target, "卸载.cmd") + "\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    // ── 安装向导界面 ───────────────────────────────────────────
    private sealed class WizardForm : Form
    {
        private readonly TextBox pathBox = new TextBox();
        private readonly Button browseButton = new Button();
        private readonly Button installButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly ProgressBar bar = new ProgressBar();
        private readonly Label statusLabel = new Label();
        private readonly CheckBox runCheck = new CheckBox();
        private bool installed;

        public WizardForm()
        {
            Text = AppName + " 安装向导";
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(540, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label
            {
                Text = "安装 " + AppName,
                Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
                Location = new Point(24, 22),
                AutoSize = true
            };
            var subtitle = new Label
            {
                Text = "本机运行、不需要管理员权限。装好后可以直接打开使用。",
                ForeColor = Color.FromArgb(0x66, 0x6B, 0x69),
                Location = new Point(26, 56),
                AutoSize = true
            };
            var pathLabel = new Label
            {
                Text = "安装位置：",
                Location = new Point(26, 96),
                AutoSize = true
            };
            pathBox.Text = DefaultTarget;
            pathBox.Location = new Point(100, 93);
            pathBox.Width = 310;
            browseButton.Text = "浏览…";
            browseButton.Location = new Point(420, 91);
            browseButton.Click += delegate
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "选择安装位置";
                    dialog.SelectedPath = pathBox.Text;
                    if (dialog.ShowDialog() == DialogResult.OK) pathBox.Text = dialog.SelectedPath;
                }
            };
            bar.Location = new Point(26, 140);
            bar.Size = new Size(488, 18);
            statusLabel.Location = new Point(26, 166);
            statusLabel.AutoSize = true;
            statusLabel.ForeColor = Color.FromArgb(0x66, 0x6B, 0x69);
            statusLabel.Text = "点「开始安装」开始。";
            runCheck.Text = "安装完成后立即打开";
            runCheck.Checked = true;
            runCheck.Location = new Point(26, 200);
            runCheck.AutoSize = true;
            installButton.Text = "开始安装";
            installButton.Size = new Size(120, 32);
            installButton.Location = new Point(262, 240);
            installButton.Click += delegate { DoInstall(); };
            cancelButton.Text = "取消";
            cancelButton.Size = new Size(90, 32);
            cancelButton.Location = new Point(392, 240);
            cancelButton.Click += delegate { Close(); };

            Controls.AddRange(new Control[]
            {
                title, subtitle, pathLabel, pathBox, browseButton, bar, statusLabel, runCheck,
                installButton, cancelButton
            });
        }

        private void DoInstall()
        {
            var target = pathBox.Text.Trim();
            if (target.Length == 0)
            {
                MessageBox.Show(this, "请先选择安装位置。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            installButton.Enabled = false;
            browseButton.Enabled = false;
            pathBox.Enabled = false;
            var progress = new Progress<int>(value => bar.Value = Math.Max(0, Math.Min(100, value)));
            try
            {
                statusLabel.Text = "正在安装…";
                Install(target, progress);
                installed = true;
                statusLabel.Text = "安装完成。";
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "安装失败：" + error.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                installButton.Enabled = true;
                browseButton.Enabled = true;
                pathBox.Enabled = true;
                statusLabel.Text = "安装失败，可以换个位置再试。";
                return;
            }

            if (runCheck.Checked) Launch(target);
            MessageBox.Show(this,
                "安装完成！\n\n位置：" + target +
                "\n已创建桌面和开始菜单快捷方式，这个安装包现在可以删掉了。" +
                "\n\n以后想卸载：开始菜单右键「" + AppName + "」或在「设置 → 应用」里卸载。",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!installed && installButton.Enabled == false) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
