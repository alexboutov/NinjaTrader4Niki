<#
.SYNOPSIS
    Analyzes NinjaTrader 8 logs from local machine, filtered to trading hours,
    parses into trades_final.txt and signals.txt, and generates analysis report.

.DESCRIPTION
    - Reads NT8 logs from local NinjaTrader log folder
    - Filters to trading hours (7-11:30 AM Eastern)
    - Creates dated folder with _local suffix
    - Parses logs into trades_final.txt and signals.txt
    - Runs Python analysis to generate {Mon}{DD}_Trading_Analysis.txt
    - Can run manually or via scheduled task

.PARAMETER Date
    The trading date to analyze logs for. Defaults to today.
    Format: yyyy-MM-dd

.PARAMETER Clipboard
    If specified, copies trades then signals to clipboard (prompts between)

.EXAMPLE
    .\AnalyzeLocalTrades.ps1
    # Parses and analyzes today's local logs

.EXAMPLE
    .\AnalyzeLocalTrades.ps1 -Date "2025-12-19"
    # Parses and analyzes logs for specific date

.EXAMPLE
    .\AnalyzeLocalTrades.ps1 -Clipboard
    # Parses, analyzes, and copies to clipboard for pasting into Claude
#>

param(
    [string]$Date = (Get-Date -Format "yyyy-MM-dd"),
    [switch]$Clipboard
)

# === CONFIGURATION ===
$LocalLogPath = "C:\Users\alexb\OneDrive\Documents\NinjaTrader 8\log"
$LocalBasePath = "C:\Users\alexb\Downloads\ActiveNiki"
$TradingStartTime = "07:00:00"
$TradingEndTime = "11:30:00"

# === FUNCTIONS ===

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "HH:mm:ss"
    $color = switch ($Level) {
        "ERROR" { "Red" }
        "WARN"  { "Yellow" }
        "OK"    { "Green" }
        default { "White" }
    }
    Write-Host "[$timestamp] $Message" -ForegroundColor $color
}

# === MAIN SCRIPT ===

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Local NinjaTrader Log Analysis" -ForegroundColor Cyan
Write-Host "  Date: $Date" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Create local dated folder with _local suffix
$localFolder = Join-Path $LocalBasePath "${Date}_local"
if (!(Test-Path $localFolder)) {
    New-Item -ItemType Directory -Path $localFolder -Force | Out-Null
    Write-Log "Created folder: $localFolder" "OK"
} else {
    Write-Log "Folder exists: $localFolder"
}

# Convert date format for NT8 logs (yyyy-MM-dd -> yyyyMMdd)
$nt8DateFormat = $Date -replace "-", ""

