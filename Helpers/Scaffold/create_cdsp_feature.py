#!/usr/bin/env python3
"""Create a CDSP feature (Context / Domain / System / Presentation triple).

Same output as Samples~/Calendar, with the namespace resolved like Rider
does it: nearest .asmdef rootNamespace + relative folder path.

Usage:
    python create_cdsp_feature.py Inventory --dir Assets/Logic/Features
    python create_cdsp_feature.py Inventory --dir Assets/Logic/Features --single-file
    python create_cdsp_feature.py Inventory --dir Assets/Logic/Features --namespace My.Game --force
    python create_cdsp_feature.py Inventory --dir Assets/Logic/Features --dry-run

Rider External Tool preset (Settings > Tools > External Tools > +):
    Name:            CDSP Feature
    Program:         python
    Arguments:       "<package-path>/Helpers/Scaffold/create_cdsp_feature.py" "$Prompt$" --dir "$FileDir$"
    Working dir:     $ProjectFileDir$
    (Right-click the PARENT folder, e.g. Features, then type the feature name.)

Only stdlib is used. Exit code 0 on success, 1 on validation error,
2 if target files already exist (unless --force).
"""

import argparse
import json
import re
import sys
from pathlib import Path

IDENT_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
INVALID_CHARS_RE = re.compile(r"[^A-Za-z0-9_]")


def fail(message, code=1):
    print("error: {}".format(message), file=sys.stderr)
    return code


def sanitize_part(part):
    """Turn one folder name into a valid C# namespace segment."""
    cleaned = INVALID_CHARS_RE.sub("_", part.strip())
    cleaned = cleaned.strip("_")
    if not cleaned:
        return ""
    if cleaned[0].isdigit():
        cleaned = "_" + cleaned
    return cleaned


def sanitize_feature(raw):
    """Turn user input into a valid PascalCase-ish C# type prefix."""
    name = (raw or "").strip()
    # Allow typing "InventoryContext" by accident: strip known suffixes.
    for suffix in ("Context", "Domain", "System", "Presentation"):
        if len(name) > len(suffix) and name.endswith(suffix):
            name = name[: -len(suffix)]
            break
    name = INVALID_CHARS_RE.sub("", name)
    if not name:
        return ""
    if name[0].isdigit():
        return ""
    return name[0].upper() + name[1:]


def find_nearest_asmdef(start_dir):
    """Walk up looking for a directory containing a *.asmdef. Returns Path or None."""
    current = start_dir.resolve()
    for _ in range(30):
        try:
            hits = sorted(current.glob("*.asmdef"))
        except OSError:
            hits = []
        if hits:
            return hits[0]
        parent = current.parent
        if parent == current:
            break
        current = parent
    return None


def read_root_namespace(asmdef_path):
    try:
        with open(asmdef_path, "r", encoding="utf-8-sig") as f:
            data = json.load(f)
    except (OSError, ValueError):
        return ""
    ns = (data.get("rootNamespace") or "") if isinstance(data, dict) else ""
    return ns.strip()


def resolve_namespace(out_dir, explicit=None):
    """(namespace, explanation). Mirrors Rider: asmdef root + relative path."""
    if explicit:
        return explicit.strip(), "explicit --namespace"
    out_dir = out_dir.resolve()
    asmdef = find_nearest_asmdef(out_dir)
    if asmdef is not None:
        root = read_root_namespace(asmdef)
        if root:
            try:
                rel = out_dir.relative_to(asmdef.parent).parts
            except ValueError:
                rel = ()
            segments = [s for s in (sanitize_part(p) for p in rel) if s]
            ns = root + ("." + ".".join(segments) if segments else "")
            return ns, "asmdef {} rootNamespace '{}' + path".format(asmdef.name, root)
        # asmdef without rootNamespace: keep searching above it.
        above = find_nearest_asmdef(asmdef.parent.parent)
        if above is not None:
            root = read_root_namespace(above)
            if root:
                try:
                    rel = out_dir.relative_to(above.parent).parts
                except ValueError:
                    rel = ()
                segments = [s for s in (sanitize_part(p) for p in rel) if s]
                ns = root + ("." + ".".join(segments) if segments else "")
                return ns, "asmdef {} rootNamespace '{}' + path".format(above.name, root)
    # Fallback: path relative to Assets/ (this is how Samples~/Calendar gets Logic.Features.Calendar).
    parts = out_dir.parts
    if "Assets" in parts:
        rel = parts[parts.index("Assets") + 1 :]
        segments = [s for s in (sanitize_part(p) for p in rel) if s]
        if segments:
            return ".".join(segments), "fallback: path relative to Assets/"
    print(
        "warning: no .asmdef with rootNamespace and no Assets/ folder found; "
        "namespace falls back to the output folder name. Pass --namespace to override.",
        file=sys.stderr,
    )
    fallback = sanitize_part(out_dir.name)
    return fallback or "Game", "fallback: output folder name"


