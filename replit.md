# Replit run notes

## Start the application

The `Start application` workflow runs the clinic web app on port 5000:

```bash
CLINIC_HOST=0.0.0.0 CLINIC_PORT=5000 CLINIC_DATA_PATH=.data dotnet run --no-launch-profile
```

The app uses a cross-platform `net7.0` target in the Linux/Replit environment. Windows builds use the `net7.0-windows` target and retain the Windows Forms folder picker.

First-run sign-in accounts (the app requires a password change immediately):

- `admin` / `Admin@123`
- `receptionist` / `Reception@123`

Clinic data is stored in `.data` for the Replit workflow. Do not use the default Windows data path in this environment.