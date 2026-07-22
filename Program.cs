using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

internal static class Program
{
    private const uint SdlInitGameController = 0x00002000;
    private const string SdlLibrary = "SDL2";
    private static readonly string DefaultSdlPath = @"C:\Program Files\AntiMicroX\bin\SDL2.dll";

    private static readonly Dictionary<string, int> ButtonIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["South"] = 0, ["East"] = 1, ["West"] = 2, ["North"] = 3,
        ["A"] = 0, ["B"] = 1, ["X"] = 2, ["Y"] = 3,
        ["View"] = 4, ["Back"] = 4, ["Guide"] = 5, ["Menu"] = 6, ["Start"] = 6,
        ["LeftStick"] = 7, ["RightStick"] = 8,
        ["LeftShoulder"] = 9, ["RightShoulder"] = 10,
        ["DpadUp"] = 11, ["DpadDown"] = 12,
        ["DpadLeft"] = 13, ["DpadRight"] = 14,
        ["Misc1"] = 15, ["Paddle1"] = 16, ["Paddle2"] = 17,
        ["Paddle3"] = 18, ["Paddle4"] = 19, ["Touchpad"] = 20,
        ["LeftTrigger"] = 100, ["RightTrigger"] = 101
    };

    private static readonly Dictionary<string, ActionBinding> BuiltInActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Send"] = new("提交/确认", new[] { new[] { 0x0D } }),
        ["Cancel"] = new("中断/关闭", new[] { new[] { 0x1B } }),
        ["NewChat"] = new("新建任务", Uri: "codex://threads/new"),
        ["DictationHold"] = new("按住说话", new[] { new[] { 0x11, 0x10, 0x44 } }, Hold: true),
        ["TypelessToggle"] = new("Typeless 语音开关", new[] { new[] { 0xA5 } }),
        ["PreviousChat"] = new("上一个任务", new[] { new[] { 0x11, 0x10, 0xDB } }),
        ["NextChat"] = new("下一个任务", new[] { new[] { 0x11, 0x10, 0xDD } }),
        ["ArrowUp"] = new("菜单上移", new[] { new[] { 0x26 } }),
        ["ArrowDown"] = new("菜单下移", new[] { new[] { 0x28 } }),
        ["ArrowLeft"] = new("菜单左移", new[] { new[] { 0x25 } }),
        ["ArrowRight"] = new("菜单右移", new[] { new[] { 0x27 } }),
        ["CycleMode"] = new("循环模式", new[] { new[] { 0x10, 0x09 } }),
        ["ClearComposer"] = new("清空输入框", new[] { new[] { 0x11, 0x41 }, new[] { 0x08 } }),
        ["ThinkingUp"] = new("提高推理强度", new[] { new[] { 0x11, 0x12, 0xBB } }),
        ["ThinkingDown"] = new("降低推理强度", new[] { new[] { 0x11, 0x12, 0xBD } }),
        ["ModelPrevious"] = new("上一个模型", new[] { new[] { 0x11, 0x10, 0x4D }, new[] { 0x26 }, new[] { 0x0D } }, StepDelayMs: 140),
        ["ModelNext"] = new("下一个模型", new[] { new[] { 0x11, 0x10, 0x4D }, new[] { 0x28 }, new[] { 0x0D } }, StepDelayMs: 140),
        ["ModelPicker"] = new("打开模型选择器", new[] { new[] { 0x11, 0x10, 0x4D } }),
        ["Review"] = new("审查标签页", new[] { new[] { 0x11, 0x10, 0x47 } }),
        ["SmartDelete"] = new("单击退格 / 双击全选", new[] { new[] { 0x08 } }, DoubleChords: new[] { new[] { 0x11, 0x41 } }),
        ["ChooseProject"] = new("选择项目", new[] { new[] { 0x11, 0x12, 0x10, 0x4F } }),
        ["ForkChat"] = new("在新任务中继续", new[] { new[] { 0x0D } }, Text: "/fork", StepDelayMs: 120),
        ["ScrollUp"] = new("聊天向上滚动", Custom: "ScrollUp"),
        ["ScrollDown"] = new("聊天向下滚动", Custom: "ScrollDown"),
        ["ToggleSidebar"] = new("切换 Sidebar", new[] { new[] { 0x11, 0x42 } }),
        ["OpenBrowser"] = new("浏览器", new[] { new[] { 0x11, 0x54 } }),
        ["OpenTerminal"] = new("终端", new[] { new[] { 0x11, 0xC0 } }),
        ["ToggleSidePanel"] = new("显示/隐藏边栏", new[] { new[] { 0x11, 0x12, 0x42 } }),
        ["CodexDictation"] = new("按住使用 Codex 听写", new[] { new[] { 0x11, 0x10, 0x44 } }, Hold: true),
        ["CyclePlanMode"] = new("切换计划模式（快捷键未配置）")
    };

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var baseDir = AppContext.BaseDirectory;
        var checkOnly = args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase));
        var diagnose = args.Any(a => a.Equals("--diagnose", StringComparison.OrdinalIgnoreCase));
        using var serviceInstance = AcquireServiceInstance(checkOnly || diagnose, out var acquired);
        if (!acquired)
        {
            Console.WriteLine("StadiaCodexBridge 服务已在运行，本次启动已忽略。");
            return 0;
        }
        var profilePath = args.FirstOrDefault(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                          ?? Path.Combine(baseDir, "controller-codex-profile.json");
        var profile = LoadProfile(profilePath);

        var sdlPath = Environment.GetEnvironmentVariable("STADIA_CODEX_SDL2") ?? DefaultSdlPath;
        if (!File.Exists(sdlPath))
        {
            Console.Error.WriteLine($"找不到 SDL2：{sdlPath}");
            Console.Error.WriteLine("请安装 AntiMicroX，或设置 STADIA_CODEX_SDL2 环境变量。");
            return 2;
        }

        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (name, _, _) =>
            name == SdlLibrary ? NativeLibrary.Load(sdlPath) : IntPtr.Zero);

        if (SDL_Init(SdlInitGameController) != 0)
        {
            Console.Error.WriteLine("无法初始化手柄输入：" + Marshal.PtrToStringUTF8(SDL_GetError()));
            return 3;
        }

        try
        {
            var controller = OpenGameController();
            if (controller == IntPtr.Zero)
            {
                Console.Error.WriteLine("没有找到兼容的游戏手柄。请先连接 Stadia、Xbox 或 Switch 手柄再运行。");
                return 4;
            }

            if (checkOnly)
            {
                PrintMappings(profile);
                SDL_GameControllerUpdate();
                Console.WriteLine($"标准扳机静止值：L2={SDL_GameControllerGetAxis(controller, 4)}, R2={SDL_GameControllerGetAxis(controller, 5)}");
                using var specialButtons = StadiaSpecialButtonReader.TryStart(() => { }, () => { });
                Console.WriteLine($"可跨项目导航的任务：{TaskNavigator.CountTasks()}");
                Console.WriteLine("手柄输入检查通过。");
                SDL_GameControllerClose(controller);
                return 0;
            }

            if (diagnose)
            {
                using var specialButtons = StadiaSpecialButtonReader.TryStart(
                    () => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  Stadia Capture: DOWN"),
                    () => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  Stadia Assistant: DOWN"));
                RunRawDiagnostic(controller, Path.Combine(baseDir, "stadia-diagnostic.log"), TimeSpan.FromSeconds(25));
                SDL_GameControllerClose(controller);
                return 0;
            }

            using var quit = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Cancel(); };

            Console.WriteLine("Controller → Codex 映射已启动。按 Ctrl+C 退出。");
            Console.WriteLine("仅在 Codex/ChatGPT 窗口位于前台时发送快捷键。\n");
            PrintMappings(profile);
            Run(controller, profile, quit.Token);
            SDL_GameControllerClose(controller);
            return 0;
        }
        finally
        {
            SDL_Quit();
        }
    }

    private static Mutex? AcquireServiceInstance(bool bypass, out bool acquired)
    {
        if (bypass)
        {
            acquired = true;
            return null;
        }

        return new Mutex(true, @"Local\StadiaCodexBridge.Service.SingleInstance.v1", out acquired);
    }

    private static Profile LoadProfile(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidDataException("配置为空");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"无法读取配置 {path}：{ex.Message}");
            Environment.Exit(5);
            return null!;
        }
    }

    private static IntPtr OpenGameController()
    {
        for (var i = 0; i < SDL_NumJoysticks(); i++)
        {
            if (SDL_IsGameController(i) == 0) continue;
            var controller = SDL_GameControllerOpen(i);
            if (controller == IntPtr.Zero) continue;
            var name = Marshal.PtrToStringUTF8(SDL_GameControllerName(controller)) ?? "Unknown controller";
            Console.WriteLine($"检测到手柄：{name}");
            return controller;
        }
        return IntPtr.Zero;
    }

    private static void Run(IntPtr controller, Profile profile, CancellationToken token)
    {
        var previous = new Dictionary<int, bool>();
        var bindings = new Dictionary<int, ActionBinding>();
        foreach (var item in profile.Buttons)
        {
            var action = ResolveAction(item.Value, profile);
            if (ButtonIds.TryGetValue(item.Key, out var buttonId) && action is not null)
                bindings[buttonId] = action;
        }
        var gestures = profile.Gestures
            .Select(item => (item.Key, Action: ResolveAction(item.Value, profile)))
            .Where(item => item.Action is not null)
            .ToDictionary(item => item.Key, item => item.Action!, StringComparer.OrdinalIgnoreCase);
        var activeHolds = new HashSet<int>();
        var pendingSingles = new Dictionary<int, DateTime>();
        var leftFlick = new FlickTracker("LeftStick");
        var rightFlick = new FlickTracker("RightStick");
        ActionBinding? SpecialAction(string name) => profile.Buttons.TryGetValue(name, out var actionName)
            ? ResolveAction(actionName, profile)
            : null;
        var captureAction = SpecialAction("StadiaCapture");
        var assistantAction = SpecialAction("StadiaAssistant");
        var assistantHoldActive = false;
        void PressAssistant()
        {
            if (assistantAction is null || !IsCodexForeground()) return;
            if (assistantAction.Hold && assistantAction.Chords is { Length: > 0 })
            {
                SetChord(assistantAction.Chords[0], true);
                assistantHoldActive = true;
                LogAction(assistantAction, "↓");
                return;
            }
            ExecuteSpecialAction(assistantAction, "Stadia Assistant");
        }
        void ReleaseAssistant()
        {
            if (!assistantHoldActive || assistantAction?.Chords is not { Length: > 0 }) return;
            SetChord(assistantAction.Chords[0], false);
            assistantHoldActive = false;
            LogAction(assistantAction, "↑");
        }
        using var specialButtons = StadiaSpecialButtonReader.TryStart(
            () => ExecuteSpecialAction(captureAction, "Stadia Capture"),
            PressAssistant,
            ReleaseAssistant);

        try
        {
            while (!token.IsCancellationRequested && SDL_GameControllerGetAttached(controller) != 0)
            {
                SDL_GameControllerUpdate();
                foreach (var (buttonId, action) in bindings)
                {
                    var pressed = ReadControlPressed(controller, buttonId);
                    var wasPressed = previous.GetValueOrDefault(buttonId);
                    if (pressed != wasPressed)
                    {
                        if (action.Hold)
                        {
                            if (pressed && IsCodexForeground())
                            {
                                SetChord(action.Chords![0], true);
                                activeHolds.Add(buttonId);
                                LogAction(action, "↓");
                            }
                            else if (!pressed && activeHolds.Remove(buttonId))
                            {
                                SetChord(action.Chords![0], false);
                                LogAction(action, "↑");
                            }
                        }
                        else if (pressed && IsCodexForeground())
                        {
                            if (action.DoubleChords is not null)
                            {
                                var now = DateTime.UtcNow;
                                if (pendingSingles.TryGetValue(buttonId, out var first)
                                    && now - first <= TimeSpan.FromMilliseconds(320))
                                {
                                    pendingSingles.Remove(buttonId);
                                    ExecuteChords(action.DoubleChords, action.StepDelayMs);
                                    Console.WriteLine($"{DateTime.Now:HH:mm:ss}  全选输入框");
                                }
                                else
                                {
                                    pendingSingles[buttonId] = now;
                                }
                            }
                            else
                            {
                                ExecuteAction(action);
                                LogAction(action, "");
                            }
                        }
                    }
                    previous[buttonId] = pressed;
                }

                foreach (var pending in pendingSingles.ToArray())
                {
                    if (DateTime.UtcNow - pending.Value <= TimeSpan.FromMilliseconds(320)) continue;
                    pendingSingles.Remove(pending.Key);
                    if (!IsCodexForeground()) continue;
                    var action = bindings[pending.Key];
                    ExecuteAction(action);
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss}  退格");
                }

                FireGesture(leftFlick.Update(
                    SDL_GameControllerGetAxis(controller, 0),
                    SDL_GameControllerGetAxis(controller, 1)), gestures);
                FireGesture(rightFlick.Update(
                    SDL_GameControllerGetAxis(controller, 2),
                    SDL_GameControllerGetAxis(controller, 3)), gestures);
                Thread.Sleep(8);
            }
        }
        finally
        {
            foreach (var buttonId in activeHolds)
                SetChord(bindings[buttonId].Chords![0], false);
            ReleaseAssistant();
        }

        if (!token.IsCancellationRequested)
            Console.Error.WriteLine("手柄连接已断开。");
    }

    private static bool ReadControlPressed(IntPtr controller, int id) => id switch
    {
        // SDL's standardized trigger axes rest at 0 even when the underlying
        // Stadia HID axis rests at -32768. Treat only the pressed half as down.
        100 => SDL_GameControllerGetAxis(controller, 4) > 16384,
        101 => SDL_GameControllerGetAxis(controller, 5) > 16384,
        _ => SDL_GameControllerGetButton(controller, id) != 0
    };

    private static void FireGesture(
        string? gesture,
        Dictionary<string, ActionBinding> gestures)
    {
        if (gesture is null || !IsCodexForeground()) return;
        if (!gestures.TryGetValue(gesture, out var action)) return;
        ExecuteAction(action);
        LogAction(action, gesture);
    }

    private static void ExecuteSpecialAction(ActionBinding? action, string button)
    {
        if (action is null || !IsCodexForeground()) return;
        ExecuteAction(action);
        LogAction(action, button);
    }

    private static void LogAction(ActionBinding action, string detail) =>
        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {action.Label} {detail}");

    private static void RunRawDiagnostic(IntPtr controller, string logPath, TimeSpan duration)
    {
        using var log = new StreamWriter(logPath, false, System.Text.Encoding.UTF8) { AutoFlush = true };
        void Write(string message)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
            Console.WriteLine(line);
            log.WriteLine(line);
        }

        var joystick = SDL_GameControllerGetJoystick(controller);
        var buttonCount = SDL_JoystickNumButtons(joystick);
        var axisCount = SDL_JoystickNumAxes(joystick);
        var hatCount = SDL_JoystickNumHats(joystick);
        Write($"原始设备：buttons={buttonCount}, axes={axisCount}, hats={hatCount}");
        Write($"前台进程：{GetForegroundProcessName()}");
        Write("开始 25 秒诊断，请依次按 A/B/X/Y、十字键和摇杆按键。");

        var buttons = new byte[Math.Max(buttonCount, 0)];
        var axes = new short[Math.Max(axisCount, 0)];
        var hats = new byte[Math.Max(hatCount, 0)];
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end && SDL_GameControllerGetAttached(controller) != 0)
        {
            SDL_GameControllerUpdate();
            for (var i = 0; i < buttons.Length; i++)
            {
                var value = SDL_JoystickGetButton(joystick, i);
                if (value != buttons[i])
                {
                    Write($"button {i}: {(value != 0 ? "DOWN" : "UP")}");
                    buttons[i] = value;
                }
            }
            for (var i = 0; i < hats.Length; i++)
            {
                var value = SDL_JoystickGetHat(joystick, i);
                if (value != hats[i])
                {
                    Write($"hat {i}: {value}");
                    hats[i] = value;
                }
            }
            for (var i = 0; i < axes.Length; i++)
            {
                var value = SDL_JoystickGetAxis(joystick, i);
                var changedZone = Math.Abs((int)value) > 16000 != Math.Abs((int)axes[i]) > 16000;
                if (changedZone)
                    Write($"axis {i}: {value}");
                axes[i] = value;
            }
            Thread.Sleep(5);
        }
        Write("诊断结束。");
    }

    private static bool IsCodexForeground()
        => GetCodexForegroundWindow() != IntPtr.Zero;

    private static IntPtr GetCodexForegroundWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            var name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            return name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("codex", StringComparison.OrdinalIgnoreCase)
                ? hwnd
                : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
    }

    private static void ScrollChat(int delta)
    {
        var hwnd = GetCodexForegroundWindow();
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return;
        var x = rect.Left + (rect.Right - rect.Left) * 2 / 3;
        var y = rect.Top + (rect.Bottom - rect.Top) / 2;
        GetCursorPos(out var original);
        SetCursorPos(x, y);
        var input = new INPUT
        {
            type = 0,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    mouseData = unchecked((uint)delta),
                    dwFlags = 0x0800 // MOUSEEVENTF_WHEEL
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        SetCursorPos(original.X, original.Y);
        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  聊天滚动 {(delta > 0 ? "向上" : "向下")}");
    }

    private static string GetForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "<none>";
        GetWindowThreadProcessId(hwnd, out var pid);
        try { return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
        catch { return $"PID {pid}"; }
    }

    private static ActionBinding? ResolveAction(string name, Profile profile)
    {
        if (BuiltInActions.TryGetValue(name, out var action))
        {
            if (!name.Equals("CyclePlanMode", StringComparison.OrdinalIgnoreCase)) return action;
            if (!profile.CustomShortcuts.TryGetValue(name, out var shortcut)
                || string.IsNullOrWhiteSpace(shortcut)) return action;
            if (TryParseShortcut(shortcut, out var chord))
                return action with
                {
                    Label = "切换计划模式",
                    Chords = new[] { chord }
                };
            Console.Error.WriteLine($"计划模式快捷键格式无效：{shortcut}");
            return action;
        }
        const string workflowPrefix = "Workflow:";
        if (name.StartsWith(workflowPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = name[workflowPrefix.Length..];
            if (profile.Workflows.TryGetValue(id, out var prompt))
                return new ActionBinding($"工作流：{id}", Uri: "codex://new?prompt=" + Uri.EscapeDataString(prompt));
        }
        Console.Error.WriteLine($"未知操作：{name}");
        return null;
    }

    private static bool TryParseShortcut(string shortcut, out int[] chord)
    {
        var keys = new List<int>();
        var hasPrimaryKey = false;
        foreach (var rawPart in shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseVirtualKey(rawPart, out var key, out var modifier) || keys.Contains(key))
            {
                chord = Array.Empty<int>();
                return false;
            }
            keys.Add(key);
            hasPrimaryKey |= !modifier;
        }
        chord = keys.ToArray();
        return hasPrimaryKey && chord.Length <= 4;
    }

    private static bool TryParseVirtualKey(string token, out int key, out bool modifier)
    {
        modifier = true;
        switch (token.Trim().ToUpperInvariant())
        {
            case "CTRL": case "CONTROL": key = 0x11; return true;
            case "SHIFT": key = 0x10; return true;
            case "ALT": key = 0x12; return true;
            case "RIGHTALT": case "RALT": key = 0xA5; return true;
        }

        modifier = false;
        var normalized = token.Trim().ToUpperInvariant();
        if (normalized.Length == 1 && char.IsLetterOrDigit(normalized[0]))
        {
            key = normalized[0];
            return true;
        }
        if (normalized.StartsWith('F') && int.TryParse(normalized[1..], out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            key = 0x6F + functionKey;
            return true;
        }
        key = normalized switch
        {
            "TAB" => 0x09, "ENTER" => 0x0D, "ESC" or "ESCAPE" => 0x1B,
            "SPACE" => 0x20, "BACKSPACE" => 0x08,
            "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "`" or "BACKTICK" => 0xC0, "-" or "MINUS" => 0xBD, "=" or "EQUAL" => 0xBB,
            "," or "COMMA" => 0xBC, "." or "PERIOD" => 0xBE, "/" or "SLASH" => 0xBF,
            _ => 0
        };
        return key != 0;
    }

    private static void ExecuteAction(ActionBinding action)
    {
        if (action.Custom == "ScrollUp")
        {
            ScrollChat(+480);
            return;
        }
        if (action.Custom == "ScrollDown")
        {
            ScrollChat(-480);
            return;
        }
        if (action.Uri is not null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = action.Uri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"无法打开 Codex 链接：{ex.Message}");
            }
            return;
        }
        if (action.Text is not null)
        {
            SendUnicodeText(action.Text);
            Thread.Sleep(action.StepDelayMs);
        }
        if (action.Chords is not null) ExecuteChords(action.Chords, action.StepDelayMs);
    }

    private static void ExecuteChords(int[][] chords, int stepDelayMs)
    {
        foreach (var chord in chords)
        {
            SendChord(chord);
            if (chords.Length > 1) Thread.Sleep(stepDelayMs);
        }
    }

    private static void SendUnicodeText(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(UnicodeInput(character, false));
            inputs.Add(UnicodeInput(character, true));
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static void SendChord(int[] keys)
    {
        var inputs = new List<INPUT>();
        foreach (var key in keys) inputs.Add(KeyInput((ushort)key, false));
        for (var i = keys.Length - 1; i >= 0; i--) inputs.Add(KeyInput((ushort)keys[i], true));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static void SetChord(int[] keys, bool down)
    {
        var inputs = new List<INPUT>();
        if (down)
            foreach (var key in keys) inputs.Add(KeyInput((ushort)key, false));
        else
            for (var i = keys.Length - 1; i >= 0; i--) inputs.Add(KeyInput((ushort)keys[i], true));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyInput(ushort key, bool up) => new()
    {
        type = 1,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = key,
                dwFlags = (up ? 0x0002u : 0u) | (key == 0xA5 ? 0x0001u : 0u)
            }
        }
    };

    private static INPUT UnicodeInput(char character, bool up) => new()
    {
        type = 1,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wScan = character,
                dwFlags = 0x0004u | (up ? 0x0002u : 0u)
            }
        }
    };

    private static void PrintMappings(Profile profile)
    {
        foreach (var item in profile.Buttons)
        {
            var action = ResolveAction(item.Value, profile);
            if (action is not null)
                Console.WriteLine($"  {item.Key,-15} → {action.Label}");
        }
        foreach (var item in profile.Gestures)
        {
            var action = ResolveAction(item.Value, profile);
            if (action is not null)
                Console.WriteLine($"  {item.Key,-15} → {action.Label}");
        }
        Console.WriteLine();
    }

    private sealed record ActionBinding(
        string Label,
        int[][]? Chords = null,
        bool Hold = false,
        string? Uri = null,
        int StepDelayMs = 25,
        int[][]? DoubleChords = null,
        string? Text = null,
        string? Custom = null);
    private sealed class Profile
    {
        public Dictionary<string, string> Buttons { get; set; } = new();
        public Dictionary<string, string> Gestures { get; set; } = new();
        public Dictionary<string, string> Workflows { get; set; } = new();
        public Dictionary<string, string> CustomShortcuts { get; set; } = new();
    }

    private sealed record DesktopTask(string Id, string Cwd, long Recency, string? Model);

    private sealed class TaskNavigator
    {
        private string? currentId;
        private readonly List<DesktopTask> tasks = ReadTasks();

        public string? InitialModel => tasks.FirstOrDefault()?.Model;

        public static int CountTasks() => ReadTasks().Count;

        public DesktopTask? Move(int delta)
        {
            if (tasks.Count == 0)
            {
                Console.Error.WriteLine("没有从 Codex 任务目录读取到可导航任务。");
                return null;
            }

            var current = currentId is null ? 0 : tasks.FindIndex(t => t.Id == currentId);
            if (current < 0) current = 0;
            var nextIndex = Math.Clamp(current + delta, 0, tasks.Count - 1);
            if (nextIndex == current && currentId is not null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  已到任务列表{(delta < 0 ? "顶部" : "底部")}");
                return tasks[current];
            }

            var next = tasks[nextIndex];
            currentId = next.Id;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"codex://threads/{next.Id}",
                    UseShellExecute = true
                });
                var project = Path.GetFileName(next.Cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                Console.WriteLine($"{DateTime.Now:HH:mm:ss}  任务 {(delta < 0 ? "上移" : "下移")} → {project} ({nextIndex + 1}/{tasks.Count})");
                return next;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"无法打开 Codex 任务：{ex.Message}");
                return null;
            }
        }

        private static List<DesktopTask> ReadTasks()
        {
            var dbPath = Path.Combine(
                Environment.GetEnvironmentVariable("USERPROFILE")
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex",
                "state_5.sqlite");
            if (!File.Exists(dbPath)) return new();

            var nodeRows = ReadTasksWithNode(dbPath);
            if (nodeRows.Count > 0) return OrderTasks(nodeRows);

            IntPtr db = IntPtr.Zero;
            IntPtr statement = IntPtr.Zero;
            try
            {
                var openResult = sqlite3_open_v2(dbPath, out db, 0x00000001, IntPtr.Zero);
                if (openResult != 0)
                {
                    Console.Error.WriteLine($"Codex 任务数据库打开失败 ({openResult})：{SqliteError(db)}");
                    return new();
                }
                sqlite3_busy_timeout(db, 1000);
                const string sql = """
                    SELECT id, cwd, COALESCE(recency_at_ms, updated_at * 1000) AS recency, model
                    FROM threads
                    WHERE archived = 0
                      AND (source IS NULL OR source NOT LIKE '{%')
                    ORDER BY id DESC
                    """;
                var prepareResult = sqlite3_prepare_v2(db, sql, -1, out statement, IntPtr.Zero);
                if (prepareResult != 0)
                {
                    Console.Error.WriteLine($"Codex 任务查询准备失败 ({prepareResult})：{SqliteError(db)}");
                    return new();
                }

                var rows = new List<DesktopTask>();
                int stepResult;
                while ((stepResult = sqlite3_step(statement)) == 100)
                {
                    var id = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 0));
                    var cwd = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 1));
                    var recency = sqlite3_column_int64(statement, 2);
                    var model = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 3));
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(cwd))
                        rows.Add(new DesktopTask(id, cwd, recency, model));
                }
                if (stepResult != 101)
                    Console.Error.WriteLine($"Codex 任务查询失败 ({stepResult})：{SqliteError(db)}");

                return OrderTasks(rows);
            }
            finally
            {
                if (statement != IntPtr.Zero) sqlite3_finalize(statement);
                if (db != IntPtr.Zero) sqlite3_close(db);
            }
        }

        private static List<DesktopTask> ReadTasksWithNode(string dbPath)
        {
            var script = Path.Combine(AppContext.BaseDirectory, "read-codex-tasks.cjs");
            if (!File.Exists(script))
            {
                Console.Error.WriteLine($"任务读取脚本不存在：{script}");
                return new();
            }
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "node.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                start.ArgumentList.Add("--experimental-sqlite");
                start.ArgumentList.Add("--no-warnings");
                start.ArgumentList.Add(script);
                start.ArgumentList.Add(dbPath);
                using var process = System.Diagnostics.Process.Start(start);
                if (process is null) return new();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(3000) || process.ExitCode != 0)
                {
                    Console.Error.WriteLine($"Node.js 任务读取失败：{error.Trim()}");
                    return new();
                }
                var parsed = JsonSerializer.Deserialize<List<DesktopTask>>(output, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new();
                return parsed;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Node.js 任务读取异常：{ex.Message}");
                return new();
            }
        }

        private static List<DesktopTask> OrderTasks(List<DesktopTask> rows) => rows
            .Select(t => t with { Cwd = NormalizeCwd(t.Cwd) })
            .GroupBy(t => t.Cwd, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Max(t => t.Recency))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g
                .OrderByDescending(t => t.Recency)
                .ThenByDescending(t => t.Id, StringComparer.Ordinal))
            .ToList();

        private static string NormalizeCwd(string cwd)
        {
            var normalized = cwd.StartsWith(@"\\?\", StringComparison.Ordinal) ? cwd[4..] : cwd;
            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string SqliteError(IntPtr database) => database == IntPtr.Zero
            ? "unknown"
            : Marshal.PtrToStringUTF8(sqlite3_errmsg(database)) ?? "unknown";
    }

    private sealed class StadiaSpecialButtonReader : IDisposable
    {
        private readonly CancellationTokenSource stop = new();
        private readonly List<FileStream> streams = new();
        private readonly List<Task> readers = new();

        private StadiaSpecialButtonReader(Action capture, Action assistantPressed, Action assistantReleased)
        {
            foreach (var path in EnumerateStadiaHidPaths())
            {
                try
                {
                    var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        64,
                        FileOptions.Asynchronous);
                    var reportLength = GetInputReportLength(stream.SafeFileHandle);
                    if (reportLength < 3)
                    {
                        stream.Dispose();
                        continue;
                    }
                    streams.Add(stream);
                    readers.Add(Task.Run(() => ReadLoop(
                        stream, reportLength, capture, assistantPressed, assistantReleased, stop.Token)));
                }
                catch
                {
                    // Some Stadia HID collections are audio/output-only and cannot be read.
                }
            }

            Console.WriteLine(readers.Count > 0
                ? "Stadia Capture/Assistant 专用 HID 通道已连接。"
                : "提示：未打开 Stadia Capture/Assistant 专用 HID 通道；普通手柄按键仍可使用。");
        }

        public static StadiaSpecialButtonReader TryStart(
            Action capture,
            Action assistantPressed,
            Action? assistantReleased = null) =>
            new(capture, assistantPressed, assistantReleased ?? (() => { }));

        private static async Task ReadLoop(
            FileStream stream,
            int reportLength,
            Action capture,
            Action assistantPressed,
            Action assistantReleased,
            CancellationToken token)
        {
            var report = new byte[reportLength];
            byte previous = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(report.AsMemory(0, report.Length), token);
                    if (read < 3 || report[0] != 3) continue;

                    // Report 3 byte 2 uses bit 0 for Capture and bit 1 for Assistant.
                    var current = (byte)(report[2] & 0x03);
                    var pressed = (byte)(current & ~previous);
                    var released = (byte)(previous & ~current);
                    previous = current;
                    if ((pressed & 0x01) != 0) capture();
                    if ((pressed & 0x02) != 0) assistantPressed();
                    if ((released & 0x02) != 0) assistantReleased();
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (IOException ex) when (!token.IsCancellationRequested)
            {
                Console.Error.WriteLine($"Stadia 专用键读取停止：{ex.Message}");
            }
        }

        private static IEnumerable<string> EnumerateStadiaHidPaths()
        {
            HidD_GetHidGuid(out var hidGuid);
            var deviceInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
            if (deviceInfo == new IntPtr(-1)) yield break;
            try
            {
                for (uint index = 0; ; index++)
                {
                    var interfaceData = new SP_DEVICE_INTERFACE_DATA
                    {
                        cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
                    };
                    if (!SetupDiEnumDeviceInterfaces(deviceInfo, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                        yield break;

                    SetupDiGetDeviceInterfaceDetail(deviceInfo, ref interfaceData, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                    if (required <= 4) continue;
                    var detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(deviceInfo, ref interfaceData, detail, required, out _, IntPtr.Zero))
                            continue;
                        var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        if (path is not null
                            && path.Contains("vid_18d1&pid_9400", StringComparison.OrdinalIgnoreCase))
                            yield return path;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfo);
            }
        }

        private static int GetInputReportLength(SafeFileHandle handle)
        {
            if (!HidD_GetPreparsedData(handle, out var preparsed)) return 0;
            try
            {
                var caps = new HIDP_CAPS { Reserved = new ushort[17] };
                return HidP_GetCaps(preparsed, ref caps) == 0x00110000
                    ? caps.InputReportByteLength
                    : 0;
            }
            finally
            {
                HidD_FreePreparsedData(preparsed);
            }
        }

        public void Dispose()
        {
            stop.Cancel();
            foreach (var stream in streams) stream.Dispose();
            try { Task.WaitAll(readers.ToArray(), 500); } catch { }
            stop.Dispose();
        }
    }

    private sealed class FlickTracker(string prefix)
    {
        private bool active;
        private string? direction;

        public string? Update(short rawX, short rawY)
        {
            var x = Math.Clamp(rawX / 32767.0, -1.0, 1.0);
            var y = Math.Clamp(rawY / 32767.0, -1.0, 1.0);
            var magnitude = Math.Sqrt(x * x + y * y);
            if (magnitude > 0.72)
            {
                active = true;
                direction = Math.Abs(x) >= Math.Abs(y)
                    ? x > 0 ? "Right" : "Left"
                    : y > 0 ? "Down" : "Up";
                return null;
            }
            if (active && magnitude < 0.35)
            {
                active = false;
                var fired = direction;
                direction = null;
                return fired is null ? null : prefix + fired;
            }
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
    {
        public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public UIntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] private struct POINT
    {
        public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public UIntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int SDL_Init(uint flags);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_Quit();
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int SDL_NumJoysticks();
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int SDL_IsGameController(int index);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SDL_GameControllerOpen(int index);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_GameControllerClose(IntPtr controller);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SDL_GameControllerName(IntPtr controller);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SDL_GameControllerGetJoystick(IntPtr controller);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int SDL_GameControllerGetAttached(IntPtr controller);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern byte SDL_GameControllerGetButton(IntPtr controller, int button);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern short SDL_GameControllerGetAxis(IntPtr controller, int axis);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_GameControllerUpdate();
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int SDL_JoystickNumButtons(IntPtr joystick);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int SDL_JoystickNumAxes(IntPtr joystick);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int SDL_JoystickNumHats(IntPtr joystick);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern byte SDL_JoystickGetButton(IntPtr joystick, int button);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern short SDL_JoystickGetAxis(IntPtr joystick, int axis);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern byte SDL_JoystickGetHat(IntPtr joystick, int hat);
    [DllImport(SdlLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SDL_GetError();

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid hidGuid);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr preparsedData, ref HIDP_CAPS capabilities);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr parent,
        uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        out int requiredSize,
        IntPtr deviceInfoData);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        out IntPtr database,
        int flags,
        IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(
        IntPtr database,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        int bytes,
        out IntPtr statement,
        IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg(IntPtr database);
}
