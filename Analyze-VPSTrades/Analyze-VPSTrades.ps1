<#
.SYNOPSIS
    Analyze-VPSTrades.ps1
    Analyzes NinjaTrader 8 trading logs directly on the VPS.

.DESCRIPTION
    - Scans ALL log files in the NT8 log directory (any date in filename)
    - Auto-detects trading date from log timestamps (supports replay sessions)
    - Creates dated analysis folder under ActiveNikiAnalysis\YYYY-MM-DD\
    - Generates trades_final.txt, signals.txt
    - Copies ActiveNikiMonitor, ActiveNikiTrader, and IndicatorValues files
    - Runs Python analysis to produce Trading_Analysis-VPS[1/2/3].txt
    - Report Time column shows full yyyy-MM-dd HH:mm:ss (from log timestamps, not system clock)
    - Designed to run unattended via scheduled task (SYSTEM account)

    Processing order (smallest files first for performance):
      1. log.*.txt           â†’ extract today's filled trades
      2. ActiveNikiMonitor_* â†’ copy for signal parsing
      3. ActiveNikiTrader_*  â†’ copy for order/close parsing
      4. IndicatorValues_*   â†’ copy for BAR analysis (largest)

.PARAMETER Date
    The trading date to analyze. If omitted, auto-detected from log file timestamps.
    Format: yyyy-MM-dd. Useful for replays or specific historical dates.

.PARAMETER NT8LogPath
    Path to the NinjaTrader 8 log directory.
    Defaults to the standard NT8 path on VPS (Administrator account) if omitted.

.PARAMETER RepoRoot
    Path to the local git repo root (must contain a reports\ subfolder).
    Defaults to the VPS repo path. Override for local testing.


.EXAMPLE
    .\Analyze-VPSTrades.ps1
    # Analyzes logs — date auto-detected, uses default VPS log path

.EXAMPLE
    .\Analyze-VPSTrades.ps1 -Date "2026-02-07"
    # Analyzes logs for a specific date

.EXAMPLE
    .Analyze-VPSTrades.ps1 -Date "2026-02-02" -NT8LogPath "C:\Users\alexb\OneDrive\Documents\NinjaTrader 8\log" -RepoRoot "C:\Users\alexb\Documents\TradingRepo\NinjaTrader4Niki"
    # Local test — Steps 7 and 9 skipped automatically (IP not a known VPS)
#>

param(
    [string]$Date       = "",
    [string]$NT8LogPath = "C:\Users\Administrator\Documents\NinjaTrader 8\log",
    [string]$RepoRoot   = "C:\Users\Administrator\Documents\TradingRepo\NinjaTrader4Niki"
)

# === CONFIGURATION ===
$AnalysisBasePath = Join-Path $NT8LogPath "ActiveNikiAnalysis"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PythonScript = Join-Path $ScriptDir "main.py"
$PythonExe = "C:\Program Files\Python313\python.exe"
if (!(Test-Path $PythonExe)) {
    $pyCmd = Get-Command python -ErrorAction SilentlyContinue
    if ($pyCmd) { $PythonExe = $pyCmd.Source }
}
$RunLog = Join-Path $AnalysisBasePath "run.log"

# === FUNCTIONS ===

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$timestamp] [$Level] $Message"
    # Write to both console (for manual runs) and log file
    switch ($Level) {
        "ERROR" { Write-Host $entry -ForegroundColor Red }
        "WARN"  { Write-Host $entry -ForegroundColor Yellow }
        "OK"    { Write-Host $entry -ForegroundColor Green }
        default { Write-Host $entry }
    }
    Add-Content -Path $RunLog -Value $entry -ErrorAction SilentlyContinue
}

# === MAIN SCRIPT ===

