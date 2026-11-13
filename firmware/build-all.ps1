# Builds all device projects and runs the native unit tests.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Invoke-Pio {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & pio @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "pio $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

Push-Location $root
try {
    foreach ($project in @('balloon', 'anchor', 'gateway')) {
        Write-Host "=== $project ==="
        Push-Location (Join-Path $root $project)
        try { Invoke-Pio @('run') } finally { Pop-Location }
    }

    Write-Host '=== native tests ==='
    if (Test-Path 'C:\msys64\mingw64\bin') {
        $env:PATH = "C:\msys64\mingw64\bin;$env:PATH"
    }
    if (-not (Get-Command gcc -ErrorAction SilentlyContinue)) {
        throw 'Native tests need gcc. Install MSYS2 and add C:\msys64\mingw64\bin to PATH.'
    }
    Push-Location (Join-Path $root 'tests')
    try { Invoke-Pio @('test', '-e', 'native') } finally { Pop-Location }
}
finally {
    Pop-Location
}
