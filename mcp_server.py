from __future__ import annotations

import argparse
import contextlib
import importlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Any, Callable

MCP_VERSION = "2024-11-05"
MAX_OUTPUT_CHARS = 40000
DEFAULT_TIMEOUT_SECONDS = 600
MANIFEST_NAMES = [
    "pyproject.toml",
    "setup.py",
    "package.json",
    "go.mod",
    "Cargo.toml",
    "requirements.txt",
    "Makefile",
    "README.md",
]
SKIP_DIR_NAMES = {
    ".git",
    ".venv",
    "__pycache__",
    "node_modules",
    "dist",
    "build",
    ".idea",
    ".pytest_cache",
    ".mypy_cache",
}

NO_ARG_SCHEMA = {"type": "object", "properties": {}}
LIST_DIRECTORY_SCHEMA = {
    "type": "object",
    "properties": {
        "path": {
            "type": "string",
            "description": "Relative path inside the repository. Defaults to '.'.",
        },
        "recursive": {
            "type": "boolean",
            "description": "When true, walk subdirectories recursively.",
            "default": False,
        },
        "max_entries": {
            "type": "integer",
            "description": "Maximum number of results to return. Defaults to 200.",
            "default": 200,
        },
    },
}
READ_TEXT_SCHEMA = {
    "type": "object",
    "properties": {
        "path": {
            "type": "string",
            "description": "Relative path to a text file inside the repository.",
        },
        "start_line": {
            "type": "integer",
            "description": "1-based inclusive start line. Defaults to 1.",
            "default": 1,
        },
        "end_line": {
            "type": "integer",
            "description": "1-based inclusive end line. Defaults to 200.",
            "default": 200,
        },
    },
    "required": ["path"],
}
SEARCH_TEXT_SCHEMA = {
    "type": "object",
    "properties": {
        "query": {
            "type": "string",
            "description": "Search pattern. Interpreted as regex when is_regex is true.",
        },
        "path": {
            "type": "string",
            "description": "Relative file or directory path to search. Defaults to '.'.",
        },
        "is_regex": {
            "type": "boolean",
            "description": "Treat the query as a regular expression.",
            "default": False,
        },
        "max_matches": {
            "type": "integer",
            "description": "Maximum number of matches to return. Defaults to 50.",
            "default": 50,
        },
    },
    "required": ["query"],
}
MAKE_DIRECTORY_SCHEMA = {
    "type": "object",
    "properties": {
        "path": {
            "type": "string",
            "description": "Relative directory path to create.",
        },
    },
    "required": ["path"],
}
CREATE_FILE_SCHEMA = {
    "type": "object",
    "properties": {
        "path": {
            "type": "string",
            "description": "Relative file path to create.",
        },
        "content": {
            "type": "string",
            "description": "UTF-8 text content for the new file.",
        },
    },
    "required": ["path", "content"],
}
WRITE_TEXT_SCHEMA = {
    "type": "object",
    "properties": {
        "path": {
            "type": "string",
            "description": "Relative file path to write.",
        },
        "content": {
            "type": "string",
            "description": "UTF-8 text content to write.",
        },
        "append": {
            "type": "boolean",
            "description": "Append to the file instead of replacing it.",
            "default": False,
        },
        "create_if_missing": {
            "type": "boolean",
            "description": "Create the file if it does not exist.",
            "default": True,
        },
    },
    "required": ["path", "content"],
}
TASK_SCHEMA = {
    "type": "object",
    "properties": {
        "timeout_seconds": {
            "type": "integer",
            "description": "Maximum runtime in seconds. Defaults to the server's configured value.",
            "default": DEFAULT_TIMEOUT_SECONDS,
        },
    },
}
REPO2QR_GENERATE_QR_SCHEMA = {
    "type": "object",
    "properties": {
        "input_path": {
            "type": "string",
            "description": "Relative path to the input file to encode.",
        },
        "output_dir": {
            "type": "string",
            "description": "Relative directory where QR images will be written.",
        },
        "format": {
            "type": "string",
            "description": "Image format to write: svg or png.",
            "default": "svg",
        },
        "chunk_size": {
            "type": "integer",
            "description": "Payload chunk size before QR packaging. Defaults to 1500.",
            "default": 1500,
        },
        "compress": {
            "type": "boolean",
            "description": "Compress before chunking. Defaults to true.",
            "default": True,
        },
    },
    "required": ["input_path", "output_dir"],
}
RENODE_START_SCHEMA = {
    "type": "object",
    "properties": {
        "script_path": {
            "type": "string",
            "description": "Relative path to a .resc script to run. Optional.",
        },
        "monitor_port": {
            "type": "integer",
            "description": "Optional monitor port passed via -P.",
        },
        "console": {
            "type": "boolean",
            "description": "Run with --console. Defaults to true.",
            "default": True,
        },
        "disable_gui": {
            "type": "boolean",
            "description": "Run with --disable-gui. Defaults to true.",
            "default": True,
        },
    },
}
SCRUTINY_START_SERVER_SCHEMA = {
    "type": "object",
    "properties": {
        "config_path": {
            "type": "string",
            "description": "Optional relative path to a Scrutiny JSON config file.",
        },
        "port": {
            "type": "integer",
            "description": "Optional TCP port passed to the Scrutiny server.",
        },
        "loglevel": {
            "type": "string",
            "description": "Optional Scrutiny server log level.",
        },
    },
}
SCRUTINY_RUNTEST_TARGET_SCHEMA = {
    "type": "object",
    "properties": {
        "target": {
            "type": "string",
            "description": "Optional Scrutiny runtest selector, for example server or server.test_api.",
        },
        "timeout_seconds": {
            "type": "integer",
            "description": "Maximum runtime in seconds.",
            "default": DEFAULT_TIMEOUT_SECONDS,
        },
    },
}


