<#
.SYNOPSIS
    Safely preview or publish custom-core changes across Renode submodules.

.DESCRIPTION
    Preview is the default. The script never rewrites remotes. Use -Apply to
    create local commits and -Apply -Push to publish the selected branch.
#>
param(
    [string]$BranchName = "custom-cores",
    [string]$RepositoryRoot = $PSScriptRoot,
    [switch]$Apply,
    [switch]$Push
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    param(
        [string]$WorkDir,
        [string[]]$Arguments,
        [switch]$AllowFailure
    )
    Write-Host ("  > git -C {0} {1}" -f $WorkDir, ($Arguments -join " ")) -ForegroundColor Cyan
    if (-not $Apply -and $Arguments[0] -in @("switch", "add", "commit", "push")) {
        return ""
    }
    $output = & git -C $WorkDir @Arguments 2>&1
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        throw ($output -join [Environment]::NewLine)
    }
    return $output
}

function Ensure-Branch {
    param([string]$WorkDir)
    $branches = Invoke-Git $WorkDir @("branch", "--list", $BranchName)
    if ($branches) {
        Invoke-Git $WorkDir @("switch", $BranchName) | Out-Null
    } else {
        Invoke-Git $WorkDir @("switch", "-c", $BranchName) | Out-Null
    }
}

function Commit-If-Dirty {
    param(
        [string]$WorkDir,
        [string]$Message
    )
    $status = Invoke-Git $WorkDir @("status", "--short")
    if (-not $status) {
        Write-Host "  clean: $WorkDir" -ForegroundColor DarkGray
        return
    }
    Write-Host ($status -join [Environment]::NewLine)
    Ensure-Branch $WorkDir
    Invoke-Git $WorkDir @("add", "-A") | Out-Null
    Invoke-Git $WorkDir @("commit", "-m", $Message) -AllowFailure | Out-Null
    if ($Push) {
        Invoke-Git $WorkDir @("push", "-u", "origin", $BranchName) | Out-Null
    }
}

$repoRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$tlibDir = Join-Path $repoRoot "src\Infrastructure\src\Emulator\Cores\tlib"
$infraDir = Join-Path $repoRoot "src\Infrastructure"

Write-Host "Mode: $(if ($Apply) { 'APPLY' } else { 'PREVIEW' })"
if ($Push -and -not $Apply) {
    throw "-Push requires -Apply"
}

Commit-If-Dirty $tlibDir "feat: add custom CPU architectures"
Commit-If-Dirty $infraDir "feat: add custom CPU cores, peripherals, and platforms"
Commit-If-Dirty $repoRoot "feat: add custom CPU architectures and platform manifests"

Write-Host "Done."
