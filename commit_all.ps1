<#
.SYNOPSIS
    Commits all custom-core changes across the 3-level submodule hierarchy:
      1. tlib (innermost)          → xsession/tlib.git
      2. renode-infrastructure     → xsession/renode-infrastructure.git
      3. renode (parent)           → xsession/renode.git

.DESCRIPTION
    Must be run from the renode repo root (c:\GIT\renode).
    Commits inside-out so every level records a valid, pushed commit hash.

.PARAMETER BranchName
    Branch name to create in all three repos. Default: custom-cores

.PARAMETER DryRun
    Show what would happen without making changes.
#>
param(
    [string]$BranchName = "custom-cores",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot

function Run {
    param([string]$Cmd, [string]$WorkDir)
    Write-Host "  > $Cmd" -ForegroundColor Cyan
    if (-not $DryRun) {
        Push-Location $WorkDir
        try { Invoke-Expression $Cmd }
        finally { Pop-Location }
    }
}

function GitCheckoutBranch {
    param([string]$Branch, [string]$WorkDir)
    Write-Host "  > git checkout -b $Branch (or switch if exists)" -ForegroundColor Cyan
    if (-not $DryRun) {
        Push-Location $WorkDir
        try {
            $existingBranches = git branch --list $Branch 2>$null
            if ($existingBranches) {
                git checkout $Branch
            } else {
                git checkout -b $Branch
            }
        } finally { Pop-Location }
    }
}

function Separator([string]$Title) {
    Write-Host ""
    Write-Host ("=" * 60) -ForegroundColor Yellow
    Write-Host "  $Title" -ForegroundColor Yellow
    Write-Host ("=" * 60) -ForegroundColor Yellow
}

# ── Paths ──────────────────────────────────────────────────────
$tlibDir   = Join-Path $RepoRoot "src\Infrastructure\src\Emulator\Cores\tlib"
$infraDir  = Join-Path $RepoRoot "src\Infrastructure"
$parentDir = $RepoRoot

# ── Verify forks are reachable ─────────────────────────────────
Separator "Pre-flight checks"
Write-Host "Checking fork remotes..."
$forkUrls = @(
    "https://github.com/xsession/tlib.git",
    "https://github.com/xsession/renode-infrastructure.git",
    "https://github.com/xsession/renode.git"
)
foreach ($url in $forkUrls) {
    $null = git ls-remote --exit-code $url HEAD 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FATAL: Cannot reach $url - fork it first!" -ForegroundColor Red
        exit 1
    }
    Write-Host "  OK: $url" -ForegroundColor Green
}

# ══════════════════════════════════════════════════════════════
# STEP 1 — tlib (innermost submodule)
# ══════════════════════════════════════════════════════════════
Separator "Step 1/3: tlib (innermost)"

# Add your fork as 'origin', keep upstream
Push-Location $tlibDir
$remotes = @(git remote)
$originUrl = if ($remotes -contains "origin") { git remote get-url origin 2>$null } else { $null }
if ($originUrl -eq "https://github.com/xsession/tlib.git") {
    Write-Host "  origin already points to your fork" -ForegroundColor DarkGray
} else {
    if ($originUrl -and -not ($remotes -contains "upstream")) {
        Run "git remote rename origin upstream" $tlibDir
    }
    $remotes = @(git remote)
    if ($remotes -contains "origin") {
        Run "git remote set-url origin https://github.com/xsession/tlib.git" $tlibDir
    } else {
        Run "git remote add origin https://github.com/xsession/tlib.git" $tlibDir
    }
}
Pop-Location

GitCheckoutBranch $BranchName $tlibDir
Run "git add -A" $tlibDir
Run 'git status --short' $tlibDir
Run "git commit -m 'feat: add custom CPU architectures (STM8, MCS51, PIC16, PIC18, C2000, dsPIC33)'" $tlibDir
Run "git push -u origin $BranchName" $tlibDir

# ══════════════════════════════════════════════════════════════
# STEP 2 — renode-infrastructure
# ══════════════════════════════════════════════════════════════
Separator "Step 2/3: renode-infrastructure"

Push-Location $infraDir
$remotes = @(git remote)
$originUrl = if ($remotes -contains "origin") { git remote get-url origin 2>$null } else { $null }
if ($originUrl -eq "https://github.com/xsession/renode-infrastructure.git") {
    Write-Host "  origin already points to your fork" -ForegroundColor DarkGray
} else {
    if ($originUrl -and -not ($remotes -contains "upstream")) {
        Run "git remote rename origin upstream" $infraDir
    }
    $remotes = @(git remote)
    if ($remotes -contains "origin") {
        Run "git remote set-url origin https://github.com/xsession/renode-infrastructure.git" $infraDir
    } else {
        Run "git remote add origin https://github.com/xsession/renode-infrastructure.git" $infraDir
    }
}
Pop-Location

# Point Infrastructure's .gitmodules to your tlib fork
Run "git config --file .gitmodules submodule.src/Emulator/Cores/tlib.url https://github.com/xsession/tlib.git" $infraDir

GitCheckoutBranch $BranchName $infraDir
Run "git add -A" $infraDir
Run 'git status --short' $infraDir
Run "git commit -m 'feat: add custom CPU cores, peripherals, and platform files'" $infraDir
Run "git push -u origin $BranchName" $infraDir

# ══════════════════════════════════════════════════════════════
# STEP 3 — renode (parent)
# ══════════════════════════════════════════════════════════════
Separator "Step 3/3: renode (parent)"

# Point parent .gitmodules to your infrastructure fork
Run "git config --file .gitmodules submodule.src/Infrastructure.url https://github.com/xsession/renode-infrastructure.git" $parentDir

GitCheckoutBranch $BranchName $parentDir
Run "git add -A" $parentDir
Run 'git status --short' $parentDir
Run "git commit -m 'feat: add custom CPU architectures and reorganize platforms'" $parentDir
Run "git push -u origin $BranchName" $parentDir

# ══════════════════════════════════════════════════════════════
Separator "Done!"
Write-Host @"
All three repos committed and pushed to branch '$BranchName':

  tlib:             https://github.com/xsession/tlib/tree/$BranchName
  infrastructure:   https://github.com/xsession/renode-infrastructure/tree/$BranchName
  renode:           https://github.com/xsession/renode/tree/$BranchName

To clone fresh with your forks:
  git clone --recurse-submodules https://github.com/xsession/renode.git -b $BranchName
"@
