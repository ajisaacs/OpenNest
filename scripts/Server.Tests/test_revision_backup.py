"""Manifest refusal tests and opt-in executable documentation/rollback rehearsal.

OPENNEST_TEST_IMAGE=<local-new-image> [OPENNEST_TEST_OLD_IMAGE=<local-old-image>]
python3 -m unittest discover -s scripts/Server.Tests -p test_revision_backup.py -v
Only generated loopback containers/volumes are used; no live server URL is accepted.
"""
import copy
import hashlib
import http.server
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import tempfile
import threading
import time
import unittest
import urllib.parse
import urllib.request
import uuid

from nest_manifest import collect
from legacy_fixture import create

ROOT = Path(__file__).resolve().parents[2]


class ManifestTests(unittest.TestCase):
    def setUp(self):
        self.ident = '00000000-0000-4000-8000-000000000001'
        self.record = dict(id=self.ident, revision=3, deleted=True, restoredFromRevision=1,
                           fileSize=3, comments='restored', savedAt='2026-01-02T03:04:05')
        self.revisions = [self.record, dict(self.record, revision=2, deleted=False,
                                          restoredFromRevision=None, comments='edited'),
                          dict(self.record, revision=1, deleted=False, restoredFromRevision=None)]
        self.failure = None
        owner = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                path = urllib.parse.urlsplit(self.path).path
                query = urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query)
                if owner.failure == 'legacy' or owner.failure == 'download' and path.endswith('/file'):
                    self.send_error(500 if owner.failure == 'download' else 404)
                    return
                if path.endswith('/file'):
                    data = b'abc'
                else:
                    values = owner.revisions if path.endswith('/revisions') else ([owner.record] if '/deleted/' in path else [])
                    offset, limit = int(query['offset'][0]), int(query['limit'][0])
                    batch = values[offset:offset + limit]
                    if owner.failure == 'truncated' and offset > 0:
                        batch = []
                    total = len(values) + (1 if owner.failure == 'total' and offset else 0)
                    data = json.dumps(dict(items=batch, total=total, offset=offset, limit=limit)).encode()
                self.send_response(200)
                self.end_headers()
                self.wfile.write(data)

            def log_message(self, format, *args):
                pass

        self.server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.url = f'http://127.0.0.1:{self.server.server_port}'

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()

    def test_all_deleted_revisions_and_parent_metadata_are_preserved(self):
        manifest = collect(self.url, 1)
        record = manifest['records'][0]
        self.assertEqual(self.record, record['current'])
        self.assertEqual([1, 2, 3], [r['metadata']['revision'] for r in record['revisions']])
        self.assertEqual(hashlib.sha256(b'abc').hexdigest(), record['archive']['sha256'])
        self.assertEqual(1, record['revisions'][2]['metadata']['restoredFromRevision'])
        altered = copy.deepcopy(manifest)
        altered['records'][0]['revisions'][0]['metadata']['comments'] = 'tampered'
        self.assertNotEqual(manifest, altered)

    def test_failed_download_emits_no_partial_manifest(self):
        self.failure = 'download'
        result = subprocess.run(['python3', str(ROOT / 'scripts/Server.Tests/nest_manifest.py'), self.url], capture_output=True)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(b'', result.stdout)

    def test_legacy_endpoint_is_not_empty_history(self):
        self.failure = 'legacy'
        with self.assertRaises(Exception):
            collect(self.url, 1)

    def test_missing_pages_and_changed_totals_fail(self):
        for failure in ('truncated', 'total'):
            self.failure = failure
            with self.assertRaises(ValueError):
                collect(self.url, 1)

    def test_missing_revision_and_invalid_parent_fail(self):
        self.revisions.pop(1)
        with self.assertRaises(ValueError):
            collect(self.url, 1)
        self.revisions.insert(1, dict(self.record, revision=2, restoredFromRevision=None))
        self.record['restoredFromRevision'] = 3
        with self.assertRaises(ValueError):
            collect(self.url, 1)


