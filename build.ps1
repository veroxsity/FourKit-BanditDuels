#requires -Version 5
# Build BanditDuels and deploy the single-DLL plugin to the server.
# SQLite (managed + native) is embedded inside BanditDuels.dll as resources;
# BundleLoader.cs loads the managed deps via AssemblyResolve and extracts the
# native binary to %TEMP%\BanditDuels-<hash>\ on first DB access.

param([switch]$StopServer)

$ErrorActionPreference = 'Stop'

$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$plug   = $PSScriptRoot
$server = Resolve-Path (Join-Path $plug '..\..\Server')

# Ensure FourKit reference is present in lib/
$libDll = Join-Path $plug 'lib\Minecraft.Server.FourKit.dll'
if (-not (Test-Path $libDll)) {
    Copy-Item (Join-Path $server 'Minecraft.Server.FourKit.dll') $libDll -Force
    Write-Host "Synced FourKit reference into lib/"
}

& $dotnet build (Join-Path $plug 'BanditDuels.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }

$builtDir   = Join-Path $plug 'bin\Release\net10.0'
$pluginsDir = Join-Path $server 'plugins'

# Stop server if needed before touching the plugin DLL
$running = Get-Process -Name 'Minecraft.Server' -ErrorAction SilentlyContinue
if ($running) {
    if ($StopServer) {
        Write-Host "Stopping running server (PID $($running.Id))..."
        Stop-Process -Id $running.Id -Force
        Start-Sleep -Milliseconds 800
    } else {
        Write-Host ""
        Write-Host "WARNING: Minecraft.Server.exe is running (PID $($running.Id))." -ForegroundColor Yellow
        Write-Host "         Stop the server, or re-run with -StopServer." -ForegroundColor Yellow
        Write-Host "Build succeeded but skipped deployment." -ForegroundColor Yellow
        exit 2
    }
}

# Clean up stray files from older deploy layouts. The single-DLL bundle means
# no managed or native SQLite DLLs should live alongside the plugin.
$strayPaths = @(
    'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.batteries_v2.dll',
    'SQLitePCLRaw.core.dll',
    'SQLitePCLRaw.provider.e_sqlite3.dll',
    'e_sqlite3.dll',
    'BanditDuels.deps',
    'runtimes'
)
foreach ($p in $strayPaths) {
    $full = Join-Path $pluginsDir $p
    if (Test-Path $full) { Remove-Item $full -Recurse -Force }
}

Copy-Item (Join-Path $builtDir 'BanditDuels.dll') (Join-Path $pluginsDir 'BanditDuels.dll') -Force

Write-Host ""
Write-Host "Deployed:"
Get-Item (Join-Path $pluginsDir 'BanditDuels.dll') | Format-List FullName, LastWriteTime, Length
Write-Host "SQLite dependencies are embedded in BanditDuels.dll"
