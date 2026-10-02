#!/usr/bin/env python3
"""Fail-closed, read-only source/GHCR checks. This tool never tags or pushes.

Credentials stay in memory; outputs contain only validated provenance/digests.
The workflow alone owns Docker login and the two explicit push commands.
"""
import argparse
import base64
import gzip
import hashlib
import io
import json
import os
import pathlib
import re
import subprocess
import sys
import tarfile
import tempfile
import urllib.error
import urllib.parse
import urllib.request

REPO = "ajisaacs/OpenNest"
SOURCE = "https://github.com/" + REPO
PACKAGE = "opennest-server"
REGISTRY_NAME = "ajisaacs/" + PACKAGE
IMAGE = "ghcr.io/" + REGISTRY_NAME
REPO_API = "https://api.github.com/repos/" + REPO
PACKAGE_API = "https://api.github.com/users/ajisaacs/packages/container/" + PACKAGE
MANIFEST_TYPES = ("application/vnd.oci.image.manifest.v1+json",
                  "application/vnd.docker.distribution.manifest.v2+json")
# Any stored image representation is a collision; published readback stays strict.
REGISTRY_MANIFEST_TYPES = MANIFEST_TYPES + ("application/vnd.oci.image.index.v1+json",
                                          "application/vnd.docker.distribution.manifest.list.v2+json")


class ReleaseError(Exception):
    pass


def require(condition, message):
    if not condition:
        raise ReleaseError(message)


def version(tag):
    require(isinstance(tag, str) and re.fullmatch(
        r"v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", tag), "invalid release tag")
    return tag[1:]


def commit(value):
    require(isinstance(value, str) and re.fullmatch(r"[0-9a-f]{40}", value), "invalid full commit SHA")
    return value


def sha256(value):
    require(isinstance(value, str) and re.fullmatch(r"sha256:[0-9a-f]{64}", value), "invalid SHA-256 digest")
    return value


def json_body(body):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "duplicate JSON key")
            result[key] = value
        return result
    try:
        return json.loads(body, object_pairs_hook=unique)
    except (ValueError, UnicodeError, TypeError):
        raise ReleaseError("malformed JSON response") from None


def command(*args):
    try:
        result = subprocess.run(args, check=False, capture_output=True, text=True, timeout=300)
    except (OSError, subprocess.TimeoutExpired):
        raise ReleaseError("local command unavailable or timed out") from None
    require(result.returncode == 0, "local command failed: " + args[0])
    return result.stdout.strip()


def git(*args):
    return command("git", *args)


def resolve_source(repo, event, ref, tag, event_sha=None):
    version(tag)  # Validate before constructing a ref or stripping v.
    require(repo == REPO, "publication requires the primary repository")
    require((event == "workflow_dispatch" and ref == "refs/heads/master") or
            (event == "release" and ref == "refs/tags/" + tag), "unapproved publication event/ref")
    expected = None
    if event == "release":
        expected = commit(event_sha if event_sha is not None else os.environ.get("GITHUB_SHA", ""))
    sha = commit(git("rev-parse", "--verify", "refs/tags/" + tag + "^{commit}"))
    require(expected is None or sha == expected, "release tag differs from event commit")
    git("merge-base", "--is-ancestor", sha, "refs/remotes/origin/master")
    return sha


def registry_path(suffix):
    return "https://ghcr.io/v2/" + REGISTRY_NAME + "/" + suffix


def absent(status, headers, body):
    if status == 200:
        return False
    require(status == 404, "registry lookup failed; not proven absent")
    data = json_body(body)
    require(isinstance(data, dict) and isinstance(data.get("errors"), list) and data["errors"],
            "malformed registry absence response")
    require(all(isinstance(error, dict) and error.get("code") in ("MANIFEST_UNKNOWN", "NAME_UNKNOWN")
                for error in data["errors"]), "registry denial/error is not absence")
    return True


def package_policy(status, headers, body):
    data = json_body(body)
    require(isinstance(data, dict), "malformed package metadata")
    require(status == 200, "package metadata inaccessible; privacy unknown")
    require(data.get("name") == PACKAGE and data.get("package_type") == "container" and
            data.get("visibility") == "private" and isinstance(data.get("repository"), dict) and
            data["repository"].get("full_name") == REPO, "package privacy/association mismatch")
    return False


