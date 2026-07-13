from pathlib import Path

from tools.custom_cores_audit.audit_repo import audit


def test_audit_detects_missing_custom_arch(tmp_path: Path):
    repo = tmp_path
    (repo / "src/Infrastructure/src/Emulator/Cores/tlib/arch/avr").mkdir(parents=True)
    (repo / "platforms").mkdir()

    findings = audit(repo)

    assert any(finding.code == "missing-tlib-arch" for finding in findings)
