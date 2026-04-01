---
marp: true
theme: default
paginate: true
---

# CDE2501 AR Wayfinding
## How the Program Works
**An AR navigation MVP prioritizing elderly and wheelchair-accessible routing.**

---

## 1. Project Overview

- **What it is:** An Augmented Reality (AR) wayfinding application built in **Unity (2022.3)**.
- **Target Areas:** Queenstown estate and NUS Engineering campus.
- **Goal:** Provide safe, elderly-first, and barrier-free routing using real-time GPS and compass data.
- **Cross-Platform:** Targets Android (ARCore) and iOS (ARKit). 

---

## 2. Core Features

- **AR Directional Guidance:** AR arrows overlaid on the camera feed to guide users step-by-step to the destination.
- **Minimap Overlay:** Top-down view highlighting the generated path and destination markers.
- **Multi-Area Support:** Dynamic switching between local map graphs (e.g., NUS vs Queenstown).
- **Safety-Weighted Routing:** Different navigation profiles (e.g., Elderly walking at 1.0 m/s vs Wheelchair at 0.8 m/s).
- **Telemetry Recording:** Records live GPS, compass, and navigation data to CSV for analytical review.

---

## 3. How Routing Works (The Brain)

- **Algorithm:** Uses a **Weighted A* (A-Star)** pathfinding algorithm to compute the optimal route over a local node-edge graph.
- **Graphs:** The graph data (`estate_graph.json`) contains nodes (points) and edges (walkable paths connecting points).
- **Profiles:** Routing evaluates edges based on "Safety Profiles" (e.g., avoiding stairs for wheelchairs).
- **Destination Select:** Users pick a destination, the system anchors to the closest start node, and computes the path.

---

## 4. Data Generation Pipeline (Offline)

The program relies on offline Python scripts to generate usable map data for Unity:
- **Map Tiles:** Fetches tiles from the **OneMap API** (Singapore localized data), Google Maps, or OpenStreetMap (OSM).
- **Graph Generation:** Scripts like `generate_osm_graph_from_kml.py` parse raw area boundaries (KML files) and calculate interconnected nodes & edges.
- **Street View:** Can download Street View panoramas corresponding to the routes for a pre-walk preview.

---

## 5. Locomotion & Sensors (Runtime)

How the app knows where you are:
- **Device Sensors:** Uses the phone's native GPS (`GPSManager.cs`) and Compass (`CompassManager.cs`) heavily smoothed to reduce jitter.
- **Simulation Mode:** For development on laptops, the app includes a robust simulation mode (WASD to walk, Q/E to rotate) overriding the physical sensors.
- **Auto-Refresh:** The system continually compares current GPS position to the route. If the user strays too far (> 2.0m off-path), the route auto-recalculates.

---

## 6. Telemetry & Field Testing

To validate the safety of the routes:
- **Built-in Recorder:** Testers can toggle `Rec: ON/OFF` directly from the Quick Start UI.
- **Data Logged:** Captures `Time, Lat, Lon, Heading, StartNode, Destination, RouteDistance` into local device CSV files.
- **Post-analysis:** This data allows developers to measure actual walking paths against computed AR-guided paths to refine edge weights.

---

## 7. Summary of the User Journey

1. **Launch:** App reads offline map graph and locations into memory.
2. **Context:** App locks onto GPS/Compass and determines nearest graph node.
3. **Selection:** User selects a destination via the UI or map.
4. **Computation:** Weighted A* pathfinder determines safest path based on current accessibility profile.
5. **Guidance:** 3D AR arrows render in the physical world; Minimap shows global progress; ETA is updated continuously.
6. **Arrival:** Guidance ends upon reaching the destination node threshold.
