#!/bin/bash
SO_DIR="/mnt/c/GIT/renode/src/Infrastructure/src/Emulator/Cores/obj/Release"
for arch in stm8 pic16; do
    SO="$SO_DIR/$arch/le/tlib/translate-$arch-le.so"
    echo "=== $arch ==="
    nm -D "$SO" | grep renode_external_attach | awk '{print $3}' | sort
    echo ""
done
