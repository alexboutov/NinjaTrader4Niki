# Use date from command line argument, or default to today
param([string]$date = (Get-Date -Format "yyyy-MM-dd"))

# Navigate to the log folder
$logFolder = "C:\Users\alexb\Downloads\ActiveNiki\$date"
cd $logFolder

# 1. Extract filled trades
Select-String -Path "log.*.txt" -Pattern "New state='Filled'" | ForEach-Object { $_.Line } > trades_final.txt

# 2. Get signals from ActiveNikiMonitor log
Select-String -Path "ActiveNikiMonitor_*.txt" -Pattern "SIGNAL|flip" | ForEach-Object { $_.Line } > signals.txt

# 3. Check file sizes
Get-Item trades_final.txt, signals.txt | Select-Object Name, Length

# 4. Display trades
cat .\trades_final.txt

# 5. Display signals
cat .\signals.txt

# 6. Return to code folder
cd C:\Users\alexb\Downloads\ActiveNiki\code