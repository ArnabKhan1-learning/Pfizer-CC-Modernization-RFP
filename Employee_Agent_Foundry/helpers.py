import json
import copy
import urllib.request
from urllib.parse import urlparse
from typing import Dict
from azure.ai.agents.aio import AgentsClient as AsyncAgentsClient

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

# ────────────────────────────────────────────────────────────────────────────
# SDK compatibility helpers
# ────────────────────────────────────────────────────────────────────────────
async def create_agent_compat(project_client, *, model: str, name: str, description: str, instructions: str, tools):
    """
    - Newer SDKs: project_client.agents.create_agent(...)
    - 2.0.0b1:    project_client.agents.create(name=..., definition={"kind":"agent", ...})
    """
    if hasattr(project_client.agents, "create_agent"):
        return await project_client.agents.create_agent(
            model=model,
            name=name,
            description=description,
            instructions=instructions,
            tools=tools
        )
    # Fallback to 2.0.0b1 shape
    definition = {
        "kind": "agent",
        "model": model,
        "description": description,
        "instructions": instructions,
        "tools": tools
    }
    return await project_client.agents.create(name=name, definition=definition)

async def create_thread_compat(agents_client: AsyncAgentsClient):
    """
    - Newer SDKs: agents_client.create_thread()
    - Older variants: agents_client.threads.create()
    """
    if hasattr(agents_client, "create_thread"):
        return await agents_client.create_thread()
    # Fallback path if threads namespace exists
    if hasattr(agents_client, "threads") and hasattr(agents_client.threads, "create"):
        return await agents_client.threads.create()
    # If neither exists, return None; ChatAgent may still create internally (but we prefer explicit)
    return None