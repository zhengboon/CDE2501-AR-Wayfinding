#!/usr/bin/env python3
"""
Cached Unity build + launch orchestrator for CDE2501-AR-Wayfinding.

Behavior:
- Computes a project fingerprint over configured watched files.
- Rebuilds in Unity batchmode only when fingerprint changed or output missing.
- Stores cache state under UnityBuildCache/.
- Writes machine + human build reports for quick review.
- Can launch Unity Editor after build/check.
"""

from __future__ import annotations

import argparse
import datetime as dt
import fnmatch
import hashlib
import json
import os
import platform
import subprocess
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional, Tuple

PROJECT_ROOT = Path(__file__).resolve().parents[1]
CONFIG_PATH = PROJECT_ROOT / "scripts" / "unity_cached_builder_config.json"
CACHE_DIR = PROJECT_ROOT / "UnityBuildCache"
STATE_PATH = CACHE_DIR / "state.json"
LATEST_SUMMARY_PATH = CACHE_DIR / "latest_build_summary.json"
LATEST_REPORT_PATH = CACHE_DIR / "latest_build_report.md"
LOGS_DIR = CACHE_DIR / "logs"

EXECUTE_METHOD = "CDE2501.Wayfinding.EditorTools.CDE2501BuildRunner.BuildFromEnvironment"

DEFAULT_CONFIG = {
    "unityExecutable": "",
    "build": {
        "target": "StandaloneWindows64",
        "outputPath": "Builds/Windows/CDE2501-Wayfinding.exe",
        "scenePath": "Assets/Scenes/Main.unity",
        "developmentBuild": False,
        "nographics": True,
    },
    "watch": {
        "roots": ["Assets", "Packages", "ProjectSettings", "scripts"],
        "excludeDirs": [
            "Library",
            "Temp",
            "Logs",
            "Build",
            "Builds",
            "Recordings",
            "UserSettings",
            ".git",
            "UnityBuildCache",
            ".tmp-yt",
            "baritone-example",
            "street_view",
            "video_frames",
        ],
        "excludeFileGlobs": [
            "*.csproj",
            "*.sln",
            "*.user",
            "*.tmp",
            "*.pidb",
            "*.pdb",
            "*.mdb",
            "*.opendb",
            "*.VC.db",
        ],
        "includeExtensions": [
            ".cs",
            ".json",
            ".unity",
            ".prefab",
            ".asset",
            ".meta",
            ".txt",
            ".md",
            ".kml",
            ".kmz",
            ".geojson",
            ".png",
            ".jpg",
            ".jpeg",
            ".shader",
            ".asmdef",
            ".asmref",
            ".uxml",
            ".uss",
        ],
    },
}


@dataclass
class ScanResult:
    digest: str
    file_count: int


@dataclass
class BuildDecision:
    should_build: bool
    reason: str
    cache_hit: bool


def is_windows_unity_executable(unity_exe: Path) -> bool:
    return unity_exe.suffix.lower() == ".exe"


def try_wslpath_to_windows(path: Path) -> Optional[str]:
    try:
        proc = subprocess.run(
            ["wslpath", "-w", str(path)],
            cwd=str(PROJECT_ROOT),
            check=False,
            capture_output=True,
            text=True,
        )
        if proc.returncode == 0:
            converted = (proc.stdout or "").strip()
            if converted:
                return converted
    except Exception:
        return None
    return None


def posix_mnt_to_windows(path: Path) -> str:
    raw = str(path)
    if raw.startswith("/mnt/") and len(raw) > 6:
        drive = raw[5].upper()
        rest = raw[6:].replace("/", "\\")
        return f"{drive}:{rest}"
    return raw


def format_path_for_unity(path: Path, unity_exe: Path) -> str:
    if not is_windows_unity_executable(unity_exe):
        return str(path)

    converted = try_wslpath_to_windows(path)
    if converted:
        return converted

    return posix_mnt_to_windows(path)


def deep_merge(base: Dict, override: Dict) -> Dict:
    merged = dict(base)
    for key, value in (override or {}).items():
        if key in merged and isinstance(merged[key], dict) and isinstance(value, dict):
            merged[key] = deep_merge(merged[key], value)
        else:
            merged[key] = value
    return merged


