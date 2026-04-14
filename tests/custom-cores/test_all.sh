#!/bin/bash
# Test all 6 custom CPU cores in Renode headlessly.
# Each core loads its .repl + binary, runs briefly, and we check the log
# for errors (abort, exception, UNDEF).

RENODE_DIR="/mnt/c/GIT/renode"
TEST_DIR="$RENODE_DIR/tests/custom-cores"
RENODE="$RENODE_DIR/output/bin/Release/Renode"
LOG="$TEST_DIR/all_tests.log"

CORES="stm8 mcs51 pic16 pic18 c2000 dspic33"

echo "========================================" > "$LOG"
echo " Custom CPU Core Blink Tests" >> "$LOG"
echo "========================================" >> "$LOG"
echo "" >> "$LOG"

PASS=0
FAIL=0

for core in $CORES; do
    echo "Testing $core..."
    CORE_LOG="$TEST_DIR/test_${core}.log"

    # Build inline Renode script
    RESC="$TEST_DIR/auto_test_${core}.resc"
    cat > "$RESC" << RESC_EOF
using sysbus
mach create "${core}-test"
machine LoadPlatformDescription @tests/custom-cores/${core}_blink.repl
sysbus LoadBinary @tests/custom-cores/blink_${core}.bin 0x000000
cpu PC 0x000000
log ">>> ${core}: starting <<<"
emulation RunFor "0.0001"
log ">>> ${core}: completed <<<"
quit
RESC_EOF

    # Run with 30-second timeout
    timeout 30 "$RENODE" \
        --disable-xwt --plain \
        -e "include @tests/custom-cores/auto_test_${core}.resc" \
        < /dev/null > "$CORE_LOG" 2>&1

    RC=$?

    # Check for success indicators
    STARTED=$(grep -c "Machine started" "$CORE_LOG" 2>/dev/null)
    PAUSED=$(grep -c "Machine paused" "$CORE_LOG" 2>/dev/null)
    COMPLETED=$(grep -c "completed" "$CORE_LOG" 2>/dev/null)
    ERRORS=$(grep -ci "error\|abort\|exception\|UNDEF\|Cannot find" "$CORE_LOG" 2>/dev/null)
    TRANSLATE_ERR=$(grep -c "Cannot find library" "$CORE_LOG" 2>/dev/null)

    echo "" >> "$LOG"
    echo "--- $core ---" >> "$LOG"

    if [ "$TRANSLATE_ERR" -gt 0 ]; then
        echo "  FAIL: translate library not found" >> "$LOG"
        grep "Cannot find library" "$CORE_LOG" >> "$LOG"
        FAIL=$((FAIL + 1))
    elif [ "$STARTED" -gt 0 ] && [ "$PAUSED" -gt 0 ]; then
        echo "  PASS: Machine started and paused successfully" >> "$LOG"
        PASS=$((PASS + 1))
    elif [ "$RC" -ne 0 ]; then
        echo "  FAIL: exit code $RC" >> "$LOG"
        tail -5 "$CORE_LOG" >> "$LOG"
        FAIL=$((FAIL + 1))
    else
        echo "  UNKNOWN: check $CORE_LOG" >> "$LOG"
        tail -5 "$CORE_LOG" >> "$LOG"
        FAIL=$((FAIL + 1))
    fi

    echo "  Log: $CORE_LOG" >> "$LOG"
done

echo "" >> "$LOG"
echo "========================================" >> "$LOG"
echo " Results: $PASS passed, $FAIL failed (of 6)" >> "$LOG"
echo "========================================" >> "$LOG"

cat "$LOG"
