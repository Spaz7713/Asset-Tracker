# Creates a Desktop shortcut to launch the Asset Tracker and then starts the app
$project = Join-Path $PSScriptRoot "MediaTracker.csproj"
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop "Launch Asset Tracker.lnk"

try {
	$WshShell = New-Object -ComObject WScript.Shell
	$shortcut = $WshShell.CreateShortcut($shortcutPath)
	# Use dotnet executable directly to avoid nested PowerShell argument quoting
	$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
	$shortcut.TargetPath = $dotnet
	$shortcut.Arguments = 'run --project "' + $project + '" -c Debug'
	$shortcut.WorkingDirectory = $PSScriptRoot
	$shortcut.WindowStyle = 1
	# set icon if present, ignore if missing
	$iconPath = Join-Path $PSScriptRoot 'favicon.ico'
	if (Test-Path $iconPath) { $shortcut.IconLocation = "$iconPath,0" }
	$shortcut.Save()
	Write-Output "Shortcut created at: $shortcutPath"
} catch {
	Write-Warning "Failed to create shortcut: $_"
}

# Stop any running MediaTracker process (apphost process name is MediaTracker when run)
try {
	Get-Process -Name MediaTracker -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
	Start-Sleep -Milliseconds 500
} catch {}

# Start the app
Write-Output "Starting Asset Tracker..."
try {
	Start-Process -FilePath $dotnet -ArgumentList @('run','--project',$project,'-c','Debug') -WorkingDirectory $PSScriptRoot -NoNewWindow
	Write-Output "Launched. Open https://localhost:5001 or http://localhost:5000 in your browser."
} catch {
	Write-Warning "Failed to start Asset Tracker: $_"
}
