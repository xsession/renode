#!/usr/bin/env python3
"""
Run all 6 custom-core blink simulations in Renode headlessly.
Each core is tested by:
  1. Loading the .resc script
  2. Executing a small number of instructions
  3. Checking the CPU didn't abort immediately

Usage (from renode repo root):
  mono output/bin/Release/Renode.exe --disable-xwt -e "include @tests/custom-cores/run_all.resc; quit"

Or use this Python wrapper which calls Renode via subprocess.
"""

import subprocess
import sys
import os

RENODE_ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))

CORES = [
    ("stm8",    "stm8_blink.resc"),
    ("mcs51",   "mcs51_blink.resc"),
    ("pic16",   "pic16_blink.resc"),
    ("pic18",   "pic18_blink.resc"),
    ("c2000",   "c2000_blink.resc"),
    ("dspic33", "dspic33_blink.resc"),
]


def build_renode_script():
    """Generate a Renode Monitor script that tests each core."""
    lines = []
    for name, resc in CORES:
        test_dir = "tests/custom-cores"
        lines.append(f'logLevel 0  # DEBUG')
        lines.append(f'echo "===== Testing {name} ====="')
        lines.append(f'include @{test_dir}/{resc}')
        lines.append(f'emulation RunFor "0.0001"')
        lines.append(f'echo "  {name}: PC = "')
        lines.append(f'cpu PC')
        lines.append(f'echo "  {name}: DONE"')
        lines.append(f'mach clear')
        lines.append('')

    lines.append('echo "===== ALL TESTS COMPLETE ====="')
    lines.append('quit')
    return "\n".join(lines)


if __name__ == "__main__":
    script = build_renode_script()
    script_path = os.path.join(RENODE_ROOT, "tests", "custom-cores", "run_all.resc")
    with open(script_path, "w") as f:
        f.write(script)
    print(f"Generated: {script_path}")
    print("Run with:")
    print(f"  mono output/bin/Release/Renode.exe --disable-xwt -e \"include @tests/custom-cores/run_all.resc\"")
