# Сборка FlowBar.exe встроенным компилятором .NET Framework (ничего ставить не нужно)
param([string]$OutputPath = "$PSScriptRoot\FlowBar.exe")

$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$wm  = "$env:WINDIR\System32\WinMetadata"   # WinRT: медиа, радиомодули
$previewAssets = @('light','dark','color') | ForEach-Object { "/resource:$PSScriptRoot\assets\preview\$_.jpg,FlowBar.Preview.$_.jpg" }
$elementalAssets = Get-ChildItem -LiteralPath "$PSScriptRoot\assets\elemental" -Filter '*.png' -File | ForEach-Object { "/resource:$($_.FullName),FlowBar.Elemental.$($_.Name)" }
$musicSource = "$PSScriptRoot\FlowBar.MusicArt.cs"
$reportSource = "$PSScriptRoot\FlowBar.BugReport.cs"
# FlowBar.manifest — масштаб по мониторам; рядом с exe должен лежать FlowBar.exe.config (включает пересчёт WPF при смене масштаба)
& "$fw\csc.exe" /nologo /target:winexe /optimize /unsafe "/out:$OutputPath" "/resource:$PSScriptRoot\CHANGELOG.md,FlowBar.CHANGELOG.md" "/resource:$PSScriptRoot\assets\Glass.hlsl,FlowBar.Glass.hlsl" $previewAssets $elementalAssets /win32manifest:"$PSScriptRoot\FlowBar.manifest" /win32icon:"$PSScriptRoot\assets\FlowBar.ico" $musicSource $reportSource `
  /r:"$wpf\PresentationFramework.dll" /r:"$wpf\PresentationCore.dll" /r:"$wpf\WindowsBase.dll" /r:"$wpf\UIAutomationProvider.dll" /r:"$wpf\UIAutomationTypes.dll" /r:"$wpf\UIAutomationClient.dll" `
  /r:System.Xaml.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Microsoft.VisualBasic.dll /r:System.Management.dll /r:"$fw\System.Runtime.dll" /r:"$fw\System.Runtime.InteropServices.WindowsRuntime.dll" `
  /r:"$wm\Windows.Foundation.winmd" /r:"$wm\Windows.Graphics.winmd" /r:"$wm\Windows.Media.winmd" /r:"$wm\Windows.Devices.winmd" /r:"$wm\Windows.Storage.winmd" /r:"$wm\Windows.Networking.winmd" /r:"$wm\Windows.UI.winmd" /r:System.Numerics.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:"$fw\System.Numerics.Vectors.dll" `
  "$PSScriptRoot\FlowBar.cs" "$PSScriptRoot\FlowBar.Design.cs" "$PSScriptRoot\FlowBar.MenuMotion.cs" "$PSScriptRoot\FlowBar.Cards.cs" "$PSScriptRoot\FlowBar.ThemeCarousel.cs" "$PSScriptRoot\FlowBar.StyleMixer.cs" "$PSScriptRoot\FlowBar.SpotifyNext.cs" "$PSScriptRoot\FlowBar.Indicators.cs" "$PSScriptRoot\FlowBar.Glass.cs" "$PSScriptRoot\FlowBar.Refraction.cs" "$PSScriptRoot\FlowBar.LensWindow.cs" "$PSScriptRoot\FlowBar.GpuLens.cs" "$PSScriptRoot\FlowBar.Tray.cs" "$PSScriptRoot\FlowBar.Update.cs" "$PSScriptRoot\FlowBar.Agent.cs" "$PSScriptRoot\FlowBar.Sliders.cs" "$PSScriptRoot\FlowBar.Appearance.cs" "$PSScriptRoot\FlowBar.Themes.cs"
if ($LASTEXITCODE -ne 0) { throw "Не удалось собрать FlowBar.exe (код $LASTEXITCODE)" }

# Сторож на Rust (необязательно): собирается, если установлен Rust. Без него FlowBar использует сторожа на .NET.
$cargo = "$env:USERPROFILE\.cargo\bin\cargo.exe"
if (Test-Path $cargo) {
  & $cargo build --release --quiet --manifest-path "$PSScriptRoot\watch\Cargo.toml"
  if ($LASTEXITCODE -eq 0) { Copy-Item "$PSScriptRoot\watch\target\release\flowbar-watch.exe" "$PSScriptRoot\flowbar-watch.exe" -Force }
}
