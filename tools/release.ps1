# Выпуск FlowBar: архив FlowBar.zip и описание из CHANGELOG.md. Запуск: tools\release.ps1 -Version 1.6.0 (после сборки и слияния в main)
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
$tmp = Join-Path $env:TEMP "flowbar-release-$Version"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
$dir = New-Item -ItemType Directory -Force (Join-Path $tmp 'FlowBar')
foreach ($f in 'FlowBar.exe', 'FlowBar.exe.config', 'flowbar-watch.exe', 'Как пользоваться.txt') {
    $src = Join-Path $root $f
    if (Test-Path $src) { Copy-Item $src $dir } elseif ($f -like 'FlowBar.exe*') { throw "нет файла $f" }
}
$built = (Get-Item (Join-Path $dir 'FlowBar.exe')).VersionInfo.FileVersion
# номер вида 1.6.7 или 1.6.7a (мелкая правка): буква a = 1, b = 2 хранится четвёртым числом версии exe
$vm = [regex]::Match($Version, '^(\d+\.\d+\.\d+)([a-z]?)$')
if (-not $vm.Success) { throw "версия должна быть вида 1.6.7 или 1.6.7a, а не $Version" }
$expected = $vm.Groups[1].Value + '.' + $(if ($vm.Groups[2].Value) { [int][char]$vm.Groups[2].Value[0] - 96 } else { 0 })
if ($built -ne $expected) { throw "FlowBar.exe версии $built, а выпуск $Version (ждали $expected)" }
$zip = Join-Path $tmp 'FlowBar.zip'
Compress-Archive -Path $dir -DestinationPath $zip -Force
# раздел этой версии из CHANGELOG.md
$lines = Get-Content (Join-Path $root 'CHANGELOG.md') -Encoding UTF8
$start = ($lines | Select-String -Pattern "^## $([regex]::Escape($Version))\b" | Select-Object -First 1).LineNumber
if (-not $start) { throw "в CHANGELOG.md нет раздела $Version" }
$body = @(); for ($i = $start; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^## ') { break }; $body += $lines[$i] }
$notes = "Что нового:`n" + (($body -join "`n").Trim()) + "`n`nОбновление: «Настройки FlowBar» → «Общие» → «Проверить обновления» → «Скачать и установить». Настройки и записи сохраняются."
$nf = Join-Path $tmp 'notes.md'; [IO.File]::WriteAllText($nf, $notes, (New-Object Text.UTF8Encoding $false))
& $gh release create "v$Version" $zip --repo merton31/FlowBar --target main --title "FlowBar $Version" --notes-file $nf
"zip: $zip  ($((Get-Item $zip).Length) байт), sha256: " + (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
