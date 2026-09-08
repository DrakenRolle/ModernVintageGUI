<#
.SYNOPSIS
Starts Vintage Story with the mod, opens the world in the Saves folder, runs a step script in it
- open the showcase, take pictures - and closes the game again.

.DESCRIPTION
The in-game half lives in ModernVintageGUI/Automation/AutoScreenshot.cs. This script builds the
mod, starts the game with --openWorld and the environment variables that half reads
(MVGUI_AUTOSHOT = the step script, MVGUI_AUTOSHOT_OUT = the output directory, MVGUI_AUTOSHOT_SKIP =
sections of the script to leave out), and waits for the done file the run writes when it is
through. The pictures are full window captures, so dropdown
lists and context menus - which draw outside the dialog they belong to - are in them.

Step reference: see steps\showcase.txt or the header of AutoScreenshot.cs.

.PARAMETER Steps
The step script. Default: steps\showcase.txt next to this file.

.PARAMETER Out
Where the pictures, the done file and the run log go. Default: out\ next to this file.

.PARAMETER World
Name of the world to open, without .vcdbs. Default: the one world in the Saves folder; with
more than one this has to be given.

.PARAMETER Configuration
Build configuration of the mod. Default: Debug.

.PARAMETER TimeoutSec
How long to wait for the script to finish before the game is killed. Default: 300.

.PARAMETER NoBuild
Use the mod as it is in bin\ instead of building it first.

.PARAMETER Skip
Names of sections of the step script to leave out, as in "-Skip pictures" for a scene that is
only built to be walked around in. A section is what a "section NAME" line in the script starts.

.PARAMETER KeepOpen
Leave the game running after the script - for looking at what the pictures show. Without this
a "quit" is appended to scripts that do not end with one. Steps written "onquit STEP" run only
when the script ends with a quit, so a scene's teardown can be marked that way and the scene
stays standing in a game that is kept open.

.EXAMPLE
.\ingame-screenshot.ps1

.EXAMPLE
.\ingame-screenshot.ps1 -Steps .\steps\showcase.txt -Out ..\..\docs\ingame -KeepOpen

.EXAMPLE
.\ingame-screenshot.ps1 -Steps ..\CopperCasing\docs\screenshots\coppercasing.txt -Out ..\CopperCasing\docs\images\ingame -NoBuild -KeepOpen
What the "Test world (keep open)" launch profile of the CopperCasing project runs, after Visual
Studio has built both mods.

.EXAMPLE
.\ingame-screenshot.ps1 -Steps ..\CopperCasing\docs\screenshots\coppercasing.txt -Out ..\CopperCasing\docs\images\ingame -NoBuild -KeepOpen -Skip pictures
The "Test world (walk around)" profile: the scene is built and the game left open on it, without
the pictures in between.
#>
[CmdletBinding()]
param(
    [string]$Steps = (Join-Path $PSScriptRoot 'steps\showcase.txt'),
    [string]$Out = (Join-Path $PSScriptRoot 'out'),
    [string]$World,
    [string]$Configuration = 'Debug',
    [int]$TimeoutSec = 300,
    [string[]]$Skip = @(),
    [switch]$NoBuild,
    [switch]$KeepOpen
)

$ErrorActionPreference = 'Stop'

$dataDir = Join-Path $env:APPDATA 'VintagestoryData'
$savesDir = Join-Path $dataDir 'Saves'
$gameLog = Join-Path $dataDir 'Logs\client-main.log'

$Out = [System.IO.Path]::GetFullPath($Out)
$doneFile = Join-Path $Out 'autoshot.done'
$runLog = Join-Path $Out 'autoshot.log'

function Show-RunLog {
    if (Test-Path $runLog) {
        Write-Host ''
        Write-Host '--- run log ---'
        Get-Content -Path $runLog | ForEach-Object { Write-Host "  $_" }
    }
    elseif (Test-Path $gameLog) {
        Write-Host ''
        Write-Host '--- client-main.log (autoshot and errors) ---'
        Select-String -Path $gameLog -Pattern 'autoshot|\[Error\]|\[Fatal\]|Exception' |
            Select-Object -Last 40 |
            ForEach-Object { Write-Host "  $($_.Line)" }
    }
}

function Fail([string]$message) {
    Write-Host ''
    Write-Host "FAILED: $message" -ForegroundColor Red
    exit 1
}

# --- where things are -------------------------------------------------------------------------

$vs = $env:VINTAGE_STORY
if (-not $vs) { Fail 'VINTAGE_STORY is not set - it has to point at the game folder, the one with Vintagestory.exe in it.' }
$exe = Join-Path $vs 'Vintagestory.exe'
if (-not (Test-Path $exe)) { Fail "no Vintagestory.exe in $vs" }

