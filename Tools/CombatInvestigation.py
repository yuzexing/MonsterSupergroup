#!/usr/bin/env python3
"""Local investigation service. Raw archives and existing databases are read-only inputs."""
import argparse
from contextlib import closing
import json
import math
import os
from pathlib import Path
import secrets
import sys
import time
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import parse_qs, urlsplit
import webbrowser


def browser_values(value):
    """JSON's integer syntax exceeds JavaScript precision; never round identity values."""
    if isinstance(value, bool):
        return value
    if isinstance(value, int) and abs(value) > 9007199254740991:
        return str(value)
    if isinstance(value, float) and not math.isfinite(value):
        return str(value)
    if isinstance(value, dict):
        return {k: browser_values(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [browser_values(v) for v in value]
    return value


def prepare_archive(roots, destination):
    import CombatEvidence as decoder
    from CombatInvestigationIndex import build_index
    destination = Path(destination).resolve()
    if destination.exists():
        raise ValueError("分析数据库已存在；请使用新的输出目录。")
    for root in roots:
        root = Path(root).resolve()
        if not root.is_dir():
            raise ValueError("归档目录不存在：" + str(root))
        if root == destination.parent or root in destination.parents:
            raise ValueError("分析输出必须在输入归档之外，避免再次导入输出。")
    destination.parent.mkdir(parents=True, exist_ok=True)
    with closing(decoder.connect(destination)) as db:
        result = decoder.import_roots(db, roots)
    build_index(destination)
    return result


def make_server(database, export_root, port=0):
    from CombatInvestigationIndex import Investigation
    from CombatInvestigationExport import IssueExporter
    investigation = Investigation(database)
    token = secrets.token_urlsafe(32)
    asset_dir = Path(__file__).with_name("CombatInvestigationWeb")
    exporter = IssueExporter(investigation, export_root)

    class Handler(BaseHTTPRequestHandler):
        server_version = "CombatInvestigation/1"

        def log_message(self, fmt, *args):
            pass

        def send(self, status, content, mime="application/json; charset=utf-8"):
            if not isinstance(content, bytes):
                content = json.dumps(browser_values(content), ensure_ascii=False, allow_nan=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", mime)
            self.send_header("Content-Length", str(len(content)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'")
            self.end_headers()
            try:
                self.wfile.write(content)
            except (BrokenPipeError, ConnectionResetError):
                pass

        def local_request(self):
            expected = "127.0.0.1:" + str(self.server.server_port)
            if self.headers.get("Host") != expected:
                self.send(403, {"error": "仅允许本机调查页面访问。"})
                return False
            origin = self.headers.get("Origin")
            if origin and origin != "http://" + expected:
                self.send(403, {"error": "拒绝其他页面发起的请求。"})
                return False
            return True

        def do_GET(self):
            if not self.local_request():
                return
            try:
                url = urlsplit(self.path)
                static = {"/": ("index.html", "text/html; charset=utf-8"),
                          "/app.js": ("app.js", "text/javascript; charset=utf-8"),
                          "/style.css": ("style.css", "text/css; charset=utf-8")}
                if url.path in static:
                    name, mime = static[url.path]
                    return self.send(200, (asset_dir / name).read_bytes(), mime)
                if len(url.query) > 32768:
                    raise ValueError("查询条件过长。")
                params = parse_qs(url.query)
                request = json.loads(params.get("q", ["{}"])[0])
                if not isinstance(request, dict):
                    raise ValueError("查询条件必须为对象。")
                selection = request.get("selection", {})
                if not isinstance(selection, dict):
                    raise ValueError("无效选择。")
                if url.path == "/api/session":
                    result = {"token": token, "database": str(Path(database).resolve()),
                              "exports": str(Path(export_root).resolve()), "version": 1, "networkDiagnostics": True}
                elif url.path == "/api/matches":
                    result = investigation.matches()
                elif url.path == "/api/entities":
                    result = investigation.entities(selection)
                elif url.path == "/api/connections":
                    result = investigation.network_connections(selection)
                elif url.path == "/api/network-load":
                    result = investigation.network_load(selection)
                elif url.path == "/api/timeline":
                    result = investigation.timeline(selection, request.get("cursor"), int(request.get("limit", 200)))
                elif url.path == "/api/state":
                    result = investigation.state(selection, request["capture"], str(request["entity"]), float(request["at"]))
                elif url.path == "/api/detail":
                    result = investigation.detail(selection, request["capture"], str(request["sequence"]))
                elif url.path == "/api/tracks":
                    result = investigation.tracks(selection)
                elif url.path == "/api/coverage":
                    result = investigation.coverage(selection)
                else:
                    return self.send(404, {"error": "不存在的入口。"})
                self.send(200, result)
            except (ValueError, KeyError, TypeError) as error:
                self.send(400, {"error": str(error)})
            except Exception as error:
                self.send(500, {"error": type(error).__name__ + ": " + str(error)})

        def do_POST(self):
            try:
                length = int(self.headers.get("Content-Length", "0"))
                if not 0 < length <= 1024 * 1024:
                    raise ValueError("请求大小无效。")
                self.connection.settimeout(10)
                body = self.rfile.read(length)
                if not self.local_request():
                    return
                if self.headers.get("X-Investigation-Token") != token:
                    return self.send(403, {"error": "请从当前调查页面操作。"})
                request = json.loads(body)
                selection = request.get("selection", {})
                seeds = request.get("records", [])
                description = str(request.get("description", ""))[:10000]
                if self.path == "/api/export/preview":
                    result = exporter.preview(selection, seeds, description)
                elif self.path == "/api/export":
                    result = exporter.export(selection, seeds, description, request.get("previewToken"))
                else:
                    return self.send(404, {"error": "不存在的入口。"})
                self.send(200, result)
            except (ValueError, KeyError, TypeError) as error:
                self.send(400, {"error": str(error)})
            except Exception as error:
                self.send(500, {"error": type(error).__name__ + ": " + str(error)})

    server = HTTPServer(("127.0.0.1", port), Handler)
    server.investigation = investigation
    return server


def main(argv=None):
    parser = argparse.ArgumentParser(description="CombatEvidence 本地对局调查时间线")
    parser.add_argument("--db", help="新版分析数据库；仅供只读查询")
    parser.add_argument("--source-db", help="旧分析数据库；复制到新目录后建立索引")
    parser.add_argument("--roots", nargs="+", help="原始导出目录或必要证据包")
    parser.add_argument("--output", help="新分析目录")
    parser.add_argument("--port", type=int, default=0)
    parser.add_argument("--open", action="store_true", help="打开本地浏览器")
    parser.add_argument("--prepare-only", action="store_true")
    args = parser.parse_args(argv)
    if sum(bool(x) for x in (args.db, args.source_db, args.roots)) != 1:
        parser.error("指定 --db、--source-db 或 --roots 中的一种。")
    if args.db:
        database = Path(args.db).resolve()
        output = Path(args.output).resolve() if args.output else database.parent
    else:
        if args.output:
            output = Path(args.output).resolve()
        else:
            base = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "MonsterSupergroup" / "CombatInvestigation"
            output = base / (time.strftime("%Y%m%d-%H%M%S") + "-" + secrets.token_hex(3))
        database = output / "investigation.sqlite"
        print("正在建立独立分析数据库：" + str(database), flush=True)
        if args.source_db:
            from CombatInvestigationIndex import prepare
            output.mkdir(parents=True, exist_ok=True)
            prepare(args.source_db, database)
        elif len(args.roots) == 1 and (Path(args.roots[0]) / "investigation-package.json").is_file():
            from CombatInvestigationExport import import_package
            import_package(args.roots[0], database)
        else:
            prepare_archive(args.roots, database)
    if args.prepare_only:
        print("分析数据库已准备：" + str(database))
        return 0
    server = make_server(database, output / "exports", args.port)
    address = "http://127.0.0.1:" + str(server.server_port)
    print("调查页面：" + address, flush=True)
    print("关闭此终端或按 Ctrl+C 停止服务。原始证据仅被读取。", flush=True)
    if args.open:
        webbrowser.open(address)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
        server.investigation.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