def load_config() -> Dict:
    config = dict(DEFAULT_CONFIG)
    if CONFIG_PATH.exists():
        try:
            user_config = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
            if not isinstance(user_config, dict):
                raise ValueError("Top-level config must be an object")
            config = deep_merge(config, user_config)
        except Exception as exc:
            print(f"[unity_cached_builder] Failed to parse config: {CONFIG_PATH} ({exc})")
            print("[unity_cached_builder] Falling back to defaults.")
    return config


def ensure_cache_dirs() -> None:
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    LOGS_DIR.mkdir(parents=True, exist_ok=True)


def load_state() -> Dict:
    if not STATE_PATH.exists():
        return {}
    try:
        state = json.loads(STATE_PATH.read_text(encoding="utf-8"))
        return state if isinstance(state, dict) else {}
    except Exception:
        return {}


def save_state(state: Dict) -> None:
    ensure_cache_dirs()
    STATE_PATH.write_text(json.dumps(state, indent=2), encoding="utf-8")


def normalize_rel(path: Path) -> str:
    return str(path.as_posix())


def should_include_file(rel_path: str, include_exts: List[str], exclude_globs: List[str]) -> bool:
    name = rel_path.rsplit("/", 1)[-1]
    for pattern in exclude_globs:
        if fnmatch.fnmatch(name, pattern) or fnmatch.fnmatch(rel_path, pattern):
            return False

    if not include_exts:
        return True

    lower = rel_path.lower()
    return any(lower.endswith(ext.lower()) for ext in include_exts)


def scan_watch_files(config: Dict) -> ScanResult:
    watch = config.get("watch", {})
    roots = watch.get("roots", [])
    exclude_dirs = set(watch.get("excludeDirs", []))
    exclude_globs = watch.get("excludeFileGlobs", [])
    include_exts = watch.get("includeExtensions", [])

    hashed = hashlib.sha256()
    file_count = 0

    for root_name in roots:
        root = PROJECT_ROOT / root_name
        if not root.exists():
            continue

        for path in sorted(root.rglob("*")):
            if not path.is_file():
                continue

            rel = normalize_rel(path.relative_to(PROJECT_ROOT))
            parts = rel.split("/")
            if any(part in exclude_dirs for part in parts[:-1]):
                continue

            if not should_include_file(rel, include_exts, exclude_globs):
                continue

            try:
                stat = path.stat()
                content_hasher = hashlib.sha256()
                with path.open("rb") as handle:
                    while True:
                        chunk = handle.read(1024 * 1024)
                        if not chunk:
                            break
                        content_hasher.update(chunk)
            except OSError:
                continue

            file_count += 1
            hashed.update(rel.encode("utf-8", errors="ignore"))
            hashed.update(b"|")
            hashed.update(str(stat.st_size).encode("ascii", errors="ignore"))
            hashed.update(b"|")
            hashed.update(content_hasher.digest())
            hashed.update(b"\n")

    build_cfg = config.get("build", {})
    hashed.update(json.dumps(build_cfg, sort_keys=True).encode("utf-8"))
    return ScanResult(digest=hashed.hexdigest(), file_count=file_count)


def find_default_unity_executable() -> Optional[Path]:
    system = platform.system().lower()
    candidates: List[Path] = []

    if system == "windows":
        hub_root = Path(r"C:\Program Files\Unity\Hub\Editor")
        preferred = hub_root / "2022.3.62f3" / "Editor" / "Unity.exe"
        candidates.append(preferred)
        if hub_root.exists():
            candidates.extend(sorted(hub_root.glob("2022.3.*/Editor/Unity.exe"), reverse=True))
            candidates.extend(sorted(hub_root.glob("*/Editor/Unity.exe"), reverse=True))
    elif system == "darwin":
        candidates.append(Path("/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity"))
        hub_root = Path("/Applications/Unity/Hub/Editor")
        if hub_root.exists():
            candidates.extend(sorted(hub_root.glob("2022.3.*/Unity.app/Contents/MacOS/Unity"), reverse=True))
            candidates.extend(sorted(hub_root.glob("*/Unity.app/Contents/MacOS/Unity"), reverse=True))
    else:
        hub_root = Path.home() / "Unity/Hub/Editor"
        candidates.append(hub_root / "2022.3.62f3/Editor/Unity")
        if hub_root.exists():
            candidates.extend(sorted(hub_root.glob("2022.3.*/Editor/Unity"), reverse=True))
            candidates.extend(sorted(hub_root.glob("*/Editor/Unity"), reverse=True))
        wsl_windows_hub = Path("/mnt/c/Program Files/Unity/Hub/Editor")
        candidates.append(wsl_windows_hub / "2022.3.62f3/Editor/Unity.exe")
        if wsl_windows_hub.exists():
            candidates.extend(sorted(wsl_windows_hub.glob("2022.3.*/Editor/Unity.exe"), reverse=True))
            candidates.extend(sorted(wsl_windows_hub.glob("*/Editor/Unity.exe"), reverse=True))

    for candidate in candidates:
        if candidate.exists():
            return candidate
    return None


