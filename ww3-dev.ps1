# WW3MOD Development Helper Script
# Usage:
#   ./ww3-dev.ps1 build        - Build only
#   ./ww3-dev.ps1 run          - Build and launch game
#   ./ww3-dev.ps1 test         - Run NUnit tests
#   ./ww3-dev.ps1 check        - Pre-flight checks (no build)
#   ./ww3-dev.ps1 clean-debug  - Strip Game.Debug/Log.Write lines from uncommitted changes
#   ./ww3-dev.ps1 log          - Open latest debug log

param(
    [Parameter(Position=0)]
    [ValidateSet("build", "run", "test", "check", "clean-debug", "log")]
    [string]$Command = "build"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Check-GameRunning {
    $procs = Get-Process -Name "OpenRA" -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "WARNING: OpenRA is running (PID: $($procs.Id -join ', ')). Build will fail." -ForegroundColor Yellow
        Write-Host "Close the game first, or press Enter to try anyway." -ForegroundColor Yellow
        Read-Host
    }
}

function Check-ConsoleWriteLine {
    Write-Host "`n--- Checking for Console.WriteLine in changed files ---" -ForegroundColor Cyan
    $changedFiles = git diff --name-only HEAD 2>$null
    $stagedFiles = git diff --name-only --cached 2>$null
    $untrackedFiles = git ls-files --others --exclude-standard 2>$null
    $allFiles = ($changedFiles + $stagedFiles + $untrackedFiles) | Where-Object { $_ -match '\.cs$' } | Sort-Object -Unique

    $found = $false
    foreach ($file in $allFiles) {
        $fullPath = Join-Path $root $file
        if (Test-Path $fullPath) {
            $matches = Select-String -Path $fullPath -Pattern "Console\.WriteLine" -AllMatches
            foreach ($match in $matches) {
                Write-Host "  FOUND: $($file):$($match.LineNumber): $($match.Line.Trim())" -ForegroundColor Red
                $found = $true
            }
        }
    }

    if (-not $found) {
        Write-Host "  OK: No Console.WriteLine found in changed files." -ForegroundColor Green
    }
    return $found
}

function Check-TestValues {
    Write-Host "`n--- Checking for test values (Cost: 1) in YAML ---" -ForegroundColor Cyan
    $yamlFiles = git diff --name-only HEAD 2>$null | Where-Object { $_ -match '\.yaml$' }

    $found = $false
    foreach ($file in $yamlFiles) {
        $fullPath = Join-Path $root $file
        if (Test-Path $fullPath) {
            $matches = Select-String -Path $fullPath -Pattern "^\s+Cost:\s+1\s*$" -AllMatches
            foreach ($match in $matches) {
                Write-Host "  SUSPICIOUS: $($file):$($match.LineNumber): $($match.Line.Trim())" -ForegroundColor Yellow
                $found = $true
            }
        }
    }

    if (-not $found) {
        Write-Host "  OK: No suspicious Cost: 1 values found." -ForegroundColor Green
    }
    return $found
}

function Do-Build {
    Check-GameRunning
    Write-Host "`n--- Building WW3MOD ---" -ForegroundColor Cyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$root\make.ps1" all
    if ($LASTEXITCODE -ne 0) {
        Write-Host "BUILD FAILED" -ForegroundColor Red
        exit 1
    }
    Write-Host "BUILD SUCCEEDED" -ForegroundColor Green
}

function Do-Run {
    Do-Build
    Write-Host "`n--- Launching WW3MOD ---" -ForegroundColor Cyan
    & "$root\launch-game.cmd"
}

function Do-Test {
    Write-Host "`n--- Running NUnit Tests ---" -ForegroundColor Cyan
    & dotnet test "$root\engine\OpenRA.Test\OpenRA.Test.csproj" --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Host "TESTS FAILED" -ForegroundColor Red
        exit 1
    }
    Write-Host "ALL TESTS PASSED" -ForegroundColor Green
}

function Do-Check {
    Write-Host "=== WW3MOD Pre-Flight Check ===" -ForegroundColor White
    $issues = $false

    $procs = Get-Process -Name "OpenRA" -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "`n  Game is running — close before building." -ForegroundColor Yellow
        $issues = $true
    }

    if (Check-ConsoleWriteLine) { $issues = $true }
    if (Check-TestValues) { $issues = $true }

    if ($issues) {
        Write-Host "`n=== Issues found. Review above. ===" -ForegroundColor Yellow
    } else {
        Write-Host "`n=== All checks passed. ===" -ForegroundColor Green
    }
}

function Do-CleanDebug {
    Write-Host "--- Stripping debug lines from uncommitted changes ---" -ForegroundColor Cyan
    $changedFiles = git diff --name-only HEAD 2>$null
    $allFiles = $changedFiles | Where-Object { $_ -match '\.cs$' }

    foreach ($file in $allFiles) {
        $fullPath = Join-Path $root $file
        if (Test-Path $fullPath) {
            $content = Get-Content $fullPath -Raw
            $original = $content

            # Remove lines containing Game.Debug( that look like temporary debug logs
            # Only removes lines that are SOLELY debug calls (indented, ending with ;)
            $content = $content -replace '(?m)^\s*Game\.Debug\(.*\);\s*\r?\n', ''
            $content = $content -replace '(?m)^\s*Log\.Write\("debug".*\);\s*\r?\n', ''

            if ($content -ne $original) {
                Set-Content -Path $fullPath -Value $content -NoNewline
                Write-Host "  Cleaned: $file" -ForegroundColor Green
            }
        }
    }
    Write-Host "Done. Review changes with 'git diff' before committing." -ForegroundColor Cyan
}

function Do-Log {
    $logDir = Join-Path $env:USERPROFILE "Documents\OpenRA\Logs"
    if (-not (Test-Path $logDir)) {
        Write-Host "Log directory not found: $logDir" -ForegroundColor Red
        return
    }

    $latest = Get-ChildItem $logDir -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) {
        Write-Host "Opening: $($latest.FullName)" -ForegroundColor Cyan
        Get-Content $latest.FullName -Tail 100
    } else {
        Write-Host "No log files found." -ForegroundColor Yellow
    }
}

switch ($Command) {
    "build"       { Do-Check; Do-Build }
    "run"         { Do-Check; Do-Run }
    "test"        { Do-Test }
    "check"       { Do-Check }
    "clean-debug" { Do-CleanDebug }
    "log"         { Do-Log }
}
