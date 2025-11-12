import os
import json
import time
import logging
import requests
import azure.functions as func
from azure.identity import DefaultAzureCredential

# ---------------- helpers ----------------

def _env(name: str, default: str | None = None, required: bool = False) -> str | None:
    v = os.getenv(name, default)
    if required and not v:
        raise RuntimeError(f"Missing env var: {name}")
    return v

def _bearer_token() -> str:
    # Uses az login locally; Managed Identity in Azure
    cred = DefaultAzureCredential()
    tok = cred.get_token("https://ai.azure.com/.default")
    return tok.token

def _latest_assistant_text(messages_payload: dict) -> str:
    data = (messages_payload or {}).get("data", [])
    # newest assistant first
    assistant = sorted(
        [m for m in data if m.get("role") == "assistant"],
        key=lambda m: m.get("created_at", 0),
        reverse=True
    )
    if not assistant:
        return ""
    content = (assistant[0] or {}).get("content") or []
    if not content:
        return ""
    # text may appear as {"text":{"value":"..."}} or "text":"..."
    first = content[0]
    t = first.get("text")
    if isinstance(t, dict):
        return t.get("value") or ""
    if isinstance(t, str):
        return t
    return ""

# ---------------- function app ----------------

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)

@app.route(route="chat", methods=["POST"])
def chat(req: func.HttpRequest) -> func.HttpResponse:
    """POST /api/chat
    Body: {"prompt":"...", "thread_id":"thread_...(optional)"}
    Returns: {"thread_id","run_id","status","answer"}
    """
    # --- robust JSON parsing (fixes silent=True issue) ---
    body = {}
    try:
        body = req.get_json()  # no 'silent' parameter
    except ValueError:
        try:
            raw = (req.get_body() or b"").decode("utf-8")
            if raw.strip():
                body = json.loads(raw)
        except Exception:
            body = {}

    prompt = (body.get("prompt") or "").strip()
    thread_id = (body.get("thread_id") or "").strip()

    # Optional: tiny API key gate for PoC
    require_key = (_env("REQUIRE_X_API_KEY", "false").lower() == "true")
    if require_key:
        provided = req.headers.get("x-api-key", "")
        expected = _env("X_API_KEY", "")
        if not expected or provided != expected:
            return func.HttpResponse("Missing or invalid x-api-key", status_code=401)

    if not prompt:
        return func.HttpResponse(
            json.dumps({"error": "Provide 'prompt'. Optional: 'thread_id'."}),
            status_code=400, mimetype="application/json"
        )

    base = _env("PROJECT_BASE", required=True).rstrip("/")
    api_version = _env("API_VERSION", "v1")
    agent_id = _env("AGENT_ID", required=True)

    token = _bearer_token()
    headers = {
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json"
    }

    try:
        # 1) Create a thread if not supplied
        if not thread_id:
            r = requests.post(
                f"{base}/threads",
                params={"api-version": api_version},
                headers=headers,
                json={}
            )
            r.raise_for_status()
            thread_id = r.json().get("id")
            if not thread_id:
                raise RuntimeError("No thread_id returned from create thread")

        # 2) Add the user message
        r = requests.post(
            f"{base}/threads/{thread_id}/messages",
            params={"api-version": api_version},
            headers=headers,
            json={"role": "user", "content": prompt}
        )
        r.raise_for_status()

        # 3) Run the agent
        r = requests.post(
            f"{base}/threads/{thread_id}/runs",
            params={"api-version": api_version},
            headers=headers,
            json={"assistant_id": agent_id}
        )
        r.raise_for_status()
        run = r.json()
        run_id = run.get("id")
        status = run.get("status", "queued")

        # 4) Poll until terminal
        deadline = time.time() + 120  # ~2 minutes
        while status in ("queued", "in_progress", "requires_action"):
            if time.time() > deadline:
                raise TimeoutError("Run polling timed out")
            time.sleep(0.9)
            rr = requests.get(
                f"{base}/threads/{thread_id}/runs/{run_id}",
                params={"api-version": api_version},
                headers=headers
            )
            rr.raise_for_status()
            status = rr.json().get("status", "")

        # 5) If completed, fetch messages and return latest assistant text
        answer = ""
        if status == "completed":
            mm = requests.get(
                f"{base}/threads/{thread_id}/messages",
                params={"api-version": api_version},
                headers=headers
            )
            mm.raise_for_status()
            answer = _latest_assistant_text(mm.json())

        result = {
            "thread_id": thread_id,
            "run_id": run_id,
            "status": status,
            "answer": answer
        }
        return func.HttpResponse(json.dumps(result), status_code=200, mimetype="application/json")

    except requests.HTTPError as e:
        logging.exception("HTTP error calling Agent backend")
        text = e.response.text if e.response is not None else str(e)
        code = e.response.status_code if e.response is not None else 500
        return func.HttpResponse(
            json.dumps({"error": f"Backend HTTP {code}", "details": text}),
            status_code=500, mimetype="application/json"
        )
    except Exception as e:
        logging.exception("Unhandled error")
        return func.HttpResponse(
            json.dumps({"error": str(e)}),
            status_code=500, mimetype="application/json"
        )
