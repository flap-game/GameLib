#!/usr/bin/env python3
"""Point every GameLib git dependency of a Unity project at one ref.

A game installs GameLib by git URL, one line per module, all on the same tag:

    "com.flapgame.gamelib.core": "https://github.com/flap-game/GameLib.git?path=Packages/com.flapgame.gamelib.core#v0.8.13",

While a fix is being tried (PC pushes a commit, Mac builds it), every one of those lines has to
move to that commit together, and Unity's packages-lock.json has to forget the old hash, or the
next import silently keeps the old code. This does both:

    python3 Tools/gamelib-pin.py 3f2a9c1 --project ~/Projects/casual-10-offline-games
    python3 Tools/gamelib-pin.py v0.8.14 --project .          # back to a release tag
    python3 Tools/gamelib-pin.py 0.8.14                       # same: 'v' is added
    python3 Tools/gamelib-pin.py --show                       # what the project uses now

A commit / branch pin is for testing only: do not commit it to the game's repo. Pin a release tag
before committing. Only lines pointing at github.com/flap-game/GameLib are touched; the file's
own formatting is kept (text replacement, not a JSON re-dump).

Standard library only, so the Mac can run it straight from a raw download:
    curl -fsSL https://raw.githubusercontent.com/flap-game/GameLib/main/Tools/gamelib-pin.py -o /tmp/gamelib-pin.py
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

_LINE = re.compile(
    r'("(com\.flapgame\.gamelib\.[A-Za-z0-9._-]+)"\s*:\s*"'
    r'(https://github\.com/flap-game/GameLib(?:\.git)?[^"#]*))(#[^"]*)?"')
_REF = re.compile(r"^[A-Za-z0-9._/-]{1,100}$")
_SEMVER = re.compile(r"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$")
_SHA = re.compile(r"^[0-9a-f]{7,40}$")


def _paths(project: Path) -> tuple[Path, Path]:
    pk = project / "Packages"
    return pk / "manifest.json", pk / "packages-lock.json"


def current(manifest: Path) -> dict[str, str]:
    text = manifest.read_text(encoding="utf-8")
    return {m.group(2): (m.group(4) or "#")[1:] for m in _LINE.finditer(text)}


def pin(project: Path, ref: str, dry_run: bool = False) -> dict:
    if _SEMVER.match(ref):
        ref = "v" + ref
    if not _REF.match(ref) or ".." in ref:
        raise SystemExit(f"error: bad ref {ref!r}")
    manifest, lock = _paths(project)
    if not manifest.is_file():
        raise SystemExit(f"error: no {manifest}")
    text = manifest.read_text(encoding="utf-8")
    before = current(manifest)
    if not before:
        raise SystemExit(f"error: {manifest} has no GameLib git dependency")
    new_text = _LINE.sub(lambda m: f'{m.group(1)}#{ref}"', text)
    dropped: list[str] = []
    if lock.is_file():
        data = json.loads(lock.read_text(encoding="utf-8"))
        deps = data.get("dependencies", {})
        # Unity re-resolves a git package only when its lock entry is gone; leaving it makes a
        # moved branch (or an unchanged-looking ref) keep the old hash.
        dropped = [k for k in list(deps) if k in before]
        for k in dropped:
            del deps[k]
    if not dry_run:
        manifest.write_text(new_text, encoding="utf-8", newline="")
        if dropped:
            lock.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8", newline="\n")
    return {"ref": ref, "before": before, "lock_entries_dropped": dropped,
            "test_pin": bool(_SHA.match(ref)) or not ref.startswith("v")}


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("ref", nargs="?", help="tag (v0.8.14 / 0.8.14), commit sha or branch")
    ap.add_argument("--project", default=".", help="Unity project root (default: cwd)")
    ap.add_argument("--show", action="store_true", help="print the refs in use and exit")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args(argv)
    project = Path(args.project).expanduser().resolve()
    manifest, _ = _paths(project)
    if args.show or not args.ref:
        refs = current(manifest) if manifest.is_file() else {}
        for name, ref in sorted(refs.items()):
            print(f"{name}  #{ref}")
        distinct = sorted(set(refs.values()))
        if len(distinct) > 1:
            print(f"WARNING: mixed refs {distinct} -- pin one with: gamelib-pin.py <ref>")
        return 0 if refs else 1
    rep = pin(project, args.ref, dry_run=args.dry_run)
    olds = sorted(set(rep["before"].values()))
    print(f"{'would pin' if args.dry_run else 'pinned'} {len(rep['before'])} GameLib packages "
          f"{olds} -> #{rep['ref']}; lock entries dropped: {len(rep['lock_entries_dropped'])}")
    if rep["test_pin"]:
        print("NOTE: test pin (commit/branch). Do not commit manifest.json like this; pin the "
              "release tag before committing.")
    print("Unity re-resolves the packages when the Editor regains focus (or on next open).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
