# DesktopPager3D-OS

Utility per Windows che nasce per **sfogliare le pagine delle icone del desktop**
quando sono più di quante ne stanno sullo schermo, e che si è evoluta in un piccolo
"sistema" a scomparsa: barra stile Windows, terminali a tendina, rotazione dello
schermo, riavvio di Explorer e due viste 3D per esplorare i file.

Resta residente in memoria con un'icona nell'area di notifica.

---

## Funzionalità

**Paginazione icone del desktop**
- `Ctrl+Alt+PgGiù` pagina avanti · `Ctrl+Alt+PgSu` pagina indietro · `Ctrl+Alt+Home` prima pagina
- Non sposta mai le icone: fa scorrere la vista della ListView del desktop
  (`LVM_SCROLL`, con rimozione temporanea di `LVS_NOSCROLL`), quindi il layout
  resta intatto e funziona anche con la disposizione automatica.

**Rotazione dello schermo** (come le hotkey dei driver Intel)
- `Ctrl+Alt+Su` sottosopra (180°) · `Ctrl+Alt+Giù` normale
- `Ctrl+Alt+Sinistra` (90°) · `Ctrl+Alt+Destra` (270°)
- `Ctrl+Alt+Shift+^` emergenza: ripristina l'orientamento normale

**Riavvio di Explorer**
- `Ctrl+Alt+Fine` (anche dal menu della tray): chiude e rilancia la shell.

**Barra a scomparsa stile Windows**
- Di default in alto, ridotta a una linguetta che si espande al passaggio del mouse;
  i pulsanti freccia la agganciano a sinistra/destra/alto. Colori dal tema di Windows.
- Avvii rapidi (trascina file o usa `＋`), terminali **PowerShell** e **CMD** a
  tendina con la console reale incorporata, Esplora file e orologio.

**Vista 3D (cover flow)**
- Anteprime dei file a schermo intero in 3D reale (WPF Viewport3D), con riflessi.
  Frecce/rotella per scorrere, Invio per aprire, Esc per chiudere.

