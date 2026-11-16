$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo = Split-Path -Parent $PSScriptRoot
$src  = "$repo\Assets\minepix.png"
$pkg  = "$repo\packaging"
$staging = "$pkg\staging"
$assets = "$staging\Assets"

# stage layout: copy build output, then write icons + manifest
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force -Path $assets | Out-Null

$cfg = 'Release'
$out = "$repo\bin\$cfg\net8.0-windows10.0.22621.0"
Get-ChildItem $out -File | Copy-Item -Destination $staging
Copy-Item "$pkg\AppxManifest.xml" "$staging\AppxManifest.xml"

function Make-Icon($size, $name) {
    $img  = [System.Drawing.Bitmap]::new($src)
    $dst  = [System.Drawing.Bitmap]::new($size, $size)
    $g    = [System.Drawing.Graphics]::FromImage($dst)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($img, 0, 0, $size, $size)
    $g.Dispose(); $img.Dispose()
    $dst.Save("$assets\$name", [System.Drawing.Imaging.ImageFormat]::Png)
    $dst.Dispose()
    Write-Output "wrote $name (${size}x${size})"
}

Make-Icon 50  'StoreLogo.png'
Make-Icon 150 'Square150x150Logo.png'
Make-Icon 44  'Square44x44Logo.png'

# tools are the NuGet-shipped Windows SDK Build Tools, not a system-wide SDK install
$tools = "$env:NUGET_PACKAGES\microsoft.windows.sdk.buildtools\10.0.22621.3233\bin\10.0.22621.0\x64"
if (-not $env:NUGET_PACKAGES) {
    $tools = 'D:\nuget\packages\microsoft.windows.sdk.buildtools\10.0.22621.3233\bin\10.0.22621.0\x64'
}
if ($env:MIX_SDK_TOOLS) { $tools = $env:MIX_SDK_TOOLS }

$msix = "$pkg\MinePix.msix"
Remove-Item $msix, "$msix.appxblockmap" -Force -ErrorAction SilentlyContinue
& "$tools\makeappx.exe" pack /d $staging /p $msix /o

Write-Output ""
Write-Output "MSIX packaged: $msix"
