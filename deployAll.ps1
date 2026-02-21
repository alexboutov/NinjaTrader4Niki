# Deploy indicators and strategies to NinjaTrader

# Define paths
$repoPath = "C:\Users\Administrator\Documents\TradingRepo\NinjaTrader4Niki"
$ntIndicatorsPath = "C:\Users\Administrator\Documents\NinjaTrader 8\bin\Custom\Indicators"
$ntStrategiesPath = "C:\Users\Administrator\Documents\NinjaTrader 8\bin\Custom\Strategies"

# Indicators to deploy
$indicators = @(
    "ActiveNikiMonitor.cs",
    "AAATrendSyncEquivalent.cs",
    "AIQ_1Equivalent.cs",
    "AIQ_SuperBandsEquivalent.cs",
    "DragonTrendEquivalent.cs",
    "EasyTrendEquivalent.cs",
    "RubyRiverEquivalent.cs",
    "SolarWaveEquivalent.cs",
    "T3ProEquivalent.cs",
    "VIDYAProEquivalent.cs"
)

# Strategy files to deploy
$strategies = @(
    "ActiveNikiTrader.cs",
    "ActiveNikiTrader.Indicators.cs",
    "ActiveNikiTrader.Logging.cs",
    "ActiveNikiTrader.Panel.cs",
    "ActiveNikiTrader.Signals.cs"
)

Write-Host ""
Write-Host "===== DEPLOYING INDICATORS =====" -ForegroundColor Cyan
$indicatorSuccess = 0
$indicatorFail = 0

foreach ($file in $indicators) {
    $sourceFile = Join-Path $repoPath $file
    $destFile = Join-Path $ntIndicatorsPath $file
    
    if (Test-Path $sourceFile) {
        Copy-Item $sourceFile $destFile -Force
        Write-Host "[OK] Copied $file" -ForegroundColor Green
        $indicatorSuccess++
    } else {
        Write-Host "[FAIL] Not found: $file" -ForegroundColor Red
        $indicatorFail++
    }
}

Write-Host ""
Write-Host "===== DEPLOYING STRATEGIES =====" -ForegroundColor Cyan
$strategySuccess = 0
$strategyFail = 0

foreach ($file in $strategies) {
    $sourceFile = Join-Path $repoPath $file
    $destFile = Join-Path $ntStrategiesPath $file
    
    if (Test-Path $sourceFile) {
        Copy-Item $sourceFile $destFile -Force
        Write-Host "[OK] Copied $file" -ForegroundColor Green
        $strategySuccess++
    } else {
        Write-Host "[FAIL] Not found: $file" -ForegroundColor Red
        $strategyFail++
    }
}

Write-Host ""
Write-Host "===== SUMMARY =====" -ForegroundColor Cyan
Write-Host "Indicators: $indicatorSuccess copied, $indicatorFail failed" -ForegroundColor White
Write-Host "Strategies: $strategySuccess copied, $strategyFail failed" -ForegroundColor White
Write-Host ""
Write-Host "Destinations:" -ForegroundColor Gray
Write-Host "  Indicators: $ntIndicatorsPath" -ForegroundColor Gray
Write-Host "  Strategies: $ntStrategiesPath" -ForegroundColor Gray
Write-Host ""
Write-Host "Next: Open NinjaTrader and compile (Tools > Edit NinjaScript > Strategy or press F5)" -ForegroundColor Yellow
