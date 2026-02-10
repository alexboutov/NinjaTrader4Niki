<#
.SYNOPSIS
    Transfers NinjaTrader 8 logs from VPS to local machine, filtered to trading hours,
    then parses into trades_final.txt and signals.txt, and generates analysis report.

.DESCRIPTION
    - Connects to VPS via WinRM
    - Downloads NT8 standard logs filtered to 7-11:30 AM Eastern
    - Downloads ActiveNikiMonitor logs (full files)
    - Creates dated folder locally
    - Parses logs into trades_final.txt and signals.txt
    - Runs Python analysis to generate Dec{DD}_Trading_Analysis.txt
    - Can run manually or via scheduled task

.PARAMETER Date
    The trading date to retrieve logs for. Defaults to today.
    Format: yyyy-MM-dd

.PARAMETER VpsIp
    VPS IP address. Defaults to stored value.

.PARAMETER Clipboard
    If specified, copies trades then signals to clipboard (prompts between)

.EXAMPLE
    .\Get-TradingLogs.ps1
    # Gets, parses, and analyzes today's logs

.EXAMPLE
    .\Get-TradingLogs.ps1 -Date "2025-12-19"
    # Gets, parses, and analyzes logs for specific date

.EXAMPLE
    .\Get-TradingLogs.ps1 -Clipboard
    # Gets, parses, analyzes, and copies to clipboard for pasting into Claude
#>

param(
    [string]$Date = (Get-Date -Format "yyyy-MM-dd"),
    [string]$VpsIp = "104.237.203.83",
    [switch]$Clipboard
)

# === CONFIGURATION ===
$VpsLogPath = "C:\Users\Administrator\Documents\NinjaTrader 8\log"
$LocalBasePath = "C:\Users\alexb\Downloads\ActiveNiki"
$CredentialPath = "$env:USERPROFILE\.vps_cred.xml"
$TradingStartTime = "07:00:00"
$TradingEndTime = "11:30:00"

# === FUNCTIONS ===

function Get-StoredCredential {
    if (Test-Path $CredentialPath) {
        try {
            return Import-Clixml -Path $CredentialPath
        } catch {
            Write-Warning "Failed to load stored credential. Will prompt for new one."
        }
    }
    
    Write-Host "No stored credential found. Enter VPS credentials:" -ForegroundColor Yellow
    Write-Host "Username should be: Admin33216" -ForegroundColor Cyan
    $cred = Get-Credential -Message "Enter VPS credentials (Admin33216)"
    
    # Store for future use
    $cred | Export-Clixml -Path $CredentialPath
    Write-Host "Credential stored at: $CredentialPath" -ForegroundColor Green
    
    return $cred
}

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
Write-Host "  NinjaTrader Log Transfer & Parse" -ForegroundColor Cyan
Write-Host "  Date: $Date" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get credentials
$cred = Get-StoredCredential

# Create local dated folder
$localFolder = Join-Path $LocalBasePath $Date
if (!(Test-Path $localFolder)) {
    New-Item -ItemType Directory -Path $localFolder -Force | Out-Null
    Write-Log "Created folder: $localFolder" "OK"
} else {
    Write-Log "Folder exists: $localFolder"
}

# Convert date format for NT8 logs (yyyy-MM-dd -> yyyyMMdd)
$nt8DateFormat = $Date -replace "-", ""

