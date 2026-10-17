"""Consistent backup and stopped-service restore for the Compose SQLite volume."""

import argparse
import datetime as dt
import os
import pathlib
import subprocess
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[1]


def compose(*args: str) -> None:
    subprocess.run(["docker", "compose", "-f", os.environ.get("INVENTORY_COMPOSE_FILE", "compose.yaml"),
                    *args], cwd=ROOT, check=True)


def backup(destination: pathlib.Path) -> None:
    destination = destination.resolve()
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = f"/app/data/backup-{uuid.uuid4().hex}.db"
    try:
        compose("exec", "-T", "api", "sqlite3", "/app/data/inventory.db", f".backup '{temporary}'")
        compose("cp", f"api:{temporary}", str(destination))
    finally:
        compose("exec", "-T", "api", "rm", "-f", temporary)
    print(f"Backup saved: {destination}")


def restore(source: pathlib.Path) -> None:
    source = source.resolve()
    if not source.is_file():
        raise FileNotFoundError(source)
    # Refuse a corrupt or arbitrary file before stopping the live service.
    import sqlite3

    with sqlite3.connect(f"file:{source.as_posix()}?mode=ro", uri=True) as database:
        if database.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
            raise ValueError("Backup failed SQLite integrity_check")
    compose("stop", "api")
    try:
        compose("cp", str(source), "api:/app/data/restore.db")
        compose("run", "--rm", "--no-deps", "--entrypoint", "sh", "api", "-c",
                "cp /app/data/restore.db /app/data/inventory.new "
                "&& test \"$(sqlite3 /app/data/inventory.new 'PRAGMA integrity_check;')\" = ok "
                "&& mv -f /app/data/inventory.new /app/data/inventory.db "
                "&& rm -f /app/data/inventory.db-wal /app/data/inventory.db-shm "
                "/app/data/restore.db")
    finally:
        compose("up", "-d", "api")
    print(f"Restored: {source}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("backup", "restore"))
    parser.add_argument("path", nargs="?", type=pathlib.Path)
    args = parser.parse_args()
    if args.action == "backup":
        default = ROOT / "backups" / f"inventory-{dt.datetime.now(dt.timezone.utc):%Y%m%dT%H%M%SZ}.db"
        backup(args.path or default)
    elif args.path is None:
        parser.error("restore requires a backup path; it replaces the current database")
    else:
        restore(args.path)


if __name__ == "__main__":
    main()
