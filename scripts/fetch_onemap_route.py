#!/usr/bin/env python3
import argparse
import json
import urllib.parse
import urllib.request
from typing import Any, Dict

ONEMAP_ROUTING_URL = "https://www.onemap.gov.sg/api/public/routingsvc/route"


def fetch_route(start: str, end: str, route_type: str, token: str) -> Dict[str, Any]:
    params = {
        "start": start,
        "end": end,
        "routeType": route_type,
        "token": token
    }
    
    if route_type == "pt":
        params["date"] = "01-01-2026"
        params["time"] = "12:00:00"
        params["mode"] = "TRANSIT"
        
    query_string = urllib.parse.urlencode(params)
    url = f"{ONEMAP_ROUTING_URL}?{query_string}"
    
    print(f"Fetching route from OneMap API: {url.replace(token, 'HIDDEN_TOKEN')}")
    
    request = urllib.request.Request(url)
    try:
        with urllib.request.urlopen(request) as response:
            return json.loads(response.read().decode("utf-8"))
    except Exception as e:
        print(f"Error fetching route: {e}")
        return {}


def main() -> int:
    parser = argparse.ArgumentParser(description="Fetch routes from OneMap API.")
    parser.add_argument("--start", required=True, help="Start coordinate (lat,lon) e.g., 1.296568,103.773253")
    parser.add_argument("--end", required=True, help="End coordinate (lat,lon) e.g., 1.298701,103.771212")
    parser.add_argument("--route-type", choices=["walk", "drive", "pt", "cycle", "wheelchair"], default="walk", help="Type of route. Note: 'wheelchair' / Barrier-Free Access requires an approved token from go.gov.sg/bfa-enquires.")
    parser.add_argument("--token", required=True, help="OneMap API Token")
    parser.add_argument("--output", default="route_result.json", help="Output JSON file path")
    args = parser.parse_args()

    # Note: BFA endpoint might be mapped differently via their backend, typically it modifies walk or uses a specialized endpoint.
    # For now, we query the main API point, unless the user configures the dedicated BFA route api url explicitly.
    data = fetch_route(args.start, args.end, args.route_type, args.token)
    
    if data:
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
        print(f"Route saved to {args.output}")
        if "route_geometry" in data:
            print("Successfully retrieved route geometry.")
        
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
