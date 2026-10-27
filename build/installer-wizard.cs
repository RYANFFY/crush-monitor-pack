using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

// Crush 好感监控器 · 安装向导（单文件 exe）
// 欢迎页 → 选安装位置 → 进度条解压 → 建快捷方式 + 注册卸载信息 → 可直接运行。
// 不写系统目录、不需要管理员权限；卸载可从「应用和功能」里点，也能双击安装目录里的 卸载.cmd。
//
// 安全约束（issue #1：v1.1.1-win.1 卸载会递归删掉整个安装目录，装到盘根时表现为「清空整盘」）：
//   1. 安装位置规范化后拒绝盘符根目录和系统目录；
//   2. 永远装进独立的 CrushMonitor 子目录，不把用户选的文件夹直接当成软件目录；
//   3. 目标目录已存在且不属于本程序时拒绝写入；
//   4. 安装完成后写一份文件清单，卸载只按清单删除，用户新增的文件一律保留。
internal static class Installer
{
    private const string PayloadResource = "payload.zip";
    private const string AppName = "Crush 好感监控器";
    private const string LauncherName = "好感度监测器.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CrushMonitor";
    private const string ManifestName = ".crushmonitor-manifest.txt";
    private const string AppFolderName = "CrushMonitor";
    private const string Version = "1.1.2";

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
                var installed = Install(silentTarget, null);
                Launch(installed);
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

    // ── 安装位置校验（issue #1 的核心修复）───────────────────────
    // 卸载脚本以前会把「脚本所在目录」整棵递归删除，所以安装目录必须保证：
    // 是独立子目录、不是盘根、不是系统目录、里面的东西确实都是我们装的。
    private static string ResolveTarget(string raw)
    {
        var value = (raw ?? string.Empty).Trim().Trim('"').Trim();
        if (value.Length == 0) throw new Exception("请先选择安装位置。");
        value = Environment.ExpandEnvironmentVariables(value);
        if (!Path.IsPathRooted(value))
            throw new Exception("请填写完整路径，例如 D:\\软件\\CrushMonitor。");

        string full;
        try
        {
            full = Path.GetFullPath(value).TrimEnd('\\', '/');
        }
        catch
        {
            throw new Exception("路径格式不正确：" + raw);
        }
        if (full.Length == 0) throw new Exception("路径格式不正确：" + raw);

        RejectDangerousPath(full);

        // 用户选的是「放在哪」，不是「装成哪个文件夹」：统一追加独立子目录
        if (!string.Equals(Path.GetFileName(full), AppFolderName, StringComparison.OrdinalIgnoreCase))
        {
            full = Path.Combine(full, AppFolderName);
            RejectDangerousPath(full);
        }
        return full;
    }

