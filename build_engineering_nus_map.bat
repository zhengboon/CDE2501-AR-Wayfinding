@echo off
echo Building NUS map atlas from KML (Google provider, OSM fallback)...
echo Tip: set GOOGLE_MAPS_API_KEY and enable billing + Map Tiles API to avoid 403.
python scripts\generate_map_atlas_from_kml.py --kml "CDE2501 NUS map.kml" --polygon-name "Map of Engine" --zoom-levels 18,19 --output-prefix nus_map --provider google --fallback-to-osm --out-dir Assets\StreamingAssets\Data
echo.
echo Building Queenstown map atlas from KML (Google provider, OSM fallback)...
python scripts\generate_map_atlas_from_kml.py --kml "cde2501.kml" --polygon-name "Site area" --zoom-levels 18,19 --output-prefix queenstown_map --provider google --fallback-to-osm --out-dir Assets\StreamingAssets\Data
echo Done!
pause
