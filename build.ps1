param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot

Push-Location -LiteralPath $projectRoot
try {
    dotnet run --project '.\tools\IconBuilder\IconBuilder.csproj' -- '.\NN.png' '.\NN.ico'
    if ($LASTEXITCODE -ne 0) {
        throw "Icon generation failed with exit code $LASTEXITCODE."
    }

    if (-not $SkipTests) {
        dotnet run --project '.\tests\INSwitch.Tests\INSwitch.Tests.csproj'
        if ($LASTEXITCODE -ne 0) {
            throw "Tests failed with exit code $LASTEXITCODE."
        }
    }

    dotnet publish '.\NNSwitch.csproj' `
        --configuration Release `
        --property:PublishProfile=win-x64

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed with exit code $LASTEXITCODE."
    }

    $publishDirectory = Join-Path $projectRoot 'bin\publish\win-x64'
    $executable = Get-Item -LiteralPath (Join-Path $publishDirectory 'NN Switch.exe')

    $shortcutPath = Join-Path $projectRoot 'NN Switch.lnk'
    $shortcutShell = New-Object -ComObject WScript.Shell
    $shortcut = $shortcutShell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable.FullName
    $shortcut.WorkingDirectory = $publishDirectory
    $shortcut.IconLocation = "$(Join-Path $projectRoot 'NN.ico'),0"
    $shortcut.Description = 'NN Switch'
    $shortcut.Save()

    Write-Host "Built: $($executable.FullName)"
    Write-Host "Shortcut: $shortcutPath"
}
finally {
    Pop-Location
}
