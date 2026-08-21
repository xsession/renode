#!/usr/bin/env bash

set -euo pipefail

IMAGE_TAG="${IMAGE_TAG:-renode-custom-cores:offline-linux}"
DOCKER_NETWORK="${DOCKER_NETWORK:-none}"
PLATFORM_ARGS=()
if [[ -n "${DOCKER_PLATFORM:-}" ]]; then
    PLATFORM_ARGS+=(--platform "$DOCKER_PLATFORM")
fi
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

docker run --rm \
    "${PLATFORM_ARGS[@]}" \
    --network "$DOCKER_NETWORK" \
    --entrypoint /usr/local/bin/package-renode \
    -e RENODE_BUILD_HOST_ARCH="${RENODE_BUILD_HOST_ARCH:-}" \
    -e RENODE_PACKAGE_SKIP_FETCH="${RENODE_PACKAGE_SKIP_FETCH:-true}" \
    -v "$ROOT_DIR:/workspace" \
    -w /workspace \
    "$IMAGE_TAG"
