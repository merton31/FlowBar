using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LumaDock;

internal sealed class RadioController
{
    internal enum State { Unknown, On, Off, Disabled, Missing }
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly Dictionary<string, State> states = new(StringComparer.Ordinal);
    public State Read(string label) => states.GetValueOrDefault(label);

    public async Task<string?> Refresh(string? label = null, bool? enabled = null)
    {
        string? kind = label switch { "Wi-Fi" => "WiFi", "Bluetooth" => "Bluetooth", _ => null };
        if (enabled.HasValue && kind is null) return "This radio cannot be controlled here.";
        await operation.WaitAsync();
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-STA", "-Command" })
                start.ArgumentList.Add(argument);
            start.ArgumentList.Add(enabled is bool on ? Script.Replace("# SET RADIO",
                "$target = '" + kind + "'\n$desired = '" + (on ? "On" : "Off") + "'\n" + ToggleScript, StringComparison.Ordinal) : Script);
            using var process = Process.Start(start) ?? throw new InvalidOperationException();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var read = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var errors = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                await process.WaitForExitAsync(deadline.Token);
                var lines = (await read).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                await errors;
                if (process.ExitCode != 0 || !lines.Contains("RADIOS_OK")) return "Windows radio controls are unavailable.";
                Update("Wi-Fi", "WiFi", lines);
                Update("Bluetooth", "Bluetooth", lines);
                string? outcome = lines.LastOrDefault(value => value.StartsWith("RADIO_SET:", StringComparison.Ordinal))?[10..];
                if (!enabled.HasValue) return null;
                return outcome switch
                {
                    "Allowed" when Read(label!) == (enabled.Value ? State.On : State.Off) => null,
                    "Allowed" => "Windows accepted the request. Refresh to confirm the resulting state.",
                    "Missing" => "Windows does not report this radio.",
                    "DeniedByUser" or "DeniedBySystem" => "Windows denied permission to change this radio.",
                    _ => "Windows could not change this radio."
                };
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill();
                ErrorLog.Note("Radios", "Windows radio control did not respond in time");
                return "Windows radio control did not respond.";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex, "Radios");
            return "Windows radio controls are unavailable.";
        }
        finally { operation.Release(); }
    }

    private void Update(string label, string kind, string[] response)
    {
        var readings = response.Where(line => line.StartsWith(kind + ":", StringComparison.Ordinal))
            .Select(line => Enum.TryParse<State>(line[(kind.Length + 1)..], out var state) ? state : State.Unknown).ToArray();
        states[label] = readings.Length == 0 ? State.Missing : readings.Contains(State.On) ? State.On
            : readings.Contains(State.Off) ? State.Off : readings[0];
    }

    private const string Script = """
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $null = [Windows.Devices.Radios.Radio, Windows.Devices.Radios, ContentType=WindowsRuntime]
        $null = [Windows.Devices.Radios.RadioState, Windows.Devices.Radios, ContentType=WindowsRuntime]
        $null = [Windows.Devices.Radios.RadioAccessStatus, Windows.Devices.Radios, ContentType=WindowsRuntime]
        $convert = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
          $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Length -eq 1 -and
          $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
        } | Select-Object -First 1
        function AwaitResult($asyncOperation, [Type]$type) {
          $task = $convert.MakeGenericMethod($type).Invoke($null, @($asyncOperation))
          $task.GetAwaiter().GetResult()
        }
        $listType = [System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]]
        $radios = @(AwaitResult ([Windows.Devices.Radios.Radio]::GetRadiosAsync()) $listType)
        # SET RADIO
        foreach ($radio in $radios) { '{0}:{1}' -f $radio.Kind, $radio.State }
        'RADIOS_OK'
        """;

    private const string ToggleScript = """
        $matching = @($radios | Where-Object { $_.Kind.ToString() -eq $target -and $_.State.ToString() -ne 'Disabled' })
        $status = 'Missing'
        if ($matching.Count -gt 0) {
          $access = AwaitResult ([Windows.Devices.Radios.Radio]::RequestAccessAsync()) ([Windows.Devices.Radios.RadioAccessStatus])
          $status = $access.ToString()
          if ($status -eq 'Allowed') {
            foreach ($radio in $matching) {
              $state = [Windows.Devices.Radios.RadioState]$desired
              if ($radio.State -eq $state) { continue }
              $result = AwaitResult ($radio.SetStateAsync($state)) ([Windows.Devices.Radios.RadioAccessStatus])
              if ($result.ToString() -ne 'Allowed') { $status = $result.ToString() }
            }
          }
        }
        'RADIO_SET:' + $status
        """;
}
