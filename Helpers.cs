using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

static class Helpers
{
    public static InstallConfig LoadConfig(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<InstallConfig>(File.ReadAllText(file)) ?? new(null) : new(null); }
        catch { return new(null); }
    }
    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(destination, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(source)) CopyDirectory(d, Path.Combine(destination, Path.GetFileName(d)));
    }
}

sealed class Db : IDisposable
{
    readonly SqliteConnection C;
    public Db(string path)
    {
        C = new SqliteConnection($"Data Source={path};Cache=Shared");
        C.Open();
        using var cmd = C.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
    }
    public void Initialize()
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Users(Id INTEGER PRIMARY KEY,Username TEXT UNIQUE NOT NULL,PasswordHash TEXT NOT NULL,Role TEXT NOT NULL,Active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS Patients(Id INTEGER PRIMARY KEY AUTOINCREMENT,PatientCode TEXT UNIQUE NOT NULL,Name TEXT NOT NULL,Phone TEXT,Email TEXT,Address TEXT,DateOfBirth TEXT,Gender TEXT,BloodGroup TEXT,Allergies TEXT,MedicalConditions TEXT,CreatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Doctors(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL,Specialization TEXT,Phone TEXT,RegistrationNo TEXT,ConsultationFee REAL NOT NULL DEFAULT 0,Active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS DoctorAvailability(DoctorId INTEGER NOT NULL,AvailabilityDate TEXT NOT NULL,Available INTEGER NOT NULL DEFAULT 0,PRIMARY KEY(DoctorId,AvailabilityDate),FOREIGN KEY(DoctorId) REFERENCES Doctors(Id));
CREATE TABLE IF NOT EXISTS Services(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL,Price REAL NOT NULL DEFAULT 0,Active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS Visits(Id INTEGER PRIMARY KEY AUTOINCREMENT,PatientId INTEGER NOT NULL,DoctorId INTEGER,VisitDate TEXT NOT NULL,VisitType TEXT NOT NULL DEFAULT 'General',Notes TEXT,ConsultationFee REAL NOT NULL DEFAULT 0,FOREIGN KEY(PatientId) REFERENCES Patients(Id),FOREIGN KEY(DoctorId) REFERENCES Doctors(Id));
CREATE TABLE IF NOT EXISTS VisitServices(Id INTEGER PRIMARY KEY AUTOINCREMENT,VisitId INTEGER NOT NULL,ServiceId INTEGER,Description TEXT NOT NULL,Amount REAL NOT NULL,FOREIGN KEY(VisitId) REFERENCES Visits(Id));
CREATE TABLE IF NOT EXISTS Payments(Id INTEGER PRIMARY KEY AUTOINCREMENT,VisitId INTEGER NOT NULL,Amount REAL NOT NULL,Method TEXT, PaidAt TEXT NOT NULL,FOREIGN KEY(VisitId) REFERENCES Visits(Id));
CREATE TABLE IF NOT EXISTS Documents(Id INTEGER PRIMARY KEY AUTOINCREMENT,PatientId INTEGER NOT NULL,VisitId INTEGER NOT NULL,FilePath TEXT NOT NULL,OriginalName TEXT,DocumentType TEXT NOT NULL DEFAULT 'Prescription',CreatedAt TEXT NOT NULL,FOREIGN KEY(PatientId) REFERENCES Patients(Id),FOREIGN KEY(VisitId) REFERENCES Visits(Id));
 CREATE TABLE IF NOT EXISTS PatientDocuments(Id INTEGER PRIMARY KEY AUTOINCREMENT,PatientId INTEGER NOT NULL,FilePath TEXT NOT NULL,OriginalName TEXT,DocumentType TEXT NOT NULL DEFAULT 'Patient document',CreatedAt TEXT NOT NULL,FOREIGN KEY(PatientId) REFERENCES Patients(Id));
  CREATE TABLE IF NOT EXISTS ClinicSettings(Id INTEGER PRIMARY KEY CHECK(Id=1),ClinicName TEXT NOT NULL,Address TEXT,Phone TEXT,Email TEXT,Website TEXT,RegistrationNo TEXT,TaxNo TEXT,Currency TEXT NOT NULL DEFAULT 'SGD',LogoPath TEXT,Footer TEXT,ManualBackupPath TEXT,BackupPath TEXT,BackupSchedule TEXT NOT NULL DEFAULT 'Off',BackupTime TEXT NOT NULL DEFAULT '02:00',BackupDay TEXT NOT NULL DEFAULT 'Monday',LastScheduledBackup TEXT,ClinicType TEXT);";
        cmd.ExecuteNonQuery();
        AddColumnIfMissing("Patients", "BloodGroup", "TEXT");
        AddColumnIfMissing("Patients", "Allergies", "TEXT");
        AddColumnIfMissing("Patients", "MedicalConditions", "TEXT");
        AddColumnIfMissing("Visits", "VisitType", "TEXT NOT NULL DEFAULT 'General'");
        AddColumnIfMissing("Doctors", "RegistrationNo", "TEXT");
        AddColumnIfMissing("ClinicSettings", "ManualBackupPath", "TEXT");
        AddColumnIfMissing("ClinicSettings", "BackupPath", "TEXT");
        AddColumnIfMissing("ClinicSettings", "BackupSchedule", "TEXT NOT NULL DEFAULT 'Off'");
        AddColumnIfMissing("ClinicSettings", "BackupTime", "TEXT NOT NULL DEFAULT '02:00'");
        AddColumnIfMissing("ClinicSettings", "BackupDay", "TEXT NOT NULL DEFAULT 'Monday'");
        AddColumnIfMissing("ClinicSettings", "LastScheduledBackup", "TEXT");
        AddColumnIfMissing("ClinicSettings", "ClinicType", "TEXT");
        if (Scalar<long>("SELECT COUNT(*) FROM Users") == 0) { AddUser(new("admin", "Admin@123", "Admin")); AddUser(new("receptionist", "Reception@123", "Receptionist")); }
        if (Scalar<long>("SELECT COUNT(*) FROM ClinicSettings") == 0) SaveSettings(new("My Clinic", "", "", "", "", "", "", "SGD", "", "", "", "", "Off", "02:00", "Monday", null));
    }
    T Scalar<T>(string sql) { using var c = C.CreateCommand(); c.CommandText = sql; return (T)Convert.ChangeType(c.ExecuteScalar()!, typeof(T)); }
    public UserRow? FindUser(string u) { using var c = C.CreateCommand(); c.CommandText = "SELECT Username,PasswordHash,Role FROM Users WHERE Username=$u AND Active=1"; c.Parameters.AddWithValue("$u", u); using var r = c.ExecuteReader(); return r.Read() ? new(r.GetString(0), r.GetString(1), r.GetString(2)) : null; }
    public bool AddUser(UserRequest x) { using var c = C.CreateCommand(); c.CommandText = "INSERT OR IGNORE INTO Users(Username,PasswordHash,Role) VALUES($u,$p,$r)"; c.Parameters.AddWithValue("$u", x.Username.Trim()); c.Parameters.AddWithValue("$p", PasswordHasher.Hash(x.Password)); c.Parameters.AddWithValue("$r", x.Role); return c.ExecuteNonQuery() == 1; }
    public object Users() { using var c = C.CreateCommand(); c.CommandText = "SELECT Id,Username,Role,Active FROM Users ORDER BY Username"; using var r = c.ExecuteReader(); var a = new List<object>(); while (r.Read()) a.Add(new { Id = r.GetInt64(0), Username = r.GetString(1), Role = r.GetString(2), Active = r.GetInt64(3) == 1 }); return a; }
    public UserAdminRow? GetUserForAdmin(int id) { using var c = C.CreateCommand(); c.CommandText = "SELECT Username,Role,Active FROM Users WHERE Id=$i"; c.Parameters.AddWithValue("$i", id); using var r = c.ExecuteReader(); return r.Read() ? new(r.GetString(0), r.GetString(1), r.GetInt64(2) == 1) : null; }
    public long ActiveAdminCount() => Scalar<long>("SELECT COUNT(*) FROM Users WHERE Role='Admin' AND Active=1");
    public bool UpdateUser(int id, UserUpdateRequest x) { using var c = C.CreateCommand(); c.CommandText = string.IsNullOrEmpty(x.Password) ? "UPDATE Users SET Username=$u,Role=$r,Active=$a WHERE Id=$i" : "UPDATE Users SET Username=$u,Role=$r,Active=$a,PasswordHash=$p WHERE Id=$i"; c.Parameters.AddWithValue("$i", id); c.Parameters.AddWithValue("$u", x.Username.Trim()); c.Parameters.AddWithValue("$r", x.Role); c.Parameters.AddWithValue("$a", x.Active ? 1 : 0); if (!string.IsNullOrEmpty(x.Password)) c.Parameters.AddWithValue("$p", PasswordHasher.Hash(x.Password)); return c.ExecuteNonQuery() == 1; }
    public object Dashboard() => new {
        patients = Scalar<long>("SELECT COUNT(*) FROM Patients"),
        visits = Scalar<long>("SELECT COUNT(*) FROM Visits WHERE date(VisitDate)=date('now','localtime')"),
        newPatientsThisMonth = Scalar<long>("SELECT COUNT(*) FROM Patients WHERE strftime('%Y-%m',CreatedAt)=strftime('%Y-%m','now','localtime')"),
        recentVisits = ((List<object>)Visits(null)).Take(5).ToList()
    };
    public object ClinicProfile() { using var c = C.CreateCommand(); c.CommandText = "SELECT ClinicName,LogoPath,Currency FROM ClinicSettings WHERE Id=1"; using var r = c.ExecuteReader(); r.Read(); return new { ClinicName = r.GetString(0), LogoPath = r.IsDBNull(1) ? "" : r.GetString(1), Currency = r.IsDBNull(2) ? "SGD" : r.GetString(2) }; }
    public object SearchPatients(string? q) { using var c = C.CreateCommand(); c.CommandText = "SELECT Id,PatientCode,Name,Phone,Email FROM Patients WHERE $q='' OR PatientCode LIKE $w OR Name LIKE $w OR Phone LIKE $w ORDER BY Name LIMIT 200"; var s = q ?? ""; c.Parameters.AddWithValue("$q", s); c.Parameters.AddWithValue("$w", $"%{s}%"); using var r = c.ExecuteReader(); var a = new List<object>(); while (r.Read()) a.Add(new { Id = r.GetInt64(0), PatientCode = r.GetString(1), Name = r.GetString(2), Phone = r.IsDBNull(3) ? "" : r.GetString(3), Email = r.IsDBNull(4) ? "" : r.GetString(4) }); return a; }
    public object AddPatient(PatientRequest x) { var code = $"P{Scalar<long>("SELECT COALESCE(MAX(Id),0)+1 FROM Patients"):D6}"; using var c = C.CreateCommand(); c.CommandText = "INSERT INTO Patients(PatientCode,Name,Phone,Email,Address,DateOfBirth,Gender,BloodGroup,Allergies,MedicalConditions,CreatedAt) VALUES($c,$n,$p,$e,$a,$d,$g,$b,$al,$mc,$t);SELECT last_insert_rowid();"; foreach (var p in new[] { ("$c", (object)code), ("$n", x.Name.Trim()), ("$p", x.Phone ?? ""), ("$e", x.Email ?? ""), ("$a", x.Address ?? ""), ("$d", x.DateOfBirth ?? ""), ("$g", x.Gender ?? ""), ("$b", x.BloodGroup ?? ""), ("$al", x.Allergies ?? ""), ("$mc", x.MedicalConditions ?? ""), ("$t", DateTime.Now.ToString("s")) }) c.Parameters.AddWithValue(p.Item1, p.Item2); var id = Convert.ToInt64(c.ExecuteScalar()); return new { Id = id, PatientCode = code }; }
    public bool UpdatePatient(int id, PatientRequest x) { using var c = C.CreateCommand(); c.CommandText = "UPDATE Patients SET Name=$n,Phone=$p,Email=$e,Address=$a,DateOfBirth=$d,Gender=$g,BloodGroup=$b,Allergies=$al,MedicalConditions=$mc WHERE Id=$i"; c.Parameters.AddWithValue("$i", id); c.Parameters.AddWithValue("$n", x.Name.Trim()); c.Parameters.AddWithValue("$p", x.Phone ?? ""); c.Parameters.AddWithValue("$e", x.Email ?? ""); c.Parameters.AddWithValue("$a", x.Address ?? ""); c.Parameters.AddWithValue("$d", x.DateOfBirth ?? ""); c.Parameters.AddWithValue("$g", x.Gender ?? ""); c.Parameters.AddWithValue("$b", x.BloodGroup ?? ""); c.Parameters.AddWithValue("$al", x.Allergies ?? ""); c.Parameters.AddWithValue("$mc", x.MedicalConditions ?? ""); return c.ExecuteNonQuery() == 1; }
    public object? GetPatient(int id) { using var c = C.CreateCommand(); c.CommandText = "SELECT Id,PatientCode,Name,Phone,Email,Address,DateOfBirth,Gender,BloodGroup,Allergies,MedicalConditions FROM Patients WHERE Id=$i"; c.Parameters.AddWithValue("$i", id); using var r = c.ExecuteReader(); if (!r.Read()) return null; return new { Id = r.GetInt64(0), PatientCode = r.GetString(1), Name = r.GetString(2), Phone = r.IsDBNull(3) ? "" : r.GetString(3), Email = r.IsDBNull(4) ? "" : r.GetString(4), Address = r.IsDBNull(5) ? "" : r.GetString(5), DateOfBirth = r.IsDBNull(6) ? "" : r.GetString(6), Gender = r.IsDBNull(7) ? "" : r.GetString(7), BloodGroup = r.IsDBNull(8) ? "" : r.GetString(8), Allergies = r.IsDBNull(9) ? "" : r.GetString(9), MedicalConditions = r.IsDBNull(10) ? "" : r.GetString(10) }; }
    public string GetPatientCode(int id) { using var c = C.CreateCommand(); c.CommandText = "SELECT PatientCode FROM Patients WHERE Id=$i"; c.Parameters.AddWithValue("$i", id); return Convert.ToString(c.ExecuteScalar())!; }
    public bool VisitBelongsToPatient(int visitId, int patientId) { using var c=C.CreateCommand(); c.CommandText="SELECT 1 FROM Visits WHERE Id=$v AND PatientId=$p"; c.Parameters.AddWithValue("$v",visitId); c.Parameters.AddWithValue("$p",patientId); return c.ExecuteScalar() is not null; }
    public object Doctors(bool availableOnly = false)
    {
        using var c = C.CreateCommand();
        c.CommandText = availableOnly
            ? "SELECT d.Id,d.Name,d.Specialization,d.Phone,d.RegistrationNo,d.ConsultationFee,d.Active FROM Doctors d JOIN DoctorAvailability a ON a.DoctorId=d.Id AND a.AvailabilityDate=$date AND a.Available=1 WHERE d.Active=1 ORDER BY d.Name"
            : "SELECT Id,Name,Specialization,Phone,RegistrationNo,ConsultationFee,Active FROM Doctors ORDER BY Name";
        if (availableOnly) c.Parameters.AddWithValue("$date", DateTime.Now.ToString("yyyy-MM-dd"));
        using var r = c.ExecuteReader();
        var a = new List<object>();
        while (r.Read())
            a.Add(new { id = r.GetInt64(0), name = r.GetString(1), specialization = r.IsDBNull(2) ? "" : r.GetString(2), phone = r.IsDBNull(3) ? "" : r.GetString(3), registrationNo = r.IsDBNull(4) ? "" : r.GetString(4), consultationFee = r.GetDecimal(5), active = r.GetInt64(6) == 1 });
        return a;
    }
    public object DoctorAvailability()
    {
        using var c = C.CreateCommand();
        c.CommandText = @"SELECT d.Id,d.Name,d.Specialization,d.RegistrationNo,
                                 CASE WHEN a.Available=1 THEN 1 ELSE 0 END
                          FROM Doctors d
                          LEFT JOIN DoctorAvailability a
                            ON a.DoctorId=d.Id AND a.AvailabilityDate=$date
                          WHERE d.Active=1
                          ORDER BY d.Name";
        c.Parameters.AddWithValue("$date", DateTime.Now.ToString("yyyy-MM-dd"));
        using var r = c.ExecuteReader();
        var list = new List<object>();
        while (r.Read())
            list.Add(new { id = r.GetInt64(0), name = r.GetString(1), specialization = r.IsDBNull(2) ? "" : r.GetString(2), registrationNo = r.IsDBNull(3) ? "" : r.GetString(3), available = r.GetInt64(4) == 1 });
        return list;
    }
    public bool DoctorAvailableToday(int id)
    {
        using var c = C.CreateCommand();
        c.CommandText = @"SELECT EXISTS(
                              SELECT 1 FROM Doctors d
                              JOIN DoctorAvailability a ON a.DoctorId=d.Id
                              WHERE d.Id=$id AND d.Active=1 AND a.AvailabilityDate=$date AND a.Available=1)";
        c.Parameters.AddWithValue("$id", id);
        c.Parameters.AddWithValue("$date", DateTime.Now.ToString("yyyy-MM-dd"));
        return Convert.ToInt64(c.ExecuteScalar()) == 1;
    }
    public void SetDoctorAvailability(int id, bool available)
    {
        if (!DoctorIsActive(id)) throw new InvalidOperationException("Only active doctors can be marked available.");
        using var c = C.CreateCommand();
        c.CommandText = @"INSERT INTO DoctorAvailability(DoctorId,AvailabilityDate,Available)
                          VALUES($id,$date,$available)
                          ON CONFLICT(DoctorId,AvailabilityDate)
                          DO UPDATE SET Available=$available";
        c.Parameters.AddWithValue("$id", id);
        c.Parameters.AddWithValue("$date", DateTime.Now.ToString("yyyy-MM-dd"));
        c.Parameters.AddWithValue("$available", available ? 1 : 0);
        c.ExecuteNonQuery();
    }
    bool DoctorIsActive(int id)
    {
        using var c = C.CreateCommand();
        c.CommandText = "SELECT Active FROM Doctors WHERE Id=$id";
        c.Parameters.AddWithValue("$id", id);
        var value = c.ExecuteScalar();
        return value is not null && Convert.ToInt64(value) == 1;
    }
    public bool DoctorAssignmentIsAvailable(int? id) => !id.HasValue || DoctorAvailableToday(id.Value);
    public void AddDoctor(DoctorRequest x) { using var c = C.CreateCommand(); c.CommandText = "INSERT INTO Doctors(Name,Specialization,Phone,RegistrationNo,ConsultationFee) VALUES($n,$s,$p,$r,$f)"; c.Parameters.AddWithValue("$n", x.Name.Trim()); c.Parameters.AddWithValue("$s", x.Specialization?.Trim() ?? ""); c.Parameters.AddWithValue("$p", x.Phone?.Trim() ?? ""); c.Parameters.AddWithValue("$r", x.RegistrationNo?.Trim() ?? ""); c.Parameters.AddWithValue("$f", x.ConsultationFee); c.ExecuteNonQuery(); }
    public bool UpdateDoctor(int id, DoctorRequest x) { using var c = C.CreateCommand(); c.CommandText = "UPDATE Doctors SET Name=$n,Specialization=$s,Phone=$p,RegistrationNo=$r,ConsultationFee=$f,Active=$a WHERE Id=$i"; c.Parameters.AddWithValue("$i", id); c.Parameters.AddWithValue("$n", x.Name.Trim()); c.Parameters.AddWithValue("$s", x.Specialization ?? ""); c.Parameters.AddWithValue("$p", x.Phone ?? ""); c.Parameters.AddWithValue("$r", x.RegistrationNo?.Trim() ?? ""); c.Parameters.AddWithValue("$f", x.ConsultationFee); c.Parameters.AddWithValue("$a", x.Active ? 1 : 0); var updated = c.ExecuteNonQuery() == 1; if (updated && !x.Active) { using var availability = C.CreateCommand(); availability.CommandText = "UPDATE DoctorAvailability SET Available=0 WHERE DoctorId=$i"; availability.Parameters.AddWithValue("$i", id); availability.ExecuteNonQuery(); } return updated; }
    public bool DoctorHasVisits(int id) { using var c=C.CreateCommand(); c.CommandText="SELECT EXISTS(SELECT 1 FROM Visits WHERE DoctorId=$i)"; c.Parameters.AddWithValue("$i",id); return Convert.ToInt64(c.ExecuteScalar())==1; }
    public bool DeleteDoctor(int id) { using var c=C.CreateCommand(); c.CommandText="DELETE FROM Doctors WHERE Id=$i"; c.Parameters.AddWithValue("$i",id); return c.ExecuteNonQuery()==1; }
    public object Services()
    {
        using var c = C.CreateCommand();
        c.CommandText = "SELECT Id,Name,Price,Active FROM Services WHERE Active=1 ORDER BY Name";
        using var r = c.ExecuteReader();
        var a = new List<object>();
        while (r.Read())
            a.Add(new
            {
                id = r.GetInt64(0),
                name = r.GetString(1),
                price = r.GetDecimal(2),
                active = r.GetInt64(3) == 1
            });
        return a;
    }
    public void AddService(ServiceRequest x) { using var c = C.CreateCommand(); c.CommandText = "INSERT INTO Services(Name,Price) VALUES($n,$p)"; c.Parameters.AddWithValue("$n", x.Name); c.Parameters.AddWithValue("$p", x.Price); c.ExecuteNonQuery(); }
    public bool UpdateService(int id, ServiceRequest x) { using var c = C.CreateCommand(); c.CommandText = "UPDATE Services SET Name=$n,Price=$p,Active=$a WHERE Id=$i"; c.Parameters.AddWithValue("$i", id); c.Parameters.AddWithValue("$n", x.Name.Trim()); c.Parameters.AddWithValue("$p", x.Price); c.Parameters.AddWithValue("$a", x.Active ? 1 : 0); return c.ExecuteNonQuery() == 1; }
    public ClinicSettings Settings() { using var c = C.CreateCommand(); c.CommandText = "SELECT ClinicName,Address,Phone,Email,Website,RegistrationNo,TaxNo,Currency,LogoPath,Footer,ManualBackupPath,BackupPath,BackupSchedule,BackupTime,BackupDay,LastScheduledBackup,ClinicType FROM ClinicSettings WHERE Id=1"; using var r = c.ExecuteReader(); r.Read(); return new ClinicSettings(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), r.IsDBNull(10) ? "" : r.GetString(10), r.IsDBNull(11) ? "" : r.GetString(11), r.GetString(12), r.GetString(13), r.GetString(14), r.IsDBNull(15) ? null : r.GetString(15), r.IsDBNull(16) ? "" : r.GetString(16)); }
    public void SaveSettings(ClinicSettings x) { using var c = C.CreateCommand(); c.CommandText = "INSERT INTO ClinicSettings(Id,ClinicName,Address,Phone,Email,Website,RegistrationNo,TaxNo,Currency,LogoPath,Footer,ManualBackupPath,BackupPath,BackupSchedule,BackupTime,BackupDay,ClinicType) VALUES(1,$n,$a,$p,$e,$w,$r,$t,$c,$l,$f,$mbp,$bp,$bs,$bt,$bd,$ct) ON CONFLICT(Id) DO UPDATE SET ClinicName=$n,Address=$a,Phone=$p,Email=$e,Website=$w,RegistrationNo=$r,TaxNo=$t,Currency=$c,LogoPath=$l,Footer=$f,ManualBackupPath=$mbp,BackupPath=$bp,BackupSchedule=$bs,BackupTime=$bt,BackupDay=$bd,ClinicType=$ct"; foreach (var p in new[] { ("$n", x.ClinicName), ("$a", x.Address ?? ""), ("$p", x.Phone ?? ""), ("$e", x.Email ?? ""), ("$w", x.Website ?? ""), ("$r", x.RegistrationNo ?? ""), ("$t", x.TaxNo ?? ""), ("$c", x.Currency ?? "SGD"), ("$l", x.LogoPath ?? ""), ("$f", x.Footer ?? ""), ("$mbp", x.ManualBackupPath ?? ""), ("$bp", x.BackupPath ?? ""), ("$bs", x.BackupSchedule ?? "Off"), ("$bt", x.BackupTime ?? "02:00"), ("$bd", x.BackupDay ?? "Monday"), ("$ct", x.ClinicType ?? "") }) c.Parameters.AddWithValue(p.Item1, p.Item2); c.ExecuteNonQuery(); }
    public void MarkScheduledBackup(string when) { using var c = C.CreateCommand(); c.CommandText = "UPDATE ClinicSettings SET LastScheduledBackup=$t WHERE Id=1"; c.Parameters.AddWithValue("$t", when); c.ExecuteNonQuery(); }
    public void AddDocument(int p, int v, string path, string original) { using var c = C.CreateCommand(); c.CommandText = "INSERT INTO Documents(PatientId,VisitId,FilePath,OriginalName,CreatedAt) VALUES($p,$v,$f,$o,$t)"; c.Parameters.AddWithValue("$p", p); c.Parameters.AddWithValue("$v", v); c.Parameters.AddWithValue("$f", path); c.Parameters.AddWithValue("$o", original); c.Parameters.AddWithValue("$t", DateTime.Now.ToString("s")); c.ExecuteNonQuery(); }
    public object AddVisit(VisitRequest x) { if (!DoctorAssignmentIsAvailable(x.DoctorId)) throw new InvalidOperationException("The selected doctor is not available today."); using var c=C.CreateCommand(); c.CommandText="INSERT INTO Visits(PatientId,DoctorId,VisitDate,VisitType,Notes,ConsultationFee) VALUES($p,$d,$v,$ty,$n,$f);SELECT last_insert_rowid()"; c.Parameters.AddWithValue("$p",x.PatientId); c.Parameters.AddWithValue("$d",x.DoctorId??(object)DBNull.Value); c.Parameters.AddWithValue("$v",string.IsNullOrWhiteSpace(x.VisitDate)?DateTime.Now.ToString("s"):x.VisitDate); c.Parameters.AddWithValue("$ty",string.IsNullOrWhiteSpace(x.VisitType)?"General":x.VisitType.Trim()); c.Parameters.AddWithValue("$n",x.Notes??""); c.Parameters.AddWithValue("$f",x.ConsultationFee); return new { id=Convert.ToInt64(c.ExecuteScalar()), patientId=x.PatientId }; }
    public object Visits(int? patientId) { using var c=C.CreateCommand(); c.CommandText="SELECT v.Id,v.PatientId,v.DoctorId,v.VisitDate,v.VisitType,v.Notes,v.ConsultationFee,p.PatientCode,p.Name,COALESCE(d.Name,'') DoctorName,COALESCE((SELECT SUM(Amount) FROM VisitServices WHERE VisitId=v.Id),0)+v.ConsultationFee Total,COALESCE((SELECT SUM(Amount) FROM Payments WHERE VisitId=v.Id),0) Paid FROM Visits v JOIN Patients p ON p.Id=v.PatientId LEFT JOIN Doctors d ON d.Id=v.DoctorId WHERE ($p IS NULL OR v.PatientId=$p) ORDER BY v.VisitDate DESC"; c.Parameters.AddWithValue("$p",patientId??(object)DBNull.Value); using var r=c.ExecuteReader(); var a=new List<object>(); while(r.Read()) a.Add(new {id=r.GetInt64(0),patientId=r.GetInt64(1),doctorId=r.IsDBNull(2)?(long?)null:r.GetInt64(2),visitDate=r.GetString(3),visitType=r.IsDBNull(4)?"General":r.GetString(4),notes=r.GetString(5),consultationFee=r.GetDecimal(6),patientCode=r.GetString(7),patientName=r.GetString(8),doctorName=r.GetString(9),total=r.GetDecimal(10),paid=r.GetDecimal(11),balance=r.GetDecimal(10)-r.GetDecimal(11)}); return a; }
    public object? Visit(int id) { var all=(List<object>)Visits(null); return all.FirstOrDefault(x => (long)x.GetType().GetProperty("id")!.GetValue(x)! == id); }
    public object AddVisitService(int id,VisitServiceRequest x) { using var c=C.CreateCommand(); c.CommandText="INSERT INTO VisitServices(VisitId,ServiceId,Description,Amount) VALUES($v,$s,$d,$a);SELECT last_insert_rowid()"; c.Parameters.AddWithValue("$v",id); c.Parameters.AddWithValue("$s",x.ServiceId??(object)DBNull.Value); c.Parameters.AddWithValue("$d",x.Description); c.Parameters.AddWithValue("$a",x.Amount); return new{id=Convert.ToInt64(c.ExecuteScalar()),visitId=id}; }
    public object AddPayment(int id,PaymentRequest x) { using var c=C.CreateCommand(); c.CommandText="INSERT INTO Payments(VisitId,Amount,Method,PaidAt) VALUES($v,$a,$m,$t);SELECT last_insert_rowid()"; c.Parameters.AddWithValue("$v",id); c.Parameters.AddWithValue("$a",x.Amount); c.Parameters.AddWithValue("$m",x.Method??""); c.Parameters.AddWithValue("$t",DateTime.Now.ToString("s")); return new{id=Convert.ToInt64(c.ExecuteScalar()),visitId=id,amount=x.Amount}; }
    public object PaymentHistory(int patientId)
    {
        using var c = C.CreateCommand();
        c.CommandText = @"SELECT pay.Id,pay.PaidAt,pay.Amount,
                                 COALESCE(NULLIF((SELECT group_concat(vs.Description, ', ')
                                                  FROM VisitServices vs
                                                  WHERE vs.VisitId=v.Id), ''),
                                          NULLIF(v.VisitType, ''), 'Consultation')
                          FROM Payments pay
                          JOIN Visits v ON v.Id=pay.VisitId
                          WHERE v.PatientId=$p
                          ORDER BY pay.PaidAt DESC,pay.Id DESC";
        c.Parameters.AddWithValue("$p", patientId);
        using var r = c.ExecuteReader();
        var payments = new List<object>();
        while (r.Read())
        {
            payments.Add(new
            {
                id = r.GetInt64(0),
                paymentDate = r.GetString(1),
                amountPaid = r.GetDecimal(2),
                service = r.GetString(3)
            });
        }
        return payments;
    }
    public object PatientHistory(int id) => Visits(id);
    public object Documents(int patientId,int visitId) { using var c=C.CreateCommand(); c.CommandText="SELECT Id id,OriginalName name,FilePath path,CreatedAt createdAt FROM Documents WHERE PatientId=$p AND VisitId=$v ORDER BY Id DESC"; c.Parameters.AddWithValue("$p",patientId); c.Parameters.AddWithValue("$v",visitId); using var r=c.ExecuteReader(); var list=new List<Dictionary<string,object?>>(); while(r.Read()){var d=new Dictionary<string,object?>();for(int i=0;i<r.FieldCount;i++)d[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);list.Add(d);} return list; }
    public object PatientDocuments(int patientId)
    {
        using var c = C.CreateCommand();
        c.CommandText = @"SELECT Id,OriginalName,CreatedAt,VisitId,0 AS PatientLevel FROM Documents WHERE PatientId=$p
                          UNION ALL
                          SELECT Id,OriginalName,CreatedAt,NULL AS VisitId,1 AS PatientLevel FROM PatientDocuments WHERE PatientId=$p
                          ORDER BY CreatedAt DESC, Id DESC";
        c.Parameters.AddWithValue("$p", patientId);
        using var r = c.ExecuteReader();
        var list = new List<object>();
        while (r.Read())
        {
            list.Add(new
            {
                id = r.GetInt64(0),
                name = r.IsDBNull(1) ? "Document" : r.GetString(1),
                createdAt = r.GetString(2),
                visitId = r.IsDBNull(3) ? (long?)null : r.GetInt64(3),
                patientLevel = r.GetInt64(4) == 1
            });
        }
        return list;
    }
    public long AddPatientDocument(int patientId, string path, string original)
    {
        using var c = C.CreateCommand();
        c.CommandText = "INSERT INTO PatientDocuments(PatientId,FilePath,OriginalName,CreatedAt) VALUES($p,$f,$o,$t);SELECT last_insert_rowid();";
        c.Parameters.AddWithValue("$p", patientId);
        c.Parameters.AddWithValue("$f", path);
        c.Parameters.AddWithValue("$o", original);
        c.Parameters.AddWithValue("$t", DateTime.Now.ToString("s"));
        return Convert.ToInt64(c.ExecuteScalar());
    }
    public (string Path,string Name)? PatientDocument(int id)
    {
        using var c = C.CreateCommand();
        c.CommandText = "SELECT FilePath,COALESCE(OriginalName,'document') FROM PatientDocuments WHERE Id=$i";
        c.Parameters.AddWithValue("$i", id);
        using var r = c.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.GetString(1)) : null;
    }
    public void BackupTo(string destinationPath)
    {
        if (File.Exists(destinationPath)) File.Delete(destinationPath);

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = C.Database,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        };