def registry_token(status, body):
    # Opaque token issuance does not prove the granted scope: GHCR may reduce it.
    # Never use token success or registry absence to override package metadata.
    require(status == 200, "registry authentication/scope request failed")
    data = json_body(body)
    require(isinstance(data, dict) and isinstance(data.get("token"), str) and
            bool(data["token"]) and not any(c.isspace() for c in data["token"]),
            "malformed token response")
    if "scope" in data:
        require(data["scope"] == "repository:" + REGISTRY_NAME + ":pull,push", "reduced registry scope")
    return data["token"]


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Client:
    def __init__(self):
        self.github_token = os.environ.get("GITHUB_TOKEN", "")
        require(bool(self.github_token), "GITHUB_TOKEN required")
        require(os.environ.get("GITHUB_REPOSITORY") == REPO, "unexpected authenticated repository")
        self.opener = urllib.request.build_opener(NoRedirect())
        credentials = base64.b64encode((os.environ.get("GITHUB_ACTOR", "") + ":" +
                                        self.github_token).encode()).decode()
        url = "https://ghcr.io/token?" + urllib.parse.urlencode({
            "service": "ghcr.io", "scope": "repository:" + REGISTRY_NAME + ":pull,push"})
        status, _, body = self.request(url, {"Authorization": "Basic " + credentials})
        self.registry_token = registry_token(status, body)

    def request(self, url, headers):
        try:
            with self.opener.open(urllib.request.Request(url, headers=headers), timeout=60) as result:
                return result.status, {k.lower(): v for k, v in result.headers.items()}, result.read()
        except urllib.error.HTTPError as error:
            return error.code, {k.lower(): v for k, v in error.headers.items()}, error.read()
        except (OSError, ValueError):
            raise ReleaseError("network/transport failure (not absence)") from None

    def get(self, path):
        if path.startswith("https://api.github.com/"):
            return self.request(path, {"Authorization": "Bearer " + self.github_token,
                                     "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"})
        require(path.startswith(registry_path("")), "unexpected registry endpoint")
        result = self.request(path, {"Authorization": "Bearer " + self.registry_token,
                                    "Accept": ", ".join(REGISTRY_MANIFEST_TYPES)})
        if "/blobs/" in path and result[0] in (302, 307):
            location = result[1].get("location", "")
            parsed = urllib.parse.urlparse(location)
            require(parsed.scheme == "https" and parsed.hostname == "pkg-containers.githubusercontent.com",
                    "unexpected blob redirect")
            # Never forward credentials to redirected storage or record signed URLs.
            return self.request(location, {})
        return result


def preflight(client, release_version, sha):
    status, _, body = client.get(REPO_API)
    repo = json_body(body)
    require(status == 200 and isinstance(repo, dict) and repo.get("full_name") == REPO and
            repo.get("default_branch") == "master", "repository/default branch authorization unproven")
    package_policy(*client.get(PACKAGE_API))
    tags_response = client.get(registry_path("tags/list"))
    require(tags_response[0] == 200, "existing package registry read access unproven")
    data = json_body(tags_response[2])
    require(isinstance(data, dict) and data.get("name") == REGISTRY_NAME and
            (data.get("tags") is None or (isinstance(data.get("tags"), list) and
             all(isinstance(tag, str) for tag in data["tags"]))), "malformed registry tag list")
    require("tags" in data and "link" not in tags_response[1], "incomplete registry tag list")
    reserved_tags = (release_version, "sha-" + sha)
    require(not any(tag in (data["tags"] or []) for tag in reserved_tags), "refusing existing immutable tag")
    for tag in reserved_tags:
        require(absent(*client.get(registry_path("manifests/" + tag))), "refusing existing immutable tag")
    return {"package": IMAGE, "visibility": "private"}


def content_digest(body):
    return "sha256:" + hashlib.sha256(body).hexdigest()


def stream_digest(stream):
    digest = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(chunk)
    return "sha256:" + digest.hexdigest()


