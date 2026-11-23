<#
 Makes a self-signed code signing certificate for the NetTactical agent. Run it once, on your own computer, in PowerShell.
 It writes three files to the current folder:
   nettactical-signing.pfx          the private key. Keep it secret. Do not commit it.
   nettactical-signing.pfx.base64   the same file as one line of text, for the GitHub secret SIGN_PFX_BASE64
   nettactical-signing.cer          the public certificate, for Trust-NetTacticalSigner.ps1 (safe to share)
 Then add two repository secrets (GitHub: Settings > Secrets and variables > Actions): SIGN_PFX_BASE64 (contents of the .base64 file)
 and SIGN_PFX_PASSWORD (the password you type here).
#>
param([string]$Subject = 'CN=NetTactical Agent, O=NetTactical', [int]$Years = 5)
$ErrorActionPreference = 'Stop'
$pw = Read-Host 'Password for the .pfx file' -AsSecureString
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
  -KeyExportPolicy Exportable -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears($Years)
try {
  Export-PfxCertificate -Cert $cert -FilePath .\nettactical-signing.pfx -Password $pw | Out-Null
  Export-Certificate -Cert $cert -FilePath .\nettactical-signing.cer | Out-Null
  [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path .\nettactical-signing.pfx))) | Set-Content -NoNewline .\nettactical-signing.pfx.base64
}
finally { Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue }   # the key now lives only in the .pfx
Write-Host "Done. Valid until $($cert.NotAfter.ToString('yyyy-MM-dd')). Thumbprint: $($cert.Thumbprint)"
