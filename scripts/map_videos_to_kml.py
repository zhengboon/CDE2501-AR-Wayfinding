import json
import math
import xml.etree.ElementTree as ET
import os

EARTH_RADIUS_M = 6378137.0
ANCHOR_LAT = 1.294550851849307
ANCHOR_LON = 103.8060771559821

def local_to_latlon(x, z):
    d_lat = z / EARTH_RADIUS_M
    lat = math.degrees(d_lat) + ANCHOR_LAT
    d_lon = x / (EARTH_RADIUS_M * math.cos(math.radians(ANCHOR_LAT)))
    lon = math.degrees(d_lon) + ANCHOR_LON
    return lat, lon

def main():
    workspace = r'c:\Users\zheng\CDE2501-AR-Wayfinding'
    graph_path = os.path.join(workspace, 'Assets', 'StreamingAssets', 'Data', 'estate_graph.json')
    video_map_path = os.path.join(workspace, 'Assets', 'StreamingAssets', 'Data', 'video_frame_map.json')
    kml_path = os.path.join(workspace, 'cde2501.kml')
    
    with open(graph_path, 'r', encoding='utf-8') as f:
        graph = json.load(f)
    
    nodes = {n['id']: n['position'] for n in graph['nodes']}
    
    with open(video_map_path, 'r', encoding='utf-8') as f:
        vmap = json.load(f)
        
    ET.register_namespace('', "http://www.opengis.net/kml/2.2")
    ET.register_namespace('gx', "http://www.google.com/kml/ext/2.2")
    ET.register_namespace('kml', "http://www.opengis.net/kml/2.2")
    ET.register_namespace('atom', "http://www.w3.org/2005/Atom")
    
    tree = ET.parse(kml_path)
    root = tree.getroot()
    doc = root.find('{http://www.opengis.net/kml/2.2}Document')
    if doc is None:
        doc = root
        
    folder = ET.SubElement(doc, '{http://www.opengis.net/kml/2.2}Folder')
    name = ET.SubElement(folder, '{http://www.opengis.net/kml/2.2}name')
    name.text = "Mapped Videos"
    
    for v in vmap.get('videos', []):
        title = v.get('title', 'Unknown Video')
        url = v.get('url', '')
        path = v.get('routeNodePath', [])
        
        coords = []
        for nid in path:
            if nid in nodes:
                pos = nodes[nid]
                lat, lon = local_to_latlon(pos['x'], pos['z'])
                ele = pos.get('y', 0)
                coords.append(f"{lon},{lat},{ele}")
                
        if not coords:
            for f in v.get('frames', []):
                pos = f.get('position')
                if pos:
                    lat, lon = local_to_latlon(pos['x'], pos['z'])
                    ele = pos.get('y', 0)
                    coords.append(f"{lon},{lat},{ele}")
                    
        if coords:
            pm = ET.SubElement(folder, '{http://www.opengis.net/kml/2.2}Placemark')
            pm_name = ET.SubElement(pm, '{http://www.opengis.net/kml/2.2}name')
            pm_name.text = title
            
            desc = ET.SubElement(pm, '{http://www.opengis.net/kml/2.2}description')
            desc.text = f'<a href="{url}">{url}</a>'
            
            style = ET.SubElement(pm, '{http://www.opengis.net/kml/2.2}Style')
            lstyle = ET.SubElement(style, '{http://www.opengis.net/kml/2.2}LineStyle')
            color = ET.SubElement(lstyle, '{http://www.opengis.net/kml/2.2}color')
            color.text = "ff0000ff" # Red line in KML (AABBGGRR) -> ff(alpha) 00(B) 00(G) ff(R)
            width = ET.SubElement(lstyle, '{http://www.opengis.net/kml/2.2}width')
            width.text = "4"
            
            ls = ET.SubElement(pm, '{http://www.opengis.net/kml/2.2}LineString')
            # clamp to ground vs absolute elevation based
            alt_mode = ET.SubElement(ls, '{http://www.opengis.net/kml/2.2}altitudeMode')
            alt_mode.text = "relativeToGround" # Since Y might be relative elevation in game space
            
            tess = ET.SubElement(ls, '{http://www.opengis.net/kml/2.2}tessellate')
            tess.text = "1"
            coord_el = ET.SubElement(ls, '{http://www.opengis.net/kml/2.2}coordinates')
            coord_el.text = " ".join(coords)

    tree.write(kml_path, encoding='utf-8', xml_declaration=True)
    print("Successfully mapped videos to KML.")

if __name__ == '__main__':
    main()
