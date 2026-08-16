[CmdletBinding()]
param(
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$solutionPath = Join-Path $PSScriptRoot 'Poe2DesktopClock.sln'
$desktopProjectPath = Join-Path $PSScriptRoot 'src\Poe2DesktopClock.Desktop\Poe2DesktopClock.Desktop.csproj'
$taskNugetCache = 'C:\Temp\Poe2DesktopClockNuGet'
$buildProperties = @('-p:Platform=x64')

$env:NUGET_PACKAGES = $taskNugetCache

dotnet restore $solutionPath @buildProperties
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($BuildOnly) {
    dotnet build $solutionPath --no-restore @buildProperties
}
else {
    dotnet run --project $desktopProjectPath --no-restore @buildProperties
}

exit $LASTEXITCODE
