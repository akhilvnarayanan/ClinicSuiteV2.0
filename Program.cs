#if WINDOWS_DESKTOP
using System.Windows.Forms;
#endif
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
var configuredPort = Environment.GetEnvironmentVariable("CLINIC_PORT") ?? builder.Configuration["ClinicManagement:Port"] ?? "5050";
var configuredHost = Environment.GetEnvironmentVariable("CLINIC_HOST") ?? "127.0.0.1";
builder.WebHost.UseUrls($"http://{configuredHost}:{configuredPort}");
var app = builder.Build();
var sessions = new ConcurrentDictionary<string, Session>();

var defaultData = Environment.GetEnvironmentVariable("CLINIC_DATA_PATH") ?? builder.Configuration["ClinicManagement:DefaultDataPath"] ?? @"C:\ClinicManagementData";
var configDir = Environment.GetEnvironmentVariable("CLINIC_CONFIG_PATH")
    ?? (OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClinicManagement")
        : Path.Combine(defaultData, ".config"));
Directory.CreateDirectory(configDir);
var configFile = Path.Combine(configDir, "config.json");
var installConfig = Helpers.LoadConfig(configFile);
var dataRoot = installConfig.DataPath ?? defaultData;
Directory.CreateDirectory(dataRoot);
Directory.CreateDirectory(Path.Combine(dataRoot, "Documents", "Patients"));
Directory.CreateDirectory(Path.Combine(dataRoot, "Backups"));
Directory.CreateDirectory(Path.Combine(dataRoot, "Logs"));
Directory.CreateDirectory(Path.Combine(dataRoot, "Branding"));
var dbPath = Path.Combine(dataRoot, "clinic.db");

using (var db = new Db(dbPath)) db.Initialize();

app.UseDefaultFiles();
app.UseStaticFiles();
if (OperatingSystem.IsWindows()) _ = Task.Run(async () => { await Task.Delay(700); try { Process.Start(new ProcessStartInfo { FileName = $"http://127.0.0.1:{configuredPort}", UseShellExecute = true }); } catch { } });

app.MapGet("/api/session", (HttpContext c) => { var s = Current(c); return Results.Ok(new { authenticated = s is not null, role = s?.Role }); });

app.MapPost("/api/login", (HttpContext c, LoginRequest r) => {
    using var db = new Db(dbPath);
    var user = db.FindUser(r.Username);
    if (user is null || !PasswordHasher.Verify(r.Password, user.PasswordHash)) return Results.Json(new { ok=false, message="Invalid username or password." }, statusCode:401);
    var token = Guid.NewGuid().ToString("N");
    sessions[token] = new Session(user.Username, user.Role, DateTimeOffset.UtcNow.AddHours(8));
    c.Response.Cookies.Append("clinic_session", token, new CookieOptions{HttpOnly=true, SameSite=SameSiteMode.Strict, Secure=false, MaxAge=TimeSpan.FromHours(8)});
    return Results.Ok(new {ok=true, role=user.Role});
});
app.MapPost("/api/logout", (HttpContext c) => { if (c.Request.Cookies.TryGetValue("clinic_session", out var token)) sessions.TryRemove(token, out _); c.Response.Cookies.Delete("clinic_session"); return Results.Ok(new{ok=true}); });

Session? Current(HttpContext c) => c.Request.Cookies.TryGetValue("clinic_session", out var token) && sessions.TryGetValue(token, out var s) && s.Expires > DateTimeOffset.UtcNow ? s : null;
bool Auth(HttpContext c) => Current(c) is not null;
bool Admin(HttpContext c) => Current(c)?.Role == "Admin";
string BackupDestination(ClinicSettings s, bool manual) {
    var configured = manual ? s.ManualBackupPath : s.BackupPath;
    return string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(dataRoot, "Backups", manual ? "Manual" : "Scheduled")
        : Path.GetFullPath(configured.Trim());
}
var backupLock = new object();