try {
    # ==================== PART 1: TRANSFER ====================
    Write-Log "Connecting to VPS at $VpsIp..."
    
    # Get list of matching log files from VPS
    $remoteFiles = Invoke-Command -ComputerName $VpsIp -Credential $cred -ScriptBlock {
        param($logPath, $nt8Date, $isoDate)
        
        $files = @{
            NT8 = @()
            ActiveNiki = @()
        }
        
        # Find NT8 standard logs for this date (exclude .en.txt duplicates)
        Get-ChildItem -Path $logPath -Filter "log.$nt8Date.*.txt" | 
            Where-Object { $_.Name -notlike "*.en.txt" } |
            ForEach-Object { $files.NT8 += $_.Name }
        
        # Find ActiveNikiMonitor logs for this date
        Get-ChildItem -Path $logPath -Filter "ActiveNikiMonitor_$isoDate*.txt" |
            ForEach-Object { $files.ActiveNiki += $_.Name }
        
        # Find ActiveNiki_v2 logs for this date (strategy logs)
        Get-ChildItem -Path $logPath -Filter "ActiveNiki_v2_*_$isoDate*.txt" |
            ForEach-Object { $files.ActiveNiki += $_.Name }
        
        return $files
    } -ArgumentList $VpsLogPath, $nt8DateFormat, $Date
    
    Write-Log "Found $($remoteFiles.NT8.Count) NT8 log(s), $($remoteFiles.ActiveNiki.Count) ActiveNiki log(s)"
    
    # Download and filter NT8 logs
    foreach ($fileName in $remoteFiles.NT8) {
        Write-Log "Processing NT8 log: $fileName"
        
        $filteredContent = Invoke-Command -ComputerName $VpsIp -Credential $cred -ScriptBlock {
            param($logPath, $fileName, $startTime, $endTime)
            
            $fullPath = Join-Path $logPath $fileName
            $lines = Get-Content $fullPath
            
            $filtered = $lines | Where-Object {
                if ($_ -match "^(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2})") {
                    $time = $Matches[2]
                    return ($time -ge $startTime -and $time -le $endTime)
                }
                return $false
            }
            
            return $filtered
        } -ArgumentList $VpsLogPath, $fileName, $TradingStartTime, $TradingEndTime
        
        if ($filteredContent -and $filteredContent.Count -gt 0) {
            $localFile = Join-Path $localFolder $fileName
            $filteredContent | Out-File -FilePath $localFile -Encoding UTF8
            Write-Log "  Saved $($filteredContent.Count) lines (trading hours only)" "OK"
        } else {
            Write-Log "  No trading hours data in this log" "WARN"
        }
    }
    
    # Download ActiveNiki logs (full files, no filtering)
    foreach ($fileName in $remoteFiles.ActiveNiki) {
        Write-Log "Downloading ActiveNiki log: $fileName"
        
        $content = Invoke-Command -ComputerName $VpsIp -Credential $cred -ScriptBlock {
            param($logPath, $fileName)
            $fullPath = Join-Path $logPath $fileName
            return Get-Content $fullPath -Raw
        } -ArgumentList $VpsLogPath, $fileName
        
        if ($content) {
            $localFile = Join-Path $localFolder $fileName
            $content | Out-File -FilePath $localFile -Encoding UTF8 -NoNewline
            Write-Log "  Saved $(($content -split "`n").Count) lines" "OK"
        }
    }
    
    Write-Host ""
    Write-Log "Transfer complete." "OK"
    
    # ==================== PART 2: PARSE ====================
    Write-Host ""
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    Write-Host "  Parsing logs..." -ForegroundColor Cyan
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    
    # 1. Extract filled trades
    Write-Log "Extracting filled trades..."
    $tradesFile = Join-Path $localFolder "trades_final.txt"
    $trades = Select-String -Path "$localFolder\log.*.txt" -Pattern "New state='Filled'" -ErrorAction SilentlyContinue | ForEach-Object { $_.Line }
    
    if ($trades) {
        $trades | Out-File -FilePath $tradesFile -Encoding UTF8
        Write-Log "  trades_final.txt: $($trades.Count) trades" "OK"
    } else {
        Write-Log "  No filled trades found" "WARN"
        "" | Out-File -FilePath $tradesFile -Encoding UTF8
    }
    
    # 2. Extract signals and flips
    Write-Log "Extracting signals and flips..."
    $signalsFile = Join-Path $localFolder "signals.txt"
    $signals = Select-String -Path "$localFolder\ActiveNikiMonitor_*.txt" -Pattern "SIGNAL|flip" -ErrorAction SilentlyContinue | ForEach-Object { $_.Line }
    
    if ($signals) {
        $signals | Out-File -FilePath $signalsFile -Encoding UTF8
        Write-Log "  signals.txt: $($signals.Count) lines" "OK"
    } else {
        Write-Log "  No signals found" "WARN"
        "" | Out-File -FilePath $signalsFile -Encoding UTF8
    }
    
    # ==================== PART 3: ANALYZE ====================
    Write-Host ""
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    Write-Host "  Generating analysis..." -ForegroundColor Cyan
    Write-Host "----------------------------------------" -ForegroundColor Cyan
    
    $analyzeScript = Join-Path $LocalBasePath "Analyze-TradingSession.py"
    
    if (Test-Path $analyzeScript) {
        if ($trades -and $trades.Count -gt 0) {
            Write-Log "Running Python analysis..."
            $pythonResult = python $analyzeScript $localFolder --date $Date 2>&1
            
            if ($LASTEXITCODE -eq 0) {
                # Extract output filename from Python output
                $day = [int]($Date.Split('-')[2])
                $analysisFile = "Dec{0:D2}_Trading_Analysis.txt" -f $day
                $analysisPath = Join-Path $localFolder $analysisFile
                
                if (Test-Path $analysisPath) {
                    $analysisSize = (Get-Item $analysisPath).Length
                    Write-Log "  $analysisFile ($([math]::Round($analysisSize/1KB, 1)) KB)" "OK"
                }
            } else {
                Write-Log "  Python analysis failed: $pythonResult" "WARN"
            }
        } else {
            Write-Log "  Skipping analysis - no trades found" "WARN"
        }
    } else {
        Write-Log "  Analyze-TradingSession.py not found at $analyzeScript" "WARN"
        Write-Log "  Copy it to $LocalBasePath to enable auto-analysis" "WARN"
    }
    
    # ==================== SUMMARY ====================
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  Complete! Files saved to:" -ForegroundColor Green
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
    Write-Host "  1. Verify VPS is accessible: Test-NetConnection -ComputerName $VpsIp -Port 5985" -ForegroundColor Gray
    Write-Host "  2. Delete stored credential and re-enter: Remove-Item $CredentialPath" -ForegroundColor Gray
    Write-Host "  3. Check VPS is not in use (market hours restriction)" -ForegroundColor Gray
    exit 1
}
