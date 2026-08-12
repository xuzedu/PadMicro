using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PadMicro.UI;

internal sealed class MainForm : Form
{
    private const string AutoStartRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartRegistryValue = "PadMicro";
    private readonly bool autoStart;
    private readonly bool startHidden;
    private readonly ControllerMapControl map;
    private readonly Label status;
    private readonly Label keyTitle;
    private readonly Label function;
    private readonly Label shortcut;
    private readonly CheckBox autoStartCheckBox;
    private NotifyIcon? trayIcon;
    private ContextMenuStrip? trayMenu;
    private Icon? trayAppIcon;
    private WebView2? webView;
    private Process? bridge;
    private CancellationTokenSource? activationListenerStop;
    private Task? activationListener;
    private bool exitRequested;
    private bool trayHintShown;
    private bool updatingAutoStartControl;
    private int bridgeGeneration;
    private string currentStatusText = "正在准备";
    private string currentStatusTone = "warning";
    private string? currentNoticeTitle;
    private string? currentNoticeMessage;
    private string? currentNoticeAction;
    private string? lastTrayAlertKey;
    private string? currentControllerName;
    private string currentControllerFamily = "stadia";
    private string currentProfileFile = "controller-padmicro-profile.stadia.json";
    private bool controllerInputReady;
    private DateTime? lastControllerInputAt;
    private string? lastControllerInputKey;
    private string? lastBlockedForeground;

