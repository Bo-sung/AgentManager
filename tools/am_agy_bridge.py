#!/usr/bin/env python3
# Copyright 2026 AgentManager. Thin stdio bridge driving the Antigravity Python SDK so the
# AgentManager (.NET) host can run agy as a STRUCTURED engine (tool calls / thinking / permissions
# / streaming) instead of the text-only ConPTY CLI adapter.
#
# Protocol (one JSON object per line, UTF-8, every line flushed):
#   REQUEST  (stdin, first line): {"prompt","cwd","model","effort","resume_id","save_dir","api_key","approvals"}
#       approvals == "yolo"  -> auto-allow every permission (no round-trip)
#       approvals == "broker"-> ask the host per tool call (default)
#   EVENTS   (stdout): {"type": <event>, ...}  (see emit())
#       assistant_delta {"text"} ; thinking {"text"} ; tool_started {"id","name","input"}
#       tool_result {"id","content","is_error"} ; session_started {"conversation_id"}
#       token_usage {"in","out"} ; turn_completed {"is_error"} ; engine_error {"message"}
#   PERMISSION round-trip (broker): bridge emits permission_request {"id","name","args"}, then
#       BLOCKS reading ONE stdin line {"type":"permission_decision","allow":bool}.
#
# Requirements:  pip install google-antigravity   (ships a compiled platform wheel from PyPI —
# cloning the SDK repo is NOT enough) + a Gemini API key (GEMINI_API_KEY or request.api_key).
# API mode is billed per Gemini API call (pay-as-you-go), unlike the subscription CLI.

import json
import os
import sys
import traceback


def emit(obj):
    """Serialize one event as a single flushed JSON line. Never raises into the SDK loop."""
    try:
        sys.stdout.write(json.dumps(obj, ensure_ascii=False) + "\n")
        sys.stdout.flush()
    except Exception:
        # If stdout is closed (host killed us), give up silently — never crash the SDK loop.
        pass


def readline_json():
    """Read one JSON line from stdin (blocking). Returns None on EOF/empty/parse failure."""
    line = sys.stdin.readline()
    if not line:
        return None
    line = line.strip()
    if not line:
        return None
    try:
        return json.loads(line)
    except Exception:
        return None


def make_permission_handler(auto_allow):
    """Build a policy.ask_user handler. In broker mode it emits permission_request and BLOCKS on a
    stdin permission_decision. In yolo mode it always returns True."""

    def handler(tool_call):
        try:
            name = getattr(tool_call, "name", None) or ""
            args = getattr(tool_call, "args", None)
            cid = getattr(tool_call, "id", None) or name
            args_json = args if isinstance(args, str) else (
                json.dumps(args, ensure_ascii=False) if args is not None else "{}"
            )
        except Exception:
            name, cid, args_json = "tool", "tool", "{}"

        if auto_allow:
            return True

        emit({"type": "permission_request", "id": str(cid), "name": str(name), "args": args_json})
        decision = readline_json()
        if decision is None:
            return False  # host gone / malformed -> deny (safe default)
        return bool(decision.get("allow", False))

    return handler


def build_config(req, tools_module, types_module, policy_module, ask_user_handler):
    """Translate the AM request into a LocalAgentConfig. CapabilitiesConfig enables writes + shell
    (SDK default is read-only); policies wire ask_user(handler) for the broker."""
    # --- model + thinking level ---
    model_name = req.get("model") or None
    effort = (req.get("effort") or "").lower()
    thinking_map = {
        "minimal": "minimal", "low": "low", "medium": "medium", "high": "high",
        "xhigh": "high", "max": "high", "default": None, "none": None, "off": None, "": None,
    }
    thinking_level = thinking_map.get(effort)
    # sdk ThinkingLevel enum is imported lazily (google.antigravity.models.ThinkingLevel)
    gemini_opts = None
    if thinking_level:
        try:
            from google.antigravity.models import ThinkingLevel, GeminiModelOptions
            gemini_opts = GeminiModelOptions(
                thinking_level=ThinkingLevel(thinking_level))
        except Exception:
            gemini_opts = None  # SDK shape changed -> fall back to default thinking

    model_target = None
    if model_name and model_name != "default":
        try:
            from google.antigravity.models import ModelTarget, GeminiAPIEndpoint
            endpoint = GeminiAPIEndpoint(options=gemini_opts)
            model_target = ModelTarget(name=model_name, endpoint=endpoint)
        except Exception:
            model_target = None  # keep SDK default (gemini-3.5-flash)

    # --- capabilities: writes + shell ON (default SDK is read-only) ---
    capabilities = None
    try:
        capabilities = types_module.CapabilitiesConfig()
    except Exception:
        capabilities = None

    # --- policies: broker every mutating command through the host unless yolo ---
    policies = []
    try:
        bt = types_module.BuiltinTools
        # Allow the read + write + shell tools; mutating ones (edit/create/run) ask the host.
        policies.append(policy_module.allow(bt.LIST_DIR.value))
        policies.append(policy_module.allow(bt.VIEW_FILE.value))
        if ask_user_handler is not None:
            policies.append(policy_module.ask_user(bt.EDIT_FILE.value, handler=ask_user_handler))
            policies.append(policy_module.ask_user(bt.CREATE_FILE.value, handler=ask_user_handler))
            policies.append(policy_module.ask_user(bt.RUN_COMMAND.value, handler=ask_user_handler))
        else:
            policies.append(policy_module.allow(bt.EDIT_FILE.value))
            policies.append(policy_module.allow(bt.CREATE_FILE.value))
            policies.append(policy_module.allow(bt.RUN_COMMAND.value))
    except Exception:
        pass

    # --- assemble LocalAgentConfig ---
    from google.antigravity import LocalAgentConfig
    kwargs = {}
    if capabilities is not None:
        kwargs["capabilities"] = capabilities
    if policies:
        kwargs["policies"] = policies
    if model_target is not None:
        kwargs["models"] = [model_target]
    resume_id = req.get("resume_id")
    if resume_id:
        kwargs["conversation_id"] = resume_id
    save_dir = req.get("save_dir")
    if save_dir:
        kwargs["save_dir"] = save_dir
    api_key = req.get("api_key") or os.environ.get("GEMINI_API_KEY")
    if api_key:
        kwargs["api_key"] = api_key
    return LocalAgentConfig(**kwargs)


