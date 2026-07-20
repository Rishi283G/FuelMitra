@echo off
echo Building the application...
dotnet publish src\FuelPro.UI\FuelPro.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -o publish_output -m:1
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b %ERRORLEVEL%
)
echo Build succeeded!
if exist Assets (
    xcopy /E /I /Y Assets publish_output\Assets
)
echo Compiling the installer...
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
if %ERRORLEVEL% NEQ 0 (
    echo Installer compilation failed!
    exit /b %ERRORLEVEL%
)
echo Installer created successfully in installer_output folder!
