$ErrorActionPreference = "Stop"

$ImageTag = if ($args.Count -ge 1) { $args[0] } else { "renode-custom-cores:offline-windows" }
$DockerPlatform = if ($env:DOCKER_PLATFORM) { $env:DOCKER_PLATFORM } else { "windows/amd64" }
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Resolve-Path (Join-Path $ScriptDir "../..")

docker build `
    --platform $DockerPlatform `
    -f (Join-Path $RootDir "docker/custom-cores/Dockerfile.windows") `
    -t $ImageTag `
    (Join-Path $RootDir "docker/custom-cores")
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Built $ImageTag for $DockerPlatform"
