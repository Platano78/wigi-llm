#!/usr/bin/env python3
"""
Lightweight HTTP server that exposes GPU VRAM usage via JSON.
Run this on any machine with an NVIDIA GPU to enable remote VRAM monitoring.

Usage:
    python3 gpu-vram-server.py              # default port 8089
    python3 gpu-vram-server.py --port 9090  # custom port

Endpoint:
    GET /gpu  ->  {"vram_used_mb": 15719, "vram_total_mb": 16303}
    GET /health -> {"status": "ok"}
"""

import json
import subprocess
import argparse
from http.server import HTTPServer, BaseHTTPRequestHandler


def get_vram():
    try:
        out = subprocess.check_output(
            ["nvidia-smi", "--query-gpu=memory.used,memory.total",
             "--format=csv,noheader,nounits"],
            timeout=5
        ).decode().strip()
        parts = out.split(",")
        if len(parts) >= 2:
            return {
                "vram_used_mb": int(parts[0].strip()),
                "vram_total_mb": int(parts[1].strip())
            }
    except Exception:
        pass
    return {"vram_used_mb": 0, "vram_total_mb": 0, "error": "nvidia-smi failed"}


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/gpu":
            data = get_vram()
            self._respond(200, data)
        elif self.path == "/health":
            self._respond(200, {"status": "ok"})
        else:
            self._respond(404, {"error": "not found"})

    def _respond(self, code, data):
        body = json.dumps(data).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, fmt, *args):
        pass  # silence request logs


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="GPU VRAM info server")
    parser.add_argument("--port", type=int, default=8089)
    parser.add_argument("--bind", default="0.0.0.0")
    args = parser.parse_args()

    server = HTTPServer((args.bind, args.port), Handler)
    print(f"GPU VRAM server listening on {args.bind}:{args.port}")
    print(f"  GET http://localhost:{args.port}/gpu")
    server.serve_forever()
