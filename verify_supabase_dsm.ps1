$projectRef = "rvcibryprvjbzrtwqktk"
$apiUrl = "https://$projectRef.supabase.co/rest/v1"
$anonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ2Y2licnlwcnZqYnpydHdxa3RrIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODEzMTIxMTcsImV4cCI6MjA5Njg4ODExN30.vMTA97993upfnOCs5ja-kxIhDSHbcx1gEQ6itNm5BBk"

$tables = @(
    "DsmUsers", "DsmPumpAssignments", "DsmDevices", "DsmSubmissions",
    "DsmSubmissionReadings", "DsmSubmissionCollections", "DsmApprovalAudits",
    "DsmNotifications", "DsmAttendance"
)

Write-Host "Verifying Supabase Phase 1 Tables deployment using curl..." -ForegroundColor Cyan

$successCount = 0
$failCount = 0

foreach ($table in $tables) {
    $url = $apiUrl + "/" + $table + "?select=*&limit=1"
    
    $statusCodeStr = & curl.exe -s -o NUL -w "%{http_code}" -H "apikey: $anonKey" $url
    $statusCodeStr = $statusCodeStr.Trim()
    
    if ($statusCodeStr -eq "200" -or $statusCodeStr -eq "406" -or $statusCodeStr -eq "401" -or $statusCodeStr -eq "204") {
        Write-Host "  [+] Table '$table' exists and is accessible (Status: $statusCodeStr)." -ForegroundColor Green
        $successCount++
    } elseif ($statusCodeStr -eq "404") {
        Write-Host "  [-] Table '$table' does not exist." -ForegroundColor Red
        $failCount++
    } else {
        Write-Host "  [?] Table '$table' status check: $statusCodeStr" -ForegroundColor Yellow
        $successCount++
    }
}

Write-Host "`nVerification Summary:" -ForegroundColor Cyan
Write-Host "  Total Synced Tables Checked: $($tables.Count)"
Write-Host "  Active/Created: $successCount" -ForegroundColor Green
Write-Host "  Not Found: $failCount" -ForegroundColor Red

# Check DsmSubmissionReadings.FuelType column (required for DSM PWA submit)
Write-Host "`nChecking DsmSubmissionReadings.FuelType column..." -ForegroundColor Cyan
$fuelTypeUrl = $apiUrl + "/DsmSubmissionReadings?select=FuelType&limit=0"
$fuelTypeStatus = (& curl.exe -s -o NUL -w "%{http_code}" -H "apikey: $anonKey" $fuelTypeUrl).Trim()
if ($fuelTypeStatus -eq "200") {
    Write-Host "  [+] FuelType column exists." -ForegroundColor Green
} else {
    Write-Host "  [-] FuelType column missing (HTTP $fuelTypeStatus). Run supabase_dsm_add_fueltype.sql in Supabase SQL Editor." -ForegroundColor Red
}
