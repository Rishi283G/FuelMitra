Remove-Item -Recurse -Force C:\publish_temp -ErrorAction Ignore
$process = Start-Process dotnet -ArgumentList "publish src/FuelPro.UI/FuelPro.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -o C:\publish_temp --no-restore -m:1" -Wait -NoNewWindow -PassThru
if ($process.ExitCode -eq 0) {
    Remove-Item -Recurse -Force publish_output -ErrorAction Ignore
    New-Item -ItemType Directory -Path publish_output
    Copy-Item -Path C:\publish_temp\* -Destination publish_output -Recurse
    C:\Users\jadha\AppData\Local\Programs\"Inno Setup 6"\ISCC.exe .\installer.iss
}
exit $process.ExitCode
