using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Crush 好感监控器 · 桌面版宿主
// 自己的窗口（标题、图标、任务栏都是本程序的），内部用 WebView2 渲染界面；
// 后台静默启动本地服务，关窗口时把它一起收掉。全程没有浏览器窗口、没有黑窗口。
internal static class Launcher
{
    private static Process server;
    private static Form window;
    private static WebView2 view;

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var nodeExe = Path.Combine(dir, "node.exe");
        var serverScript = Path.Combine(dir, "app\\server.mjs");
        if (!File.Exists(nodeExe) || !File.Exists(serverScript))
        {
            Fail("程序文件不完整：缺少 node.exe 或 app\\server.mjs");
            return 1;
        }

        var port = FreePort(3178);
        try { File.WriteAllText(Path.Combine(dir, ".port"), port.ToString()); } catch { }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = nodeExe,
                Arguments = "\"" + serverScript + "\"",
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.EnvironmentVariables["PORT"] = port.ToString();
            server = Process.Start(info);
        }
        catch (Exception error)
        {
            Fail("启动本地服务失败：" + error.Message);
            return 1;
        }

        var url = "http://127.0.0.1:" + port + "/";
        if (!WaitForServer(port, 20000))
        {
            StopServer();
            Fail("本地服务启动超时。可能被安全软件拦截，或 3178-3278 端口都被占用。");
            return 1;
        }

        window = new Form
        {
            Text = "Crush 好感监控器",
            Width = 1200,
            Height = 880,
            MinimumSize = new Size(880, 620),
            StartPosition = FormStartPosition.CenterScreen,
            BackColor = Color.FromArgb(0xEE, 0xF0, 0xEF),
            Icon = LoadIcon()
        };
        var hint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(0x6C, 0x70, 0x72),
            Text = "正在载入界面…"
        };
        view = new WebView2 { Dock = DockStyle.Fill, Visible = false };
        window.Controls.Add(hint);
        window.Controls.Add(view);

        // WebView2 的初始化必须在消息循环跑起来之后异步做，否则会死锁（窗口永远不显示）。
        window.Shown += async delegate
        {
            try
            {
                // 用户数据（含聊天记录）放在程序目录里，绿色版可以整体搬走
                var environment = await CoreWebView2Environment.CreateAsync(
                    null,
                    Path.Combine(dir, "webview-data"));
                await view.EnsureCoreWebView2Async(environment);
                view.CoreWebView2.Settings.AreDevToolsEnabled = false;
                view.CoreWebView2.Settings.IsStatusBarEnabled = false;
                view.Source = new Uri(url);
                view.Visible = true;
                hint.Visible = false;
            }
            catch (Exception error)
            {
                // 没见过 WebView2 运行时的老机器：退回到系统浏览器，至少能用
                StopServer();
                MessageBox.Show(
                    "这台电脑缺少 WebView2 运行环境（Windows 10/11 通常自带）。\n" +
                    "现在改为用默认浏览器打开。\n\n详细信息：" + error.Message,
                    "Crush 好感监控器",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                Process.Start(url);
                window.Close();
            }
        };

        window.FormClosed += delegate { StopServer(); };
        Application.Run(window);
        StopServer();
        return 0;
    }

    private static Icon LoadIcon()
    {
        try
        {
            using (var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("app.ico"))
                if (stream != null) return new Icon(stream);
        }
        catch
        {
        }
        return SystemIcons.Application;
    }

    private static int FreePort(int start)
    {
        for (var port = start; port < start + 100; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch
            {
            }
        }
        return start;
    }

    private static bool WaitForServer(int port, int timeoutMs)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            if (server != null && server.HasExited) return false;
            try
            {
                var request = WebRequest.Create("http://127.0.0.1:" + port + "/api/health");
                request.Timeout = 1500;
                using (var response = request.GetResponse()) return true;
            }
            catch
            {
                Thread.Sleep(400);
            }
        }
        return false;
    }

    private static void StopServer()
    {
        try
        {
            if (server != null && !server.HasExited)
            {
                server.Kill();
                server.WaitForExit(3000);
            }
        }
        catch
        {
        }
    }

    private static void Fail(string message)
    {
        MessageBox.Show(message, "Crush 好感监控器", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