def layer_diff_id(stream, media_type):
    try:
        if media_type in ("application/vnd.oci.image.layer.v1.tar+gzip",
                          "application/vnd.docker.image.rootfs.diff.tar.gzip"):
            with gzip.GzipFile(fileobj=stream) as decoded:
                return stream_digest(decoded)
        require(media_type == "application/vnd.oci.image.layer.v1.tar", "unsupported layer encoding")
        return stream_digest(stream)
    except (OSError, EOFError):
        raise ReleaseError("invalid layer compression") from None


def image_manifest(body):
    data = json_body(body)
    require(isinstance(data, dict) and data.get("schemaVersion") == 2 and
            data.get("mediaType") in MANIFEST_TYPES and isinstance(data.get("config"), dict) and
            isinstance(data.get("layers"), list), "unexpected manifest (single-platform required)")
    return data


def rootfs(config):
    data = config.get("rootfs")
    require(isinstance(data, dict) and data.get("type") == "layers" and
            isinstance(data.get("diff_ids"), list) and data["diff_ids"], "missing config rootfs chain")
    return [sha256(value) for value in data["diff_ids"]]


def archive_identity(path, local):
    """Bind verified saved bytes to the inspected immutable store identity.

    Both stores may save an OCI envelope. Classic Id hashes raw config;
    containerd Id/Descriptor identify the manifest, whose verified descriptors
    bind config/layers. Inspect Config is reconstructed, not the saved JSON.
    No archive extraction, and all temporary image bytes are removed by caller.
    """
    try:
        with tarfile.open(path) as archive:
            def member(name):
                entry = archive.getmember(name)
                require(entry.isfile(), "archive member is not a regular file")
                stream = archive.extractfile(entry)
                if stream is None:
                    raise ReleaseError("missing archive file")
                return stream

            def blob(descriptor):
                require(isinstance(descriptor, dict), "invalid blob descriptor")
                digest = sha256(descriptor.get("digest"))
                stream = member("blobs/sha256/" + digest[7:])
                require(type(descriptor.get("size")) is int and descriptor["size"] == archive.getmember("blobs/sha256/" + digest[7:]).size,
                        "archive blob size mismatch")
                require(stream_digest(stream) == digest, "archive blob digest mismatch")
                stream.close()
                return member("blobs/sha256/" + digest[7:])

            names = archive.getnames()
            require(len(names) == len(set(names)), "duplicate archive members")
            exported_descriptor = None
            if "index.json" in names:
                index = json_body(member("index.json").read())
                require(isinstance(index, dict) and index.get("schemaVersion") == 2 and
                        isinstance(index.get("manifests"), list) and len(index["manifests"]) == 1,
                        "ambiguous image archive")
                descriptor = index["manifests"][0]
                require(isinstance(descriptor, dict) and descriptor.get("mediaType") in MANIFEST_TYPES,
                        "unexpected archive image index/attestation")
                manifest = image_manifest(blob(descriptor).read())
                require(manifest["mediaType"] == descriptor["mediaType"], "archive manifest media type mismatch")
                exported_descriptor = descriptor
                config_body = blob(manifest["config"]).read()
                layers = manifest["layers"]
                diffs = [layer_diff_id(blob(layer), layer.get("mediaType")) for layer in layers]
            else:
                entries = json_body(member("manifest.json").read())
                require(isinstance(entries, list) and len(entries) == 1 and isinstance(entries[0], dict),
                        "ambiguous classic image archive")
                config_body = member(entries[0]["Config"]).read()
                diffs = [stream_digest(member(name)) for name in entries[0]["Layers"]]
            config_digest = content_digest(config_body)
            inspected_descriptor = local.get("Descriptor")
            if inspected_descriptor is None:
                require(config_digest == sha256(local.get("Id")),
                        "saved config does not bind inspected immutable image")
            else:
                require(isinstance(inspected_descriptor, dict) and isinstance(exported_descriptor, dict) and
                        type(inspected_descriptor.get("size")) is int and
                        all(inspected_descriptor.get(key) == exported_descriptor.get(key)
                            for key in ("digest", "size", "mediaType")) and
                        exported_descriptor["digest"] == sha256(local.get("Id")),
                        "saved manifest does not bind inspected immutable image")
            config = json_body(config_body)
            require(isinstance(config, dict) and config.get("os") == local.get("Os") and
                    config.get("architecture") == local.get("Architecture") and isinstance(config.get("config"), dict),
                    "saved config platform mismatch")
            require(diffs == rootfs(config) == local.get("RootFS", {}).get("Layers"), "saved layer/rootfs chain mismatch")
            return {"config_digest": config_digest, "rootfs_diff_ids": diffs, "image_config": config}
    except (OSError, tarfile.TarError, KeyError, TypeError, AttributeError):
        raise ReleaseError("invalid image archive") from None


