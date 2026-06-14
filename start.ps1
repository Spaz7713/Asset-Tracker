# Start Asset Tracker (portable)
$project = Join-Path $PSScriptRoot 'MediaTracker.csproj'
Write-Output "Restoring packages..."
dotnet restore $project
Write-Output "Building..."
dotnet build $project -c Debug
Write-Output "Running..."
dotnet run --project $project -c Debug
