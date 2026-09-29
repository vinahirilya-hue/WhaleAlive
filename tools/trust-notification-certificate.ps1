param(
 [Parameter(Mandatory=$true)][string]$CertificatePath,
 [Parameter(Mandatory=$true)][string]$ExpectedThumbprint,
 [Parameter(Mandatory=$true)][string]$ResultPath
)
$ErrorActionPreference = 'Stop'
try {
 $petCertificate = Get-PfxCertificate -FilePath $CertificatePath
 if ($petCertificate.Subject -ne 'CN=WhaleAlive.Local' -or $petCertificate.Thumbprint -ne $ExpectedThumbprint) { throw 'Certificate does not match the approved local project certificate.' }
 Import-Certificate -FilePath $CertificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
 @{ success = $true; thumbprint = $petCertificate.Thumbprint; store = 'LocalMachine/TrustedPeople' } | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
} catch {
 @{ success = $false; error = $_.Exception.Message } | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
 exit 1
}
