# Starts the Smart-X API in the background, waits until it answers, then starts the WPF client.
# Usage (PowerShell, from the solution folder):   .\Start-SmartX.ps1
$ErrorActionPreference = "Stop"

Write-Host "Starting SmartX.Api on http://localhost:5177 ..."
$api = Start-Process -FilePath "dotnet" -ArgumentList "run --project SmartX.Api" -PassThru -WindowStyle Minimized

for ($i = 0; $i -lt 40; $i++) {
    try { Invoke-RestMethod -Uri "http://localhost:5177/" -TimeoutSec 2 | Out-Null; break }
    catch { Start-Sleep -Milliseconds 750 }
}

Write-Host "Starting SmartX.Client ..."
try {
    dotnet run --project SmartX.Client
}
finally {
    Write-Host "Client closed - stopping the API."
    if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force }
}