def resolve_unity_executable(config: Dict, override: Optional[str]) -> Optional[Path]:
    env_override = os.environ.get("UNITY_EXE", "").strip()
    cfg_value = override or env_override or str(config.get("unityExecutable", "")).strip()

    if cfg_value:
        path = Path(cfg_value).expanduser()
        if path.exists():
            return path

    return find_default_unity_executable()


def resolve_output_path(build_cfg: Dict, override: Optional[str]) -> Path:
    rel_or_abs = override or build_cfg.get("outputPath", "Builds/Windows/CDE2501-Wayfinding.exe")
    candidate = Path(rel_or_abs).expanduser()
    if not candidate.is_absolute():
        candidate = PROJECT_ROOT / candidate
    return candidate


def analyze_log(log_path: Path) -> Tuple[int, int, List[str], List[str]]:
    if not log_path.exists():
        return 0, 0, [], []

    try:
        lines = log_path.read_text(encoding="utf-8", errors="ignore").splitlines()
    except Exception:
        return 0, 0, [], []

    error_lines: List[str] = []
    warning_lines: List[str] = []

    for line in lines:
        l = line.lower()
        if " error " in l or "error cs" in l or "error:" in l:
            error_lines.append(line.strip())
        elif " warning " in l or "warning cs" in l or "warning:" in l:
            warning_lines.append(line.strip())

    return len(error_lines), len(warning_lines), error_lines[:20], warning_lines[:20]


def write_reports(summary: Dict) -> None:
    ensure_cache_dirs()
    LATEST_SUMMARY_PATH.write_text(json.dumps(summary, indent=2), encoding="utf-8")

    lines = [
        "# Cached Unity Build Report",
        "",
        f"- Time: {summary.get('timestamp')}",
        f"- Cache Hit: {summary.get('cacheHit')}",
        f"- Build Trigger: {summary.get('reason')}",
        f"- Build Attempted: {summary.get('buildAttempted')}",
        f"- Build Succeeded: {summary.get('buildSucceeded')}",
        f"- Build Target: {summary.get('buildTarget')}",
        f"- Output: {summary.get('outputPath')}",
        f"- Unity Executable: {summary.get('unityExecutable')}",
        f"- Fingerprint: {summary.get('fingerprint')}",
        f"- Watched Files: {summary.get('watchedFiles')}",
        f"- Duration Seconds: {summary.get('durationSeconds')}",
        f"- Log File: {summary.get('logFile')}",
        f"- Error Count (log scan): {summary.get('errorCount')}",
        f"- Warning Count (log scan): {summary.get('warningCount')}",
        "",
    ]

    failure_hint = summary.get("failureHint")
    if failure_hint:
        lines.append("## Failure Hint")
        lines.append("")
        lines.append(f"- {failure_hint}")
        lines.append("")

    errors = summary.get("errorExamples") or []
    warnings = summary.get("warningExamples") or []

    if errors:
        lines.append("## Error Examples")
        lines.append("")
        for line in errors:
            lines.append(f"- `{line}`")
        lines.append("")

    if warnings:
        lines.append("## Warning Examples")
        lines.append("")
        for line in warnings:
            lines.append(f"- `{line}`")
        lines.append("")

    LATEST_REPORT_PATH.write_text("\n".join(lines), encoding="utf-8")


