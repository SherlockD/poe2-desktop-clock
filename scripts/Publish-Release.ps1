[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutputDirectory = 'artifacts\release',

    [string]$CertificatePath,

    [string]$CertificatePassword,

    [switch]$GenerateDevelopmentCertificate
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-NormalizedVersion
{
    param([string]$Value)

    $withoutPrefix = $Value.Trim().TrimStart('v', 'V')
    $core = ($withoutPrefix -split '[-+]')[0]
    $parsed = [Version]::Parse($core)
    $build = if ($parsed.Build -ge 0) { $parsed.Build } else { 0 }
    $revision = if ($parsed.Revision -ge 0) { $parsed.Revision } else { 0 }
    $parts = @($parsed.Major, $parsed.Minor, $build, $revision)
    if ($parts | Where-Object { $_ -lt 0 -or $_ -gt 65535 })
    {
        throw "MSIX version parts must be between 0 and 65535: $Value"
    }

    return [pscustomobject]@{
        Package = $parts -join '.'
        DotNet = "$($parsed.Major).$($parsed.Minor).$build"
    }
}

function Find-WindowsSdkTool
{
    param([string]$Name)

    $fromPath = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $fromPath)
    {
        return $fromPath.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidates = @(Get-ChildItem -Path $kitsRoot -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } |
        Sort-Object FullName -Descending)
    if ($candidates.Count -eq 0)
    {
        throw "$Name was not found. Install the Windows SDK or MSIX Packaging Tools."
    }

    return $candidates[0].FullName
}

