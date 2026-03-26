#!/usr/bin/env python3
import argparse
import csv
import json
import re
import urllib.request
from pathlib import Path

DEFAULT_TARGET_COUNT = 24

POSITIVE = {
    "walk": 3,
    "walking": 3,
    "tour": 2,
    "queenstown": 2,
    "tanglin": 2,
    "dawson": 2,
    "commonwealth": 2,
    "mrt to": 2,
    "from": 1,
    "to": 1,
}
NEGATIVE = {
    "bus": -4,
    "train": -4,
    "cycling": -3,
    "ride": -3,
    "for sale": -5,
    "elevator": -3,
    "platform": -2,
    "zoo": -5,
    "marina bay": -3,
    "chinatown": -3,
}

HARD_EXCLUDE = [
    "bus",
    "train ride",
    "for sale",
    "elevator",
    "platform",
    "cycling",
    "ride from",
    "buses at",
    "station ||",
]

WALK_REQUIRED = [
    "walk",
    "walking",
    "walking tour",
    "walk from",
    "walk to",
    "neighborhood walk",
]

THUMB_CANDIDATES = [
    "https://i.ytimg.com/vi/{id}/maxresdefault.jpg",
    "https://i.ytimg.com/vi/{id}/sddefault.jpg",
    "https://i.ytimg.com/vi/{id}/hqdefault.jpg",
    "https://i.ytimg.com/vi/{id}/mqdefault.jpg",
]


def resolve_path(project_root: Path, raw_path: str) -> Path:
    candidate = Path(raw_path).expanduser()
    if candidate.is_absolute():
        return candidate
    return (project_root / candidate).resolve()


def display_path(path: Path, project_root: Path) -> str:
    try:
        return str(path.relative_to(project_root))
    except ValueError:
        return str(path)


def norm(text: str) -> str:
    return re.sub(r"\s+", " ", (text or "").strip().lower())


def parse_tsv(path: Path):
    rows = []
    with path.open("r", encoding="utf-8") as file:
        reader = csv.DictReader(file, delimiter="\t")
        for row in reader:
            if not row:
                continue
            score = int((row.get("score") or "0").strip() or "0")
            title = (row.get("title") or "").strip()
            url = (row.get("url") or "").strip()
            video_id = ""
            if "v=" in url:
                video_id = url.split("v=")[-1].split("&")[0]
            rows.append(
                {
                    "id": video_id,
                    "title": title,
                    "uploader": (row.get("uploader") or "").strip(),
                    "duration": (row.get("duration") or "").strip(),
                    "url": url,
                    "scan_score": score,
                    "source": "shortlist",
                }
            )
    return rows


def parse_full(path: Path):
    raw = json.loads(path.read_text(encoding="utf-8"))
    data = raw.get("items", []) if isinstance(raw, dict) else raw
    rows = []
    for row in data:
        rows.append(
            {
                "id": (row.get("id") or "").strip(),
                "title": (row.get("title") or "").strip(),
                "uploader": (row.get("uploader") or "").strip(),
                "duration": (row.get("duration") or "").strip(),
                "url": (row.get("url") or "").strip(),
                "scan_score": int(row.get("score") or 0),
                "source": "full",
            }
        )
    return rows


def relevance_score(item):
    title = norm(item["title"])
    score = item["scan_score"] * 2
    for keyword, weight in POSITIVE.items():
        if keyword in title:
            score += weight
    for keyword, weight in NEGATIVE.items():
        if keyword in title:
            score += weight

    if "queenstown" not in title and "tanglin" not in title and "dawson" not in title and "commonwealth" not in title:
        score -= 4
    return score


def is_walk_content(item):
    title = norm(item["title"])
    if any(keyword in title for keyword in HARD_EXCLUDE):
        return False
    return any(keyword in title for keyword in WALK_REQUIRED)


def check_thumbnail(video_id: str, timeout_seconds: float):
    if not video_id:
        return False, ""

    for pattern in THUMB_CANDIDATES:
        url = pattern.format(id=video_id)
        req = urllib.request.Request(url, method="GET", headers={"User-Agent": "Mozilla/5.0"})
        try:
            with urllib.request.urlopen(req, timeout=max(1.0, timeout_seconds)) as response:
                content_type = response.headers.get("Content-Type", "")
                content_len = int(response.headers.get("Content-Length", "0") or "0")
                if response.status == 200 and ("image" in content_type or url.endswith(".jpg")) and content_len > 2000:
                    return True, url
        except Exception:
            continue
    return False, ""


def dedupe(rows):
    out = []
    seen = set()
    for row in rows:
        video_id = row["id"]
        if not video_id or video_id in seen:
            continue
        seen.add(video_id)
        out.append(row)
    return out