def python_cmd(*args: str) -> list[str]:
    return [sys.executable, *args]


EXPLICIT_REPO_TASKS: dict[str, dict[str, dict[str, Any]]] = {
    "chsm": {
        "install": {
            "description": "Install chsm Python dependencies from requirements.txt.",
            "command": python_cmd("-m", "pip", "install", "-r", "requirements.txt"),
        },
        "test": {
            "description": "Run chsm tests from the root test directory.",
            "command": python_cmd("-m", "pytest", "test"),
        },
    },
    "code_map": {
        "install": {
            "description": "Install code_map in editable mode.",
            "command": python_cmd("-m", "pip", "install", "-e", "."),
        },
        "build": {
            "description": "Run the code_map PowerShell build script.",
            "command": ["powershell", "-ExecutionPolicy", "Bypass", "-File", "build.ps1"],
        },
        "test": {
            "description": "Run code_map tests with pytest.",
            "command": python_cmd("-m", "pytest"),
        },
    },
    "docs_collection": {
        "install": {
            "description": "Install docs_collection with development dependencies.",
            "command": python_cmd("-m", "pip", "install", "-e", ".[dev]"),
        },
        "build": {
            "description": "Build the docs_collection Python package.",
            "command": python_cmd("-m", "build"),
        },
        "test": {
            "description": "Run docs_collection tests with pytest.",
            "command": python_cmd("-m", "pytest"),
        },
        "lint": {
            "description": "Run ruff against docs_collection source and tests.",
            "command": python_cmd("-m", "ruff", "check", "python", "gui", "tests"),
        },
        "typecheck": {
            "description": "Run mypy for docs_collection source directories.",
            "command": python_cmd("-m", "mypy", "python", "gui"),
        },
    },
    "protosim": {
        "install": {
            "description": "Install protosim with test dependencies.",
            "command": python_cmd("-m", "pip", "install", "-e", ".[test]"),
        },
        "build": {
            "description": "Build the protosim Python package.",
            "command": python_cmd("-m", "build"),
        },
        "test": {
            "description": "Run protosim pytest suite.",
            "command": python_cmd("-m", "pytest"),
        },
        "lint": {
            "description": "Run ruff for protosim source and tests.",
            "command": python_cmd("-m", "ruff", "check", "protosim_kicad", "tests"),
        },
        "typecheck": {
            "description": "Run mypy against protosim source.",
            "command": python_cmd("-m", "mypy", "protosim_kicad"),
        },
    },
    "pyontrust": {
        "install": {
            "description": "Install pyontrust with development dependencies.",
            "command": python_cmd("-m", "pip", "install", "-e", ".[dev]"),
        },
        "build": {
            "description": "Build the pyontrust Python package.",
            "command": python_cmd("-m", "build"),
        },
        "test": {
            "description": "Run pyontrust pytest suite.",
            "command": python_cmd("-m", "pytest"),
        },
        "lint": {
            "description": "Run ruff for pyontrust.",
            "command": python_cmd("-m", "ruff", "check", "."),
        },
        "typecheck": {
            "description": "Run mypy against pyontrust.",
            "command": python_cmd("-m", "mypy", "src/pyontrust"),
        },
    },
    "renode": {
        "install": {
            "description": "Install Renode test dependencies from tests/requirements.txt.",
            "command": python_cmd("-m", "pip", "install", "-r", "tests/requirements.txt"),
            "timeout_seconds": 1200,
        },
        "build": {
            "description": "Build Renode from source using the repository build script.",
            "command": ["bash", "./build.sh"],
            "timeout_seconds": 1800,
        },
        "test": {
            "description": "Run Renode tests through the Python test harness.",
            "command": python_cmd("tests/run_tests.py"),
            "timeout_seconds": 1800,
        },
    },
    "repo2qr": {
        "install": {
            "description": "Install repo2qr in editable mode from setup.py.",
            "command": python_cmd("-m", "pip", "install", "-e", "."),
        },
        "build": {
            "description": "Build repo2qr source and wheel distributions.",
            "command": python_cmd("setup.py", "sdist", "bdist_wheel"),
        },
        "test": {
            "description": "Run the repo2qr pytest suite.",
            "command": python_cmd("-m", "pytest", "tests"),
        },
    },
    "route_core": {
        "install": {
            "description": "Install route_core workspace dependencies.",
            "command": ["npm", "install"],
        },
        "build": {
            "description": "Build all route_core workspaces.",
            "command": ["npm", "run", "build"],
        },
        "test": {
            "description": "Run route_core workspace tests.",
            "command": ["npm", "run", "test"],
        },
        "lint": {
            "description": "Run route_core linting.",
            "command": ["npm", "run", "lint"],
        },
        "typecheck": {
            "description": "Run route_core typechecking.",
            "command": ["npm", "run", "typecheck"],
        },
    },
    "scrutiny-embedded": {
        "build": {
            "description": "Build scrutiny-embedded using the repository script.",
            "command": ["bash", "./scripts/build.sh"],
            "timeout_seconds": 1800,
        },
        "test": {
            "description": "Build scrutiny-embedded with tests enabled.",
            "command": ["bash", "./scripts/build.sh"],
            "env": {
                "SCRUTINY_BUILD_TEST": "1",
                "SCRUTINY_BUILD_TESTAPP": "1",
                "CMAKE_BUILD_TYPE": "Debug",
            },
            "timeout_seconds": 1800,
        },
    },
    "scrutiny-main": {
        "install": {
            "description": "Install scrutiny-main with development dependencies.",
            "command": python_cmd("-m", "pip", "install", "-e", ".[dev]"),
        },
        "build": {
            "description": "Build the scrutiny-main Python package.",
            "command": python_cmd("-m", "build"),
        },
        "test": {
            "description": "Run scrutiny-main through its native runtest CLI.",
            "command": python_cmd("-m", "scrutiny", "runtest"),
            "timeout_seconds": 1200,
        },
    },
    "visual_system_designer": {
        "install": {
            "description": "Install visual_system_designer in editable mode.",
            "command": python_cmd("-m", "pip", "install", "-e", "."),
        },
        "build": {
            "description": "Build the visual_system_designer Python package.",
            "command": python_cmd("-m", "build"),
        },
    },
}


