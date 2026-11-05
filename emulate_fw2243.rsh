// Emulate the original FW-22-43-00.hex firmware on the PIC12F629 platform.
//
// Run from the renode root:
//   ./run.sh -s emulate_fw2243.rsh
//
// Expected behavior (verified against the annotated disassembly):
//   1. Reset path (0x0000-0x0009, 0x0045): tight loop, no CLRWDT,
//      WDT resets the machine every ~1.024 virtual seconds.
//   2. The dormant main block (0x0046-0x0075) is unreachable from reset;
//      forcing PC there starts the WDT-feeding idle loop (CALL PROC).
//
using @platforms/cpus/microchip/pic12f629.repl

// Program Flash: 1024 14-bit words, little-endian byte pairs
pm load @0x000000 FW-22-43-00.hex

echo ""
echo "=== FW-22-43-00 on PIC12F629: shipped reset-path behavior ==="
sleep 3.5

echo "--- state after ~3.5 virtual seconds ---"
cpu PC
cpu W
pm @0x200008
pm @0x200081
machine GetVirtualTimeMs

echo ""
echo "=== Forcing the dormant main block at 0x0046 (WDT-feeding idle loop) ==="
cpu PC 0x46
sleep 2.5

echo "--- state after 2.5 more virtual seconds ---"
cpu PC
cpu W
pm @0x200092
pm @0x200090
machine GetVirtualTimeMs
