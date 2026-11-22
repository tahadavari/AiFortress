param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
$release = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\release'
$installer = Join-Path $release "AiFortress-$Version-setup-win-x64.exe"
$portable = Join-Path $release "AiFortress-$Version-portable-win-x64.exe"
$target = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'AiFortress'
$expectedTask = 'AiFortress'

function Invoke-SilentSetup($Executable, $Log, $Extra) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$Log`"") + $Extra
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $Log) { Get-Content -LiteralPath $Log -Tail 60 | Write-Host }
        throw "Setup returned exit code $($process.ExitCode)."
    }
}

if (Test-Path -LiteralPath $target) {
    throw 'The installer smoke test requires a Windows runner with no existing AiFortress installation.'
}

$log = Join-Path ([IO.Path]::GetTempPath()) "aifortress-install-$Version.log"
Invoke-SilentSetup $installer $log @('/TASKS=startatlogon')
$app = Join-Path $target 'AiFortress.exe'
$uninstaller = (Get-ChildItem -LiteralPath $target -Filter 'unins*.exe' -File).FullName

try {
    if ((Get-FileHash -LiteralPath $app).Hash -ne (Get-FileHash -LiteralPath $portable).Hash) {
        throw 'The installer extracted a different application than the portable EXE.'
    }
    $task = Get-ScheduledTask -TaskName $expectedTask -TaskPath '\'
    if ($task.Actions.Execute -ne $app -or $task.Actions.Arguments -ne '--background') {
        throw 'The startup task does not point to the installed application.'
    }
}
finally {
    $log = Join-Path ([IO.Path]::GetTempPath()) "aifortress-uninstall-$Version.log"
    Invoke-SilentSetup $uninstaller $log @()
}

if ((Test-Path -LiteralPath $app) -or
    (Get-ScheduledTask -TaskName $expectedTask -TaskPath '\' -ErrorAction SilentlyContinue)) {
    throw 'The uninstall left behind the executable or startup task.'
}

Write-Host 'Install, portable match, startup task and uninstall verified.'
