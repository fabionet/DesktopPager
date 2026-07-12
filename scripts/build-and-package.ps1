$ErrorActionPreference = "Stop"

$repo = "C:\Users\FabioNET\Desktop\git_work_by_warp\DesktopPager"
$project = Join-Path $repo "src\DesktopPager.Tray\DesktopPager.Tray.csproj"
$iconScript = Join-Path $repo "scripts\create-app-icon.ps1"
$iconSource = Join-Path $repo "src\DesktopPager.Tray\Assets\DesktopPager.ico"
$artifacts = Join-Path $repo "artifacts"
$publishDir = Join-Path $artifacts "publish\DesktopPager.Tray-win-x64"
$installerDir = Join-Path $artifacts "DesktopPager-installer"
$installerZip = Join-Path $artifacts "DesktopPager-autoinstaller-win-x64.zip"

Write-Host "==> Preparing artifact directories"
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $installerDir) { Remove-Item $installerDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null

if (Test-Path $iconScript) {
    Write-Host "==> Generating app icon"
    powershell -ExecutionPolicy Bypass -File $iconScript
}

Write-Host "==> Building and publishing (Release, win-x64, self-contained, single-file)"
dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:PublishTrimmed=false `
  -o $publishDir

Write-Host "==> Preparing installer payload"
Copy-Item -Path "$publishDir\*" -Destination $installerDir -Recurse -Force
if (Test-Path $iconSource) {
    Copy-Item -Path $iconSource -Destination (Join-Path $installerDir "DesktopPager.ico") -Force
}

$installPs1 = @"
`$ErrorActionPreference = 'Stop'
`$sourceDir = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$targetDir = Join-Path `$env:LOCALAPPDATA 'DesktopPager'
`$startMenuDir = Join-Path `$env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DesktopPager'
`$exePath = Join-Path `$targetDir 'DesktopPager.Tray.exe'
`$iconPath = Join-Path `$targetDir 'DesktopPager.ico'

New-Item -ItemType Directory -Path `$targetDir -Force | Out-Null
Copy-Item -Path (Join-Path `$sourceDir '*') -Destination `$targetDir -Recurse -Force

New-Item -ItemType Directory -Path `$startMenuDir -Force | Out-Null
`$shortcutPath = Join-Path `$startMenuDir 'DesktopPager.lnk'
`$wsh = New-Object -ComObject WScript.Shell
`$shortcut = `$wsh.CreateShortcut(`$shortcutPath)
`$shortcut.TargetPath = `$exePath
`$shortcut.WorkingDirectory = `$targetDir
if (Test-Path `$iconPath) {
  `$shortcut.IconLocation = `$iconPath
} else {
  `$shortcut.IconLocation = `$exePath
}
`$shortcut.Save()

Write-Host "DesktopPager installato in: `$targetDir"
Write-Host "Avvio da Start Menu > DesktopPager"
"@

$uninstallPs1 = @"
`$targetDir = Join-Path `$env:LOCALAPPDATA 'DesktopPager'
`$startMenuDir = Join-Path `$env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DesktopPager'

if (Test-Path `$targetDir) { Remove-Item `$targetDir -Recurse -Force }
if (Test-Path `$startMenuDir) { Remove-Item `$startMenuDir -Recurse -Force }
Write-Host "DesktopPager disinstallato."
"@

$installCmd = @"
@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0install.ps1"
pause
"@

Set-Content -Path (Join-Path $installerDir "install.ps1") -Value $installPs1 -Encoding UTF8
Set-Content -Path (Join-Path $installerDir "uninstall.ps1") -Value $uninstallPs1 -Encoding UTF8
Set-Content -Path (Join-Path $installerDir "install.cmd") -Value $installCmd -Encoding ASCII

Write-Host "==> Creating installer zip"
if (Test-Path $installerZip) { Remove-Item $installerZip -Force }
Compress-Archive -Path "$installerDir\*" -DestinationPath $installerZip -Force

Write-Host ""
Write-Host "Build and packaging completed."
Write-Host "Publish output : $publishDir"
Write-Host "Installer folder: $installerDir"
Write-Host "Installer zip   : $installerZip"
