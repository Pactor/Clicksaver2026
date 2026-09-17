<#
.SYNOPSIS
    Builds ClickSaver2026 into Build\: the 32-bit hook DLL with CMake, then the app and tests.
.DESCRIPTION
    The runnable app ends up in Build\<Configuration>\ClickSaver2026.exe with the hook beside it.
.EXAMPLE
    ./build.ps1
    ./build.ps1 -Configuration Debug -Test
    ./build.ps1 -Clean
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Test,
    [switch] $Clean
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root 'Build'

function Invoke-Step([string] $name, [scriptblock] $step) {
    Write-Host "== $name" -ForegroundColor Cyan
    & $step
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE" }
}

if ($Clean -and (Test-Path $build)) {
    Write-Host "== Clean" -ForegroundColor Cyan
    Remove-Item $build -Recurse -Force
}

$hookTree = Join-Path $build 'cmake/hook'
Invoke-Step 'Configure hook' { cmake -S (Join-Path $root 'ClickSaver2026.Hook') -B $hookTree -A Win32 "-DCLICKSAVER_OUTPUT_DIR=$build" }
Invoke-Step 'Build hook' { cmake --build $hookTree --config $Configuration }

# The stand-in client the end-to-end tests attach to.
$harnessTree = Join-Path $build 'cmake/harness'
Invoke-Step 'Configure harness' { cmake -S (Join-Path $root 'ClickSaver2026.HookHarness') -B $harnessTree -A Win32 "-DCLICKSAVER_OUTPUT_DIR=$(Join-Path $build 'Harness')" }
Invoke-Step 'Build harness' { cmake --build $harnessTree --config Release }

Invoke-Step 'Build app' { dotnet build (Join-Path $root 'ClickSaver2026.slnx') -c $Configuration }

if ($Test) {
    Invoke-Step 'Test' { dotnet test --solution (Join-Path $root 'ClickSaver2026.slnx') -c $Configuration --no-build }
}

Write-Host "Done: $(Join-Path $build "$Configuration\ClickSaver2026.exe")" -ForegroundColor Green