function Write-ResizedPng
{
    param(
        [string]$Source,
        [string]$Destination,
        [int]$Size
    )

    Add-Type -AssemblyName System.Drawing
    $sourceImage = [System.Drawing.Image]::FromFile($Source)
    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try
    {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try
        {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($sourceImage, 0, 0, $Size, $Size)
        }
        finally
        {
            $graphics.Dispose()
        }

        $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally
    {
        $bitmap.Dispose()
        $sourceImage.Dispose()
    }
}

function New-DevelopmentCertificate
{
    param(
        [string]$Path,
        [string]$Password
    )

    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    try
    {
        $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=Poe2DesktopClock',
            $rsa,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $request.CertificateExtensions.Add(
            [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $request.CertificateExtensions.Add(
            [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
                [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,
                $true))
        $oids = [System.Security.Cryptography.OidCollection]::new()
        [void]$oids.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
        $request.CertificateExtensions.Add(
            [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids, $true))
        $request.CertificateExtensions.Add(
            [System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false))

        $certificate = $request.CreateSelfSigned(
            [DateTimeOffset]::UtcNow.AddDays(-1),
            [DateTimeOffset]::UtcNow.AddYears(2))
        try
        {
            [System.IO.File]::WriteAllBytes(
                $Path,
                $certificate.Export(
                    [System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx,
                    $Password))
        }
        finally
        {
            $certificate.Dispose()
        }
    }
    finally
    {
        $rsa.Dispose()
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory))
{
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
else
{
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}
$versionInfo = Get-NormalizedVersion $Version
$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$workingRoot = [System.IO.Path]::GetFullPath((Join-Path $tempBase "Poe2DesktopClock-$([Guid]::NewGuid().ToString('N'))"))
if (-not $workingRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase))
{
    throw "Refusing to use a working directory outside the system temp path: $workingRoot"
}

$publishRoot = Join-Path $workingRoot 'publish'
$packageRoot = Join-Path $workingRoot 'package'
$assetsRoot = Join-Path $packageRoot 'Assets'
$projectPath = Join-Path $repoRoot 'src\Poe2DesktopClock.Desktop\Poe2DesktopClock.Desktop.csproj'
$manifestTemplatePath = Join-Path $repoRoot 'packaging\AppxManifest.xml'
$sourceLogoPath = Join-Path $repoRoot 'src\Poe2DesktopClock.Infrastructure.Windows\Assets\Poe2DesktopClock.png'
$installerPath = Join-Path $repoRoot 'packaging\Install-Poe2DesktopClock.ps1'

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $publishRoot, $packageRoot, $assetsRoot -Force | Out-Null

$effectiveCertificatePath = $CertificatePath
$effectiveCertificatePassword = $CertificatePassword
$generatedCertificatePath = $null

try
{
    if ([string]::IsNullOrWhiteSpace($effectiveCertificatePath))
    {
        if (-not $GenerateDevelopmentCertificate)
        {
            throw 'Provide -CertificatePath or use -GenerateDevelopmentCertificate.'
        }

        $generatedCertificatePath = Join-Path $workingRoot 'development-signing.pfx'
        $effectiveCertificatePassword = [Guid]::NewGuid().ToString('N')
        New-DevelopmentCertificate -Path $generatedCertificatePath -Password $effectiveCertificatePassword
        $effectiveCertificatePath = $generatedCertificatePath
    }
    elseif (-not [System.IO.Path]::IsPathRooted($effectiveCertificatePath))
    {
        $effectiveCertificatePath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $effectiveCertificatePath))
    }

    if (-not (Test-Path -LiteralPath $effectiveCertificatePath -PathType Leaf))
    {
        throw "Signing certificate not found: $effectiveCertificatePath"
    }

    & dotnet publish $projectPath `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:Platform=x64 `
        -p:Version=$($versionInfo.DotNet) `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishReadyToRun=false `
        -o $publishRoot
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $portablePath = Join-Path $outputRoot "Poe2DesktopClock-$($versionInfo.Package)-win-x64-portable.zip"
    Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $portablePath -CompressionLevel Optimal -Force

    Copy-Item -Path (Join-Path $publishRoot '*') -Destination $packageRoot -Recurse -Force
    Write-ResizedPng -Source $sourceLogoPath -Destination (Join-Path $assetsRoot 'StoreLogo.png') -Size 50
    Write-ResizedPng -Source $sourceLogoPath -Destination (Join-Path $assetsRoot 'Square44x44Logo.png') -Size 44
    Write-ResizedPng -Source $sourceLogoPath -Destination (Join-Path $assetsRoot 'Square150x150Logo.png') -Size 150

    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $effectiveCertificatePath,
        $effectiveCertificatePassword,
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    try
    {
        $publisher = [System.Security.SecurityElement]::Escape($certificate.Subject)
        $manifest = Get-Content -LiteralPath $manifestTemplatePath -Raw
        $manifest = $manifest.Replace('__VERSION__', $versionInfo.Package)
        $manifest = $manifest.Replace('__PUBLISHER__', $publisher)
        Set-Content -LiteralPath (Join-Path $packageRoot 'AppxManifest.xml') -Value $manifest -Encoding utf8

        $publicCertificatePath = Join-Path $outputRoot "Poe2DesktopClock-$($versionInfo.Package).cer"
        [System.IO.File]::WriteAllBytes(
            $publicCertificatePath,
            $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    }
    finally
    {
        $certificate.Dispose()
    }

    $makeAppx = Find-WindowsSdkTool 'makeappx.exe'
    $signTool = Find-WindowsSdkTool 'signtool.exe'
    $msixPath = Join-Path $outputRoot "Poe2DesktopClock-$($versionInfo.Package)-win-x64.msix"
    & $makeAppx pack /d $packageRoot /p $msixPath /o
    if ($LASTEXITCODE -ne 0)
    {
        throw "MakeAppx failed with exit code $LASTEXITCODE."
    }

    & $signTool sign /fd SHA256 /f $effectiveCertificatePath /p $effectiveCertificatePassword $msixPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "SignTool failed with exit code $LASTEXITCODE."
    }

    Copy-Item -LiteralPath $installerPath -Destination (Join-Path $outputRoot 'Install-Poe2DesktopClock.ps1') -Force
    $checksumPath = Join-Path $outputRoot 'SHA256SUMS.txt'
    $checksumFiles = Get-ChildItem -LiteralPath $outputRoot -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' }
    $checksums = $checksumFiles | Sort-Object Name | ForEach-Object {
        $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        "$($hash.Hash.ToLowerInvariant()) *$($_.Name)"
    }
    Set-Content -LiteralPath $checksumPath -Value $checksums -Encoding ascii

    Write-Host "Release artifacts written to $outputRoot"
}
finally
{
    if ((Test-Path -LiteralPath $workingRoot) -and
        $workingRoot.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase))
    {
        Remove-Item -LiteralPath $workingRoot -Recurse -Force
    }
}