try {
    # ==================== PART 1: FIND LOG FILES ====================
    Write-Log "Scanning local logs at: $LocalLogPath"
    
    # Find NT8 standard logs for this date (exclude .en.txt duplicates)
    $nt8Logs = Get-ChildItem -Path $LocalLogPath -Filter "log.$nt8DateFormat.*.txt" -ErrorAction SilentlyContinue | 
        Where-Object { $_.Name -notlike "*.en.txt" }
    
    # Find ActiveNikiMonitor logs for this date
    $monitorLogs = Get-ChildItem -Path $LocalLogPath -Filter "ActiveNikiMonitor_$Date*.txt" -ErrorAction SilentlyContinue
    
    # Find ActiveNikiTrader logs for this date
    $traderLogs = Get-ChildItem -Path $LocalLogPath -Filter "ActiveNikiTrader_$Date*.txt" -ErrorAction SilentlyContinue
    
    # Find IndicatorValues CSV for this date
    $indicatorLogs = Get-ChildItem -Path $LocalLogPath -Filter "IndicatorValues_$Date*.csv" -ErrorAction SilentlyContinue
    
    Write-Log "Found $($nt8Logs.Count) NT8 log(s), $($monitorLogs.Count) Monitor log(s), $($traderLogs.Count) Trader log(s)"
    
    if ($nt8Logs.Count -eq 0) {
        Write-Log "No NT8 logs found for $Date" "WARN"
    }
    
    # ==================== PART 2: PARSE ====================
    Write-Host ""
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    Write-Host "  Parsing logs..." -ForegroundColor Cyan
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    
    # 1. Extract filled trades from NT8 logs (filtered to trading hours)
    Write-Log "Extracting filled trades (trading hours: $TradingStartTime - $TradingEndTime)..."
    $tradesFile = Join-Path $localFolder "trades_final.txt"
    $allTrades = @()
    
    foreach ($logFile in $nt8Logs) {
        $lines = Get-Content $logFile.FullName
        $filteredTrades = $lines | Where-Object {
            if ($_ -match "New state='Filled'" -and $_ -match "^(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2})") {
                $time = $Matches[2]
                return ($time -ge $TradingStartTime -and $time -le $TradingEndTime)
            }
            return $false
        }
        $allTrades += $filteredTrades
    }
    
    if ($allTrades.Count -gt 0) {
        $allTrades | Out-File -FilePath $tradesFile -Encoding UTF8
        Write-Log "  trades_final.txt: $($allTrades.Count) trades" "OK"
    } else {
        Write-Log "  No filled trades found in trading hours" "WARN"
        "" | Out-File -FilePath $tradesFile -Encoding UTF8
    }
    
    # 2. Extract signals and flips from ActiveNikiMonitor logs
    Write-Log "Extracting signals from Monitor logs..."
    $signalsFile = Join-Path $localFolder "signals.txt"
    $allSignals = @()
    
    foreach ($logFile in $monitorLogs) {
        $signals = Select-String -Path $logFile.FullName -Pattern "SIGNAL|flip" -ErrorAction SilentlyContinue | 
            ForEach-Object { $_.Line }
        $allSignals += $signals
    }
    
    if ($allSignals.Count -gt 0) {
        $allSignals | Out-File -FilePath $signalsFile -Encoding UTF8
        Write-Log "  signals.txt: $($allSignals.Count) lines" "OK"
    } else {
        Write-Log "  No Monitor signals found" "WARN"
        "" | Out-File -FilePath $signalsFile -Encoding UTF8
    }
    
    # 3. Copy ActiveNikiTrader logs to the analysis folder (for Python script)
    Write-Log "Copying ActiveNikiTrader logs..."
    foreach ($logFile in $traderLogs) {
        $destPath = Join-Path $localFolder $logFile.Name
        Copy-Item -Path $logFile.FullName -Destination $destPath -Force
        Write-Log "  Copied: $($logFile.Name)" "OK"
    }
    
    # 4. Copy IndicatorValues CSV to the analysis folder
    Write-Log "Copying IndicatorValues CSV..."
    foreach ($logFile in $indicatorLogs) {
        $destPath = Join-Path $localFolder $logFile.Name
        Copy-Item -Path $logFile.FullName -Destination $destPath -Force
        Write-Log "  Copied: $($logFile.Name)" "OK"
    }
    
    # ==================== PART 3: ANALYZE ====================
    Write-Host ""
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    Write-Host "  Generating analysis..." -ForegroundColor Cyan
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    
    $analyzeScript = Join-Path $LocalBasePath "Analyze-TradingSession.py"
    
    if (Test-Path $analyzeScript) {
        if ($allTrades.Count -gt 0 -or $traderLogs.Count -gt 0) {
            Write-Log "Running Python analysis..."
            $pythonResult = python $analyzeScript $localFolder --date $Date 2>&1
            
            if ($LASTEXITCODE -eq 0) {
                # Find the generated analysis file
                $analysisFiles = Get-ChildItem -Path $localFolder -Filter "*_Trading_Analysis.txt" -ErrorAction SilentlyContinue
                
                foreach ($analysisFile in $analysisFiles) {
                    $analysisSize = $analysisFile.Length
                    Write-Log "  $($analysisFile.Name) ($([math]::Round($analysisSize/1KB, 1)) KB)" "OK"
                }
            } else {
                Write-Log "  Python analysis failed: $pythonResult" "WARN"
            }
        } else {
            Write-Log "  Skipping analysis - no trades or trader logs found" "WARN"
        }
    } else {
        Write-Log "  Analyze-TradingSession.py not found at $analyzeScript" "WARN"
        Write-Log "  Copy it to $LocalBasePath to enable auto-analysis" "WARN"
    }
    
    # ==================== SUMMARY ====================
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  Complete! Analysis saved to:" -ForegroundColor Green
    Write-Host "  $localFolder" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    
    # List all files
    Get-ChildItem $localFolder | ForEach-Object {
        $size = if ($_.Length -gt 1024) { "$([math]::Round($_.Length/1KB, 1)) KB" } else { "$($_.Length) bytes" }
        Write-Host "  - $($_.Name) ($size)" -ForegroundColor Gray
    }
    
    # ==================== CLIPBOARD OPTION ====================
    if ($Clipboard) {
        Write-Host ""
        Write-Host "Press Enter to copy trades_final.txt to clipboard..." -ForegroundColor Yellow
        Read-Host
        Get-Content $tradesFile | Set-Clipboard
        Write-Log "trades_final.txt copied to clipboard" "OK"
        
        Write-Host "Press Enter to copy signals.txt to clipboard..." -ForegroundColor Yellow
        Read-Host
        Get-Content $signalsFile | Set-Clipboard
        Write-Log "signals.txt copied to clipboard" "OK"
    }
    
    Write-Host ""
    
} catch {
    Write-Log "ERROR: $($_.Exception.Message)" "ERROR"
    Write-Host ""
    Write-Host "Troubleshooting tips:" -ForegroundColor Yellow
    Write-Host "  1. Verify log folder exists: $LocalLogPath" -ForegroundColor Gray
    Write-Host "  2. Check if NinjaTrader has created logs for $Date" -ForegroundColor Gray
    Write-Host "  3. Ensure Python is installed and Analyze-TradingSession.py exists" -ForegroundColor Gray
    exit 1
}