**Vista 3D Game** 🎮
- Esplorazione in prima persona di dischi e cartelle come dentro un negozio, con
  un ingresso cinematografico (caricamento → porta bianca a 4 parti in stile logo
  Windows → vista dall'alto → atterraggio). Movimento frecce/WASD, Invio o clic per
  entrare/aprire, Backspace per risalire, Esc per uscire.

**Effetti desktop 3D** (opzionali, di default spenti)
- 🧊 **Cubo del desktop**: tieni **Ctrl + tasto destro** e trascina sul desktop per
  far ruotare le pagine delle icone come le facce di un cubo 3D; al rilascio scatta
  alla faccia più vicina e cambia pagina.
- 🫧 **Finestre "gelatina"** (stile Compiz): spostando una finestra questa ondeggia
  in modo elastico. Il contenuto è un fermo-immagine durante il movimento e torna
  vivo al rilascio (limite tecnico di un'app non-compositor).
- Si attivano dal menu tasto-destro della barra o dal menu della tray, alla voce
  **"Effetti desktop 3D"**; lo stato viene ricordato tra un avvio e l'altro.

---

## Installazione (utente finale)

1. Scarica `DesktopPager3D-OS-1.1.0-Setup.msi` dalla pagina
   [Releases](https://github.com/fabionet/DesktopPager3D-OS/releases).
2. Doppio clic sull'MSI e segui la procedura guidata (richiede i privilegi di
   amministratore perché installa in `C:\Program Files\DesktopPager3D-OS`).
3. Avvia **DesktopPager3D-OS** dal menu Start.

L'installer è **autonomo (self-contained)**: include il runtime .NET 8, quindi
**non serve installare nulla** oltre all'MSI. L'MSI è firmato con il certificato
di FabioNET.

> Avvio automatico con Windows: apri l'app, clic destro sull'icona nella tray e
> attiva "Avvio automatico con Windows" (crea una voce in
> `HKCU\...\CurrentVersion\Run`).

---

## Compilazione dai sorgenti

### Prerequisiti
- **.NET 8 SDK** (per l'app principale, C# WinForms + WPF): <https://dotnet.microsoft.com/download/dotnet/8.0>
- **WiX Toolset v5** (solo per generare l'MSI), come strumento .NET:
  ```
  dotnet tool install --global wix --version 5.0.2
  wix extension add -g WixToolset.UI.wixext/5.0.2
  ```
- **w64devkit** (GCC/MinGW, solo per la versione nativa C++ opzionale): <https://github.com/skeeto/w64devkit>

### Compilare ed eseguire l'app
```
git clone https://github.com/fabionet/DesktopPager3D-OS.git
cd DesktopPager3D-OS
dotnet build src/DesktopPager.Tray/DesktopPager.Tray.csproj -c Release
dotnet run  --project src/DesktopPager.Tray/DesktopPager.Tray.csproj -c Release
```

### Eseguire i test
```
dotnet test tests/DesktopPager.Tray.Tests/DesktopPager.Tray.Tests.csproj -c Release
```

### Generare l'installer MSI
```
# 1) pubblicazione self-contained
dotnet publish src/DesktopPager.Tray/DesktopPager.Tray.csproj -c Release -r win-x64 --self-contained true -o publish

# 2) build dell'MSI (percorsi ASSOLUTI per le variabili)
wix build installer/Product.wxs -ext WixToolset.UI.wixext ^
  -d "PublishDir=%CD%\publish" ^
  -d "IconPath=%CD%\src\DesktopPager.Tray\Assets\DesktopPager.ico" ^
  -d "LicenseRtf=%CD%\installer\License.rtf" ^
  -o "installer\DesktopPager3D-OS-1.0.0-Setup.msi"
```

### Versione nativa C++ (opzionale, componente leggero solo-pager)
In `src/DesktopPager.NativeCpp`. Build con MinGW/w64devkit:
```
cd src/DesktopPager.NativeCpp
windres resources.rc res.o
g++ -O2 -std=c++17 -municode -mwindows -DUNICODE -D_UNICODE -DWIN32_LEAN_AND_MEAN -DNOMINMAX main.cpp res.o -o DesktopPagerNative.exe -luser32 -lshell32 -lgdi32 -ladvapi32 -lcomctl32
```
oppure con CMake (generatore Visual Studio o MinGW). Hotkey identiche all'app .NET
(paginazione, rotazione, riavvio Explorer). Le viste 3D e la barra sono solo nell'app .NET.

---

## Struttura del progetto
```
src/DesktopPager.Tray/        app principale C# (WinForms + WPF)
src/DesktopPager.NativeCpp/   versione nativa C++ Win32 (pager leggero)
tests/DesktopPager.Tray.Tests/ test xUnit
installer/                    sorgenti WiX dell'MSI + licenza
scripts/                      script di build/icona/packaging
```

---

## Requisiti di sistema
- Windows 10 o 11 (x64).
- Le viste 3D usano WPF/Direct3D; su GPU molto datate il cover flow ripiega su una
  resa 2D (GDI+). Testato su NVIDIA GeForce GT 540M / Intel HD 3000.

---

## Licenze

**DesktopPager3D-OS** è distribuito con licenza **MIT** — vedi il file
[LICENSE](LICENSE).

Componenti di terze parti coinvolti:

| Componente | Uso | Licenza |
|---|---|---|
| .NET 8 Runtime, Windows Desktop (WPF, Windows Forms) | runtime dell'app, incluso nel pacchetto self-contained | MIT |
| WiX Toolset v5 | generazione dell'installer MSI (solo build) | Microsoft Reciprocal License (MS-RL) |
| API di Windows (user32, shell32, gdi32, dwmapi, comctl32, ecc.) | funzioni di sistema tramite P/Invoke | Windows SDK / EULA Microsoft |
| Segoe UI (font) | testo dell'interfaccia | font di sistema Windows (licenza Microsoft) |
| Icone di sistema, anteprime shell, icone di dischi/cartelle | mostrate a runtime | fornite dal sistema operativo Windows |

Le icone e le anteprime dei file mostrate nelle viste sono quelle del sistema
operativo/dei relativi handler e restano di proprietà dei rispettivi titolari.
