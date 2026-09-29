param([string]$AppDirectory = (Join-Path $PSScriptRoot '..\prototype\bin\Release\net9.0-windows'))
$ErrorActionPreference = 'Stop'
$petDirectory = (Resolve-Path -LiteralPath $AppDirectory).Path
$petSdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$petSdk = Get-ChildItem -LiteralPath $petSdkRoot -Directory | Sort-Object Name -Descending | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makeappx.exe') } | Select-Object -First 1
if (!$petSdk) { throw 'Windows SDK MakeAppx.exe was not found.' }
$petPackage = Join-Path $petDirectory 'WhaleAlive.Identity.msix'
$petBrandAssets = Join-Path $petDirectory 'Identity\Assets\Brand'
New-Item -ItemType Directory -Path $petBrandAssets -Force | Out-Null
Copy-Item -Path (Join-Path $petDirectory 'Assets\Brand\*.png') -Destination $petBrandAssets -Force
& (Join-Path $petSdk.FullName 'x64\makeappx.exe') pack /o /nv /d (Join-Path $petDirectory 'Identity') /p $petPackage
if ($LASTEXITCODE -ne 0) { throw 'Could not build the local identity package.' }
$petCertificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=WhaleAlive.Local' -and $_.FriendlyName -eq 'Whale Alive local development' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(7) } | Select-Object -First 1
if (!$petCertificate) {
 $petCertificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=WhaleAlive.Local' -FriendlyName 'Whale Alive local development' -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(1)
}
# Keep the private key in the local user key store; export the public certificate only.
Export-Certificate -Cert $petCertificate -FilePath (Join-Path $petDirectory 'WhaleAlive.Local.cer') | Out-Null
& (Join-Path $petSdk.FullName 'x64\signtool.exe') sign /fd SHA256 /s My /sha1 $petCertificate.Thumbprint $petPackage
if ($LASTEXITCODE -ne 0) { throw 'Could not sign the local identity package.' }
Write-Output "Prepared signed local package. Public certificate: $($petCertificate.Thumbprint). Not added to TrustedPeople."
