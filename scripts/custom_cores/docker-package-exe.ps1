$ErrorActionPreference = "Stop"

$ImageTag = if ($env:IMAGE_TAG) { $env:IMAGE_TAG } else { "renode-custom-cores:offline-windows" }
$DockerPlatform = if ($env:DOCKER_PLATFORM) { $env:DOCKER_PLATFORM } else { "windows/amd64" }
$DockerNetwork = if ($env:DOCKER_NETWORK) { $env:DOCKER_NETWORK } else { "none" }
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Resolve-Path (Join-Path $ScriptDir "../..")

docker run --rm `
    --platform $DockerPlatform `
    --network $DockerNetwork `
    --entrypoint powershell `
    -e "RENODE_BUILD_HOST_ARCH=$env:RENODE_BUILD_HOST_ARCH" `
    -e "RENODE_PACKAGE_SKIP_FETCH=$env:RENODE_PACKAGE_SKIP_FETCH" `
    -v "${RootDir}:C:\workspace" `
    -w C:\workspace `
    $ImageTag `
    -NoLogo -NoProfile -ExecutionPolicy Bypass -File C:\package-renode.ps1
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
