$ErrorActionPreference = "Stop"

$ImageTag = if ($args.Count -ge 1) { $args[0] } else { "renode-custom-cores:offline-windows" }
$Output = if ($args.Count -ge 2) { $args[1] } else { "renode-custom-cores-offline-windows.tar" }

docker save $ImageTag -o $Output
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Saved $ImageTag to $Output"