@unittest.skipUnless(os.environ.get('OPENNEST_TEST_IMAGE'), 'Set OPENNEST_TEST_IMAGE for disposable Docker documentation rehearsal')
class DocumentationTests(unittest.TestCase):
    def run_command(self, *args, input=None, check=True):
        result = subprocess.run(args, input=input, text=True, capture_output=True, timeout=120, cwd=self.scratch)
        if check and result.returncode:
            self.fail(f"Command failed ({result.returncode}): {args}\n{result.stdout}\n{result.stderr}")
        return result

    def docker(self, *args, **kwargs):
        return self.run_command('docker', '--context', 'default', *args, **kwargs)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='opennest-backup-doc-', dir=os.environ.get('TMPDIR'))
        self.scratch = Path(self.temp.name)
        self.token = 'opennest-doc-' + uuid.uuid4().hex
        self.image = os.environ['OPENNEST_TEST_IMAGE']
        self.volumes = []
        self.containers = []
        self.compose = None
        self.addCleanup(self.cleanup_resources)
        endpoint = self.docker('context', 'inspect', 'default', '--format', '{{.Endpoints.docker.Host}}').stdout.strip()
        self.assertEqual('unix:///var/run/docker.sock', endpoint)
        self.volume = self.token + '-data'
        self.new_volume(self.volume)
        with socket.socket() as probe:
            probe.bind(('127.0.0.1', 0))
            port = probe.getsockname()[1]
        configuration = {'services': {'opennest-server': {'image': self.image,
                         'ports': [f'127.0.0.1:{port}:8090'], 'volumes': [self.volume + ':/app/data'],
                         'cap_drop': ['ALL'], 'security_opt': ['no-new-privileges:true']}},
                         'volumes': {self.volume: {'external': True}}}
        (self.scratch / 'compose.json').write_text(json.dumps(configuration))
        self.compose = f'docker --context default compose -p {self.token} -f compose.json'
        self.run_command('bash', '-c', self.compose + ' up -d')
        self.cid = self.run_command('bash', '-c', self.compose + ' ps -q').stdout.strip()
        self.url = self.ready(self.cid)
        shutil.copy(ROOT / 'scripts/Server.Tests/nest_manifest.py', self.scratch / 'nest_manifest.py')
        doc = (ROOT / 'docs/nest-storage.md').read_text()
        blocks = re.findall(r'```bash\n(.*?)```', doc, re.S)
        selected = [block for block in blocks if 'backup_nests()' in block or 'restore_check()' in block]
        self.assertEqual(2, len(selected))
        self.functions = '\n'.join('\n'.join(line for line in block.splitlines()
                                   if not line.startswith(('url=', 'backup_nests opennest-', 'restore_check opennest-')))
                                   for block in selected)
        (self.scratch / 'procedures.sh').write_text(self.functions)
        self.run_command('bash', '-n', 'procedures.sh')
        # Opaque synthetic archives exercise history without any customer data.
        record = self.request('POST', '/api/nests', multipart=True)
        record['comments'] = 'metadata version'
        record = self.request('PUT', f"/api/nests/{record['id']}/metadata", record, record['revision'])
        record = self.request('POST', f"/api/nests/{record['id']}/revisions/1/restore", revision=record['revision'])
        self.request('DELETE', f"/api/nests/{record['id']}", revision=record['revision'])
        self.request('POST', '/api/nests', multipart=True)

    def new_volume(self, name):
        self.assertNotEqual(0, self.docker('volume', 'inspect', name, check=False).returncode)
        self.docker('volume', 'create', '--label', 'com.opennest.doc-owner=' + self.token, name)
        self.assertEqual(self.token, self.docker('volume', 'inspect', '--format', '{{index .Labels "com.opennest.doc-owner"}}', name).stdout.strip())
        self.volumes.append(name)

    def ready(self, cid):
        port = self.docker('port', cid, '8090/tcp').stdout.strip().split(':')[1]
        url = f'http://127.0.0.1:{port}'
        for _ in range(60):
            try:
                with urllib.request.urlopen(url + '/healthz', timeout=1) as response:
                    if json.load(response) == {'status': 'ok'}:
                        return url
            except OSError:
                time.sleep(1)
        self.fail('Disposable service did not become ready')

    def request(self, method, path, data=None, revision=None, multipart=False):
        headers = {}
        if revision:
            headers['If-Match'] = f'"{revision}"'
        if multipart:
            boundary = 'synthetic-boundary'
            data = (f'--{boundary}\r\nContent-Disposition: form-data; name="metadata"\r\n\r\n'
                    '{"name":"Synthetic documentation fixture"}\r\n'
                    f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="fixture.nest"\r\n'
                    f'Content-Type: application/zip\r\n\r\nabc\r\n--{boundary}--\r\n').encode()
            headers['Content-Type'] = 'multipart/form-data; boundary=' + boundary
        elif data is not None:
            data = json.dumps(data).encode()
            headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(self.url + path, data=data, headers=headers, method=method)
        with urllib.request.urlopen(request, timeout=10) as response:
            return json.load(response)

    def procedure(self, command, check=True):
        prefix = f'compose={json.dumps(self.compose)}; url={self.url}; source ./procedures.sh; '
        return self.run_command('bash', '-c', prefix + command, check=check)

    def cleanup_resources(self):
        # Compose removes only this randomly generated project's containers, never its external volumes.
        try:
            if self.compose:
                self.run_command('bash', '-c', self.compose + ' down')
            for cid in self.containers:
                self.docker('rm', '-f', cid)
            for volume in self.volumes:
                labels = json.loads(self.docker('volume', 'inspect', '--format', '{{json .Labels}}', volume).stdout)
                self.assertTrue(labels.get('com.opennest.doc-owner') == self.token
                                or labels.get('com.opennest.restore.owner') == volume and volume.startswith(self.token + '-'))
                self.assertEqual('', self.docker('container', 'ls', '-aq', '--filter', 'volume=' + volume).stdout.strip(),
                                 'A documentation check container was not removed')
                self.docker('volume', 'rm', volume)
                self.assertNotEqual(0, self.docker('volume', 'inspect', volume, check=False).returncode)
            self.assertEqual('', self.docker('container', 'ls', '-aq', '--filter', 'label=com.docker.compose.project=' + self.token).stdout.strip())
        finally:
            self.temp.cleanup()

    def test_verbatim_backup_restore_and_refusals(self):
        self.procedure('backup_nests backup')
        original = json.loads((self.scratch / 'backup.manifest').read_text())
        self.assertEqual(2, len(original['records']))
        self.assertEqual(5, sum(len(r['revisions']) for r in original['records']))
        started = self.docker('inspect', '--format', '{{.State.StartedAt}}', self.cid).stdout
        refused = self.procedure('if backup_nests backup; then exit 90; fi')
        self.assertIn('already exists', refused.stderr)
        self.assertEqual(started, self.docker('inspect', '--format', '{{.State.StartedAt}}', self.cid).stdout)
        restored = self.token + '-restored'
        refused = self.procedure(f'if restore_check backup {self.image} {self.volume}; then exit 90; fi')
        self.assertIn('already exists', refused.stderr)
        self.volumes.append(restored)
        self.procedure(f'restore_check backup {self.image} {restored}')
        tampered = self.token + '-tampered'
        self.volumes.append(tampered)
        original['records'][0]['revisions'][0]['archive']['sha256'] = '0' * 64
        (self.scratch / 'backup.manifest').write_text(json.dumps(original))
        refused = self.procedure(f'if restore_check backup {self.image} {tampered}; then exit 90; fi')
        self.assertIn('archives differ', refused.stderr)

    @unittest.skipUnless(os.environ.get('OPENNEST_TEST_OLD_IMAGE'), 'Set OPENNEST_TEST_OLD_IMAGE for old-image rollback rehearsal')
    def test_old_image_only_on_pre_migration_backup_copy(self):
        create(self.scratch)
        rollback = self.token + '-rollback'
        self.new_volume(rollback)
        # Restore synthetic pre-migration tar to a NEW owned volume, never the upgraded database.
        with (self.scratch / 'legacy.tar').open('rb') as archive:
            subprocess.run(['docker', '--context', 'default', 'run', '--rm', '-i', '--network', 'none',
                            '--entrypoint', 'tar', '-v', rollback + ':/app/data', self.image,
                            '-C', '/app/data', '-xf', '-'], stdin=archive, check=True, timeout=30, capture_output=True)
        cid = self.docker('run', '-d', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true',
                          '-p', '127.0.0.1::8090', '-v', rollback + ':/app/data',
                          os.environ['OPENNEST_TEST_OLD_IMAGE']).stdout.strip()
        self.containers.append(cid)
        url = self.ready(cid)
        expected = json.loads((self.scratch / 'legacy.manifest').read_text())['records']
        with urllib.request.urlopen(url + '/api/nests') as response:
            actual = {r['id']: r for r in json.load(response)}
        self.assertEqual(2, len(actual))
        for entry in expected:
            record = {k: v for k, v in entry['current'].items() if k not in ('revision', 'deleted', 'restoredFromRevision')}
            self.assertEqual(record, actual[record['id']])
            with urllib.request.urlopen(url + f"/api/nests/{record['id']}/file") as response:
                self.assertEqual(entry['archive']['sha256'], hashlib.sha256(response.read()).hexdigest())
        with self.assertRaises(Exception):
            collect(url)


if __name__ == '__main__':
    unittest.main()
