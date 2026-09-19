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

Change passwords/users before production use.

## Build
```powershell
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Installer
Use the included `Installer/Setup.iss` with Inno Setup. The installer lets the user choose the application folder and clinic data folder. It writes the selected data path to `C:\ProgramData\ClinicManagement\config.json`.


Backup fix: the manual database backup now checkpoints SQLite WAL data, creates the backup through the SQLite backup API, validates the resulting database with PRAGMA integrity_check, and only then creates the ZIP.
