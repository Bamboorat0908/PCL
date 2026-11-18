"""Incremental website route activation with backups and nginx rollback. Python 3.8+."""
import argparse
import datetime
import hashlib
import json
import re
import shutil
import subprocess
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--key", required=True)
    parser.add_argument("--snippet", required=True)
    parser.add_argument("--authorize-only", action="store_true")
    args = parser.parse_args()
    tag = datetime.datetime.utcnow().strftime("%Y%m%dT%H%M%SZ")
    backup = Path("/var/backups/brcl-bank") / tag
    backup.mkdir(parents=True, mode=0o700)
    public = Path(args.key).read_text().strip().split()
    if len(public) < 2 or public[0] != "ssh-ed25519" or not re.fullmatch(r"[A-Za-z0-9+/=]+", public[1]):
        raise ValueError("Invalid tunnel public key")
    key_line = 'restrict,port-forwarding,permitlisten="127.0.0.1:18786",permitopen="127.0.0.1:1",command="/bin/false" ' + public[0] + " " + public[1] + " brcl-bank-tunnel-20261118"
    authorized = Path("/root/.ssh/authorized_keys")
    previous_keys = authorized.read_text() if authorized.exists() else ""
    existing = [line for line in previous_keys.splitlines() if "brcl-bank-tunnel-20261118" in line]
    if existing and existing != [key_line]:
        raise ValueError("A different bank tunnel authorization already exists")
    if not existing:
        if authorized.exists():
            shutil.copy2(str(authorized), str(backup / "authorized_keys"))
        authorized.parent.mkdir(mode=0o700, exist_ok=True)
        authorized.write_text(previous_keys.rstrip("\n") + "\n" + key_line + "\n")
        authorized.chmod(0o600)
    if args.authorize_only:
        print(json.dumps({"authorized": True, "backup": str(backup)}))
        return
    vhost = Path("/etc/nginx/sites-available/mcstatus")
    snippet = Path("/etc/nginx/snippets/brcl-bank.conf")
    rate = Path("/etc/nginx/conf.d/brcl-bank-ratelimit.conf")
    source = vhost.read_text()
    needle = "    include snippets/brcl-site.conf;"
    include = "    include snippets/brcl-bank.conf;"
    if source.count(needle) != 1:
        raise ValueError("Expected one existing HTTPS website include")
    updated = source if include in source else source.replace(needle, needle + "\n" + include, 1)
    originals = {}
    for path in (vhost, snippet, rate):
        originals[path] = path.read_bytes() if path.exists() else None
        if path.exists():
            shutil.copy2(str(path), str(backup / path.name))
    try:
        snippet.write_bytes(Path(args.snippet).read_bytes())
        rate.write_text("limit_req_zone $binary_remote_addr zone=brcl_bank_queries:10m rate=3r/s;\n")
        vhost.write_text(updated)
        subprocess.run(["nginx", "-t"], check=True)
        subprocess.run(["systemctl", "reload", "nginx"], check=True)
    except Exception:
        for path, content in originals.items():
            if content is None:
                if path.exists():
                    path.unlink()
            else:
                path.write_bytes(content)
        subprocess.run(["nginx", "-t"], check=True)
        subprocess.run(["systemctl", "reload", "nginx"], check=True)
        raise
    receipt = {"url": "https://zhushu.homes/bank/v1", "backup": str(backup), "previous_vhost_sha256": hashlib.sha256(originals[vhost]).hexdigest(), "vhost_sha256": hashlib.sha256(vhost.read_bytes()).hexdigest(), "transport": "restricted SSH reverse forward, loopback only"}
    (backup / "receipt.json").write_text(json.dumps(receipt, indent=2))
    print(json.dumps(receipt))


if __name__ == "__main__":
    main()