def local_identity(image):
    local = inspect(image)
    # Use the inspected immutable store ID, not a potentially retargeted tag.
    with tempfile.TemporaryDirectory(prefix="opennest-image-identity-") as directory:
        path = pathlib.Path(directory) / "image.tar"
        command("docker", "image", "save", "-o", str(path), sha256(local.get("Id")))
        local.update(archive_identity(path, local))
    require(inspect(image)["Id"] == local["Id"], "local image changed while inspecting")
    return local


def check_local(local, release_version, sha):
    require(isinstance(local, dict), "invalid local image inspect")
    image_id = sha256(local.get("Id"))
    require(local.get("Os") == "linux" and local.get("Architecture") == "amd64", "wrong image platform")
    config = local.get("Config")
    require(isinstance(config, dict) and isinstance(config.get("Labels"), dict), "missing image config/labels")
    labels = config["Labels"]
    for key, expected in (("source", SOURCE), ("version", release_version), ("revision", sha)):
        require(labels.get("org.opencontainers.image." + key) == expected, "OCI label mismatch: " + key)
    base = labels.get("org.opencontainers.image.base.name", "")
    require(isinstance(base, str) and re.fullmatch(
        r"mcr\.microsoft\.com/dotnet/aspnet@sha256:[0-9a-f]{64}", base), "runtime base not digest pinned")
    config_digest = sha256(local.get("config_digest"))
    diffs = local.get("rootfs_diff_ids")
    require(isinstance(diffs, list) and diffs and
            diffs == local.get("RootFS", {}).get("Layers"), "local rootfs identity mismatch")
    for value in diffs:
        sha256(value)
    return {"image_id": image_id, "config_digest": config_digest, "rootfs_diff_ids": diffs,
            "platform": "linux/amd64", "version": release_version,
            "source_sha": sha, "runtime_image": base, "source": SOURCE}


def readback(client, release_version, sha, local):
    result = check_local(local, release_version, sha)
    manifests = []
    digests = []
    for tag in (release_version, "sha-" + sha):
        status, headers, body = client.get(registry_path("manifests/" + tag))
        require(status == 200, "published tag readback failed")
        remote_digest = sha256(headers.get("docker-content-digest"))
        require(remote_digest == "sha256:" + hashlib.sha256(body).hexdigest(), "manifest content digest mismatch")
        manifest = image_manifest(body)
        manifests.append(manifest)
        digests.append(remote_digest)
    require(digests[0] == digests[1], "published tags have different digests")
    descriptor = manifests[0]["config"]
    config_digest = sha256(descriptor.get("digest"))
    require(config_digest == result["config_digest"], "registry config differs from smoked local config")
    status, _, body = client.get(registry_path("blobs/" + config_digest))
    require(status == 200 and type(descriptor.get("size")) is int and descriptor["size"] == len(body) and
            config_digest == "sha256:" + hashlib.sha256(body).hexdigest(), "config blob readback mismatch")
    remote = json_body(body)
    require(isinstance(remote, dict) and remote.get("os") == "linux" and remote.get("architecture") == "amd64" and
            remote == local.get("image_config"), "remote platform/image config mismatch")
    diffs = rootfs(remote)
    require(diffs == result["rootfs_diff_ids"] and len(manifests[0]["layers"]) == len(diffs),
            "remote rootfs chain differs from smoked image")
    for layer, expected in zip(manifests[0]["layers"], diffs):
        require(isinstance(layer, dict), "invalid remote layer descriptor")
        digest = sha256(layer.get("digest"))
        status, _, body = client.get(registry_path("blobs/" + digest))
        require(status == 200 and type(layer.get("size")) is int and layer["size"] == len(body) and
                digest == content_digest(body), "layer blob readback mismatch")
        require(layer_diff_id(io.BytesIO(body), layer.get("mediaType")) == expected,
                "remote layer does not match smoked rootfs")
    result.update(manifest_digest=digests[0], config_digest=config_digest, package=IMAGE)
    return result


