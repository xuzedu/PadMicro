# PadMicro

PadMicro 把 Stadia、Xbox 和 Switch 等 SDL 标准游戏手柄转换为 Codex/ChatGPT Windows 桌面端控制器。按键使用位置语义，因此不同品牌的标签可以不同：`South` 永远指手柄最下方的面键。

默认布局参考 [OpenMicro](https://github.com/stephenleo/OpenMicro) 的控制器抽象和工作流设计。OpenMicro 当前的桌面端驱动是 macOS 实现；本程序是面向 Windows 的独立实现。

## 默认映射

| 控件位置 | Xbox | Switch | Codex 操作 |
|---|---:|---:|---|
| South | A | B | 提交或确认 |
| East | B | A | 中断或关闭 |
| North | Y | X | 可选 Typeless 切换听写，或 Codex 默认按住听写 |
| West | X | Y | 单击退格；320ms 内双击则 `Ctrl+A` 全选输入框 |
| 十字键左 | — | — | `Ctrl+B` 切换 Sidebar |
| 十字键右 | — | — | `Ctrl+Alt+B` 显示/隐藏边栏 |
| 十字键上 | — | — | `Ctrl+T` 打开浏览器 |
| 十字键下 | — | — | `` Ctrl+` `` 打开终端 |
| Stadia Capture | — | — | 默认未绑定；恢复后为 `/project` 选择项目 |
| Stadia Assistant | — | — | `/fork`：在新任务中继续当前对话 |
| Stadia View（三点） | Xbox View | — | Stadia：新聊天；Xbox：`/fork` |
| Stadia Menu（三横） | Xbox Menu | — | `/project` 选择项目 |
| Xbox Share | — | — | 新聊天 |
| Stadia 开关机键 | — | — | 默认未绑定；恢复后为新建任务 |
| 左摇杆甩上/下 | — | — | 像鼠标滚轮一样浏览聊天内容 |
| 左摇杆甩左/右 | — | — | 降低 / 提高推理强度（左小右大） |
| 右摇杆四方向 | — | — | 上、下、左、右方向键 |
| L1 | LB | L | `Ctrl+Shift+[` 向左切换侧边栏任务窗口 |
| R1 | RB | R | `Ctrl+Shift+]` 向右切换侧边栏任务窗口 |
| L2 | LT | ZL | `/plan` 切换计划模式 |
| R2 | RT | ZR | `codex://threads/new` 新建任务 |
| R3 | R3 | R Stick | Enter：选中项目并关闭选择菜单 |
| View / Menu | View / Menu | − / + | 见上方按手柄类型区分的映射 |

Stadia 与 Xbox 保持相同的主要位置映射：A 提交、B 取消、Y 语音、X 单击退格/双击全选、Menu 选择项目、L2 计划模式、R2 新建任务。Stadia Assistant 与 Xbox View 用于 `/fork`；Stadia View 与 Xbox Share 用于新聊天。

## Codex 一次性设置

在 **设置 → 键盘快捷键** 中设置：

- Increase reasoning effort：`Ctrl+Alt+=`
- Decrease reasoning effort：`Ctrl+Alt+-`

Typeless 使用 Windows 官方默认全局快捷键 `Right Alt`。请安装并保持 Typeless 在后台运行，光标需要停在 Codex 输入框中。当前电脑尚未检测到 Typeless，录音设备已检测到 `麦克风 (Realtek High Definition Audio)`。

左摇杆上/下会直接向聊天区域发送真实鼠标滚轮事件，不再切换任务，也不需要先进入滚动模式。左/右使用 Codex 中配置的推理强度快捷键。

Stadia Assistant 与 Xbox View 通过 Codex 官方 `/fork` 命令复制当前对话到新任务。Stadia Capture 和 Assistant 不属于 SDL 标准 17 键，桥接器通过 Windows HID 报告单独读取；启动时出现“专用 HID 通道已连接”才表示这两个键可用。

## 使用和配置

双击 `Start-PadMicro-UI.cmd` 打开 `publish` 中的可视化控制台。窗口会自动启动后台手柄桥接；悬停任意按键可查看功能与快捷键，“全部映射”可切换全局预览。关闭主窗口后程序保留在系统托盘，右键托盘图标选择“退出服务”才会完全停止。必须由当前登录的 Windows 用户启动，不能从服务或远程诊断会话启动。

控制台底部的“开机自启”开关可控制当前 Windows 用户登录后是否自动在后台启动 PadMicro。该设置不需要管理员权限。

编辑 `controller-padmicro-profile.json` 可以修改按钮、摇杆手势和工作流提示。Windows 安装包已包含 SDL2 运行库，不需要另外安装 AntiMicroX。

`customShortcuts` 按物理键位保存快捷键覆盖，例如 `"DpadUp": "Ctrl+T"`。控制台中选中任意按键后，也可以在“触发操作”下点击“编辑”修改；留空保存会删除该键位的覆盖并恢复默认操作。“撤销绑定”会让按键停止发送命令，并保留原操作和自定义快捷键，之后可点击“绑定”恢复。

故障排查时双击 `Diagnose-Controller.cmd`，或运行 `publish\PadMicro.exe --check controller-padmicro-profile.json`。
