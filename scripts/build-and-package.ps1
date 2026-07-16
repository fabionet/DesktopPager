# Build completa di DesktopPager3D-OS: publish self-contained + installer MSI
# (WiX v5), con firma opzionale di eseguibile e MSI.
#
#   .\scripts\build-and-package.ps1         # MSI non firmato
#   .\scripts\build-and-package.ps1 -Sign   # firma exe e MSI col certificato FabioNET
#
# Prerequisiti: .NET 8 SDK e WiX v5. Vedi il README, sezione "Generare
# l'installer MSI". Tutti i percorsi sono relativi alla radice del repository.

[CmdletBinding()]
param(
    # Firma exe e MSI. Senza questo flag la build produce un MSI non firmato e
    # funziona anche su macchine prive del certificato.
    [switch]$Sign,
    [string]$Configuration = "Release",
    [string]$Thumbprint = "93D9C19F749ED540600AFF34E8A23DE6B7EA7DC3",
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$repo         = Split-Path -Parent $PSScriptRoot
$project      = Join-Path $repo "src\DesktopPager.Tray\DesktopPager.Tray.csproj"
$wxsPath      = Join-Path $repo "installer\Product.wxs"
$iconPath     = Join-Path $repo "src\DesktopPager.Tray\Assets\DesktopPager.ico"
$licenseRtf   = Join-Path $repo "installer\License.rtf"
$publishDir   = Join-Path $repo "publish"
$installerDir = Join-Path $repo "installer"

# --- Controlli preliminari (falliscono subito, prima della build lunga) -------

foreach ($tool in @("dotnet", "wix")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Comando '$tool' non trovato nel PATH. Vedi i prerequisiti nel README."
    }
}

foreach ($required in @($project, $wxsPath, $iconPath, $licenseRtf)) {
    if (-not (Test-Path $required)) { throw "File richiesto non trovato: $required" }
}

$cert = $null
if ($Sign) {
    $cert = Get-ChildItem -Path Cert:\CurrentUser\My, Cert:\LocalMachine\My |
            Where-Object { $_.Thumbprint -eq $Thumbprint } |
            Select-Object -First 1
    if (-not $cert) {
        throw "Certificato con thumbprint $Thumbprint non trovato negli store personali. Esegui senza -Sign per un MSI non firmato."
    }
    if (-not $cert.HasPrivateKey) {
        throw "Il certificato $Thumbprint non ha la chiave privata: impossibile firmare."
    }
    Write-Host ("==> Certificato di firma: {0}" -f $cert.Subject)
}

# --- Versione e nome dell'eseguibile: unica fonte di verita' il csproj --------

$csproj = [xml](Get-Content $project -Raw)
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
$assemblyName = ($csproj.Project.PropertyGroup.AssemblyName | Where-Object { $_ } | Select-Object -First 1)
if (-not $version)      { throw "Nessun <Version> trovato in $project" }
if (-not $assemblyName) { throw "Nessun <AssemblyName> trovato in $project" }
$version = $version.Trim()
$assemblyName = $assemblyName.Trim()

# La Version dentro Product.wxs finisce nel pacchetto installato, mentre il nome
# del file MSI viene dal csproj: se divergono, il nome del file mentirebbe sul
# contenuto e l'aggiornamento maggiore non scatterebbe come atteso.
$wxsVersion = ([xml](Get-Content $wxsPath -Raw)).Wix.Package.Version
if ($wxsVersion -ne $version) {
    throw "Versione disallineata: csproj = $version, Product.wxs = $wxsVersion. Allineale prima di generare l'MSI."
}

$exePath = Join-Path $publishDir "$assemblyName.exe"
$msiPath = Join-Path $installerDir "$assemblyName-$version-Setup.msi"

Write-Host "==> DesktopPager3D-OS $version ($Configuration, win-x64)"

# --- Publish self-contained ---------------------------------------------------

Write-Host "==> Pubblicazione self-contained"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish $project -c $Configuration -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallito (exit code $LASTEXITCODE)" }
if (-not (Test-Path $exePath)) { throw "Eseguibile atteso non prodotto: $exePath" }

# --- Firma dell'eseguibile (prima della build MSI, che lo incorpora) ----------

function Invoke-SignFile([string]$path) {
    $result = Set-AuthenticodeSignature -FilePath $path -Certificate $cert `
                                        -HashAlgorithm SHA256 -TimestampServer $TimestampServer
    if ($result.Status -ne "Valid") {
        throw ("Firma fallita per {0}: {1}" -f $path, $result.StatusMessage)
    }
    Write-Host ("    firmato: {0}" -f (Split-Path -Leaf $path))
}

if ($Sign) {
    Write-Host "==> Firma dell'eseguibile"
    Invoke-SignFile $exePath
}

# --- Build dell'MSI -----------------------------------------------------------

Write-Host "==> Build dell'installer MSI (WiX v5)"
if (Test-Path $msiPath) { Remove-Item $msiPath -Force }

& wix build $wxsPath `
    -ext WixToolset.UI.wixext `
    -d "PublishDir=$publishDir" `
    -d "IconPath=$iconPath" `
    -d "LicenseRtf=$licenseRtf" `
    -o $msiPath
if ($LASTEXITCODE -ne 0) { throw "wix build fallito (exit code $LASTEXITCODE)" }
if (-not (Test-Path $msiPath)) { throw "MSI atteso non prodotto: $msiPath" }

if ($Sign) {
    Write-Host "==> Firma dell'MSI"
    Invoke-SignFile $msiPath
}

# --- Riepilogo ----------------------------------------------------------------

Write-Host ""
Write-Host "Build completata."
Write-Host "  Publish : $publishDir"
Write-Host "  MSI     : $msiPath"
if ($Sign) {
    Write-Host "  Firma   : exe e MSI firmati ($($cert.Subject))"
} else {
    Write-Host "  Firma   : nessuna (riesegui con -Sign per firmare)"
}
