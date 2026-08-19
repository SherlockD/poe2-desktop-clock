[CmdletBinding()]
param(
    [switch]$TrustCertificate
)

$ErrorActionPreference = 'Stop'

$packages = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.msix' -File)
if ($packages.Count -ne 1)
{
    throw "Ожидался один MSIX-пакет рядом со скриптом, найдено: $($packages.Count)."
}

if ($TrustCertificate)
{
    $certificates = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cer' -File)
    if ($certificates.Count -ne 1)
    {
        throw "Для доверия ожидался один CER-сертификат рядом со скриптом, найдено: $($certificates.Count)."
    }

    Import-Certificate -FilePath $certificates[0].FullName -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
}

try
{
    Add-AppxPackage -Path $packages[0].FullName
}
catch
{
    if (-not $TrustCertificate)
    {
        throw "Не удалось установить пакет. Если это development-сборка, повторите команду с -TrustCertificate. $($_.Exception.Message)"
    }

    throw
}

Write-Host 'PoE 2 Desktop Clock установлен.'
