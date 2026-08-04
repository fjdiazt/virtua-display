$ErrorActionPreference = 'Stop'

$manifest = Join-Path $PSScriptRoot 'payload.sha256'
foreach ($line in Get-Content -LiteralPath $manifest) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }

    $parts = $line -split '\s+', 2
    if ($parts.Count -ne 2) {
        throw "Invalid payload manifest line: $line"
    }

    $expected, $relative = $parts
    $path = Join-Path $PSScriptRoot $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing payload file: $relative"
    }

    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -ne $expected) { throw "SHA-256 mismatch: $relative" }
}

