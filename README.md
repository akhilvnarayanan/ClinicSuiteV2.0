# Clinic Suite v1.0

Offline/local clinic management application using ASP.NET Core + HTML/CSS/JavaScript + SQLite.

## Architecture
- `ClinicManagement.exe` starts a local server on `127.0.0.1:5050` and opens the browser manually or via installer shortcut.
- SQLite database and documents are outside the application folder by default: `C:\ClinicManagementData`.
- Prescription images/documents are stored as files; SQLite stores metadata/path.
- SQLite WAL + FULL synchronous mode is enabled.
- Admin and Receptionist roles are included.
- Clinic profile, backup, users, doctors and services are prepared.

## Default login
- admin / Admin@123
- receptionist / Reception@123

The administrator manages staff passwords and account access from the Users screen inside the app.

## Build
```powershell
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Installer
On a Windows computer with the .NET 8 SDK installed, create the self-contained Windows payload with:

```powershell
.\Installer\Publish-Windows.ps1
```

Install Inno Setup 6 and create the installer with:

```powershell
.\Installer\Publish-Windows.ps1 -BuildInstaller
```

The installer lets the user choose the application folder and clinic data folder. It writes the selected data path to `%ProgramData%\ClinicManagement\config.json`, preserves the selected data folder during upgrades, and does not remove it during uninstall.

The Windows installer currently expects ImageMagick to be available as `magick.exe` for JPG/JPEG/PNG-to-PDF document conversion. A future packaging step should bundle or install a pinned ImageMagick version rather than relying on a pre-existing system installation.


Backup fix: the manual database backup now checkpoints SQLite WAL data, creates the backup through the SQLite backup API, validates the resulting database with PRAGMA integrity_check, and only then creates the ZIP.
