"""Behavioral gates for the read-only server image release helper."""
import copy
import gzip
import hashlib
import io
import os
import tarfile
import importlib.util
import json
import pathlib
import subprocess
import tempfile
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location(
    "release", pathlib.Path(__file__).with_name("server_image_release.py"))
assert SPEC is not None and SPEC.loader is not None
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)
SHA = "a" * 40
BASE = "mcr.microsoft.com/dotnet/aspnet@sha256:" + "b" * 64
LAYER = b"deterministic synthetic rootfs layer"
LAYER_GZIP = gzip.compress(LAYER, mtime=0)


def encoded(value):
    return json.dumps(value, separators=(",", ":")).encode()


def digest(data):
    return "sha256:" + hashlib.sha256(data).hexdigest()


def config():
    return {"os": "linux", "architecture": "amd64",
            "rootfs": {"type": "layers", "diff_ids": [digest(LAYER)]}, "config": {
        "User": "1654", "Entrypoint": ["dotnet", "OpenNest.Server.dll"],
        "Labels": {"org.opencontainers.image.source": release.SOURCE,
                   "org.opencontainers.image.revision": SHA,
                   "org.opencontainers.image.version": "1.2.3",
                   "org.opencontainers.image.base.name": BASE}}}


class FakeClient:
    def __init__(self, responses):
        self.responses = responses
        self.calls = []

    def get(self, path, **kwargs):
        self.calls.append(path)
        response = self.responses[path]
        if isinstance(response, Exception):
            raise response
        return response


def response(status, value, headers=None):
    return status, headers or {}, encoded(value)


class StrictInputs(unittest.TestCase):
    def test_tag_positive(self):
        for value in ("v0.0.0", "v1.2.3", "v10.20.30"):
            self.assertEqual(release.version(value), value[1:])

    def test_tag_rejects_shell_and_noncanonical_versions(self):
        for value in ("1.2.3", "v01.2.3", "v1.02.3", "v1.2.03", "v1.2.3\n",
                      "v1.2.3-rc1", "v1.2.3+meta", "v1.2", "v1.2.3;id", ""):
            with self.subTest(value=value), self.assertRaises(release.ReleaseError):
                release.version(value)

    def test_sha_and_digest_are_full_lowercase(self):
        self.assertEqual(release.commit(SHA), SHA)
        self.assertEqual(release.sha256("sha256:" + "a" * 64), "sha256:" + "a" * 64)
        for value in ("a" * 39, "A" * 40, SHA + "\n", "--help"):
            with self.assertRaises(release.ReleaseError):
                release.commit(value)
        for value in ("sha256:abc", "sha512:" + "a" * 64, "sha256:" + "A" * 64):
            with self.assertRaises(release.ReleaseError):
                release.sha256(value)

    def test_source_requires_primary_repo_and_approved_event(self):
        with mock.patch.object(release, "git", return_value=SHA):
            self.assertEqual(release.resolve_source(release.REPO, "workflow_dispatch",
                                                   "refs/heads/master", "v1.2.3"), SHA)
            self.assertEqual(release.resolve_source(release.REPO, "release",
                                                   "refs/tags/v1.2.3", "v1.2.3", SHA), SHA)
            for repo, event, ref in (("fork/OpenNest", "release", "refs/tags/v1.2.3"),
                                     (release.REPO, "push", "refs/tags/v1.2.3"),
                                     (release.REPO, "workflow_dispatch", "refs/heads/other")):
                with self.assertRaises(release.ReleaseError):
                    release.resolve_source(repo, event, ref, "v1.2.3")

    def test_source_rejects_missing_tag_or_non_master_ancestry(self):
        for calls in ([release.ReleaseError("missing")], [SHA, release.ReleaseError("ancestry")]):
            with mock.patch.object(release, "git", side_effect=calls):
                with self.assertRaises(release.ReleaseError):
                    release.resolve_source(release.REPO, "release", "refs/tags/v1.2.3", "v1.2.3", SHA)

    def test_source_validates_tag_before_git(self):
        with mock.patch.object(release, "git") as git:
            with self.assertRaises(release.ReleaseError):
                release.resolve_source(release.REPO, "release", "refs/tags/x", "x;id")
            git.assert_not_called()

    def test_json_rejects_malformed_and_duplicate_keys(self):
        for value in (b"<html>", b'{"a":1,"a":2}'):
            with self.assertRaises(release.ReleaseError):
                release.json_body(value)


