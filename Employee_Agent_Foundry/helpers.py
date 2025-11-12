import json
import copy
import urllib.request
from urllib.parse import urlparse
from typing import Dict

# ────────────────────────────────────────────────────────────────────────────
# Helpers
# ────────────────────────────────────────────────────────────────────────────
def load_openapi(oas_url: str) -> Dict:
    with urllib.request.urlopen(oas_url) as resp:
        return json.loads(resp.read().decode("utf-8"))

def normalize_spec_path_from_url(func_url: str) -> str:
    """
    Convert function URL path to the OpenAPI 'paths' key.
    Azure Functions specs often omit the '/api' prefix, so we normalize.
    """
    path = urlparse(func_url).path or "/"
    if path.startswith("/api/"):
        path = path[len("/api"):]
    elif path == "/api":
        path = "/"
    if not path.startswith("/"):
        path = "/" + path
    return path

def single_path_spec(full_spec: Dict, path_key: str) -> Dict:
    """Return a copy of the spec containing only one path; keep components intact."""
    spec = copy.deepcopy(full_spec)
    paths = spec.get("paths", {})
    if path_key not in paths:
        alt_key = "/api" + path_key if not path_key.startswith("/api") else path_key[4:]
        if alt_key in paths:
            path_key = alt_key
        else:
            raise RuntimeError(f"Path '{path_key}' not found in OpenAPI spec (also tried '{alt_key}').")
    spec["paths"] = {path_key: paths[path_key]}
    return spec