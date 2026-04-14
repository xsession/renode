#!/bin/bash
# Run a single Renode test script headlessly
SCRIPT="${1:-test_stm8.resc}"
cd /mnt/c/GIT/renode
timeout 15 ./output/bin/Release/Renode \
  --disable-xwt \
  --console \
  -e "include @tests/custom-cores/$SCRIPT" \
  > /mnt/c/GIT/renode/tests/custom-cores/test_output.log 2>&1
RC=$?
echo "EXIT CODE: $RC"
cat /mnt/c/GIT/renode/tests/custom-cores/test_output.log
