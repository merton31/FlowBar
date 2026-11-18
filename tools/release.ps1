# Выпуск FlowBar: сборка одним файлом, архив FlowBar.zip и описание из CHANGELOG.md.
# Запуск: tools\release.ps1 -Version 2.0.1 (после слияния в main). Обновление FlowBar читает последний выпуск: тег vX.Y.Z, архив FlowBar*.zip с FlowBar.exe, сумму SHA-256 сообщает GitHub.
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$gh = if (Test-Path 'C:\Program Files\GitHub CLI\gh.exe') { 'C:\Program Files\GitHub CLI\gh.exe' } else { 'gh' }
$vm = [regex]::Match($Version, '^(\d+\.\d+\.\d+)([a-z]?)$')
if (-not $vm.Success) { throw "версия должна быть вида 2.0.1 или 2.0.1a, а не $Version" }
# тег ставится на main: проверяем, что собираем именно его и что дерево чистое
& git -C $root fetch origin main --quiet
if (& git -C $root status --porcelain --untracked-files=no) { throw "есть незакоммиченные изменения — закоммитьте или уберите их перед выпуском" }
if ((& git -C $root rev-parse HEAD) -ne (& git -C $root rev-parse origin/main)) { throw "HEAD не совпадает с origin/main — слейте PR и обновите main (git pull)" }
$tmp = Join-Path $env:TEMP "flowbar-release-$Version"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
$build = Join-Path $tmp 'build'
& dotnet publish (Join-Path $root 'source\FlowBar.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:TreatWarningsAsErrors=true -o $build
if ($LASTEXITCODE -ne 0) { throw "сборка не удалась" }
$exe = Join-Path $build 'FlowBar.exe'
# версия exe: 2.0.1 → 2.0.1.0, 2.0.1a → 2.0.1.1
$expected = $vm.Groups[1].Value + '.' + $(if ($vm.Groups[2].Value) { [int][char]$vm.Groups[2].Value[0] - 96 } else { 0 })
$built = (Get-Item $exe).VersionInfo.FileVersion
if ($built -ne $expected) { throw "FlowBar.exe версии $built, а выпуск $Version (ждали $expected)" }
# проверки на собранном файле
foreach ($mode in '--smoke', '--audit-ui', '--check-install', '--check-dictation', '--check-places', '--check-jumplist', '--preview-glass', '--check-live') {
    $folder = Join-Path $tmp ('check' + $mode)
    New-Item -ItemType Directory $folder | Out-Null
    $p = Start-Process $exe -ArgumentList $mode, $folder -PassThru
    if (-not $p.WaitForExit(300000)) { $p.Kill(); throw "$mode: не завершилась за 5 минут" }
    $last = (Get-Content (Join-Path $folder 'result.txt') -Tail 1 -ErrorAction SilentlyContinue)
    if ($last -ne 'OK') { throw "$mode не прошла: $last" }
}
$dir = New-Item -ItemType Directory -Force (Join-Path $tmp 'FlowBar')
Copy-Item $exe $dir
$zip = Join-Path $tmp 'FlowBar.zip'
Compress-Archive -Path $dir -DestinationPath $zip -Force
# раздел этой версии из CHANGELOG.md
$lines = Get-Content (Join-Path $root 'CHANGELOG.md') -Encoding UTF8
$start = ($lines | Select-String -Pattern "^## $([regex]::Escape($Version))\b" | Select-Object -First 1).LineNumber
if (-not $start) { throw "в CHANGELOG.md нет раздела $Version" }
$body = @(); for ($i = $start; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^## ') { break }; $body += $lines[$i] }
$notes = "Что нового:`n" + (($body -join "`n").Trim()) + "`n`nУстановка: распакуйте FlowBar.zip и запустите FlowBar.exe. Обновление: «Настройки» → «Общие» → «Проверить обновления»."
$nf = Join-Path $tmp 'notes.md'; [IO.File]::WriteAllText($nf, $notes, (New-Object Text.UTF8Encoding $false))
& $gh release create "v$Version" $zip --repo merton31/FlowBar --target main --title "FlowBar $Version" --notes-file $nf
"zip: $zip  ($((Get-Item $zip).Length) байт), sha256: " + (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
