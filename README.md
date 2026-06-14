MediaTracker (Asset Tracker)

Quick launch (development)

1. Prerequisites
   - .NET SDK 8.0 or later installed
   - (Windows) LocalDB available for SQL Server LocalDB

2. Restore packages
   dotnet restore

3. Trust the ASP.NET Core dev certificate (interactive)
   dotnet dev-certs https --trust
   (Accept the OS prompt to trust the certificate.)

4. Build
   dotnet build MediaTracker.csproj -c Debug

5. Run the app
   - From the repo root you can use the included portable scripts:
	 - Windows batch: start.bat
	 - PowerShell: start.ps1
   - Or run directly:
	 dotnet run --project MediaTracker.csproj

6. Open in browser
   - https://localhost:5001 or http://localhost:5000
   - The app's default page is the Inventory (Asset) index.

Notes
- The development startup recreates the LocalDB database to pick up model changes. Don't use this in production.
- Barcode generation uses System.Drawing (Windows). If you run on non-Windows, replace the renderer with a cross-platform library or render an HTML label for printing.
- Labels: use the "Labels" menu to generate inventory numbers and PDF417 barcode images. Assigning a label creates a Label record and does not overwrite SKU.

If you want a different README format or extra instructions (Dockerfile, migrations, CI), tell me which items to add.

Quick start (clone + run)
1. git clone <repo>
2. cd "Asset Tracker"
3. .\start.bat   # or pwsh .\start.ps1

The start scripts use relative paths and can be committed to the repo so collaborators can launch the app without editing absolute paths.