class RegistrySafety(unittest.TestCase):
    def test_only_authenticated_structured_404_is_absence(self):
        for code in ("NAME_UNKNOWN", "MANIFEST_UNKNOWN"):
            self.assertTrue(release.absent(*response(404, {"errors": [{"code": code}]})))
        for status, body in ((401, {"errors": [{"code": "UNAUTHORIZED"}]}),
                             (403, {}), (429, {}), (500, {}), (404, {}),
                             (404, {"errors": []}),
                             (404, {"errors": [{"code": "DENIED"}]}),
                             (404, {"errors": [{"code": "NAME_UNKNOWN"}, {"code": "DENIED"}]})):
            with self.subTest(status=status, body=body), self.assertRaises(release.ReleaseError):
                release.absent(*response(status, body))
        self.assertFalse(release.absent(*response(200, {"schemaVersion": 2})))

    def test_private_associated_package(self):
        package = {"name": release.PACKAGE, "package_type": "container", "visibility": "private",
                   "repository": {"full_name": release.REPO}}
        self.assertFalse(release.package_policy(*response(200, package)))
        for key, value in (("visibility", "public"), ("visibility", "internal"),
                           ("repository", None), ("repository", {"full_name": "other/OpenNest"}),
                           ("name", "other"), ("package_type", "npm")):
            wrong = dict(package, **{key: value})
            with self.assertRaises(release.ReleaseError):
                release.package_policy(*response(200, wrong))

    def test_package_metadata_404_never_authorizes_bootstrap(self):
        with self.assertRaises(release.ReleaseError):
            release.package_policy(*response(404, {
                "message": "Not Found", "documentation_url": "https://docs.github.com/rest/packages"}))
        for status, body in ((403, {"message": "Resource not accessible"}), (404, {}),
                             (404, {"message": "Not Found", "documentation_url": "https://evil.test"})):
            with self.assertRaises(release.ReleaseError):
                release.package_policy(*response(status, body))

    def test_authenticated_scope_response_must_succeed_and_be_well_formed(self):
        self.assertEqual(release.registry_token(200, encoded({"token": "opaque-v1-token"})), "opaque-v1-token")
        for status, body in ((401, {}), (403, {"errors": [{"code": "DENIED"}]}),
                             (500, {}), (200, {}), (200, {"token": ""}), (200, {"token": "a\nb"})):
            with self.assertRaises(release.ReleaseError):
                release.registry_token(status, encoded(body))

    def preflight_client(self, package_status=200, tags=None):
        package = {"name": release.PACKAGE, "package_type": "container", "visibility": "private",
                   "repository": {"full_name": release.REPO}}
        missing = {"message": "Not Found", "documentation_url": "https://docs.github.com/rest/packages"}
        return FakeClient({
            release.REPO_API: response(200, {"full_name": release.REPO, "default_branch": "master"}),
            release.PACKAGE_API: response(package_status, package if package_status == 200 else missing),
            release.registry_path("tags/list"): response(200, {"name": release.REGISTRY_NAME, "tags": tags or []}),
            release.registry_path("manifests/1.2.3"): response(404, {"errors": [{"code": "MANIFEST_UNKNOWN"}]}),
            release.registry_path("manifests/sha-" + SHA): response(404, {"errors": [{"code": "MANIFEST_UNKNOWN"}]})})

    def test_preflight_checks_both_collision_tags(self):
        client = self.preflight_client()
        release.preflight(client, "1.2.3", SHA)
        self.assertIn(release.registry_path("manifests/sha-" + SHA), client.calls)
        for tag in ("1.2.3", "sha-" + SHA):
            client = self.preflight_client()
            client.responses[release.registry_path("manifests/" + tag)] = response(200, {})
            with self.assertRaises(release.ReleaseError):
                release.preflight(client, "1.2.3", SHA)

    def test_missing_metadata_denies_regardless_of_registry_tags(self):
        with self.assertRaises(release.ReleaseError):
            release.preflight(self.preflight_client(404), "1.2.3", SHA)
        with self.assertRaises(release.ReleaseError):
            release.preflight(self.preflight_client(404, ["old"]), "1.2.3", SHA)

    def test_preflight_transport_metadata_and_tag_list_errors_fail_closed(self):
        for path in (release.REPO_API, release.PACKAGE_API, release.registry_path("tags/list")):
            for bad in (release.ReleaseError("network"), response(401, {}), response(200, {})):
                client = self.preflight_client()
                client.responses[path] = bad
                with self.subTest(path=path), self.assertRaises(release.ReleaseError):
                    release.preflight(client, "1.2.3", SHA)