class MCPError:
    PARSE_ERROR = -32700
    INVALID_REQUEST = -32600
    METHOD_NOT_FOUND = -32601
    INVALID_PARAMS = -32602
    INTERNAL_ERROR = -32603


def normalize_tool_prefix(repo_name: str) -> str:
    return repo_name.replace("-", "_")


def clip_output(text: str) -> str:
    if len(text) <= MAX_OUTPUT_CHARS:
        return text
    remaining = len(text) - MAX_OUTPUT_CHARS
    return text[:MAX_OUTPUT_CHARS] + f"\n... truncated {remaining} characters"


class RepoMCPServer:
    def __init__(self, repo_root: Path, repo_name: str):
        self.repo_root = repo_root.resolve()
        self.repo_name = repo_name
        self.tool_prefix = normalize_tool_prefix(repo_name)
        self._running = True
        self._tool_handlers: dict[str, Callable[[dict[str, Any]], dict[str, Any]]] = {}
        self._tool_definitions: list[dict[str, Any]] = []
        self._managed_processes: dict[str, dict[str, Any]] = {}
        self._task_definitions = self._infer_task_definitions()
        self._task_definitions.update(EXPLICIT_REPO_TASKS.get(repo_name, {}))
        self._register_tools()

    def serve(self) -> None:
        try:
            while self._running:
                raw_line = sys.stdin.readline()
                if not raw_line:
                    break

                raw_line = raw_line.strip()
                if not raw_line:
                    continue

                try:
                    request = json.loads(raw_line)
                except json.JSONDecodeError as exc:
                    self._write_response(
                        self._make_error(None, MCPError.PARSE_ERROR, f"Parse error: {exc}")
                    )
                    continue

                response = self._handle_request(request)
                if response is not None:
                    self._write_response(response)
        finally:
            self._cleanup_managed_processes()

    def _register_tools(self) -> None:
        prefix = self.tool_prefix
        self._register_tool(
            f"{prefix}_repo_info",
            f"Return high-level metadata and task support for the {self.repo_name} repository.",
            NO_ARG_SCHEMA,
            self._tool_repo_info,
        )
        self._register_tool(
            f"{prefix}_manifest_summary",
            f"List common build and packaging manifests detected in {self.repo_name}.",
            NO_ARG_SCHEMA,
            self._tool_manifest_summary,
        )
        self._register_tool(
            f"{prefix}_list_directory",
            f"List files and directories inside {self.repo_name} with optional recursion.",
            LIST_DIRECTORY_SCHEMA,
            self._tool_list_directory,
        )
        self._register_tool(
            f"{prefix}_read_text_file",
            f"Read a UTF-8 text file from {self.repo_name} with optional line slicing.",
            READ_TEXT_SCHEMA,
            self._tool_read_text_file,
        )
        self._register_tool(
            f"{prefix}_search_text",
            f"Search text inside {self.repo_name} using ripgrep when available.",
            SEARCH_TEXT_SCHEMA,
            self._tool_search_text,
        )
        self._register_tool(
            f"{prefix}_git_status",
            f"Return a concise git status for {self.repo_name}.",
            NO_ARG_SCHEMA,
            self._tool_git_status,
        )
        self._register_tool(
            f"{prefix}_make_directory",
            f"Create a directory inside {self.repo_name}.",
            MAKE_DIRECTORY_SCHEMA,
            self._tool_make_directory,
        )
        self._register_tool(
            f"{prefix}_create_file",
            f"Create a new UTF-8 text file inside {self.repo_name}. Fails if the file already exists.",
            CREATE_FILE_SCHEMA,
            self._tool_create_file,
        )
        self._register_tool(
            f"{prefix}_write_text_file",
            f"Write or append UTF-8 text to a file inside {self.repo_name}.",
            WRITE_TEXT_SCHEMA,
            self._tool_write_text_file,
        )

        for action, task in sorted(self._task_definitions.items()):
            self._register_tool(
                f"{prefix}_{action}",
                task["description"],
                TASK_SCHEMA,
                lambda args, action=action: self._tool_run_task(action, args),
            )

        if self.repo_name == "repo2qr":
            self._register_tool(
                f"{prefix}_generate_qr",
                "Encode a file in repo2qr into numbered QR image files.",
                REPO2QR_GENERATE_QR_SCHEMA,
                self._tool_repo2qr_generate_qr,
            )

        if self.repo_name == "renode":
            self._register_tool(
                f"{prefix}_start",
                "Start Renode against an optional .resc script as a managed background process.",
                RENODE_START_SCHEMA,
                self._tool_renode_start,
            )
            self._register_tool(
                f"{prefix}_status",
                "Return status information for the managed Renode process.",
                NO_ARG_SCHEMA,
                self._tool_renode_status,
            )
            self._register_tool(
                f"{prefix}_stop",
                "Stop the managed Renode process if one is running.",
                NO_ARG_SCHEMA,
                self._tool_renode_stop,
            )

        if self.repo_name == "scrutiny-main":
            self._register_tool(
                f"{prefix}_start_server",
                "Start the Scrutiny server as a managed background process.",
                SCRUTINY_START_SERVER_SCHEMA,
                self._tool_scrutiny_start_server,
            )
            self._register_tool(
                f"{prefix}_server_status",
                "Return status information for the managed Scrutiny server process.",
                NO_ARG_SCHEMA,
                self._tool_scrutiny_server_status,
            )
            self._register_tool(
                f"{prefix}_stop_server",
                "Stop the managed Scrutiny server process if one is running.",
                NO_ARG_SCHEMA,
                self._tool_scrutiny_stop_server,
            )
            self._register_tool(
                f"{prefix}_runtest_target",
                "Run Scrutiny's native runtest command with an optional target selector.",
                SCRUTINY_RUNTEST_TARGET_SCHEMA,
                self._tool_scrutiny_runtest_target,
            )

    def _register_tool(
        self,
        name: str,
        description: str,
        input_schema: dict[str, Any],
        handler: Callable[[dict[str, Any]], dict[str, Any]],
    ) -> None:
        self._tool_definitions.append(
            {
                "name": name,
                "description": description,
                "inputSchema": input_schema,
            }
        )
        self._tool_handlers[name] = handler

    def _infer_task_definitions(self) -> dict[str, dict[str, Any]]:
        tasks: dict[str, dict[str, Any]] = {}
        package_json_path = self.repo_root / "package.json"
        if package_json_path.exists():
            try:
                package_data = json.loads(package_json_path.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError):
                package_data = {}
            scripts = package_data.get("scripts", {}) if isinstance(package_data, dict) else {}
            tasks["install"] = {
                "description": f"Install Node.js dependencies for {self.repo_name}.",
                "command": ["npm", "install"],
            }
            if isinstance(scripts, dict):
                if "build" in scripts:
                    tasks["build"] = {
                        "description": f"Run the npm build script for {self.repo_name}.",
                        "command": ["npm", "run", "build"],
                    }
                if "test" in scripts:
                    tasks["test"] = {
                        "description": f"Run the npm test script for {self.repo_name}.",
                        "command": ["npm", "run", "test"],
                    }
                if "lint" in scripts:
                    tasks["lint"] = {
                        "description": f"Run the npm lint script for {self.repo_name}.",
                        "command": ["npm", "run", "lint"],
                    }
                if "typecheck" in scripts:
                    tasks["typecheck"] = {
                        "description": f"Run the npm typecheck script for {self.repo_name}.",
                        "command": ["npm", "run", "typecheck"],
                    }

        if (self.repo_root / "go.mod").exists():
            tasks.setdefault(
                "build",
                {
                    "description": f"Build all Go packages in {self.repo_name}.",
                    "command": ["go", "build", "./..."],
                },
            )
            tasks.setdefault(
                "test",
                {
                    "description": f"Run all Go tests in {self.repo_name}.",
                    "command": ["go", "test", "./..."],
                },
            )
            tasks.setdefault(
                "lint",
                {
                    "description": f"Run go vet for {self.repo_name}.",
                    "command": ["go", "vet", "./..."],
                },
            )

        has_python_package = (self.repo_root / "pyproject.toml").exists() or (self.repo_root / "setup.py").exists()
        if has_python_package or (self.repo_root / "requirements.txt").exists():
            if has_python_package:
                tasks.setdefault(
                    "install",
                    {
                        "description": f"Install {self.repo_name} in editable mode.",
                        "command": python_cmd("-m", "pip", "install", "-e", "."),
                    },
                )
                tasks.setdefault(
                    "build",
                    {
                        "description": f"Build the Python package for {self.repo_name}.",
                        "command": python_cmd("-m", "build"),
                    },
                )
            else:
                tasks.setdefault(
                    "install",
                    {
                        "description": f"Install Python dependencies for {self.repo_name} from requirements.txt.",
                        "command": python_cmd("-m", "pip", "install", "-r", "requirements.txt"),
                    },
                )

            tasks.setdefault(
                "test",
                {
                    "description": f"Run pytest for {self.repo_name}.",
                    "command": python_cmd("-m", "pytest"),
                },
            )
            tasks.setdefault(
                "lint",
                {
                    "description": f"Run ruff for {self.repo_name}.",
                    "command": python_cmd("-m", "ruff", "check", "."),
                },
            )
            tasks.setdefault(
                "typecheck",
                {
                    "description": f"Run mypy for {self.repo_name}.",
                    "command": python_cmd("-m", "mypy", "."),
                },
            )

        return tasks

    def _write_response(self, payload: dict[str, Any]) -> None:
        sys.stdout.write(json.dumps(payload) + "\n")
        sys.stdout.flush()

    def _handle_request(self, request: dict[str, Any]) -> dict[str, Any] | None:
        if not isinstance(request, dict):
            return self._make_error(None, MCPError.INVALID_REQUEST, "Request must be an object")

        method = request.get("method")
        req_id = request.get("id")
        params = request.get("params") or {}

        if method == "initialize":
            return self._make_result(
                req_id,
                {
                    "protocolVersion": MCP_VERSION,
                    "capabilities": {"tools": {}},
                    "serverInfo": {
                        "name": f"{self.repo_name}-repo-mcp",
                        "version": "0.3.0",
                    },
                },
            )

        if method in {"initialized", "notifications/initialized"}:
            return None

        if method == "ping":
            return self._make_result(req_id, {})

        if method == "tools/list":
            return self._make_result(req_id, {"tools": self._tool_definitions})

        if method == "tools/call":
            tool_name = params.get("name", "")
            tool_args = params.get("arguments") or {}
            handler = self._tool_handlers.get(tool_name)
            if handler is None:
                return self._make_error(
                    req_id, MCPError.METHOD_NOT_FOUND, f"Unknown tool: {tool_name}"
                )

            try:
                result = handler(tool_args)
            except Exception as exc:
                return self._make_result(
                    req_id,
                    {
                        "content": [{"type": "text", "text": json.dumps({"error": str(exc)}, indent=2)}],
                        "structuredContent": {"error": str(exc)},
                        "isError": True,
                    },
                )

            return self._make_result(
                req_id,
                {
                    "content": [{"type": "text", "text": json.dumps(result, indent=2)}],
                    "structuredContent": result,
                },
            )

        if method == "shutdown":
            self._cleanup_managed_processes()
            self._running = False
            return self._make_result(req_id, {})

        if req_id is None:
            return None

        return self._make_error(req_id, MCPError.METHOD_NOT_FOUND, f"Unknown method: {method}")

    def _make_result(self, req_id: Any, result: dict[str, Any]) -> dict[str, Any]:
        return {"jsonrpc": "2.0", "id": req_id, "result": result}

    def _make_error(self, req_id: Any, code: int, message: str) -> dict[str, Any]:
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "error": {"code": code, "message": message},
        }

    def _resolve_path(self, raw_path: str = ".") -> Path:
        candidate = (self.repo_root / raw_path).resolve()
        try:
            candidate.relative_to(self.repo_root)
        except ValueError as exc:
            raise ValueError(f"Path escapes repository root: {raw_path}") from exc
        return candidate

    def _tool_repo_info(self, args: dict[str, Any]) -> dict[str, Any]:
        manifest_summary = self._tool_manifest_summary(args)
        readme_path = self.repo_root / "README.md"
        return {
            "name": self.repo_name,
            "tool_prefix": self.tool_prefix,
            "root": str(self.repo_root),
            "readme": str(readme_path) if readme_path.exists() else None,
            "manifests": manifest_summary["files"],
            "available_tasks": sorted(self._task_definitions.keys()),
            "git": self._tool_git_status({}),
        }

    def _tool_manifest_summary(self, args: dict[str, Any]) -> dict[str, Any]:
        files = []
        for name in MANIFEST_NAMES:
            manifest_path = self.repo_root / name
            if manifest_path.exists():
                files.append(name)
        return {
            "repository": self.repo_name,
            "files": files,
            "available_tasks": sorted(self._task_definitions.keys()),
        }

    def _tool_list_directory(self, args: dict[str, Any]) -> dict[str, Any]:
        target = self._resolve_path(str(args.get("path", ".")))
        recursive = bool(args.get("recursive", False))
        max_entries = max(1, min(int(args.get("max_entries", 200)), 1000))
        if not target.exists():
            raise FileNotFoundError(f"Path does not exist: {target}")
        if target.is_file():
            rel_path = target.relative_to(self.repo_root).as_posix()
            return {"path": rel_path, "entries": [{"path": rel_path, "type": "file"}]}

        entries: list[dict[str, str]] = []
        if recursive:
            for root, dir_names, file_names in os.walk(target):
                dir_names[:] = [name for name in dir_names if name not in SKIP_DIR_NAMES]
                root_path = Path(root)
                for name in sorted(dir_names):
                    entry_path = root_path / name
                    entries.append(
                        {
                            "path": entry_path.relative_to(self.repo_root).as_posix(),
                            "type": "directory",
                        }
                    )
                    if len(entries) >= max_entries:
                        return {
                            "path": target.relative_to(self.repo_root).as_posix(),
                            "entries": entries,
                            "truncated": True,
                        }
                for name in sorted(file_names):
                    entry_path = root_path / name
                    entries.append(
                        {
                            "path": entry_path.relative_to(self.repo_root).as_posix(),
                            "type": "file",
                        }
                    )
                    if len(entries) >= max_entries:
                        return {
                            "path": target.relative_to(self.repo_root).as_posix(),
                            "entries": entries,
                            "truncated": True,
                        }
        else:
            for child in sorted(target.iterdir(), key=lambda item: (item.is_file(), item.name.lower())):
                if child.name in SKIP_DIR_NAMES:
                    continue
                entries.append(
                    {
                        "path": child.relative_to(self.repo_root).as_posix(),
                        "type": "directory" if child.is_dir() else "file",
                    }
                )
                if len(entries) >= max_entries:
                    return {
                        "path": target.relative_to(self.repo_root).as_posix(),
                        "entries": entries,
                        "truncated": True,
                    }

        return {"path": target.relative_to(self.repo_root).as_posix(), "entries": entries}

    def _tool_read_text_file(self, args: dict[str, Any]) -> dict[str, Any]:
        target = self._resolve_path(str(args.get("path", "")))
        if not target.exists() or not target.is_file():
            raise FileNotFoundError(f"File does not exist: {target}")

        start_line = max(1, int(args.get("start_line", 1)))
        end_line = max(start_line, int(args.get("end_line", 200)))
        with target.open("r", encoding="utf-8", errors="replace") as handle:
            lines = handle.readlines()

        selected = lines[start_line - 1:end_line]
        return {
            "path": target.relative_to(self.repo_root).as_posix(),
            "start_line": start_line,
            "end_line": min(end_line, len(lines)),
            "line_count": len(lines),
            "content": "".join(selected),
        }

    def _tool_search_text(self, args: dict[str, Any]) -> dict[str, Any]:
        query = str(args.get("query", "")).strip()
        if not query:
            raise ValueError("query is required")

        target = self._resolve_path(str(args.get("path", ".")))
        is_regex = bool(args.get("is_regex", False))
        max_matches = max(1, min(int(args.get("max_matches", 50)), 200))

        ripgrep_result = self._search_with_ripgrep(target, query, is_regex, max_matches)
        if ripgrep_result is not None:
            return ripgrep_result
        return self._search_with_python(target, query, is_regex, max_matches)

    def _tool_git_status(self, args: dict[str, Any]) -> dict[str, Any]:
        if shutil.which("git") is None:
            return {"available": False, "reason": "git not found"}

        completed = subprocess.run(
            ["git", "-C", str(self.repo_root), "status", "--short", "--branch"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        if completed.returncode != 0:
            return {
                "available": False,
                "reason": completed.stderr.strip() or "not a git repository",
            }
        lines = [line for line in completed.stdout.splitlines() if line.strip()]
        return {
            "available": True,
            "status": lines,
        }

    def _tool_make_directory(self, args: dict[str, Any]) -> dict[str, Any]:
        target = self._resolve_path(str(args.get("path", "")))
        target.mkdir(parents=True, exist_ok=True)
        return {
            "path": target.relative_to(self.repo_root).as_posix(),
            "created": True,
        }

    def _tool_create_file(self, args: dict[str, Any]) -> dict[str, Any]:
        target = self._resolve_path(str(args.get("path", "")))
        if target.exists():
            raise FileExistsError(f"File already exists: {target}")

        target.parent.mkdir(parents=True, exist_ok=True)
        content = str(args.get("content", ""))
        target.write_text(content, encoding="utf-8")
        return {
            "path": target.relative_to(self.repo_root).as_posix(),
            "created": True,
            "bytes_written": len(content.encode("utf-8")),
        }

    def _tool_write_text_file(self, args: dict[str, Any]) -> dict[str, Any]:
        target = self._resolve_path(str(args.get("path", "")))
        create_if_missing = bool(args.get("create_if_missing", True))
        append = bool(args.get("append", False))
        content = str(args.get("content", ""))

        if not target.exists() and not create_if_missing:
            raise FileNotFoundError(f"File does not exist: {target}")

        target.parent.mkdir(parents=True, exist_ok=True)
        mode = "a" if append else "w"
        with target.open(mode, encoding="utf-8") as handle:
            handle.write(content)

        return {
            "path": target.relative_to(self.repo_root).as_posix(),
            "appended": append,
            "bytes_written": len(content.encode("utf-8")),
        }

    def _tool_run_task(self, action: str, args: dict[str, Any]) -> dict[str, Any]:
        task = self._task_definitions[action]
        timeout_seconds = int(args.get("timeout_seconds", task.get("timeout_seconds", DEFAULT_TIMEOUT_SECONDS)))
        timeout_seconds = max(1, min(timeout_seconds, 3600))
        command = list(task["command"])
        env = os.environ.copy()
        env.update(task.get("env", {}))

        try:
            completed = subprocess.run(
                command,
                cwd=self.repo_root,
                env=env,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=timeout_seconds,
                check=False,
            )
        except FileNotFoundError as exc:
            return {
                "repository": self.repo_name,
                "action": action,
                "command": command,
                "cwd": str(self.repo_root),
                "success": False,
                "timed_out": False,
                "error": str(exc),
            }
        except subprocess.TimeoutExpired as exc:
            return {
                "repository": self.repo_name,
                "action": action,
                "command": command,
                "cwd": str(self.repo_root),
                "success": False,
                "timed_out": True,
                "timeout_seconds": timeout_seconds,
                "stdout": clip_output(exc.stdout or ""),
                "stderr": clip_output(exc.stderr or ""),
            }

        return {
            "repository": self.repo_name,
            "action": action,
            "command": command,
            "cwd": str(self.repo_root),
            "success": completed.returncode == 0,
            "timed_out": False,
            "returncode": completed.returncode,
            "stdout": clip_output(completed.stdout),
            "stderr": clip_output(completed.stderr),
        }

    def _tool_repo2qr_generate_qr(self, args: dict[str, Any]) -> dict[str, Any]:
        input_path = self._resolve_path(str(args.get("input_path", "")))
        if not input_path.exists() or not input_path.is_file():
            raise FileNotFoundError(f"Input file does not exist: {input_path}")

        output_dir = self._resolve_path(str(args.get("output_dir", "")))
        output_dir.mkdir(parents=True, exist_ok=True)
        image_format = str(args.get("format", "svg")).lower()
        if image_format not in {"svg", "png"}:
            raise ValueError("format must be 'svg' or 'png'")

        chunk_size = max(1, int(args.get("chunk_size", 1500)))
        compress = bool(args.get("compress", True))
        package_root = self.repo_root / "qr_package"

        sys.path.insert(0, str(package_root))
        try:
            binary_to_qr = importlib.import_module("qr_package.binary_to_qr")
            payload_module = importlib.import_module("qr_package.payload")
        finally:
            sys.path.pop(0)

        encode_file_to_base64_chunks = binary_to_qr.encode_file_to_base64_chunks
        generate_qr_code_png = binary_to_qr.generate_qr_code_png
        generate_qr_code_svg = binary_to_qr.generate_qr_code_svg
        encode_payload = payload_module.encode_payload

        created_files: list[str] = []
        capture = io.StringIO()
        with contextlib.redirect_stdout(capture):
            base64_chunks = encode_file_to_base64_chunks(
                str(input_path),
                chunk_size=chunk_size,
                compress=compress,
            )

            total = len(base64_chunks)
            for index, chunk in enumerate(base64_chunks, start=1):
                payload = encode_payload(total=total, index=index, data=chunk)
                if image_format == "png":
                    output_path = generate_qr_code_png(payload, index, str(output_dir))
                else:
                    generate_qr_code_svg(payload, index, str(output_dir))
                    output_path = str(output_dir / f"qr_{index}.svg")
                created_files.append(Path(output_path).relative_to(self.repo_root).as_posix())

        return {
            "input_path": input_path.relative_to(self.repo_root).as_posix(),
            "output_dir": output_dir.relative_to(self.repo_root).as_posix(),
            "format": image_format,
            "chunk_size": chunk_size,
            "compress": compress,
            "file_count": len(created_files),
            "files": created_files,
            "captured_stdout": clip_output(capture.getvalue()),
        }

    def _tool_renode_start(self, args: dict[str, Any]) -> dict[str, Any]:
        launcher = self.repo_root / "renode"
        command: list[str]
        if os.name == "nt" and shutil.which("bash"):
            command = ["bash", str(launcher)]
        else:
            command = [str(launcher)]

        if bool(args.get("console", True)):
            command.append("--console")
        if bool(args.get("disable_gui", True)):
            command.append("--disable-gui")
        if "monitor_port" in args and args.get("monitor_port") is not None:
            command.extend(["-P", str(int(args["monitor_port"]))])
        if args.get("script_path"):
            script_path = self._resolve_path(str(args["script_path"]))
            command.append(str(script_path))

        return self._start_managed_process("renode", command)

    def _tool_renode_status(self, args: dict[str, Any]) -> dict[str, Any]:
        return self._get_managed_process_status("renode")

    def _tool_renode_stop(self, args: dict[str, Any]) -> dict[str, Any]:
        return self._stop_managed_process("renode")

    def _tool_scrutiny_start_server(self, args: dict[str, Any]) -> dict[str, Any]:
        command = python_cmd("-m", "scrutiny", "server")
        if args.get("config_path"):
            config_path = self._resolve_path(str(args["config_path"]))
            command.extend(["--config", str(config_path)])
        if args.get("port") is not None:
            command.extend(["--port", str(int(args["port"]))])
        if args.get("loglevel"):
            command.extend(["--loglevel", str(args["loglevel"])])
        return self._start_managed_process("scrutiny_server", command)

    def _tool_scrutiny_server_status(self, args: dict[str, Any]) -> dict[str, Any]:
        return self._get_managed_process_status("scrutiny_server")

    def _tool_scrutiny_stop_server(self, args: dict[str, Any]) -> dict[str, Any]:
        return self._stop_managed_process("scrutiny_server")

    def _tool_scrutiny_runtest_target(self, args: dict[str, Any]) -> dict[str, Any]:
        command = python_cmd("-m", "scrutiny", "runtest")
        if args.get("target"):
            command.append(str(args["target"]))

        timeout_seconds = int(args.get("timeout_seconds", DEFAULT_TIMEOUT_SECONDS))
        timeout_seconds = max(1, min(timeout_seconds, 3600))
        try:
            completed = subprocess.run(
                command,
                cwd=self.repo_root,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=timeout_seconds,
                check=False,
            )
        except subprocess.TimeoutExpired as exc:
            return {
                "repository": self.repo_name,
                "command": command,
                "success": False,
                "timed_out": True,
                "timeout_seconds": timeout_seconds,
                "stdout": clip_output(exc.stdout or ""),
                "stderr": clip_output(exc.stderr or ""),
            }

        return {
            "repository": self.repo_name,
            "command": command,
            "success": completed.returncode == 0,
            "timed_out": False,
            "returncode": completed.returncode,
            "stdout": clip_output(completed.stdout),
            "stderr": clip_output(completed.stderr),
        }

    def _start_managed_process(
        self,
        key: str,
        command: list[str],
        env: dict[str, str] | None = None,
    ) -> dict[str, Any]:
        current = self._managed_processes.get(key)
        if current and current["process"].poll() is None:
            status = self._get_managed_process_status(key)
            status["already_running"] = True
            return status

        stdout_file = tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", suffix=f"_{key}_stdout.log", delete=False
        )
        stderr_file = tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", suffix=f"_{key}_stderr.log", delete=False
        )

        process = subprocess.Popen(
            command,
            cwd=self.repo_root,
            env={**os.environ, **(env or {})},
            stdout=stdout_file,
            stderr=stderr_file,
            text=True,
        )

        self._managed_processes[key] = {
            "process": process,
            "command": command,
            "stdout_path": Path(stdout_file.name),
            "stderr_path": Path(stderr_file.name),
            "stdout_handle": stdout_file,
            "stderr_handle": stderr_file,
            "started_at": time.time(),
        }
        return self._get_managed_process_status(key)

    def _get_managed_process_status(self, key: str) -> dict[str, Any]:
        info = self._managed_processes.get(key)
        if info is None:
            return {"name": key, "running": False, "status": "not_started"}

        process: subprocess.Popen[str] = info["process"]
        returncode = process.poll()
        self._flush_process_logs(info)
        if returncode is not None:
            self._close_process_logs(info)

        return {
            "name": key,
            "running": returncode is None,
            "pid": process.pid,
            "returncode": returncode,
            "command": info["command"],
            "stdout_tail": self._read_tail(info["stdout_path"]),
            "stderr_tail": self._read_tail(info["stderr_path"]),
        }

    def _stop_managed_process(self, key: str) -> dict[str, Any]:
        info = self._managed_processes.get(key)
        if info is None:
            return {"name": key, "running": False, "stopped": False, "status": "not_started"}

        process: subprocess.Popen[str] = info["process"]
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)

        self._flush_process_logs(info)
        self._close_process_logs(info)
        return {
            "name": key,
            "running": False,
            "stopped": True,
            "returncode": process.poll(),
            "stdout_tail": self._read_tail(info["stdout_path"]),
            "stderr_tail": self._read_tail(info["stderr_path"]),
        }

    def _cleanup_managed_processes(self) -> None:
        for key in list(self._managed_processes.keys()):
            self._stop_managed_process(key)

    def _flush_process_logs(self, info: dict[str, Any]) -> None:
        for handle_name in ("stdout_handle", "stderr_handle"):
            handle = info.get(handle_name)
            if handle and not handle.closed:
                handle.flush()

    def _close_process_logs(self, info: dict[str, Any]) -> None:
        for handle_name in ("stdout_handle", "stderr_handle"):
            handle = info.get(handle_name)
            if handle and not handle.closed:
                handle.close()

    def _read_tail(self, path: Path, max_lines: int = 40) -> str:
        if not path.exists():
            return ""
        try:
            with path.open("r", encoding="utf-8", errors="replace") as handle:
                lines = handle.readlines()
        except OSError:
            return ""
        return clip_output("".join(lines[-max_lines:]))

    def _search_with_ripgrep(
        self, target: Path, query: str, is_regex: bool, max_matches: int
    ) -> dict[str, Any] | None:
        if shutil.which("rg") is None:
            return None

        command = ["rg", "--json", "--line-number", "--color", "never"]
        if not is_regex:
            command.append("--fixed-strings")
        command.extend([query, str(target)])

        completed = subprocess.run(
            command,
            cwd=self.repo_root,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )

        matches: list[dict[str, Any]] = []
        for line in completed.stdout.splitlines():
            if not line.strip():
                continue
            payload = json.loads(line)
            if payload.get("type") != "match":
                continue
            data = payload["data"]
            match = {
                "path": Path(data["path"]["text"]).relative_to(self.repo_root).as_posix(),
                "line": data["line_number"],
                "text": data["lines"]["text"].rstrip("\r\n"),
            }
            matches.append(match)
            if len(matches) >= max_matches:
                break

        return {
            "query": query,
            "is_regex": is_regex,
            "matches": matches,
            "truncated": len(matches) >= max_matches,
            "engine": "ripgrep",
        }

    def _search_with_python(
        self, target: Path, query: str, is_regex: bool, max_matches: int
    ) -> dict[str, Any]:
        pattern = re.compile(query) if is_regex else None
        matches: list[dict[str, Any]] = []

        files: list[Path]
        if target.is_file():
            files = [target]
        else:
            files = []
            for root, dir_names, file_names in os.walk(target):
                dir_names[:] = [name for name in dir_names if name not in SKIP_DIR_NAMES]
                root_path = Path(root)
                for name in file_names:
                    files.append(root_path / name)

        for file_path in files:
            try:
                with file_path.open("r", encoding="utf-8", errors="replace") as handle:
                    for line_number, line in enumerate(handle, start=1):
                        found = bool(pattern.search(line)) if pattern else query in line
                        if not found:
                            continue
                        matches.append(
                            {
                                "path": file_path.relative_to(self.repo_root).as_posix(),
                                "line": line_number,
                                "text": line.rstrip("\r\n"),
                            }
                        )
                        if len(matches) >= max_matches:
                            return {
                                "query": query,
                                "is_regex": is_regex,
                                "matches": matches,
                                "truncated": True,
                                "engine": "python",
                            }
            except OSError:
                continue

        return {
            "query": query,
            "is_regex": is_regex,
            "matches": matches,
            "truncated": False,
            "engine": "python",
        }


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run a repository MCP server with repo-scoped read, write, task, and native tools."
    )
    parser.add_argument(
        "--repo-root",
        required=False,
        help="Repository root exposed by this server. Defaults to the script directory.",
    )
    parser.add_argument(
        "--repo-name",
        required=False,
        help="Display name for this repository. Defaults to the script directory name.",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv or sys.argv[1:])
    script_path = Path(__file__).resolve()
    repo_root = Path(args.repo_root).resolve() if args.repo_root else script_path.parent
    if not repo_root.exists() or not repo_root.is_dir():
        raise SystemExit(f"Repository root does not exist: {repo_root}")

    repo_name = args.repo_name or repo_root.name
    server = RepoMCPServer(repo_root=repo_root, repo_name=repo_name)
    server.serve()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())