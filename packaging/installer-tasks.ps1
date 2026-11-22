param(
    [Parameter(Mandatory)]
    [ValidateSet('Prepare', 'Register', 'Cleanup')]
    [string]$Action,
    [Parameter(Mandatory)]
    [string]$InstallDirectory,
    [switch]$StartAtLogon
)

$ErrorActionPreference = 'Stop'
$taskName = 'AiFortress'
$taskPath = '\'
$programFiles = [Environment]::GetFolderPath('ProgramFiles')
$installedExe = Join-Path $InstallDirectory 'AiFortress.exe'
$previousExe = Join-Path $programFiles 'AiFortress\AiFortress.exe'
$legacyExe = Join-Path $programFiles 'ClaudeGuard\ClaudeGuard.exe'

function Get-ApplicationTask($Name, $Paths) {
    Get-ScheduledTask -TaskName $Name -TaskPath $taskPath -ErrorAction SilentlyContinue |
        Where-Object { $_.Actions | Where-Object { $_.Execute -and $_.Execute.Trim('"') -in $Paths } }
}

function Stop-Application($Programs, $Tasks) {
    foreach ($task in $Tasks) {
        if ($task.State -eq 'Running') {
            Stop-ScheduledTask -InputObject $task
        }
    }
    foreach ($process in Get-Process -Name AiFortress, ClaudeGuard -ErrorAction SilentlyContinue) {
        if ($process.Path -in $Programs) {
            Stop-Process -InputObject $process
            if (-not $process.WaitForExit(5000)) { throw 'A previous copy is still running.' }
        }
    }
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Administrator privileges are required.'
    }

    $registered = Get-ApplicationTask $taskName @($installedExe, $previousExe)
    switch ($Action) {
        'Prepare' {
            $legacyTask = Get-ApplicationTask 'ClaudeGuard' @($legacyExe)
            Stop-Application @($installedExe, $previousExe, $legacyExe) @($registered; $legacyTask)
        }
        'Register' {
            if ($StartAtLogon) {
                if ($installedExe -ne $previousExe) {
                    throw 'Install in Program Files to enable startup at sign-in.'
                }
                if (-not $registered -and (Get-ScheduledTask -TaskName $taskName -TaskPath $taskPath -ErrorAction SilentlyContinue)) {
                    throw 'An unrelated scheduled task already uses the AiFortress name.'
                }
                $launch = New-ScheduledTaskAction -Execute $installedExe -Argument '--background'
                $trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
                $owner = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Highest
                $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
                Register-ScheduledTask -TaskName $taskName -TaskPath $taskPath -Action $launch -Trigger $trigger `
                    -Principal $owner -Settings $settings -Force | Out-Null
            } elseif ($registered) {
                Unregister-ScheduledTask -InputObject $registered -Confirm:$false
            }

            $legacyTask = Get-ApplicationTask 'ClaudeGuard' @($legacyExe)
            if ($legacyTask) { Unregister-ScheduledTask -InputObject $legacyTask -Confirm:$false }
        }
        'Cleanup' {
            Stop-Application @($installedExe) @($registered)
            if (Test-Path -LiteralPath $installedExe -PathType Leaf) {
                $cleanup = Start-Process -FilePath $installedExe -ArgumentList '--remove-protection' -WindowStyle Hidden -Wait -PassThru
                if ($cleanup.ExitCode -ne 0) { throw "Protection cleanup returned $($cleanup.ExitCode)." }
            }
            if ($registered) { Unregister-ScheduledTask -InputObject $registered -Confirm:$false }
        }
    }
    exit 0
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
