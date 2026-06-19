$dllPath = "d:\Projects\Pump_Automation\PyroSync_Max\PyroSync_Max\src\FuelPro.UI\bin\Debug\net8.0-windows\Microsoft.Data.Sqlite.dll"
$dbPath = "$env:LOCALAPPDATA\FuelPro\fuelPro.db"

# Load the assembly
[System.Reflection.Assembly]::LoadFrom($dllPath) | Out-Null

$connectionString = "Data Source=$dbPath"
$connection = New-Object Microsoft.Data.Sqlite.SqliteConnection($connectionString)

try {
    $connection.Open()
    Write-Host "Connected to SQLite database at: $dbPath" -ForegroundColor Green
    
    $keys = @{
        "Sync.SupabaseUrl" = "https://rvcibryprvjbzrtwqktk.supabase.co"
        "Sync.SupabaseApiKey" = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ2Y2licnlwcnZqYnpydHdxa3RrIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODEzMTIxMTcsImV4cCI6MjA5Njg4ODExN30.vMTA97993upfnOCs5ja-kxIhDSHbcx1gEQ6itNm5BBk"
        "Sync.IsEnabled" = "true"
    }

    foreach ($entry in $keys.GetEnumerator()) {
        $key = $entry.Key
        $value = $entry.Value
        
        # Check if key exists
        $cmd = $connection.CreateCommand()
        $cmd.CommandText = "SELECT COUNT(*) FROM AppMeta WHERE [Key] = @key"
        $cmd.Parameters.AddWithValue("@key", $key) | Out-Null
        $count = $cmd.ExecuteScalar()
        $cmd.Dispose()

        $cmdUpdate = $connection.CreateCommand()
        if ($count -eq 0) {
            $cmdUpdate.CommandText = "INSERT INTO AppMeta ([Key], [Value]) VALUES (@key, @value)"
            Write-Host "  Inserting AppMeta key: '$key'"
        } else {
            $cmdUpdate.CommandText = "UPDATE AppMeta SET [Value] = @value WHERE [Key] = @key"
            Write-Host "  Updating AppMeta key: '$key'"
        }
        $cmdUpdate.Parameters.AddWithValue("@key", $key) | Out-Null
        $cmdUpdate.Parameters.AddWithValue("@value", $value) | Out-Null
        $cmdUpdate.ExecuteNonQuery() | Out-Null
        $cmdUpdate.Dispose()
    }
    
    Write-Host "Local SQLite sync configuration updated successfully!" -ForegroundColor Green
} catch {
    Write-Error "Database operation failed: $_"
} finally {
    $connection.Close()
}