async def run_turn(req):
    """Drive the SDK Agent for one turn, serializing its async streams to stdout JSON lines."""
    try:
        from google.antigravity import Agent
        from google.antigravity import types
        from google.antigravity.hooks import policy
    except ImportError as ex:
        # The Antigravity SDK is not installed. Tell the host the exact fix so the transcript
        # shows a clear actionable error instead of a bare traceback.
        emit({"type": "engine_error",
              "message": "Antigravity SDK not installed (" + str(ex)
              + "). Install it with:  pip install google-antigravity"})
        emit({"type": "turn_completed", "is_error": True})
        return

    auto_allow = (req.get("approvals") or "broker") == "yolo"
    handler = make_permission_handler(auto_allow)
    config = build_config(req, None, types, policy, handler)

    async with Agent(config) as agent:
        conversation_id = None
        try:
            conversation_id = agent.conversation_id
        except Exception:
            pass
        if conversation_id:
            emit({"type": "session_started", "conversation_id": str(conversation_id)})

        prompt = req.get("prompt") or ""
        response = await agent.chat(prompt)

        # Stream the three structured surfaces. Each yields conversational deltas.
        try:
            async for thought in response.thoughts:
                if thought:
                    emit({"type": "thinking", "text": str(thought)})
        except Exception:
            pass

        # tool calls + their results. Stream the live calls; attempt to pair each with its result
        # via the unified chunks stream when available.
        tool_results = {}
        try:
            async for chunk in response.chunks:
                ctype = type(chunk).__name__
                if ctype == "ToolCall":
                    cid = getattr(chunk, "id", "") or ""
                    cname = getattr(chunk, "name", "") or ""
                    cargs = getattr(chunk, "args", None)
                    args_json = cargs if isinstance(cargs, str) else (
                        json.dumps(cargs, ensure_ascii=False) if cargs is not None else "{}")
                    emit({"type": "tool_started", "id": str(cid), "name": str(cname),
                          "input": args_json})
                elif ctype == "ToolResult":
                    rid = getattr(chunk, "id", "") or ""
                    rcontent = getattr(chunk, "content", None)
                    rtext = rcontent if isinstance(rcontent, str) else (
                        json.dumps(rcontent, ensure_ascii=False) if rcontent is not None else "")
                    rerr = bool(getattr(chunk, "is_error", False))
                    tool_results[str(rid)] = (rtext, rerr)
        except Exception:
            pass

        # text tokens (assistant_delta) — stream the response itself as str tokens.
        final_text = ""
        try:
            async for token in response:
                if token:
                    s = str(token)
                    final_text += s
                    emit({"type": "assistant_delta", "text": s})
        except Exception:
            pass

        # Emit any tool results we collected (paired). Kept after deltas so order is stable.
        for rid, (text, is_err) in tool_results.items():
            emit({"type": "tool_result", "id": rid, "content": text, "is_error": is_err})

        # usage (best-effort — field names vary across SDK versions).
        try:
            usage = getattr(response, "usage", None)
            if usage is None and hasattr(agent, "_conversation"):
                usage = getattr(agent._conversation, "usage", None)
            if usage:
                def _num(o, k):
                    try:
                        return int(getattr(o, k) or 0)
                    except Exception:
                        return 0
                emit({"type": "token_usage", "in": _num(usage, "input_tokens") or _num(usage, "inputTokens"),
                      "out": _num(usage, "output_tokens") or _num(usage, "outputTokens")})
        except Exception:
            pass

    emit({"type": "turn_completed", "is_error": False})


def main():
    req = readline_json()
    if not req:
        emit({"type": "engine_error", "message": "bridge: no request line received on stdin"})
        emit({"type": "turn_completed", "is_error": True})
        return

    # cwd: drive the SDK in the AM worktree so local tools touch the user's repo.
    cwd = req.get("cwd")
    if cwd:
        try:
            os.chdir(cwd)
        except Exception:
            emit({"type": "engine_error",
                  "message": "bridge: cannot change to working directory: " + str(cwd)})

    # API key into env too (some SDK paths read GEMINI_API_KEY only).
    api_key = req.get("api_key")
    if api_key:
        os.environ["GEMINI_API_KEY"] = api_key

    try:
        import asyncio
        asyncio.run(run_turn(req))
    except ImportError as ex:
        emit({"type": "engine_error",
              "message": "bridge: Python stdlib missing asyncio (" + str(ex) + ")"})
        emit({"type": "turn_completed", "is_error": True})
    except Exception as ex:  # noqa: BLE001 — surface every failure to the host, never hang
        emit({"type": "engine_error", "message": "bridge: " + str(ex)})
        try:
            emit({"type": "engine_error",
                  "message": traceback.format_exc(limit=4)})
        except Exception:
            pass
        emit({"type": "turn_completed", "is_error": True})


if __name__ == "__main__":
    main()