DOMAIN_TPL = """using CDSP.Base;

namespace {ns}
{{
    public class {name}Domain : Domain
    {{
    }}
}}
"""

PRESENTATION_TPL = """using CDSP.Base;

namespace {ns}
{{
    public class {name}Presentation : Presentation<{name}System>
    {{
    }}
}}
"""

SYSTEM_TPL = """using CDSP.Base;

namespace {ns}
{{
    public class {name}System : System<{name}Domain, {name}Presentation>
    {{
    }}
}}
"""

CONTEXT_TPL = """using CDSP.Base;

namespace {ns}
{{
    public class {name}Context : Context<{name}Domain, {name}System, {name}Presentation>
    {{
    }}
}}
"""

SINGLE_FILE_TPL = """using CDSP.Base;

namespace {ns}
{{
    public class {name}Domain : Domain
    {{
    }}

    public class {name}Presentation : Presentation<{name}System>
    {{
    }}

    public class {name}System : System<{name}Domain, {name}Presentation>
    {{
    }}

    public class {name}Context : Context<{name}Domain, {name}System, {name}Presentation>
    {{
    }}
}}
"""


def plan_targets(feature, parent_dir, single_file):
    if single_file:
        return [(parent_dir / (feature + ".cs"), SINGLE_FILE_TPL)]
    out_dir = parent_dir / feature
    return [
        (out_dir / (feature + "Domain.cs"), DOMAIN_TPL),
        (out_dir / (feature + "Presentation.cs"), PRESENTATION_TPL),
        (out_dir / (feature + "System.cs"), SYSTEM_TPL),
        (out_dir / (feature + "Context.cs"), CONTEXT_TPL),
    ]


def parse_args(argv=None):
    p = argparse.ArgumentParser(
        description="Create a CDSP feature (Calendar-style triple with correct namespace)."
    )
    p.add_argument("feature", help="Feature name, e.g. Inventory")
    p.add_argument("--dir", required=True, help="Parent folder (split mode creates a <Feature>/ subfolder there)")
    p.add_argument("--single-file", action="store_true", help="Write one <Feature>.cs instead of four files")
    p.add_argument("--namespace", dest="namespace", default="", help="Override the auto-detected namespace")
    p.add_argument("--force", action="store_true", help="Overwrite existing files")
    p.add_argument("--dry-run", action="store_true", help="Print what would be created, write nothing")
    return p.parse_args(argv)


def main(argv=None):
    args = parse_args(argv)
    feature = sanitize_feature(args.feature)
    if not feature or not IDENT_RE.match(feature):
        return fail(
            "invalid feature name {!r}: use letters, digits and _ (must not start with a digit).".format(args.feature)
        )
    parent_dir = Path(args.dir)
    targets = plan_targets(feature, parent_dir, args.single_file)
    ns_dir = parent_dir if args.single_file else parent_dir / feature
    namespace, explanation = resolve_namespace(ns_dir, args.namespace or "")

    if args.dry_run:
        print("feature:   {}".format(feature))
        print("namespace: {} ({})".format(namespace, explanation))
        for path, _tpl in targets:
            print("write:     {}".format(path))
        return 0

    existing = [str(p) for p, _ in targets if p.exists()]
    if existing and not args.force:
        print("error: already exists (use --force to overwrite):", file=sys.stderr)
        for e in existing:
            print("  " + e, file=sys.stderr)
        return 2

    for path, tpl in targets:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(tpl.format(ns=namespace, name=feature), encoding="utf-8", newline="\n")
        print("created: {}".format(path))
    print("namespace: {} ({})".format(namespace, explanation))
    print("note: Unity generates .meta files on refresh; nothing else to do.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