def inspect(image):
    result = json_body(command("docker", "image", "inspect", image))
    require(isinstance(result, list) and len(result) == 1, "ambiguous local image")
    return result[0]


def outputs(values):
    path = os.environ.get("GITHUB_OUTPUT")
    if path:
        with open(path, "a", encoding="utf-8") as file:
            for key, value in values.items():
                require(isinstance(value, str) and "\n" not in value and "\r" not in value, "unsafe workflow output")
                file.write(key + "=" + value + "\n")


def save(name, data):
    directory = pathlib.Path(os.environ.get("METADATA_DIR", ".hermes/server-image-metadata"))
    directory.mkdir(parents=True, exist_ok=True)
    (directory / name).write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("source", "bases", "local", "preflight", "readback", "pulled"))
    args = parser.parse_args()
    if args.operation == "source":
        tag = os.environ.get("TAG", "")
        sha = resolve_source(os.environ.get("GITHUB_REPOSITORY"), os.environ.get("GITHUB_EVENT_NAME"),
                             os.environ.get("GITHUB_REF"), tag, os.environ.get("GITHUB_SHA"))
        outputs({"source_sha": sha, "version": version(tag)})
        save("source.json", {"source_sha": sha, "version": version(tag), "tag": tag})
        return
    if args.operation == "bases":
        bases = {}
        for key, kind in (("sdk_image", "sdk"), ("runtime_image", "aspnet")):
            name = "mcr.microsoft.com/dotnet/" + kind
            command("docker", "pull", "--platform", "linux/amd64", name + ":8.0")
            data = inspect(name + ":8.0")
            candidates = [value for value in data.get("RepoDigests", []) if value.startswith(name + "@")]
            require(len(candidates) == 1, "base digest not uniquely resolved")
            sha256(candidates[0].split("@", 1)[1])
            bases[key] = candidates[0]
        outputs(bases)
        save("bases.json", bases)
        return
    release_version = version("v" + os.environ.get("VERSION", ""))
    sha = commit(os.environ.get("SOURCE_SHA", ""))
    require(git("rev-parse", "HEAD") == sha, "checkout is not the exact tested source")
    if args.operation == "preflight":
        save("preflight.json", preflight(Client(), release_version, sha))
        return
    local = local_identity(os.environ.get("LOCAL_IMAGE", ""))
    if args.operation == "readback":
        client = Client()
        package_policy(*client.get(PACKAGE_API))
        directory = pathlib.Path(os.environ.get("METADATA_DIR", ".hermes/server-image-metadata"))
        try:
            previous = json_body((directory / "local.json").read_bytes())
        except OSError:
            raise ReleaseError("pre-smoke identity unavailable") from None
        require(previous == check_local(local, release_version, sha), "image differs from pre-smoke identity")
        result = readback(client, release_version, sha, local)
        outputs({"manifest_digest": result["manifest_digest"], "config_digest": result["config_digest"]})
        save("registry.json", result)
    else:
        result = check_local(local, release_version, sha)
        if args.operation == "pulled":
            require(result["config_digest"] == sha256(os.environ.get("CONFIG_DIGEST", "")), "pulled config digest mismatch")
            expected = sha256(os.environ.get("MANIFEST_DIGEST", ""))
            require(IMAGE + "@" + expected in local.get("RepoDigests", []), "pulled registry digest mismatch")
        if args.operation == "local":
            outputs({"image_id": result["image_id"]})
        save("pulled.json" if args.operation == "pulled" else "local.json", result)


if __name__ == "__main__":
    try:
        main()
    except ReleaseError as error:
        print("STOP: " + str(error), file=sys.stderr)
        sys.exit(1)