def build_if_needed(
    unity_exe: Path,
    config: Dict,
    target_override: Optional[str],
    output_override: Optional[str],
    scene_override: Optional[str],
    dev_override: Optional[bool],
    force: bool,
) -> Dict:
    ensure_cache_dirs()

    build_cfg = config.get("build", {})
    target = target_override or build_cfg.get("target", "StandaloneWindows64")
    output_path = resolve_output_path(build_cfg, output_override)
    scene_path = scene_override or build_cfg.get("scenePath", "Assets/Scenes/Main.unity")
    dev_build = dev_override if dev_override is not None else bool(build_cfg.get("developmentBuild", False))
    use_nographics = bool(build_cfg.get("nographics", True))

    state = load_state()

    previous_fingerprint = state.get("fingerprint")
    previous_target = state.get("buildTarget")
    output_exists = output_path.exists()

    if force:
        # Skip expensive full-tree hashing for forced builds.
        scan = ScanResult(
            digest=str(previous_fingerprint or "<skipped-force-scan>"),
            file_count=int(state.get("watchedFiles", 0) or 0),
        )
        decision = BuildDecision(True, "Forced rebuild requested (fingerprint scan skipped)", cache_hit=False)
    else:
        scan = scan_watch_files(config)
        if previous_fingerprint != scan.digest:
            decision = BuildDecision(True, "Input fingerprint changed", cache_hit=False)
        elif previous_target != target:
            decision = BuildDecision(True, "Build target changed", cache_hit=False)
        elif not output_exists:
            decision = BuildDecision(True, "Build output missing", cache_hit=False)
        else:
            decision = BuildDecision(False, "No changes detected; using cache", cache_hit=True)

    timestamp = dt.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    log_file = LOGS_DIR / f"unity_build_{dt.datetime.now().strftime('%Y%m%d_%H%M%S')}.log"

    summary = {
        "timestamp": timestamp,
        "cacheHit": decision.cache_hit,
        "reason": decision.reason,
        "buildAttempted": decision.should_build,
        "buildSucceeded": decision.cache_hit,
        "buildTarget": target,
        "outputPath": str(output_path),
        "unityExecutable": str(unity_exe),
        "fingerprint": scan.digest,
        "watchedFiles": scan.file_count,
        "durationSeconds": 0.0,
        "logFile": str(log_file),
        "errorCount": 0,
        "warningCount": 0,
        "errorExamples": [],
        "warningExamples": [],
        "failureHint": "",
    }

    if not decision.should_build:
        write_reports(summary)
        print(f"[unity_cached_builder] Cache hit: {decision.reason}")
        print(f"[unity_cached_builder] Output: {output_path}")
        return summary

    output_path.parent.mkdir(parents=True, exist_ok=True)
    unity_project_path = format_path_for_unity(PROJECT_ROOT, unity_exe)
    unity_log_path = format_path_for_unity(log_file, unity_exe)
    unity_output_path = format_path_for_unity(output_path, unity_exe)

    command = [
        str(unity_exe),
        "-batchmode",
        "-quit",
        "-projectPath",
        unity_project_path,
        "-executeMethod",
        EXECUTE_METHOD,
        "-logFile",
        unity_log_path,
    ]

    if use_nographics:
        command.insert(3, "-nographics")

    env = os.environ.copy()
    scene_value = str(scene_path)
    scene_as_path = Path(scene_path).expanduser()
    if scene_as_path.is_absolute():
        scene_value = format_path_for_unity(scene_as_path, unity_exe)
    env["CDE2501_BUILD_TARGET"] = str(target)
    env["CDE2501_BUILD_OUTPUT"] = unity_output_path
    env["CDE2501_SCENE_PATH"] = scene_value
    env["CDE2501_DEVELOPMENT_BUILD"] = "1" if dev_build else "0"

    print("[unity_cached_builder] Running Unity batch build...")
    print("[unity_cached_builder] " + " ".join(command))

    start = time.time()
    process = subprocess.run(command, cwd=str(PROJECT_ROOT), env=env, check=False)
    duration = time.time() - start

    error_count, warning_count, errors, warnings = analyze_log(log_file)
    succeeded = process.returncode == 0 and output_path.exists()

    summary.update(
        {
            "buildSucceeded": succeeded,
            "durationSeconds": round(duration, 3),
            "errorCount": error_count,
            "warningCount": warning_count,
            "errorExamples": errors,
            "warningExamples": warnings,
        }
    )

    if not succeeded:
        joined_errors = "\n".join(errors).lower()
        if "access token is unavailable" in joined_errors or "licensing" in joined_errors:
            summary["failureHint"] = "Unity license token unavailable. Open Unity Hub, sign in, then rerun."
        else:
            summary["failureHint"] = "Close any Unity Editor instance using this project, then rerun cached build."

    if succeeded:
        fingerprint_for_state = scan.digest
        watched_files_for_state = scan.file_count
        if force and previous_fingerprint:
            # Preserve last known fingerprint metadata when force skips scan.
            fingerprint_for_state = str(previous_fingerprint)
            watched_files_for_state = int(state.get("watchedFiles", watched_files_for_state) or watched_files_for_state)

        new_state = {
            "fingerprint": fingerprint_for_state,
            "buildTarget": target,
            "outputPath": str(output_path),
            "lastBuildTime": timestamp,
            "lastLogFile": str(log_file),
            "watchedFiles": watched_files_for_state,
        }
        save_state(new_state)

    write_reports(summary)
    return summary


