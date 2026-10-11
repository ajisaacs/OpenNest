#!/usr/bin/env python3
"""Generate only synthetic legacy SQLite files inside caller-owned scratch."""
import hashlib
import json
from pathlib import Path
import sqlite3
import sys
import tarfile

SCHEMA = """
CREATE TABLE nests (
 id TEXT PRIMARY KEY, name TEXT NOT NULL, customer TEXT NOT NULL DEFAULT '',
 dateCreated TEXT NOT NULL, dateModified TEXT NOT NULL, material TEXT NOT NULL DEFAULT '',
 thickness REAL NOT NULL DEFAULT 0, status TEXT NOT NULL DEFAULT 'Quote',
 plateCount INTEGER NOT NULL DEFAULT 0, partCount INTEGER NOT NULL DEFAULT 0,
 comments TEXT NOT NULL DEFAULT '', madeBy TEXT NOT NULL DEFAULT '',
 fileSize INTEGER NOT NULL DEFAULT 0, savedAt TEXT NOT NULL, file BLOB NOT NULL);
CREATE INDEX ix_nests_savedAt ON nests(savedAt DESC);
"""


def create(directory):
    db = directory / 'nests.db'
    expected = []
    with sqlite3.connect(db) as connection:
        connection.executescript(SCHEMA)
        for index in range(1, 3):
            ident = f'00000000-0000-4000-8000-{index:012d}'
            data = bytes([0, index, 255, 128, 0])
            date = '2026-01-02T03:04:05'
            fields = (ident, f'Synthetic legacy {index}', 'Synthetic fixture', date, date,
                      'Synthetic alloy', 0.25, 'ToBeCut', 2, 3, 'Legacy notes', 'Fixture', len(data), date)
            connection.execute('INSERT INTO nests VALUES (' + ','.join('?' * 15) + ')', (*fields, data))
            metadata: dict[str, object] = dict(zip(('id', 'name', 'customer', 'dateCreated', 'dateModified', 'material',
                                 'thickness', 'status', 'plateCount', 'partCount', 'comments',
                                 'madeBy', 'fileSize', 'savedAt'), fields))
            metadata.update(status='toBeCut', revision=1, deleted=False, restoredFromRevision=None)
            archive = {'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data)}
            expected.append({'current': metadata, 'archive': archive,
                             'revisions': [{'metadata': metadata, 'archive': archive}]})
    (directory / 'legacy.manifest').write_text(json.dumps({'format': 1, 'records': expected}, sort_keys=True, indent=2) + '\n')
    with tarfile.open(directory / 'legacy.tar', 'w') as archive:
        info = archive.gettarinfo(str(db), arcname='./nests.db')
        info.uid = info.gid = 1654
        info.uname = info.gname = ''
        with db.open('rb') as source:
            archive.addfile(info, source)


if __name__ == '__main__':
    create(Path(sys.argv[1]))
