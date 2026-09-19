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
On a Windows computer with the .NET 8 SDK or newer installed, create the self-contained Windows payload with:

```powershell
.\Installer\Publish-Windows.ps1
```

Install Inno Setup 6 and create the installer with:

```powershell
.\Installer\Publish-Windows.ps1 -BuildInstaller
```

That command creates an unsigned development installer. For public distribution, install the organization's code-signing certificate with its private key in the Windows `CurrentUser\My` or `LocalMachine\My` certificate store, install the Windows SDK (`signtool.exe`), and run:

```powershell
.\Installer\Publish-Windows.ps1 -BuildInstaller -Sign -CertificateThumbprint "CERTIFICATE_THUMBPRINT"
```

The signing flow signs and verifies the published `ClinicManagement.exe` before Inno Setup packages it, then signs and verifies `ClinicSuiteSetup.exe` with SHA-256 and an RFC 3161 timestamp. The certificate and private key stay in the Windows certificate store and are never written to the repository or installer payload.

The installer lets the user choose the application folder and clinic data folder. It writes the selected data path to `%ProgramData%\ClinicManagement\config.json`, preserves the selected data folder during upgrades, and does not remove it during uninstall.

The Windows packaging script downloads and verifies the pinned official ImageMagick `7.1.2-31` Q16 x64 installer. Inno Setup runs it silently into the installed application's `ImageMagick` folder, verifies that `magick.exe` exists, and Clinic Suite uses that packaged executable for JPG/JPEG/PNG-to-PDF conversion. The SHA-256 verification is performed before the installer payload is created.


Backup fix: the manual database backup now checkpoints SQLite WAL data, creates the backup through the SQLite backup API, validates the resulting database with PRAGMA integrity_check, and only then creates the ZIP.
