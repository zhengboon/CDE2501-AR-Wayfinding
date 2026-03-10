#!/usr/bin/env python3
import csv
import json
import re
import urllib.request
from pathlib import Path

ROOT = Path('/mnt/c/Users/zheng/CDE2501-AR-Wayfinding')
SHORTLIST = ROOT / 'Docs' / 'queenstown_youtube_scan_shortlist.tsv'
FULL = ROOT / 'Docs' / 'queenstown_youtube_scan_all.json'
OUT_PRIMARY = ROOT / 'Docs' / 'queenstown_videos_primary.tsv'
OUT_FINAL = ROOT / 'Docs' / 'queenstown_videos_final.tsv'
OUT_SUMMARY = ROOT / 'Docs' / 'queenstown_videos_summary.json'

TARGET_COUNT = 24

POSITIVE = {
    'walk': 3, 'walking': 3, 'tour': 2, 'queenstown': 2, 'tanglin': 2,
    'dawson': 2, 'commonwealth': 2, 'mrt to': 2, 'from': 1, 'to': 1,
}
NEGATIVE = {
    'bus': -4, 'train': -4, 'cycling': -3, 'ride': -3,
    'for sale': -5, 'elevator': -3, 'platform': -2,
    'zoo': -5, 'marina bay': -3, 'chinatown': -3,
}

HARD_EXCLUDE = [
    'bus', 'train ride', 'for sale', 'elevator', 'platform', 'cycling',
    'ride from', 'buses at', 'station ||',
]

WALK_REQUIRED = [
    'walk', 'walking', 'walking tour', 'walk from', 'walk to', 'neighborhood walk',
]

THUMB_CANDIDATES = [
    'https://i.ytimg.com/vi/{id}/maxresdefault.jpg',
    'https://i.ytimg.com/vi/{id}/sddefault.jpg',
    'https://i.ytimg.com/vi/{id}/hqdefault.jpg',
    'https://i.ytimg.com/vi/{id}/mqdefault.jpg',
]


def norm(text: str) -> str:
    return re.sub(r'\s+', ' ', (text or '').strip().lower())


def parse_tsv(path: Path):
    rows = []
    with path.open('r', encoding='utf-8') as f:
        reader = csv.DictReader(f, delimiter='\t')
        for row in reader:
            if not row:
                continue
            score = int((row.get('score') or '0').strip() or '0')
            title = (row.get('title') or '').strip()
            url = (row.get('url') or '').strip()
            vid = ''
            if 'v=' in url:
                vid = url.split('v=')[-1].split('&')[0]
            rows.append({
                'id': vid,
                'title': title,
                'uploader': (row.get('uploader') or '').strip(),
                'duration': (row.get('duration') or '').strip(),
                'url': url,
                'scan_score': score,
                'source': 'shortlist',
            })
    return rows


def parse_full(path: Path):
    data = json.loads(path.read_text(encoding='utf-8'))
    rows = []
    for row in data:
        rows.append({
            'id': (row.get('id') or '').strip(),
            'title': (row.get('title') or '').strip(),
            'uploader': (row.get('uploader') or '').strip(),
            'duration': (row.get('duration') or '').strip(),
            'url': (row.get('url') or '').strip(),
            'scan_score': int(row.get('score') or 0),
            'source': 'full',
        })
    return rows


def relevance_score(item):
    t = norm(item['title'])
    score = item['scan_score'] * 2
    for k, w in POSITIVE.items():
        if k in t:
            score += w
    for k, w in NEGATIVE.items():
        if k in t:
            score += w
    # reject very weak entries
    if 'queenstown' not in t and 'tanglin' not in t and 'dawson' not in t and 'commonwealth' not in t:
        score -= 4
    return score


def is_walk_content(item):
    t = norm(item['title'])
    if any(k in t for k in HARD_EXCLUDE):
        return False
    return any(k in t for k in WALK_REQUIRED)


def check_thumbnail(video_id: str):
    if not video_id:
        return False, ''

    for pat in THUMB_CANDIDATES:
        url = pat.format(id=video_id)
        req = urllib.request.Request(url, method='GET', headers={'User-Agent': 'Mozilla/5.0'})
        try:
            with urllib.request.urlopen(req, timeout=12) as r:
                ct = r.headers.get('Content-Type', '')
                ln = int(r.headers.get('Content-Length', '0') or '0')
                if r.status == 200 and ('image' in ct or url.endswith('.jpg')) and ln > 2000:
                    return True, url
        except Exception:
            continue
    return False, ''


def dedupe(rows):
    out = []
    seen = set()
    for r in rows:
        vid = r['id']
        if not vid or vid in seen:
            continue
        seen.add(vid)
        out.append(r)
    return out


def write_tsv(path: Path, rows):
    fields = [
        'rank', 'mode', 'id', 'title', 'uploader', 'duration', 'url',
        'scan_score', 'relevance', 'thumbnail_ok', 'thumbnail_url', 'source'
    ]
    with path.open('w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=fields, delimiter='\t')
        w.writeheader()
        for i, r in enumerate(rows, 1):
            out = dict(r)
            out['rank'] = i
            w.writerow(out)


def main():
    shortlist = dedupe(parse_tsv(SHORTLIST))
    full = dedupe(parse_full(FULL))

    for r in shortlist:
        r['relevance'] = relevance_score(r)
    shortlist.sort(key=lambda x: x['relevance'], reverse=True)

    # Good ones first (strict walk content)
    primary = []
    for r in shortlist:
        ok, thumb = check_thumbnail(r['id'])
        r['thumbnail_ok'] = ok
        r['thumbnail_url'] = thumb
        r['mode'] = 'primary'
        if r['relevance'] >= 8 and ok and is_walk_content(r):
            primary.append(r)

    primary = primary[:TARGET_COUNT]
    write_tsv(OUT_PRIMARY, primary)

    # Final set: use primary first, then fallback from full if missing count
    final = list(primary)
    need = max(0, TARGET_COUNT - len(final))

    if need > 0:
        full_by_id = {r['id']: r for r in full}
        for r in full_by_id.values():
            r['relevance'] = relevance_score(r)
        fallback = sorted(full_by_id.values(), key=lambda x: x['relevance'], reverse=True)

        selected_ids = {r['id'] for r in final}
        for r in fallback:
            if len(final) >= TARGET_COUNT:
                break
            if r['id'] in selected_ids:
                continue
            if r['relevance'] < 2:
                continue
            if not is_walk_content(r):
                continue
            ok, thumb = check_thumbnail(r['id'])
            if not ok:
                continue
            r['thumbnail_ok'] = ok
            r['thumbnail_url'] = thumb
            r['mode'] = 'fallback'
            final.append(r)
            selected_ids.add(r['id'])

    # sort final with primary first, then relevance
    final.sort(key=lambda x: (0 if x.get('mode') == 'primary' else 1, -x['relevance']))
    final = final[:TARGET_COUNT]
    write_tsv(OUT_FINAL, final)

    summary = {
        'shortlist_count': len(shortlist),
        'primary_count': len(primary),
        'final_count': len(final),
        'target_count': TARGET_COUNT,
        'primary_file': str(OUT_PRIMARY.relative_to(ROOT)),
        'final_file': str(OUT_FINAL.relative_to(ROOT)),
        'rule': 'Primary uses good videos + thumbnail available. Fallback fills from full set when needed.'
    }
    OUT_SUMMARY.write_text(json.dumps(summary, indent=2), encoding='utf-8')

    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
