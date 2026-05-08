@echo off
echo Building the application...
dotnet publish src\FuelPro.UI\FuelPro.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish_output
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b %ERRORLEVEL%
)
echo Build succeeded!
echo Compiling the installer...
iscc installer.iss
if %ERRORLEVEL% NEQ 0 (
    echo Installer compilation failed!
    exit /b %ERRORLEVEL%
)
echo Installer created successfully in installer_output folder!
