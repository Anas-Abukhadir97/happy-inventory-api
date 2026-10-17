"""Run the tested immutable release pair from releases/phase-6-tested.json."""

import json
import os
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]


def image_environment() -> dict[str, str]:
    manifest = json.loads((ROOT / "releases" / "phase-6-tested.json").read_text())
    if manifest.get("schemaVersion") != 1:
        raise ValueError("Unsupported release manifest")
    result = os.environ.copy()
    for component, repository in (("api", "happy-inventory-api"), ("web", "happy-inventory-web")):
        item = manifest[component]
        image = item["image"]
        pattern = rf"ghcr\.io/anas-abukhadir97/{repository}@sha256:[0-9a-f]{{64}}"
        if not re.fullmatch(pattern, image) or not re.fullmatch(r"[0-9a-f]{40}", item["sourceCommit"]):
            raise ValueError(f"Invalid {component} image or source commit in release manifest")
        if item["repository"] != f"Anas-Abukhadir97/{repository}":
            raise ValueError(f"Invalid {component} repository in release manifest")
        result[f"{component.upper()}_IMAGE"] = image
    result["INVENTORY_COMPOSE_FILE"] = "compose.release.yaml"
    return result


def main() -> None:
    args = sys.argv[1:]
    if not args:
        raise SystemExit("Usage: python scripts/release_pair.py up -d | backup [path] | restore <path> | down")
    environment = image_environment()
    if args[0] in ("backup", "restore"):
        command = [sys.executable, "scripts/sqlite_volume.py", *args]
    else:
        command = ["docker", "compose", "-f", "compose.release.yaml", *args]
    subprocess.run(command, cwd=ROOT, env=environment, check=True)


if __name__ == "__main__":
    main()
