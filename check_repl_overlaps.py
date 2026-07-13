"""Scan .repl files for MappedMemory/peripheral address overlaps and alignment issues."""
import argparse
import re
import sys
from pathlib import Path

DEFAULT_PLATFORMS_DIR = Path(__file__).resolve().parent / "platforms"

# Architecture -> page size (bytes) based on TARGET_PAGE_BITS
ARCH_PAGE = {
    'cortex-m': 0x400,    # ARM: 10 bits
    'cortex-a': 0x400,
    'cortex-r': 0x400,
    'arm926':   0x400,
    'arm1176':  0x400,
    'arm':      0x400,
    'riscv':    0x1000,   # 12 bits
    'sifive':   0x1000,
    'vexriscv': 0x1000,
    'ri5cy':    0x1000,
    'cv32e40p': 0x1000,
    'c2000':    0x1000,
    'dspic':    0x1000,
    'sparc':    0x1000,
    'leon3':    0x1000,
    'leon4':    0x1000,
    'powerpc':  0x1000,
    'x86':      0x1000,
    'xtensa':   0x1000,
}

def detect_arch(content):
    """Detect architecture from cpu definition in .repl."""
    m = re.search(r'cpuType:\s*"([^"]+)"', content, re.IGNORECASE)
    if m:
        cpu_type = m.group(1).lower()
        for key in ARCH_PAGE:
            if key in cpu_type:
                return key, ARCH_PAGE[key]
    # Check class name
    for pattern, key in [
        (r'CPU\.CortexM\b', 'cortex-m'),
        (r'CPU\.ARMv[78]A\b', 'cortex-a'),
        (r'CPU\.ARMv[78]R\b', 'cortex-r'),
        (r'CPU\.Arm\b', 'arm'),
        (r'CPU\.RiscV\b', 'riscv'),
        (r'CPU\.VexRiscv\b', 'vexriscv'),
        (r'CPU\.CV32E40P\b', 'riscv'),
        (r'CPU\.C2000\b', 'c2000'),
        (r'CPU\.DSPIC\b', 'dspic'),
        (r'CPU\.Sparc\b', 'sparc'),
        (r'CPU\.PowerPc\b', 'powerpc'),
        (r'CPU\.X86\b', 'x86'),
        (r'CPU\.Xtensa\b', 'xtensa'),
    ]:
        if re.search(pattern, content, re.IGNORECASE):
            return key, ARCH_PAGE.get(key, 0x1000)
    return 'unknown', 0x400  # conservative default

def parse_repl_regions(content):
    """Parse all address region definitions from a .repl file."""
    regions = []
    
    # Pattern 1: name: Type @ sysbus <0xADDR, +0xSIZE>
    for m in re.finditer(
        r'^(\w+):\s+\S+\s+@\s+sysbus\s+<\s*0x([0-9A-Fa-f]+)\s*,\s*\+\s*0x([0-9A-Fa-f]+)\s*>',
        content, re.MULTILINE
    ):
        name = m.group(1)
        addr = int(m.group(2), 16)
        size = int(m.group(3), 16)
        regions.append((name, addr, size, m.start()))
    
    # Pattern 2: name: Type @ sysbus 0xADDR (size in next line)
    # Also captures peripherals without explicit size (size=0 means point registration)
    for m in re.finditer(
        r'^(\w+):\s+(\S+)\s+@\s+sysbus\s+0x([0-9A-Fa-f]+)\s*$',
        content, re.MULTILINE
    ):
        name = m.group(1)
        type_name = m.group(2)
        addr = int(m.group(3), 16)
        # Look for size only inside this declaration block. The old script used
        # a fixed character window and could borrow size: from the next region.
        next_decl = re.search(r'\n\w+:\s+\S+\s+@\s+sysbus', content[m.end():])
        block_end = m.end() + next_decl.start() if next_decl else len(content)
        block = content[m.end():block_end]
        sm = re.search(r'^\s*size:\s*0x([0-9A-Fa-f]+)', block, re.MULTILINE)
        size = int(sm.group(1), 16) if sm else 0
        regions.append((name, addr, size, m.start()))
    
    return regions

