#!/usr/bin/env python3
"""Read-only, canonical revision manifest; requires a quiescent version-aware server."""
import argparse
import hashlib
import json
import sys
import urllib.error
import urllib.request
import uuid


def require(condition, message):
    if not condition:
        raise ValueError(message)


def collect(url, limit=200):
    require(1 <= limit <= 200, "Invalid page size")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def read(path, binary=False):
        try:
            response = opener.open(url.rstrip('/') + path, timeout=30)
        except urllib.error.HTTPError as error:
            error.close()
            raise ValueError("HTTP request failed") from None
        with response:
            if binary:
                digest = hashlib.sha256()
                size = 0
                while chunk := response.read(1024 * 1024):
                    size += len(chunk)
                    digest.update(chunk)
                return {"sha256": digest.hexdigest(), "bytes": size}
            return json.load(response)

    def pages(path):
        items, total = [], None
        while True:
            page = read(f"{path}?offset={len(items)}&limit={limit}")
            require(page['offset'] == len(items) and page['limit'] == limit, "Page bounds differ")
            require(type(page['total']) is int and page['total'] >= 0, "Invalid total")
            if total is None:
                total = page['total']
            require(total == page['total'], "Server changed during enumeration")
            batch = page['items']
            require(isinstance(batch, list) and len(batch) <= limit, "Invalid page")
            require(len(items) + len(batch) <= total, "Page exceeds total")
            require(batch or len(items) == total, "Truncated enumeration")
            items.extend(batch)
            if len(items) == total:
                return items

    result, ids = [], set()
    for deleted in (False, True):
        path = '/api/nests/deleted' if deleted else '/api/nests'
        records = pages(path + '/query')
        for record in records:
            ident = record['id']
            require(str(uuid.UUID(ident)) == ident and ident not in ids, "Invalid or duplicate nest ID")
            ids.add(ident)
            require(record['deleted'] is deleted, "Visibility differs")
            revisions = pages(path + '/' + ident + '/revisions')
            require([r['revision'] for r in revisions] == list(range(record['revision'], 0, -1)),
                    "Missing, duplicate, or unordered revisions")
            require(revisions and revisions[0] == record, "Current projection differs from latest revision")
            history = []
            for revision in revisions:
                number = revision['revision']
                require(revision['id'] == ident, "Revision belongs to another nest")
                parent = revision['restoredFromRevision']
                require(parent is None or type(parent) is int and 1 <= parent < number,
                        "Invalid restore source relationship")
                archive = read(f"{path}/{ident}/revisions/{number}/file", binary=True)
                require(archive['bytes'] == revision['fileSize'], "Revision archive size differs")
                history.append({"metadata": revision, "archive": archive})
            current = read(f"{path}/{ident}/file", binary=True)
            require(current == history[0]['archive'], "Current archive differs from latest revision")
            result.append({"current": record, "archive": current,
                           "revisions": sorted(history, key=lambda r: r['metadata']['revision'])})
    return {"format": 1, "records": sorted(result, key=lambda r: r['current']['id'])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('url')
    parser.add_argument('--page-size', type=int, default=200)
    args = parser.parse_args()
    try:
        manifest = collect(args.url, args.page_size)
        # Emit only after every request and invariant succeeds; failure never emits a partial manifest.
        print(json.dumps(manifest, sort_keys=True, indent=2))
    except Exception as error:
        print(f"Manifest failed: {type(error).__name__}", file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
