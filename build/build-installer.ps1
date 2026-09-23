# 重新生成「好感度监测器 安装程序.exe」和「免安装版.zip」。
# 特点：不带任何 API Key；首次运行由程序自己提示填写。
# 用法: powershell -File build-installer.ps1

$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\91802\Documents\ChatGPT\好感度监测器'
$root = 'C:\Users\91802\CodexTools\crush-monitor'
$stage = Join-Path $root 'build\src'
$payload = Join-Path $root 'build\payload'
$installerDir = Join-Path $root 'build\installer'
$installerSrc = Join-Path $root 'build\installer-src\installer.cs'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

Write-Host '1/5 复制源码到构建目录（排除 .env / node_modules / .git）…'
foreach ($folder in @($stage, $payload)) {
  if (-not $folder.StartsWith($root)) { throw "拒绝操作构建目录以外的路径：$folder" }
  if (Test-Path $folder) {
    # 先摘掉 node_modules 的目录链接（只删链接，不动仓库里的真实目录），再删整棵树
    $link = Join-Path $folder 'node_modules'
    if (Test-Path $link) {
      $item = Get-Item -LiteralPath $link -Force
      if ($item.LinkType -eq 'Junction') { [System.IO.Directory]::Delete($link, $false) }
    }
    [System.IO.Directory]::Delete($folder, $true)
  }
}
New-Item -ItemType Directory -Force -Path $stage, "$payload\app", "$payload\dist", $installerDir | Out-Null
foreach ($file in @((Join-Path $installerDir 'payload.zip'), (Join-Path $root '好感度监测器-安装程序.exe'))) {
  if (Test-Path $file) { [System.IO.File]::Delete($file) }
}
if (-not (Test-Path $installerSrc)) { throw "缺少安装器源码：$installerSrc" }
robocopy $repo $stage /E /XD node_modules .git artifacts dist build /XF .env /NFL /NDL /NJH /NJS /NP | Out-Null

# 覆盖「便携版向导」相关文件（这些改动不进你的仓库，只存在于打包产物里）
$overlay = Join-Path $root 'build\overlay'
if (-not (Test-Path $overlay)) { throw "缺少 overlay 目录：$overlay" }
robocopy $overlay $stage /E /NFL /NDL /NJH /NJS /NP | Out-Null

New-Item -ItemType Junction -Path (Join-Path $stage 'node_modules') -Target (Join-Path $repo 'node_modules') | Out-Null

Write-Host '2/5 构建前端 + 打包服务端…'
Push-Location $stage
try {
  & npx tsc --noEmit | Out-Host
  & npx vite build | Out-Host
  & npx esbuild server/index.ts --bundle --platform=node --format=esm --target=node22 `
      --outfile=build/server.mjs --log-level=warning `
      --banner:js="import { createRequire as __cr } from 'node:module'; const require = __cr(import.meta.url);" | Out-Host
} finally { Pop-Location }

Write-Host '3/5 组装免安装版…'
Copy-Item 'C:\Program Files\nodejs\node.exe' (Join-Path $payload 'node.exe') -Force
Copy-Item (Join-Path $stage 'build\server.mjs') (Join-Path $payload 'app\server.mjs') -Force
Copy-Item -Recurse -Force (Join-Path $stage 'dist\*') (Join-Path $payload 'dist')

# 原生宿主（自己的窗口 + WebView2 渲染；无浏览器窗口、无黑窗口）
$launcherSrc = Join-Path $root 'build\desktop\launcher-webview.cs'
$appIcon = Join-Path $root 'build\desktop\app.ico'
$webviewLib = Join-Path $root 'build\webview2\pkg\lib\net462'
$webviewNative = Join-Path $root 'build\webview2\pkg\runtimes\win-x64\native'
if (-not (Test-Path $launcherSrc)) { throw "缺少启动器源码：$launcherSrc" }
if (-not (Test-Path $webviewLib)) { throw "缺少 WebView2 SDK：$webviewLib" }
& $csc /nologo /target:winexe /platform:x64 "/out:$(Join-Path $payload '好感度监测器.exe')" `
  "/win32icon:$appIcon" "/resource:$appIcon,app.ico" `
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
  "/reference:$webviewLib\Microsoft.Web.WebView2.Core.dll" `
  "/reference:$webviewLib\Microsoft.Web.WebView2.WinForms.dll" `
  $launcherSrc | Out-Host
Copy-Item (Join-Path $webviewLib 'Microsoft.Web.WebView2.Core.dll') $payload -Force
Copy-Item (Join-Path $webviewLib 'Microsoft.Web.WebView2.WinForms.dll') $payload -Force
Copy-Item (Join-Path $webviewNative 'WebView2Loader.dll') $payload -Force
Copy-Item $appIcon $payload -Force
$crlf = "`r`n"
$ascii = New-Object System.Text.UTF8Encoding($false)
$utf8bom = New-Object System.Text.UTF8Encoding($true)
# 停止脚本（正常关窗即可退出，这里只是兜底）
$stopCmd = @('@echo off','powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stop.ps1"','echo.','pause') -join $crlf
$uninstallCmd = @('@echo off','powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"','echo.','pause') -join $crlf
$uninstallPs1 = @'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
# 先停应用窗口，再停它拉起来的后台服务（node 不解掉的话文件删不干净）
Get-Process -Name '好感度监测器' -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch {} }
Get-CimInstance Win32_Process -Filter "Name='node.exe'" |
  Where-Object { $_.CommandLine -like "*$dir*" } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
