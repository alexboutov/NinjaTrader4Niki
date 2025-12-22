# Navigate to the folder
cd C:\Users\alexb\Downloads\ActiveNiki\2025-12-19_save

# 1. Extract filled trades
Select-String -Path "log.*.txt" -Pattern "New state='Filled'" | ForEach-Object { $_.Line } > trades_final.txt

# 2. Get signals from ActiveNikiMonitor log
Select-String -Path "ActiveNikiMonitor_*.txt" -Pattern "SIGNAL|flip" | ForEach-Object { $_.Line } > signals.txt

# 3. Check file sizes
Get-Item trades_final.txt, signals.txt | Select-Object Name, Length

# 4. Copy trades to clipboard (paste first)
cat .\trades_final.txt | clip

# 5. Copy signals to clipboard (paste second)
cat .\signals.txt | clip