# Entry point — uses Microsoft Agent Framework end-to-end (unchanged behavior)

import asyncio
from typing import List

# ── Azure identity (async) & Azure AI SDKs
from azure.identity.aio import AzureCliCredential
from azure.ai.projects.aio import AIProjectClient                  # <-- Agent creation (Project-scoped)
from azure.ai.agents.aio import AgentsClient as AsyncAgentsClient  # <-- Async Agents client

# ── Microsoft Agent Framework (core)
from agent_framework.azure import AzureAIAgentClient               # <-- Framework chat client for Azure AI Foundry
from agent_framework import ChatAgent                              # <-- Framework high-level chat orchestrator

# ── Tool modeling (OpenAPI tools)
from azure.ai.agents.models import OpenApiTool, OpenApiAuthDetails

# Local modules (same logic split for clarity)
from config import (
PROJECT_ENDPOINT,
MODEL_DEPLOYMENT,
OPENAPI_V3_URL,
FUNC_UPDATE_URL,
FUNC_VALIDATE_URL,
ASSISTANT_NAME,
)
from instructions import ASSISTANT_DESCRIPTION, ASSISTANT_INSTRUCTIONS
from helpers import load_openapi, normalize_spec_path_from_url, single_path_spec, create_agent_compat, create_thread_compat

# ────────────────────────────────────────────────────────────────────────────
# Main — FULL Agent Framework usage
# ────────────────────────────────────────────────────────────────────────────
async def main():
    # Guardrails
    missing = [k for k, v in {
        "AZURE_AI_PROJECT_ENDPOINT": PROJECT_ENDPOINT,
        "AZURE_AI_MODEL_DEPLOYMENT_NAME": MODEL_DEPLOYMENT,
        "FUNCTION_OPENAPI_SCHEMA_URL": OPENAPI_V3_URL,
        "EMPLOYEE_INFO_VALIDATE_FUNCTION": FUNC_VALIDATE_URL,
        "EMPLOYEE_INFO_UPDATE_FUNCTION": FUNC_UPDATE_URL,
    }.items() if not v]
    if missing:
        raise SystemExit(f"Missing variables in .env: {', '.join(missing)}")

    print("Using configuration (.env):")
    print(f"  Project Endpoint : {PROJECT_ENDPOINT}")
    print(f"  Model Deployment : {MODEL_DEPLOYMENT}")
    print(f"  OpenAPI v3 URL   : {OPENAPI_V3_URL}")
    print(f"  Validate URL     : {FUNC_VALIDATE_URL}")
    print(f"  Update URL       : {FUNC_UPDATE_URL}")

    # Load OpenAPI once; slice into per-operation specs
    full_spec = load_openapi(OPENAPI_V3_URL)
    validate_path = normalize_spec_path_from_url(FUNC_VALIDATE_URL)
    update_path   = normalize_spec_path_from_url(FUNC_UPDATE_URL)

    SPEC_VALIDATE = single_path_spec(full_spec, validate_path)
    SPEC_UPDATE   = single_path_spec(full_spec, update_path)

    # Build OpenAPI tools (anonymous, PoC)
    anon_auth = OpenApiAuthDetails(type="anonymous")
    tool_validate = OpenApiTool(
        name="EmployeeValidation",
        description="Validate employee via POST /ValidateEmployeeProfile (employee_id, first_name, last_name).",
        spec=SPEC_VALIDATE,
        auth=anon_auth,
    )
    tool_update = OpenApiTool(
        name="EmployeeUpdate",
        description="Update profile via POST /UpdateEmployeeProfile (employee_id + fields to change).",
        spec=SPEC_UPDATE,
        auth=anon_auth,
    )
    tool_definitions = []
    tool_definitions.extend(tool_validate.definitions)
    tool_definitions.extend(tool_update.definitions)
    print(f"\nTool definitions prepared: {len(tool_definitions)}")

    # ── Agent Framework + Azure AI Foundry (ASYNC) ──────────────────────────
    async with AzureCliCredential() as credential:
        # (A) Create the assistant via the **Project-scoped** client
        async with AIProjectClient(endpoint=PROJECT_ENDPOINT, credential=credential) as project_client:
            print("\n📋 Creating assistant in Azure AI Foundry (via Project client)...")
            assistant = await create_agent_compat(
                project_client,
                model=MODEL_DEPLOYMENT,
                name=ASSISTANT_NAME,
                description=ASSISTANT_DESCRIPTION,
                instructions=ASSISTANT_INSTRUCTIONS,
                tools=tool_definitions,
            )
            print("✅ Assistant created")
            print(f"   Name : {getattr(assistant, 'name', '')}")
            print(f"   Id   : {getattr(assistant, 'id', '')}")  # asst_...
            print(f"   Model: {getattr(assistant, 'model', '')}")

            # (B) Open an **async Agents client** and pass it to the **Agent Framework** chat client.
            async with AsyncAgentsClient(endpoint=PROJECT_ENDPOINT, credential=credential) as agents_client:
                # Create a PERSISTENT THREAD once and reuse it for the whole interactive loop
                thread = await create_thread_compat(agents_client)
                thread_id = getattr(thread, "id", None) if thread else None

                # ── AzureAIAgentClient ties the framework to Azure AI Foundry Agents
                # Pass thread_id when available to avoid accidental new-thread runs.
                if thread_id and "thread_id" in AzureAIAgentClient.__init__.__code__.co_varnames:
                    framework_chat_client = AzureAIAgentClient(
                        agents_client=agents_client,
                        agent_id=assistant.id,
                        thread_id=thread_id
                    )
                else:
                    framework_chat_client = AzureAIAgentClient(
                        agents_client=agents_client,
                        agent_id=assistant.id
                    )

                # ── ChatAgent = high-level chat orchestrator with streaming
                async with ChatAgent(chat_client=framework_chat_client) as agent:
                    print("\n" + "="*72)
                    print("💬 Interactive Chat via Microsoft Agent Framework (type 'quit' to exit)")
                    print("="*72 + "\n")

                    while True:
                        try:
                            user_input = input("You: ").strip()
                        except (EOFError, KeyboardInterrupt):
                            print("\n👋 Goodbye!")
                            break

                        if not user_input:
                            continue
                        if user_input.lower() in {"quit", "exit", "q"}:
                            print("\n👋 Goodbye!")
                            break

                        # Streaming tokens + tool-call aware events (when available)
                        print("Agent: ", end="", flush=True)
                        async for chunk in agent.run_stream(user_input):
                            if getattr(chunk, "text", None):
                                print(chunk.text, end="", flush=True)
                        print()  # newline


if __name__ == "__main__":
    asyncio.run(main())