        using (var source = new SqliteConnection(csb.ToString()))
        {
            source.Open();
            using var cmd = source.CreateCommand();
            cmd.CommandText = "PRAGMA busy_timeout=10000; VACUUM INTO $p;";
            cmd.Parameters.AddWithValue("$p", destinationPath);
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length < 1024)
            throw new IOException("The SQLite backup file was not created correctly.");

        var verifyCsb = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
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
    }
    public (string Path,string Name)? Document(int id) { using var c=C.CreateCommand(); c.CommandText="SELECT FilePath,COALESCE(OriginalName,'document') FROM Documents WHERE Id=$i"; c.Parameters.AddWithValue("$i",id); using var r=c.ExecuteReader(); return r.Read()?(r.GetString(0),r.GetString(1)):null; }
    public object DailyReport(string? date) { var d=string.IsNullOrWhiteSpace(date)?DateTime.Now.ToString("yyyy-MM-dd"):date; using var c=C.CreateCommand(); c.CommandText="SELECT COUNT(*) visits,COALESCE(SUM(v.ConsultationFee),0)+COALESCE((SELECT SUM(Amount) FROM VisitServices WHERE VisitId IN (SELECT Id FROM Visits WHERE date(VisitDate)=$d)),0) billed,COALESCE((SELECT SUM(Amount) FROM Payments WHERE date(PaidAt)=$d),0) collected FROM Visits v WHERE date(v.VisitDate)=$d"; c.Parameters.AddWithValue("$d",d); using var r=c.ExecuteReader(); r.Read(); return new{date=d,visits=r.GetInt64(0),billed=r.GetDecimal(1),collected=r.GetDecimal(2),balance=r.GetDecimal(1)-r.GetDecimal(2)}; }
    void AddColumnIfMissing(string table, string column, string definition) { using var c=C.CreateCommand(); c.CommandText="SELECT COUNT(*) FROM pragma_table_info($table) WHERE name=$column"; c.Parameters.AddWithValue("$table",table); c.Parameters.AddWithValue("$column",column); if(Convert.ToInt64(c.ExecuteScalar())==0){c.Parameters.Clear();c.CommandText=$"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";c.ExecuteNonQuery();} }
    object Query(string sql) { using var c = C.CreateCommand(); c.CommandText = sql; using var r = c.ExecuteReader(); var list = new List<Dictionary<string, object?>>(); while (r.Read()) { var d = new Dictionary<string, object?>(); for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i); list.Add(d); } return list; }
    public void Dispose() => C.Dispose();
}

record UserRow(string Username, string PasswordHash, string Role);
record UserAdminRow(string Username, string Role, bool Active);
static class PasswordHasher
{
    public static string Hash(string p) { var salt = RandomNumberGenerator.GetBytes(16); var key = Rfc2898DeriveBytes.Pbkdf2(p, salt, 120000, HashAlgorithmName.SHA256, 32); return $"PBKDF2$120000${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}"; }
    public static bool Verify(string p, string stored) { try { var x = stored.Split('$'); var key = Rfc2898DeriveBytes.Pbkdf2(p, Convert.FromBase64String(x[2]), int.Parse(x[1]), HashAlgorithmName.SHA256, 32); return CryptographicOperations.FixedTimeEquals(key, Convert.FromBase64String(x[3])); } catch { return false; } }
}