$ErrorActionPreference = "Stop"

$Workspace = if ($env:RENODE_WORKDIR) { $env:RENODE_WORKDIR } else { "C:\workspace" }
$BuildHostArch = if ($env:RENODE_BUILD_HOST_ARCH) { $env:RENODE_BUILD_HOST_ARCH } else { "i386" }

Set-Location $Workspace

$bashWorkspace = $Workspace -replace "\\", "/"
if ($bashWorkspace -match "^([A-Za-z]):/(.*)$") {
    $bashWorkspace = "/" + $Matches[1].ToLowerInvariant() + "/" + $Matches[2]
}

$skipFetch = if ($env:RENODE_PACKAGE_SKIP_FETCH -eq "false") { "" } else { "--skip-fetch" }
$command = "cd '$bashWorkspace' && ./build.sh -t $skipFetch --host-arch '$BuildHostArch'"
& C:\tools\msys64\usr\bin\bash.exe -lc $command
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force -Path output\deploy | Out-Null
Get-ChildItem output\packages -Filter "*.setup.exe" | Copy-Item -Destination output\deploy -Verbose
Get-ChildItem output\deploy -Filter "*.setup.exe"
