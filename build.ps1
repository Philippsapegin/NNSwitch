param(
    [switch]$SkipTests,
    [switch]$SkipShortcut,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot

Push-Location -LiteralPath $projectRoot
try {
    if (-not $SkipTests) {
        dotnet run --project '.\tests\INSwitch.Tests\INSwitch.Tests.csproj'
        if ($LASTEXITCODE -ne 0) {
            throw "Tests failed with exit code $LASTEXITCODE."
        }
    }

    $publishArguments = @(
        'publish',
        '.\NNSwitch.csproj',
        '--configuration',
        'Release',
        '--property:PublishProfile=win-x64'
    )
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $publishArguments += "--property:Version=$Version"
    }

    dotnet @publishArguments

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed with exit code $LASTEXITCODE."
    }

    $publishDirectory = Join-Path $projectRoot 'bin\publish\win-x64'
    $executable = Get-Item -LiteralPath (Join-Path $publishDirectory 'NN Switch.exe')

    Write-Host "Built: $($executable.FullName)"
    if (-not $SkipShortcut) {
        $shortcutPath = Join-Path $projectRoot 'NN Switch.lnk'
        $shortcutShell = New-Object -ComObject WScript.Shell
        $shortcut = $shortcutShell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $executable.FullName
        $shortcut.WorkingDirectory = $publishDirectory
        $shortcut.IconLocation = "$(Join-Path $projectRoot 'NN.ico'),0"
        $shortcut.Description = 'NN Switch'
        $shortcut.Save()
        Write-Host "Shortcut: $shortcutPath"
    }
}
finally {
    Pop-Location
}
