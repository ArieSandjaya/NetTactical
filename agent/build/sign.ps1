# Signs the given files with the certificate in SIGN_PFX_BASE64 / SIGN_PFX_PASSWORD. Used by the agent workflow; does nothing when the secrets are not set.
param([Parameter(Mandatory)][string[]]$Files)
$ErrorActionPreference = 'Stop'
if (-not $env:SIGN_PFX_BASE64) { Write-Host 'No signing secret set: files stay unsigned.'; return }
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName | Select-Object -Last 1
if (-not $signtool) { throw 'signtool.exe not found on this runner' }
$pfx = Join-Path $env:RUNNER_TEMP 'signing.pfx'
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SIGN_PFX_BASE64))
try {
  foreach ($f in $Files) {
    & $signtool.FullName sign /fd SHA256 /f $pfx /p $env:SIGN_PFX_PASSWORD /tr http://timestamp.digicert.com /td SHA256 /d 'NetTactical Agent' $f
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for $f" }
    $sig = Get-AuthenticodeSignature $f
    if (-not $sig.SignerCertificate) { throw "$f has no signature after signing" }
    Write-Host "Signed $f by $($sig.SignerCertificate.Subject)"
  }
}
finally { Remove-Item $pfx -Force -ErrorAction SilentlyContinue }
