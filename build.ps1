<#
.SYNOPSIS
    Builds ClickSaver2026: the 32-bit hook DLL with CMake, then the app and tests with dotnet.
.EXAMPLE
    ./build.ps1
    ./build.ps1 -Configuration Debug -Test
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Test
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$hookBuild = Join-Path $root 'build/hook'

function Invoke-Step([string] $name, [scriptblock] $step) {
    Write-Host "== $name" -ForegroundColor Cyan
    & $step
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE" }
}

Invoke-Step 'Configure hook' { cmake -S (Join-Path $root 'src/ClickSaver2026.Hook') -B $hookBuild -A Win32 }
Invoke-Step 'Build hook' { cmake --build $hookBuild --config $Configuration }

# The stand-in client the end-to-end test attaches to.
$harnessBuild = Join-Path $root 'build/harness'
Invoke-Step 'Configure harness' { cmake -S (Join-Path $root 'tests/HookHarness') -B $harnessBuild -A Win32 }
Invoke-Step 'Build harness' { cmake --build $harnessBuild --config Release }
Invoke-Step 'Build app' { dotnet build (Join-Path $root 'ClickSaver2026.slnx') -c $Configuration }

if ($Test) {
    Invoke-Step 'Test' { dotnet test --solution (Join-Path $root 'ClickSaver2026.slnx') -c $Configuration --no-build }
}

$output = Join-Path $root "src/ClickSaver2026.App/bin/$Configuration/net10.0-windows"
Write-Host "Done: $output\ClickSaver2026.exe" -ForegroundColor Green
