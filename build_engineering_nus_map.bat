@echo off
echo Building NUS map atlas from KML (OneMap provider)...
python scripts\generate_map_atlas_from_kml.py --kml "CDE2501 NUS map.kml" --polygon-name "Map of Engine" --zoom-levels 18,19 --output-prefix nus_map --provider onemap --out-dir Assets\StreamingAssets\Data
echo.
echo Building NUS road graph + locations from KML...
python scripts\generate_osm_graph_from_kml.py --kml "CDE2501 NUS map.kml" --polygon-name "Map of Engine" --graph-output "Assets\StreamingAssets\Data\nus_estate_graph.json" --locations-output "Assets\StreamingAssets\Data\nus_locations.json" --boundary-output "Assets\StreamingAssets\Data\nus_boundary.geojson" --area-name "NUS Engineering Wayfinding" --anchor-name "NUS Engineering Anchor" --node-prefix NUS --raw-cache "Docs\nus_osm_raw.json" --through-building-corridors
echo.
echo Building Queenstown map atlas from KML (OneMap provider)...
python scripts\generate_map_atlas_from_kml.py --kml "cde2501.kml" --polygon-name "Site area" --zoom-levels 18,19 --output-prefix queenstown_map --provider onemap --out-dir Assets\StreamingAssets\Data
echo Done!
pause
