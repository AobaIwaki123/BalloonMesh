# Builds all device projects and runs the native unit tests.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Push-Location $root
try {
    foreach ($project in @('balloon', 'anchor', 'gateway')) {
        Write-Host "=== $project ==="
        Push-Location (Join-Path $root $project)
        try { pio run } finally { Pop-Location }
    }

    Write-Host '=== native tests ==='
    if (Test-Path 'C:\msys64\mingw64\bin') {
        $env:PATH = "C:\msys64\mingw64\bin;$env:PATH"
    }
    Push-Location (Join-Path $root 'tests')
    try { pio test -e native } finally { Pop-Location }
}
finally {
    Pop-Location
}
