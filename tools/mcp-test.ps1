# Проверка MCP-сервера FlowBar настоящими вызовами: tools\mcp-test.ps1 (панель должна быть запущена)
$exe = "$env:LOCALAPPDATA\Programs\FlowBar\FlowBar.exe"
$psi = New-Object Diagnostics.ProcessStartInfo $exe, '--mcp'
$psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.CreateNoWindow = $true
$psi.StandardOutputEncoding = [Text.Encoding]::UTF8
$p = [Diagnostics.Process]::Start($psi)
$in = New-Object IO.StreamWriter($p.StandardInput.BaseStream, (New-Object Text.UTF8Encoding $false)); $in.AutoFlush = $true
function Call($obj) { $in.WriteLine(($obj | ConvertTo-Json -Compress -Depth 6)); $p.StandardOutput.ReadLine() | ConvertFrom-Json }
function Tool($name, $a) { $r = Call @{ jsonrpc = '2.0'; id = (Get-Random); method = 'tools/call'; params = @{ name = $name; arguments = $a } }; "$name -> " + $(if ($r.result.isError) { 'ОШИБКА: ' } else { '' }) + $r.result.content[0].text }
$i = Call @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'test'; version = '1' } } }
"initialize -> " + $i.result.serverInfo.name + " " + $i.result.serverInfo.version + ", протокол " + $i.result.protocolVersion
$in.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
$l = Call @{ jsonrpc = '2.0'; id = 2; method = 'tools/list' }
"tools/list -> " + $l.result.tools.Count + " инструментов: " + (($l.result.tools | ForEach-Object name) -join ', ')
Tool 'notify' @{ title = 'Проверка агентов'; text = 'MCP-сервер FlowBar работает' }
Tool 'add_reminder' @{ text = 'проверка MCP, можно удалить'; in_minutes = 90 }
Tool 'list_reminders' @{}
Tool 'cancel_reminder' @{ text = 'проверка MCP' }
Tool 'list_reminders' @{}
Tool 'add_reminder' @{ text = 'без времени' }
Tool 'list_events' @{}
(Tool 'list_tasks' @{}).Split("`n").Count.ToString() + " строк в ответе list_tasks"
$in.Close(); if (-not $p.WaitForExit(3000)) { $p.Kill(); 'сервер не завершился сам' } else { "сервер завершился, код " + $p.ExitCode }
