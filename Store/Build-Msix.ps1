param(
    [Parameter(Mandatory=$true)][string]$IdentityName,
    [Parameter(Mandatory=$true)][string]$Publisher,
    [Parameter(Mandatory=$true)][string]$PublisherDisplayName,
    [string]$MakeAppx,
    [version]$PackageVersion,
    [ValidateSet('x64', 'arm64', 'x86')][string]$Architecture = 'x64',
    [string]$OutputPath
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
    $out = Join-Path $root 'artifacts'
    [void][IO.Directory]::CreateDirectory($out)
    $stage = Join-Path ([IO.Path]::GetTempPath()) ('widgets-msix-' + [guid]::NewGuid())
    $assets = Join-Path $stage 'Assets'
    [void][IO.Directory]::CreateDirectory($assets)
    try {
        cmd.exe /d /c Build.cmd --store "$stage"
        if ($LASTEXITCODE) { throw 'Store-Build fehlgeschlagen.' }
        $exe = Join-Path $stage 'DesktopWidgetsForHomeAssistant.Store.exe'
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
        $provider = Join-Path $PSScriptRoot 'WidgetBoard\WidgetBoard.csproj'
        $providerOutput = Join-Path $stage 'WidgetBoard'
        & dotnet publish $provider -c Release -r "win-$Architecture" -o $providerOutput | Out-Host
        if ($LASTEXITCODE) { throw 'Widgetboard-Build fehlgeschlagen.' }
        # WinRT metadata discovery uses the package root for the WidgetProvider proxy.
        Move-Item -LiteralPath (Join-Path $providerOutput 'Microsoft.Windows.Widgets.winmd') -Destination $stage

        Copy-Item -LiteralPath (Join-Path $root 'MDI-NOTICE.txt'),(Join-Path $root 'MDI-LICENSE.txt') -Destination $stage
        foreach ($screenshot in 'MixedPickerPreview.png', 'SwitchesPickerPreview.png', 'SensorsPickerPreview.png', 'SolarPickerPreviewV2.png') {
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot $screenshot) -Destination $assets
        }
        [xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
        $manifest.Package.Identity.SetAttribute('Name', $IdentityName)
        $manifest.Package.Identity.SetAttribute('Publisher', $Publisher)
        $manifest.Package.Identity.SetAttribute('ProcessorArchitecture', $Architecture)
        $manifest.Package.Identity.SetAttribute('Version', $(if ($PackageVersion) {
            $PackageVersion.ToString(4)
        } else { $version.FileVersion }))
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

        $package = if ($OutputPath) { $OutputPath } else {
            Join-Path $out ("DesktopWidgets-" + $version.ProductVersion.Replace("+build.", "-Build-") + "-$Architecture.msix")
        }
        $packLog = & $MakeAppx pack /d $stage /p $package /o
        if ($LASTEXITCODE) { throw ("MSIX-Paketierung fehlgeschlagen.`n" + ($packLog -join "`n")) }
        Get-Item -LiteralPath $package
    } finally {
        $resolvedStage = (Resolve-Path -LiteralPath $stage).ProviderPath
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ([IO.Path]::GetDirectoryName($resolvedStage) -eq $tempRoot.TrimEnd('\', '/') -and
            [IO.Path]::GetFileName($resolvedStage) -match '^widgets-msix-[a-f0-9-]{36}$') {
            Remove-Item -LiteralPath $resolvedStage -Recurse -Force
        }
    }
} finally { Pop-Location }