    public MainForm(bool autoStart, bool startHidden)
    {
        this.autoStart = autoStart;
        this.startHidden = startHidden;
        Text = "PadMicro 控制台";
        if (startHidden) ShowInTaskbar = false;
        ClientSize = new Size(1440, 900);
        MinimumSize = new Size(1180, 760);
        BackColor = Color.FromArgb(8, 12, 21);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 10f);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        HandleCreated += (_, _) => EnableDarkTitleBar(Handle);
        var webEntryAvailable = File.Exists(Path.Combine(AppContext.BaseDirectory, "Web", "index.html"));
        if (webEntryAvailable) Opacity = 0;

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 100,
            BackColor = Color.FromArgb(8, 12, 21),
            Padding = new Padding(30, 20, 30, 10)
        };
        var title = new Label
        {
            Text = "PadMicro",
            Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold),
            ForeColor = Color.FromArgb(242, 246, 255),
            AutoSize = true,
            Location = new Point(28, 20)
        };
        var subtitle = new Label
        {
            Text = "Codex 手柄控制台  /  实时按键映射",
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ForeColor = Color.FromArgb(119, 139, 170),
            AutoSize = true,
            Location = new Point(31, 61)
        };
        status = new Label
        {
            Text = "●  正在准备",
            ForeColor = Color.FromArgb(255, 194, 89),
            BackColor = Color.FromArgb(19, 27, 41),
            AutoSize = false,
            Size = new Size(164, 36),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(1026, 28)
        };
        status.Region = new Region(RoundedPath(new RectangleF(0, 0, status.Width, status.Height), 18));
        header.Resize += (_, _) => status.Left = header.ClientSize.Width - status.Width - 30;
        header.Controls.AddRange([title, subtitle, status]);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 82,
            BackColor = Color.FromArgb(8, 12, 21),
            Padding = new Padding(30, 14, 30, 20)
        };
        var deviceHint = new Label
        {
            Text = "SDL / HID  ·  Stadia · Xbox · Switch",
            ForeColor = Color.FromArgb(91, 109, 139),
            Font = new Font("Microsoft YaHei UI", 9f),
            AutoSize = true,
            Location = new Point(31, 29)
        };
        var start = MakeButton("启动桥接", Color.FromArgb(35, 207, 180), Color.FromArgb(48, 225, 197));
        var stop = MakeButton("停止", Color.FromArgb(28, 38, 55), Color.FromArgb(39, 51, 72));
        var export = MakeButton("导出映射图", Color.FromArgb(81, 91, 210), Color.FromArgb(99, 111, 232));
        autoStartCheckBox = new CheckBox
        {
            Text = "开机自启",
            Checked = IsAutoStartEnabled(),
            AutoSize = true,
            ForeColor = Color.FromArgb(154, 171, 198),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(225, 28)
        };
        start.Size = new Size(126, 44);
        stop.Size = new Size(96, 44);
        export.Size = new Size(138, 44);
        start.Click += (_, _) => StartBridge();
        stop.Click += (_, _) => StopBridge();
        export.Click += (_, _) => ExportFromDialog();
        autoStartCheckBox.CheckedChanged += (_, _) =>
        {
            if (!updatingAutoStartControl) SaveAutoStart(autoStartCheckBox.Checked);
        };
        void PositionFooterButtons()
        {
            export.Location = new Point(footer.ClientSize.Width - export.Width - 30, 14);
            stop.Location = new Point(export.Left - stop.Width - 10, 14);
            start.Location = new Point(stop.Left - start.Width - 10, 14);
        }
        footer.Resize += (_, _) => PositionFooterButtons();
        footer.Controls.AddRange([deviceHint, autoStartCheckBox, start, stop, export]);
        PositionFooterButtons();

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(8, 12, 21),
            Padding = new Padding(28, 8, 28, 8)
        };
        var details = new CardPanel
        {
            Dock = DockStyle.Right,
            Width = 370,
            BackColor = Color.FromArgb(13, 20, 33),
            BorderColor = Color.FromArgb(31, 45, 67),
            Radius = 22
        };
        var spacer = new Panel { Dock = DockStyle.Right, Width = 18, BackColor = Color.Transparent };
        map = new ControllerMapControl(LoadMappings()) { Dock = DockStyle.Fill };
        map.HoveredChanged += ShowMapping;

        var eyebrow = new Label
        {
            Text = "CURRENT MAPPING",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(58, 220, 196),
            AutoSize = true,
            Location = new Point(26, 30)
        };
        keyTitle = new Label
        {
            Text = "选择一个按键",
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(239, 244, 255),
            AutoSize = false,
            Location = new Point(24, 61),
            Size = new Size(318, 42)
        };
        function = new Label
        {
            Text = "将鼠标移到手柄按键上查看功能",
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ForeColor = Color.FromArgb(154, 171, 198),
            AutoSize = false,
            Location = new Point(26, 112),
            Size = new Size(318, 60)
        };
        var shortcutCaption = new Label
        {
            Text = "触发操作",
            ForeColor = Color.FromArgb(91, 110, 140),
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(26, 180)
        };
        shortcut = new Label
        {
            Text = "等待选择",
            Font = new Font("Consolas", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 224, 201),
            BackColor = Color.FromArgb(18, 35, 48),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 12, 0),
            AutoSize = false,
            Location = new Point(25, 207),
            Size = new Size(320, 44)
        };
        shortcut.Region = new Region(RoundedPath(new RectangleF(0, 0, shortcut.Width, shortcut.Height), 11));
        var divider = new Panel
        {
            BackColor = Color.FromArgb(31, 44, 64),
            Location = new Point(26, 280),
            Size = new Size(318, 1)
        };
        var interactionTitle = new Label
        {
            Text = "交互说明",
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(225, 233, 247),
            AutoSize = true,
            Location = new Point(26, 306)
        };
        var interaction = new Label
        {
            Text = "悬停即可高亮按键。摇杆与十字键的四个方向可分别识别。",
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ForeColor = Color.FromArgb(126, 145, 175),
            AutoSize = false,
            Location = new Point(26, 337),
            Size = new Size(318, 58)
        };
        var configTitle = new Label
        {
            Text = "配置文件",
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(91, 110, 140),
            AutoSize = true,
            Location = new Point(26, 421)
        };
        var config = new Label
        {
            Text = "controller-padmicro-\nprofile.json  ·  27 个交互热区",
            Font = new Font("Consolas", 9.5f),
            ForeColor = Color.FromArgb(162, 180, 207),
            AutoSize = false,
            Location = new Point(26, 449),
            Size = new Size(318, 72)
        };
        details.Controls.AddRange([eyebrow, keyTitle, function, shortcutCaption, shortcut, divider,
            interactionTitle, interaction, configTitle, config]);
        content.Controls.Add(map);
        content.Controls.Add(spacer);
        content.Controls.Add(details);

        Controls.Add(content);
        Controls.Add(footer);
        Controls.Add(header);
        if (autoStart) InitializeTray();
        Shown += async (_, _) =>
        {
            if (this.startHidden) Hide();
            if (this.autoStart) StartBridge();
            try
            {
                await InitializeWebInterfaceAsync();
            }
            finally
            {
                Opacity = 1;
            }
        };
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && trayIcon is not null) HideToTray();
        };
        FormClosing += OnFormClosing;
    }

    private static ModernButton MakeButton(string text, Color color, Color hover) => new(text, color, hover);

    private void ShowMapping(MapItem? item)
    {
        if (item is null)
        {
            keyTitle.Text = "选择一个按键";
            function.Text = "将鼠标移到手柄按键上查看功能";
            shortcut.Text = "等待选择";
            return;
        }
        keyTitle.Text = item.Display;
        function.Text = item.Function;
        shortcut.Text = string.IsNullOrWhiteSpace(item.Shortcut) ? "未设置快捷键" : item.Shortcut;
    }

    private void StartBridge()
    {
        if (bridge is { HasExited: false }) return;
        var existing = Process.GetProcessesByName("PadMicro").FirstOrDefault();
        if (existing is not null)
        {
            var generation = ++bridgeGeneration;
            TrackBridge(existing, generation, null);
            SetStatus("● 桥接已在运行", Color.FromArgb(77, 220, 175), "PadMicro - 运行中");
            return;
        }
        var executable = Path.Combine(AppContext.BaseDirectory, "PadMicro.exe");
        var profile = Path.Combine(AppContext.BaseDirectory, "controller-padmicro-profile.json");
        if (!File.Exists(executable) || !File.Exists(profile))
        {
            SetStatus("● 缺少桥接程序或配置", Color.FromArgb(255, 104, 120), "PadMicro - 配置缺失");
            return;
        }
        try
        {
            currentControllerName = null;
            controllerInputReady = false;
            lastControllerInputAt = null;
            lastControllerInputKey = null;
            lastBlockedForeground = null;
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                WorkingDirectory = AppContext.BaseDirectory
            };
            start.ArgumentList.Add(profile);
            var process = Process.Start(start);
            if (process is null) throw new InvalidOperationException("进程未启动");
            var generation = ++bridgeGeneration;
            var standardError = process.StandardError.ReadToEndAsync();
            _ = ReadBridgeOutputAsync(process, generation);
            SetStatus("● 正在检测手柄", Color.FromArgb(113, 167, 255), "PadMicro - 正在检测手柄");
            TrackBridge(process, generation, standardError);
            _ = ConfirmBridgeStartedAsync(process, generation);
        }
        catch (Exception ex)
        {
            SetStatus("● 启动失败：" + ex.Message, Color.FromArgb(255, 104, 120), "PadMicro - 启动失败");
        }
    }

    private void StopBridge()
    {
        ++bridgeGeneration;
        foreach (var process in Process.GetProcessesByName("PadMicro"))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch { }
            finally { process.Dispose(); }
        }
        try { bridge?.Dispose(); } catch { }
        bridge = null;
        currentControllerName = null;
        controllerInputReady = false;
        lastControllerInputAt = null;
        lastControllerInputKey = null;
        lastBlockedForeground = null;
        SetStatus("● 桥接已停止", Color.FromArgb(255, 194, 89), "PadMicro - 已停止");
    }

    private void ExportFromDialog()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG 图片|*.png",
            FileName = "PadMicro-Technical-Keymap.png",
            InitialDirectory = AppContext.BaseDirectory
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ExportImage(dialog.FileName);
        SetStatus("● 映射图已导出", Color.FromArgb(113, 167, 255), "PadMicro - 映射图已导出");
    }

    public void ExportImage(string path)
    {
        map.ExportPoster(path);
    }

    private void InitializeTray()
    {
        trayAppIcon = CreateTrayIcon();
        Icon = trayAppIcon;
        trayMenu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(18, 25, 38),
            ForeColor = Color.FromArgb(233, 239, 249),
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ShowImageMargin = false,
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable())
        };
        var openItem = new ToolStripMenuItem("打开控制台") { Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold) };
        var startItem = new ToolStripMenuItem("启动桥接服务");
        var stopItem = new ToolStripMenuItem("停止桥接服务");
        var exitItem = new ToolStripMenuItem("退出并停止服务");
        openItem.Click += (_, _) => RestoreFromTray();
        startItem.Click += (_, _) => StartBridge();
        stopItem.Click += (_, _) => StopBridge();
        exitItem.Click += (_, _) => ExitApplication();
        trayMenu.Items.AddRange([openItem, new ToolStripSeparator(), startItem, stopItem,
            new ToolStripSeparator(), exitItem]);
        trayMenu.Opening += (_, _) =>
        {
            var running = IsBridgeRunning();
            startItem.Enabled = !running;
            stopItem.Enabled = running;
        };
        trayIcon = new NotifyIcon
        {
            Icon = trayAppIcon,
            Text = "PadMicro - 正在准备",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        StartActivationListener();
    }

    private void StartActivationListener()
    {
        activationListenerStop = new CancellationTokenSource();
        var token = activationListenerStop.Token;
        activationListener = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        Program.ActivationPipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    var command = await reader.ReadLineAsync(token);
                    if (!string.Equals(command, "SHOW", StringComparison.OrdinalIgnoreCase)
                        || IsDisposed || Disposing || !IsHandleCreated) continue;
                    BeginInvoke(RestoreFromTray);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException)
                {
                    try { await Task.Delay(120, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }, token);
    }

    private void TrackBridge(Process process, int generation, Task<string>? standardError)
    {
        try { bridge?.Dispose(); } catch { }
        bridge = process;
        bridge.EnableRaisingEvents = true;
        bridge.Exited += async (_, _) =>
        {
            var exitCode = -1;
            try { exitCode = process.ExitCode; } catch { }
            var error = "";
            if (standardError is not null)
            {
                try { error = await standardError; } catch { }
            }
            if (IsDisposed || Disposing || generation != bridgeGeneration)
            {
                process.Dispose();
                return;
            }
            try
            {
                BeginInvoke(() => HandleBridgeExited(process, generation, exitCode, error));
            }
            catch { }
        };
    }

    private async Task ConfirmBridgeStartedAsync(Process process, int generation)
    {
        await Task.Delay(500);
        if (IsDisposed || Disposing || generation != bridgeGeneration) return;
        try
        {
            if (process.HasExited) return;
            BeginInvoke(() =>
            {
                if (generation != bridgeGeneration || process.HasExited) return;
                if (controllerInputReady)
                    SetStatus($"● {currentControllerName ?? "手柄"} 已连接", Color.FromArgb(77, 220, 175), "PadMicro - 运行中");
            });
        }
        catch { }
    }

    private async Task ReadBridgeOutputAsync(Process process, int generation)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (IsDisposed || Disposing || generation != bridgeGeneration) return;
                try { BeginInvoke(() => HandleBridgeOutput(line, generation)); }
                catch { return; }
            }
        }
        catch { }
    }

    private void HandleBridgeOutput(string line, int generation)
    {
        if (generation != bridgeGeneration) return;
        const string devicePrefix = "PADMICRO_DEVICE|";
        const string familyPrefix = "PADMICRO_FAMILY|";
        const string profilePrefix = "PADMICRO_PROFILE|";
        const string inputPrefix = "PADMICRO_INPUT|";
        const string blockedPrefix = "PADMICRO_BLOCKED|";

        if (line.StartsWith(devicePrefix, StringComparison.Ordinal))
        {
            currentControllerName = line[devicePrefix.Length..].Trim();
            SendWebState();
            return;
        }
        if (line.StartsWith(familyPrefix, StringComparison.Ordinal))
        {
            currentControllerFamily = line[familyPrefix.Length..].Trim().ToLowerInvariant();
            SendWebState();
            return;
        }
        if (line.StartsWith(profilePrefix, StringComparison.Ordinal))
        {
            currentProfileFile = Path.GetFileName(line[profilePrefix.Length..].Trim());
            map.ReloadMappings(LoadMappings());
            SendWebState();
            return;
        }
        if (line.Equals("PADMICRO_READY", StringComparison.Ordinal))
        {
            controllerInputReady = true;
            SetStatus($"● {currentControllerName ?? "手柄"} 已连接", Color.FromArgb(77, 220, 175), "PadMicro - 运行中");
            return;
        }
        if (line.StartsWith(inputPrefix, StringComparison.Ordinal))
        {
            lastControllerInputAt = DateTime.Now;
            lastControllerInputKey = line[inputPrefix.Length..].Trim();
            lastBlockedForeground = null;
            SendWebState();
            return;
        }
        if (line.StartsWith(blockedPrefix, StringComparison.Ordinal))
        {
            lastBlockedForeground = line[blockedPrefix.Length..].Trim();
            SetStatus("● 已收到手柄输入，但 Codex 不在前台", Color.FromArgb(255, 194, 89),
                "PadMicro - 输入已被暂停",
                "手柄输入已识别",
                $"当前前台程序是“{lastBlockedForeground}”。请先切换到 Codex，再操作手柄。",
                null);
        }
    }

    private void HandleBridgeExited(Process process, int generation, int exitCode, string error)
    {
        if (generation != bridgeGeneration) return;
        if (ReferenceEquals(bridge, process)) bridge = null;
        process.Dispose();

        if (exitCode == 4 || error.Contains("没有找到兼容的游戏手柄", StringComparison.Ordinal))
        {
            SetStatus("● 等待手柄连接", Color.FromArgb(255, 194, 89), "PadMicro - 未检测到手柄",
                "未检测到手柄",
                "请打开手柄，并在 Windows 蓝牙设置中确认它显示为“已连接”，然后重新检测。支持 Stadia、Xbox 和 Switch 手柄。",
                "start", notifyWhenHidden: true);
            return;
        }

        if (error.Contains("手柄连接已断开", StringComparison.Ordinal))
        {
            SetStatus("● 手柄连接已断开", Color.FromArgb(255, 194, 89), "PadMicro - 手柄已断开",
                "手柄连接已断开",
                "请检查手柄电量和蓝牙连接。重新连接成功后，点击“重新检测”即可恢复桥接。",
                "start", notifyWhenHidden: true);
            return;
        }

        if (exitCode == 2)
        {
            SetStatus("● 缺少 SDL2 运行库", Color.FromArgb(255, 104, 120), "PadMicro - 运行库缺失",
                "桥接组件不完整",
                "没有找到 SDL2 运行库。请重新安装 PadMicro；若使用便携版，请确认 SDL2.dll 与 PadMicro.exe 位于同一目录。",
                null, notifyWhenHidden: true);
            return;
        }

        if (exitCode == 3)
        {
            SetStatus("● 手柄输入初始化失败", Color.FromArgb(255, 104, 120), "PadMicro - 初始化失败",
                "无法初始化手柄输入",
                string.IsNullOrWhiteSpace(error) ? "Windows 手柄输入服务初始化失败，请重启 PadMicro 或 Windows 后再试。" : error.Trim(),
                "start", notifyWhenHidden: true);
            return;
        }

        SetStatus("● 桥接意外停止", Color.FromArgb(255, 104, 120), "PadMicro - 桥接异常停止",
            "桥接服务意外停止",
            string.IsNullOrWhiteSpace(error) ? $"后台桥接进程已退出（代码 {exitCode}）。可点击重新检测再次启动。" : error.Trim(),
            "start", notifyWhenHidden: true);
    }

    private void SetStatus(string text, Color color, string trayText, string? noticeTitle = null,
        string? noticeMessage = null, string? noticeAction = null, bool notifyWhenHidden = false)
    {
        status.Text = text;
        status.ForeColor = color;
        currentStatusText = text;
        currentStatusTone = StatusTone(color);
        currentNoticeTitle = noticeTitle;
        currentNoticeMessage = noticeMessage;
        currentNoticeAction = noticeAction;
        if (trayIcon is not null) trayIcon.Text = trayText.Length > 63 ? trayText[..63] : trayText;
        if (noticeTitle is null)
        {
            lastTrayAlertKey = null;
        }
        else if (notifyWhenHidden && trayIcon is not null && (!Visible || !ShowInTaskbar)
                 && !string.Equals(lastTrayAlertKey, noticeTitle, StringComparison.Ordinal))
        {
            lastTrayAlertKey = noticeTitle;
            trayIcon.ShowBalloonTip(4500, noticeTitle, noticeMessage ?? "请打开 PadMicro 查看详情。",
                currentStatusTone == "danger" ? ToolTipIcon.Error : ToolTipIcon.Warning);
        }
        SendWebState();
    }

    private async Task InitializeWebInterfaceAsync()
    {
        var webRoot = Path.Combine(AppContext.BaseDirectory, "Web");
        var entry = Path.Combine(webRoot, "index.html");
        if (!Directory.Exists(webRoot) || !File.Exists(entry)) return;

        var view = new WebView2
        {
            Dock = DockStyle.None,
            Bounds = ClientRectangle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Visible = false,
            DefaultBackgroundColor = Color.FromArgb(7, 11, 19)
        };
        Controls.Add(view);
        view.BringToFront();
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PadMicro",
                "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            view.CoreWebView2.Settings.AreDevToolsEnabled = false;
            view.CoreWebView2.Settings.IsStatusBarEnabled = false;
            view.CoreWebView2.Settings.IsZoomControlEnabled = false;
            view.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            view.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            view.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "stadiabridge.local",
                AppContext.BaseDirectory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            view.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            var navigationReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            view.NavigationCompleted += (_, e) =>
            {
                SendWebState();
                navigationReady.TrySetResult(e.IsSuccess);
            };
            webView = view;
            SetNativeInterfaceVisible(false);
            view.Bounds = ClientRectangle;
            view.BringToFront();
            view.Source = new Uri("https://stadiabridge.local/Web/index.html");
            view.Visible = true;
            if (!await navigationReady.Task.WaitAsync(TimeSpan.FromSeconds(8)))
                throw new InvalidOperationException("Web 界面导航失败。");
        }
        catch (Exception ex)
        {
            Controls.Remove(view);
            view.Dispose();
            webView = null;
            SetNativeInterfaceVisible(true);
            SetStatus("● Web 界面不可用，已使用原生界面", Color.FromArgb(255, 194, 89),
                "PadMicro - 原生界面");
            Debug.WriteLine(ex);
        }
    }

    private void SetNativeInterfaceVisible(bool visible)
    {
        foreach (Control control in Controls)
        {
            if (ReferenceEquals(control, webView)) continue;
            control.Visible = visible;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            if (string.Equals(type, "ready", StringComparison.OrdinalIgnoreCase))
            {
                SendWebState();
                return;
            }
            if (!string.Equals(type, "command", StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("command", out var commandValue)) return;

            var hasValue = root.TryGetProperty("value", out var valueElement);
            var value = hasValue && valueElement.ValueKind == JsonValueKind.String
                ? valueElement.GetString()
                : null;
            switch (commandValue.GetString()?.ToLowerInvariant())
            {
                case "start": StartBridge(); break;
                case "stop": StopBridge(); break;
                case "export": ExportFromDialog(); break;
                case "tray": HideToTray(); break;
                case "set-assistant-mode": SaveAssistantMode(value); break;
                case "set-mapping-shortcut":
                    if (hasValue && valueElement.ValueKind == JsonValueKind.Object)
                    {
                        var profileKey = valueElement.TryGetProperty("profileKey", out var keyElement)
                            ? keyElement.GetString()
                            : null;
                        var shortcut = valueElement.TryGetProperty("shortcut", out var shortcutElement)
                            ? shortcutElement.GetString()
                            : null;
                        SaveMappingShortcut(profileKey, shortcut);
                    }
                    break;
                case "set-mapping-enabled":
                    if (hasValue && valueElement.ValueKind == JsonValueKind.Object)
                    {
                        var profileKey = valueElement.TryGetProperty("profileKey", out var keyElement)
                            ? keyElement.GetString()
                            : null;
                        var enabled = valueElement.TryGetProperty("enabled", out var enabledElement)
                                      && enabledElement.ValueKind == JsonValueKind.True;
                        SaveMappingEnabled(profileKey, enabled);
                    }
                    break;
                case "set-auto-start": SaveAutoStart(string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)); break;
            }
        }
        catch (JsonException) { }
    }

    private void SendWebState()
    {
        if (webView?.CoreWebView2 is null) return;
        var loadedMappings = LoadMappings();
        map.ReloadMappings(loadedMappings);
        var mappings = loadedMappings.ToDictionary(
            item => item.Key,
            item => new
            {
                Display = ControllerDisplayName(item.Key),
                Function = item.Value.Function,
                Shortcut = string.IsNullOrWhiteSpace(item.Value.Shortcut) ? "未设置快捷键" : item.Value.Shortcut,
                item.Value.Action,
                item.Value.DefaultShortcut,
                item.Value.CustomShortcut,
                item.Value.Enabled
            },
            StringComparer.OrdinalIgnoreCase);
        var payload = new
        {
            Type = "state",
            Status = currentStatusText,
            Tone = currentStatusTone,
            Running = IsBridgeRunning(),
            Controller = new
            {
                Name = currentControllerName,
                Family = currentControllerFamily,
                Profile = currentProfileFile,
                Connected = controllerInputReady,
                LastInputAt = lastControllerInputAt,
                LastInputKey = lastControllerInputKey,
                BlockedForeground = lastBlockedForeground
            },
            Notice = new
            {
                Visible = !string.IsNullOrWhiteSpace(currentNoticeTitle),
                Title = currentNoticeTitle,
                Message = currentNoticeMessage,
                Action = currentNoticeAction
            },
            Mappings = mappings,
            Settings = ReadProfileSettings()
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        try { webView.CoreWebView2.PostWebMessageAsJson(json); }
        catch (InvalidOperationException) { }
    }

    private string ControllerDisplayName(string key) => key switch
    {
        "South" => "A",
        "East" => "B",
        "West" => "X",
        "North" => "Y",
        "View" => currentControllerFamily == "xbox" ? "View" : "View（三点）",
        "Menu" => currentControllerFamily == "xbox" ? "Menu" : "Menu（三横）",
        "Guide" => currentControllerFamily == "xbox" ? "Xbox 键" : "Stadia 键",
        "Misc1" => "Share",
        "StadiaAssistant" => "Assistant（四点）",
        "StadiaCapture" => "Capture（取景框）",
        "LeftTrigger" => "L2",
        "RightTrigger" => "R2",
        "LeftShoulder" => "L1",
        "RightShoulder" => "R1",
        "LeftStick" => "L3",
        "RightStick" => "R3",
        "DpadUp" => "十字键 ↑",
        "DpadDown" => "十字键 ↓",
        "DpadLeft" => "十字键 ←",
        "DpadRight" => "十字键 →",
        "LeftStickUp" => "左摇杆 ↑",
        "LeftStickDown" => "左摇杆 ↓",
        "LeftStickLeft" => "左摇杆 ←",
        "LeftStickRight" => "左摇杆 →",
        "RightStickUp" => "右摇杆 ↑",
        "RightStickDown" => "右摇杆 ↓",
        "RightStickLeft" => "右摇杆 ←",
        "RightStickRight" => "右摇杆 →",
        _ => key
    };

    private void SaveAssistantMode(string? mode)
    {
        var action = mode?.ToLowerInvariant() switch
        {
            "typeless" => "TypelessToggle",
            "codex" => "CodexDictation",
            _ => null
        };
        if (action is null) return;
        UpdateProfile(root =>
        {
            var buttons = root["buttons"] as JsonObject ?? new JsonObject();
            root["buttons"] = buttons;
            buttons["North"] = action;
        });
    }

    private void SaveMappingShortcut(string? profileKey, string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(profileKey))
        {
            SendShortcutResult(profileKey, false, "没有选中可编辑的按键。");
            return;
        }
        var normalized = shortcut?.Trim() ?? "";
        if (!IsSupportedShortcut(normalized))
        {
            SetStatus("● 快捷键格式无效", Color.FromArgb(255, 104, 120),
                "PadMicro - 快捷键格式无效");
            SendShortcutResult(profileKey, false, "格式无效。示例：Ctrl+Shift+P、Alt+F4、RightAlt。");
            return;
        }

        var saved = UpdateProfile(root =>
        {
            var action = FindMappedAction(root, profileKey)
                         ?? throw new InvalidOperationException($"配置中没有找到键位 {profileKey}。");
            var shortcuts = root["customShortcuts"] as JsonObject ?? new JsonObject();
            root["customShortcuts"] = shortcuts;
            if (string.IsNullOrWhiteSpace(normalized)) shortcuts.Remove(profileKey);
            else shortcuts[profileKey] = normalized;

            // Migrate the former R2 action-level setting to the per-control format.
            if (profileKey.Equals("RightTrigger", StringComparison.OrdinalIgnoreCase)
                && shortcuts.ContainsKey(action))
                shortcuts.Remove(action);
        });
        SendShortcutResult(profileKey, saved,
            saved
                ? string.IsNullOrWhiteSpace(normalized) ? "已恢复默认触发操作。" : $"已保存：{normalized}"
                : "保存失败，请查看顶部状态提示。");
    }

    private static string? FindMappedAction(JsonObject root, string profileKey)
    {
        foreach (var groupName in new[] { "buttons", "gestures" })
            if (root[groupName] is JsonObject group
                && group.TryGetPropertyValue(profileKey, out var value)
                && value is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var action))
                return action;
        return null;
    }

    private void SaveMappingEnabled(string? profileKey, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(profileKey)) return;
        UpdateProfile(root =>
        {
            _ = FindMappedAction(root, profileKey)
                ?? throw new InvalidOperationException($"配置中没有找到键位 {profileKey}。");
            var disabled = root["disabledBindings"] as JsonArray ?? new JsonArray();
            root["disabledBindings"] = disabled;
            for (var index = disabled.Count - 1; index >= 0; index--)
            {
                if (disabled[index] is JsonValue value
                    && value.TryGetValue<string>(out var key)
                    && key.Equals(profileKey, StringComparison.OrdinalIgnoreCase))
                    disabled.RemoveAt(index);
            }
            if (!enabled) disabled.Add(profileKey);
        });
    }

    private void SendShortcutResult(string? profileKey, bool success, string message)
    {
        if (webView?.CoreWebView2 is null) return;
        var json = JsonSerializer.Serialize(new
        {
            Type = "shortcut-result",
            ProfileKey = profileKey,
            Success = success,
            Message = message
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        try { webView.CoreWebView2.PostWebMessageAsJson(json); }
        catch (InvalidOperationException) { }
    }

    private void SaveAutoStart(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using var key = Registry.CurrentUser.CreateSubKey(AutoStartRegistryPath, writable: true)
                    ?? throw new InvalidOperationException("无法打开当前用户的启动项注册表。");
                key.SetValue(AutoStartRegistryValue, $"\"{Application.ExecutablePath}\" --background",
                    RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryPath, writable: true);
                key?.DeleteValue(AutoStartRegistryValue, throwOnMissingValue: false);
            }

            updatingAutoStartControl = true;
            autoStartCheckBox.Checked = enabled;
            updatingAutoStartControl = false;
            SendWebState();
        }
        catch (Exception ex)
        {
            updatingAutoStartControl = true;
            autoStartCheckBox.Checked = IsAutoStartEnabled();
            updatingAutoStartControl = false;
            SetStatus("● 修改开机自启失败：" + ex.Message, Color.FromArgb(255, 104, 120),
                "PadMicro - 设置失败");
        }
    }

    private bool UpdateProfile(Action<JsonObject> update)
    {
        try
        {
            var path = GetCurrentProfilePath();
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            update(root);
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            map.ReloadMappings(LoadMappings());
            var restart = IsBridgeRunning();
            if (restart)
            {
                StopBridge();
                StartBridge();
            }
            else
            {
                SetStatus("● 配置已保存，桥接未启动", Color.FromArgb(255, 194, 89),
                    "PadMicro - 配置已保存");
            }
            SendWebState();
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("● 保存配置失败：" + ex.Message, Color.FromArgb(255, 104, 120),
                "PadMicro - 保存配置失败");
            return false;
        }
    }

    private object ReadProfileSettings()
    {
        var assistantMode = "typeless";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(
                GetCurrentProfilePath()));
            var root = document.RootElement;
            if (root.TryGetProperty("buttons", out var buttons)
                && buttons.TryGetProperty("North", out var assistantAction)
                && string.Equals(assistantAction.GetString(), "CodexDictation", StringComparison.OrdinalIgnoreCase))
                assistantMode = "codex";
        }
        catch { }
        return new
        {
            AssistantMode = assistantMode,
            AutoStartEnabled = IsAutoStartEnabled()
        };
    }

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryPath);
            return key?.GetValue(AutoStartRegistryValue) is string command
                   && !string.IsNullOrWhiteSpace(command);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSupportedShortcut(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return true;
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 4) return false;
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasPrimary = false;
        foreach (var part in parts)
        {
            if (!unique.Add(part)) return false;
            var token = part.ToUpperInvariant();
            if (token is "RIGHTALT" or "RALT")
            {
                if (parts.Length == 1) hasPrimary = true;
                continue;
            }
            if (token is "CTRL" or "CONTROL" or "SHIFT" or "ALT") continue;
            var valid = token.Length == 1 && char.IsLetterOrDigit(token[0]);
            valid |= token.StartsWith('F') && int.TryParse(token[1..], out var functionKey)
                     && functionKey is >= 1 and <= 24;
            valid |= token is "TAB" or "ENTER" or "ESC" or "ESCAPE" or "SPACE" or "BACKSPACE"
                or "LEFT" or "UP" or "RIGHT" or "DOWN" or "`" or "BACKTICK"
                or "-" or "MINUS" or "=" or "EQUAL" or "," or "COMMA"
                or "." or "PERIOD" or "/" or "SLASH";
            if (!valid) return false;
            hasPrimary = true;
        }
        return hasPrimary;
    }

    private static string StatusTone(Color color)
    {
        if (color.G > 190 && color.R < 120) return "success";
        if (color.R > 220 && color.G < 150) return "danger";
        if (color.B > 200 && color.R < 180) return "info";
        return "warning";
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        if (trayHintShown || trayIcon is null) return;
        trayHintShown = true;
        trayIcon.ShowBalloonTip(1800, "PadMicro 仍在运行",
            "双击托盘图标可恢复窗口，右键可停止或退出服务。", ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        exitRequested = true;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!exitRequested && trayIcon is not null && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (autoStart) StopBridge();
        activationListenerStop?.Cancel();
        try { activationListener?.Wait(500); } catch { }
        activationListenerStop?.Dispose();
        webView?.Dispose();
        if (trayIcon is not null) trayIcon.Visible = false;
        trayIcon?.Dispose();
        trayMenu?.Dispose();
        trayAppIcon?.Dispose();
    }

    private static Icon CreateTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "PadMicro.ico");
        if (File.Exists(iconPath))
        {
            using var icon = new Icon(iconPath, new Size(32, 32));
            return (Icon)icon.Clone();
        }

        using var associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        return associated is null ? (Icon)SystemIcons.Application.Clone() : (Icon)associated.Clone();
    }

    private static bool IsBridgeRunning()
    {
        var processes = Process.GetProcessesByName("PadMicro");
        var running = processes.Any(process =>
        {
            try { return !process.HasExited; }
            catch { return false; }
        });
        foreach (var process in processes) process.Dispose();
        return running;
    }

    private static void EnableDarkTitleBar(IntPtr handle)
    {
        if (!OperatingSystem.IsWindows()) return;
        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    private static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private string GetCurrentProfilePath()
    {
        var selected = Path.Combine(AppContext.BaseDirectory, currentProfileFile);
        return File.Exists(selected)
            ? selected
            : Path.Combine(AppContext.BaseDirectory, "controller-padmicro-profile.json");
    }

    private Dictionary<string, MappingInfo> LoadMappings()
    {
        var actions = ActionCatalog.All;
        var result = new Dictionary<string, MappingInfo>(StringComparer.OrdinalIgnoreCase);
        var profilePath = GetCurrentProfilePath();
        if (!File.Exists(profilePath)) return result;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(profilePath));
            var shortcutOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var disabledBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (document.RootElement.TryGetProperty("customShortcuts", out var customShortcuts))
                foreach (var property in customShortcuts.EnumerateObject())
                    shortcutOverrides[property.Name] = property.Value.GetString() ?? "";
            if (document.RootElement.TryGetProperty("disabledBindings", out var disabled)
                && disabled.ValueKind == JsonValueKind.Array)
                foreach (var item in disabled.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } key)
                        disabledBindings.Add(key);
            foreach (var group in new[] { "buttons", "gestures" })
            {
                if (!document.RootElement.TryGetProperty(group, out var mappings)) continue;
                foreach (var property in mappings.EnumerateObject())
                {
                    var action = property.Value.GetString() ?? "";
                    var info = actions.GetValueOrDefault(action, new MappingInfo(action, ""));
                    var defaultShortcut = info.Shortcut;
                    string? customShortcut = null;
                    if (!shortcutOverrides.TryGetValue(property.Name, out customShortcut)
                        && !shortcutOverrides.TryGetValue(action, out customShortcut))
                        customShortcut = null;
                    if (!string.IsNullOrWhiteSpace(customShortcut))
                        info = info with { Shortcut = customShortcut };
                    result[property.Name] = info with
                    {
                        Function = disabledBindings.Contains(property.Name) ? "未绑定" : info.Function,
                        Shortcut = disabledBindings.Contains(property.Name) ? "不发送命令" : info.Shortcut,
                        Action = action,
                        DefaultShortcut = defaultShortcut,
                        CustomShortcut = customShortcut,
                        Enabled = !disabledBindings.Contains(property.Name)
                    };
                }
            }
        }
        catch { }
        return result;
    }
}