string CreateBackup(ClinicSettings s, bool manual)
{
    lock (backupLock)
    {
        var folder = BackupDestination(s, manual);
        Directory.CreateDirectory(folder);
        var backup = Path.Combine(folder, $"ClinicBackup_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        var tempDb = Path.Combine(Path.GetTempPath(), "ClinicBackup_" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            // Create a standalone, consistent SQLite snapshot. No destination
            // SQLite connection is kept open while the ZIP is being created.
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            };

            using (var source = new SqliteConnection(csb.ToString()))
            {
                source.Open();
                using var cmd = source.CreateCommand();
                cmd.CommandText = "PRAGMA busy_timeout=10000; VACUUM INTO $p;";
                cmd.Parameters.AddWithValue("$p", tempDb);
                cmd.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();

            if (!File.Exists(tempDb) || new FileInfo(tempDb).Length < 1024)
                throw new IOException("The SQLite snapshot was not created correctly.");

            // Validate the standalone snapshot with a non-pooled connection.
            var verifyCsb = new SqliteConnectionStringBuilder
            {
                DataSource = tempDb,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            };

            using (var verify = new SqliteConnection(verifyCsb.ToString()))
            {
                verify.Open();
                using var check = verify.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                var result = Convert.ToString(check.ExecuteScalar());
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("SQLite integrity check failed for the backup.");
            }

            SqliteConnection.ClearAllPools();

            // Create the ZIP only after every SQLite handle is closed.
            if (File.Exists(backup)) File.Delete(backup);
            using (var archive = System.IO.Compression.ZipFile.Open(backup, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("clinic.db", System.IO.Compression.CompressionLevel.Fastest);
                using var input = new FileStream(tempDb, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = entry.Open();
                input.CopyTo(output);
            }

            // Verify the final archive before reporting success.
            using (var archive = System.IO.Compression.ZipFile.Open(backup, System.IO.Compression.ZipArchiveMode.Read))
            {
                var entry = archive.GetEntry("clinic.db");
                if (entry is null || entry.Length < 1024)
                    throw new IOException("The backup ZIP does not contain a valid clinic.db file.");
            }

            return backup;
        }
        catch
        {
            try { if (File.Exists(backup)) File.Delete(backup); } catch { }
            throw;
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }
}
bool ScheduledDue(ClinicSettings s, DateTime now)
{
    if (s.BackupSchedule is not ("Daily" or "Weekly") || !TimeSpan.TryParse(s.BackupTime, out var at)) return false;
    if (!DateTime.TryParse(s.LastScheduledBackup, out var last)) last = DateTime.MinValue;
    if (now.Date == last.Date && s.BackupSchedule == "Daily") return false;
    if (s.BackupSchedule == "Weekly") {
        var day = Enum.TryParse<DayOfWeek>(s.BackupDay, true, out var parsed) ? parsed : DayOfWeek.Monday;
        if (now.DayOfWeek != day || now.Date <= last.Date.AddDays(6)) return false;
    }
    return now.TimeOfDay >= at;
}

app.MapGet("/api/dashboard", (HttpContext c) => {
    if (!Auth(c)) return Results.Unauthorized();
    using var db=new Db(dbPath);
    return Results.Ok(db.Dashboard());
});
app.MapGet("/api/clinic-profile", (HttpContext c) => {
    if (!Auth(c)) return Results.Unauthorized();
    using var db = new Db(dbPath);
    return Results.Ok(db.ClinicProfile());
});
app.MapGet("/api/patients", (HttpContext c, string? q) => {
    if (!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); return Results.Ok(db.SearchPatients(q));
});
app.MapPost("/api/patients", (HttpContext c, PatientRequest r) => {
    if (!Auth(c)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(r.Name)) return Results.BadRequest(new { message = "Name is required." });
    using var db=new Db(dbPath); return Results.Ok(db.AddPatient(r));
});
app.MapPut("/api/patients/{id:int}", (HttpContext c, int id, PatientRequest r) => {
    if (!Auth(c)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(r.Name)) return Results.BadRequest(new { message = "Name is required." });
    using var db = new Db(dbPath);
    return db.UpdatePatient(id, r) ? Results.Ok(db.GetPatient(id)) : Results.NotFound();
});
app.MapGet("/api/patients/{id:int}", (HttpContext c,int id) => { if(!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); var p=db.GetPatient(id); return p is null?Results.NotFound():Results.Ok(p); });
app.MapGet("/api/patients/{id:int}/history", (HttpContext c,int id) => { if(!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); return Results.Ok(db.PatientHistory(id)); });
app.MapGet("/api/visits", (HttpContext c,int? patientId) => { if(!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); return Results.Ok(db.Visits(patientId)); });
app.MapGet("/api/visits/{id:int}", (HttpContext c,int id) => { if(!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); var v=db.Visit(id); return v is null?Results.NotFound():Results.Ok(v); });
app.MapPost("/api/visits", (HttpContext c, VisitRequest r) => { if(!Auth(c)) return Results.Unauthorized(); if(r.PatientId<=0) return Results.BadRequest(new{message="Patient is required."}); using var db=new Db(dbPath); try{return Results.Ok(db.AddVisit(r));}catch(InvalidOperationException ex){return Results.BadRequest(new{message=ex.Message});}catch(SqliteException){return Results.BadRequest(new{message="Patient or doctor does not exist."});} });
app.MapPost("/api/visits/{id:int}/services", (HttpContext c,int id, VisitServiceRequest r) => { if(!Auth(c)) return Results.Unauthorized(); if(r.Amount<0||string.IsNullOrWhiteSpace(r.Description)) return Results.BadRequest(new{message="Description and non-negative amount are required."}); using var db=new Db(dbPath); return db.Visit(id) is null?Results.NotFound():Results.Ok(db.AddVisitService(id,r)); });
app.MapPost("/api/visits/{id:int}/payments", (HttpContext c,int id, PaymentRequest r) => { if(!Auth(c)) return Results.Unauthorized(); if(r.Amount<=0) return Results.BadRequest(new{message="Payment must be positive."}); using var db=new Db(dbPath); return db.Visit(id) is null?Results.NotFound():Results.Ok(db.AddPayment(id,r)); });
app.MapGet("/api/patients/{id:int}/payments", (HttpContext c,int id) => { if(!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); return Results.Ok(db.PaymentHistory(id)); });
app.MapGet("/api/doctors", (HttpContext c) => { if(!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); return Results.Ok(db.Doctors()); });
app.MapGet("/api/doctors/available", (HttpContext c) => { if(!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); return Results.Ok(db.Doctors(true)); });
app.MapGet("/api/doctor-availability", (HttpContext c) => { if(!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); return Results.Ok(db.DoctorAvailability()); });
app.MapPost("/api/doctor-availability", (HttpContext c, DoctorAvailabilityRequest r) => {
    if(!Auth(c)) return Results.Unauthorized();
    if(r.DoctorId<=0) return Results.BadRequest(new { message = "Doctor is required." });
    using var db=new Db(dbPath);
    try { db.SetDoctorAvailability(r.DoctorId, r.Available); return Results.Ok(); }
    catch(InvalidOperationException ex) { return Results.BadRequest(new { message = ex.Message }); }
});
app.MapPost("/api/doctors", (HttpContext c, DoctorRequest r) => { if(!Admin(c)) return Results.StatusCode(403); if(string.IsNullOrWhiteSpace(r.Name)||r.ConsultationFee<0) return Results.BadRequest(new{message="Name and a non-negative consultation fee are required."}); using var db=new Db(dbPath); db.AddDoctor(r); return Results.Ok(); });
app.MapPut("/api/doctors/{id:int}", (HttpContext c, int id, DoctorRequest r) => {
    if (!Admin(c)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(r.Name) || r.ConsultationFee < 0) return Results.BadRequest(new { message = "Name and a non-negative consultation fee are required." });
    using var db = new Db(dbPath);
    return db.UpdateDoctor(id, r) ? Results.Ok() : Results.NotFound();
});
app.MapDelete("/api/doctors/{id:int}", (HttpContext c, int id) => {
    if (!Admin(c)) return Results.StatusCode(403);
    using var db = new Db(dbPath);
    if (db.DoctorHasVisits(id)) return Results.Conflict(new { message = "This doctor is used in existing visits and cannot be deleted. Mark the doctor inactive instead." });
    return db.DeleteDoctor(id) ? Results.Ok(new { ok = true }) : Results.NotFound();
});
app.MapGet("/api/services", (HttpContext c) => { if(!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); return Results.Ok(db.Services()); });
app.MapPost("/api/services", (HttpContext c, ServiceRequest r) => { if(!Admin(c)) return Results.StatusCode(403); using var db=new Db(dbPath); db.AddService(r); return Results.Ok(); });
app.MapPut("/api/services/{id:int}", (HttpContext c, int id, ServiceRequest r) => {
    if (!Admin(c)) return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(r.Name) || r.Price < 0) return Results.BadRequest(new { message = "Name and a non-negative price are required." });
    using var db = new Db(dbPath);
    return db.UpdateService(id, r) ? Results.Ok() : Results.NotFound();
});
app.MapGet("/api/settings", (HttpContext c) => { if(!Admin(c)) return Results.StatusCode(403); using var db=new Db(dbPath); return Results.Ok(db.Settings()); });
app.MapPost("/api/settings", (HttpContext c, ClinicSettings s) => {
    if (!Admin(c)) return Results.StatusCode(403);
    if (s.BackupSchedule is not ("Off" or "Daily" or "Weekly")) return Results.BadRequest(new { message = "Choose Off, Daily, or Weekly for backup schedule." });
    if (!TimeSpan.TryParse(s.BackupTime, out _)) return Results.BadRequest(new { message = "Enter a valid backup time." });
    if (s.BackupSchedule == "Weekly" && !Enum.TryParse<DayOfWeek>(s.BackupDay, true, out _)) return Results.BadRequest(new { message = "Choose a valid backup day." });
    try {
        if (!string.IsNullOrWhiteSpace(s.ManualBackupPath)) Directory.CreateDirectory(Path.GetFullPath(s.ManualBackupPath.Trim()));
        if (!string.IsNullOrWhiteSpace(s.BackupPath)) Directory.CreateDirectory(Path.GetFullPath(s.BackupPath.Trim()));
        using var db = new Db(dbPath); db.SaveSettings(s); return Results.Ok();
    } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) {
        return Results.BadRequest(new { message = "The backup folder could not be created. Check the path and permissions." });
    }
});
app.MapPost("/api/browse-folder", (HttpContext c) =>
{
    if (!Admin(c)) return Results.StatusCode(403);

#if !WINDOWS_DESKTOP
    return Results.BadRequest(new
    {
        message = "Browse Folder is available only in the Windows app. Enter the backup folder path manually."
    });
#else
    if (!OperatingSystem.IsWindows())
        return Results.BadRequest(new
        {
            message = "Browse Folder is available only in the Windows app. Enter the backup folder path manually."
        });

    try
    {
        string? selectedPath = null;
        Exception? pickerError = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Select a database backup folder",
                    ShowNewFolderButton = true,
                    UseDescriptionForTitle = true
                };

                if (dialog.ShowDialog() == DialogResult.OK)
                    selectedPath = dialog.SelectedPath;
            }
            catch (Exception ex)
            {
                pickerError = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (pickerError is not null)
            return Results.BadRequest(new
            {
                message = "The Windows folder picker could not be opened. Enter the backup folder path manually."
            });

        return string.IsNullOrWhiteSpace(selectedPath)
            ? Results.Ok(new { cancelled = true, path = "" })
            : Results.Ok(new { cancelled = false, path = selectedPath });
    }
    catch
    {
        return Results.BadRequest(new
        {
            message = "The Windows folder picker could not be opened. Enter the backup folder path manually."
        });
    }
#endif
});
app.MapPost("/api/clinic-logo", async (HttpContext c, IFormFile file) => {
    if (!Admin(c)) return Results.StatusCode(403);
    if (file.Length == 0 || file.Length > 2 * 1024 * 1024) return Results.BadRequest(new { message = "Choose a logo smaller than 2 MB." });
    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    var types = new Dictionary<string, string> { [".png"]="image/png", [".jpg"]="image/jpeg", [".jpeg"]="image/jpeg", [".webp"]="image/webp", [".gif"]="image/gif" };
    if (!types.ContainsKey(ext)) return Results.BadRequest(new { message = "Use a PNG, JPG, WEBP, or GIF logo." });
    var folder = Path.Combine(dataRoot, "Branding");
    foreach (var existing in Directory.GetFiles(folder, "clinic-logo.*")) File.Delete(existing);
    var path = Path.Combine(folder, "clinic-logo" + ext);
    await using (var stream = File.Create(path)) await file.CopyToAsync(stream);
    return Results.Ok(new { logoPath = "/api/clinic-logo/file" });
});
app.MapGet("/api/clinic-logo/file", (HttpContext c) => {
    if (!Auth(c)) return Results.Unauthorized();
    var path = Directory.GetFiles(Path.Combine(dataRoot, "Branding"), "clinic-logo.*").FirstOrDefault();
    if (path is null) return Results.NotFound();
    var contentType = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".gif" => "image/gif", _ => "application/octet-stream" };
    c.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    return Results.File(Path.GetFullPath(path), contentType);
});
app.MapGet("/api/users", (HttpContext c) => { if(!Admin(c)) return Results.StatusCode(403); using var db=new Db(dbPath); return Results.Ok(db.Users()); });
app.MapPost("/api/users", (HttpContext c, UserRequest r) => {
    if(!Admin(c)) return Results.StatusCode(403);
    if(string.IsNullOrWhiteSpace(r.Username) || string.IsNullOrWhiteSpace(r.Password) || (r.Role != "Admin" && r.Role != "Receptionist"))
        return Results.BadRequest(new { message = "Username, password and a valid role are required." });
    using var db=new Db(dbPath);
    return db.AddUser(r) ? Results.Ok(new { ok = true }) : Results.Conflict(new { message = "Username already exists." });
});
app.MapPut("/api/users/{id:int}", (HttpContext c, int id, UserUpdateRequest r) => {
    var session = Current(c);
    if (session?.Role != "Admin") return Results.StatusCode(403);
    if (string.IsNullOrWhiteSpace(r.Username) || (r.Role != "Admin" && r.Role != "Receptionist"))
        return Results.BadRequest(new { message = "Username and a valid role are required." });
    if (!string.IsNullOrEmpty(r.Password) && r.Password.Length < 8)
        return Results.BadRequest(new { message = "The new password must be at least 8 characters." });
    using var db = new Db(dbPath);
    var existing = db.GetUserForAdmin(id);
    if (existing is null) return Results.NotFound();
    if (string.Equals(existing.Username, session.Username, StringComparison.OrdinalIgnoreCase) && (!r.Active || r.Role != "Admin"))
        return Results.BadRequest(new { message = "You cannot disable or remove administrator access from your own account." });
    if (existing.Role == "Admin" && existing.Active && (!r.Active || r.Role != "Admin") && db.ActiveAdminCount() <= 1)
        return Results.BadRequest(new { message = "At least one active administrator is required." });
    try {
        return db.UpdateUser(id, r) ? Results.Ok(new { ok = true }) : Results.NotFound();
    } catch (SqliteException ex) when (ex.SqliteErrorCode == 19) {
        return Results.Conflict(new { message = "Username already exists." });
    }
});
app.MapGet("/api/data-location", (HttpContext c) => !Admin(c)?Results.StatusCode(403):Results.Ok(new{path=dataRoot,db=dbPath}));
app.MapPost("/api/backup", (HttpContext c) => {
    if(!Admin(c)) return Results.StatusCode(403);
    try
    {
        ClinicSettings settings;
        using (var db = new Db(dbPath)) settings = db.Settings();
        var file = CreateBackup(settings, true);
        return Results.Ok(new { file, message = "Database backup created successfully." });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.BadRequest(new { message = "Backup could not be created because the destination folder is not writable." });
    }
    catch (IOException ex)
    {
        return Results.BadRequest(new { message = "Backup could not be created: " + ex.Message });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Backup failed: " + ex.Message });
    }
});

