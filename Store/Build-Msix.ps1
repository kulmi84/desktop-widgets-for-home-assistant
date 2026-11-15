param(
    [Parameter(Mandatory=$true)][string]$IdentityName,
    [Parameter(Mandatory=$true)][string]$Publisher,
    [Parameter(Mandatory=$true)][string]$PublisherDisplayName,
    [string]$MakeAppx
)
$ErrorActionPreference = 'Stop'
if (!$MakeAppx) {
    $sdk = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits\10\bin'
    $MakeAppx = Get-ChildItem $sdk -Filter makeappx.exe -Recurse |
        Where-Object { $_.Directory.Name -eq 'x64' } |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$MakeAppx) { throw 'MakeAppx.exe aus dem Windows SDK fehlt.' }

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    cmd.exe /d /c Build.cmd --store
    if ($LASTEXITCODE) { throw 'Store-Build fehlgeschlagen.' }
    $exe = Join-Path $root 'DesktopWidgets.Store.exe'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    $out = Join-Path $root 'artifacts'
    [void][IO.Directory]::CreateDirectory($out)
    $stage = Join-Path ([IO.Path]::GetTempPath()) ('widgets-msix-' + [guid]::NewGuid())
    $assets = Join-Path $stage 'Assets'
    [void][IO.Directory]::CreateDirectory($assets)
    try {
        Copy-Item -LiteralPath $exe,(Join-Path $root 'MDI-NOTICE.txt'),(Join-Path $root 'MDI-LICENSE.txt') -Destination $stage
        [xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
        $manifest.Package.Identity.SetAttribute('Name', $IdentityName)
        $manifest.Package.Identity.SetAttribute('Publisher', $Publisher)
        $manifest.Package.Identity.SetAttribute('Version', $version.FileVersion)
        $manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
        $manifest.Save((Join-Path $stage 'AppxManifest.xml'))

        Add-Type -AssemblyName System.Drawing
        $icon = [Drawing.Icon]::new((Join-Path $root 'HomeAssistantWidget.ico'), 256, 256)
        try {
            $source = $icon.ToBitmap()
            try {
                foreach ($size in @(44, 50, 150)) {
                    $bitmap = [Drawing.Bitmap]::new($size, $size)
                    $graphics = [Drawing.Graphics]::FromImage($bitmap)
                    try {
                        $graphics.InterpolationMode = 'HighQualityBicubic'
                        $graphics.DrawImage($source, 0, 0, $size, $size)
                        $bitmap.Save((Join-Path $assets "Logo$size.png"), [Drawing.Imaging.ImageFormat]::Png)
                    } finally { $graphics.Dispose(); $bitmap.Dispose() }
                }
            } finally { $source.Dispose() }
        } finally { $icon.Dispose() }

        $package = Join-Path $out ("DesktopWidgets-" + $version.FileVersion + ".msix")
        & $MakeAppx pack /d $stage /p $package /o
        if ($LASTEXITCODE) { throw 'MSIX-Paketierung fehlgeschlagen.' }
        Get-Item -LiteralPath $package
    } finally {
        foreach ($file in Get-ChildItem -LiteralPath $assets -File) { [IO.File]::Delete($file.FullName) }
        [IO.Directory]::Delete($assets)
        foreach ($file in Get-ChildItem -LiteralPath $stage -File) { [IO.File]::Delete($file.FullName) }
        [IO.Directory]::Delete($stage)
    }
} finally { Pop-Location }
