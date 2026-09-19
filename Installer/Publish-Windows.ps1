param(
    [switch]$BuildInstaller
)

$ErrorActionPreference = "Stop"

if ($env:OS -ne "Windows_NT") {
    throw "Run this script on Windows. It produces the self-contained win-x64 desktop payload."
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "ClinicManagement.csproj"
$payload = Join-Path $PSScriptRoot "Payload"
$setup = Join-Path $PSScriptRoot "Setup.iss"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK is required. Install the .NET 8 SDK and run this script again."
}

$sdkVersion = (& dotnet --version).Trim()
if (-not $sdkVersion.StartsWith("8.")) {
    throw "Clinic Suite Windows packaging requires the .NET 8 SDK. Found $sdkVersion."
}

if (Test-Path $payload) {
    Remove-Item $payload -Recurse -Force
}
New-Item $payload -ItemType Directory -Force | Out-Null

Push-Location $root
try {
    & dotnet restore $project
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed."
    }

    & dotnet publish $project `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $payload `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed."
    }
}
finally {
    Pop-Location
}

$exe = Join-Path $payload "ClinicManagement.exe"
if (-not (Test-Path $exe)) {
    throw "Publish completed but $exe was not created."
}

Write-Host "Windows payload created at $payload"

if ($BuildInstaller) {
    $isccCandidates = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }

    if (-not $isccCandidates) {
        throw "Inno Setup 6 was not found. Install it or run without -BuildInstaller."
    }

    & $isccCandidates[0] $setup
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed."
    }

    Write-Host "Installer created in $PSScriptRoot\Output"
}