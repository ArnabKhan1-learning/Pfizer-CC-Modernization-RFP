import os
from dotenv import load_dotenv

# ────────────────────────────────────────────────────────────────────────────
# Load environment
# ────────────────────────────────────────────────────────────────────────────

load_dotenv(".env")

PROJECT_ENDPOINT  = os.getenv("AZURE_AI_PROJECT_ENDPOINT")
MODEL_DEPLOYMENT  = os.getenv("AZURE_AI_MODEL_DEPLOYMENT_NAME")
OPENAPI_V3_URL    = os.getenv("FUNCTION_OPENAPI_SCHEMA_URL")
FUNC_UPDATE_URL   = os.getenv("EMPLOYEE_INFO_UPDATE_FUNCTION")
FUNC_VALIDATE_URL = os.getenv("EMPLOYEE_INFO_VALIDATE_FUNCTION")
ASSISTANT_NAME    = os.getenv("AGENT_NAME", "Employee-Assistance")