def launch_editor(unity_exe: Path) -> None:
    command = [str(unity_exe), "-projectPath", format_path_for_unity(PROJECT_ROOT, unity_exe)]
    subprocess.Popen(command, cwd=str(PROJECT_ROOT))
    print("[unity_cached_builder] Unity Editor launched.")


def print_status(config: Dict) -> int:
    state = load_state()
    print("[unity_cached_builder] Project root:", PROJECT_ROOT)
    print("[unity_cached_builder] Config path:", CONFIG_PATH)
    print("[unity_cached_builder] Cache state path:", STATE_PATH)
    print("[unity_cached_builder] Cached fingerprint:", state.get("fingerprint", "<none>"))
    print("[unity_cached_builder] Last build time:", state.get("lastBuildTime", "<none>"))
    print("[unity_cached_builder] Last output:", state.get("outputPath", "<none>"))
    print("[unity_cached_builder] Last report:", LATEST_REPORT_PATH)

    unity_exe = resolve_unity_executable(config, None)
    print("[unity_cached_builder] Unity executable:", unity_exe if unity_exe else "<not found>")

    scan = scan_watch_files(config)
    print("[unity_cached_builder] Current fingerprint:", scan.digest)
    print("[unity_cached_builder] Watched files:", scan.file_count)
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Cached Unity build + launch orchestrator")
    parser.add_argument("--force", action="store_true", help="Force rebuild even if cache says unchanged")
    parser.add_argument("--launch-editor", action="store_true", help="Launch Unity Editor after build/check")
    parser.add_argument("--skip-build", action="store_true", help="Skip batch build/check and only launch editor")
    parser.add_argument("--status", action="store_true", help="Print cache + fingerprint status and exit")
    parser.add_argument("--print-report", action="store_true", help="Print latest markdown build report and exit")
    parser.add_argument("--unity-exe", type=str, default=None, help="Override Unity executable path")
    parser.add_argument("--target", type=str, default=None, help="Override build target")
    parser.add_argument("--output", type=str, default=None, help="Override build output path")
    parser.add_argument("--scene", type=str, default=None, help="Override scene path")
    parser.add_argument("--development", action="store_true", help="Override: create development build")
    parser.add_argument("--release", action="store_true", help="Override: create non-development build")

    args = parser.parse_args()

    config = load_config()

    if args.print_report:
        if LATEST_REPORT_PATH.exists():
            print(LATEST_REPORT_PATH.read_text(encoding="utf-8", errors="ignore"))
            return 0
        print(f"[unity_cached_builder] No report found at {LATEST_REPORT_PATH}")
        return 1

    if args.status:
        return print_status(config)

    unity_exe = resolve_unity_executable(config, args.unity_exe)
    if unity_exe is None:
        print("[unity_cached_builder] Unity executable not found.")
        print("[unity_cached_builder] Set it in scripts/unity_cached_builder_config.json (unityExecutable) or UNITY_EXE env var.")
        return 2

    dev_override: Optional[bool] = None
    if args.development and args.release:
        print("[unity_cached_builder] Use either --development or --release, not both.")
        return 2
    if args.development:
        dev_override = True
    elif args.release:
        dev_override = False

    if not args.skip_build:
        summary = build_if_needed(
            unity_exe=unity_exe,
            config=config,
            target_override=args.target,
            output_override=args.output,
            scene_override=args.scene,
            dev_override=dev_override,
            force=args.force,
        )

        if not summary.get("buildSucceeded", False):
            print("[unity_cached_builder] Build failed. See report/log:")
            print("  -", LATEST_REPORT_PATH)
            print("  -", summary.get("logFile"))
            if summary.get("failureHint"):
                print("  - Hint:", summary.get("failureHint"))
            return 1

        print("[unity_cached_builder] Build/check complete.")
        print("[unity_cached_builder] Report:", LATEST_REPORT_PATH)

    if args.launch_editor:
        launch_editor(unity_exe)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
