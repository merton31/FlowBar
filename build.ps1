# Сборка FlowBar.exe встроенным компилятором .NET Framework (ничего ставить не нужно)
$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$wm  = "$env:WINDIR\System32\WinMetadata"   # WinRT: медиа, радиомодули
# FlowBar.manifest — масштаб по мониторам; рядом с exe должен лежать FlowBar.exe.config (включает пересчёт WPF при смене масштаба)
& "$fw\csc.exe" /nologo /target:winexe /optimize /out:"$PSScriptRoot\FlowBar.exe" /win32manifest:"$PSScriptRoot\FlowBar.manifest" `
  /r:"$wpf\PresentationFramework.dll" /r:"$wpf\PresentationCore.dll" /r:"$wpf\WindowsBase.dll" /r:"$wpf\UIAutomationProvider.dll" /r:"$wpf\UIAutomationTypes.dll" `
  /r:System.Xaml.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Microsoft.VisualBasic.dll /r:System.Management.dll /r:"$fw\System.Runtime.dll" /r:"$fw\System.Runtime.InteropServices.WindowsRuntime.dll" `
  /r:"$wm\Windows.Foundation.winmd" /r:"$wm\Windows.Media.winmd" /r:"$wm\Windows.Devices.winmd" /r:"$wm\Windows.Storage.winmd" /r:"$wm\Windows.Networking.winmd" /r:"$wm\Windows.UI.winmd" /r:System.Numerics.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:"$fw\System.Numerics.Vectors.dll" `
  "$PSScriptRoot\FlowBar.cs" "$PSScriptRoot\FlowBar.Design.cs" "$PSScriptRoot\FlowBar.Cards.cs" "$PSScriptRoot\FlowBar.Glass.cs" "$PSScriptRoot\FlowBar.Tray.cs" "$PSScriptRoot\FlowBar.Update.cs"

# Сторож на Rust (необязательно): собирается, если установлен Rust. Без него FlowBar использует сторожа на .NET.
$cargo = "$env:USERPROFILE\.cargo\bin\cargo.exe"
if (Test-Path $cargo) {
  & $cargo build --release --quiet --manifest-path "$PSScriptRoot\watch\Cargo.toml"
  if ($LASTEXITCODE -eq 0) { Copy-Item "$PSScriptRoot\watch\target\release\flowbar-watch.exe" "$PSScriptRoot\flowbar-watch.exe" -Force }
}