# === AUTO-DETECT DATE FROM LOG FILES (if not provided) ===
if ([string]::IsNullOrEmpty($Date)) {
    Write-Host "[INFO] No date specified — detecting from log file timestamps..."

    # Strategy 1: Read most recent ActiveNikiTrader_*.txt and grab first timestamp line
    $traderDetect = Get-ChildItem -Path $NT8LogPath -Filter "ActiveNikiTrader_*.txt" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if ($traderDetect) {
        $firstLine = Get-Content $traderDetect.FullName -TotalCount 60 -ErrorAction SilentlyContinue |
            Where-Object { $_ -match "^\d{4}-\d{2}-\d{2} " } | Select-Object -First 1
        if ($firstLine -match "^(\d{4}-\d{2}-\d{2})") {
            $Date = $Matches[1]
            Write-Host "[INFO] Date detected from ActiveNikiTrader log: $Date"
        }
    }

    # Strategy 2: Fall back to most recent IndicatorValues_*.csv
    if ([string]::IsNullOrEmpty($Date)) {
        $csvDetect = Get-ChildItem -Path $NT8LogPath -Filter "IndicatorValues_*.csv" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($csvDetect) {
            $firstLine = Get-Content $csvDetect.FullName -TotalCount 5 -ErrorAction SilentlyContinue |
                Where-Object { $_ -match "^\d{4}-\d{2}-\d{2}" } | Select-Object -First 1
            if ($firstLine -match "^(\d{4}-\d{2}-\d{2})") {
                $Date = $Matches[1]
                Write-Host "[INFO] Date detected from IndicatorValues CSV: $Date"
            }
        }
    }

    # Strategy 3: Fall back to today (last resort)
    if ([string]::IsNullOrEmpty($Date)) {
        $Date = Get-Date -Format "yyyy-MM-dd"
        Write-Host "[WARN] Could not detect date from log files — falling back to today: $Date"
    }
}

# Ensure base analysis directory exists
if (!(Test-Path $AnalysisBasePath)) {
    New-Item -ItemType Directory -Path $AnalysisBasePath -Force | Out-Null
}

Write-Log "========================================"
Write-Log "  VPS Trading Log Analysis"
Write-Log "  Date: $Date"
Write-Log "========================================"

# Create dated analysis folder
$analysisFolder = Join-Path $AnalysisBasePath $Date
if (!(Test-Path $analysisFolder)) {
    New-Item -ItemType Directory -Path $analysisFolder -Force | Out-Null
    Write-Log "Created folder: $analysisFolder" "OK"
} else {
    Write-Log "Folder exists: $analysisFolder (will overwrite)"
}

# Convert date for NT8 log filename matching (yyyy-MM-dd -> yyyyMMdd)
$nt8DateFormat = $Date -replace "-", ""

