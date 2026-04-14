#!/usr/bin/env python3
"""
Generate blink firmware binaries for all 6 custom Renode CPU cores.

Each firmware is a minimal hand-assembled binary that uses ONLY opcodes
verified as implemented in each translate.c.  The binaries run tight
loops loading alternating values into registers (or writing to GPIO
addresses where the translate.c supports memory writes).

GPIO/LED regions are memory-mapped in each .repl file so Renode can
hook a LED model to observe the toggles.
"""

import struct
import os

OUT_DIR = os.path.dirname(os.path.abspath(__file__))


def write_bin(name: str, data: bytes):
    path = os.path.join(OUT_DIR, name)
    with open(path, "wb") as f:
        f.write(data)
    print(f"  {name}: {len(data)} bytes")


# ---------------------------------------------------------------------------
# STM8
# ---------------------------------------------------------------------------
# Byte-addressed, variable-length (1-5 bytes), code starts at 0x000000.
# 0xA6 nn       → LD A, #nn          (2 bytes)  — load imm to A
# 0xB7 nn       → LD shortmem, A     (2 bytes)  — store A to addr8
# 0x20 rel      → JRA rel            (2 bytes)  — branch PC+2+rel
#
# GPIO mapped at short address 0x50 (in .repl we map 0x50 as 1-byte output).
# Blink: LD A,#0xFF → ST 0x50,A → LD A,#0x00 → ST 0x50,A → JRA back
def gen_stm8():
    GPIO = 0x50
    code = bytearray()
    # 0x00: A6 FF        LD A, #0xFF
    code += bytes([0xA6, 0xFF])
    # 0x02: B7 50        LD $50, A
    code += bytes([0xB7, GPIO])
    # 0x04: A6 00        LD A, #0x00
    code += bytes([0xA6, 0x00])
    # 0x06: B7 50        LD $50, A
    code += bytes([0xB7, GPIO])
    # 0x08: 20 F6        JRA -10  → target = 0x0A + (-10) = 0x00
    code += bytes([0x20, 0xF6 & 0xFF])
    write_bin("blink_stm8.bin", bytes(code))


# ---------------------------------------------------------------------------
# MCS-51 (8051)
# ---------------------------------------------------------------------------
# Code space at 0x000000.  SFR space at 0x300000 (mapped by tlib).
# 0x75 dd ii    → MOV direct, #imm   (3 bytes) — addr ≥ 0x80 → SFR write
# 0x80 rel      → SJMP rel           (2 bytes) — target = (pc_after) + rel
#
# Use SFR address 0x90 (Port 1) mapped via .repl at 0x300090.
def gen_mcs51():
    P1 = 0x90  # direct address for SFR P1
    code = bytearray()
    # 0x00: 75 90 FF     MOV P1, #0xFF
    code += bytes([0x75, P1, 0xFF])
    # 0x03: 75 90 00     MOV P1, #0x00
    code += bytes([0x75, P1, 0x00])
    # 0x06: 80 F8        SJMP -8  → target = (0x08) + (-8) = 0x00
    code += bytes([0x80, 0xF8 & 0xFF])
    write_bin("blink_mcs51.bin", bytes(code))


# ---------------------------------------------------------------------------
# PIC16
# ---------------------------------------------------------------------------
# 14-bit instruction words stored as 16-bit LE.  Word-addressed program memory.
# Byte address = word index * 2 in the binary file.
#
# Decode masks from translate.c:
#   MOVLW k   : (insn & 0x3F00) == 0x3000   → k = insn & 0xFF
#   MOVWF f   : (insn & 0x3F80) == 0x0080   → f = insn & 0x7F
#   GOTO k11  : (insn & 0x3800) == 0x2800   → k = insn & 0x7FF
#
# gen_store_freg writes to PIC16_DATA_BASE + f.
# We use file register 0x06 (PORTB).  .repl maps DATA_BASE+0x06 to a LED.
def gen_pic16():
    PORTB = 0x06
    code = bytearray()
    words = [
        0x30FF,           # MOVLW 0xFF
        0x0080 | PORTB,   # MOVWF PORTB  → 0x0086
        0x3000,           # MOVLW 0x00
        0x0080 | PORTB,   # MOVWF PORTB
        0x2800,           # GOTO 0x000   → word address 0
    ]
    for w in words:
        code += struct.pack("<H", w)
    write_bin("blink_pic16.bin", bytes(code))


