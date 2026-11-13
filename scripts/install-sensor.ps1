<#
.SYNOPSIS
    Installs and registers the Sentravane Sensor as a Windows Background Service.
.DESCRIPTION
    Builds or packages the Sentravane.Sensor.Service executable, registers it under the
    Windows Service Control Manager (SCM) running as NT AUTHORITY\SYSTEM, configures
    automatic crash recovery, and configures event logging.
.PARAMETER InstallDir
    Target directory where binaries will be deployed (default: $env:ProgramFiles\Sentravane).
.PARAMETER ServerUrl
    Sentravane Fleet Server URL (default: http://localhost:5000).
.PARAMETER EnrollmentToken
    Fleet tenant enrollment key.
#>

[CmdletBinding()]
param (
    [string]$InstallDir = "$env:ProgramFiles\Sentravane",
    [string]$ServerUrl = "http://localhost:5000",
    [string]$EnrollmentToken = "DEFAULT_TOKEN"
)

# 1. Require Administrator Privileges
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "[-] This script requires Administrator privileges. Please re-run PowerShell as Administrator."
    exit 1
}

$ServiceName = "SentravaneSensor"
$ServiceDisplayName = "Sentravane Endpoint Security Sensor"
$ServiceDescription = "Driverless Windows ransomware containment and fleet security sensor."

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       Sentravane Windows Sensor Enterprise Installer     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 2. Stop existing service if running
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "[*] Stopping existing $ServiceName service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# 3. Create Target Installation Directory
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Write-Host "[+] Created install directory: $InstallDir" -ForegroundColor Green
}

# 4. Publish Sensor Binary
$SensorProjectPath = Join-Path $PSScriptRoot "src\Sentravane.Sensor.Service\Sentravane.Sensor.Service.csproj"
Write-Host "[*] Publishing Sentravane Sensor binary to $InstallDir..." -ForegroundColor Cyan

dotnet publish $SensorProjectPath -c Release -r win-x64 --self-contained false -o $InstallDir --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "[-] dotnet publish failed. Check build logs."
    exit $LASTEXITCODE
}

# 5. Publish CLI utility to same directory
$CliProjectPath = Join-Path $PSScriptRoot "src\Sentravane.Cli\Sentravane.Cli.csproj"
Write-Host "[*] Publishing Sentravane CLI utility to $InstallDir..." -ForegroundColor Cyan
dotnet publish $CliProjectPath -c Release -r win-x64 --self-contained false -o $InstallDir --nologo

# 6. Write Configuration Settings
$configPath = Join-Path $InstallDir "appsettings.json"
$configJson = @"
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Sentravane": {
    "ServerUrl": "$ServerUrl",
    "EnrollmentToken": "$EnrollmentToken"
  }
}
"@
Set-Content -Path $configPath -Value $configJson -Encoding UTF8
Write-Host "[+] Wrote sensor configuration to $configPath" -ForegroundColor Green

# 7. Register or Update Windows Service
$binaryExe = Join-Path $InstallDir "Sentravane.Sensor.Service.exe"

if (-not $existingService) {
    Write-Host "[*] Registering Windows Service ($ServiceName)..." -ForegroundColor Cyan
    sc.exe create $ServiceName binPath= "`"$binaryExe`"" start= auto DisplayName= "`"$ServiceDisplayName`""
    sc.exe description $ServiceName "$ServiceDescription"
} else {
    Write-Host "[*] Updating binary path for existing service ($ServiceName)..." -ForegroundColor Cyan
    sc.exe config $ServiceName binPath= "`"$binaryExe`"" start= auto
}

# 8. Configure Auto-Restart on Crash (Recovery Policy)
# First failure: restart after 5s; Second failure: restart after 10s; Subsequent: restart after 30s
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000

# 9. Add InstallDir to System PATH so 'sentravane' command is globally available
$machinePath = [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::Machine)
if ($machinePath -notlike "*$InstallDir*") {
    [Environment]::SetEnvironmentVariable("Path", "$machinePath;$InstallDir", [EnvironmentVariableTarget]::Machine)
    Write-Host "[+] Added $InstallDir to system PATH" -ForegroundColor Green
}

# 10. Start the Service
Write-Host "[*] Starting $ServiceName service..." -ForegroundColor Cyan
Start-Service -Name $ServiceName

$status = (Get-Service -Name $ServiceName).Status
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "[+] Sentravane Sensor successfully installed and running!" -ForegroundColor Green
Write-Host "    Service Status: $status" -ForegroundColor Green
Write-Host "    Install Path  : $InstallDir" -ForegroundColor Green
Write-Host "    CLI Tool      : sentravane (Run 'sentravane status' to verify)" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