try {
    # ==================== STEP 1: EXTRACT TRADES FROM log.*.txt ====================
    Write-Log "Step 1: Scanning log.*.txt files for today's filled trades..."

    $logFiles = Get-ChildItem -Path $NT8LogPath -Filter "log.*.txt" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike "*.en.txt" }

    Write-Log "  Found $($logFiles.Count) NT8 log file(s) to scan"

    $allTrades = @()
    foreach ($logFile in $logFiles) {
        $lines = Get-Content $logFile.FullName -ErrorAction SilentlyContinue
        $todayTrades = $lines | Where-Object {
            $_ -match "New state='Filled'" -and $_ -match "^$Date"
        }
        if ($todayTrades) {
            $allTrades += $todayTrades
            Write-Log "  $($logFile.Name): $($todayTrades.Count) filled trade(s) for $Date"
        }
    }

    $tradesFile = Join-Path $analysisFolder "trades_final.txt"
    if ($allTrades.Count -gt 0) {
        $allTrades | Out-File -FilePath $tradesFile -Encoding UTF8
        Write-Log "  trades_final.txt: $($allTrades.Count) trades" "OK"
    } else {
        Write-Log "  No filled trades found for $Date" "WARN"
        "" | Out-File -FilePath $tradesFile -Encoding UTF8
    }

    # ==================== STEP 2: EXTRACT SIGNALS FROM log.*.txt ====================
    Write-Log "Step 2: Extracting signals from log.*.txt for today..."

    # Also build signals.txt from NT8 logs (grep SIGNAL|flip lines with today's date)
    $allSignalLines = @()
    foreach ($logFile in $logFiles) {
        $lines = Get-Content $logFile.FullName -ErrorAction SilentlyContinue
        $todaySignals = $lines | Where-Object {
            ($_ -match "SIGNAL|flip") -and $_ -match "^$Date"
        }
        if ($todaySignals) {
            $allSignalLines += $todaySignals
        }
    }

    $signalsFile = Join-Path $analysisFolder "signals.txt"
    if ($allSignalLines.Count -gt 0) {
        $allSignalLines | Out-File -FilePath $signalsFile -Encoding UTF8
        Write-Log "  signals.txt: $($allSignalLines.Count) lines" "OK"
    } else {
        Write-Log "  No signal lines found in NT8 logs for $Date" "WARN"
        "" | Out-File -FilePath $signalsFile -Encoding UTF8
    }

    # ==================== STEP 3: COPY ActiveNikiMonitor FILES ====================
    Write-Log "Step 3: Copying ActiveNikiMonitor log files..."

    $monitorFiles = Get-ChildItem -Path $NT8LogPath -Filter "ActiveNikiMonitor_*.txt" -ErrorAction SilentlyContinue
    $monitorCopied = 0

    foreach ($file in $monitorFiles) {
        # Check if file contains any lines with today's date
        $hasToday = Select-String -Path $file.FullName -Pattern $Date -Quiet -ErrorAction SilentlyContinue
        if ($hasToday) {
            Copy-Item -Path $file.FullName -Destination (Join-Path $analysisFolder $file.Name) -Force
            Write-Log "  Copied (has today's data): $($file.Name)"
            $monitorCopied++
        }
    }
    Write-Log "  $monitorCopied of $($monitorFiles.Count) Monitor file(s) had data for $Date" "OK"

    # ==================== STEP 4: COPY ActiveNikiTrader FILES ====================
    Write-Log "Step 4: Copying ActiveNikiTrader log files..."

    $traderFiles = Get-ChildItem -Path $NT8LogPath -Filter "ActiveNikiTrader_*.txt" -ErrorAction SilentlyContinue
    $traderCopied = 0

    foreach ($file in $traderFiles) {
        $hasToday = Select-String -Path $file.FullName -Pattern $Date -Quiet -ErrorAction SilentlyContinue
        if ($hasToday) {
            Copy-Item -Path $file.FullName -Destination (Join-Path $analysisFolder $file.Name) -Force
            Write-Log "  Copied (has today's data): $($file.Name)"
            $traderCopied++
        }
    }
    Write-Log "  $traderCopied of $($traderFiles.Count) Trader file(s) had data for $Date" "OK"

    # ==================== STEP 5: COPY IndicatorValues FILES ====================
    Write-Log "Step 5: Copying IndicatorValues CSV files..."

    $csvFiles = Get-ChildItem -Path $NT8LogPath -Filter "IndicatorValues_*.csv" -ErrorAction SilentlyContinue
    $csvCopied = 0

    foreach ($file in $csvFiles) {
        # For CSVs, check if any row contains today's date in BarTime
        # Use Select-String for fast check without loading entire file
        $hasToday = Select-String -Path $file.FullName -Pattern $Date -Quiet -ErrorAction SilentlyContinue
        if (!$hasToday) {
            # Also check M/D/YYYY format (e.g., 2/9/2026)
            $dt = [datetime]::ParseExact($Date, "yyyy-MM-dd", $null)
            $altDatePattern = "$($dt.Month)/$($dt.Day)/$($dt.Year)"
            $hasToday = Select-String -Path $file.FullName -Pattern ([regex]::Escape($altDatePattern)) -Quiet -ErrorAction SilentlyContinue
        }
        if ($hasToday) {
            Copy-Item -Path $file.FullName -Destination (Join-Path $analysisFolder $file.Name) -Force
            $fileSize = [math]::Round($file.Length / 1KB, 1)
            Write-Log "  Copied (has today's data): $($file.Name) (${fileSize} KB)"
            $csvCopied++
        }
    }
    Write-Log "  $csvCopied of $($csvFiles.Count) CSV file(s) had data for $Date" "OK"

    # ==================== STEP 6: RUN PYTHON ANALYSIS ====================
    Write-Log "Step 6: Running Python analysis..."

    if ((Test-Path $PythonScript) -and (Test-Path $PythonExe)) {
        $hasData = ($allTrades.Count -gt 0) -or ($traderCopied -gt 0)
        if ($hasData) {
            Write-Log "  $PythonExe $PythonScript $analysisFolder --date $Date --full-timestamps"
            $pythonOutput = & $PythonExe $PythonScript $analysisFolder --date $Date --full-timestamps 2>&1
            $pythonOutput | ForEach-Object { Write-Log "  [PY] $_" }

            if ($LASTEXITCODE -eq 0) {
                # Find generated analysis file
                $analysisFiles = Get-ChildItem -Path $analysisFolder -Filter "*_Trading_Analysis.txt" -ErrorAction SilentlyContinue
                foreach ($af in $analysisFiles) {
                    $afSize = [math]::Round($af.Length / 1KB, 1)
                    Write-Log "  Report: $($af.Name) (${afSize} KB)" "OK"
                }

                # Clean up copied source files (report is self-contained)
                $patterns = @("ActiveNikiMonitor_*.txt", "ActiveNikiTrader_*.txt", "IndicatorValues_*.csv")
                $removed = 0
                foreach ($pattern in $patterns) {
                    Get-ChildItem -Path $analysisFolder -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object {
                        Remove-Item $_.FullName -Force
                        $removed++
                    }
                }
                if ($removed -gt 0) {
                    Write-Log "  Cleaned up $removed copied source file(s)" "OK"
                }
            } else {
                Write-Log "  Python analysis exited with code $LASTEXITCODE" "ERROR"
            }
        } else {
            Write-Log "  Skipping analysis - no trades or trader logs found for $Date" "WARN"
        }
    } else {
        Write-Log "  main.py not found at: $PythonScript" "ERROR"
        Write-Log "  Or python not found at: $PythonExe" "ERROR"
    }

    # ==================== SUMMARY ====================
    Write-Log "========================================"
    Write-Log "  Analysis complete for $Date"
    Write-Log "  Output: $analysisFolder"
    Write-Log "========================================"

    # List all files in analysis folder
    Get-ChildItem $analysisFolder | ForEach-Object {
        $size = if ($_.Length -gt 1024) { "$([math]::Round($_.Length/1KB, 1)) KB" } else { "$($_.Length) bytes" }
        Write-Log "  - $($_.Name) ($size)"
    }

    # ==================== STEP 7: PUSH REPORT TO GITHUB ====================
    Write-Log "Step 7: Pushing report to GitHub..."
    $gitExe    = "C:\Program Files\Git\cmd\git.exe"
    $reportsDir = Join-Path $RepoRoot "reports"

    $reportFile = Get-ChildItem -Path $analysisFolder -Filter "*_Trading_Analysis.txt" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($reportFile) {
        # Resolve VPS name from public IP — skip git push if running on an unrecognized machine
        $publicIP = (Invoke-RestMethod -Uri "https://api.ipify.org" -ErrorAction SilentlyContinue).Trim()
        $VpsName = switch ($publicIP) {
            "104.237.203.83" { "VPS1" }
            "205.234.153.21" { "VPS2" }
            "64.44.56.21"    { "VPS3" }
            default          { $null }
        }

        if (-not $VpsName) {
            Write-Log "  Skipping git push — IP $publicIP is not a known VPS. Run this step manually from a VPS." "WARN"
        } else {
        Write-Log "  VPS identity: $VpsName (IP: $publicIP)"

        # Ensure reports directory exists
        if (!(Test-Path $reportsDir)) {
            New-Item -ItemType Directory -Path $reportsDir -Force | Out-Null
            Write-Log "  Created reports directory: $reportsDir" "OK"
        }

        $destReport  = Join-Path $reportsDir "Trading_Analysis-${VpsName}.txt"
        $tradesFile  = Join-Path $analysisFolder "trades_final.txt"
        $signalsFile = Join-Path $analysisFolder "signals.txt"

        Copy-Item $reportFile.FullName $destReport -Force
        if (Test-Path $tradesFile)  { Copy-Item $tradesFile  (Join-Path $reportsDir "trades_final.txt") -Force }
        if (Test-Path $signalsFile) { Copy-Item $signalsFile (Join-Path $reportsDir "signals.txt")      -Force }

        if (!(Test-Path $gitExe)) {
            $gitCmd = Get-Command git -ErrorAction SilentlyContinue
            if ($gitCmd) { $gitExe = $gitCmd.Source }
        }

        Push-Location $RepoRoot
        try {
            & $gitExe pull --rebase 2>&1 | Out-Null
            & $gitExe add reports/ 2>&1 | Out-Null
            & $gitExe commit -m $Date 2>&1 | Out-Null
            & $gitExe push 2>&1 | Out-Null
            Write-Log "  Git push completed for $Date - Trading_Analysis-${VpsName}.txt" "OK"
        } catch {
            Write-Log "  Git push failed: $_" "ERROR"
        } finally {
            Pop-Location
        }
        } # end if $VpsName
    } else {
        Write-Log "  No analysis report found to push" "WARN"
    }
    # ==================== STEP 8: CLEAN UP OLD LOG FILES ====================
    Write-Log "Step 8: Cleaning up NT8 log files older than 3 weeks..."
    $cutoffDate = (Get-Date).AddDays(-21)
    $cleanupPatterns = @("ActiveNikiTrader_*.txt", "IndicatorValues_*.csv")
    $totalRemoved = 0

    foreach ($pattern in $cleanupPatterns) {
        $oldFiles = Get-ChildItem -Path $NT8LogPath -Filter $pattern -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -lt $cutoffDate }
        foreach ($file in $oldFiles) {
            Remove-Item $file.FullName -Force -ErrorAction SilentlyContinue
            Write-Log "  Deleted: $($file.Name) (last modified $($file.LastWriteTime.ToString('yyyy-MM-dd')))"
            $totalRemoved++
        }
    }

    if ($totalRemoved -gt 0) {
        Write-Log "  Removed $totalRemoved old file(s)" "OK"
    } else {
        Write-Log "  No files older than 3 weeks found" "INFO"
    }

    # ==================== STEP 9: SEND EMAIL REPORT ====================
    Write-Log "Step 9: Sending email report..."

    if (-not $VpsName) {
        Write-Log "  Skipping email — not running on a known VPS." "WARN"
    } else {
        $EmailTo      = "alex.boutov@gmail.com"
        $EmailFrom    = "alex.boutov@gmail.com"
        $EmailAppPass = "oqmy bqia arud hfmf"

        $emailAttachment = Join-Path $reportsDir "Trading_Analysis-${VpsName}.txt"

        if (Test-Path $emailAttachment) {
            try {
                $smtpCred = New-Object System.Management.Automation.PSCredential(
                    $EmailFrom,
                    (ConvertTo-SecureString $EmailAppPass -AsPlainText -Force)
                )
                $tradeCount  = if ($allTrades.Count -gt 0) { $allTrades.Count } else { 0 }
                $signalCount = if ($allSignalLines.Count -gt 0) { $allSignalLines.Count } else { 0 }
                $emailBody = "Trading Analysis Report - ${VpsName} - ${Date}`nTrades filled : ${tradeCount}`nSignal lines  : ${signalCount}`nFull report is attached.`n---`nGenerated automatically by Analyze-VPSTrades.ps1 on ${VpsName}"
                $mailParams = @{
                    From        = $EmailFrom
                    To          = $EmailTo
                    Subject     = "[${VpsName}] Trading Analysis - ${Date}"
                    Body        = $emailBody
                    Attachments = $emailAttachment
                    SmtpServer  = "smtp.gmail.com"
                    Port        = 587
                    UseSsl      = $true
                    Credential  = $smtpCred
                }
                Send-MailMessage @mailParams
                Write-Log "  Email sent to $EmailTo - Trading_Analysis-${VpsName}.txt" "OK"
            } catch {
                Write-Log "  Email send failed: $_" "ERROR"
            }
        } else {
            Write-Log "  Email attachment not found, skipping: $emailAttachment" "WARN"
        }
    }

} catch {
    Write-Log "FATAL: $($_.Exception.Message)" "ERROR"
    Write-Log "Stack: $($_.ScriptStackTrace)" "ERROR"
    exit 1
}
