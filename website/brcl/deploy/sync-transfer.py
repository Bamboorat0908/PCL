#!/usr/bin/env python3
"""Stage hash-verified files from the SSH mirror and switch the HTTPS manifest."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
from tempfile import NamedTemporaryFile
from urllib.request import urlopen

ROOT = Path("/var/www/brcl-transfer")
UPSTREAM = "http://127.0.0.1:25161/"
PACKS = [
    ("modpacks/TRM-1.11-high.zip", 164408085, "408a5201984a03dea09cc9a475186af56a5b350fa6860707774e2e823cf403fe"),
    ("modpacks/TRM-1.11-low.zip", 164806392, "77ae44973a087a767bf64ce927dcae66595a330d7d8e77e2b48ad3c900d369fd"),
]


def digest_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def sync_file(path, size, expected):
    destination = ROOT / path
    destination.parent.mkdir(mode=0o755, exist_ok=True)
    if destination.is_file():
        if destination.stat().st_size != size or digest_file(destination) != expected:
            raise ValueError(f"Existing immutable file differs: {path}")
        return path
    with NamedTemporaryFile(dir=destination.parent, prefix=".incoming-", delete=False) as out:
        temporary = Path(out.name)
        try:
            with urlopen(UPSTREAM + path, timeout=60) as data:
                shutil.copyfileobj(data, out, length=1024 * 1024)
            out.flush()
            if temporary.stat().st_size != size or digest_file(temporary) != expected:
                raise ValueError(f"Size or SHA-256 mismatch: {path}")
            temporary.chmod(0o644)
            temporary.replace(destination)
        finally:
            temporary.unlink(missing_ok=True)
    return path


def read_manifest(name, limit, expected):
    with urlopen(UPSTREAM + name, timeout=20) as response:
        data = response.read(limit + 1)
    if len(data) > limit or hashlib.sha256(data).hexdigest() != expected:
        raise ValueError(f"Mirror {name} differs from the locally verified release")
    return data


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--revision", required=True)
    parser.add_argument("--manifest-sha256", required=True)
    parser.add_argument("--signature-sha256", required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){3}", args.revision):
        raise ValueError("Invalid revision")
    manifest_data = read_manifest("latest.json", 65536, args.manifest_sha256)
    signature = read_manifest("latest.sig", 4096, args.signature_sha256)
    manifest = json.loads(manifest_data)
    revision = args.revision
    if manifest["version"] != revision or manifest["package"] != f"releases/BRCL-{revision}.zip":
        raise ValueError("Unexpected update package")
    if manifest["direct_exe"] != f"releases/BRCL-{revision}.exe":
        raise ValueError("Unexpected direct executable")

    files = [
        (manifest["package"], manifest["size"], manifest["sha256"]),
        (manifest["direct_exe"], manifest["direct_exe_size"], manifest["direct_exe_sha256"]),
    ] + PACKS
    with ThreadPoolExecutor(max_workers=4) as executor:
        transfers = [executor.submit(sync_file, *item) for item in files]
        for transfer in transfers:
            print("Verified " + transfer.result(), flush=True)

    manifests = ROOT / "manifests" / revision
    manifests.mkdir(mode=0o755, parents=True, exist_ok=True)
    for name, data in (("latest.json", manifest_data), ("latest.sig", signature)):
        destination = manifests / name
        if destination.is_file():
            if destination.read_bytes() != data:
                raise ValueError(f"Already staged {name} differs")
        else:
            destination.write_bytes(data)
        public = ROOT / name
        if not public.is_symlink():
            public.symlink_to("current/" + name)
    next_current = ROOT / "current.next"
    next_current.unlink(missing_ok=True)
    next_current.symlink_to(f"manifests/{revision}", target_is_directory=True)
    next_current.replace(ROOT / "current")
    print(f"Primary HTTPS data active for {revision}", flush=True)


if __name__ == "__main__":
    main()