class ReadbackSafety(unittest.TestCase):
    def fixture(self):
        blob = encoded(config())
        manifest = {"schemaVersion": 2, "mediaType": release.MANIFEST_TYPES[0],
                    "config": {"digest": digest(blob), "size": len(blob)},
                    "layers": [{"mediaType": "application/vnd.oci.image.layer.v1.tar+gzip",
                                "digest": digest(LAYER_GZIP), "size": len(LAYER_GZIP)}]}
        body = encoded(manifest)
        responses = {release.registry_path("manifests/" + tag):
                     (200, {"docker-content-digest": digest(body)}, body)
                     for tag in ("1.2.3", "sha-" + SHA)}
        responses[release.registry_path("blobs/" + digest(blob))] = (200, {}, blob)
        responses[release.registry_path("blobs/" + digest(LAYER_GZIP))] = (200, {}, LAYER_GZIP)
        local = {"Id": digest(blob), "config_digest": digest(blob), "Os": "linux", "Architecture": "amd64",
                 "Config": config()["config"], "RootFS": {"Type": "layers", "Layers": [digest(LAYER)]},
                 "image_config": config(), "rootfs_diff_ids": [digest(LAYER)]}
        return FakeClient(responses), local

    def test_readback_matches_both_tags_and_full_config(self):
        client, local = self.fixture()
        result = release.readback(client, "1.2.3", SHA, local)
        self.assertEqual(result["config_digest"], local["Id"])
        self.assertTrue(result["manifest_digest"].startswith("sha256:"))
        self.assertNotEqual(result["manifest_digest"], result["config_digest"])

    def test_tag_digest_header_body_and_config_mismatch(self):
        for mode in ("tag", "header", "blob", "local"):
            client, local = self.fixture()
            path = release.registry_path("manifests/sha-" + SHA)
            if mode in ("tag", "header"):
                status, headers, body = client.responses[path]
                client.responses[path] = (status, {"docker-content-digest": "sha256:" + "f" * 64}, body)
            elif mode == "blob":
                client.responses[release.registry_path("blobs/" + local["Id"])] = (200, {}, b"{}")
            else:
                local["image_config"]["config"]["User"] = "0"
            with self.subTest(mode=mode), self.assertRaises(release.ReleaseError):
                release.readback(client, "1.2.3", SHA, local)

    def test_both_valid_but_different_tag_manifests_are_rejected(self):
        client, local = self.fixture()
        path = release.registry_path("manifests/sha-" + SHA)
        _, _, body = client.responses[path]
        changed = json.loads(body)
        changed["annotations"] = {"test": "different manifest, same config"}
        body = encoded(changed)
        client.responses[path] = (200, {"docker-content-digest": digest(body)}, body)
        with self.assertRaisesRegex(release.ReleaseError, "different digests"):
            release.readback(client, "1.2.3", SHA, local)

    def test_remote_platform_and_config_mismatches_even_with_valid_hashes(self):
        for field, value in (("os", "windows"), ("architecture", "arm64"), ("config", {})):
            client, local = self.fixture()
            remote = config()
            remote[field] = value
            blob = encoded(remote)
            local["config_digest"] = digest(blob)
            manifest = {"schemaVersion": 2, "mediaType": release.MANIFEST_TYPES[0],
                        "config": {"digest": digest(blob), "size": len(blob)},
                        "layers": [{"mediaType": "application/vnd.oci.image.layer.v1.tar+gzip",
                                    "digest": digest(LAYER_GZIP), "size": len(LAYER_GZIP)}]}
            body = encoded(manifest)
            for tag in ("1.2.3", "sha-" + SHA):
                client.responses[release.registry_path("manifests/" + tag)] = (
                    200, {"docker-content-digest": digest(body)}, body)
            client.responses[release.registry_path("blobs/" + digest(blob))] = (200, {}, blob)
            with self.subTest(field=field), self.assertRaises(release.ReleaseError):
                release.readback(client, "1.2.3", SHA, local)

    def test_platform_labels_and_image_config_are_exact(self):
        for field, value in (("Os", "windows"), ("Architecture", "arm64"), ("Id", "sha256:short")):
            _, local = self.fixture()
            local[field] = value
            with self.assertRaises(release.ReleaseError):
                release.check_local(local, "1.2.3", SHA)
        for label in ("source", "version", "revision", "base.name"):
            _, local = self.fixture()
            local["Config"]["Labels"]["org.opencontainers.image." + label] = "wrong"
            with self.assertRaises(release.ReleaseError):
                release.check_local(local, "1.2.3", SHA)

    def test_registry_rejects_index_and_missing_config(self):
        for bad in ({"schemaVersion": 2, "mediaType": "application/vnd.oci.image.index.v1+json"},
                    {"schemaVersion": 2, "mediaType": release.MANIFEST_TYPES[0]}):
            client, local = self.fixture()
            body = encoded(bad)
            for tag in ("1.2.3", "sha-" + SHA):
                client.responses[release.registry_path("manifests/" + tag)] = (
                    200, {"docker-content-digest": digest(body)}, body)
            with self.assertRaises(release.ReleaseError):
                release.readback(client, "1.2.3", SHA, local)

    def test_cli_failure_is_nonzero_without_secret_logging(self):
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run(["python3", str(pathlib.Path(release.__file__)), "source"],
                                    env={"PATH": "/usr/bin", "TAG": "bad;id", "GITHUB_TOKEN": "secret-sentinel"},
                                    capture_output=True, text=True, cwd=directory)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("secret-sentinel", result.stdout + result.stderr)


