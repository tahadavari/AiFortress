[CmdletBinding()]
param(
    [ValidatePattern('\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z')]
    [string]$Version = '1.0.0',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repo 'artifacts\release'
}
$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$temporaryRoot = (Get-Item -LiteralPath ([IO.Path]::GetTempPath())).FullName
$stage = (New-Item -ItemType Directory -Path (Join-Path $temporaryRoot "ClaudeGuard-$([guid]::NewGuid())")).FullName

try {
    $project = Join-Path $repo 'src\ClaudeGuard\ClaudeGuard.csproj'
    $publish = Join-Path $stage 'publish'
    dotnet publish $project -c Release -r win-x64 --self-contained -o $publish `
        -p:Version=$Version -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    $layout = New-Item -ItemType Directory -Path (Join-Path $stage 'package')
    $app = New-Item -ItemType Directory -Path (Join-Path $layout.FullName 'app')
    Copy-Item -LiteralPath (Join-Path $publish 'ClaudeGuard.exe') -Destination $app.FullName
    foreach ($file in 'install.bat', 'uninstall.bat', 'setup.ps1') {
        Copy-Item -LiteralPath (Join-Path $repo "packaging\$file") -Destination $layout.FullName
    }
    Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination $layout.FullName
    Copy-Item -LiteralPath (Join-Path $repo 'README.fa.md') -Destination $layout.FullName

    $name = "ClaudeGuard-$Version-win-x64.zip"
    $zip = Join-Path $stage $name
    $inputs = Get-ChildItem -LiteralPath $layout.FullName | Select-Object -ExpandProperty FullName
    Compress-Archive -LiteralPath $inputs -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$zip.sha256", "$hash  $name`n", [Text.Encoding]::ASCII)
    Move-Item -LiteralPath $zip, "$zip.sha256" -Destination $output -Force
    Write-Host "Release package: $(Join-Path $output $name)"
}
finally {
    $resolved = Get-Item -LiteralPath $stage -ErrorAction SilentlyContinue
    if ($resolved -and $resolved.Parent.FullName -eq $temporaryRoot -and
        $resolved.Name -match '\AClaudeGuard-[0-9a-f-]{36}\z') {
        Remove-Item -LiteralPath $resolved.FullName -Recurse -Force
    }
}