for ($i = 0; $i -lt 20; $i++) {
  $busy = Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object { $_.CommandLine -like "*$dir*" }
  if (-not $busy) { break }
  Start-Sleep -Milliseconds 300
}
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Crush 好感监控器.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Programs')) 'Crush 好感监控器.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CrushMonitor' -Recurse -Force -ErrorAction SilentlyContinue
Write-Host '快捷方式和卸载信息已清理，正在删除程序文件…'
Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', ('ping -n 3 127.0.0.1 >nul & rmdir /s /q "' + $dir + '"') -WindowStyle Hidden
'@ -join $crlf
$startPs1 = @'
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $dir

# 从 3178 开始找一个没被占用的端口
$port = 3178
while ($port -lt 3278) {
  $busy = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
  if (-not $busy) { break }
  $port++
}
if ($port -ge 3278) { $port = 3178 }

Set-Content -LiteralPath (Join-Path $dir '.port') -Value $port -Encoding ASCII
$env:PORT = "$port"
Write-Host "Crush Monitor 使用端口 $port"
Start-Process -FilePath (Join-Path $dir 'node.exe') -ArgumentList (Join-Path $dir 'app\server.mjs') -WorkingDirectory $dir -WindowStyle Minimized
Start-Sleep -Seconds 3
Start-Process "http://127.0.0.1:$port/"
'@ -join $crlf
$stopPs1 = @'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $dir
$portFile = Join-Path $dir '.port'
$port = 3178
if (Test-Path $portFile) {
  $raw = (Get-Content -LiteralPath $portFile -Raw).Trim()
  if ($raw -match '^\d+$') { $port = [int]$raw }
}

$killed = $false
$conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
foreach ($processId in ($conns | Select-Object -ExpandProperty OwningProcess -Unique)) {
  Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
  Write-Host "已停止进程 $processId（端口 $port）"
  $killed = $true
}
if (-not $killed) {
  Get-CimInstance Win32_Process -Filter "Name='node.exe'" |
    Where-Object { $_.CommandLine -like "*$dir*server.mjs*" } |
    ForEach-Object {
      Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
      Write-Host "已停止进程 $($_.ProcessId)"
      $killed = $true
    }
}
if (-not $killed) { Write-Host '没有在运行的服务' }
Remove-Item -LiteralPath $portFile -Force -ErrorAction SilentlyContinue
'@ -join $crlf
$readme = @(
  'Crush 好感监控器',
  '',
  '【怎么用】',
  '1. 双击桌面上的「Crush 好感监控器」，或者这个文件夹里的「好感度监测器.exe」。',
  '   会打开一个独立的应用窗口，不是浏览器标签页，也没有黑窗口。',
  '2. 第一次打开会让你填一个 TypeSafe API Key（自己去 console.typesafe.ai 注册获取），',
  '   填完点「保存并进入」就能用；Key 只存在这个文件夹里的 .env，不会上传到别处。',
  '3. 用法：把微信聊天记录复制粘贴进窗口，选哪个昵称是你自己，点「分析聊天」。',
  '4. 分析完点右下角「导出结果」，可以导出一份 HTML 报告。',
  '',
  '【怎么退出】',
  '直接关掉应用窗口就行，后台服务会一起退出。（万一没退干净，双击「停止.cmd」收尾。）',
  '',
  '【注意】',
  '- 聊天记录存在浏览器本地，按地址端口分开保存；程序优先用 3178 端口，被占用时会自动换端口，',
  '  换端口后先前记录不会自动带过来。',
  '- 分析时聊天内容会发送到 TypeSafe 的服务器。',
  '- 结果仅供参考娱乐，AI 看不到你们线下的相处。'
) -join $crlf
[System.IO.File]::WriteAllText((Join-Path $payload 'stop.ps1'), $stopPs1, $utf8bom)
[System.IO.File]::WriteAllText((Join-Path $payload '停止.cmd'), $stopCmd, $ascii)
[System.IO.File]::WriteAllText((Join-Path $payload 'uninstall.ps1'), $uninstallPs1, $utf8bom)
[System.IO.File]::WriteAllText((Join-Path $payload '卸载.cmd'), $uninstallCmd, $ascii)
[System.IO.File]::WriteAllText((Join-Path $payload '说明.txt'), $readme, $utf8bom)

$portable = Join-Path $root '好感度监测器-免安装版.zip'
if (Test-Path $portable) { [System.IO.File]::Delete($portable) }
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $portable -CompressionLevel Optimal

Write-Host '4/5 打包安装器载荷…'
$payloadZip = Join-Path $installerDir 'payload.zip'
Copy-Item $portable $payloadZip -Force

Write-Host '5/5 编译安装器…'
# 安装向导（单文件 exe：欢迎页 → 选位置 → 进度条 → 快捷方式 + 卸载注册）
$installerSrc = Join-Path $root 'build\installer-src\installer-wizard.cs'
$setup = Join-Path $root '好感度监测器-安装程序.exe'
if (Test-Path $setup) { [System.IO.File]::Delete($setup) }
& $csc /nologo /target:winexe /platform:anycpu "/out:$setup" `
  "/resource:$payloadZip,payload.zip" `
  /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll `
  /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
  $installerSrc | Out-Host

Write-Host ''
Write-Host "完成："
Write-Host "  安装程序 $setup（$([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB）"
Write-Host "  免安装版 $portable（$([math]::Round((Get-Item $portable).Length / 1MB, 1)) MB）"
Write-Host '提醒：这两个包里都不含 API Key，由用户第一次运行时自己填。'