# ---------------------------------------------------------------------------
# PIC18
# ---------------------------------------------------------------------------
# 16-bit instruction words, byte-addressed.  Some 2-word (32-bit) insns.
#
# Decode masks from translate.c:
#   MOVLW k   : (insn & 0xFF00) == 0x0E00   → k = insn & 0xFF
#   MOVWF f,a : (insn & 0xFE00) == 0x6E00   → f = insn & 0xFF, a = (insn >> 8) & 1
#               a=0 → access bank: f < 0x60 → DATA_BASE+f, else → DATA_BASE+0xF00+f-0x60
#   GOTO addr : (insn & 0xFF00) == 0xEF00, then word2 with 0xF000 prefix
#               target = ((word2 & 0x0FFF) << 8 | (word1 & 0xFF)) << 1
#
# We use access-bank file register 0x10 for GPIO.
# .repl maps PIC18_DATA_BASE+0x10 to a LED.
def gen_pic18():
    GPIO_F = 0x10   # access-bank address, a=0, f < 0x60 → DATA_BASE + f
    code = bytearray()
    words = [
        0x0EFF,                    # MOVLW 0xFF
        0x6E00 | GPIO_F,           # MOVWF f=0x10, a=0 → 0x6E10
        0x0E00,                    # MOVLW 0x00
        0x6E00 | GPIO_F,           # MOVWF f=0x10, a=0
        0xEF00,                    # GOTO 0x000000 — word 1: low byte of target = 0
        0xF000,                    # GOTO word 2: upper bits = 0
    ]
    for w in words:
        code += struct.pack("<H", w)
    write_bin("blink_pic18.bin", bytes(code))


# ---------------------------------------------------------------------------
# C2000 (TMS320C28x)
# ---------------------------------------------------------------------------
# 16-bit instruction words, byte-addressed (2 bytes per word).
# switch(insn >> 8):
#   0x00: NOP (0x0000), LRETR (0x0006), IDLE (0x0183)
#   0x38-0x3B: MOVL ACC, #imm16  (2-word: [opcode_word] [imm16])
#   0x56: B offset  → target = (pc+2) + offset*2
#
# Blink: MOVL ACC #0xFF, NOP, NOP, B back to start.
# No store-to-memory instruction is implemented, so we just toggle ACC.
def gen_c2000():
    code = bytearray()
    # 0x0000: MOVL ACC, #0x00FF  — 2 words (4 bytes)
    code += struct.pack("<H", 0x3800)   # opcode word (upper byte 0x38)
    code += struct.pack("<H", 0x00FF)   # imm16 = 0x00FF
    # 0x0004: MOVL ACC, #0x0000  — 2 words (4 bytes)
    code += struct.pack("<H", 0x3800)
    code += struct.pack("<H", 0x0000)
    # 0x0008: B offset → back to 0x0000
    # target = (pc+2) + offset*2 → 0x0000 = (0x0008+2) + offset*2
    # offset*2 = -10 → offset = -5 → signed byte 0xFB
    code += struct.pack("<H", 0x56FB)
    write_bin("blink_c2000.bin", bytes(code))


# ---------------------------------------------------------------------------
# dsPIC33
# ---------------------------------------------------------------------------
# 24-bit instructions in 32-bit words (LE, phantom byte = 0x00 in MSB).
# PC increments by 4 per instruction.  GOTO is 2-word (8 bytes).
#
# Decode: switch(insn >> 16)  (= bits[23:16])
#   0x00: NOP
#   0xE0-0xE7: MOV #lit16, Wnd  → lit = insn & 0xFFFF, Wnd = (insn>>16) & 0xF
#   0x04: GOTO  → 2-word, target = ((insn&0xFFFF) | ((insn2&0x7F)<<16)) << 1
#   0x78: MOV Wns, Wnd
#
# Blink: MOV #0xFFFF, W0 → MOV #0x0000, W0 → GOTO 0.
def gen_dspic33():

    def w24(val: int) -> bytes:
        """Pack a 24-bit instruction into 32-bit LE (phantom byte = 0)."""
        return struct.pack("<I", val & 0x00FFFFFF)

    code = bytearray()
    # 0x00: MOV #0xFFFF, W0
    # op = 0xE0 (Wnd=0 encoded in opcode byte[3:0]), lit16 = 0xFFFF
    # insn = (0xE0 << 16) | 0xFFFF = 0xE0FFFF
    code += w24(0xE0FFFF)
    # 0x04: MOV #0x0000, W0
    # insn = (0xE0 << 16) | 0x0000 = 0xE00000
    code += w24(0xE00000)
    # 0x08: GOTO 0x000000  (2-word, 8 bytes)
    # word1: op=0x04, target[15:0]=0x0000 → 0x040000
    code += w24(0x040000)
    # word2: target[22:16]=0x00 → 0x000000
    code += w24(0x000000)
    write_bin("blink_dspic33.bin", bytes(code))


if __name__ == "__main__":
    print("Generating blink firmware binaries...")
    gen_stm8()
    gen_mcs51()
    gen_pic16()
    gen_pic18()
    gen_c2000()
    gen_dspic33()
    print("Done!")
