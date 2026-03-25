@echo off
setlocal
cd /d "%~dp0"

where py >nul 2>nul
if %errorlevel%==0 (
    py -3 scripts\unity_cached_builder.py --launch-editor %*
) else (
    python scripts\unity_cached_builder.py --launch-editor %*
)

endlocal
