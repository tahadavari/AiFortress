[CmdletBinding()]
param(
    [ValidatePattern('\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z')]
    [string]$Version = '1.1.0',
    [string]$OutputDirectory,
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repo "artifacts\release\$Version"
}
if (-not $InnoCompiler) {
    $InnoCompiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    if (-not $InnoCompiler) {
        $locations = @(
            [Environment]::GetFolderPath('ProgramFilesX86')
            [Environment]::GetFolderPath('ProgramFiles')
            (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs')
        )
        $InnoCompiler = $locations | ForEach-Object { Join-Path $_ 'Inno Setup 6\ISCC.exe' } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw 'Install Inno Setup 6 or provide the path to ISCC.exe via -InnoCompiler.'
}
$InnoCompiler = (Resolve-Path -LiteralPath $InnoCompiler).Path
$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$temporaryRoot = (Get-Item -LiteralPath ([IO.Path]::GetTempPath())).FullName
$stage = (New-Item -ItemType Directory -Path (Join-Path $temporaryRoot "AiFortress-$([guid]::NewGuid())")).FullName

try {
    $project = Join-Path $repo 'src\AiFortress\AiFortress.csproj'
    $publish = Join-Path $stage 'publish'
    dotnet publish $project -c Release -r win-x64 --self-contained -o $publish `
        -p:Version=$Version -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    $binary = Join-Path $publish 'AiFortress.exe'
    $installer = Join-Path $repo 'packaging\AiFortress.iss'
    $versionNumber = ($Version -split '-', 2)[0]
    & $InnoCompiler "/DReleaseVersion=$Version" "/DReleaseVersionNumber=$versionNumber" "/DAppBinary=$binary" "/O$stage" $installer
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }

    $artifacts = @("AiFortress-$Version-setup-win-x64.exe", "AiFortress-$Version-portable-win-x64.exe")
    Copy-Item -LiteralPath $binary -Destination (Join-Path $stage $artifacts[1])
    $sums = foreach ($name in $artifacts) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $stage $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $name"
    }
    [IO.File]::WriteAllText((Join-Path $stage 'SHA256SUMS.txt'), ($sums -join "`n") + "`n", [Text.Encoding]::ASCII)
    foreach ($name in ($artifacts + 'SHA256SUMS.txt')) {
        Move-Item -LiteralPath (Join-Path $stage $name) -Destination $output -Force
        Write-Host (Join-Path $output $name)
    }
}
finally {
    $resolved = Get-Item -LiteralPath $stage -ErrorAction SilentlyContinue
    if ($resolved -and $resolved.Parent.FullName -eq $temporaryRoot -and
        $resolved.Name -match '\AAiFortress-[0-9a-f-]{36}\z') {
        Remove-Item -LiteralPath $resolved.FullName -Recurse -Force
    }
}
