#!/usr/bin/env python3
"""Audit a Renode custom-cores checkout for common integration gaps."""
from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import asdict, dataclass
from pathlib import Path


CUSTOM_ARCHES = ("avr", "stm8", "mcs51", "pic16", "pic18", "c2000", "dspic33")


@dataclass
class Finding:
    severity: str
    code: str
    path: str
    message: str


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def audit(repo: Path) -> list[Finding]:
    findings: list[Finding] = []
    cores = repo / "src" / "Infrastructure" / "src" / "Emulator" / "Cores"
    tlib_arch = cores / "tlib" / "arch"

    for arch in CUSTOM_ARCHES:
        if not (tlib_arch / arch).exists():
            findings.append(Finding("error", "missing-tlib-arch", str(tlib_arch / arch), f"Missing tlib arch directory for {arch}"))

    repl_root = repo / "platforms"
    backup_root = repo / "_backup_peripherals"
    class_refs: dict[str, Path] = {}
    if repl_root.exists():
        for repl in repl_root.rglob("*.repl"):
            for match in re.finditer(r":\s+([A-Za-z0-9_.]+)\s+@", read_text(repl)):
                class_name = match.group(1).split(".")[-1]
                if class_name:
                    class_refs.setdefault(class_name, repl)

    source_roots = [
        repo / "src" / "Infrastructure" / "src" / "Emulator" / "Peripherals" / "Peripherals",
        cores,
    ]
    source_classes = {path.stem for root in source_roots if root.exists() for path in root.rglob("*.cs")}
    backup_classes = {path.stem for path in backup_root.rglob("*.cs")} if backup_root.exists() else set()

    for class_name, repl in sorted(class_refs.items()):
        if class_name in {"MappedMemory", "ArrayMemory"}:
            continue
        if class_name not in source_classes and class_name in backup_classes:
            findings.append(Finding("warning", "backup-only-model", str(repl), f"{class_name} is referenced by REPL but exists only under _backup_peripherals"))

    hardcoded = re.compile(r"\b[A-Za-z]:\\GIT\\renode\b", re.IGNORECASE)
    for path in [*repo.glob("*.py"), *repo.glob("*.ps1"), *repo.rglob("tools/custom_cores_audit/*.py")]:
        if hardcoded.search(read_text(path)):
            findings.append(Finding("error", "hardcoded-path", str(path.relative_to(repo)), "Contains a machine-specific C:\\GIT\\renode path"))

    for submodule in ("src/Infrastructure", "src/Infrastructure/src/Emulator/Cores/tlib"):
        if not (repo / submodule / ".git").exists():
            findings.append(Finding("warning", "submodule-not-initialized", submodule, "Submodule checkout is missing .git metadata"))

    return findings


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repository", nargs="?", type=Path, default=Path.cwd())
    parser.add_argument("--json", action="store_true")
    parser.add_argument("--fail-on", choices=("never", "warning", "error"), default="error")
    args = parser.parse_args(argv)
    repo = args.repository.resolve()
    findings = audit(repo)
    if args.json:
        print(json.dumps([asdict(finding) for finding in findings], indent=2))
    else:
        for finding in findings:
            print(f"{finding.severity.upper()} {finding.code}: {finding.path}: {finding.message}")
        print(f"Findings: {len(findings)}")
    if args.fail_on == "never":
        return 0
    if args.fail_on == "warning" and findings:
        return 1
    if args.fail_on == "error" and any(f.severity == "error" for f in findings):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
