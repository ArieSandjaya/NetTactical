<#
 Makes a computer trust files signed with the NetTactical certificate: the certificate goes into Trusted Root Certification Authorities and
 Trusted Publishers of the computer. Needs an elevated shell. Safe to run again.
 Whoever holds the private key (.pfx) can then sign programs these computers accept, so keep that file safe.

 To get the text for -CerBase64:  [Convert]::ToBase64String([IO.File]::ReadAllBytes("nettactical-signing.cer"))
 To roll it out with NetTactical: Scripts > new script (PowerShell) with this content and the base64 text filled in, then run it as a bulk action.
#>
param([Parameter(Mandatory)][string]$CerBase64)
$ErrorActionPreference = 'Stop'
$tmp = Join-Path $env:TEMP 'nettactical-signing.cer'
[IO.File]::WriteAllBytes($tmp, [Convert]::FromBase64String($CerBase64))
try {
  $c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $tmp
  foreach ($store in 'Root', 'TrustedPublisher') {
    $s = New-Object System.Security.Cryptography.X509Certificates.X509Store $store, 'LocalMachine'
    $s.Open('ReadWrite'); $s.Add($c); $s.Close()
  }
  Write-Host "Trusted: $($c.Subject) ($($c.Thumbprint))"
}
finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
