Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  FuelPro - Installer Build Script" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Publish the application as a standalone Windows executable
Write-Host "`n[1/3] Publishing .NET 8 Application..." -ForegroundColor Yellow
$publishDir = ".\publish_output"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

dotnet publish src/FuelPro.UI/FuelPro.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -o $publishDir -m:1

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[ERROR] dotnet publish failed!" -ForegroundColor Red
    exit 1
}
Write-Host "[SUCCESS] Application published to $publishDir" -ForegroundColor Green

# 2. Check for Inno Setup, download and install if missing
Write-Host "`n[2/3] Checking Inno Setup Compiler..." -ForegroundColor Yellow
$isccPathAdmin = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$isccPathUser = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
$isccPath = ""

if (Test-Path $isccPathAdmin) { $isccPath = $isccPathAdmin }
elseif (Test-Path $isccPathUser) { $isccPath = $isccPathUser }

if ($isccPath -eq "") {
    Write-Host "Inno Setup is not installed. Downloading installer..." -ForegroundColor Yellow
    $installerPath = "$env:TEMP\innosetup_installer.exe"
    Invoke-WebRequest -Uri "https://jrsoftware.org/download.php/is.exe" -OutFile $installerPath
    
    Write-Host "Please click 'Yes' on the UAC prompt to install Inno Setup..." -ForegroundColor Cyan
    Start-Process -FilePath $installerPath -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-" -Wait -Verb RunAs
    
    if (Test-Path $isccPathAdmin) { $isccPath = $isccPathAdmin }
    elseif (Test-Path $isccPathUser) { $isccPath = $isccPathUser }

    if ($isccPath -eq "") {
        Write-Host "`n[ERROR] Inno Setup installation failed or was cancelled." -ForegroundColor Red
        exit 1
    }
}
Write-Host "[SUCCESS] Inno Setup compiler found." -ForegroundColor Green

# 3. Compile the Installer
Write-Host "`n[3/3] Compiling Inno Setup Script..." -ForegroundColor Yellow

& $isccPath ".\installer.iss"

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[ERROR] Inno Setup compilation failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n==========================================" -ForegroundColor Cyan
Write-Host "[SUCCESS] Installer generated in .\installer_output\" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Cyan
