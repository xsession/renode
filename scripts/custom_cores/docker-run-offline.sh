#!/usr/bin/env bash

set -euo pipefail

IMAGE_TAG="${IMAGE_TAG:-renode-custom-cores:offline}"
PLATFORM_ARGS=()
if [[ -n "${DOCKER_PLATFORM:-}" ]]; then
    PLATFORM_ARGS+=(--platform "$DOCKER_PLATFORM")
fi
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

docker run --rm \
    "${PLATFORM_ARGS[@]}" \
    --network none \
    -e RENODE_CUSTOM_CORE_ARCHES="${RENODE_CUSTOM_CORE_ARCHES:-}" \
    -e RENODE_BUILD_HOST_ARCH="${RENODE_BUILD_HOST_ARCH:-}" \
    -v "$ROOT_DIR:/workspace" \
    -w /workspace \
    "$IMAGE_TAG"
