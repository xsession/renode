"""Fix systematic .repl bugs in custom platform files.

Verified against actual file contents. Fixes:
1. C2000 timer size 0x10 -> 0x08 (each CPU timer is 8 bytes)
2. C2000 M0/M1 SARAM: merge into single 4KB page-aligned block
3. C2000 SPI sizes 0x18 -> 0x10 (real SPI regs = 14 words)
4. C2000 UART/SCI sizes 0x1C -> 0x10 (real SCI regs = 16 words)
5. C2000 eCAN sizes 0x300 -> 0x200 (only f2812, f28335 at 0x6000/0x6200)
6. C2000 ADC sizes 0xA0 -> 0x80 (real ADC = 128 registers)
7. C2000 eCAP sizes 0x40 -> 0x20 (only files with 0x20-interval eCAPs)
8. C2000 flash overlaps: merge multi-bank into single block
9. C2000 lsram/gsram overlap in f280049c
10. dsPIC flash offset to 0x100000 (Harvard arch separation)
11. dsPIC timer/ADC/GPIO size fixes
12. PIC16/PIC18 port size and SRAM overlap fixes
"""
import re
from pathlib import Path

PLATFORMS = Path(r"c:\GIT\renode\platforms")

def read_file(p):
    return p.read_text(encoding='utf-8')

def write_file(p, content):
    p.write_text(content, encoding='utf-8')

fixes_applied = []

def apply_regex(filepath, pattern, replacement, desc):
    """Apply a regex replacement and log it."""
    content = read_file(filepath)
    new_content = re.sub(pattern, replacement, content)
    if new_content != content:
        write_file(filepath, new_content)
        fixes_applied.append(f"  {filepath.name}: {desc}")
        return True
    return False

def apply_text(filepath, old, new, desc):
    """Apply a text replacement and log it."""
    content = read_file(filepath)
    if old in content:
        content = content.replace(old, new, 1)
        write_file(filepath, content)
        fixes_applied.append(f"  {filepath.name}: {desc}")
        return True
    return False

# ════════════════════════════════════════════════════════════
# 1. C2000 TIMER SIZE: 0x10 -> 0x08
#    All 9 TMS320 files have timer0/1/2 at 8-byte intervals
# ════════════════════════════════════════════════════════════
print("=== 1. C2000 timer sizes ===")
for repl in sorted(PLATFORMS.rglob("tms320*.repl")):
    apply_regex(repl,
        r'(timer\d:\s+Timers\.C2000_CPUTimer\s+@\s+sysbus\s+<0x[0-9A-Fa-f]+,\s+)\+0x10>',
        r'\g<1>+0x08>',
        "timer size 0x10 -> 0x08")

