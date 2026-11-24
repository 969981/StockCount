#requires -Version 7.0
param([string]$ApiExe = 'publish/avalonia-desktop/StockCount.Api.exe')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This smoke test requires Windows.' }
$exe = (Resolve-Path -LiteralPath $ApiExe).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ('StockCount-lifetime-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$owner = $null
$api = $null

try {
    # Use an isolated, currently unused pair; never touch existing services.
    $listening = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty LocalPort)
    $port = 25080..25280 | Where-Object { $_ -notin $listening -and ($_ + 1) -notin $listening } | Select-Object -First 1
    if (-not $port) { throw 'No free adjacent test ports.' }
    $ownerInfo = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $ownerInfo.UseShellExecute = $false
    $ownerInfo.CreateNoWindow = $true
    foreach ($arg in @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 180')) {
        $ownerInfo.ArgumentList.Add($arg)
    }
    $owner = [Diagnostics.Process]::Start($ownerInfo)

    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    $info.WorkingDirectory = $root
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    # Do not inherit deployment settings or secrets from the runner.
    foreach ($key in @($info.Environment.Keys)) {
        if ($key.StartsWith('STOCKCOUNT_', [StringComparison]::OrdinalIgnoreCase)) {
            $null = $info.Environment.Remove($key)
        }
    }
    $info.Environment['STOCKCOUNT_API_PORT'] = [string]$port
    $info.Environment['STOCKCOUNT_ALLOW_LAN'] = 'false'
    $info.Environment['STOCKCOUNT_ENABLE_TEST_WRITES'] = 'false'
    $info.Environment['STOCKCOUNT_ENABLE_PRODUCTION_WRITES'] = 'false'
    $info.Environment['STOCKCOUNT_LOG_DB_PATH'] = Join-Path $root 'server-logs.db'
    $info.Environment['STOCKCOUNT_MANAGER_PID'] = [string]$owner.Id
    $info.Environment['STOCKCOUNT_MANAGER_STARTED_UTC_TICKS'] = [string]$owner.StartTime.ToUniversalTime().Ticks
    $api = [Diagnostics.Process]::Start($info)

    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $healthy = $false
    do {
        if ($api.HasExited) { throw "API exited before health check: $($api.ExitCode)" }
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$($port + 1)/health" -TimeoutSec 2 -NoProxy
            $healthy = $health.service -eq 'StockCount.Api'
        } catch { $healthy = $false }
        if (-not $healthy) { Start-Sleep -Milliseconds 250 }
    } until ($healthy -or [DateTime]::UtcNow -ge $deadline)
    if (-not $healthy) { throw 'Published API did not become healthy.' }

    $rows = @(Get-NetTCPConnection -LocalPort $port,($port + 1) -State Listen)
    foreach ($p in @($port, ($port + 1))) {
        if (-not @($rows | Where-Object { $_.LocalPort -eq $p -and $_.OwningProcess -eq $api.Id }).Count) {
            throw "Published API PID $($api.Id) did not own port $p."
        }
    }
    if (@($rows | Where-Object { $_.OwningProcess -ne $api.Id }).Count) {
        throw 'A different process owns one of the fixture ports.'
    }
    Write-Host "Published API PID $($api.Id) healthy on $port / $($port + 1); manager fixture PID $($owner.Id)."

    # Terminate only the simulated manager, not the process tree or API itself.
    $owner.Kill()
    if (-not $owner.WaitForExit(5000)) { throw 'Manager fixture did not exit.' }
    if (-not $api.WaitForExit(45000)) { throw 'API remained after manager exit.' }
    if ($api.ExitCode -ne 0) { throw "API did not shut down normally: exit code $($api.ExitCode)." }
    $remaining = @(Get-NetTCPConnection -LocalPort $port,($port + 1) -State Listen -ErrorAction SilentlyContinue)
    if ($remaining.Count -ne 0) { throw 'Listener remained after API shutdown.' }
    if (-not (Test-Path -LiteralPath (Join-Path $root 'server-logs.db'))) { throw 'SQLite log database was not created.' }
    Write-Host 'PASS: published API exited normally after manager termination and released both listeners.'
}
finally {
    foreach ($process in @($api, $owner)) {
        if ($null -ne $process) {
            try {
                if (-not $process.HasExited) {
                    $process.Kill()
                    $null = $process.WaitForExit(5000)
                }
            } finally { $process.Dispose() }
        }
    }
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