def write_tsv(path: Path, rows):
    fields = [
        "rank",
        "mode",
        "id",
        "title",
        "uploader",
        "duration",
        "url",
        "scan_score",
        "relevance",
        "thumbnail_ok",
        "thumbnail_url",
        "source",
    ]
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fields, delimiter="\t")
        writer.writeheader()
        for rank, row in enumerate(rows, 1):
            out = dict(row)
            out["rank"] = rank
            writer.writerow(out)


def parse_args() -> argparse.Namespace:
    project_root_default = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description="Select Queenstown walking videos from scan outputs.")
    parser.add_argument("--project-root", default=str(project_root_default), help="Project root (defaults to repo root)")
    parser.add_argument("--shortlist", default="Docs/queenstown_youtube_scan_shortlist.tsv", help="Shortlist TSV input path")
    parser.add_argument("--full", default="Docs/queenstown_youtube_scan_all.json", help="Full JSON input path")
    parser.add_argument("--out-primary", default="Docs/queenstown_videos_primary.tsv", help="Primary TSV output path")
    parser.add_argument("--out-final", default="Docs/queenstown_videos_final.tsv", help="Final TSV output path")
    parser.add_argument("--out-summary", default="Docs/queenstown_videos_summary.json", help="Summary JSON output path")
    parser.add_argument("--target-count", type=int, default=DEFAULT_TARGET_COUNT, help="Target number of selected videos")
    parser.add_argument("--thumbnail-timeout-seconds", type=float, default=12.0, help="Thumbnail request timeout seconds")
    parser.add_argument("--skip-thumbnail-check", action="store_true", help="Skip thumbnail HTTP checks (faster/offline mode)")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    project_root = Path(args.project_root).expanduser().resolve()

    shortlist_path = resolve_path(project_root, args.shortlist)
    full_path = resolve_path(project_root, args.full)
    out_primary_path = resolve_path(project_root, args.out_primary)
    out_final_path = resolve_path(project_root, args.out_final)
    out_summary_path = resolve_path(project_root, args.out_summary)

    if not shortlist_path.exists():
        raise FileNotFoundError(f"Missing shortlist TSV: {shortlist_path}")
    if not full_path.exists():
        raise FileNotFoundError(f"Missing full JSON: {full_path}")

    target_count = max(1, args.target_count)
    shortlist = dedupe(parse_tsv(shortlist_path))
    full = dedupe(parse_full(full_path))

    for row in shortlist:
        row["relevance"] = relevance_score(row)
    shortlist.sort(key=lambda row: row["relevance"], reverse=True)

    primary = []
    for row in shortlist:
        if args.skip_thumbnail_check:
            thumbnail_ok, thumbnail_url = True, ""
        else:
            thumbnail_ok, thumbnail_url = check_thumbnail(row["id"], args.thumbnail_timeout_seconds)

        row["thumbnail_ok"] = thumbnail_ok
        row["thumbnail_url"] = thumbnail_url
        row["mode"] = "primary"

        if row["relevance"] >= 8 and thumbnail_ok and is_walk_content(row):
            primary.append(row)

    primary = primary[:target_count]
    write_tsv(out_primary_path, primary)

    final = list(primary)
    need = max(0, target_count - len(final))

    if need > 0:
        full_by_id = {row["id"]: row for row in full}
        for row in full_by_id.values():
            row["relevance"] = relevance_score(row)
        fallback = sorted(full_by_id.values(), key=lambda row: row["relevance"], reverse=True)

        selected_ids = {row["id"] for row in final}
        for row in fallback:
            if len(final) >= target_count:
                break
            if row["id"] in selected_ids:
                continue
            if row["relevance"] < 2:
                continue
            if not is_walk_content(row):
                continue

            if args.skip_thumbnail_check:
                thumbnail_ok, thumbnail_url = True, ""
            else:
                thumbnail_ok, thumbnail_url = check_thumbnail(row["id"], args.thumbnail_timeout_seconds)
            if not thumbnail_ok:
                continue

            row["thumbnail_ok"] = thumbnail_ok
            row["thumbnail_url"] = thumbnail_url
            row["mode"] = "fallback"
            final.append(row)
            selected_ids.add(row["id"])

    final.sort(key=lambda row: (0 if row.get("mode") == "primary" else 1, -row["relevance"]))
    final = final[:target_count]
    write_tsv(out_final_path, final)

    summary = {
        "shortlist_count": len(shortlist),
        "primary_count": len(primary),
        "final_count": len(final),
        "target_count": target_count,
        "primary_file": display_path(out_primary_path, project_root),
        "final_file": display_path(out_final_path, project_root),
        "rule": "Primary uses good videos + thumbnail available. Fallback fills from full set when needed.",
        "skip_thumbnail_check": bool(args.skip_thumbnail_check),
    }

    out_summary_path.parent.mkdir(parents=True, exist_ok=True)
    out_summary_path.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps(summary, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