# ════════════════════════════════════════════════════════════
# 2. C2000 M0/M1 SARAM: merge into single page-aligned block
#    C2000 TARGET_PAGE_BITS=12 → 4KB pages → M1 at 0x400 invalid
# ════════════════════════════════════════════════════════════
print("=== 2. C2000 M0/M1 SARAM merge ===")
for repl in sorted(PLATFORMS.rglob("tms320*.repl")):
    content = read_file(repl)
    # Match m0{sa}ram + m1{sa}ram with any sizes (0x400, 0x600, 0x800)
    m = re.search(
        r'(m0(?:sa)?ram):\s+Memory\.MappedMemory\s+@\s+sysbus\s+0x0+\s*\n'
        r'\s+size:\s*(0x[0-9A-Fa-f]+)\s*[^\n]*\n'
        r'(\s*\n)*'  # optional blank lines
        r'(m1(?:sa)?ram):\s+Memory\.MappedMemory\s+@\s+sysbus\s+0x0*400\s*\n'
        r'\s+size:\s*(0x[0-9A-Fa-f]+)\s*[^\n]*',
        content
    )
    if not m:
        # Also try matching the "m0ram" / "m1ram" variant
        m = re.search(
            r'(m0ram):\s+Memory\.MappedMemory\s+@\s+sysbus\s+0x0+\s*\n'
            r'\s+size:\s*(0x[0-9A-Fa-f]+)\s*[^\n]*\n'
            r'(\s*\n)*'
            r'(m1ram):\s+Memory\.MappedMemory\s+@\s+sysbus\s+0x0*400\s*\n'
            r'\s+size:\s*(0x[0-9A-Fa-f]+)\s*[^\n]*',
            content
        )
    if m:
        old_block = m.group(0)
        m0_size = int(m.group(2), 16)
        m1_size = int(m.group(5), 16)
        # Compute merged size: covers 0x000000 to max(m0_end, m1_end)
        merged_end = max(m0_size, 0x400 + m1_size)
        # Round up to 4KB (page) boundary for clean alignment
        merged_size = ((merged_end + 0xFFF) // 0x1000) * 0x1000
        is_sa = 'saram' in old_block.lower()
        name = 'm0m1saram' if is_sa else 'm0m1ram'
        new_block = (
            f"{name}: Memory.MappedMemory @ sysbus 0x000000\n"
            f"    size: 0x{merged_size:06X}    // M0+M1 merged ({merged_size // 1024} KB, page-aligned)"
        )
        content = content.replace(old_block, new_block)
        write_file(repl, content)
        fixes_applied.append(f"  {repl.name}: merged M0+M1 -> single {merged_size:#x} block")

# ════════════════════════════════════════════════════════════
# 3. C2000 SPI size: 0x18 -> 0x10
# ════════════════════════════════════════════════════════════
print("=== 3. C2000 SPI sizes ===")
for repl in sorted(PLATFORMS.rglob("tms320*.repl")):
    apply_regex(repl,
        r'(spi_\w:\s+SPI\.C2000_SPI\s+@\s+sysbus\s+<0x[0-9A-Fa-f]+,\s+)\+0x18>',
        r'\g<1>+0x10>',
        "SPI size 0x18 -> 0x10")

# ════════════════════════════════════════════════════════════
# 4. C2000 UART/SCI size: 0x1C -> 0x10
# ════════════════════════════════════════════════════════════
print("=== 4. C2000 UART/SCI sizes ===")
for repl in sorted(PLATFORMS.rglob("tms320*.repl")):
    apply_regex(repl,
        r'(uart_\w:\s+UART\.C2000_SCI\s+@\s+sysbus\s+<0x[0-9A-Fa-f]+,\s+)\+0x1C>',
        r'\g<1>+0x10>',
        "UART/SCI size 0x1C -> 0x10")

# ════════════════════════════════════════════════════════════
# 5. C2000 eCAN size: 0x300 -> 0x200
# ════════════════════════════════════════════════════════════
print("=== 5. C2000 eCAN sizes ===")
for repl in sorted(PLATFORMS.rglob("tms320*.repl")):
    apply_regex(repl,
        r'((?:ecan|can)_\w:\s+CAN\.C2000_eCAN\s+@\s+sysbus\s+<0x[0-9A-Fa-f]+,\s+)\+0x300>',
        r'\g<1>+0x200>',
        "eCAN size 0x300 -> 0x200")

# ════════════════════════════════════════════════════════════
# 6. C2000 ADC size: 0xA0 -> 0x80
# ════════════════════════════════════════════════════════════
print("=== 6. C2000 ADC sizes ===")
for repl in sorted(PLATFORMS.rglob("tms320*.repl")):
    apply_regex(repl,
        r'(adc(?:_\w)?:\s+Analog\.C2000_ADC12\s+@\s+sysbus\s+<0x[0-9A-Fa-f]+,\s+)\+0xA0>',
        r'\g<1>+0x80>',
        "ADC size 0xA0 -> 0x80")

# ════════════════════════════════════════════════════════════
# 7. C2000 eCAP size: only files with 0x20-interval eCAPs
#    f280025c, f280049c, f28388d, f28p650dk have eCAPs at 0x50xx
#    f28035, f28069, f2812, f28335 have eCAPs at 0x6Axx (0x40 apart = OK)
# ════════════════════════════════════════════════════════════
print("=== 7. C2000 eCAP sizes (0x20-interval only) ===")
for repl in sorted(PLATFORMS.rglob("tms320*.repl")):
    # Only fix eCAPs at 0x005xxx addresses (0x20-interval, overlapping)
    apply_regex(repl,
        r'(ecap\d:\s+Timers\.C2000_eCAP\s+@\s+sysbus\s+<0x005[0-9A-Fa-f]+,\s+)\+0x40>',
        r'\g<1>+0x20>',
        "eCAP size 0x40 -> 0x20 (0x20-interval)")

# ════════════════════════════════════════════════════════════
# 8. C2000 flash bank overlaps: merge into single block
# ════════════════════════════════════════════════════════════
print("=== 8. C2000 flash bank overlap fixes ===")

# tms320f28388d: flash0 (0x80000, 512KB) + flash1 (0xA0000, 512KB) → overlap
# Real: 1.5 MB total flash → single block at 0x80000 size 0x180000
f = PLATFORMS / "cpus" / "ti" / "tms320f28388d.repl"
if f.exists():
    apply_text(f,
        'flash0: Memory.MappedMemory @ sysbus 0x080000\n'
        '    size: 0x080000    // 512 KB (BANK0)\n'
        '\n'
        'flash1: Memory.MappedMemory @ sysbus 0x0A0000\n'
        '    size: 0x080000    // 512 KB (BANK1)',
        '// Flash: 1.5 MB total — merged into single block (avoids bank overlap)\n'
        'flash: Memory.MappedMemory @ sysbus 0x080000\n'
        '    size: 0x180000    // 1.5 MB (BANK0+BANK1+BANK2)',
        "merged flash0+flash1 into single 1.5MB block")

# tms320f28p650dk: 4 banks all overlap → single block
f = PLATFORMS / "cpus" / "ti" / "tms320f28p650dk.repl"
if f.exists():
    apply_text(f,
        'flash0: Memory.MappedMemory @ sysbus 0x080000\n'
        '    size: 0x080000    // Bank 0: 512 KB\n'
        '\n'
        'flash1: Memory.MappedMemory @ sysbus 0x0A0000\n'
        '    size: 0x040000    // Bank 1: 256 KB\n'
        '\n'
        'flash2: Memory.MappedMemory @ sysbus 0x0C0000\n'
        '    size: 0x040000    // Bank 2: 256 KB\n'
        '\n'
        'flash3: Memory.MappedMemory @ sysbus 0x0E0000\n'
        '    size: 0x040000    // Bank 3: 256 KB',
        '// Flash: 1.28 MB total — merged into single block (avoids bank overlap)\n'
        'flash: Memory.MappedMemory @ sysbus 0x080000\n'
        '    size: 0x140000    // 1.28 MB (all banks merged)',
        "merged flash0-flash3 into single 1.28MB block")

# ════════════════════════════════════════════════════════════
# 9. C2000 lsram/gsram overlap in f280049c
#    lsram at 0x8000 size 0x4000 covers gsram at 0xA000
# ════════════════════════════════════════════════════════════
print("=== 9. C2000 lsram/gsram overlap ===")
f = PLATFORMS / "cpus" / "ti" / "tms320f280049c.repl"
if f.exists():
    apply_text(f,
        'lsram: Memory.MappedMemory @ sysbus 0x008000\n'
        '    size: 0x004000    // LS0-LS7: 16 KB',
        'lsram: Memory.MappedMemory @ sysbus 0x008000\n'
        '    size: 0x002000    // LS0-LS3: 8 KB (0x8000-0x9FFF, clear of GS0 at 0xA000)',
        "lsram size 0x4000 -> 0x2000 (avoid gsram overlap)")

# ════════════════════════════════════════════════════════════
# 10. dsPIC: Harvard architecture — flash must not cover data space
# ════════════════════════════════════════════════════════════
print("=== 10. dsPIC flash/RAM Harvard offset ===")

# dspic30f5011: flash at 0x000000 → move to 0x100000
f = PLATFORMS / "cpus" / "microchip" / "dspic30f5011.repl"
if f.exists():
    apply_text(f,
        '// ---- Program Flash (mapped as byte-addressed for the model) ----\n'
        'flash: Memory.MappedMemory @ sysbus 0x000000\n'
        '    size: 0x00AC00    // 66 KB program flash (phantom-byte model)',
        '// ---- Program Flash ----\n'
        '// NOTE: dsPIC30F is Harvard architecture. Program memory mapped at 0x100000\n'
        '// to avoid overlap with data-space SFR/SRAM on the flat sysbus.\n'
        'flash: Memory.MappedMemory @ sysbus 0x100000\n'
        '    size: 0x00AC00    // 66 KB program flash (phantom-byte model)',
        "moved flash to 0x100000 (Harvard arch separation)")

    # RAM at 0x800 → move to 0x1000 for dsPIC33 page alignment (4KB)
    apply_text(f,
        'ram: Memory.MappedMemory @ sysbus 0x000800\n'
        '    size: 0x001000    // 4 KB data RAM',
        'ram: Memory.MappedMemory @ sysbus 0x001000\n'
        '    size: 0x001000    // 4 KB data RAM (page-aligned)',
        "RAM 0x800 -> 0x1000 (page-aligned)")

# dspic33fj128gm802: flash at 0x000000 → move to 0x100000
f = PLATFORMS / "cpus" / "microchip" / "dspic33fj128gm802.repl"
if f.exists():
    apply_text(f,
        '// ---- Program Flash ----\n'
        'flash: Memory.MappedMemory @ sysbus 0x000000\n'
        '    size: 0x016000    // 128 KB (phantom-byte)',
        '// ---- Program Flash ----\n'
        '// NOTE: dsPIC33FJ is Harvard architecture. Program memory at 0x100000\n'
        '// to avoid overlap with data-space SFR/SRAM.\n'
        'flash: Memory.MappedMemory @ sysbus 0x100000\n'
        '    size: 0x016000    // 128 KB (phantom-byte)',
        "moved flash to 0x100000 (Harvard arch)")

    # RAM at 0x800 → move to 0x1000
    apply_text(f,
        'ram: Memory.MappedMemory @ sysbus 0x000800\n'
        '    size: 0x004000    // 16 KB data RAM',
        'ram: Memory.MappedMemory @ sysbus 0x001000\n'
        '    size: 0x004000    // 16 KB data RAM (page-aligned)',
        "RAM 0x800 -> 0x1000 (page-aligned)")

    # GPIO ports: size 0x12 -> 0x08 (TRIS+PORT+LAT+ODC = 4 regs × 2B = 8B)
    apply_regex(f,
        r'(gpio_port[a-z]:\s+GPIOPort\.dsPIC33_GPIO\s+@\s+sysbus\s+<0x[0-9A-Fa-f]+,\s+)\+0x12>',
        r'\g<1>+0x08>',
        "GPIO port size 0x12 -> 0x08")

# ════════════════════════════════════════════════════════════
# 11. dsPIC30F timer and ADC overlap fixes
# ════════════════════════════════════════════════════════════
print("=== 11. dsPIC30F timer/ADC overlaps ===")

f = PLATFORMS / "cpus" / "microchip" / "dspic30f5011.repl"
if f.exists():
    # Timer2-5 overlap each other (at 4-byte intervals with size 0x06)
    # timer2 at 0x106, timer3 at 0x10A, timer4 at 0x110, timer5 at 0x116
    # Reduce to size 0x04 to avoid overlap
    apply_regex(f,
        r'(timer[2-5]:\s+Timers\.dsPIC30_Timer\s+@\s+sysbus\s+<0x000[0-9A-Fa-f]+,\s+)\+0x06>',
        r'\g<1>+0x04>',
        "timer2-5 size 0x06 -> 0x04 (avoid overlap)")

    # ADC at 0x2A0 size 0x30 overlaps gpio_portb at 0x2C6
    # Real ADC regs end at ~0x2BF. Fix: 0x30 -> 0x20
    apply_regex(f,
        r'(adc:\s+Analog\.dsPIC30_ADC\s+@\s+sysbus\s+<0x0002A0,\s+)\+0x30>',
        r'\g<1>+0x20>',
        "ADC size 0x30 -> 0x20 (avoid GPIO overlap)")

# ════════════════════════════════════════════════════════════
# 12. PIC16/PIC18 port and SRAM overlap fixes
# ════════════════════════════════════════════════════════════
print("=== 12. PIC16/PIC18 fixes ===")

# PIC16F877A: SRAM at 0x200000 covers SFR peripherals
f = PLATFORMS / "cpus" / "microchip" / "pic16f877a.repl"
if f.exists():
    apply_text(f,
        '// Data SRAM (general-purpose registers 0x20-0x6F bank0, mirrored)\n'
        'sram: Memory.MappedMemory @ sysbus 0x200000\n'
        '    size: 0x200',
        '// Data SRAM — starts at 0x200020 to avoid overlapping SFR peripherals\n'
        '// (SFRs at 0x200000-0x20001F handled by peripheral models)\n'
        'sram: Memory.MappedMemory @ sysbus 0x200020\n'
        '    size: 0x150     // GPR: 0x20-0x6F per bank (~368 bytes effective)',
        "SRAM moved to 0x200020 (clear of SFR peripherals)")

    # Port sizes: 0x2 -> 0x1 (each PORT is a single byte register)
    apply_regex(f,
        r'(port[a-e]:\s+GPIOPort\.PIC16_GPIO\s+@\s+sysbus\s+<0x[0-9A-Fa-f]+,\s+)\+0x2>',
        r'\g<1>+0x1>',
        "port sizes 0x2 -> 0x1")

# PIC18F4520: ports overlap (size 0x3 at 1-byte intervals)
f = PLATFORMS / "cpus" / "microchip" / "pic18f4520.repl"
if f.exists():
    apply_regex(f,
        r'(port[a-e]:\s+GPIOPort\.PIC18_GPIO\s+@\s+sysbus\s+<0x[0-9A-Fa-f]+,\s+)\+0x3>',
        r'\g<1>+0x1>',
        "port sizes 0x3 -> 0x1")

# ════════════════════════════════════════════════════════════
# 13. F28069 flash alignment: 0x3D7800 not 4KB-aligned
# ════════════════════════════════════════════════════════════
print("=== 13. F28069 flash alignment ===")
f = PLATFORMS / "cpus" / "ti" / "tms320f28069.repl"
if f.exists():
    apply_text(f,
        'flash: Memory.MappedMemory @ sysbus 0x3D7800\n'
        '    size: 0x020800    // 256 KB total OTP + Flash sectors A-H',
        'flash: Memory.MappedMemory @ sysbus 0x3D8000\n'
        '    size: 0x020000    // 128 KB Flash (page-aligned; OTP region omitted)',
        "flash 0x3D7800 -> 0x3D8000 (page-aligned)")

print("\n" + "=" * 60)
print("SUMMARY")
print("=" * 60)
for line in fixes_applied:
    print(line)
print(f"\nTotal fixes applied: {len(fixes_applied)}")
