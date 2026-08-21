$ErrorActionPreference = "Stop"

$Workspace = if ($env:RENODE_WORKDIR) { $env:RENODE_WORKDIR } else { "C:\workspace" }
$Arches = if ($env:RENODE_CUSTOM_CORE_ARCHES) { $env:RENODE_CUSTOM_CORE_ARCHES } else { "avr stm8 mcs51 pic16 pic18 c2000 dspic33" }
$FailOn = if ($env:RENODE_CUSTOM_CORES_AUDIT_FAIL_ON) { $env:RENODE_CUSTOM_CORES_AUDIT_FAIL_ON } else { "error" }
$BuildHostArch = if ($env:RENODE_BUILD_HOST_ARCH) { $env:RENODE_BUILD_HOST_ARCH } else { "i386" }

Set-Location $Workspace

$Python = Get-Command python -ErrorAction SilentlyContinue
if (-not $Python) {
    $Python = Get-ChildItem -Path C:\Python*\python.exe | Select-Object -First 1
}
if (-not $Python) {
    throw "Python executable was not found."
}
$PythonPath = if ($Python.Source) { $Python.Source } else { $Python.FullName }

& $PythonPath -m pytest -q tests/custom_cores_audit
& $PythonPath tools/custom_cores_audit/audit_repo.py . --fail-on $FailOn

$bashWorkspace = $Workspace -replace "\\", "/"
if ($bashWorkspace -match "^([A-Za-z]):/(.*)$") {
    $bashWorkspace = "/" + $Matches[1].ToLowerInvariant() + "/" + $Matches[2]
}

foreach ($arch in $Arches.Split(" ", [System.StringSplitOptions]::RemoveEmptyEntries)) {
    Write-Host "Building custom tlib core: $arch"
    $command = "cd '$bashWorkspace' && RENODE_CUSTOM_CORES='$arch.le' ./build.sh --external-lib-only --external-lib-arch '$arch' --tlib-export-compile-commands --skip-fetch --host-arch '$BuildHostArch'"
    & C:\tools\msys64\usr\bin\bash.exe -lc $command
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
