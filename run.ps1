[CmdletBinding()]
param(
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'Poe2DeskTracker.csproj'
$taskNugetCache = 'C:\Temp\Poe2DeskTrackerNuGet'
$taskIntermediate = 'C:\Temp\Poe2DeskTrackerBuild\obj\'
$taskExtensions = 'C:\Temp\Poe2DeskTrackerBuild\msbuild\'
$buildProperties = @(
    "-p:BaseIntermediateOutputPath=$taskIntermediate",
    "-p:MSBuildProjectExtensionsPath=$taskExtensions",
    '-p:Platform=x64'
)

$env:NUGET_PACKAGES = $taskNugetCache

dotnet restore $projectPath @buildProperties
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($BuildOnly) {
    dotnet build $projectPath --no-restore @buildProperties
}
else {
    dotnet run --project $projectPath --no-restore @buildProperties
}

exit $LASTEXITCODE
