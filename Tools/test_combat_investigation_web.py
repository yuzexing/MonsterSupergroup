import hashlib
from contextlib import closing
import json
from pathlib import Path
import queue
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

import CombatEvidence as evidence
from CombatInvestigation import browser_values, main, make_server
from CombatInvestigationIndex import build_index


class InvestigationWebTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        source = self.root / "original" / "run" / "1" / "sources" / "host"
        source.mkdir(parents=True)
        records = [dict(schemaVersion=2, captureId="host", runId="run", round=1, recordSequence="1",
                        stage="source.start", monotonicTime=10, input={}),
                   dict(schemaVersion=2, captureId="host", runId="run", round=1, recordSequence="2",
                        stage="owner.attack_started", monotonicTime=11, source=2, target=4,
                        eventId="18446744073709551615", input={"weaponId": 7, "nestedId": 18446744073709551615})]
        (source / "events-00001.jsonl").write_text("".join(json.dumps(x) + "\n" for x in records), encoding="utf-8")
        (source / "coverage.json").write_text(json.dumps(dict(captureId="host", runId="run", round=1,
            produced="2", written="2", flushed="2", complete=True, tailUnknown=False, gaps=[])), encoding="utf-8")
        self.database = self.root / "analysis.sqlite"
        with closing(evidence.connect(self.database)) as db:
            evidence.import_roots(db, [source])
        build_index(self.database)
        self.before = hashlib.sha256(self.database.read_bytes()).hexdigest()
        ready = queue.Queue()
        def run():
            server = make_server(self.database, self.root / "exports")
            ready.put(server)
            try:
                server.serve_forever(poll_interval=0.01)
            finally:
                server.server_close()
                server.investigation.close()
        self.thread = threading.Thread(target=run)
        self.thread.start()
        self.server = ready.get(timeout=10)
        self.url = "http://127.0.0.1:" + str(self.server.server_port)
        self.selection = dict(run="run", round=1, captures=["host"])

    def tearDown(self):
        self.server.shutdown()
        self.thread.join(timeout=10)
        self.assertFalse(self.thread.is_alive())
        self.assertEqual(self.before, hashlib.sha256(self.database.read_bytes()).hexdigest())
        self.temp.cleanup()

    def get(self, path, data=None, headers=None):
        from urllib.parse import quote
        address = self.url + path
        if data is not None:
            address += "?q=" + quote(json.dumps(data))
        return urlopen(Request(address, headers=headers or {}), timeout=10)

    def test_page_api_and_nested_uint64_remain_exact(self):
        with self.get("/") as response:
            self.assertIn("对局调查时间线", response.read().decode())
            self.assertIn("frame-ancestors 'none'", response.headers["Content-Security-Policy"])
        with self.get("/api/detail", dict(selection=self.selection, capture="host", sequence="2")) as response:
            data = json.load(response)
        record = next(r for r in data["records"] if r["recordSequence"] == "2")
        self.assertEqual(record["input"]["nestedId"], "18446744073709551615")
        self.assertEqual(record["eventId"], "18446744073709551615")
        with self.get("/api/timeline", dict(selection=self.selection)) as response:
            self.assertEqual(json.load(response)["total"], 2)

    def test_no_filesystem_route_or_cross_origin_export(self):
        for path, headers, code in [("/../../ProjectSettings/ProjectVersion.txt", {}, 404),
                                     ("/api/session", {"Origin": "https://example.invalid"}, 403),
                                     ("/api/session", {"Host": "evil.invalid"}, 403)]:
            with self.assertRaises(HTTPError) as caught:
                self.get(path, headers=headers)
            self.assertEqual(caught.exception.code, code)
            caught.exception.close()
        request = Request(self.url + "/api/export", data=b'{}', headers={"Content-Type": "application/json"})
        with self.assertRaises(HTTPError) as caught:
            urlopen(request, timeout=10)
        self.assertEqual(caught.exception.code, 403)
        caught.exception.close()
        self.assertFalse((self.root / "exports").exists())

    def test_serialization_retains_bool_and_finite_numbers(self):
        self.assertEqual(browser_values({"bool": True, "id": 2**64-1, "float": 1.2345678901234}),
                         {"bool": True, "id": str(2**64-1), "float": 1.2345678901234})

    def test_cli_prepares_source_database_in_new_nested_directory(self):
        source = self.root / "old-analysis.sqlite"
        with closing(evidence.connect(source)) as db:
            evidence.import_roots(db, [self.root / "original"])
        before = hashlib.sha256(source.read_bytes()).hexdigest()
        output = self.root / "new" / "analysis"
        self.assertEqual(0, main(["--source-db", str(source), "--output", str(output), "--prepare-only"]))
        self.assertTrue((output / "investigation.sqlite").is_file())
        self.assertEqual(before, hashlib.sha256(source.read_bytes()).hexdigest())


if __name__ == "__main__":
    unittest.main()
