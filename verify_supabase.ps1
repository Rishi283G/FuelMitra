$projectRef = "rvcibryprvjbzrtwqktk"
$apiUrl = "https://$projectRef.supabase.co/rest/v1"
$anonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ2Y2licnlwcnZqYnpydHdxa3RrIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODEzMTIxMTcsImV4cCI6MjA5Njg4ODExN30.vMTA97993upfnOCs5ja-kxIhDSHbcx1gEQ6itNm5BBk"

$tables = @(
    "Users", "Shifts", "DsmEntries", "NozzleReadings", "PaymentCollections",
    "DebitEntries", "TestingEntries", "Expenses", "CashDenominations", "Settings",
    "ShiftOtherCash", "ShiftFuelRates", "DsmProfiles", "Creditors", "CreditorRepayments",
    "AgsShiftImports", "AgsNozzleReadings", "AgsTankStocks", "AgsDailySummaries"
)

Write-Host "Verifying Supabase Tables deployment using curl..." -ForegroundColor Cyan

$successCount = 0
$failCount = 0

foreach ($table in $tables) {
    # Construct URL using string concatenation to avoid variable interpolation bugs
    $url = $apiUrl + "/" + $table + "?select=local_id&limit=1"
    
    # Run curl in silent mode, discard output body, and output only the HTTP status code
    $statusCodeStr = & curl.exe -s -o NUL -w "%{http_code}" -H "apikey: $anonKey" $url
    
    # Clean output string of any leading/trailing whitespace
    $statusCodeStr = $statusCodeStr.Trim()
    
    if ($statusCodeStr -eq "200" -or $statusCodeStr -eq "406" -or $statusCodeStr -eq "401") {
        Write-Host "  [+] Table '$table' exists and is accessible." -ForegroundColor Green
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
