# Crush 好感监控器 · Windows 安装包

> 这是**第三方打包**的 Windows 安装程序。源码与版权属于原作者 **[FerryCorleone/crush-monitor](https://github.com/FerryCorleone/crush-monitor)**，本仓库只提供打包好的安装包，方便不想折腾命令行的人一键装上。

## 下载

| 平台 | 文件 | 大小 |
|---|---|---|
| Windows | [Releases](../../releases) 里的 `CrushMonitor-Setup-*.exe`（单文件安装包） | 约 34 MB |
| macOS | [Releases](../../releases) 里的 `CrushMonitor-macOS-*.tar.gz`（含 `.app`） | 约 78 MB |

**macOS 首次打开**：把 `.app` 拖到「应用程序」，然后**右键 → 打开 → 再点「打开」**（未签名应用的常规提示）。如果仍被拦，在终端执行一次
`xattr -dr com.apple.quarantine "/应用程序/好感度监测器.app"`。

## 这是什么

上游项目是一个用 Jev 模型分析聊天记录的小工具：逐句标出情绪与意图、给出好感信号评分、给自己的回复按 SSS–D 分档，并建议下一步怎么聊。它原本需要在终端里依次执行 `npm ci`、`npm run setup`、`npm run build`、`npm start` 才能跑起来，这个仓库把整套东西打成了一个双击就能装的安装包。

## 与上游的差别（只在打包层）

- **单文件安装包**：可选安装位置、带进度条、自动创建桌面和开始菜单快捷方式，可在「设置 → 应用」里正常卸载；装完这个安装包就能删掉
- **独立应用窗口**：原生窗口 + WebView2 渲染，不需要命令行，也不再是浏览器标签页
- **首次运行向导**：选平台（TypeSafe / Vercel AI Gateway / OpenRouter）→ 粘贴自己的 API Key → 真机验证后即可使用
- **端口自动选择**：默认 3178，被占用会自动换；聊天记录保存在程序目录里，绿色版可整体搬走
- **多了一个「导出结果」按钮**：可把聊天原文与全部分析结论导出成一份 HTML 报告
- 上游代码逻辑未做修改；**安装包里不含任何 API Key**，Key 由使用者在首次运行时自己填写

## 使用步骤

1. 下载并双击安装程序。若出现「Windows 已保护你的电脑」，点「更多信息 → 仍要运行」（未签名程序的常规提示）
2. 选择安装位置 → 安装 → 程序自动打开
3. 第一次打开会让你选平台并粘贴自己的 API Key

Key 需要自己准备：TypeSafe 目前是邀请／候补制，也可以用 OpenRouter 或 Vercel AI Gateway 的 Jev 接入。费用、额度与隐私以自己的账号和所选平台为准。

## 原作者与许可

- 上游项目：[FerryCorleone/crush-monitor](https://github.com/FerryCorleone/crush-monitor)，MIT 许可
- 版权归原作者及贡献者所有。本仓库按 MIT 许可分发打包产物，未包含任何密钥或用户数据
- 本项目与微信、腾讯、TypeSafe 均无隶属关系
- 打包脚本与改动源码见 [`build/`](build/)（桌面宿主与安装向导），其余为上游源码本身

## 免责声明

- 本仓库是**非官方**打包，与原作者、TypeSafe 之间没有隶属或背书关系；原作者如果要求，可随时删除本仓库及发布物
- 软件按「现状」提供，不提供任何明示或暗示的担保；因使用本程序产生的后果（包括但不限于 API 额度消耗、数据丢失、判断误差）由使用者自行承担
- 分析结果由 AI 生成，仅供娱乐参考，不构成任何情感、心理或法律建议
- 使用时会把你粘贴的聊天内容发送到你选择的模型平台（TypeSafe / Vercel / OpenRouter），请自行确认隐私与合规
- 安装包未做代码签名，Windows 可能提示「未知发布者」
