#!/usr/bin/env python3
"""Check StockCount Obsidian WikiLinks, Markdown relative links and YAML frontmatter.

No external dependencies and no network requests; all content comes from git.
Usage: python scripts/check_obsidian_links.py
"""
from __future__ import annotations

import re
import sys
from collections import defaultdict
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]
EXCLUDED = {".git", ".obsidian", ".venv", "bin", "obj", ".trash", "__pycache__"}
FENCE_RE = re.compile(r"\\x60\\x60\\x60[\s\S]*?\\x60\\x60\\x60")
INLINE_CODE_RE = re.compile(r"\\x60[^\\x60\n]*\\x60")
WIKI_RE = re.compile(r"\[\[([^\]]+)\]\]")
MARKDOWN_RE = re.compile(r"\]\(([^)\n]+)\)")
EXTERNAL = ("https:", "http:", "mailto:", "tel:", "data:", "app:", "obsidian:")


def note_text(text: str) -> str:
    # Markdown examples inside code fences or inline literals are not real links.
    return INLINE_CODE_RE.sub("", FENCE_RE.sub("", text))


def report_errors() -> int:
    docs = sorted(
        p for p in ROOT.rglob("*.md")
        if not EXCLUDED.intersection(p.relative_to(ROOT).parts)
    )
    files = {
        p.relative_to(ROOT).as_posix(): p
        for p in ROOT.rglob("*")
        if p.is_file() and not EXCLUDED.intersection(p.relative_to(ROOT).parts)
    }
    name_lookup: dict[str, list[str]] = defaultdict(list)
    for file in docs:
        name_lookup[file.stem].append(file.relative_to(ROOT).as_posix())
    errors: list[str] = []
    wiki_count = md_count = 0

    for doc in docs:
        key = doc.relative_to(ROOT).as_posix()
        content = doc.read_text(encoding="utf-8-sig")
        if not content.startswith("---\n") or "\n---\n" not in content[4:]:
            errors.append(f"{key}: missing Obsidian YAML frontmatter")
        else:
            yaml = content.split("\n---\n", 1)[0]
            for prop in ("title:", "aliases:", "tags:"):
                if prop not in yaml:
                    errors.append(f"{key}: frontmatter missing {prop[:-1]}")

        clean = note_text(content)

        for match in WIKI_RE.finditer(clean):
            wiki_count += 1
            target = match.group(1).split("|", 1)[0].split("#", 1)[0].strip()
            if not target:
                continue
            if target.lower().endswith(".md"):
                target = target[:-3]
            candidate = target.lstrip("/") + ".md"
            if candidate in files:
                continue
            if "/" not in target and len(name_lookup.get(target, [])) == 1:
                continue
            errors.append(f"{key}: broken or ambiguous Obsidian WikiLink [[{target}]]")

        for match in MARKDOWN_RE.finditer(clean):
            raw = match.group(1).strip()
            if not raw or raw.startswith("#") or raw.startswith(EXTERNAL):
                continue
            # Paths in authored docs are URL-encoded when needed for GitHub.
            raw = raw.split(" ", 1)[0].split("#", 1)[0].split("?", 1)[0].strip("<>")
            if not raw:
                continue
            target = (doc.parent / unquote(raw)).resolve()
            try:
                to_repo = target.relative_to(ROOT).as_posix()
            except ValueError:
                errors.append(f"{key}: Markdown link leaves repository: {raw}")
                continue
            md_count += 1
            if to_repo not in files:
                errors.append(f"{key}: broken Markdown link ({raw}) => {to_repo}")

    print(f"Scanned {len(docs)} Markdown notes; {wiki_count} WikiLinks, {md_count} relative Markdown links.")
    if errors:
        for error in errors:
            print(f"ERROR {error}", file=sys.stderr)
        print(f"FAILED: {len(errors)} link/metadata problems.", file=sys.stderr)
        return 1
    print("PASS: YAML frontmatter and all resolved local Wiki/Markdown links.")
    return 0


if __name__ == "__main__":
    raise SystemExit(report_errors())
