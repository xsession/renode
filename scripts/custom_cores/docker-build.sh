#!/usr/bin/env bash

set -euo pipefail

IMAGE_TAG="${1:-renode-custom-cores:offline}"
PLATFORMS="${DOCKER_PLATFORMS:-${2:-linux/amd64}}"
OUTPUT="${DOCKER_OUTPUT:-load}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

BUILD_ARGS=(
    buildx build
    --platform "$PLATFORMS"
    -f "$ROOT_DIR/docker/custom-cores/Dockerfile"
    -t "$IMAGE_TAG"
)

case "$OUTPUT" in
    load)
        if [[ "$PLATFORMS" == *,* ]]; then
            echo "DOCKER_OUTPUT=load supports one platform at a time. Use DOCKER_OUTPUT=oci or build per-platform archives."
            exit 1
        fi
        BUILD_ARGS+=(--load)
        ;;
    push)
        BUILD_ARGS+=(--push)
        ;;
    oci)
        OCI_OUTPUT="${DOCKER_OCI_OUTPUT:-renode-custom-cores-oci.tar}"
        BUILD_ARGS+=(--output "type=oci,dest=$OCI_OUTPUT")
        ;;
    *)
        echo "Unsupported DOCKER_OUTPUT '$OUTPUT'. Use load, push, or oci."
        exit 1
        ;;
esac

docker "${BUILD_ARGS[@]}" "$ROOT_DIR/docker/custom-cores"

echo "Built $IMAGE_TAG for $PLATFORMS using DOCKER_OUTPUT=$OUTPUT"