app.MapPost("/api/documents/{patientId:int}/{visitId:int}", async (HttpContext c,int patientId,int visitId,IFormFile file) => {
    if(!Auth(c)) return Results.Unauthorized(); if(file.Length==0) return Results.BadRequest("Empty file");
    using var db=new Db(dbPath); var patient=db.GetPatient(patientId); if(patient is null || !db.VisitBelongsToPatient(visitId, patientId)) return Results.NotFound();
    var folder=Path.Combine(dataRoot,"Documents","Patients",db.GetPatientCode(patientId)); Directory.CreateDirectory(folder);
    var ext=Path.GetExtension(file.FileName); if(string.IsNullOrWhiteSpace(ext) || ext.Length>10 || ext.IndexOfAny(Path.GetInvalidFileNameChars())>=0) ext=".bin";
    var path=Path.Combine(folder,$"{DateTime.Now:yyyy-MM-dd}_V{visitId}_Prescription{ext}");
    await using(var fs=File.Create(path)) await file.CopyToAsync(fs);
    db.AddDocument(patientId,visitId,Path.GetRelativePath(dataRoot,path),file.FileName);
    return Results.Ok(new{path});
});
app.MapGet("/api/documents/{patientId:int}/{visitId:int}", (HttpContext c,int patientId,int visitId) => { if(!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); return !db.VisitBelongsToPatient(visitId,patientId)?Results.NotFound():Results.Ok(db.Documents(patientId,visitId)); });
app.MapGet("/api/documents/{id:int}/view", (HttpContext c,int id) => {
    if(!Auth(c)) return Results.Unauthorized();
    using var db=new Db(dbPath);
    var d=db.Document(id);
    if(d is null) return Results.NotFound();
    var fullPath=Path.GetFullPath(Path.Combine(dataRoot,d.Value.Path));
    var safeRoot=Path.GetFullPath(dataRoot)+Path.DirectorySeparatorChar;
    return fullPath.StartsWith(safeRoot,StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath)
        ? Results.File(fullPath,"application/pdf",enableRangeProcessing:true)
        : Results.NotFound();
});
app.MapGet("/api/documents/{id:int}/download", (HttpContext c,int id) => {
    if(!Auth(c)) return Results.Unauthorized();
    using var db=new Db(dbPath);
    var d=db.Document(id);
    if(d is null) return Results.NotFound();
    var fullPath = Path.GetFullPath(Path.Combine(dataRoot,d.Value.Path));
    var safeRoot = Path.GetFullPath(dataRoot) + Path.DirectorySeparatorChar;
    return fullPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath)
        ? Results.File(fullPath, "application/octet-stream", d.Value.Name)
        : Results.NotFound();
});
app.MapPost("/api/patient-documents/{patientId:int}", async (HttpContext c, int patientId, IFormFile file) => {
    if (!Auth(c)) return Results.Unauthorized();
    if (file.Length == 0 || file.Length > 10 * 1024 * 1024)
        return Results.BadRequest(new { message = "Choose a file smaller than 10 MB." });

    using var db = new Db(dbPath);
    var patient = db.GetPatient(patientId);
    if (patient is null) return Results.NotFound();

    var folder = Path.Combine(dataRoot, "Documents", "Patients", db.GetPatientCode(patientId));
    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, $"{DateTime.Now:yyyy-MM-dd}_PatientDocument_{Guid.NewGuid():N}.pdf");
    try
    {
        await DocumentFiles.SaveAsPdfAsync(file, path);
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 503);
    }

    var originalName = Path.GetFileName(file.FileName);
    if (DocumentFiles.IsImage(Path.GetExtension(originalName)))
        originalName = Path.GetFileNameWithoutExtension(originalName) + ".pdf";
    var id = db.AddPatientDocument(patientId, Path.GetRelativePath(dataRoot, path), originalName);
    return Results.Ok(new { id, path });
});
app.MapGet("/api/patient-documents/{patientId:int}", (HttpContext c, int patientId) => {
    if (!Auth(c)) return Results.Unauthorized();
    using var db = new Db(dbPath);
    return db.GetPatient(patientId) is null ? Results.NotFound() : Results.Ok(db.PatientDocuments(patientId));
});
app.MapGet("/api/patient-documents/{id:int}/view", (HttpContext c, int id) => {
    if (!Auth(c)) return Results.Unauthorized();
    using var db = new Db(dbPath);
    var d = db.PatientDocument(id);
    if (d is null) return Results.NotFound();
    var fullPath = Path.GetFullPath(Path.Combine(dataRoot, d.Value.Path));
    var safeRoot = Path.GetFullPath(dataRoot) + Path.DirectorySeparatorChar;
    return fullPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath)
        ? Results.File(fullPath, "application/pdf", enableRangeProcessing: true)
        : Results.NotFound();
});
app.MapGet("/api/patient-documents/{id:int}/download", (HttpContext c, int id) => {
    if (!Auth(c)) return Results.Unauthorized();
    using var db = new Db(dbPath);
    var d = db.PatientDocument(id);
    if (d is null) return Results.NotFound();
    var fullPath = Path.GetFullPath(Path.Combine(dataRoot, d.Value.Path));
    var safeRoot = Path.GetFullPath(dataRoot) + Path.DirectorySeparatorChar;
    return fullPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath)
        ? Results.File(fullPath, "application/pdf", d.Value.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? d.Value.Name : d.Value.Name + ".pdf")
        : Results.NotFound();
});
app.MapGet("/api/reports/daily", (HttpContext c,string? date) => { if(!Auth(c)) return Results.Unauthorized(); using var db=new Db(dbPath); return Results.Ok(db.DailyReport(date)); });

