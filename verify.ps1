param(
    [switch]$SkipShortcut
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$executable = Join-Path $projectRoot 'bin\publish\win-x64\NN Switch.exe'

& (Join-Path $projectRoot 'build.ps1') -SkipShortcut:$SkipShortcut
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

dotnet run `
    --project (Join-Path $projectRoot 'tests\INSwitch.Tests\INSwitch.Tests.csproj') `
    -- `
    --integration $executable

if ($LASTEXITCODE -ne 0) {
    throw "Integration tests failed with exit code $LASTEXITCODE."
}
