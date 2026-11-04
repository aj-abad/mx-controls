# Rebuilds MX Studio as one portable exe, closes the running window, puts the new exe in place and starts it
# again.
# Usage: deploy.cmd [-Destination <path to MX Studio.exe>] [-SkipBuild]
#
#  - The exe carries the agent inside it (the app's embedded-agent feature). On launch it unpacks the agent to
#    %LOCALAPPDATA%\MxControls\mx-agent.exe, where the agent's sign-in task points, and stops the older agent
#    there first. So the agent keeps the buttons working until the new window takes over, and a failed build
#    leaves both the window and the agent alone.
#  - The window only shows settings; closing it doesn't touch the buttons.
param(
    [string]$Destination = 'D:\Programs\MX Studio.exe',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$CloseTimeoutMs = 5000
$desktop = Join-Path $PSScriptRoot 'desktop'
$built = Join-Path $desktop 'src-tauri\target\release\mx-controls.exe'
$agent = Join-Path $env:LOCALAPPDATA 'MxControls\mx-agent.exe'
$Destination = [IO.Path]::GetFullPath($Destination)

function Get-Running([string]$path) {
    Get-Process | Where-Object { $_.Path -and [string]::Equals($_.Path, $path, 'OrdinalIgnoreCase') }
}

# Not Get-FileHash: Windows PowerShell started from PowerShell 7 inherits a module path it can't load it from.
function Get-Sha256([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($path))) } finally { $sha.Dispose() }
}

# Any running copy, wherever it is: a new one would only hand over to it and exit. Earlier builds were MX Controls.
function Stop-Window {
    $windows = @(Get-Process | Where-Object { $_.Path -and $_.Description -in 'MX Studio', 'MX Controls' })
    if (-not $windows) {
        Write-Host 'MX Studio is not open.'
        return
    }
    foreach ($window in $windows) {
        Write-Host "Closing MX Studio (process $($window.Id))..."
        if ($window.CloseMainWindow() -and $window.WaitForExit($CloseTimeoutMs)) { continue }
        Write-Warning 'The window did not close when asked; ending it.'
        Stop-Process -Id $window.Id -Force
        $window.WaitForExit($CloseTimeoutMs) | Out-Null
    }
}

if (-not $SkipBuild) {
    Write-Host 'Building...'
    Push-Location $desktop
    try {
        # tauri build runs `pnpm agent && pnpm generate` first, so the embedded agent and the pages are current.
        & pnpm tauri build --no-bundle --features embedded-agent
        if ($LASTEXITCODE -ne 0) { throw "The build failed (exit code $LASTEXITCODE); the running copy was left alone." }
    } finally {
        Pop-Location
    }
}
if (-not (Test-Path $built)) { throw "$built not found; run without -SkipBuild." }

# The exe carries the sidecar build had at the time, unless `pnpm agent` has replaced it since.
$hostTriple = ([string]((rustc -vV) -match '^host: ') -replace '^host: ', '').Trim()
$sidecar = Get-Item (Join-Path $desktop "src-tauri\binaries\mx-agent-$hostTriple.exe")
$expected = if ($sidecar.LastWriteTime -le (Get-Item $built).LastWriteTime) { Get-Sha256 $sidecar.FullName }

Stop-Window

Write-Host "Copying to $Destination..."
New-Item -ItemType Directory -Force -Path (Split-Path $Destination) | Out-Null
for ($attempt = 1; ; $attempt++) {
    # Antivirus scanning can hold the old file for a moment after the process exits.
    try { Copy-Item $built $Destination -Force; break }
    catch { if ($attempt -ge 10) { throw }; Start-Sleep -Milliseconds 300 }
}

Write-Host 'Starting MX Studio...'
Start-Process $Destination -WorkingDirectory (Split-Path $Destination)

# The first start of a new build stops the old agent and unpacks this one before starting it, so an agent running
# from there may still be the old one. The file only matches once the old agent has exited.
function Get-NewAgent {
    $agentProcess = Get-Running $agent | Select-Object -First 1
    if ($agentProcess -and (-not $expected -or (Get-Sha256 $agent) -eq $expected)) { $agentProcess }
}
for ($waited = 0; $waited -lt 30000 -and -not ($running = Get-NewAgent); $waited += 250) {
    Start-Sleep -Milliseconds 250
}
if (-not $running) {
    $other = Get-Process mx-agent -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($other) {
        throw ("The window connected to the agent running from $($other.Path) instead. Stop it with " +
               "`"$($other.Path)`" --stop and start MX Studio again.")
    }
    if (Get-Running $agent) { throw "The agent at $agent is still the old one after 30 seconds." }
    throw "The agent did not start from $agent within 30 seconds."
}
$window = Get-Running $Destination | Select-Object -First 1
Write-Host "Deployed: MX Studio is open from $Destination (process $($window.Id)), its agent running from $agent."
