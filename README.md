# DesktopPager
Applicazione desktop Windows per la gestione a pagine delle icone sul desktop.

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
Hotkey: Ctrl+Shift+PgUp, Ctrl+Shift+PgDn, Ctrl+Shift+Fine.
