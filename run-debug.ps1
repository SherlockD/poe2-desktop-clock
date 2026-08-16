[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'src\Poe2DesktopClock.ConsoleDebug\Poe2DesktopClock.ConsoleDebug.csproj'
$env:NUGET_PACKAGES = 'C:\Temp\Poe2DesktopClockNuGet'

dotnet run --project $projectPath '-p:Platform=x64'
exit $LASTEXITCODE
