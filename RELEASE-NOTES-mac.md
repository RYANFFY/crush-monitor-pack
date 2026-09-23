macOS 版（第三方打包），基于上游 `FerryCorleone/crush-monitor` v1.1.1。

**源码与版权属于原作者：[FerryCorleone/crush-monitor](https://github.com/FerryCorleone/crush-monitor)。** 本仓库只发布打包产物。

**怎么用**

1. 下载 `CrushMonitor-macOS-*.tar.gz`，双击解压得到「好感度监测器-macOS」文件夹
2. 把里面的「好感度监测器.app」拖到「应用程序」（或就地使用）
3. **第一次打开要右键 → 打开 → 再点一次「打开」**（未签名应用的常规提示；也可在终端执行 `xattr -dr com.apple.quarantine "/应用程序/好感度监测器.app"`）
4. 程序会打开一个没有地址栏的窗口，第一次让你选平台（TypeSafe / Vercel / OpenRouter）并粘贴自己的 API Key，验证通过即可使用

**这个包的特点**

- 原生 `.app`（自带图标、Dock 里就是它自己），不是命令行脚本
- 内置 Node 运行时（Apple 芯片 / Intel 两种架构自动选择），**不需要自己装任何东西**
- 应用包内置了上游代码 + 首次运行向导 + 「导出结果」按钮（导出 HTML 分析报告）
- 端口自动选择；Key 与聊天记录放在 `~/Library/Application Support/CrushMonitor/`
- **不含任何 API Key**，Key 需要自己申请

**说明**：本包是在 Windows 上制作、通过结构/权限/图标/脚本语法校验的，但没有在真实 Mac 上跑过；如果遇到问题请提 issue。

**免责声明**：非官方打包，与原作者及 TypeSafe 无隶属关系；软件按「现状」提供、不提供担保，使用后果自负；分析结果由 AI 生成，仅供娱乐参考；聊天内容会发送到你所选的模型平台；应用未做苹果签名与公证，首次打开需要手动放行。

**SHA256**

```
B291616C64B21BE90904B7DAD952555508DEF2E2E81C3DB9003A329B5CB25446
```