internal sealed class DarkMenuColorTable : ProfessionalColorTable
{
    private static readonly Color Surface = Color.FromArgb(18, 25, 38);
    private static readonly Color Hover = Color.FromArgb(31, 45, 64);
    private static readonly Color Border = Color.FromArgb(42, 58, 80);

    public override Color ToolStripDropDownBackground => Surface;
    public override Color ImageMarginGradientBegin => Surface;
    public override Color ImageMarginGradientMiddle => Surface;
    public override Color ImageMarginGradientEnd => Surface;
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemBorder => Border;
    public override Color MenuBorder => Border;
    public override Color SeparatorDark => Border;
    public override Color SeparatorLight => Surface;
}

internal sealed class CardPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.FromArgb(31, 45, 67);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 20;

    public CardPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        if (Width < 2 || Height < 2) return;
        using var path = CreateRoundedPath(new RectangleF(0, 0, Width, Height), Radius);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(BorderColor, 1f);
        using var path = CreateRoundedPath(new RectangleF(.5f, .5f, Width - 1f, Height - 1f), Radius);
        e.Graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ModernButton : Control
{
    private readonly Color fillColor;
    private readonly Color hoverColor;
    private bool hovered;
    private bool pressed;

    public ModernButton(string text, Color fillColor, Color hoverColor)
    {
        Text = text;
        this.fillColor = fillColor;
        this.hoverColor = hoverColor;
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TabStop = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = hovered ? hoverColor : fillColor;
        if (pressed) color = ControlPaint.Dark(color, .08f);
        using var brush = new SolidBrush(color);
        e.Graphics.FillRoundedRectangle(brush, new RectangleF(0, 0, Width, Height), 12);
        if (Focused)
        {
            using var focus = new Pen(Color.FromArgb(145, 255, 255, 255), 1.5f);
            e.Graphics.DrawRoundedRectangle(focus, new RectangleF(2, 2, Width - 4, Height - 4), 10);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

internal sealed class ControllerMapControl : Control
{
    private readonly Dictionary<string, MappingInfo> mappings;
    private readonly Bitmap? controllerArtwork;
    private readonly System.Windows.Forms.Timer animation;
    private MapItem? hovered;
    private float glow;

    public event Action<MapItem?>? HoveredChanged;
    public IReadOnlyList<MapItem> Items { get; private set; }

    public ControllerMapControl(Dictionary<string, MappingInfo> mappings)
    {
        this.mappings = mappings;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(8, 12, 21);
        Cursor = Cursors.Default;
        controllerArtwork = LoadControllerArtwork();
        Items = BuildItems();
        animation = new System.Windows.Forms.Timer { Interval = 16 };
        animation.Tick += (_, _) =>
        {
            var target = hovered is null ? 0f : 1f;
            glow += (target - glow) * .18f;
            if (hovered is not null || glow > .01f) Invalidate();
        };
        animation.Start();
        MouseMove += OnMouseMoved;
        MouseLeave += (_, _) => SetHover(null);
    }

    public void ReloadMappings(Dictionary<string, MappingInfo> updated)
    {
        mappings.Clear();
        foreach (var item in updated) mappings[item.Key] = item.Value;
        Items = BuildItems();
        SetHover(null);
        Invalidate();
    }

    private IReadOnlyList<MapItem> BuildItems()
    {
        MapItem Item(string key, string display, float x, float y, float radius = 24, bool list = true)
        {
            var info = mappings.GetValueOrDefault(key, new MappingInfo("未映射", ""));
            return new MapItem(key, display, info.Function, info.Shortcut, info.Action,
                info.DefaultShortcut, info.CustomShortcut, x, y, radius, list);
        }

        return new List<MapItem>
        {
            Item("LeftTrigger", "L2", 208, 78, 34), Item("RightTrigger", "R2", 692, 78, 34),
            Item("LeftShoulder", "L1", 236, 116, 34), Item("RightShoulder", "R1", 664, 116, 34),
            Item("LeftStickUp", "左摇杆 ↑", 326, 306, 18), Item("LeftStickDown", "左摇杆 ↓", 326, 362, 18),
            Item("LeftStickLeft", "左摇杆 ←", 298, 334, 18), Item("LeftStickRight", "左摇杆 →", 354, 334, 18),
            Item("LeftStick", "L3", 326, 334, 23),
            Item("DpadUp", "十字键 ↑", 248, 205, 17), Item("DpadDown", "十字键 ↓", 248, 255, 17),
            Item("DpadLeft", "十字键 ←", 223, 230, 17), Item("DpadRight", "十字键 →", 273, 230, 17),
            Item("View", "View（三点）", 371, 180, 18), Item("StadiaAssistant", "Assistant（四点）", 395, 233, 18),
            Item("Guide", "Stadia", 450, 336, 27), Item("Menu", "Menu（三横）", 531, 180, 18),
            Item("StadiaCapture", "Capture（取景框）", 505, 233, 18),
            Item("North", "Y", 666, 188, 23), Item("West", "X", 613, 229, 23),
            Item("East", "B", 715, 237, 23), Item("South", "A", 660, 277, 23),
            Item("RightStickUp", "右摇杆 ↑", 572, 306, 18), Item("RightStickDown", "右摇杆 ↓", 572, 362, 18),
            Item("RightStickLeft", "右摇杆 ←", 544, 334, 18), Item("RightStickRight", "右摇杆 →", 600, 334, 18),
            Item("RightStick", "R3", 572, 334, 23)
        };
    }

    private static Bitmap? LoadControllerArtwork()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PadMicro-Keymap.png");
        if (!File.Exists(path)) return null;
        try
        {
            using var source = new Bitmap(path);
            return new Bitmap(source);
        }
        catch
        {
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animation.Dispose();
            controllerArtwork?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnMouseMoved(object? sender, MouseEventArgs e)
    {
        var point = ToDesign(e.Location);
        var next = Items
            .OrderBy(item => item.Radius)
            .FirstOrDefault(item => Distance(point, new PointF(item.X, item.Y)) <= item.Radius);
        SetHover(next);
    }

    public void SetExternalHover(MapItem? item) => SetHover(item);

    public void ExportPoster(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Bitmap(1600, 1000);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.FromArgb(9, 14, 25));

        using var titleFont = new Font("Microsoft YaHei UI", 42f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subtitleFont = new Font("Microsoft YaHei UI", 20f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(Color.FromArgb(238, 244, 255));
        using var mutedBrush = new SolidBrush(Color.FromArgb(132, 153, 184));
        g.DrawString("PadMicro 按键映射", titleFont, titleBrush, 42, 30);
        g.DrawString("按 Stadia 实物布局绘制 · 映射读取自 controller-padmicro-profile.json", subtitleFont, mutedBrush, 47, 108);

        var controllerState = g.Save();
        g.TranslateTransform(12, 182);
        g.ScaleTransform(.98f, .98f);
        DrawController(g);
        g.Restore(controllerState);

        using var panelBrush = new SolidBrush(Color.FromArgb(14, 23, 39));
        using var panelPen = new Pen(Color.FromArgb(42, 207, 195), 1.5f);
        var panel = new RectangleF(870, 142, 685, 820);
        g.FillRoundedRectangle(panelBrush, panel, 18);
        g.DrawRoundedRectangle(panelPen, panel, 18);
        using var sectionFont = new Font("Microsoft YaHei UI", 28f, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("全部当前映射", sectionFont, titleBrush, 898, 165);

        using var keyFont = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var functionFont = new Font("Microsoft YaHei UI", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var shortcutFont = new Font("Consolas", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var functionBrush = new SolidBrush(Color.FromArgb(198, 211, 232));
        using var shortcutBrush = new SolidBrush(Color.FromArgb(77, 220, 195));
        using var divider = new Pen(Color.FromArgb(30, 49, 72), 1f);
        using var functionFormat = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        using var shortcutFormat = new StringFormat
        {
            Alignment = StringAlignment.Far,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        var y = 212f;
        foreach (var item in Items.Where(item => item.ShowInList))
        {
            g.DrawString(item.Display, keyFont, titleBrush, new RectangleF(898, y, 190, 24), functionFormat);
            g.DrawString(item.Function, functionFont, functionBrush, new RectangleF(1088, y, 300, 24), functionFormat);
            g.DrawString(item.Shortcut, shortcutFont, shortcutBrush, new RectangleF(1392, y, 135, 24), shortcutFormat);
            g.DrawLine(divider, 898, y + 25, 1527, y + 25);
            y += 27f;
        }

        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void SetHover(MapItem? item)
    {
        if (ReferenceEquals(hovered, item)) return;
        hovered = item;
        glow = item is null ? glow : .15f;
        Cursor = item is null ? Cursors.Default : Cursors.Hand;
        HoveredChanged?.Invoke(item);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        if (ClientSize.Width < 4 || ClientSize.Height < 4) return;

        var surface = new RectangleF(1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
        using (var card = new LinearGradientBrush(surface,
                   Color.FromArgb(14, 23, 38), Color.FromArgb(9, 15, 26), 90f))
            g.FillRoundedRectangle(card, surface, 22);
        using (var border = new Pen(Color.FromArgb(28, 42, 63), 1f))
            g.DrawRoundedRectangle(border, surface, 22);
        using (var dot = new SolidBrush(Color.FromArgb(20, 107, 132, 165)))
        {
            for (var y = 30; y < ClientSize.Height - 20; y += 34)
                for (var x = 30; x < ClientSize.Width - 20; x += 34)
                    g.FillEllipse(dot, x, y, 1.6f, 1.6f);
        }

        var state = g.Save();
        var scale = Math.Min(ClientSize.Width / 900f, ClientSize.Height / 620f);
        var offset = new PointF((ClientSize.Width - 900 * scale) / 2f, (ClientSize.Height - 620 * scale) / 2f);
        g.TranslateTransform(offset.X, offset.Y);
        g.ScaleTransform(scale, scale);
        DrawController(g);
        g.Restore(state);
    }

    private void DrawController(Graphics g)
    {
        if (controllerArtwork is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(controllerArtwork,
                new RectangleF(90, 115, 720, 490),
                new Rectangle(420, 205, 825, 561),
                GraphicsUnit.Pixel);
            DrawTrigger(g, 208, 78, "L2"); DrawTrigger(g, 692, 78, "R2");
            DrawTrigger(g, 236, 116, "L1"); DrawTrigger(g, 664, 116, "R1");

            foreach (var item in Items)
                if (ReferenceEquals(item, hovered)) DrawHighlight(g, item);

            using var artworkHintFont = new Font("Microsoft YaHei UI", 9f);
            using var artworkHintBrush = new SolidBrush(Color.FromArgb(112, 132, 162));
            g.DrawString("悬停按键查看映射", artworkHintFont, artworkHintBrush, 365, 575);
            return;
        }

        using var shadow = new GraphicsPath();
        AddBody(shadow, 0, 18);
        using var shadowBrush = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
        g.FillPath(shadowBrush, shadow);

        using var body = new GraphicsPath();
        AddBody(body, 0, 0);
        using var bodyBrush = new LinearGradientBrush(new Rectangle(120, 120, 660, 430),
            Color.FromArgb(239, 243, 249), Color.FromArgb(177, 188, 204), 90f);
        g.FillPath(bodyBrush, body);
        using var outline = new Pen(Color.FromArgb(95, 112, 139), 2f);
        g.DrawPath(outline, body);

        DrawTrigger(g, 208, 78, "L2"); DrawTrigger(g, 692, 78, "R2");
        DrawTrigger(g, 236, 116, "L1"); DrawTrigger(g, 664, 116, "R1");
        DrawStick(g, 326, 334, "L3", true); DrawStick(g, 572, 334, "R3", false);
        DrawDpad(g);
        DrawButton(g, 666, 188, "Y"); DrawButton(g, 613, 229, "X");
        DrawButton(g, 715, 237, "B"); DrawButton(g, 660, 277, "A");
        DrawSmallButton(g, 371, 180, "•••"); DrawSmallButton(g, 395, 233, "••••");
        DrawSmallButton(g, 531, 180, "☰"); DrawSmallButton(g, 505, 233, "[ ]");
        DrawButton(g, 450, 336, "S", 28);

        foreach (var item in Items)
            if (ReferenceEquals(item, hovered)) DrawHighlight(g, item);

        using var hintFont = new Font("Microsoft YaHei UI", 9f);
        using var hintBrush = new SolidBrush(Color.FromArgb(112, 132, 162));
        g.DrawString("悬停按键查看映射", hintFont, hintBrush, 365, 575);
    }

    private static void AddBody(GraphicsPath path, float x, float y)
    {
        path.StartFigure();
        path.AddBezier(210 + x, 135 + y, 150 + x, 140 + y, 92 + x, 260 + y, 92 + x, 418 + y);
        path.AddBezier(92 + x, 418 + y, 92 + x, 555 + y, 188 + x, 578 + y, 254 + x, 458 + y);
        path.AddBezier(254 + x, 458 + y, 300 + x, 440 + y, 350 + x, 438 + y, 450 + x, 438 + y);
        path.AddBezier(450 + x, 438 + y, 550 + x, 438 + y, 600 + x, 440 + y, 646 + x, 458 + y);
        path.AddBezier(646 + x, 458 + y, 712 + x, 578 + y, 808 + x, 555 + y, 808 + x, 418 + y);
        path.AddBezier(808 + x, 418 + y, 808 + x, 260 + y, 780 + x, 148 + y, 690 + x, 135 + y);
        path.AddBezier(690 + x, 135 + y, 590 + x, 119 + y, 530 + x, 146 + y, 450 + x, 146 + y);
        path.AddBezier(450 + x, 146 + y, 370 + x, 146 + y, 310 + x, 119 + y, 210 + x, 135 + y);
        path.CloseFigure();
    }

    private void DrawTrigger(Graphics g, float x, float y, string text)
    {
        using var brush = new SolidBrush(Color.FromArgb(32, 42, 58));
        using var pen = new Pen(Color.FromArgb(91, 112, 143), 2);
        g.FillRoundedRectangle(brush, new RectangleF(x - 42, y - 17, 84, 34), 14);
        g.DrawRoundedRectangle(pen, new RectangleF(x - 42, y - 17, 84, 34), 14);
        DrawCentered(g, text, x, y, 10, Color.FromArgb(218, 229, 245));
    }

    private void DrawStick(Graphics g, float x, float y, string text, bool left)
    {
        using var outer = new SolidBrush(Color.FromArgb(76, 90, 109));
        using var inner = new SolidBrush(Color.FromArgb(27, 34, 46));
        g.FillEllipse(outer, x - 57, y - 57, 114, 114);
        g.FillEllipse(inner, x - 46, y - 46, 92, 92);
        DrawCentered(g, "↑", x, y - 36, 10, Color.FromArgb(105, 139, 171));
        DrawCentered(g, "↓", x, y + 36, 10, Color.FromArgb(105, 139, 171));
        DrawCentered(g, "←", x - 36, y, 10, Color.FromArgb(105, 139, 171));
        DrawCentered(g, "→", x + 36, y, 10, Color.FromArgb(105, 139, 171));
        DrawCentered(g, text, x, y, 9, Color.FromArgb(207, 220, 238));
    }

    private void DrawDpad(Graphics g)
    {
        using var brush = new SolidBrush(Color.FromArgb(31, 40, 54));
        g.FillRoundedRectangle(brush, new RectangleF(229, 186, 38, 88), 8);
        g.FillRoundedRectangle(brush, new RectangleF(204, 211, 88, 38), 8);
        DrawCentered(g, "↑", 248, 205, 10, Color.FromArgb(184, 201, 223));
        DrawCentered(g, "↓", 248, 255, 10, Color.FromArgb(184, 201, 223));
        DrawCentered(g, "←", 223, 230, 10, Color.FromArgb(184, 201, 223));
        DrawCentered(g, "→", 273, 230, 10, Color.FromArgb(184, 201, 223));
    }

    private void DrawButton(Graphics g, float x, float y, string text, float radius = 25)
    {
        using var brush = new SolidBrush(Color.FromArgb(26, 34, 47));
        using var pen = new Pen(Color.FromArgb(95, 115, 145), 2);
        g.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
        g.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);
        DrawCentered(g, text, x, y, 14, Color.FromArgb(235, 241, 250));
    }

    private void DrawSmallButton(Graphics g, float x, float y, string text)
    {
        using var brush = new SolidBrush(Color.FromArgb(38, 49, 65));
        g.FillEllipse(brush, x - 18, y - 13, 36, 26);
        DrawCentered(g, text, x, y, 8, Color.FromArgb(221, 230, 244));
    }

    private void DrawHighlight(Graphics g, MapItem item)
    {
        var alpha = (int)(90 + 100 * glow);
        using var halo = new SolidBrush(Color.FromArgb(alpha / 3, 41, 224, 202));
        using var ring = new Pen(Color.FromArgb(alpha, 63, 237, 215), 3f + glow * 2f);
        var radius = item.Radius + 10 + glow * 5;
        g.FillEllipse(halo, item.X - radius, item.Y - radius, radius * 2, radius * 2);
        g.DrawEllipse(ring, item.X - item.Radius - 4, item.Y - item.Radius - 4,
            (item.Radius + 4) * 2, (item.Radius + 4) * 2);
        using var labelBrush = new SolidBrush(Color.FromArgb(235, 13, 22, 35));
        using var labelPen = new Pen(Color.FromArgb(95, 63, 237, 215), 1);
        var label = $"{item.Display}  ·  {item.Function}";
        using var font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        var size = g.MeasureString(label, font);
        var box = new RectangleF(450 - size.Width / 2 - 12, 510, size.Width + 24, 36);
        g.FillRoundedRectangle(labelBrush, box, 10);
        g.DrawRoundedRectangle(labelPen, box, 10);
        g.DrawString(label, font, Brushes.White, box.Left + 12, box.Top + 8);
    }

    private static void DrawCentered(Graphics g, string text, float x, float y, float size, Color color)
    {
        using var font = new Font("Microsoft YaHei UI", size, FontStyle.Bold);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, new PointF(x, y), format);
    }

    private PointF ToDesign(Point point)
    {
        var scale = Math.Min(ClientSize.Width / 900f, ClientSize.Height / 620f);
        var offsetX = (ClientSize.Width - 900 * scale) / 2f;
        var offsetY = (ClientSize.Height - 620 * scale) / 2f;
        return new PointF((point.X - offsetX) / scale, (point.Y - offsetY) / scale);
    }

    private static float Distance(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

internal sealed record MapItem(
    string ProfileKey,
    string Display,
    string Function,
    string Shortcut,
    string Action,
    string DefaultShortcut,
    string? CustomShortcut,
    float X,
    float Y,
    float Radius,
    bool ShowInList);

internal sealed record MappingInfo(
    string Function,
    string Shortcut,
    string Action = "",
    string DefaultShortcut = "",
    string? CustomShortcut = null,
    bool Enabled = true);

internal static class ActionCatalog
{
    public static readonly Dictionary<string, MappingInfo> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Send"] = new("提交或确认", "Enter"),
        ["Cancel"] = new("中断运行或关闭窗口", "Esc"),
        ["ForkChat"] = new("在新任务中继续当前对话", "/fork"),
        ["SmartDelete"] = new("单击退格；双击全选输入框", "Backspace / Ctrl+A"),
        ["ChooseProject"] = new("选择项目", "/project"),
        ["TypelessToggle"] = new("开始或结束 Typeless 听写", "Right Alt"),
        ["NewChat"] = new("创建新任务", "codex://threads/new"),
        ["QuickChat"] = new("新聊天", "Ctrl+Alt+N"),
        ["PreviousChat"] = new("向左切换侧边栏任务", "Ctrl+Shift+["),
        ["NextChat"] = new("向右切换侧边栏任务", "Ctrl+Shift+]"),
        ["CycleMode"] = new("循环模式", "Shift+Tab"),
        ["CodexDictation"] = new("Codex 自带听写", "Ctrl+Shift+D"),
        ["SystemReserved"] = new("系统保留（开机 / Xbox Game Bar）", "不发送命令"),
        ["CyclePlanMode"] = new("切换计划模式", "/plan"),
        ["OpenTerminal"] = new("打开终端", "Ctrl+`"),
        ["ToggleSidePanel"] = new("显示或隐藏边栏", "Ctrl+Alt+B"),
        ["ToggleSidebar"] = new("切换 Sidebar", "Ctrl+B"),
        ["OpenBrowser"] = new("打开浏览器", "Ctrl+T"),
        ["ScrollUp"] = new("向上滚动聊天", "Mouse Wheel ↑"),
        ["ScrollDown"] = new("向下滚动聊天", "Mouse Wheel ↓"),
        ["ThinkingDown"] = new("降低模型推理强度", "Ctrl+Alt+-"),
        ["ThinkingUp"] = new("提高模型推理强度", "Ctrl+Alt+="),
        ["ArrowUp"] = new("方向键上", "↑"),
        ["ArrowDown"] = new("方向键下", "↓"),
        ["ArrowLeft"] = new("方向键左", "←"),
        ["ArrowRight"] = new("方向键右", "→")
    };
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius) =>
        graphics.FillPath(brush, Rounded(bounds, radius));

    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF bounds, float radius) =>
        graphics.DrawPath(pen, Rounded(bounds, radius));

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
