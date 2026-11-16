$ErrorActionPreference = 'Stop'
$pkg = Split-Path -Parent $MyInvocation.MyCommand.Path
$msix = "$pkg\MinePix.msix"
if (-not (Test-Path $msix)) {
    throw ("" + $msix + " not found - run Build-Msix.ps1 first")
}

# tools are the NuGet-shipped Windows SDK Build Tools
$tools = "$env:NUGET_PACKAGES\microsoft.windows.sdk.buildtools\10.0.22621.3233\bin\10.0.22621.0\x64"
if (-not $env:NUGET_PACKAGES) {
    $tools = 'D:\nuget\packages\microsoft.windows.sdk.buildtools\10.0.22621.3233\bin\10.0.22621.0\x64'
}
if ($env:MIXSDK_TOOLS) { $tools = $env:MIXSDK_TOOLS }

Write-Output 'Looking for (or creating) a matching self-signed developer certificate in CurrentUser\My...'
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=MinePix Dev' } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=MinePix Dev' `
        -KeyUsage DigitalSignature -KeySpec Signature -KeyExportPolicy Exportable `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3,1.3.6.1.4.1.311.10.3.13')
    Write-Output 'Created self-signed developer certificate.'
} else {
    Write-Output 'Reusing existing developer certificate.'
}

Write-Output 'Signing MSIX...'
& "$tools\signtool.exe" sign /fd SHA256 /a /v $msix

Write-Output 'Trusting certificate in CurrentUser\TrustedPeople...'
$cer = "$pkg\minepix-dev.cer"
Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null

Write-Output 'Installing package...'
Add-AppxPackage -Path $msix -ForceUpdateFromAnyVersion -ErrorAction Stop

Write-Output ''
Write-Output 'Installed packages:'
Get-AppxPackage -Name 'MinePix.App' | Select-Object Name, Version, InstallLocation | Format-List