if (-not $World) {
    $worlds = @(Get-ChildItem -Path $savesDir -Filter '*.vcdbs' -File -ErrorAction SilentlyContinue)
    if ($worlds.Count -eq 0) { Fail "no world in $savesDir" }
    if ($worlds.Count -gt 1) { Fail "more than one world in $savesDir - pass -World with the one to open: $(($worlds | ForEach-Object { $_.BaseName }) -join ', ')" }
    $World = $worlds[0].BaseName
}
elseif (-not (Test-Path (Join-Path $savesDir "$World.vcdbs"))) {
    Fail "no world named '$World' in $savesDir"
}

$modProject = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\ModernVintageGUI\ModernVintageGUI.csproj'))
$modDir = Split-Path -Parent $modProject
$modPath = Join-Path $modDir "bin\$Configuration\Mods"
$assets = Join-Path $modDir 'assets'

if (-not (Test-Path $Steps)) { Fail "step script not found: $Steps" }
$Steps = (Resolve-Path $Steps).Path

# --- build ------------------------------------------------------------------------------------

if (-not $NoBuild) {
    Write-Host "Building $modProject ($Configuration)..."
    & dotnet build $modProject -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail 'the mod did not build' }
}
if (-not (Test-Path (Join-Path $modPath 'mod\ModernVintageGUI.dll'))) {
    Fail "no built mod in $modPath - build it, or drop -NoBuild"
}

# --- the script the game will run -------------------------------------------------------------

# A copy, so a quit can be appended without touching the original.
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$stepText = Get-Content -Path $Steps -Raw
if (-not $KeepOpen -and ($stepText -notmatch '(?m)^\s*quit\s*(#.*)?$')) {
    $stepText = $stepText.TrimEnd() + "`r`nquit`r`n"
}
$runSteps = Join-Path $Out 'autoshot.steps'
Set-Content -Path $runSteps -Value $stepText -Encoding utf8
Remove-Item -Path $doneFile, $runLog -Force -ErrorAction SilentlyContinue

# --- start the game ---------------------------------------------------------------------------

Write-Host "World:  $World"
Write-Host "Steps:  $Steps"
if ($Skip.Count -gt 0) { Write-Host "Skip:   $($Skip -join ', ')" }
Write-Host "Output: $Out"

$started = Get-Date
$env:MVGUI_AUTOSHOT = $runSteps
$env:MVGUI_AUTOSHOT_OUT = $Out
if ($Skip.Count -gt 0) { $env:MVGUI_AUTOSHOT_SKIP = ($Skip -join ',') }
try {
    $arguments = @(
        '--openWorld', ('"{0}"' -f $World),
        '--addModPath', ('"{0}"' -f $modPath),
        '--addOrigin', ('"{0}"' -f $assets)
    )
    $game = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $vs -PassThru
}
finally {
    Remove-Item -Path Env:\MVGUI_AUTOSHOT, Env:\MVGUI_AUTOSHOT_OUT, Env:\MVGUI_AUTOSHOT_SKIP -ErrorAction SilentlyContinue
}
Write-Host "Game started (pid $($game.Id)), waiting up to $TimeoutSec s for the script to finish..."

# --- wait for the done file -------------------------------------------------------------------

$deadline = (Get-Date).AddSeconds($TimeoutSec)
while (-not (Test-Path $doneFile)) {
    if ($game.HasExited) {
        Show-RunLog
        Fail "the game exited (code $($game.ExitCode)) before the script finished"
    }
    if ((Get-Date) -gt $deadline) {
        Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
        Show-RunLog
        Fail "no result after $TimeoutSec s - killed the game"
    }
    Start-Sleep -Milliseconds 500
}
$status = (Get-Content -Path $doneFile -Raw).Trim()

# --- let it close -----------------------------------------------------------------------------

# Leaving the world saves it; the game closes itself once the server has stopped.
if (-not $KeepOpen) {
    Write-Host 'Script finished, waiting for the game to save and close...'
    if (-not $game.WaitForExit(120000)) {
        Write-Warning 'the game did not close on its own within two minutes - killing it'
        Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
    }
}

Show-RunLog
Write-Host ''

if ($status -ne 'ok') { Fail $status }

$shots = @(Get-ChildItem -Path $Out -Filter '*.png' -File | Where-Object { $_.LastWriteTime -ge $started } | Sort-Object Name)
Write-Host "OK - $($shots.Count) screenshot(s):" -ForegroundColor Green
$shots | ForEach-Object { Write-Host "  $($_.FullName)" }
exit 0