    private static void RejectDangerousPath(string full)
    {
        var trimmed = full.TrimEnd('\\', '/');

        var root = Path.GetPathRoot(trimmed);
        if (string.IsNullOrEmpty(root) ||
            string.Equals(trimmed, root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            throw new Exception(
                "不能安装到盘符根目录（例如 D:\\）。\n\n" +
                "请选择一个具体的文件夹，安装程序会自动在里面创建 " + AppFolderName + " 子目录。\n" +
                "装到盘根会让卸载的删除范围覆盖整个盘。");

        foreach (var systemRoot in SystemRoots())
        {
            if (string.IsNullOrEmpty(systemRoot)) continue;
            var candidate = systemRoot.TrimEnd('\\', '/');
            if (candidate.Length < 4) continue;
            if (string.Equals(trimmed, candidate, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(candidate + "\\", StringComparison.OrdinalIgnoreCase))
                throw new Exception("不能安装到系统目录：" + candidate + "。\n\n请换一个普通文件夹。");
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile) &&
            string.Equals(trimmed, profile.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            throw new Exception("不能直接装到用户主目录，请选择其下的具体文件夹。");
    }

    private static string[] SystemRoots()
    {
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
    }

    // 目标目录已存在时确认「是不是我们自己的」：有清单或具备本程序特征文件才算。
    private static void EnsureTargetIsUsable(string target)
    {
        if (!Directory.Exists(target)) return;
        if (File.Exists(Path.Combine(target, ManifestName))) return;
        if (Directory.GetFileSystemEntries(target).Length == 0) return;

        // 兼容 v1.1.1 及更早版本装出来的目录：认领后补写清单，后续卸载就能精确删除
        if (File.Exists(Path.Combine(target, LauncherName)) && File.Exists(Path.Combine(target, "node.exe")))
            return;

        throw new Exception(
            "这个文件夹里已经有其他文件，为避免误删，请换一个文件夹。\n\n位置：" + target +
            "\n\n安装程序会在你选择的文件夹里自动创建 " + AppFolderName + " 子目录。");
    }

    // ── 安装逻辑 ───────────────────────────────────────────────
    private static string Install(string rawTarget, IProgress<int> progress)
    {
        var target = ResolveTarget(rawTarget);
        EnsureTargetIsUsable(target);

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

            var previous = ReadManifest(target);
            var installed = new List<string>();

            Directory.CreateDirectory(target);
            CopyTree(staging, target, progress, installed);
            Report(progress, 85);

            // 升级：清掉旧清单里有、新版本不再提供的程序文件（仍严格限定在安装目录内）
            foreach (var relative in previous)
                if (!ContainsRelative(installed, relative))
                    TryDeleteInstalledFile(target, relative);

            WriteManifest(target, installed);

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
        return target;
    }

    private static bool ContainsRelative(List<string> files, string relative)
    {
        foreach (var item in files)
            if (string.Equals(item, relative, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // 卸载清单：一行一个 file=相对路径，卸载脚本只删这些文件
    private static void WriteManifest(string target, List<string> files)
    {
        var lines = new List<string>
        {
            "# CrushMonitor install manifest v1",
            "app=" + AppFolderName,
            "version=" + Version,
            "installed=" + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            "dir=" + target
        };
        foreach (var relative in files) lines.Add("file=" + relative);
        File.WriteAllLines(Path.Combine(target, ManifestName), lines.ToArray(), new UTF8Encoding(true));
    }

    private static List<string> ReadManifest(string target)
    {
        var result = new List<string>();
        var path = Path.Combine(target, ManifestName);
        if (!File.Exists(path)) return result;
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (!line.StartsWith("file=", StringComparison.Ordinal)) continue;
                var relative = line.Substring(5).Trim();
                if (relative.Length > 0) result.Add(relative);
            }
        }
        catch
        {
        }
        return result;
    }

    private static void TryDeleteInstalledFile(string target, string relative)
    {
        try
        {
            if (relative.IndexOf("..", StringComparison.Ordinal) >= 0) return;
            if (Path.IsPathRooted(relative)) return;
            var full = Path.GetFullPath(Path.Combine(target, relative));
            if (!full.StartsWith(target.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(full)) File.Delete(full);
        }
        catch
        {
        }
    }

    private static void Report(IProgress<int> progress, int value)
    {
        if (progress != null) progress.Report(value);
    }

    private static void CopyTree(string from, string to, IProgress<int> progress, List<string> installed)
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
            installed.Add(relative.Replace('/', '\\'));
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
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "crush-monitor (开源项目)");
                key.SetValue("InstallLocation", target);
                key.SetValue("DisplayIcon", Path.Combine(target, LauncherName));
                // 直接调卸载脚本，不经过 卸载.cmd：脚本自己会暂停，也方便它清理自身
                var uninstallScript = Path.Combine(target, "uninstall.ps1");
                var uninstallCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + uninstallScript + "\"";
                key.SetValue("UninstallString", uninstallCommand);
                key.SetValue("QuietUninstallString", uninstallCommand + " -NoPause");
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
                Text = "本机运行、不需要管理员权限。会在你选的文件夹里建 " + AppFolderName + " 子目录，卸载只删程序文件。",
                ForeColor = Color.FromArgb(0x66, 0x6B, 0x69),
                Location = new Point(26, 56),
                AutoSize = true
            };
            var pathLabel = new Label
            {
                Text = "安装到：",
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
            installButton.Enabled = false;
            browseButton.Enabled = false;
            pathBox.Enabled = false;
            var progress = new Progress<int>(value => bar.Value = Math.Max(0, Math.Min(100, value)));
            string target;
            try
            {
                statusLabel.Text = "正在安装…";
                target = Install(pathBox.Text, progress);
                installed = true;
                statusLabel.Text = "安装完成。";
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                installButton.Enabled = true;
                browseButton.Enabled = true;
                pathBox.Enabled = true;
                statusLabel.Text = "安装没有开始，可以换个位置再试。";
                return;
            }

            pathBox.Text = target;
            if (runCheck.Checked) Launch(target);
            MessageBox.Show(this,
                "安装完成！\n\n位置：" + target +
                "\n\n程序装在独立的 " + AppFolderName + " 文件夹里，卸载时只删除这里的程序文件，" +
                "你自己放进去的文件和聊天记录不会被删。" +
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
