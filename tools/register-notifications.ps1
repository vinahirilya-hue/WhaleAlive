param([string]$AppDirectory = (Join-Path $PSScriptRoot '..\prototype\bin\Release\net9.0-windows'), [switch]$TrustLocalCertificate)
$ErrorActionPreference = 'Stop'
$petDirectory = (Resolve-Path -LiteralPath $AppDirectory).Path
$petManifest = Join-Path $petDirectory 'Identity\AppxManifest.xml'
if (!(Test-Path -LiteralPath (Join-Path $petDirectory 'WhaleAlive.exe'))) { throw 'Build WhaleAlive before registering.' }
if (!(Test-Path -LiteralPath $petManifest)) { throw 'Identity manifest is missing. Rebuild the application.' }
$petPackage = Join-Path $petDirectory 'WhaleAlive.Identity.msix'
$petPublicCertificate = Join-Path $petDirectory 'WhaleAlive.Local.cer'
if (!(Test-Path -LiteralPath $petPackage)) { throw 'Run tools/build-notification-package.ps1 first.' }
# Trust only when explicitly requested. Never change Developer Mode or notification privacy settings.
if ($TrustLocalCertificate) { Import-Certificate -FilePath $petPublicCertificate -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Select-Object Subject,Thumbprint }
Add-AppxPackage -Path $petPackage -ExternalLocation $petDirectory
Write-Output 'Whale Alive identity registered. Restart the pet, then click Enable notification access and answer the Windows permission prompt.'
