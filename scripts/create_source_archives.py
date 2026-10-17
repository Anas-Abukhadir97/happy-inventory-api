"""Create reviewer source ZIPs from clean sibling Git checkouts."""

import datetime as dt
import hashlib
import json
import pathlib
import subprocess

API = pathlib.Path(__file__).resolve().parents[1]
WEB = API.parent / "frontend"
OUTPUT = API.parent / "phase7-release"
REPOSITORIES = (("happy-inventory-api", API), ("happy-inventory-web", WEB))


def git(root: pathlib.Path, *args: str) -> bytes:
    return subprocess.run(["git", *args], cwd=root, check=True, capture_output=True).stdout


def verify_checkout(name: str, root: pathlib.Path) -> str:
    if git(root, "branch", "--show-current").decode().strip() != "main":
        raise ValueError(f"{name} must be on main")
    if git(root, "status", "--porcelain"):
        raise ValueError(f"{name} checkout has uncommitted files")
    tracked = git(root, "ls-files", "-z").decode().split("\0")
    forbidden = (".env", ".db", ".sqlite", ".log", ".pdf", "happy-inventory.html",
                 "pasted text.txt", "happy-inventory-strategy.md")
    for path in tracked:
        if not path:
            continue
        lowered = path.lower()
        if lowered == ".env" or any(lowered.endswith(suffix) for suffix in forbidden[1:]):
            raise ValueError(f"Sensitive or source-assessment file is tracked: {path}")
    return git(root, "rev-parse", "HEAD").decode().strip()


def main() -> None:
    revisions = {name: verify_checkout(name, root) for name, root in REPOSITORIES}
    OUTPUT.mkdir(exist_ok=True)
    archives = []
    for name, root in REPOSITORIES:
        revision = revisions[name]
        destination = OUTPUT / f"{name}-{revision[:12]}-source.zip"
        subprocess.run(["git", "archive", "--format=zip", f"--prefix={name}/",
                        f"--output={destination}", revision], cwd=root, check=True)
        digest = hashlib.sha256(destination.read_bytes()).hexdigest()
        archives.append({"repository": f"Anas-Abukhadir97/{name}", "commit": revision,
                         "file": destination.name, "sizeBytes": destination.stat().st_size,
                         "sha256": digest})
    manifest = {"createdAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(), "archives": archives,
                "note": "Source-only reviewer bundles; Azure URL awaits Phase 8 cost approval."}
    (OUTPUT / "source-archives.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (OUTPUT / "SHA256SUMS.txt").write_text(
        "".join(f"{entry['sha256']}  {entry['file']}\n" for entry in archives), encoding="utf-8")
    print(f"Created two source archives and checksums in {OUTPUT}")


if __name__ == "__main__":
    main()
