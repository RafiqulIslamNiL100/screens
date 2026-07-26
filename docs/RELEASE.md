# Release

Exact sequence to ship a new version. Bump the version everywhere in the
same commit as any shippable change — the assembly version and both copies
of `version.json` must never disagree.

1. Bump `<Version>` in `src/Screens.App/Screens.App.csproj` (and
   `AssemblyVersion`/`FileVersion` if you're not passing them via
   `/p:Version=` on the command line).
2. Publish the self-contained win-x64 build:
   ```
   dotnet publish src/Screens.App/Screens.App.csproj -c Release -r win-x64 \
     --self-contained true /p:PublishSingleFile=true /p:Version=X.Y.Z
   ```
3. Build the installer:
   ```
   makensis /DVERSION=X.Y.Z installer/Screens.nsi
   ```
   (`installer/build-installer.ps1 -Version X.Y.Z` runs both of the above
   steps for you on Windows.)
4. Compute the SHA-256 of the installer:
   ```
   sha256sum installer/Screens-Setup-X.Y.Z.exe
   ```
5. Update `version.json` (`version`, `notes`, `url`, `sha256`) — in **both**
   this repo's root `version.json` and `screens-fnl-app`'s `version.json`.
   The `url` must point at the raw file in `screens-fnl-app`:
   ```
   https://github.com/RafiqulIslamNiL100/screens-fnl-app/raw/main/Screens-Setup-X.Y.Z.exe
   ```
6. Commit `Screens-Setup-X.Y.Z.exe` + `version.json` + `admin.html` to
   `screens-fnl-app` (distribution repo — no source code there).
7. Verify the raw GitHub URL actually downloads the installer:
   ```
   curl -fSL -o /tmp/verify.exe \
     https://raw.githubusercontent.com/RafiqulIslamNiL100/screens-fnl-app/main/Screens-Setup-X.Y.Z.exe
   sha256sum /tmp/verify.exe   # must match step 4
   ```

Start at **1.0.0**.

## What ships where

| Repo | Contents |
|------|----------|
| `screens` (this repo) | Source, `installer/Screens.nsi`, `admin/admin.html`, `db/schema.sql`, docs, this repo's own `version.json` mirror |
| `screens-fnl-app` | `Screens-Setup-X.Y.Z.exe`, `version.json` (the one the running app actually reads), `admin.html`, `README.md` |

The in-app update checker reads `version.json` from `screens-fnl-app`, not
from this repo:

```
https://raw.githubusercontent.com/RafiqulIslamNiL100/screens-fnl-app/main/version.json
```