_ = Task.Run(async () => {
    while (true) {
        try {
            ClinicSettings s;
            using (var db = new Db(dbPath)) s = db.Settings();
            var now = DateTime.Now;
            if (ScheduledDue(s, now)) {
                CreateBackup(s, false);
                using var db = new Db(dbPath);
                db.MarkScheduledBackup(now.ToString("s"));
            }
        } catch { /* A later scheduler pass retries transient failures. */ }
        await Task.Delay(TimeSpan.FromSeconds(30));
    }
});
app.MapFallbackToFile("index.html");
app.Run();

record LoginRequest(string Username,string Password);
record PatientRequest(string Name,string? Phone,string? Email,string? Address,string? DateOfBirth,string? Gender,string? BloodGroup,string? Allergies,string? MedicalConditions);
record DoctorRequest(string Name,string? Specialization,string? Phone,decimal ConsultationFee,bool Active = true,string? RegistrationNo = null);
record ServiceRequest(string Name,decimal Price,bool Active = true);
record UserRequest(string Username,string Password,string Role);
record UserUpdateRequest(string Username,string Role,bool Active,string? Password);
record ClinicSettings(string ClinicName,string? Address,string? Phone,string? Email,string? Website,string? RegistrationNo,string? TaxNo,string? Currency,string? LogoPath,string? Footer,string? ManualBackupPath = "",string? BackupPath = "",string? BackupSchedule = "Off",string? BackupTime = "02:00",string? BackupDay = "Monday",string? LastScheduledBackup = null,string? ClinicType = "");
record InstallConfig(string? DataPath);
record Session(string Username, string Role, DateTimeOffset Expires);
record VisitRequest(int PatientId,int? DoctorId,string? VisitDate,string? VisitType,string? Notes,decimal ConsultationFee);
record DoctorAvailabilityRequest(int DoctorId,bool Available);
record VisitServiceRequest(int? ServiceId,string Description,decimal Amount);
record PaymentRequest(decimal Amount,string? Method);
