<#
.SYNOPSIS
    Builds ClickSaver2026 into Build\: the native AOT projects (32-bit) and the WPF app and tests.
.DESCRIPTION
    The runnable app ends up in Build\<Configuration>\ClickSaver2026.exe with the hook beside it.
    The hook and the test harness are C# compiled with Native AOT to native 32-bit binaries, so the
    Visual Studio C++ build tools must be installed (Native AOT uses the Microsoft linker).
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

# Native AOT invokes the Microsoft linker, which finds the toolset through vswhere.
$installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if (Test-Path $installer) { $env:PATH = "$installer;$env:PATH" }

function Invoke-Step([string] $name, [scriptblock] $step) {
    Write-Host "== $name" -ForegroundColor Cyan
    & $step
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE" }
}

if ($Clean -and (Test-Path $build)) {
    Write-Host "== Clean" -ForegroundColor Cyan
    Remove-Item $build -Recurse -Force
}

# The hook, published straight into the app's output folder so it ships beside the exe.
Invoke-Step 'Publish hook' { dotnet publish (Join-Path $root 'ClickSaver2026.Hook') -c $Configuration -o (Join-Path $build $Configuration) }

# The stand-in client the hook tests attach to. MessageProtocol first: HookHost links its import lib.
Invoke-Step 'Publish MessageProtocol' { dotnet publish (Join-Path $root 'ClickSaver2026.HookHarness\MessageProtocol') -c Release -o (Join-Path $build 'Harness') }
Invoke-Step 'Publish HookHost' { dotnet publish (Join-Path $root 'ClickSaver2026.HookHarness\HookHost') -c Release -o (Join-Path $build 'Harness') }

Invoke-Step 'Build app' { dotnet build (Join-Path $root 'ClickSaver2026.slnx') -c $Configuration }

if ($Test) {
    Invoke-Step 'Test' { dotnet test --solution (Join-Path $root 'ClickSaver2026.slnx') -c $Configuration --no-build }
}

Write-Host "Done: $(Join-Path $build "$Configuration\ClickSaver2026.exe")" -ForegroundColor Green
