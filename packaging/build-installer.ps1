[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot 'artifacts'
$publish = Join-Path $artifacts 'publish'
$setup = Join-Path $artifacts "VirtuaDisplay-Setup-$Version.exe"

if (-not ([IO.Path]::GetFullPath($publish).StartsWith(
    [IO.Path]::GetFullPath($repoRoot) + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase))) {
    throw 'Publish path escaped the repository.'
}

Push-Location $repoRoot
try {
    & (Join-Path $PSScriptRoot 'verify-payload.ps1')

    if (Test-Path -LiteralPath $publish) {
        Remove-Item -LiteralPath $publish -Recurse -Force
    }

    dotnet publish .\src\Virtua.Display\Virtua.Display.csproj `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishReadyToRun=false -p:DebugType=None -p:SatelliteResourceLanguages=en `
        -p:Version=$Version `
        -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $iscc = $command.Source }
    }
    if (-not $iscc) {
        throw 'Inno Setup 6 is required: winget install --id JRSoftware.InnoSetup'
    }

    & $iscc "/Qp" "/DMyAppVersion=$Version" .\packaging\VirtuaDisplay.iss
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
    if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
        throw "Expected setup was not created: $setup"
    }

    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
    Write-Host "Installer: $setup"
    Write-Host "SHA-256: $hash"
}
finally {
    Pop-Location
}
