#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DATA="$(mktemp -d)"
PORT="${CLINIC_TEST_PORT:-5099}"
COOKIE="$DATA/cookie"
export CLINIC_DATA_PATH="$DATA/data" CLINIC_PORT="$PORT"
cleanup(){ [[ -n "${PID:-}" ]] && kill "$PID" 2>/dev/null || true; rm -rf "$DATA"; }
trap cleanup EXIT
dotnet run --project "$ROOT/ClinicManagement.csproj" --no-launch-profile >"$DATA/app.log" 2>&1 & PID=$!
for i in {1..50}; do curl -sf "http://127.0.0.1:$PORT/api/session" >/dev/null && break; sleep .2; done
base="http://127.0.0.1:$PORT"
request(){ curl -fsS -b "$COOKIE" -c "$COOKIE" "$@"; }
status(){ curl -sS -o /dev/null -w '%{http_code}' -b "$COOKIE" -c "$COOKIE" "$@"; }
grepq(){ grep -q "$1" <<<"$2"; }
session="$(curl -fsS "$base/api/session")"; grepq '"authenticated":false' "$session"
login="$(curl -fsS -c "$COOKIE" -H 'Content-Type: application/json' -d '{"Username":"admin","Password":"Admin@123"}' "$base/api/login")"; grepq '"role":"Admin"' "$login"
browse="$(curl -sS -X POST -b "$COOKIE" "$base/api/browse-folder")"; grepq 'Windows app' "$browse"
p="$(request -H 'Content-Type: application/json' -d '{"Name":"Integration Patient","Phone":"555","BloodGroup":"O+","Allergies":"Penicillin","MedicalConditions":"Asthma"}' "$base/api/patients")"; grepq 'patientCode' "$p"; pid="$(sed -E 's/.*"id":([0-9]+).*/\1/' <<<"$p")"
list="$(request "$base/api/patients?q=Integration")"; grepq 'Integration Patient' "$list"; patient="$(request "$base/api/patients/$pid")"; grepq '"bloodGroup":"O+"' "$patient"; grepq '"allergies":"Penicillin"' "$patient"; grepq '"medicalConditions":"Asthma"' "$patient"; request "$base/api/patients/$pid/history" >/dev/null
updated="$(request -X PUT -H 'Content-Type: application/json' -d '{"Name":"Updated Patient","Phone":"777","BloodGroup":"A+","Allergies":"","MedicalConditions":"Diabetes"}' "$base/api/patients/$pid")"; grepq '"name":"Updated Patient"' "$updated"; grepq '"bloodGroup":"A+"' "$updated"
[[ "$(status -X PUT -H 'Content-Type: application/json' -d '{"Name":"Nobody"}' "$base/api/patients/99999")" == 404 ]]
request -H 'Content-Type: application/json' -d '{"Name":"Integration Doctor","Specialization":"General","Phone":"555-0100","ConsultationFee":50}' "$base/api/doctors" >/dev/null
doctor="$(request "$base/api/doctors")"; grepq '"name":"Integration Doctor"' "$doctor"; grepq '"phone":"555-0100"' "$doctor"; didoctor="$(sed -E 's/.*"id":([0-9]+).*/\1/' <<<"$doctor")"; request -X PUT -H 'Content-Type: application/json' -d '{"Name":"Updated Doctor","Specialization":"Family","Phone":"555-0200","ConsultationFee":60,"Active":false}' "$base/api/doctors/$didoctor" >/dev/null; doctor="$(request "$base/api/doctors")"; grepq '"name":"Updated Doctor"' "$doctor"; grepq '"specialization":"Family"' "$doctor"; grepq '"active":false' "$doctor"; [[ "$(status -X PUT -H 'Content-Type: application/json' -d '{"Name":"Nobody","Price":1}' "$base/api/doctors/99999")" == 404 ]]
request -H 'Content-Type: application/json' -d '{"Name":"Removable Doctor","ConsultationFee":25}' "$base/api/doctors" >/dev/null; removableid="$(sqlite3 "$DATA/data/clinic.db" "SELECT Id FROM Doctors WHERE Name='Removable Doctor';")"; request -X DELETE "$base/api/doctors/$removableid" >/dev/null; doctor="$(request "$base/api/doctors")"; ! grepq 'Removable Doctor' "$doctor"
request -H 'Content-Type: application/json' -d '{"Name":"Integration Service","Price":25}' "$base/api/services" >/dev/null
service="$(request "$base/api/services")"; diservice="$(sed -E 's/.*"Id":([0-9]+).*/\1/' <<<"$service")"; request -X PUT -H 'Content-Type: application/json' -d '{"Name":"Updated Service","Price":30,"Active":false}' "$base/api/services/$diservice" >/dev/null; service="$(request "$base/api/services")"; grepq '"Active":0' "$service"; [[ "$(status -X PUT -H 'Content-Type: application/json' -d '{"Name":"Nobody","Price":1}' "$base/api/services/99999")" == 404 ]]
[[ "$(status -H 'Content-Type: application/json' -d '{"Name":"","ConsultationFee":-1}' "$base/api/patients")" == 400 ]]
visit="$(request -H 'Content-Type: application/json' -d "{\"PatientId\":$pid,\"DoctorId\":$didoctor,\"VisitDate\":\"2025-01-01T10:00:00\",\"VisitType\":\"Follow-up\",\"Notes\":\"checkup\",\"ConsultationFee\":50}" "$base/api/visits")"; vid="$(sed -E 's/.*"id":([0-9]+).*/\1/' <<<"$visit")"
[[ "$(status -X DELETE "$base/api/doctors/$didoctor")" == 409 ]]
request -H 'Content-Type: application/json' -d '{"Description":"Lab test","Amount":25}' "$base/api/visits/$vid/services" >/dev/null
request -H 'Content-Type: application/json' -d '{"Amount":30,"Method":"Cash"}' "$base/api/visits/$vid/payments" >/dev/null
visits="$(request "$base/api/visits")"; grepq '"total":75' "$visits"; grepq '"balance":45' "$visits"; grepq '"visitType":"Follow-up"' "$visits"; request "$base/api/visits/$vid" >/dev/null
dashboard="$(request "$base/api/dashboard")"; grepq '"newPatientsThisMonth":' "$dashboard"; grepq '"recentVisits":' "$dashboard"; request "$base/api/reports/daily?date=2025-01-01" >/dev/null
request -H 'Content-Type: application/json' -d '{"ClinicName":"Integration Clinic","Currency":"INR"}' "$base/api/settings" >/dev/null
profile="$(request "$base/api/clinic-profile")"; grepq '"clinicName":"Integration Clinic"' "$profile"
base64 -d >"$DATA/logo.png" <<<"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
logo="$(request -F "file=@$DATA/logo.png" "$base/api/clinic-logo")"; grepq '"logoPath":"/api/clinic-logo/file"' "$logo"
request -H 'Content-Type: application/json' -d '{"ClinicName":"Integration Clinic","Currency":"INR","LogoPath":"/api/clinic-logo/file"}' "$base/api/settings" >/dev/null
request "$base/api/clinic-logo/file" >/dev/null
custom_backup="$DATA/custom-backups"
manual_backup="$DATA/manual-backups"
request -H 'Content-Type: application/json' -d "{\"ClinicName\":\"Integration Clinic\",\"Currency\":\"INR\",\"ManualBackupPath\":\"$manual_backup\",\"BackupPath\":\"$custom_backup\",\"BackupSchedule\":\"Weekly\",\"BackupTime\":\"23:59\",\"BackupDay\":\"Monday\"}" "$base/api/settings" >/dev/null
settings="$(request "$base/api/settings")"; grepq "\"manualBackupPath\":\"$manual_backup\"" "$settings"; grepq "\"backupPath\":\"$custom_backup\"" "$settings"; grepq '"backupSchedule":"Weekly"' "$settings"
request -H 'Content-Type: application/json' -d '{"Username":"testuser","Password":"Test@123","Role":"Receptionist"}' "$base/api/users" >/dev/null
testuser="$(request "$base/api/users")"; testuserid="$(sed -E 's/.*"Username":"testuser".*"Id":([0-9]+).*/\1/' <<<"$testuser")"
[[ "$testuserid" =~ ^[0-9]+$ ]] || testuserid="$(sqlite3 "$DATA/data/clinic.db" "SELECT Id FROM Users WHERE Username='testuser';")"
request -X PUT -H 'Content-Type: application/json' -d '{"Username":"updateduser","Role":"Admin","Active":true,"Password":"Updated@123"}' "$base/api/users/$testuserid" >/dev/null
updateduser="$(request "$base/api/users")"; grepq '"username":"updateduser"' "$updateduser"; grepq '"role":"Admin"' "$updateduser"
curl -fsS -c "$DATA/updated-cookie" -H 'Content-Type: application/json' -d '{"Username":"updateduser","Password":"Updated@123"}' "$base/api/login" >/dev/null
[[ "$(status -X PUT -H 'Content-Type: application/json' -d '{"Username":"admin","Role":"Receptionist","Active":true}' "$base/api/users/1")" == 400 ]]
printf 'document body' >"$DATA/doc.txt"; request -F "file=@$DATA/doc.txt" "$base/api/documents/$pid/$vid" >/dev/null; docs="$(request "$base/api/documents/$pid/$vid")"; did="$(sed -E 's/.*"id":([0-9]+).*/\1/' <<<"$docs")"; request "$base/api/documents/$did/download" >/dev/null
request -X POST "$base/api/backup" >/dev/null
backup="$(find "$manual_backup" -name '*.zip' -print -quit)"
mkdir -p "$DATA/extracted"; unzip -q "$backup" -d "$DATA/extracted"
[[ "$(sqlite3 "$DATA/extracted/clinic.db" "SELECT COUNT(*) FROM Patients WHERE Name='Updated Patient';")" == "1" ]]
[[ "$(sqlite3 "$DATA/extracted/clinic.db" "SELECT COUNT(*) FROM Visits WHERE PatientId=$pid;")" == "1" ]]
[[ "$(sqlite3 "$DATA/extracted/clinic.db" "SELECT COUNT(*) FROM Payments WHERE VisitId=$vid AND Amount=30;")" == "1" ]]
[[ "$(find "$DATA/extracted" -mindepth 1 -type f -printf '%P\n' | sort)" == "clinic.db" ]]
[[ "$(find "$custom_backup" -name '*.zip' -print -quit)" == "" ]]
second="$(request -H 'Content-Type: application/json' -d '{"Username":"testuser","Password":"Test@123","Role":"Receptionist"}' "$base/api/users" 2>/dev/null || true)"
[[ "$(status -H 'Content-Type: application/json' -d '{"Username":"testuser","Password":"Test@123","Role":"Receptionist"}' "$base/api/users")" == 409 ]]
printf 'other patient' >"$DATA/other.txt"
other="$(request -H 'Content-Type: application/json' -d '{"Name":"Other Patient"}' "$base/api/patients")"; otherid="$(sed -E 's/.*"id":([0-9]+).*/\1/' <<<"$other")"
[[ "$(curl -sS -o /dev/null -w '%{http_code}' -b "$COOKIE" -F "file=@$DATA/other.txt" "$base/api/documents/$otherid/$vid")" == 404 ]]
curl -fsS -c "$DATA/reception-cookie" -H 'Content-Type: application/json' -d '{"Username":"receptionist","Password":"Reception@123"}' "$base/api/login" >/dev/null
[[ "$(curl -sS -o /dev/null -w '%{http_code}' -b "$DATA/reception-cookie" "$base/api/patients")" == 200 ]]
[[ "$(curl -sS -o /dev/null -w '%{http_code}' -b "$DATA/reception-cookie" "$base/api/clinic-profile")" == 200 ]]
[[ "$(curl -sS -o /dev/null -w '%{http_code}' -b "$DATA/reception-cookie" "$base/api/clinic-logo/file")" == 200 ]]
[[ "$(curl -sS -o /dev/null -w '%{http_code}' -b "$DATA/reception-cookie" "$base/api/settings")" == 403 ]]
[[ "$(curl -sS -o /dev/null -w '%{http_code}' -X POST -b "$DATA/reception-cookie" "$base/api/browse-folder")" == 403 ]]
[[ "$(curl -sS -o /dev/null -w '%{http_code}' -X PUT -H 'Content-Type: application/json' -b "$DATA/reception-cookie" -d '{"Name":"Nope","Price":1}' "$base/api/services/$diservice")" == 403 ]]
[[ "$(curl -sS -o /dev/null -w '%{http_code}' -X PUT -H 'Content-Type: application/json' -b "$DATA/reception-cookie" -d '{"Name":"Nope","ConsultationFee":1}' "$base/api/doctors/$didoctor")" == 403 ]]
[[ "$(curl -sS -o /dev/null -w '%{http_code}' -X DELETE -b "$DATA/reception-cookie" "$base/api/doctors/$didoctor")" == 403 ]]
curl -sS -o /dev/null -w '%{http_code}' -H 'Cookie: clinic_session=forged; clinic_role=Admin' "$base/api/settings" | grep -q 403
curl -fsS -c "$COOKIE" -X POST "$base/api/logout" >/dev/null
curl -s -o /dev/null -w '%{http_code}' -b "$COOKIE" "$base/api/patients" | grep -q 401
[[ "$(curl -sS -o /dev/null -w '%{http_code}' -X POST -b "$COOKIE" "$base/api/browse-folder")" == 403 ]]
echo "integration tests passed"