param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$taskName = 'AiFortress'
$programFiles = [Environment]::GetFolderPath('ProgramFiles')
$installDirectory = Join-Path $programFiles 'AiFortress'
$installedExe = Join-Path $installDirectory 'AiFortress.exe'

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script as administrator.'
    }

    $sourceExe = Join-Path $PSScriptRoot 'app\AiFortress.exe'
    if (-not $Uninstall -and -not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
        throw 'Extract the entire release ZIP before running install.bat.'
    }

    $task = Get-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction SilentlyContinue
    if ($task -and $task.State -eq 'Running') {
        Stop-ScheduledTask -InputObject $task
    }
    $legacyExe = Join-Path $programFiles 'ClaudeGuard\ClaudeGuard.exe'
    $legacyTask = $null
    if (-not $Uninstall) {
        $legacyTask = Get-ScheduledTask -TaskName 'ClaudeGuard' -TaskPath '\' -ErrorAction SilentlyContinue |
            Where-Object { $_.Actions | Where-Object { $_.Execute -and $_.Execute.Trim('"') -eq $legacyExe } }
        if ($legacyTask -and $legacyTask.State -eq 'Running') {
            Stop-ScheduledTask -InputObject $legacyTask
        }
    }

    foreach ($process in Get-Process -Name AiFortress, ClaudeGuard -ErrorAction SilentlyContinue) {
        if ($process.Path -eq $installedExe -or (-not $Uninstall -and $process.Path -eq $legacyExe)) {
            Stop-Process -InputObject $process -ErrorAction Stop
            if (-not $process.WaitForExit(5000)) { throw 'A previous copy is still running.' }
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
                throw "Cleanup returned exit code $($cleanup.ExitCode). Exit any running copy of AiFortress and retry."
            }
        }
        if ($task) { Unregister-ScheduledTask -InputObject $task -Confirm:$false }
        if (Test-Path -LiteralPath $installedExe) { Remove-Item -LiteralPath $installedExe }
        if ((Test-Path -LiteralPath $installDirectory) -and -not (Get-ChildItem -LiteralPath $installDirectory -Force)) {
            Remove-Item -LiteralPath $installDirectory
        }
        Write-Host 'AiFortress removed. Settings remain in ProgramData for a future installation.'
        exit 0
    }

    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -LiteralPath $sourceExe -Destination $installedExe -Force
    $action = New-ScheduledTaskAction -Execute $installedExe -Argument '--background'
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
    $owner = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -TaskPath '\' -Action $action -Trigger $trigger `
        -Principal $owner -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName -TaskPath '\'
    if ($legacyTask) { Unregister-ScheduledTask -InputObject $legacyTask -Confirm:$false }
    Write-Host 'AiFortress installed. Click the fortress icon in the notification area.'
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
