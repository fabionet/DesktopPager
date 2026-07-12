$ErrorActionPreference = "Stop"

$repo = "C:\Users\FabioNET\Desktop\git_work_by_warp\DesktopPager"
$zipPath = Join-Path $repo "artifacts\DesktopPager-autoinstaller-win-x64.zip"
$installerDir = Join-Path $repo "artifacts\DesktopPager-installer"
$installScript = Join-Path $installerDir "install.ps1"
$targetDir = Join-Path $env:LOCALAPPDATA "DesktopPager"
$shortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\DesktopPager\DesktopPager.lnk"

Write-Host "==> ZIP content verification"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
$entries = $zip.Entries | Select-Object -ExpandProperty FullName
$zip.Dispose()

$required = @(
    "DesktopPager.Tray.exe",
    "DesktopPager.ico",
    "install.ps1",
    "install.cmd",
    "uninstall.ps1"
)

foreach ($item in $required) {
    if ($entries -notcontains $item) {
        throw "Missing required entry in ZIP: $item"
    }
}
Write-Host "ZIP OK: required entries found."

Write-Host "==> Basic source security check"
$suspiciousPatterns = @(
    "Invoke-WebRequest",
    "DownloadString",
    "IEX(",
    "Process.Start",
    "cmd.exe /c",
    "http://",
    "https://"
)

$scanFiles = @(
    (Join-Path $repo "scripts\build-and-package.ps1"),
    (Join-Path $installerDir "install.ps1"),
    (Join-Path $installerDir "uninstall.ps1")
)

foreach ($file in $scanFiles) {
    $content = Get-Content $file -Raw
    foreach ($pattern in $suspiciousPatterns) {
        if ($content -like "*$pattern*") {
            Write-Host ("Warning pattern found in {0}: {1}" -f $file, $pattern)
        }
    }
}
Write-Host "Security scan complete (basic static check)."

Write-Host "==> Executing installer"
& powershell -ExecutionPolicy Bypass -File $installScript

Write-Host "==> Post-install verification"
if (-not (Test-Path $targetDir)) {
    throw "Install failed: target dir not found: $targetDir"
}
if (-not (Test-Path $shortcutPath)) {
    throw "Install failed: shortcut not found: $shortcutPath"
}

Write-Host "INSTALLATION OK"
Write-Host "Target: $targetDir"
Write-Host "Shortcut: $shortcutPath"