def check_overlaps(regions):
    """Check for overlapping regions. Returns list of (region1, region2) tuples."""
    issues = []
    for i in range(len(regions)):
        n1, a1, s1, _ = regions[i]
        if s1 == 0:
            continue
        end1 = a1 + s1 - 1
        for j in range(i + 1, len(regions)):
            n2, a2, s2, _ = regions[j]
            if s2 == 0:
                # Point peripheral inside a range
                if a1 <= a2 <= end1:
                    issues.append((regions[i], regions[j], 'peripheral inside MappedMemory'))
                continue
            end2 = a2 + s2 - 1
            # Check overlap  
            if a1 <= end2 and a2 <= end1:
                issues.append((regions[i], regions[j], 'address range overlap'))
    return issues

def check_alignment(regions, page_size, content):
    """Check MappedMemory regions for alignment."""
    issues = []
    for name, addr, size, pos in regions:
        # Find the type
        line_start = content.rfind('\n', 0, pos) + 1
        line = content[line_start:content.find('\n', pos)]
        if 'Memory.MappedMemory' in line or 'Memory.ArrayMemory' in line:
            if addr % page_size != 0:
                issues.append((name, addr, page_size, f'not aligned to 0x{page_size:X}'))
    return issues

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "platforms_dir",
        nargs="?",
        type=Path,
        default=DEFAULT_PLATFORMS_DIR,
        help="Renode platforms directory. Defaults to ./platforms next to this script.",
    )
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    args = parser.parse_args(argv)
    platforms_dir = args.platforms_dir.resolve()
    if not platforms_dir.exists():
        parser.error(f"platforms directory does not exist: {platforms_dir}")

    total_files = 0
    total_issues = 0
    report = []
    
    for repl_file in sorted(platforms_dir.rglob('*.repl')):
        total_files += 1
        content = repl_file.read_text(encoding='utf-8', errors='replace')
        arch, page_size = detect_arch(content)
        regions = parse_repl_regions(content)
        
        overlap_issues = check_overlaps(regions)
        align_issues = check_alignment(regions, page_size, content)
        
        if overlap_issues or align_issues:
            rel_path = repl_file.relative_to(platforms_dir)
            file_report = {"file": str(rel_path), "arch": arch, "page_size": page_size, "issues": []}
            if not args.json:
                print(f"\n{'='*60}")
                print(f"FILE: {rel_path}  (arch={arch}, page=0x{page_size:X})")
                print(f"{'='*60}")
            
            for r1, r2, desc in overlap_issues:
                n1, a1, s1, _ = r1
                n2, a2, s2, _ = r2
                file_report["issues"].append({
                    "type": "overlap",
                    "description": desc,
                    "first": {"name": n1, "address": a1, "size": s1},
                    "second": {"name": n2, "address": a2, "size": s2},
                })
                if not args.json:
                    print(f"  OVERLAP: {n1} (0x{a1:08X}, size=0x{s1:X}) vs {n2} (0x{a2:08X}, size=0x{s2:X}) [{desc}]")
                total_issues += 1
            
            for name, addr, ps, desc in align_issues:
                file_report["issues"].append({
                    "type": "alignment",
                    "description": desc,
                    "region": {"name": name, "address": addr, "page_size": ps},
                })
                if not args.json:
                    print(f"  ALIGN: {name} at 0x{addr:08X} - {desc}")
                total_issues += 1
            report.append(file_report)
    
    if args.json:
        import json

        print(json.dumps({"files_scanned": total_files, "issues_found": total_issues, "files": report}, indent=2))
    else:
        print(f"\n\nSummary: Scanned {total_files} files, found {total_issues} issues")
    return 1 if total_issues else 0

if __name__ == '__main__':
    sys.exit(main())
