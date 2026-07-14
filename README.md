# DesktopPager
Applicazione desktop Windows per la gestione a pagine delle icone sul desktop.

## Come funziona (v0.2)
Il motore di paging **non sposta mai le icone**: fa scorrere la vista della
ListView del desktop con `LVM_SCROLL`, dopo aver rimosso temporaneamente lo
stile `LVS_NOSCROLL` (che altrimenti fa ignorare il comando). Tornando alla
prima pagina lo stile originale viene ripristinato e il desktop resta
esattamente com'era. Funziona anche con "disposizione automatica" attiva.

Il vecchio approccio (riposizionamento fisico delle icone) e' stato rimosso:
inviava `LVM_SETITEMPOSITION32` con le coordinate in `lParam`, ma quel
messaggio si aspetta un puntatore a `POINT` — Explorer dereferenziava un
puntatore non valido e crashava.

Hotkey globali:
- `Ctrl+Alt+PgGiu` — pagina avanti
- `Ctrl+Alt+PgSu` — pagina indietro
- `Ctrl+Alt+Home` — prima pagina
- `Ctrl+Alt+Fine` — riavvia Explorer (disponibile anche dal menu della tray)

Rotazione dello schermo (come le hotkey dei driver Intel):
- `Ctrl+Alt+Su` — schermo sottosopra (180°)
- `Ctrl+Alt+Giu` — torna normale
- `Ctrl+Alt+Sinistra` — ruota con la barra sul lato sinistro (90°)
- `Ctrl+Alt+Destra` — ruota con la barra sul lato destro (270°)
- `Ctrl+Alt+Shift+^` — emergenza: riporta tutto come prima

## Barra a scomparsa (v0.4, solo versione .NET)
All'avvio compare una barra stile Windows, di default in alto ridotta a una
linguetta centrale color accento che si espande al passaggio del mouse. I
pulsanti freccia alle estremita' la agganciano al lato sinistro o destro
(anch'essi a scomparsa) o di nuovo in alto. I colori seguono il tema chiaro/
scuro di Windows e il colore accento DWM. La barra ospita:
- **avvii rapidi**: trascina file/programmi sulla barra o usa il pulsante ＋;
  clic per avviare, clic destro per rimuovere. I collegamenti vivono in
  `%AppData%\DesktopPager\QuickLaunch`.
- **terminali a tendina** PowerShell (PS) e CMD (>_): scendono fino a meta'
  schermo con la vera console incorporata e restano fissi finche' non si
  chiudono con la X.
- **vista 3D (3D)**: cover flow a schermo intero delle anteprime dei file.
  3D reale in WPF (Viewport3D con camera prospettica, copertine texturizzate
  inclinate nello spazio e riflessi a pavimento), accelerato dalla GPU con
  fallback software; se WPF non e' disponibile ripiega sulla resa GDI+ senza
  GPU per macchine molto datate. Frecce/rotella/clic laterali per scorrere,
  Invio o doppio clic per aprire, Esc per chiudere.
- **Esplora file** e orologio, come la barra di Windows.

## Istruzioni Git del progetto
Flusso consigliato per lavorare sul repository abionet/DesktopPager.

1. Clona la repository: git clone https://github.com/fabionet/DesktopPager.git e poi cd DesktopPager.
2. Aggiorna sempre main: git checkout main + git pull --ff-only origin main.
3. Crea branch dedicato: git checkout -b feature/nome-funzionalita.
4. Commit piccoli e chiari: git add . e git commit -m  descrizione.
5. Pubblica il branch: git push -u origin feature/nome-funzionalita.
6. Apri una Pull Request verso main con riepilogo modifiche e test eseguiti.
7. Prima del merge, riallinea il branch: git checkout main, git pull --ff-only origin main, git checkout feature/nome-funzionalita, git rebase main.
8. Per release: git tag vX.Y.Z e git push origin vX.Y.Z.
9. Dopo merge, pulizia branch: git branch -d feature/nome-funzionalita e git push origin --delete feature/nome-funzionalita.

## Convenzioni
- Usa prefissi branch: eature/, ix/, chore/.
- Esegui sempre test/build prima della PR.
- Mantieni main stabile e rilasciabile.

## Versione C++ nativa (Win32)
La conversione C++ e in src/DesktopPager.NativeCpp.
Build (Visual Studio generator):
cmake -S . -B build-cpp -G  Visual Studio 17 2022 -A x64
cmake --build build-cpp --config Release
Eseguibile: build-cpp\src\DesktopPager.NativeCpp\Release\DesktopPagerNative.exe
Build alternativa (MinGW/w64devkit):
cmake -S . -B build-cpp -G "MinGW Makefiles"
cmake --build build-cpp
Hotkey: Ctrl+Alt+PgGiu, Ctrl+Alt+PgSu, Ctrl+Alt+Home, Ctrl+Alt+Fine (riavvia Explorer).
