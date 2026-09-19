param(
    [switch]$BuildInstaller,
    [switch]$Sign,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

if ($env:OS -ne "Windows_NT") {
    throw "Run this script on Windows. It produces the self-contained win-x64 desktop payload."
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "ClinicManagement.csproj"
$payload = Join-Path $PSScriptRoot "Payload"
$setup = Join-Path $PSScriptRoot "Setup.iss"
$imageMagickVersion = "7.1.2-31"
$imageMagickUrl = "https://github.com/ImageMagick/ImageMagick/releases/download/$imageMagickVersion/ImageMagick-$imageMagickVersion-Q16-x64-static.exe"
$imageMagickSha256 = "765eeb01e4def9ab7dfb8e3989141a6a3fae578b34309cc72969768cb0d2e917"

function Find-SignTool {
    $candidates = @()
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        $candidates += $command.Source
    }

    $windowsKitRoots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "${env:ProgramFiles}\Windows Kits\10\bin"
    ) | Where-Object { $_ -and (Test-Path $_) }
    foreach ($rootPath in $windowsKitRoots) {
        $candidates += Get-ChildItem $rootPath -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -ExpandProperty FullName
    }

    $signTool = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $signTool) {
        throw "signtool.exe was not found. Install the Windows SDK or run without -Sign."
    }
    return $signTool
}

function Get-SigningCertificate([string]$thumbprint) {
    $normalizedThumbprint = ($thumbprint -replace '\s', '').ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($normalizedThumbprint)) {
        throw "A certificate thumbprint is required when -Sign is used."
    }

    $certificate = Get-ChildItem -Path @("Cert:\CurrentUser\My", "Cert:\LocalMachine\My") -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $normalizedThumbprint } |
        Select-Object -First 1
    if (-not $certificate) {
        throw "The signing certificate $normalizedThumbprint was not found in the CurrentUser or LocalMachine personal certificate store."
    }
    if (-not $certificate.HasPrivateKey) {
        throw "The signing certificate $normalizedThumbprint does not have an accessible private key."
    }
    return [pscustomobject]@{
        Thumbprint = $normalizedThumbprint
        MachineStore = $certificate.PSParentPath -match 'LocalMachine\\My'
    }
}

function Sign-AndVerify(
    [string]$artifact,
    [string]$signTool,
    [string]$thumbprint,
    [string]$timestampUrl,
    [bool]$machineStore
) {
    Write-Host "Signing $(Split-Path $artifact -Leaf)..."
    $signArguments = @("sign")
    if ($machineStore) {
        $signArguments += "/sm"
    }
    $signArguments += @("/sha1", $thumbprint, "/fd", "SHA256", "/tr", $timestampUrl, "/td", "SHA256", "/d", "Clinic Suite", $artifact)
    & $signTool @signArguments
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed to sign $artifact."
    }

    & $signTool verify /pa /all $artifact
    if ($LASTEXITCODE -ne 0) {
        throw "signtool could not verify the signature on $artifact."
    }
    Write-Host "Verified signature and timestamp on $(Split-Path $artifact -Leaf)."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK is required. Install .NET 8 SDK or newer and run this script again."
}

$sdkVersion = (& dotnet --version).Trim()
$sdkMajor = [int]($sdkVersion.Split('.')[0])
if ($sdkMajor -lt 8) {
    throw "Clinic Suite Windows packaging requires the .NET 8 SDK or newer. Found $sdkVersion."
}

if (Test-Path $payload) {
    Remove-Item $payload -Recurse -Force
}
New-Item $payload -ItemType Directory -Force | Out-Null

Push-Location $root
try {
    & dotnet restore $project --runtime win-x64
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

$signTool = $null
$signingThumbprint = $null
$signingMachineStore = $false
if ($Sign) {
    $signTool = Find-SignTool
    $signingCertificate = Get-SigningCertificate $CertificateThumbprint
    $signingThumbprint = $signingCertificate.Thumbprint
    $signingMachineStore = $signingCertificate.MachineStore
    Sign-AndVerify $exe $signTool $signingThumbprint $TimestampUrl $signingMachineStore
}

$imageMagickPath = Join-Path $payload "ImageMagickSetup.exe"
Write-Host "Downloading pinned ImageMagick $imageMagickVersion..."
Invoke-WebRequest `
    -Uri $imageMagickUrl `
    -OutFile $imageMagickPath `
    -Headers @{ "User-Agent" = "ClinicSuite-Windows-Packaging" }

$actualImageMagickSha256 = (Get-FileHash $imageMagickPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualImageMagickSha256 -ne $imageMagickSha256) {
    Remove-Item $imageMagickPath -Force
    throw "ImageMagick checksum verification failed. Expected $imageMagickSha256 but found $actualImageMagickSha256."
}

Write-Host "Verified ImageMagick installer checksum: $actualImageMagickSha256"
Write-Host "Windows payload created at $payload"

if ($BuildInstaller) {
    if (-not $Sign) {
        Write-Warning "The generated installer will be unsigned. Use -Sign -CertificateThumbprint <thumbprint> for public distribution."
    }

    $isccCandidate = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    if (-not $isccCandidate) {
        throw "Inno Setup 6 was not found. Install it or run without -BuildInstaller."
    }

    & $isccCandidate $setup
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed."
    }

    $installer = Join-Path $PSScriptRoot "Output\ClinicSuiteSetup.exe"
    if (-not (Test-Path $installer)) {
        throw "Inno Setup completed but $installer was not created."
    }
    if ($Sign) {
        Sign-AndVerify $installer $signTool $signingThumbprint $TimestampUrl $signingMachineStore
    }

    Write-Host "Installer created in $installer"
}