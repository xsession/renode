# ============================================================================
# FW-22-43-00 (PIC12F629) — simulation + signal analysis + time-domain viz
# ----------------------------------------------------------------------------
# Requires: Renode built from the sibling custom-cores checkout (../renode):
#   * platforms/cpus/microchip/pic12f629.repl  (CPU.PIC12 core + WDT model)
#   * built-in Pic12Trace engine
#     (src/Infrastructure/src/Emulator/Cores/PIC12/Pic12Trace.cs)
#
# Run from the renode checkout root:
#   renode -s fw2243_trace.rsh      (or: ./run.sh -s fw2243_trace.rsh)
#
# What it does:
#   1. loads the recovered firmware image (fw-22-43-00.bin, generated from
#      ../FW-22-43-00.hex by sim/hex2bin.py)
#   2. starts the built-in trace: 1 sample/us for the first 2 ms (full
#      instruction resolution), then 1 sample/20 us over the whole window
#   3. runs 5.12 virtual seconds = 5 WDT periods (1.024 s each)
#   4. stops the trace and renders (in the working directory):
#        fw2243_trace/trace_detail.csv   (1 us, first 2 ms)
#        fw2243_trace/trace_coarse.csv   (20 us, full window)
#        fw2243_trace/resets.csv         (WDT reset events)
#        fw2243_trace/stats.json         (loop, WDT, histograms, per period)
#        fw2243_viz.png                  (time plots: WDT sawtooth, W
#                                         periodic signal, PC zoom,
#                                         instruction mix, per-period table)
#
# Expected behavior (cross-checked against the cycle-accurate model in
# FW-22-43-00_RE/sim/): boot loop 0x0000..0x0045, 11 instructions = 11 us
# (90.909 kHz), W=0x38 for 3 us of every loop (27.3 % duty), no CLRWDT on
# the reachable path -> WDT resets the machine every 1.024 s, RAM retained
# (GPR 0x08 = 0x38 survives), PCON = 0xF8 after each WDT reset.
# ============================================================================
using sysbus
mach create "fw-22-43-00"
machine LoadPlatformDescription @platforms/cpus/microchip/pic12f629.repl
sysbus LoadBinary @../FW-22-43-00_RE/fw-22-43-00.bin 0x000000
cpu PC 0x000000
log ">>> FW-22-43-00 (PIC12F629): starting - boot loop, no CLRWDT on reachable path <<<"
python "from Antmicro.Renode.Peripherals.CPU import PIC12; core = monitor.Machine.GetPeripheralsOfType(type(PIC12)).First(); core.Trace.Start(2000, 1, 20)"
emulation RunFor "5.12"
python "core.Trace.Stop(); core.Trace.PrintStats(); core.Trace.WriteCsv('fw2243_trace'); core.Trace.WritePng('fw2243_viz.png')"
log ">>> done: fw2243_trace/ + fw2243_viz.png in the working directory <<<"