class ArchiveIdentity(unittest.TestCase):
    def archive(self, path, mode="oci", mutation=None):
        client, local = ReadbackSafety().fixture()
        config_body = encoded(config())
        manifest_body = client.responses[release.registry_path("manifests/1.2.3")][2]
        descriptor = {"mediaType": release.MANIFEST_TYPES[0], "digest": digest(manifest_body), "size": len(manifest_body)}
        index = {"schemaVersion": 2, "manifests": [descriptor]}
        members = {"blobs/sha256/" + digest(config_body)[7:]: config_body,
                   "blobs/sha256/" + digest(manifest_body)[7:]: manifest_body,
                   "blobs/sha256/" + digest(LAYER_GZIP)[7:]: LAYER_GZIP}
        if mode == "classic":
            members = {"config.json": config_body, "layer.tar": LAYER,
                       "manifest.json": encoded([{"Config": "config.json", "Layers": ["layer.tar"]}])}
        else:
            if mutation == "index":
                descriptor["mediaType"] = "application/vnd.oci.image.index.v1+json"
            if mutation == "multiple":
                index["manifests"].append(copy.deepcopy(descriptor))
            members["index.json"] = encoded(index)
        if mutation == "blob":
            members["blobs/sha256/" + digest(LAYER_GZIP)[7:]] = b"bad layer"
        if mutation == "rootfs":
            local["RootFS"]["Layers"] = ["sha256:" + "c" * 64]
        with tarfile.open(path, "w") as archive:
            for name, body in members.items():
                entry = tarfile.TarInfo(name)
                entry.size = len(body)
                archive.addfile(entry, io.BytesIO(body))
        local["Id"] = digest(manifest_body) if mode == "oci" else digest(config_body)
        if mode == "oci":
            local["Descriptor"] = copy.deepcopy(descriptor)
        return local

    def test_archive_config_identity_on_classic_and_containerd_stores(self):
        # Classic Moby also exports an OCI envelope in newer versions; its ID
        # still hashes raw config, not the generated export manifest.
        for mode in ("classic", "classic-oci", "oci"):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory) / "image.tar"
                local = self.archive(path, mode)
                identity = release.archive_identity(path, local)
                self.assertEqual(identity["config_digest"], digest(encoded(config())))
                self.assertEqual(identity["rootfs_diff_ids"], [digest(LAYER)])
                self.assertEqual(local["Id"] == identity["config_digest"], mode != "oci")

    def normalized_inspect(self, local):
        # Moby v28.0.4 image.NewFromJSON decodes into container.Config and
        # images.ImageInspect returns that struct, not RawJSON. These fields
        # lack omitempty (api/types/container/config.go), so inspect emits
        # their Go zero values even when the saved OCI config omits them.
        # This is a source-backed fixture, not an invented hosted-run diff.
        local["Config"] = dict(local["Config"], Hostname="", Domainname="",
                               AttachStdin=False, AttachStdout=False, AttachStderr=False,
                               Tty=False, OpenStdin=False, StdinOnce=False, Env=None,
                               Cmd=None, Image="", Volumes=None, WorkingDir="", OnBuild=None)
        return local

    def test_normalized_classic_inspect_keeps_exact_saved_config_identity(self):
        for mode in ("classic", "classic-oci"):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory) / "image.tar"
                local = self.normalized_inspect(self.archive(path, mode))
                self.assertNotEqual(local["Config"], config()["config"])
                try:
                    identity = release.archive_identity(path, local)
                except release.ReleaseError as error:
                    self.fail("raw config identity must survive Moby inspect defaults: " + str(error))
                self.assertEqual(identity["config_digest"], local["Id"])
                self.assertEqual(identity["image_config"], config())
                local.update(identity)
                client, _ = ReadbackSafety().fixture()
                result = release.readback(client, "1.2.3", SHA, local)
                self.assertEqual(result["config_digest"], local["Id"])

    def test_readback_uses_saved_config_not_normalized_inspect_config(self):
        client, local = ReadbackSafety().fixture()
        self.normalized_inspect(local)
        try:
            result = release.readback(client, "1.2.3", SHA, local)
        except release.ReleaseError as error:
            self.fail("remote must match saved config bytes, not reconstructed inspect: " + str(error))
        self.assertEqual(result["config_digest"], digest(encoded(config())))

    def test_classic_export_must_hash_to_inspected_immutable_id(self):
        for mode in ("classic", "classic-oci"):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory) / "image.tar"
                local = self.archive(path, mode)
                # Same nested runtime config/platform/rootfs, different raw image
                # config (created/history/etc.) must not be accepted as the ID.
                local["Id"] = digest(encoded(dict(config(), created="2000-01-01T00:00:00Z")))
                with self.assertRaisesRegex(release.ReleaseError, "immutable image"):
                    release.archive_identity(path, local)

    def test_containerd_export_must_bind_inspected_descriptor_and_id(self):
        for field, value in (("Id", "sha256:" + "d" * 64),
                             ("digest", "sha256:" + "d" * 64), ("size", 1),
                             ("size", True), ("mediaType", release.MANIFEST_TYPES[1]),
                             ("Descriptor", None), ("Descriptor", {})):
            with self.subTest(field=field, value=value), tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory) / "image.tar"
                local = self.archive(path)
                if field in ("Id", "Descriptor"):
                    local[field] = value
                else:
                    local["Descriptor"][field] = value
                with self.assertRaisesRegex(release.ReleaseError, "immutable image"):
                    release.archive_identity(path, local)

    def test_containerd_descriptor_cannot_use_legacy_archive_fallback(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "image.tar"
            local = self.archive(path, "classic")
            local["Descriptor"] = {"mediaType": release.MANIFEST_TYPES[0],
                                   "digest": local["Id"], "size": len(encoded(config()))}
            with self.assertRaisesRegex(release.ReleaseError, "immutable image"):
                release.archive_identity(path, local)

    def test_local_identity_exports_immutable_id_and_rejects_tag_retarget(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "image.tar"
            original = self.normalized_inspect(self.archive(path, "classic"))

            def export(*args):
                self.assertEqual(args[:4], ("docker", "image", "save", "-o"))
                self.assertEqual(args[5], original["Id"])
                pathlib.Path(args[4]).write_bytes(path.read_bytes())
                return ""

            with mock.patch.object(release, "command", side_effect=export), \
                    mock.patch.object(release, "inspect", return_value=copy.deepcopy(original)) as inspect:
                try:
                    local = release.local_identity("retargetable:tag")
                except release.ReleaseError as error:
                    self.fail("immutable export must verify with normalized inspect: " + str(error))
                self.assertEqual(local["image_config"], config())
                self.assertEqual(inspect.call_args_list, [mock.call("retargetable:tag")] * 2)
            changed = dict(original, Id="sha256:" + "e" * 64)
            with mock.patch.object(release, "command", side_effect=export), \
                    mock.patch.object(release, "inspect", side_effect=[copy.deepcopy(original), changed]):
                with self.assertRaisesRegex(release.ReleaseError, "changed while inspecting"):
                    release.local_identity("retargetable:tag")

    def test_archive_rejects_indexes_attestations_corruption_and_wrong_rootfs(self):
        for mutation in ("index", "multiple", "blob", "rootfs"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory) / "image.tar"
                local = self.archive(path, mutation=mutation)
                with self.assertRaises(release.ReleaseError):
                    release.archive_identity(path, local)

    def test_remote_layers_verify_compressed_digest_and_ordered_diff_ids(self):
        for mode in ("hash", "size", "diff", "encoding", "count"):
            client, local = ReadbackSafety().fixture()
            path = release.registry_path("manifests/1.2.3")
            manifest = json.loads(client.responses[path][2])
            if mode in ("hash", "size"):
                client.responses[release.registry_path("blobs/" + digest(LAYER_GZIP))] = (200, {}, b"bad")
                if mode == "hash":
                    manifest["layers"][0]["size"] = 3
            elif mode == "diff":
                body = gzip.compress(b"different rootfs", mtime=0)
                manifest["layers"][0].update(digest=digest(body), size=len(body))
                client.responses[release.registry_path("blobs/" + digest(body))] = (200, {}, body)
            elif mode == "encoding":
                manifest["layers"][0]["mediaType"] = "unsupported"
            else:
                manifest["layers"] = []
            body = encoded(manifest)
            for tag in ("1.2.3", "sha-" + SHA):
                client.responses[release.registry_path("manifests/" + tag)] = (200, {"docker-content-digest": digest(body)}, body)
            with self.subTest(mode=mode), self.assertRaises(release.ReleaseError):
                release.readback(client, "1.2.3", SHA, local)

    def test_pulled_cli_compares_archive_config_not_store_id(self):
        _, local = ReadbackSafety().fixture()
        manifest_digest = "sha256:" + "e" * 64
        local["Id"] = manifest_digest
        local["RepoDigests"] = [release.IMAGE + "@" + manifest_digest]
        env = {"VERSION": "1.2.3", "SOURCE_SHA": SHA, "CONFIG_DIGEST": local["config_digest"],
               "MANIFEST_DIGEST": manifest_digest, "LOCAL_IMAGE": local["Id"]}
        with mock.patch.dict(os.environ, env), mock.patch.object(release, "git", return_value=SHA), \
                mock.patch.object(release, "local_identity", return_value=local), \
                mock.patch.object(release, "save") as save, mock.patch("sys.argv", ["helper", "pulled"]):
            try:
                release.main()
            except release.ReleaseError as error:
                self.fail("pulled config identity must not depend on Docker Id: " + str(error))
            self.assertEqual(save.call_args.args[1]["config_digest"], env["CONFIG_DIGEST"])
            with mock.patch.dict(os.environ, {"CONFIG_DIGEST": manifest_digest}), self.assertRaises(release.ReleaseError):
                release.main()

    def test_release_event_sha_is_validated_before_git(self):
        for value in ("", SHA + "\n", SHA.upper(), SHA[:-1], " " + SHA):
            with mock.patch.object(release, "git") as git, self.subTest(value=value), self.assertRaises(release.ReleaseError):
                release.resolve_source(release.REPO, "release", "refs/tags/v1.2.3", "v1.2.3", value)
            git.assert_not_called()

    def test_remote_rootfs_config_mismatch_with_valid_blob_hashes(self):
        client, local = ReadbackSafety().fixture()
        remote = config()
        remote["rootfs"]["diff_ids"] = ["sha256:" + "c" * 64]
        blob = encoded(remote)
        # Isolate the rootfs guard after the exact saved config comparison.
        local["config_digest"] = digest(blob)
        local["image_config"] = remote
        manifest = json.loads(client.responses[release.registry_path("manifests/1.2.3")][2])
        manifest["config"].update(digest=digest(blob), size=len(blob))
        body = encoded(manifest)
        for tag in ("1.2.3", "sha-" + SHA):
            client.responses[release.registry_path("manifests/" + tag)] = (200, {"docker-content-digest": digest(body)}, body)
        client.responses[release.registry_path("blobs/" + digest(blob))] = (200, {}, blob)
        with self.assertRaisesRegex(release.ReleaseError, "rootfs chain"):
            release.readback(client, "1.2.3", SHA, local)

    def test_readback_requires_the_pre_smoke_identity_stamp(self):
        client, local = ReadbackSafety().fixture()
        client.responses[release.PACKAGE_API] = response(200, {"name": release.PACKAGE,
            "package_type": "container", "visibility": "private", "repository": {"full_name": release.REPO}})
        with tempfile.TemporaryDirectory() as directory:
            env = {"VERSION": "1.2.3", "SOURCE_SHA": SHA, "METADATA_DIR": directory}
            stamp = pathlib.Path(directory) / "local.json"
            with mock.patch.dict(os.environ, env), mock.patch.object(release, "git", return_value=SHA), \
                    mock.patch.object(release, "Client", return_value=client), \
                    mock.patch.object(release, "local_identity", return_value=local), \
                    mock.patch("sys.argv", ["helper", "readback"]):
                with self.assertRaisesRegex(release.ReleaseError, "identity unavailable"):
                    release.main()
                previous = release.check_local(local, "1.2.3", SHA)
                previous["image_id"] = "sha256:" + "e" * 64
                stamp.write_text(json.dumps(previous))
                with self.assertRaisesRegex(release.ReleaseError, "pre-smoke identity"):
                    release.main()
                self.assertNotIn(release.registry_path("manifests/1.2.3"), client.calls)
                stamp.write_text(json.dumps(release.check_local(local, "1.2.3", SHA)))
                with mock.patch.object(release, "outputs"):
                    release.main()
                self.assertEqual(json.loads((pathlib.Path(directory) / "registry.json").read_text())["config_digest"], local["config_digest"])

    def test_reduced_registry_read_access_fails_before_collision_checks(self):
        client = RegistrySafety().preflight_client()
        client.responses[release.registry_path("tags/list")] = response(404, {"errors": [{"code": "NAME_UNKNOWN"}]})
        with self.assertRaisesRegex(release.ReleaseError, "read access"):
            release.preflight(client, "1.2.3", SHA)
        self.assertNotIn(release.registry_path("manifests/1.2.3"), client.calls)


class SpecRegressions(unittest.TestCase):
    def test_masked_package_404_denies_even_empty_or_missing_registry(self):
        safety = RegistrySafety()
        for registry in (response(200, {"name": release.REGISTRY_NAME, "tags": []}),
                         response(404, {"errors": [{"code": "NAME_UNKNOWN"}]})):
            client = safety.preflight_client(404)
            client.responses[release.registry_path("tags/list")] = registry
            with self.subTest(registry=registry), self.assertRaises(release.ReleaseError):
                release.preflight(client, "1.2.3", SHA)
            self.assertNotIn(release.registry_path("tags/list"), client.calls)

    def test_opaque_reduced_scope_token_cannot_authorize_masked_metadata(self):
        with mock.patch.dict("os.environ", {"GITHUB_TOKEN": "test-only", "GITHUB_REPOSITORY": release.REPO}):
            for scope in (None, "", "repository:" + release.REGISTRY_NAME + ":pull"):
                token = {"token": "opaque-test-only"}
                if scope is not None:
                    token["scope"] = scope
                responses = [response(200, token),
                             response(200, {"full_name": release.REPO, "default_branch": "master"}),
                             response(404, {"message": "Not Found", "documentation_url": "https://docs.github.com/rest/packages"}),
                             response(200, {"name": release.REGISTRY_NAME, "tags": []}),
                             response(404, {"errors": [{"code": "MANIFEST_UNKNOWN"}]}),
                             response(404, {"errors": [{"code": "MANIFEST_UNKNOWN"}]})]
                with self.subTest(scope=scope), mock.patch.object(release.Client, "request", side_effect=responses):
                    with self.assertRaises(release.ReleaseError):
                        release.preflight(release.Client(), "1.2.3", SHA)

    def test_store_image_id_is_not_config_digest(self):
        client, local = ReadbackSafety().fixture()
        local["config_digest"] = local["Id"]
        local["Id"] = "sha256:" + "e" * 64  # containerd identifies the manifest, not config
        try:
            result = release.readback(client, "1.2.3", SHA, local)
        except release.ReleaseError as error:
            self.fail("same config must verify independently of Docker Id: " + str(error))
        self.assertEqual(result["config_digest"], local["config_digest"])
        self.assertEqual(result["image_id"], local["Id"])

    def test_remote_rootfs_must_match_smoked_image(self):
        client, local = ReadbackSafety().fixture()
        local["RootFS"] = {"Type": "layers", "Layers": ["sha256:" + "c" * 64]}
        with self.assertRaises(release.ReleaseError):
            release.readback(client, "1.2.3", SHA, local)

    def test_workflow_builds_without_attestations(self):
        workflow = pathlib.Path(__file__).resolve().parents[1] / ".github/workflows/server-image.yml"
        builds = workflow.read_text().split("docker build ")[1:]
        self.assertEqual(len(builds), 2)
        for build in builds:
            invocation = build.split('tee "$RESULTS/build.log"')[0]
            self.assertIn("--provenance=false", invocation)
            self.assertIn("--sbom=false", invocation)

    def test_retargeted_master_ancestor_tag_cannot_replace_release_event(self):
        with tempfile.TemporaryDirectory() as directory:
            def run(*args):
                result = subprocess.run(["git", *args], cwd=directory, capture_output=True, text=True)
                if result.returncode:
                    raise release.ReleaseError("git fixture failed")
                return result.stdout.strip()
            run("init", "-b", "master")
            earlier = ""
            for message in ("earlier approved release", "later master ancestor"):
                run("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "--allow-empty", "-m", message)
                if message.startswith("earlier"):
                    earlier = run("rev-parse", "HEAD")
                    run("tag", "v1.2.3")
            later = run("rev-parse", "HEAD")
            run("update-ref", "refs/remotes/origin/master", later)
            run("tag", "-f", "v1.2.3", later)
            with mock.patch.object(release, "git", side_effect=run), mock.patch.dict("os.environ", {"GITHUB_SHA": earlier}):
                with self.assertRaisesRegex(release.ReleaseError, "event commit"):
                    release.resolve_source(release.REPO, "release", "refs/tags/v1.2.3", "v1.2.3")
                self.assertEqual(release.resolve_source(release.REPO, "workflow_dispatch", "refs/heads/master", "v1.2.3"), later)


class ImmutableTagCollisions(unittest.TestCase):
    INDEX_TYPES = ("application/vnd.oci.image.index.v1+json",
                   "application/vnd.docker.distribution.manifest.list.v2+json")

    def client(self):
        # Exercise real Client.get, with only its transport stubbed. No login/network.
        client = release.Client.__new__(release.Client)
        client.github_token = "github-test-only"
        client.registry_token = "registry-test-only"
        return client

    def test_manifest_requests_accept_all_four_image_representations(self):
        expected = {"application/vnd.oci.image.manifest.v1+json",
                    "application/vnd.docker.distribution.manifest.v2+json",
                    "application/vnd.oci.image.index.v1+json",
                    "application/vnd.docker.distribution.manifest.list.v2+json"}
        for tag in ("1.2.3", "sha-" + SHA):
            client = self.client()
            path = release.registry_path("manifests/" + tag)
            with self.subTest(tag=tag), mock.patch.object(client, "request", return_value=response(200, {})) as request:
                self.assertEqual(client.get(path)[0], 200)
                request.assert_called_once()
                actual_path, headers = request.call_args.args
                self.assertEqual(actual_path, path)
                self.assertEqual(headers["Authorization"], "Bearer registry-test-only")
                self.assertEqual({value.strip() for value in headers["Accept"].split(",")}, expected)

    def listed_collision(self, tag):
        client = RegistrySafety().preflight_client(tags=["old", tag])
        client.responses[release.registry_path("manifests/" + tag)] = response(404, {"errors": [{
            "code": "MANIFEST_UNKNOWN", "message": "OCI index found, but Accept header does not support OCI indexes"}]})
        with self.assertRaisesRegex(release.ReleaseError, "existing immutable tag"):
            release.preflight(client, "1.2.3", SHA)
        self.assertEqual(client.calls, [release.REPO_API, release.PACKAGE_API, release.registry_path("tags/list")])

    def test_listed_version_rejected_before_misleading_manifest_404(self):
        self.listed_collision("1.2.3")

    def test_listed_full_sha_rejected_before_misleading_manifest_404(self):
        self.listed_collision("sha-" + SHA)

    def unlisted_collision(self, media_type):
        for tag in ("1.2.3", "sha-" + SHA):
            # Model a tag created after tags/list: only manifest negotiation can see it.
            responses = RegistrySafety().preflight_client(tags=["old"]).responses
            client = self.client()
            path = release.registry_path("manifests/" + tag)
            negotiated = []

            def request(actual_path, headers):
                if actual_path == path:
                    accepted = {value.strip() for value in headers["Accept"].split(",")}
                    if media_type in accepted:
                        negotiated.append(200)
                        return response(200, {"schemaVersion": 2, "mediaType": media_type, "manifests": []},
                                        {"content-type": media_type})
                    negotiated.append(404)
                    return response(404, {"errors": [{"code": "MANIFEST_UNKNOWN",
                                                      "message": "Accept header does not support stored image type"}]})
                return responses[actual_path]

            with self.subTest(tag=tag), mock.patch.object(client, "request", side_effect=request) as transport:
                with self.assertRaisesRegex(release.ReleaseError, "existing immutable tag"):
                    release.preflight(client, "1.2.3", SHA)
                self.assertEqual(negotiated, [200])
                self.assertIn(mock.call(path, mock.ANY), transport.call_args_list)

    def test_unlisted_oci_index_is_a_collision_for_either_tag(self):
        self.unlisted_collision(self.INDEX_TYPES[0])

    def test_unlisted_docker_manifest_list_is_a_collision_for_either_tag(self):
        self.unlisted_collision(self.INDEX_TYPES[1])

    def test_published_readback_still_rejects_both_index_types(self):
        for media_type in self.INDEX_TYPES:
            for tag in ("1.2.3", "sha-" + SHA):
                fixture, local = ReadbackSafety().fixture()
                body = encoded({"schemaVersion": 2, "mediaType": media_type, "manifests": []})
                path = release.registry_path("manifests/" + tag)
                fixture.responses[path] = (200, {"docker-content-digest": digest(body)}, body)
                client = self.client()
                with self.subTest(media_type=media_type, tag=tag), \
                        mock.patch.object(client, "request", side_effect=lambda path, headers: fixture.responses[path]):
                    with self.assertRaisesRegex(release.ReleaseError, "single-platform required"):
                        release.readback(client, "1.2.3", SHA, local)


class WorkflowBehavior(unittest.TestCase):
    def test_build_log_pipeline_preserves_docker_failure(self):
        workflow = pathlib.Path(__file__).resolve().parents[1] / ".github/workflows/server-image.yml"
        lines = workflow.read_text().splitlines()
        scripts = []
        for start, line in enumerate(lines):
            if line == "          set -euo pipefail":
                end = start
                while end < len(lines) and (not lines[end] or lines[end].startswith("          ")):
                    end += 1
                scripts.append("\n".join(value[10:] for value in lines[start:end]))
        # Find build blocks by their docker command as well, so removing the
        # pipefail line cannot cause the actual behavioral probes to disappear.
        if not scripts:
            for start, line in enumerate(lines):
                if line == '          mkdir -p "$RESULTS"':
                    end = start
                    while end < len(lines) and (not lines[end] or lines[end].startswith("          ")):
                        end += 1
                    scripts.append("\n".join(value[10:] for value in lines[start:end]))
        self.assertEqual(len(scripts), 2)
        for script in scripts:
            with tempfile.TemporaryDirectory() as directory:
                env = {"PATH": "/usr/bin", "RESULTS": directory, "VERSION": "1.2.3", "SOURCE_SHA": SHA,
                       "SDK_IMAGE": "sdk", "RUNTIME_IMAGE": "runtime", "LOCAL_IMAGE": "local"}
                prefix = "docker() { return 17; }; python3() { printf 'UNSAFE_CONTINUATION\\n'; };\n"
                result = subprocess.run(["bash", "-e", "-c", prefix + script],
                                        env=env, capture_output=True, text=True)
            self.assertEqual(result.returncode, 17, result.stdout + result.stderr)
            self.assertNotIn("UNSAFE_CONTINUATION", result.stdout)


class RealSourceResolution(unittest.TestCase):
    def test_existing_tag_peels_to_commit_and_rejects_non_master_source(self):
        with tempfile.TemporaryDirectory() as directory:
            def run(*args):
                result = subprocess.run(["git", *args], cwd=directory, capture_output=True, text=True)
                if result.returncode:
                    raise release.ReleaseError("git probe rejected")
                return result.stdout.strip()
            run("init", "-b", "master")
            run("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "--allow-empty", "-m", "base")
            sha = run("rev-parse", "HEAD")
            run("update-ref", "refs/remotes/origin/master", sha)
            run("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "tag", "-a", "v1.2.3", "-m", "approved")
            with mock.patch.object(release, "git", side_effect=run):
                self.assertEqual(release.resolve_source(release.REPO, "release", "refs/tags/v1.2.3", "v1.2.3", sha), sha)
                with self.assertRaises(release.ReleaseError):
                    release.resolve_source(release.REPO, "release", "refs/tags/v1.2.4", "v1.2.4", sha)
                run("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "--allow-empty", "-m", "unmerged")
                run("tag", "v1.2.4")
                unmerged_sha = run("rev-parse", "HEAD")
                with self.assertRaises(release.ReleaseError):
                    release.resolve_source(release.REPO, "release", "refs/tags/v1.2.4", "v1.2.4", unmerged_sha)


if __name__ == "__main__":
    unittest.main()
