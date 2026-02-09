<#
.SYNOPSIS
    Analyze-VPSTrades.ps1
    Analyzes NinjaTrader 8 trading logs directly on the VPS.

.DESCRIPTION
    - Scans ALL log files in the NT8 log directory (any date in filename)
    - Extracts today's trading data from all files (handles cross-day strategy sessions)
    - Creates dated analysis folder under ActiveNikiAnalysis\YYYY-MM-DD\
    - Generates trades_final.txt, signals.txt
    - Copies ActiveNikiMonitor, ActiveNikiTrader, and IndicatorValues files
    - Runs Python analysis to produce {Mon}{DD}_Trading_Analysis.txt
    - Designed to run unattended via scheduled task (SYSTEM account)

    Processing order (smallest files first for performance):
      1. log.*.txt           → extract today's filled trades
      2. ActiveNikiMonitor_* → copy for signal parsing
      3. ActiveNikiTrader_*  → copy for order/close parsing
      4. IndicatorValues_*   → copy for BAR analysis (largest)

.PARAMETER Date
    The trading date to analyze. Defaults to today. Format: yyyy-MM-dd

.EXAMPLE
    .\Analyze-VPSTrades.ps1
    # Analyzes today's logs

.EXAMPLE
    .\Analyze-VPSTrades.ps1 -Date "2026-02-07"
    # Analyzes logs for a specific date
#>

param(
    [string]$Date = (Get-Date -Format "yyyy-MM-dd")
)

# === CONFIGURATION ===
$NT8LogPath = "C:\Users\Administrator\Documents\NinjaTrader 8\log"
$AnalysisBasePath = Join-Path $NT8LogPath "ActiveNikiAnalysis"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PythonScript = Join-Path $ScriptDir "main.py"
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

    if (Test-Path $PythonScript) {
        $hasData = ($allTrades.Count -gt 0) -or ($traderCopied -gt 0)
        if ($hasData) {
            Write-Log "  python $PythonScript $analysisFolder --date $Date"
            $pythonOutput = python $PythonScript $analysisFolder --date $Date 2>&1
            $pythonOutput | ForEach-Object { Write-Log "  [PY] $_" }

            if ($LASTEXITCODE -eq 0) {
                # Find generated analysis file
                $analysisFiles = Get-ChildItem -Path $analysisFolder -Filter "*_Trading_Analysis.txt" -ErrorAction SilentlyContinue
                foreach ($af in $analysisFiles) {
                    $afSize = [math]::Round($af.Length / 1KB, 1)
                    Write-Log "  Report: $($af.Name) (${afSize} KB)" "OK"
                }
            } else {
                Write-Log "  Python analysis exited with code $LASTEXITCODE" "ERROR"
            }
        } else {
            Write-Log "  Skipping analysis - no trades or trader logs found for $Date" "WARN"
        }
    } else {
        Write-Log "  main.py not found at: $PythonScript" "ERROR"
        Write-Log "  Expected location: $ScriptDir" "ERROR"
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

} catch {
    Write-Log "FATAL: $($_.Exception.Message)" "ERROR"
    Write-Log "Stack: $($_.ScriptStackTrace)" "ERROR"
    exit 1
}
