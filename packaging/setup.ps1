param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$taskName = 'ClaudeGuard'
$installDirectory = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ClaudeGuard'
$installedExe = Join-Path $installDirectory 'ClaudeGuard.exe'

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script as administrator.'
    }

    $sourceExe = Join-Path $PSScriptRoot 'app\ClaudeGuard.exe'
    if (-not $Uninstall -and -not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
        throw 'Extract the entire release ZIP before running install.bat.'
    }

    $task = Get-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction SilentlyContinue
    if ($task -and $task.State -eq 'Running') {
        Stop-ScheduledTask -InputObject $task
    }
    foreach ($process in Get-Process -Name ClaudeGuard -ErrorAction SilentlyContinue) {
        if ($process.Path -eq $installedExe) {
            Stop-Process -InputObject $process -ErrorAction Stop
            if (-not $process.WaitForExit(5000)) { throw 'ClaudeGuard is still running.' }
        }
    }

    if ($Uninstall) {
        # Prefer the current package so upgrades from versions without a cleanup command also work.
        $cleanupExe = $sourceExe
        if (-not (Test-Path -LiteralPath $cleanupExe -PathType Leaf)) {
            $cleanupExe = $installedExe
        }
        if (Test-Path -LiteralPath $cleanupExe -PathType Leaf) {
            $cleanup = Start-Process -FilePath $cleanupExe -ArgumentList '--remove-protection' -WindowStyle Hidden -Wait -PassThru
            if ($cleanup.ExitCode -ne 0) {
                throw "Cleanup returned exit code $($cleanup.ExitCode). Exit any running copy of ClaudeGuard and retry."
            }
        }
        if ($task) { Unregister-ScheduledTask -InputObject $task -Confirm:$false }
        if (Test-Path -LiteralPath $installedExe) { Remove-Item -LiteralPath $installedExe }
        if ((Test-Path -LiteralPath $installDirectory) -and -not (Get-ChildItem -LiteralPath $installDirectory -Force)) {
            Remove-Item -LiteralPath $installDirectory
        }
        Write-Host 'ClaudeGuard removed. Settings remain in ProgramData for a future installation.'
        exit 0
    }

    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force
    $action = New-ScheduledTaskAction -Execute $installedExe
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
    $owner = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -TaskPath '\' -Action $action -Trigger $trigger `
        -Principal $owner -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName -TaskPath '\'
    Write-Host 'ClaudeGuard installed. Look for the shield in the notification area.'
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
