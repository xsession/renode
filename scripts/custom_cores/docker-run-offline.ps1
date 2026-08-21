$ErrorActionPreference = "Stop"

$ImageTag = if ($env:IMAGE_TAG) { $env:IMAGE_TAG } else { "renode-custom-cores:offline-windows" }
$DockerPlatform = if ($env:DOCKER_PLATFORM) { $env:DOCKER_PLATFORM } else { "windows/amd64" }
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Resolve-Path (Join-Path $ScriptDir "../..")

docker run --rm `
    --platform $DockerPlatform `
    --network none `
    -e "RENODE_CUSTOM_CORE_ARCHES=$env:RENODE_CUSTOM_CORE_ARCHES" `
    -e "RENODE_BUILD_HOST_ARCH=$env:RENODE_BUILD_HOST_ARCH" `
    -v "${RootDir}:C:\workspace" `
    -w C:\workspace `
    $ImageTag
