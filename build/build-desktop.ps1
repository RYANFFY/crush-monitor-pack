# 生成「好感度监测器-桌面版.zip」：独立应用窗口（Edge 应用模式），绿色免安装，不含 API Key。
# 依赖 build-installer.ps1 先生成 build\payload（服务端单文件 + 前端 dist）。
# 用法: powershell -File build-desktop.ps1

$ErrorActionPreference = 'Stop'
$root = 'C:\Users\91802\CodexTools\crush-monitor'
$build = Join-Path $root 'build'
$payload = Join-Path $build 'payload'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path (Join-Path $payload 'app\server.mjs'))) { throw '先运行 build-installer.ps1 生成 build\payload' }

Write-Host '1/2 打包绿色版（直接用 payload，和安装程序同一份文件）…'
$pkgRoot = Join-Path $build 'desktop-package'
if (Test-Path $pkgRoot) { [System.IO.Directory]::Delete($pkgRoot, $true) }
New-Item -ItemType Directory -Force -Path $pkgRoot | Out-Null
$out = Join-Path $pkgRoot '好感度监测器'
Copy-Item $payload $out -Recurse -Force

Write-Host '2/2 压缩…'
$zip = Join-Path $root '好感度监测器-桌面版.zip'
if (Test-Path $zip) { [System.IO.File]::Delete($zip) }
Compress-Archive -Path $out -DestinationPath $zip -CompressionLevel Optimal

Write-Host '4/4 校验…'
$envFile = 'C:\Users\91802\Documents\ChatGPT\好感度监测器\.env'
if (Test-Path $envFile) {
  try {
    $keys = @()
    foreach ($pattern in '^TYPESAFE_API_KEY=(.*)$', '^JEV_API_KEY=(.*)$', '^OPENROUTER_API_KEY=(.*)$') {
      $match = Select-String -Path $envFile -Pattern $pattern
      if ($match) { $keys += $match.Matches[0].Groups[1].Value.Trim().Trim("'") }
    }
    $keys = $keys | Where-Object { $_ }
    $bytes = [System.IO.File]::ReadAllBytes($zip)
    $text = [System.Text.Encoding]::Latin1.GetString($bytes)
    $hit = $false
    foreach ($key in $keys) { if ($text.Contains($key)) { $hit = $true } }
    Write-Host ("  包内是否含你的任何 Key：{0}（应为 False）" -f $hit)
  } catch {
    Write-Host "  Key 校验跳过：$($_.Exception.Message)"
  }
}
Write-Host ''
Write-Host "完成：$zip（$([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB）"
