#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-or-later
"""The point-cloud worker: watches a hand-off directory for jobs, runs one at a time, and writes
back what happened.

The tools that make a point cloud showable (py3dtiles, laspy, numpy) are Python and the application
is .NET, so they live in this container, and the application is deliberately given no way to start
containers of its own. The two meet on a shared directory instead, exactly as the terrain worker and
the application do: the application writes what it wants done, this loop does it, and writes the
outcome back beside the request.

Layout of the hand-off directory (``SILEXGIS_SCANS_ROOT``, ``/data/scans`` in the image):

    capabilities.json           which steps the workers can run, with which tool at which version.
                                Absent = no worker. Each worker rewrites its own entries (marked
                                with its role as "owner") at every start and keeps the others'.
    capabilities.lock           held while a worker rewrites capabilities.json
    defaults/                   HDMapping's default odometry parameters, dumped by the HDMapping
                                worker at start: hdmapping.odometry.toml as the program wrote it,
                                hdmapping.odometry.json with the same values for the application
    jobs/<jobId>/request.json   written by the application (to a temporary name, then renamed):
                                {"step": "...", "parameters": {...}, "inputs": {"<kind>": "<file>"},
                                 "timeoutSeconds": N}. Input files sit beside it, named by plain
                                file names and nothing else: no directory part, no "..".
    jobs/<jobId>/started        written here when the job is taken (a UTC timestamp)
    jobs/<jobId>/progress.json  {"fraction": 0..1, "message": "...", "updatedAt": "..."}
    jobs/<jobId>/worker.log     what the tool said, as it said it
    jobs/<jobId>/cancel         written by the application to call the job off; only its
                                existence matters
    jobs/<jobId>/<outputs>      the step's products (estimate.json, display.laz, tiles.zip)
    jobs/<jobId>/result.json    written here, last, to a temporary name and renamed into place:
                                {"status": "ok|failed|refused|cancelled|interrupted", "step",
                                 "reason", "outputs": [{"kind", "file", "sizeBytes", "sha256"}],
                                 "tool", "toolVersion", "startedAt", "finishedAt", "seconds",
                                 "peakRssBytes", "logTail": [...], "data": {...}}

Both files that cross the boundary are renamed into place, and both sides must do it that way: a
reader that looks while an ordinary write is half done sees a truncated file, and a truncated
request is a job of the wrong thing.

The steps:

    pointcloud.estimate     one pass over the cloud: how many points would remain at voxel sizes
                            1, 2, 3, 5 and 10 cm, with the tile size and tiling time each would
                            take, from figures measured on real recordings. Writes estimate.json.
    pointcloud.downsample   a display copy of the cloud: "voxel" keeps the point nearest the centre
                            of each cell of N cm; "everyNth" keeps every n-th point. The original
                            is never altered. Writes display.laz.
    py3dtiles.tile          3D Tiles 1.0 (.pnts) from a LAZ/LAS, packed into one zip archive. The
                            cloud is first shifted to its own centre, so the tiles carry no
                            coordinates at all — the shift is reported in the result and is the
                            application's to keep as the scan's placement. Writes tiles.zip.
    pointcloud.raster       a top view and an extended profile of the cloud as PNG pictures.
                            Writes plan.png and profile.png.
    mesh.surface            a surface of the cloud for a cave's walls: voxel means, normals turned
                            toward the scanner's trajectory, screened Poisson (Open3D), the
                            least-supported faces trimmed, decimated to a triangle count. Written
                            about the tiles' shift, like them carrying no coordinate. Writes
                            surface.stl (binary).

and, in the HDMapping worker (SILEXGIS_SCANS_WORKER_ROLE=hdmapping), for raw scanner recordings:

    hdmapping.preflight     a recording's chunks paired, every lidar header readable, and room on
                            the volume for odometry. Writes preflight.json.
    hdmapping.odometry      HDMapping's LiDAR-inertial odometry (step 1) over the recording, with
                            HDMapping's defaults and the request's "values" laid over them. Writes
                            session.zip: the session, without its cache.
    hdmapping.export        the session exported (step 2) into one cloud and the trajectory the
                            scanner travelled. Writes export.laz and trajectory.csv.
    pointcloud.check        a cloud (and, given one, a trajectory) checked before anything is built
                            on it: count, finite coordinates, bounds, time order. Writes check.json.

Three things this loop deliberately does not do: it does not judge the outcome beyond the tool's
exit code (the log is kept for the side that reads the result); it does not delete anything outside
the job directory it is working in; and it does not retry — a job it took and never finished (the
container was stopped mid-run) is answered "interrupted" rather than run again, because only the
application can decide whether its half-written outputs are worth anything.

Only the standard library is needed to run the loop and answer a request; laspy, numpy, py3dtiles
and open3d are imported when a step needs them, so the protocol can be exercised without them.
"""
from __future__ import annotations

import datetime as _dt
import hashlib
import json
import math
import os
import re
import resource
import shutil
import signal
import struct
import subprocess
import sys
import time
import zipfile
from pathlib import Path

ROOT = Path(os.environ.get("SILEXGIS_SCANS_ROOT", "/data/scans"))
POLL_SECONDS = os.environ.get("SILEXGIS_SCANS_POLL_SECONDS", "2")
# Both are read from the environment rather than from the machine: inside a container limited to
# a few processors and a slice of memory, os.cpu_count() and the free memory still report the
# host's, and py3dtiles sizes itself from exactly those two when not told otherwise.
TILE_JOBS = os.environ.get("SILEXGIS_SCANS_JOBS", "4")
TILE_CACHE_MB = os.environ.get("SILEXGIS_SCANS_CACHE_MB", "2048")
# The most one job may run, when the request does not say; the request's own figure wins.
DEFAULT_TIMEOUT_SECONDS = os.environ.get("SILEXGIS_SCANS_JOB_TIMEOUT_SECONDS", "14400")
WORKER_VERSION = os.environ.get("SILEXGIS_SCANS_WORKER_VERSION", "dev")
# How long a tool asked to stop is given before it is killed outright.
STOP_GRACE_SECONDS = 10

ESTIMATE_VOXELS_CM = (1, 2, 3, 5, 10)
# Measured on py3dtiles 12.1.1 over two real recordings (34 M and 73 M points): about 17 bytes of
# .pnts per point with intensity, 3 more with colour, 0.5–0.7 M points per second on 12 jobs, and
# about 17 s of process start-up however small the file.
TILE_BYTES_PER_POINT = 17.0
TILE_BYTES_PER_RGB_POINT = 20.0
TILE_POINTS_PER_SECOND = 600_000.0
TILE_FIXED_SECONDS = 17.0
CHUNK_POINTS = 2_000_000

STEPS = ("pointcloud.estimate", "pointcloud.downsample", "py3dtiles.tile", "pointcloud.raster", "mesh.surface")
LOG_TAIL_LINES = 30

# Which worker this is. Two may share one hand-off directory: the tiler (every installation that
# processes scans) and the HDMapping worker (installations that turn raw scanner recordings into
# point clouds). Each runs only its own steps, announces only its own entries in capabilities.json,
# and leaves the other's jobs alone — including the other's abandoned ones, which only their owner
# may call interrupted.
ROLE = os.environ.get("SILEXGIS_SCANS_WORKER_ROLE", "tiler")
HDMAPPING_STEPS = ("hdmapping.preflight", "hdmapping.odometry", "hdmapping.export", "pointcloud.check")
ROLE_STEPS = {"tiler": STEPS, "hdmapping": HDMAPPING_STEPS}
HDMAPPING_COMMIT = os.environ.get("HDMAPPING_COMMIT", "")
# HDMapping's two headless programs, found on PATH. Named by the environment so that the loop around
# them can be exercised with stand-ins where HDMapping is not built.
ODOMETRY_PROGRAM = os.environ.get("SILEXGIS_SCANS_HDM_ODOMETRY", "lidar_odometry_step_1")
EXPORT_PROGRAM = os.environ.get("SILEXGIS_SCANS_HDM_EXPORT", "multi_view_tls_registration_step_2")
# The free space odometry is refused without, as a multiple of the recording's size. Measured on two
# real recordings: step 1's cache peaks at 17–20 times the raw size inside the job directory before
# it is removed, and the session and the export add about one more each.
ODOMETRY_DISK_FACTOR = 22
# HDMapping's default parameters, dumped by the program itself at start and kept beside
# capabilities.json, so the application can store a run's parameters resolved against the exact
# version that will run them.
DEFAULTS_DIRECTORY = "defaults"
ODOMETRY_DEFAULTS_NAME = "hdmapping.odometry"
# Set by publish_defaults() when the dump worked; the HDMapping steps are announced only then.
PUBLISHED_DEFAULTS: dict | None = None
# Lines of a tool's output that are not passed on in a result's log tail. HDMapping's export prints
# the session's extent, which in a georeferenced session is a real position; the log on the volume
# keeps it, the result (which the application may show to anyone who can see the upload) does not.
WITHHELD_LINE = re.compile(r"^(\s*[XYZ] \((?:length|width|height)\)\s*:).*$")


def now() -> str:
    return _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def say(message: str) -> None:
    print(f"{now()} {message}", flush=True)


def write_whole(path: Path, text: str) -> None:
    """Writes under a temporary name and renames into place, so a reader never sees half a file."""
    part = path.with_name(path.name + ".part")
    part.write_text(text, encoding="utf-8")
    os.replace(part, path)


def write_json(path: Path, value: object) -> None:
    write_whole(path, json.dumps(value, indent=1) + "\n")


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def plain_file_name(name: object) -> bool:
    """A name the request may use for a file inside the job directory: no path at all."""
    return isinstance(name, str) and name != "" and name not in (".", "..") \
        and "/" not in name and "\\" not in name and "\0" not in name


class Refused(Exception):
    """The request asks for something this worker will not do; nothing was run."""


class Cancelled(Exception):
    """The application called the job off while it ran."""


class TimedOut(Exception):
    """The job reached its time limit and was stopped."""


class ToolFailed(Exception):
    """The tool ran and exited non-zero."""


class CheckFailed(ToolFailed):
    """What the step was given, or what a tool made, does not pass the step's checks."""


def tool_versions() -> dict[str, str]:
    versions: dict[str, str] = {}
    try:
        from importlib.metadata import version
        for package in ("py3dtiles", "laspy", "lazrs", "numpy"):
            try:
                versions[package] = version(package)
            except Exception:  # noqa: BLE001 - absent is a fact to report, not an error
                pass
        # Open3D is installed as its CPU-only build, which is the same module under another
        # distribution name; either is reported as open3d.
        for distribution in ("open3d-cpu", "open3d"):
            try:
                versions["open3d"] = version(distribution)
                break
            except Exception:  # noqa: BLE001
                pass
    except Exception:  # noqa: BLE001
        pass
    return versions


def hdmapping_version(software_version: object) -> str:
    """HDMapping's version as recorded per run: the release it reports, at the commit it was built
    from, because between releases the command line and the parameters change under one number."""
    version = str(software_version or "unknown")
    return f"{version}@{HDMAPPING_COMMIT}" if HDMAPPING_COMMIT else version


def capabilities() -> dict:
    """This worker's own announcement. Every step entry names this worker as its owner."""
    versions = tool_versions()
    steps = []
    if ROLE == "tiler":
        if "laspy" in versions and "numpy" in versions:
            steps.append({"id": "pointcloud.estimate", "tool": "laspy", "version": versions["laspy"]})
            steps.append({"id": "pointcloud.downsample", "tool": "laspy", "version": versions["laspy"]})
            steps.append({"id": "pointcloud.raster", "tool": "numpy", "version": versions["numpy"]})
        if "py3dtiles" in versions:
            steps.append({"id": "py3dtiles.tile", "tool": "py3dtiles", "version": versions["py3dtiles"]})
        if "open3d" in versions and "laspy" in versions and "numpy" in versions:
            steps.append({"id": SURFACE_STEP, "tool": "open3d", "version": versions["open3d"]})
    elif ROLE == "hdmapping" and PUBLISHED_DEFAULTS is not None:
        version = hdmapping_version(PUBLISHED_DEFAULTS.get("softwareVersion"))
        steps.append({"id": "hdmapping.preflight", "tool": "scan-worker", "version": WORKER_VERSION})
        steps.append({"id": "hdmapping.odometry", "tool": "hdmapping", "version": version,
                      "defaults": f"{DEFAULTS_DIRECTORY}/{ODOMETRY_DEFAULTS_NAME}.json"})
        if shutil.which(EXPORT_PROGRAM):
            steps.append({"id": "hdmapping.export", "tool": "hdmapping", "version": version})
        if "laspy" in versions and "numpy" in versions:
            steps.append({"id": "pointcloud.check", "tool": "laspy", "version": versions["laspy"]})
    for step in steps:
        step["owner"] = ROLE
    value = {
        "worker": WORKER_VERSION,
        "writtenAt": now(),
        "steps": steps,
        "tools": versions,
        "limits": {
            "tileJobs": int(TILE_JOBS),
            "tileCacheMb": int(TILE_CACHE_MB),
            "defaultTimeoutSeconds": int(DEFAULT_TIMEOUT_SECONDS),
        },
    }
    if HDMAPPING_COMMIT:
        value["hdmapping"] = HDMAPPING_COMMIT
    return value


def merge_capabilities(own: dict, earlier: object, role: str) -> dict:
    """The announcement to write: this worker's entries, plus every entry another worker owns.

    Entries without an owner come from a worker that wrote the file alone and are dropped: whoever
    wrote them, they are this worker's to replace or nobody's. A step this worker announces is its
    own even when another worker's entry names it too."""
    own_ids = {step["id"] for step in own.get("steps", [])}
    others: list = []
    workers: dict = {}
    if isinstance(earlier, dict):
        for step in earlier.get("steps") or []:
            if isinstance(step, dict) and step.get("owner") not in (None, role) and step.get("id") not in own_ids:
                others.append(step)
        if isinstance(earlier.get("workers"), dict):
            workers = {name: entry for name, entry in earlier["workers"].items() if name != role}
    workers[role] = {key: own[key] for key in ("worker", "writtenAt", "tools", "limits", "hdmapping") if key in own}
    merged = dict(own)
    merged["steps"] = list(own.get("steps", [])) + others
    merged["workers"] = workers
    return merged


def announce() -> dict:
    """Writes this worker's entries into capabilities.json, keeping the other workers' entries.

    Two workers starting together would each read the file, add their own and write it back, and
    the later write would drop the earlier's entries; a lock on the volume puts the two in turn."""
    own = capabilities()
    with (ROOT / "capabilities.lock").open("a") as lock:
        try:
            import fcntl
            fcntl.flock(lock, fcntl.LOCK_EX)
        except ImportError:  # not on Linux: a lone worker needs no lock
            pass
        try:
            earlier = json.loads((ROOT / "capabilities.json").read_text(encoding="utf-8"))
        except (OSError, ValueError):
            earlier = None
        merged = merge_capabilities(own, earlier, ROLE)
        write_json(ROOT / "capabilities.json", merged)
    return own


def steps_of_other_workers() -> set[str]:
    try:
        announced = json.loads((ROOT / "capabilities.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return set()
    if not isinstance(announced, dict):
        return set()
    return {step.get("id") for step in announced.get("steps") or []
            if isinstance(step, dict) and step.get("owner") not in (None, ROLE)}


class Job:
    """One job directory, and everything written into it."""

    def __init__(self, directory: Path) -> None:
        self.directory = directory
        self.request: dict = {}
        self.log_path = directory / "worker.log"
        self.started_at = now()
        self.clock = time.monotonic()
        self.timeout = float(DEFAULT_TIMEOUT_SECONDS)
        self.outputs: list[dict] = []
        self.data: dict = {}
        self.tool = ""
        self.tool_version = ""

    # -- the small things every step shares --------------------------------------------------

    def log(self, line: str) -> None:
        with self.log_path.open("a", encoding="utf-8") as handle:
            handle.write(f"{now()} {line}\n")

    def progress(self, fraction: float, message: str) -> None:
        write_json(self.directory / "progress.json", {
            "fraction": max(0.0, min(1.0, float(fraction))),
            "message": message,
            "updatedAt": now(),
        })

    def check(self) -> None:
        """Called from inside every long loop: stops the job when it was called off or ran too long."""
        if (self.directory / "cancel").exists():
            raise Cancelled()
        if time.monotonic() - self.clock > self.timeout:
            raise TimedOut()

    def input(self, kind: str) -> Path:
        inputs = self.request.get("inputs")
        if not isinstance(inputs, dict) or kind not in inputs:
            raise Refused(f"the request names no input of kind {kind}")
        name = inputs[kind]
        if not plain_file_name(name):
            raise Refused(f"input {kind} is not a plain file name")
        path = self.directory / name
        if not path.is_file():
            raise Refused(f"input {kind} ({name}) is not a file in the job directory")
        return path

    def parameter(self, name: str, default):
        parameters = self.request.get("parameters")
        if isinstance(parameters, dict) and name in parameters and parameters[name] is not None:
            return parameters[name]
        return default

    def produced(self, kind: str, path: Path) -> None:
        self.outputs.append({
            "kind": kind,
            "file": path.name,
            "sizeBytes": path.stat().st_size,
            "sha256": sha256_of(path),
        })

    def run_tool(self, command: list[str], progress_from_line=None, env: dict | None = None) -> None:
        """Runs a command line into worker.log, watching for a withdrawal and the time limit."""
        self.log("running: " + " ".join(command))
        with self.log_path.open("ab") as log:
            try:
                process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT,
                                           cwd=self.directory, start_new_session=True, env=env)
            except OSError as error:
                raise ToolFailed(f"{command[0]} could not be started: {error.strerror}") from error
            try:
                while process.poll() is None:
                    try:
                        self.check()
                    except (Cancelled, TimedOut):
                        self._stop(process)
                        raise
                    if progress_from_line is not None:
                        last = self.last_log_line()
                        if last:
                            progress_from_line(last)
                    time.sleep(1)
            finally:
                if process.poll() is None:
                    self._stop(process)
        if process.returncode != 0:
            raise ToolFailed(f"{command[0]} exited {process.returncode}; its own account is in worker.log")

    @staticmethod
    def _stop(process: subprocess.Popen) -> None:
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            return
        waited = 0
        while process.poll() is None and waited < STOP_GRACE_SECONDS:
            time.sleep(1)
            waited += 1
        if process.poll() is None:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()

    def last_log_line(self) -> str:
        try:
            with self.log_path.open("rb") as handle:
                handle.seek(0, os.SEEK_END)
                size = handle.tell()
                handle.seek(max(0, size - 4096))
                tail = handle.read().decode("utf-8", "replace")
        except OSError:
            return ""
        lines = [line for line in tail.replace("\r", "\n").split("\n") if line.strip()]
        return lines[-1] if lines else ""

    def log_tail(self) -> list[str]:
        try:
            lines = self.log_path.read_text(encoding="utf-8", errors="replace").splitlines()
        except OSError:
            return []
        return [withheld(line) for line in lines[-LOG_TAIL_LINES:]]


def withheld(line: str) -> str:
    return WITHHELD_LINE.sub(r"\1 [withheld]", line)


# -- the steps ---------------------------------------------------------------------------------

def read_cloud_summary(reader) -> dict:
    header = reader.header
    return {
        "pointCount": int(header.point_count),
        "pointFormat": int(header.point_format.id),
        "hasRgb": "red" in header.point_format.dimension_names,
        # Every LAS point format carries an intensity field, so its presence says nothing about
        # whether the scanner wrote one; "hasIntensity" is answered by the points (see
        # IntensityWatch) and this is only whether the field exists to be read.
        "intensityField": "intensity" in header.point_format.dimension_names,
        "min": [float(v) for v in header.mins],
        "max": [float(v) for v in header.maxs],
    }


class IntensityWatch:
    """Whether any point seen so far carries a non-zero intensity.

    A cloud converted from a mesh, or written by a scanner that records none, has the field in
    every record and zero in every one of them; tiled with intensity it would render black. So
    the field counts as present only once a value in it is not zero, measured in whichever pass
    over the points a step already makes."""

    def __init__(self, summary: dict) -> None:
        self.field = bool(summary.get("intensityField"))
        self.seen = False

    def look(self, chunk) -> None:
        if self.field and not self.seen:
            import numpy as np
            self.seen = bool(np.any(np.asarray(chunk.intensity)))

    @property
    def has_intensity(self) -> bool:
        return self.field and self.seen


def ensure_extent(summary: dict, source: Path, job: Job) -> None:
    """Fills in the cloud's extent from its points when the header's bounding box cannot be used.

    The LAS header carries the cloud's min and max, and every grid here is laid over that box.
    Some writers leave the box at zero (a published cave scan converted from a mesh does), and a
    grid over a zero box is two cells wide: every point lands in the same few cells and the voxel
    counts come out a hundred times too small. So a box that is empty on any axis is replaced by
    one measured from the points, in one extra pass over the file."""
    import laspy
    import numpy as np

    if summary["pointCount"] == 0 or all(summary["max"][i] > summary["min"][i] for i in range(3)):
        return
    job.log("the header's bounding box is empty; measuring the extent from the points")
    mins = np.full(3, np.inf)
    maxs = np.full(3, -np.inf)
    with laspy.open(source) as reader:
        for chunk in reader.chunk_iterator(CHUNK_POINTS):
            job.check()
            xyz = np.column_stack((np.asarray(chunk.x), np.asarray(chunk.y), np.asarray(chunk.z)))
            mins = np.minimum(mins, xyz.min(axis=0))
            maxs = np.maximum(maxs, xyz.max(axis=0))
    summary["min"] = [float(v) for v in mins]
    summary["max"] = [float(v) for v in maxs]


def voxel_grid(mins, maxs, voxel_m: float):
    """The cell counts of a grid over the extent, or None when a flat key would not fit in 63 bits."""
    import numpy as np
    counts = [int(np.floor((maxs[i] - mins[i]) / voxel_m)) + 2 for i in range(3)]
    if counts[0] * counts[1] * counts[2] >= (1 << 62):
        return None
    return counts


def voxel_keys(xyz, mins, counts, voxel_m: float):
    """One int64 per point naming the cell it falls in."""
    import numpy as np
    ix = np.floor((xyz[:, 0] - mins[0]) / voxel_m).astype(np.int64)
    iy = np.floor((xyz[:, 1] - mins[1]) / voxel_m).astype(np.int64)
    iz = np.floor((xyz[:, 2] - mins[2]) / voxel_m).astype(np.int64)
    return ix + counts[0] * (iy + counts[1] * iz)


def tile_estimate(points: int, has_rgb: bool) -> dict:
    per_point = TILE_BYTES_PER_RGB_POINT if has_rgb else TILE_BYTES_PER_POINT
    return {
        "estimatedTileBytes": int(points * per_point),
        "estimatedSeconds": int(TILE_FIXED_SECONDS + points / TILE_POINTS_PER_SECOND),
    }


def step_estimate(job: Job) -> None:
    import laspy
    import numpy as np

    job.tool, job.tool_version = "laspy", tool_versions().get("laspy", "")
    source = job.input("pointCloudLaz")
    sizes_cm = job.parameter("voxelsCm", list(ESTIMATE_VOXELS_CM))
    if not isinstance(sizes_cm, list) or not all(isinstance(s, (int, float)) and s > 0 for s in sizes_cm):
        raise Refused("voxelsCm must be a list of positive numbers")

    with laspy.open(source) as reader:
        summary = read_cloud_summary(reader)
        ensure_extent(summary, source, job)
        total = summary["pointCount"]
        mins, maxs = summary["min"], summary["max"]
        grids = {s: voxel_grid(mins, maxs, s / 100.0) for s in sizes_cm}
        # Every voxel size is counted in one read of the file: each chunk's cells are reduced to
        # their unique keys (a dense scan collapses a lot here), kept, and counted once at the end.
        kept: dict = {s: [] for s in sizes_cm if grids[s] is not None}
        intensity = IntensityWatch(summary)
        done = 0
        for chunk in reader.chunk_iterator(CHUNK_POINTS):
            job.check()
            intensity.look(chunk)
            xyz = np.column_stack((np.asarray(chunk.x), np.asarray(chunk.y), np.asarray(chunk.z)))
            for s in kept:
                kept[s].append(np.unique(voxel_keys(xyz, mins, grids[s], s / 100.0)))
            done += len(chunk)
            job.progress(0.9 * done / max(1, total), f"counted {done:,} of {total:,} points")

    rows = []
    for s in sizes_cm:
        if grids[s] is None:
            rows.append({"voxelCm": s, "points": None, "estimatedTileBytes": None, "estimatedSeconds": None})
            continue
        remaining = int(np.unique(np.concatenate(kept[s])).shape[0]) if kept[s] else 0
        rows.append({"voxelCm": s, "points": remaining, **tile_estimate(remaining, summary["hasRgb"])})
    estimate = {
        "pointCount": total,
        "hasRgb": summary["hasRgb"],
        "hasIntensity": intensity.has_intensity,
        "full": {"points": total, **tile_estimate(total, summary["hasRgb"])},
        "voxels": rows,
        "model": {
            "tileBytesPerPoint": TILE_BYTES_PER_RGB_POINT if summary["hasRgb"] else TILE_BYTES_PER_POINT,
            "tilePointsPerSecond": TILE_POINTS_PER_SECOND,
            "tileFixedSeconds": TILE_FIXED_SECONDS,
        },
    }
    out = job.directory / "estimate.json"
    write_json(out, estimate)
    job.produced("estimate", out)
    job.data = estimate


def step_downsample(job: Job) -> None:
    import laspy
    import numpy as np

    job.tool, job.tool_version = "laspy", tool_versions().get("laspy", "")
    source = job.input("pointCloudLaz")
    method = job.parameter("method", "voxel")
    out = job.directory / "display.laz"

    with laspy.open(source) as reader:
        summary = read_cloud_summary(reader)
        ensure_extent(summary, source, job)
        total = summary["pointCount"]
        mins, maxs = summary["min"], summary["max"]
        header = laspy.LasHeader(point_format=reader.header.point_format, version=reader.header.version)
        header.scales = reader.header.scales
        header.offsets = reader.header.offsets
        for vlr in reader.header.vlrs:
            header.vlrs.append(vlr)

        if method == "voxel":
            voxel_cm = job.parameter("voxelCm", 2)
            if not isinstance(voxel_cm, (int, float)) or voxel_cm <= 0:
                raise Refused("voxelCm must be a positive number")
            voxel_m = voxel_cm / 100.0
            counts = voxel_grid(mins, maxs, voxel_m)
            if counts is None:
                raise Refused(f"the cloud's extent is too large for a {voxel_cm} cm grid")
            keep = _voxel_filter(job, reader, header, out, mins, counts, voxel_m, total)
            job.data = {"method": "voxel", "voxelCm": voxel_cm, "pointsIn": total, "pointsOut": keep}
        elif method == "everyNth":
            n = job.parameter("everyNth", 2)
            if not isinstance(n, int) or n < 1:
                raise Refused("everyNth must be a whole number of at least 1")
            keep = 0
            done = 0
            with laspy.open(out, mode="w", header=header) as writer:
                for chunk in reader.chunk_iterator(CHUNK_POINTS):
                    job.check()
                    # The stride is carried across chunks, so "every n-th" means every n-th of
                    # the whole cloud and not of each chunk.
                    first = (-done) % n
                    selected = chunk[np.arange(first, len(chunk), n)]
                    writer.write_points(selected)
                    keep += len(selected)
                    done += len(chunk)
                    job.progress(0.95 * done / max(1, total), f"kept {keep:,} of {done:,} points")
            job.data = {"method": "everyNth", "everyNth": n, "pointsIn": total, "pointsOut": keep}
        else:
            raise Refused(f"downsample method '{method}' is not one this worker runs (voxel, everyNth)")

    job.produced("pointCloudLazDisplay", out)


def _voxel_filter(job: Job, reader, header, out: Path, mins, counts, voxel_m: float, total: int) -> int:
    """One point per cell: the one nearest the cell's centre within a chunk, and the first chunk
    to reach a cell keeps it. A cell straddling two chunks therefore keeps the earlier chunk's
    nearest point rather than the nearest of both, which for a display copy is beside the point."""
    import laspy
    import numpy as np

    seen = np.empty(0, dtype=np.int64)
    keep = 0
    done = 0
    with laspy.open(out, mode="w", header=header) as writer:
        for chunk in reader.chunk_iterator(CHUNK_POINTS):
            job.check()
            xyz = np.column_stack((np.asarray(chunk.x), np.asarray(chunk.y), np.asarray(chunk.z)))
            keys = voxel_keys(xyz, mins, counts, voxel_m)
            centre = (np.floor((xyz - mins) / voxel_m) + 0.5) * voxel_m + mins
            distance = np.sum((xyz - centre) ** 2, axis=1)
            # Sort by cell, then by distance to the centre: the first index of each cell is its
            # nearest point.
            order = np.lexsort((distance, keys))
            sorted_keys = keys[order]
            first = np.concatenate(([True], sorted_keys[1:] != sorted_keys[:-1]))
            candidates = order[first]
            candidate_keys = sorted_keys[first]
            fresh = ~np.isin(candidate_keys, seen, assume_unique=True)
            chosen = np.sort(candidates[fresh])
            if chosen.size:
                writer.write_points(chunk[chosen])
                seen = np.union1d(seen, candidate_keys[fresh])
                keep += int(chosen.size)
            done += len(chunk)
            job.progress(0.95 * done / max(1, total), f"kept {keep:,} of {done:,} points")
    return keep


def step_tile(job: Job) -> None:
    import laspy
    import numpy as np

    job.tool, job.tool_version = "py3dtiles", tool_versions().get("py3dtiles", "")
    inputs = job.request.get("inputs") or {}
    # The display copy, when the recipe made one, is what gets tiled; the original otherwise.
    source = job.input("pointCloudLazDisplay" if "pointCloudLazDisplay" in inputs else "pointCloudLaz")
    spec = str(job.parameter("specVersion", "1.0"))
    if spec not in ("1.0", "1.1"):
        raise Refused("specVersion must be 1.0 or 1.1")
    want_rgb = bool(job.parameter("rgb", True))
    want_intensity = bool(job.parameter("intensity", True))

    # Shift the cloud to its own centre before tiling, whatever its frame. A georeferenced file
    # would otherwise put its real coordinates into every tile and into the tileset's root
    # transform; shifting every file the same way means no file is special, and the shift is
    # reported so the application can keep it as the scan's placement.
    job.progress(0.02, "recentring the cloud")
    local = job.directory / "local.laz"
    with laspy.open(source) as reader:
        summary = read_cloud_summary(reader)
        ensure_extent(summary, source, job)
        total = summary["pointCount"]
        centre = [float(np.floor((summary["min"][i] + summary["max"][i]) / 2.0)) for i in range(3)]
        header = laspy.LasHeader(point_format=reader.header.point_format, version=reader.header.version)
        header.scales = reader.header.scales
        # The stored integers are kept; only the offset that turns them into metres changes. The
        # records are handed over packed (raw integers) on purpose: given scale-aware records,
        # laspy would rescale them to keep their real-world coordinates, which is the opposite of
        # what is wanted here, and overflow the integers of a georeferenced file doing it.
        header.offsets = np.asarray(reader.header.offsets, dtype=np.float64) - np.asarray(centre)
        intensity = IntensityWatch(summary)
        done = 0
        with laspy.open(local, mode="w", header=header) as writer:
            for chunk in reader.chunk_iterator(CHUNK_POINTS):
                job.check()
                intensity.look(chunk)
                writer.write_points(laspy.PackedPointRecord(chunk.array, chunk.point_format))
                done += len(chunk)
                job.progress(0.02 + 0.08 * done / max(1, total), f"recentred {done:,} of {total:,} points")
    has_rgb = summary["hasRgb"]
    # Measured on the cloud being tiled: an all-zero field is not tiled, and is not reported.
    has_intensity = intensity.has_intensity

    tiles = job.directory / "tiles"
    if tiles.exists():
        shutil.rmtree(tiles)
    command = [
        "py3dtiles", "convert", str(local), "--out", str(tiles),
        "--jobs", str(int(TILE_JOBS)), "--cache_size", str(int(TILE_CACHE_MB)),
        "--spec-version", spec,
    ]
    if want_intensity and has_intensity:
        command += ["--extra-fields", "intensity"]
    if not (want_rgb and has_rgb):
        command += ["--no-rgb"]

    def progress_from_line(line: str) -> None:
        # py3dtiles prints its own percentage as it goes; it is reported as 10 % to 85 % here.
        head = line.strip().split("%")[0].strip().split()
        if head and head[-1].replace(".", "", 1).isdigit():
            try:
                job.progress(0.10 + 0.75 * float(head[-1]) / 100.0, f"tiling: {line.strip()[:120]}")
            except ValueError:
                pass

    job.progress(0.10, "tiling the cloud")
    job.run_tool(command, progress_from_line)
    local.unlink(missing_ok=True)

    if not (tiles / "tileset.json").is_file():
        raise ToolFailed("py3dtiles exited 0 but wrote no tileset.json")
    job.progress(0.88, "packing the tiles")
    archive = job.directory / "tiles.zip"
    tile_count = 0
    tile_bytes = 0
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=1) as pack:
        for path in sorted(tiles.rglob("*")):
            if path.is_file():
                job.check()
                pack.write(path, path.relative_to(tiles).as_posix())
                tile_count += 1
                tile_bytes += path.stat().st_size
    shutil.rmtree(tiles)
    job.produced("pointCloudTiles", archive)
    job.data = {
        "pointCount": total,
        "originShift": centre,
        "localMin": [summary["min"][i] - centre[i] for i in range(3)],
        "localMax": [summary["max"][i] - centre[i] for i in range(3)],
        "specVersion": spec,
        "rgb": bool(want_rgb and has_rgb),
        "intensity": bool(want_intensity and has_intensity),
        "tileCount": tile_count,
        "tileBytes": tile_bytes,
        "archiveBytes": archive.stat().st_size,
    }


# -- raw scanner recordings and HDMapping ------------------------------------------------------
#
# A raw MANDEYE recording reaches a job as its files, copied into the job directory under their own
# names, and one manifest naming them: inputs {"rawRecording": "recording.json"}, where
# recording.json is {"files": ["lidar0000.laz", "imu0000.csv", ...]}. The plain-name rule holds for
# every one of them, so a recording is hundreds of plain files and never a path.
#
# HDMapping's runs are long (longer than the recording lasted) and are split into stages, each
# marked done in stages.json as it finishes. A worker restarted between two stages resumes after the
# last one done; a worker restarted in the middle of one answers the job "interrupted" and names the
# stage, because a half-run odometry leaves a session that looks whole and is not.

RECORDING_CHUNKED = (
    (re.compile(r"^lidar(\d{1,6})\.laz$", re.IGNORECASE), "lidar"),
    (re.compile(r"^imu(\d{1,6})\.csv$", re.IGNORECASE), "imu"),
    (re.compile(r"^lidar(\d{1,6})\.sn$", re.IGNORECASE), "serial"),
    (re.compile(r"^gnss(\d{1,6})\.gnss$", re.IGNORECASE), "gnss"),
)
STAGED_STEPS = ("hdmapping.odometry", "hdmapping.export")
STAGES_FILE = "stages.json"


def classify_recording_file(name: str) -> tuple[str, int | None] | None:
    """What a file of a MANDEYE recording is, with its chunk number; None for a name a recording
    does not hold. The same names the application accepts on upload."""
    for pattern, kind in RECORDING_CHUNKED:
        match = pattern.match(name)
        if match:
            return kind, int(match.group(1))
    if name.lower().endswith(".nmea"):
        return "nmea", None
    if name.lower() == "calibration.json":
        return "calibration", None
    return None


def pair_recording(names: list[str]) -> dict:
    """Classifies a recording's file names and pairs its lidar chunks with its IMU logs.

    Raises Refused for a name no recording holds (the request is wrong) and CheckFailed for a
    recording the odometry cannot read (a chunk without its pair)."""
    lidar: dict[int, str] = {}
    imu: dict[int, str] = {}
    seen: set[str] = set()
    gnss = False
    for name in names:
        if not plain_file_name(name):
            raise Refused("a file of the recording is not named by a plain file name")
        kind = classify_recording_file(name)
        if kind is None:
            raise Refused(f"'{name[:80]}' is not a file of a MANDEYE recording")
        if name.lower() in seen:
            raise Refused(f"'{name[:80]}' is named twice")
        seen.add(name.lower())
        if kind[0] == "lidar":
            lidar[kind[1]] = name
        elif kind[0] == "imu":
            imu[kind[1]] = name
        elif kind[0] in ("gnss", "nmea"):
            gnss = True
    if not lidar or set(lidar) != set(imu):
        raise CheckFailed("the recording does not pair every lidar chunk with the IMU log of the same number")
    return {"lidar": lidar, "imu": imu, "gnss": gnss, "names": list(names)}


def las_point_count(path: Path) -> int:
    """The point count a LAS or LAZ file's public header declares, read with nothing but struct:
    the header is uncompressed even inside a LAZ file."""
    with path.open("rb") as handle:
        head = handle.read(375)
    if len(head) < 227 or head[:4] != b"LASF":
        raise CheckFailed(f"{path.name} is not a LAS or LAZ file: its header does not read")
    major, minor = head[24], head[25]
    header_size = struct.unpack_from("<H", head, 94)[0]
    legacy = struct.unpack_from("<I", head, 107)[0]
    if major != 1 or header_size < 227:
        raise CheckFailed(f"{path.name} has a LAS header this worker does not read (version {major}.{minor})")
    if minor >= 4 and len(head) >= 255 and header_size >= 375:
        return struct.unpack_from("<Q", head, 247)[0] or legacy
    return legacy


def read_recording(job: "Job") -> dict:
    """The recording the request names: its manifest read, every file found in the job directory."""
    manifest = job.input("rawRecording")
    try:
        value = json.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise Refused(f"the recording's manifest cannot be read: {error}") from error
    names = value.get("files") if isinstance(value, dict) else None
    if not isinstance(names, list) or not names or not all(isinstance(n, str) for n in names):
        raise Refused("the recording's manifest names no files")
    recording = pair_recording(names)
    paths = []
    for name in names:
        path = job.directory / name
        if not path.is_file():
            raise Refused(f"the recording's file {name} is not in the job directory")
        paths.append(path)
    recording["paths"] = paths
    recording["bytes"] = sum(path.stat().st_size for path in paths)
    return recording


def preflight_checks(job: "Job", recording: dict) -> dict:
    """What has to hold before odometry is worth starting: every chunk paired (done when the
    recording was read), every lidar chunk's header readable, and room on the volume for step 1's
    cache, which it writes beside its input and which peaks at about twenty times the recording."""
    points = 0
    for chunk in sorted(recording["lidar"]):
        job.check()
        points += las_point_count(job.directory / recording["lidar"][chunk])
    if points == 0:
        raise CheckFailed("the recording's lidar chunks hold no points")
    free = shutil.disk_usage(job.directory).free
    needed = recording["bytes"] * ODOMETRY_DISK_FACTOR
    facts = {
        "chunks": len(recording["lidar"]),
        "files": len(recording["names"]),
        "rawBytes": recording["bytes"],
        "points": points,
        "gnss": recording["gnss"],
        "freeBytes": free,
        "neededBytes": needed,
    }
    if free < needed:
        raise CheckFailed(f"the scans volume has {free:,} bytes free and odometry needs about {needed:,} "
                          f"({ODOMETRY_DISK_FACTOR} times the recording) for its working files")
    return facts


def step_preflight(job: "Job") -> None:
    job.tool, job.tool_version = "scan-worker", WORKER_VERSION
    recording = read_recording(job)
    job.progress(0.1, "reading the chunks' headers")
    job.data = preflight_checks(job, recording)
    out = job.directory / "preflight.json"
    write_json(out, job.data)
    job.produced("report", out)


class Stages:
    """The stages of a long step, each marked done in stages.json as it finishes, with what the
    step has learnt so far, so a resumed job carries on with it."""

    def __init__(self, job: "Job") -> None:
        self.job = job
        self.path = job.directory / STAGES_FILE
        self.state = read_stages(job.directory) or {"done": [], "running": None, "data": {}}
        job.data = dict(self.state.get("data") or {}) | job.data

    def run(self, name: str, work) -> None:
        if name in self.state["done"]:
            return
        self.state["running"] = name
        self._save()
        work()
        self.state["done"].append(name)
        self.state["running"] = None
        self._save()

    def _save(self) -> None:
        self.state["data"] = self.job.data
        write_json(self.path, self.state)


def read_stages(directory: Path) -> dict | None:
    try:
        state = json.loads((directory / STAGES_FILE).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    if not isinstance(state, dict) or not isinstance(state.get("done"), list):
        return None
    return state


def finite(value):
    """A value JSON can carry: TOML allows inf and nan, JSON does not."""
    if isinstance(value, float) and not math.isfinite(value):
        return str(value)
    if isinstance(value, dict):
        return {k: finite(v) for k, v in value.items()}
    if isinstance(value, list):
        return [finite(v) for v in value]
    return value


def toml_value(value) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float):
        if math.isnan(value):
            return "nan"
        if math.isinf(value):
            return "inf" if value > 0 else "-inf"
        return repr(value)
    if isinstance(value, str):
        # A JSON string is a TOML basic string: the same quotes and the same escapes.
        return json.dumps(value, ensure_ascii=False)
    if isinstance(value, list):
        return "[" + ", ".join(toml_value(v) for v in value) + "]"
    raise ValueError(f"a {type(value).__name__} has no place in HDMapping's parameters")


def toml_key(key: str) -> str:
    return key if re.fullmatch(r"[A-Za-z0-9_-]+", key) else json.dumps(key, ensure_ascii=False)


def toml_text(values: dict) -> str:
    """HDMapping's parameter file from its values: one table per section, as the program writes it."""
    lines = ["# HDMapping parameters, written by the SilexGIS scan worker from HDMapping's own",
             "# defaults and the run's settings."]
    for key, value in values.items():
        if not isinstance(value, dict):
            lines.append(f"{toml_key(key)} = {toml_value(value)}")
    for section, table in values.items():
        if isinstance(table, dict):
            lines.append("")
            lines.append(f"[{toml_key(section)}]")
            for key, value in table.items():
                lines.append(f"{toml_key(key)} = {toml_value(value)}")
    return "\n".join(lines) + "\n"


def _same_kind(default, value) -> bool:
    if isinstance(default, bool):
        return isinstance(value, bool)
    if isinstance(default, int):
        return isinstance(value, int) and not isinstance(value, bool)
    if isinstance(default, float):
        return isinstance(value, (int, float)) and not isinstance(value, bool)
    if isinstance(default, list):
        return isinstance(value, list) and all(
            _same_kind(default[0], v) if default else isinstance(v, (int, float, bool, str)) for v in value)
    return False


def _kind_name(default) -> str:
    if isinstance(default, bool):
        return "true or false"
    if isinstance(default, int):
        return "a whole number"
    if isinstance(default, float):
        return "a number"
    if isinstance(default, list):
        return "a list"
    return "unchanged"


def resolve_parameters(defaults: dict, overrides) -> tuple[dict, list[str]]:
    """HDMapping's defaults with a run's settings laid over them, and the keys the settings changed.

    Every key set must be one the defaults hold, with a value of the default's kind; anything else
    is refused, naming the key, because HDMapping reads an unknown key as nothing and a misspelt
    setting would otherwise run silently with its default. [version_info] is always the program's
    own, whatever the settings say: the values were resolved for some version, and the one that
    runs is the one that counts. Text values (the version, and directories the program writes to)
    cannot be changed at all: a directory outside the job's own is not for a request to name."""
    resolved = json.loads(json.dumps(defaults))  # a deep copy; the dump is plain data
    changed: list[str] = []
    if overrides is None:
        return resolved, changed
    if not isinstance(overrides, dict):
        raise Refused("values must be an object of HDMapping parameter sections")
    for section, table in overrides.items():
        if section == "version_info":
            continue
        if not isinstance(defaults.get(section), dict) or not isinstance(table, dict):
            raise Refused(f"[{section}] is not a section of HDMapping's odometry parameters")
        for key, value in table.items():
            if key not in defaults[section]:
                raise Refused(f"{section}.{key} is not one of HDMapping's odometry parameters")
            default = defaults[section][key]
            if isinstance(default, str):
                if value != default:
                    raise Refused(f"{section}.{key} cannot be set")
                continue
            if not _same_kind(default, value):
                raise Refused(f"{section}.{key} must be {_kind_name(default)}")
            if isinstance(value, (int, float)) and not isinstance(value, bool) and not math.isfinite(float(value)):
                raise Refused(f"{section}.{key} must be a finite number")
            if isinstance(default, float):
                value = float(value)
            elif isinstance(default, list) and default and isinstance(default[0], float):
                value = [float(v) for v in value]
            if value != default:
                changed.append(f"{section}.{key}")
            resolved[section][key] = value
    return resolved, changed


def read_defaults(path: Path) -> dict:
    import tomllib
    text = path.read_text(encoding="utf-8")
    try:
        values = tomllib.loads(text)
    except tomllib.TOMLDecodeError as error:
        raise ToolFailed(f"{ODOMETRY_PROGRAM} wrote default parameters that do not read: {error}") from error
    version = (values.get("version_info") or {}).get("software_version", "")
    return {"toml": text, "values": values, "softwareVersion": str(version)}


def publish_defaults() -> bool:
    """Dumps HDMapping's default parameters beside capabilities.json, as the program's own TOML and
    as JSON for the application. False (and the HDMapping steps not announced) when the program
    cannot be run: a worker that cannot dump its defaults cannot run odometry either."""
    global PUBLISHED_DEFAULTS
    folder = ROOT / DEFAULTS_DIRECTORY
    folder.mkdir(parents=True, exist_ok=True)
    dumped = folder / f"{ODOMETRY_DEFAULTS_NAME}.dump.toml"
    dumped.unlink(missing_ok=True)
    try:
        run = subprocess.run([ODOMETRY_PROGRAM, "--dump-default-params", str(dumped)],
                             capture_output=True, timeout=120, cwd=folder, env=tool_env(folder))
    except (OSError, subprocess.TimeoutExpired) as error:
        say(f"HDMapping's odometry cannot be run, so no HDMapping step is offered: {error}")
        return False
    if run.returncode != 0 or not dumped.is_file():
        say(f"{ODOMETRY_PROGRAM} --dump-default-params exited {run.returncode}; no HDMapping step is offered")
        return False
    try:
        defaults = read_defaults(dumped)
    except ToolFailed as error:
        say(f"{error}; no HDMapping step is offered")
        return False
    os.replace(dumped, folder / f"{ODOMETRY_DEFAULTS_NAME}.toml")
    write_json(folder / f"{ODOMETRY_DEFAULTS_NAME}.json", {
        "step": "hdmapping.odometry",
        "tool": "hdmapping",
        "version": hdmapping_version(defaults["softwareVersion"]),
        "softwareVersion": defaults["softwareVersion"],
        "commit": HDMAPPING_COMMIT,
        "writtenAt": now(),
        "values": finite(defaults["values"]),
        "toml": defaults["toml"],
    })
    PUBLISHED_DEFAULTS = defaults
    return True


def tool_env(directory: Path) -> dict:
    """The environment HDMapping runs in: a home and a temporary directory inside the job's own
    folder, so whatever it writes there lands on the volume and not in the small /tmp."""
    scratch = directory / "home"
    scratch.mkdir(exist_ok=True)
    env = dict(os.environ)
    env["HOME"] = str(scratch)
    env["TMPDIR"] = str(scratch)
    return env


ODOMETRY_POSES = re.compile(r"finished optimizing .* - (\d+) / (\d+) at iteration")
ODOMETRY_COUNTED = re.compile(r"(?:running iterations:|processing worker_data) (\d+)/(\d+)")


def odometry_progress(line: str) -> tuple[float, str] | None:
    """The odometry stage's own progress (0 to 1) from the last line step 1 printed, or None for a
    line that says nothing about it. The poses it optimises one by one are nearly all of its time."""
    match = ODOMETRY_POSES.search(line) or ODOMETRY_COUNTED.search(line)
    if match:
        done, total = int(match.group(1)), int(match.group(2))
        if total > 0:
            return 0.05 + 0.85 * min(done, total) / total, f"odometry: {done:,} of {total:,} poses"
    if line.startswith("Loading point clouds") or line.startswith("Loading data"):
        return 0.02, "loading the recording"
    if "Odometry finished" in line:
        return 0.92, "writing the session"
    if line.startswith("Status: completed"):
        return 0.98, "odometry finished"
    return None


def export_progress(line: str) -> tuple[float, str] | None:
    if "contains" in line and "points" in line:
        return 0.3, "reading the session's scans"
    if line.startswith("updated:"):
        return 0.6, "writing the cloud"
    if line.startswith("successfully written"):
        return 0.9, "writing the trajectory"
    return None


def odometry_summary(log_text: str) -> dict:
    """What step 1 reports about its run: elapsed time, the trajectory's length, the cache's peak,
    the memory it used, and whether it said it completed. Figures only, never a position."""
    summary: dict = {"completed": "Status: completed" in log_text}
    for key, pattern in (
        ("elapsedSeconds", r"finished computation, elapsed time: ([0-9.]+)s"),
        ("trajectoryMetres", r"total_length_of_calculated_trajectory: ([0-9.]+)"),
        ("cachePeakMiB", r"Cache: removed; peak ([0-9.]+) MiB"),
        ("residentPeakMiB", r"Application resident RAM peak sampled: ([0-9.]+) MiB"),
    ):
        found = re.findall(pattern, log_text)
        if found:
            summary[key] = float(found[-1])
    return summary


def read_log_end(job: "Job", size: int = 256 * 1024) -> str:
    try:
        with job.log_path.open("rb") as handle:
            handle.seek(0, os.SEEK_END)
            handle.seek(max(0, handle.tell() - size))
            return handle.read().decode("utf-8", "replace")
    except OSError:
        return ""


def session_facts(session_file: Path) -> dict:
    """The session's scan count and the odometry version that wrote it. Its offsets, which can be a
    real position, are not read."""
    try:
        session = json.loads(session_file.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise CheckFailed(f"the session file does not read: {error}") from error
    names = session.get("laz_file_names") if isinstance(session, dict) else None
    settings = session.get("Session Settings") if isinstance(session, dict) else None
    return {
        "scans": len(names) if isinstance(names, list) else 0,
        "softwareVersion": str((settings or {}).get("lidar_odometry_version", "")),
    }


def step_odometry(job: "Job") -> None:
    job.tool = "hdmapping"
    recording = read_recording(job)
    stages = Stages(job)
    links = job.directory / "in"
    session = job.directory / "lio"
    params = job.directory / "params.toml"

    def preflight() -> None:
        job.progress(0.01, "checking the recording")
        job.data["preflight"] = preflight_checks(job, recording)

    def parameters() -> None:
        dumped = job.directory / "defaults.toml"
        dumped.unlink(missing_ok=True)
        job.run_tool([ODOMETRY_PROGRAM, "--dump-default-params", str(dumped)], env=tool_env(job.directory))
        if not dumped.is_file():
            raise ToolFailed(f"{ODOMETRY_PROGRAM} wrote no default parameters")
        defaults = read_defaults(dumped)
        values, changed = resolve_parameters(defaults["values"], job.parameter("values", None))
        write_whole(params, toml_text(values))
        job.data["parameters"] = {
            "softwareVersion": defaults["softwareVersion"],
            "sha256": sha256_of(params),
            "changed": changed,
        }

    def link() -> None:
        # Step 1 writes a cache folder (and a preview folder) inside the folder it reads from, so
        # it is given a folder of its own holding links to the recording's files, which stay
        # untouched beside them. The links are relative, so the job directory can be read from
        # any mount point.
        if links.exists():
            shutil.rmtree(links)
        links.mkdir()
        for path in recording["paths"]:
            os.symlink(os.path.join("..", path.name), links / path.name)

    def odometry() -> None:
        if session.exists():
            shutil.rmtree(session)

        def progress(line: str) -> None:
            found = odometry_progress(line)
            if found:
                job.progress(0.02 + 0.86 * found[0], found[1])

        job.progress(0.02, "starting odometry")
        job.run_tool([ODOMETRY_PROGRAM, str(links), str(params), str(session)], progress,
                     env=tool_env(job.directory))
        if not (session / "session.mjs").is_file():
            raise ToolFailed(f"{ODOMETRY_PROGRAM} exited 0 but wrote no session")
        job.data["odometry"] = odometry_summary(read_log_end(job)) | session_facts(session / "session.mjs")

    def pack() -> None:
        job.progress(0.9, "packing the session")
        archive = job.directory / "session.zip"
        pack_folder(job, session, archive, skip=("cache", "preview"))
        shutil.rmtree(session)
        if links.exists():
            shutil.rmtree(links)

    stages.run("preflight", preflight)
    stages.run("parameters", parameters)
    stages.run("links", link)
    stages.run("odometry", odometry)
    stages.run("session", pack)
    job.tool_version = hdmapping_version(job.data.get("parameters", {}).get("softwareVersion"))
    job.produced("hdmSession", job.directory / "session.zip")


def pack_folder(job: "Job", folder: Path, archive: Path, skip: tuple[str, ...] = ()) -> None:
    """Packs a folder into a zip, its top-level folders named in skip left out. Point files are
    stored as they are (they are compressed already); everything else is deflated."""
    part = archive.with_name(archive.name + ".part")
    with zipfile.ZipFile(part, "w") as pack:
        for path in sorted(folder.rglob("*")):
            relative = path.relative_to(folder)
            if relative.parts[0] in skip or not path.is_file() or path.is_symlink():
                continue
            job.check()
            stored = path.suffix.lower() in (".laz", ".zip")
            pack.write(path, relative.as_posix(),
                       compress_type=zipfile.ZIP_STORED if stored else zipfile.ZIP_DEFLATED)
    os.replace(part, archive)


def unpack_session(job: "Job", archive: Path, folder: Path) -> None:
    """Unpacks a session archive, refusing any member that would land outside the folder."""
    if folder.exists():
        shutil.rmtree(folder)
    folder.mkdir()
    try:
        with zipfile.ZipFile(archive) as pack:
            for member in pack.infolist():
                job.check()
                name = member.filename
                parts = name.replace("\\", "/").split("/")
                if name.startswith(("/", "\\")) or ".." in parts or ":" in name or "\0" in name:
                    raise CheckFailed("the session archive names a file outside itself")
                if member.is_dir():
                    continue
                target = folder.joinpath(*[p for p in parts if p])
                target.parent.mkdir(parents=True, exist_ok=True)
                with pack.open(member) as source, target.open("wb") as out:
                    shutil.copyfileobj(source, out, 1 << 20)
    except zipfile.BadZipFile as error:
        raise CheckFailed(f"the session archive does not read: {error}") from error


def step_export(job: "Job") -> None:
    job.tool = "hdmapping"
    archive = job.input("hdmSession")
    stages = Stages(job)
    folder = job.directory / "session"
    out = job.directory / "export"
    cloud = job.directory / "export.laz"
    trajectory = job.directory / "trajectory.csv"

    def unpack() -> None:
        job.progress(0.02, "unpacking the session")
        unpack_session(job, archive, folder)
        if not (folder / "session.mjs").is_file():
            raise CheckFailed("the session archive holds no session.mjs")
        job.data["session"] = session_facts(folder / "session.mjs")

    def export() -> None:
        if out.exists():
            shutil.rmtree(out)

        def progress(line: str) -> None:
            found = export_progress(line)
            if found:
                job.progress(0.05 + 0.9 * found[0], found[1])

        job.run_tool([EXPORT_PROGRAM, str(folder / "session.mjs"), "--export", str(out)], progress,
                     env=tool_env(job.directory))
        laz, csv = out / "all_step_2.laz", out / "trajectories.csv"
        if not laz.is_file() or not csv.is_file():
            raise ToolFailed(f"{EXPORT_PROGRAM} exited 0 but did not write both the cloud and the trajectory")
        os.replace(laz, cloud)
        os.replace(csv, trajectory)
        log = read_log_end(job, 64 * 1024)
        written = re.findall(r"successfully written (\d+) points", log)
        job.data["export"] = {
            "pointsWritten": int(written[-1]) if written else None,
            # HDMapping leaves a pose's Unix time at zero when the recording did not carry one.
            "missingTimestamps": "timestamps are missing" in log,
        }
        shutil.rmtree(out)
        shutil.rmtree(folder)

    stages.run("unpack", unpack)
    stages.run("export", export)
    job.tool_version = hdmapping_version(job.data.get("session", {}).get("softwareVersion"))
    job.produced("pointCloudLaz", cloud)
    job.produced("trajectory", trajectory)


def check_trajectory(path: Path, job: "Job") -> tuple[dict, list[str]]:
    """HDMapping's exported trajectory: nine numbers a line (time, a flag, position, quaternion),
    every one finite, the times (where present) never going back."""
    rows = 0
    missing = 0
    backwards = 0
    bad = 0
    length = 0.0
    last_time = None
    last_xyz = None
    with path.open("r", encoding="utf-8", errors="replace") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            fields = re.split(r"[,\s]+", line)
            try:
                numbers = [float(f) for f in fields]
            except ValueError:
                bad += 1
                continue
            if len(numbers) != 9 or not all(math.isfinite(n) for n in numbers):
                bad += 1
                continue
            rows += 1
            if rows % 100_000 == 0:
                job.check()
            time_stamp, xyz = numbers[0], numbers[2:5]
            if time_stamp == 0:
                missing += 1
            else:
                if last_time is not None and time_stamp < last_time:
                    backwards += 1
                last_time = time_stamp
            if last_xyz is not None:
                length += math.dist(last_xyz, xyz)
            last_xyz = xyz
    problems = []
    if rows == 0:
        problems.append("the trajectory holds no poses")
    if bad:
        problems.append(f"{bad:,} trajectory lines are not nine finite numbers")
    if backwards:
        problems.append(f"the trajectory's time goes back {backwards:,} times")
    return {"rows": rows, "missingTimestamps": missing, "timeGoesBack": backwards,
            "lengthMetres": round(length, 2)}, problems


def step_check(job: "Job") -> None:
    """A cloud checked before anything is built on it: the points its header declares are there,
    every coordinate is a finite number inside the header's bounds, and the extent is not empty;
    with a trajectory, that too reads and runs forward in time. The report carries figures and the
    extent's size, never a coordinate."""
    import laspy
    import numpy as np

    job.tool, job.tool_version = "laspy", tool_versions().get("laspy", "")
    source = job.input("pointCloudLaz")
    inputs = job.request.get("inputs") or {}
    problems: list[str] = []
    with laspy.open(source) as reader:
        header = reader.header
        declared = int(header.point_count)
        scales = np.asarray(header.scales, dtype=np.float64)
        offsets = np.asarray(header.offsets, dtype=np.float64)
        if not (np.all(np.isfinite(scales)) and np.all(np.isfinite(offsets)) and np.all(scales > 0)):
            problems.append("the header's scales or offsets are not finite positive numbers")
        header_min = np.asarray(header.mins, dtype=np.float64)
        header_max = np.asarray(header.maxs, dtype=np.float64)
        mins = np.full(3, np.inf)
        maxs = np.full(3, -np.inf)
        counted = 0
        non_finite = 0
        for chunk in reader.chunk_iterator(CHUNK_POINTS):
            job.check()
            xyz = np.column_stack((np.asarray(chunk.x), np.asarray(chunk.y), np.asarray(chunk.z)))
            good = np.all(np.isfinite(xyz), axis=1)
            non_finite += int((~good).sum())
            if good.any():
                mins = np.minimum(mins, xyz[good].min(axis=0))
                maxs = np.maximum(maxs, xyz[good].max(axis=0))
            counted += len(chunk)
            job.progress(0.9 * counted / max(1, declared), f"checked {counted:,} of {declared:,} points")
    if counted == 0:
        problems.append("the cloud holds no points")
    if counted != declared:
        problems.append(f"the header declares {declared:,} points and the file holds {counted:,}")
    if non_finite:
        problems.append(f"{non_finite:,} points have a coordinate that is not a finite number")
    size = [float(maxs[i] - mins[i]) if counted else 0.0 for i in range(3)]
    bounds_hold = bool(counted == 0 or (np.all(mins >= header_min - scales) and np.all(maxs <= header_max + scales)))
    if not bounds_hold:
        problems.append("the header's bounds do not contain the cloud's points")
    if counted and sum(1 for s in size if s > 0) < 2:
        problems.append("the cloud's extent is empty on more than one axis")
    report = {
        "pointCount": counted,
        "declaredPoints": declared,
        "extentMetres": [round(s, 3) for s in size],
        "nonFinitePoints": non_finite,
        "headerBoundsHold": bounds_hold,
    }
    if "trajectory" in inputs:
        report["trajectory"], trajectory_problems = check_trajectory(job.input("trajectory"), job)
        problems += trajectory_problems
    report["problems"] = problems
    out = job.directory / "check.json"
    write_json(out, report)
    job.produced("report", out)
    job.data = report
    if problems:
        raise CheckFailed("the cloud failed its check: " + "; ".join(problems))


# -- plan and profile images ---------------------------------------------------------------------

# The most an image of the plan or the profile may hold. A long cave at the asked pixel size would
# need more; the pixel is then made larger, and the result says so, rather than a picture written
# that no browser would open.
RASTER_MAX_SIDE = 16384
RASTER_MAX_PIXELS = 32_000_000
# A colour scale from dark violet through blue and green to yellow, readable by people who do not
# tell red from green, and printable in grey.
RASTER_RAMP = ((68, 1, 84), (59, 82, 139), (33, 145, 140), (94, 201, 98), (253, 231, 37))


def png_bytes(rgba) -> bytes:
    """An 8-bit RGBA picture as a PNG, written with the standard library: rows unfiltered, zlib."""
    import struct
    import zlib
    import numpy as np

    height, width = int(rgba.shape[0]), int(rgba.shape[1])
    rows = np.zeros((height, width * 4 + 1), dtype=np.uint8)
    rows[:, 1:] = rgba.reshape(height, width * 4)

    def chunk(kind: bytes, body: bytes) -> bytes:
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(rows.tobytes(), 6)) + chunk(b"IEND", b""))


def raster_pixel(width_m: float, height_m: float, pixel_m: float) -> float:
    """The asked pixel size, or the smallest larger one that keeps the picture inside the limits."""
    width_m, height_m = max(width_m, pixel_m), max(height_m, pixel_m)
    return max(pixel_m, width_m / RASTER_MAX_SIDE, height_m / RASTER_MAX_SIDE,
               math.sqrt(width_m * height_m / RASTER_MAX_PIXELS)) * 1.000001


def coloured(values, low: float, high: float):
    """Cell values to colours on the ramp; a cell no point fell in (NaN) is left transparent."""
    import numpy as np

    filled = np.isfinite(values)
    span = high - low if high > low else 1.0
    t = np.clip((np.where(filled, values, low) - low) / span, 0.0, 1.0) * (len(RASTER_RAMP) - 1)
    lower = np.minimum(t.astype(np.int32), len(RASTER_RAMP) - 2)
    frac = (t - lower)[..., None]
    ramp = np.asarray(RASTER_RAMP, dtype=np.float32)
    rgb = ramp[lower] * (1.0 - frac) + ramp[lower + 1] * frac
    rgba = np.zeros(values.shape + (4,), dtype=np.uint8)
    rgba[..., :3] = np.round(rgb).astype(np.uint8)
    rgba[..., 3] = np.where(filled, 255, 0)
    return rgba


def read_centreline(path: Path):
    """The vertices of the centreline the application drew, as an (n, 3) array, or None when the
    file is not a LineString of at least two finite vertices."""
    import numpy as np

    try:
        value = json.loads(path.read_text(encoding="utf-8"))
        coordinates = value["geometry"]["coordinates"]
        line = np.asarray([[float(c) for c in vertex[:3]] for vertex in coordinates], dtype=np.float64)
    except (OSError, ValueError, KeyError, TypeError, IndexError):
        return None
    if line.ndim != 2 or line.shape[0] < 2 or line.shape[1] != 3 or not np.all(np.isfinite(line)):
        return None
    return line


def centreline_samples(line, spacing: float):
    """The line walked at a fixed spacing: each sample's position, the distance walked to it, and
    the direction of travel there."""
    import numpy as np

    steps = np.diff(line[:, :2], axis=0)
    lengths = np.hypot(steps[:, 0], steps[:, 1])
    keep = lengths > 1e-9
    starts, steps, lengths = line[:-1][keep], steps[keep], lengths[keep]
    if len(lengths) == 0:
        return None
    walked = np.concatenate(([0.0], np.cumsum(lengths)))
    total = float(walked[-1])
    at = np.arange(0.0, total + spacing / 2, spacing)
    segment = np.clip(np.searchsorted(walked, at, side="right") - 1, 0, len(lengths) - 1)
    along = (at - walked[segment])[:, None]
    direction = steps[segment] / lengths[segment][:, None]
    xy = starts[segment, :2] + direction * along
    return xy, at, direction, total


def nearest_samples(cells_xy, samples_xy, job: Job):
    """For each cell centre, the index of the nearest sample, by brute force in blocks."""
    import numpy as np

    nearest = np.empty(len(cells_xy), dtype=np.int32)
    block = max(1, 4_000_000 // max(1, len(samples_xy)))
    for start in range(0, len(cells_xy), block):
        job.check()
        part = cells_xy[start:start + block]
        dx = part[:, None, 0] - samples_xy[None, :, 0]
        dy = part[:, None, 1] - samples_xy[None, :, 1]
        nearest[start:start + block] = np.argmin(dx * dx + dy * dy, axis=1)
    return nearest


def step_raster(job: Job) -> None:
    """A top view of the cloud and an extended profile along the centreline, as PNG pictures at a
    set pixel size, coloured by height or by the points' intensity. The top view keeps, per cell,
    the lowest point (the floor); the profile unrolls the passage along the centreline, so the
    horizontal axis is the distance walked along it and the vertical one the height. Heights are
    given as metres above the cloud's lowest point, and nothing in the pictures or in the result is
    a coordinate. Without a centreline only the top view is drawn."""
    import laspy
    import numpy as np

    job.tool, job.tool_version = "numpy", tool_versions().get("numpy", "")
    source = job.input("pointCloudLaz")
    inputs = job.request.get("inputs") or {}
    asked_pixel_m = float(job.parameter("pixelCm", 5)) / 100.0
    colour_by = str(job.parameter("colourBy", "height"))
    if not (0.005 <= asked_pixel_m <= 1.0):
        raise Refused("pixelCm must be between 0.5 and 100")
    if colour_by not in ("height", "intensity"):
        raise Refused("colourBy must be height or intensity")
    line = None
    if "centreline" in inputs:
        line = read_centreline(job.input("centreline"))
        if line is None:
            job.log("the centreline does not read as a line of two vertices or more; no profile is drawn")

    with laspy.open(source) as reader:
        header = reader.header
        declared = max(1, int(header.point_count))
        mins = np.asarray(header.mins, dtype=np.float64)
        maxs = np.asarray(header.maxs, dtype=np.float64)
        has_intensity = "intensity" in set(header.point_format.dimension_names)
    if colour_by == "intensity" and not has_intensity:
        job.log("the cloud has no intensity; the pictures are coloured by height")
        colour_by = "height"
    width_m, height_m = float(maxs[0] - mins[0]), float(maxs[1] - mins[1])
    pixel_m = raster_pixel(width_m, height_m, asked_pixel_m)
    plan_w = max(1, int(math.ceil(max(width_m, pixel_m) / pixel_m)))
    plan_h = max(1, int(math.ceil(max(height_m, pixel_m) / pixel_m)))

    # Cells of a coarse grid that hold points, for finding which part of the centreline a point
    # stands beside: a point takes the place along the line of the sample nearest its cell.
    coarse_m = 1.0
    plan_value = np.full(plan_w * plan_h, np.inf if colour_by == "height" else 0.0, dtype=np.float64)
    plan_count = np.zeros(plan_w * plan_h, dtype=np.int64)
    coarse_w = max(1, int(math.ceil(max(width_m, coarse_m) / coarse_m)))
    coarse_h = max(1, int(math.ceil(max(height_m, coarse_m) / coarse_m)))
    occupied = np.zeros(coarse_w * coarse_h, dtype=bool) if line is not None else None
    passes = 2 if line is not None else 1
    counted = 0
    with laspy.open(source) as reader:
        for chunk in reader.chunk_iterator(CHUNK_POINTS):
            job.check()
            x, y, z = np.asarray(chunk.x), np.asarray(chunk.y), np.asarray(chunk.z)
            good = np.isfinite(x) & np.isfinite(y) & np.isfinite(z)
            x, y, z = x[good], y[good], z[good]
            col = np.clip(((x - mins[0]) / pixel_m).astype(np.int64), 0, plan_w - 1)
            row = np.clip(((maxs[1] - y) / pixel_m).astype(np.int64), 0, plan_h - 1)
            cell = row * plan_w + col
            if colour_by == "height":
                np.minimum.at(plan_value, cell, z - mins[2])
            else:
                np.add.at(plan_value, cell, np.asarray(chunk.intensity)[good].astype(np.float64))
            np.add.at(plan_count, cell, 1)
            if occupied is not None:
                cc = np.clip(((x - mins[0]) / coarse_m).astype(np.int64), 0, coarse_w - 1)
                cr = np.clip(((y - mins[1]) / coarse_m).astype(np.int64), 0, coarse_h - 1)
                occupied[cr * coarse_w + cc] = True
            counted += len(chunk)
            job.progress(0.9 * counted / declared / passes, f"top view: {counted:,} of {declared:,} points")

    filled = plan_count > 0
    if colour_by == "height":
        plan = np.where(filled, plan_value, np.nan)
    else:
        plan = np.where(filled, plan_value / np.maximum(plan_count, 1), np.nan)
    plan = plan.reshape(plan_h, plan_w)
    if not filled.any():
        raise CheckFailed("the cloud holds no point with finite coordinates")
    low, high = (float(v) for v in np.percentile(plan[np.isfinite(plan)], (2, 98)))
    if colour_by == "height":
        # One height scale for both pictures: the whole cloud's, floor to ceiling.
        low, high = 0.0, max(float(maxs[2] - mins[2]), pixel_m)
    plan_file = job.directory / "plan.png"
    plan_file.write_bytes(png_bytes(coloured(plan, low, high)))
    job.produced("planImage", plan_file)
    data = {
        "pixelM": round(pixel_m, 4),
        "askedPixelM": round(asked_pixel_m, 4),
        "colourBy": colour_by,
        "valueLow": round(low, 3),
        "valueHigh": round(high, 3),
        "plan": {"width": plan_w, "height": plan_h},
    }

    samples = centreline_samples(line, max(0.25, pixel_m)) if line is not None else None
    if samples is not None:
        samples_xy, walked, direction, total = samples
        if len(samples_xy) > 20_000:
            samples_xy, walked, direction, total = centreline_samples(line, total / 20_000)
        spacing = float(walked[1] - walked[0]) if len(walked) > 1 else max(0.25, pixel_m)
        cells = np.flatnonzero(occupied)
        centres = np.column_stack((mins[0] + (cells % coarse_w + 0.5) * coarse_m,
                                   mins[1] + (cells // coarse_w + 0.5) * coarse_m))
        nearest_of_cell = np.full(coarse_w * coarse_h, -1, dtype=np.int32)
        nearest_of_cell[cells] = nearest_samples(centres, samples_xy, job)
        profile_len = total + spacing
        z_span = float(maxs[2] - mins[2])
        profile_pixel = raster_pixel(profile_len, z_span, pixel_m)
        prof_w = max(1, int(math.ceil(max(profile_len, profile_pixel) / profile_pixel)))
        prof_h = max(1, int(math.ceil(max(z_span, profile_pixel) / profile_pixel)))
        prof_value = np.zeros(prof_w * prof_h, dtype=np.float64)
        prof_count = np.zeros(prof_w * prof_h, dtype=np.int64)
        counted = 0
        with laspy.open(source) as reader:
            for chunk in reader.chunk_iterator(CHUNK_POINTS):
                job.check()
                x, y, z = np.asarray(chunk.x), np.asarray(chunk.y), np.asarray(chunk.z)
                good = np.isfinite(x) & np.isfinite(y) & np.isfinite(z)
                x, y, z = x[good], y[good], z[good]
                cc = np.clip(((x - mins[0]) / coarse_m).astype(np.int64), 0, coarse_w - 1)
                cr = np.clip(((y - mins[1]) / coarse_m).astype(np.int64), 0, coarse_h - 1)
                nearest = nearest_of_cell[cr * coarse_w + cc]
                # The place along the line: the nearest sample's, moved along the direction of
                # travel by as far as the point lies ahead of or behind it, within half a spacing.
                ahead = ((x - samples_xy[nearest, 0]) * direction[nearest, 0]
                         + (y - samples_xy[nearest, 1]) * direction[nearest, 1])
                s = walked[nearest] + np.clip(ahead, -spacing / 2, spacing / 2) + spacing / 2
                col = np.clip((s / profile_pixel).astype(np.int64), 0, prof_w - 1)
                row = np.clip(((maxs[2] - z) / profile_pixel).astype(np.int64), 0, prof_h - 1)
                cell = row * prof_w + col
                if colour_by == "height":
                    np.add.at(prof_value, cell, z - mins[2])
                else:
                    np.add.at(prof_value, cell, np.asarray(chunk.intensity)[good].astype(np.float64))
                np.add.at(prof_count, cell, 1)
                counted += len(chunk)
                job.progress(0.45 + 0.45 * counted / declared, f"profile: {counted:,} of {declared:,} points")
        profile = np.where(prof_count > 0, prof_value / np.maximum(prof_count, 1), np.nan).reshape(prof_h, prof_w)
        profile_file = job.directory / "profile.png"
        profile_file.write_bytes(png_bytes(coloured(profile, low, high)))
        job.produced("profileImage", profile_file)
        data["profile"] = {"width": prof_w, "height": prof_h, "pixelM": round(profile_pixel, 4),
                           "lengthM": round(total, 2)}
    job.progress(1.0, "pictures written")
    job.data = data


# -- a surface made from the points ------------------------------------------------------------

SURFACE_STEP = "mesh.surface"
# The most points the surface is built from, after the voxel: Poisson's octree and the normal
# search hold every one of them in memory several times over. Past it the voxel is too small for
# the cloud, and the step says so rather than run the container out of memory.
SURFACE_MAX_POINTS = 12_000_000
# The trajectory is walked at this spacing before each point is matched to the nearest place the
# scanner passed; finer adds nothing to which way a wall faces.
SURFACE_TRAJECTORY_SPACING_M = 0.25
SURFACE_TRAJECTORY_MAX_SAMPLES = 4_000
# Points are matched to the trajectory a cell of this size at a time: every point of a cell looks
# toward the same place.
SURFACE_MATCH_CELL_M = 1.0
# The parameters, their defaults and their bounds. The defaults are a first guess at a cave
# passage scanned at centimetre density, still to be tuned on recordings.
SURFACE_PARAMETERS = {
    "voxelCm": (2.0, 0.5, 50.0),
    "normalRadiusCm": (6.0, 1.0, 200.0),
    "normalNeighbours": (30, 5, 200),
    "poissonDepth": (9, 5, 12),
    "densityTrimPercent": (2.0, 0.0, 30.0),
    "targetTriangles": (500_000, 1_000, 10_000_000),
    "orientCellM": (3.0, 0.5, 50.0),
}


def load_open3d():
    """Open3D, imported without its notebook viewer.

    Importing the package imports its viewers, and one of them (the plotly one) imports dash and
    plotly at once: a quarter of a gigabyte of web-server packages that the image deliberately does
    not install, because nothing here shows anything. The module that would import them is put in
    place first as an empty stand-in whose functions refuse, so the package loads with the
    geometry it is wanted for."""
    import sys
    import types

    name = "open3d.visualization.draw_plotly"
    if name not in sys.modules:
        stand_in = types.ModuleType(name)

        def absent(*_args, **_kwargs):
            raise RuntimeError("the plotly viewer is not installed in this worker")

        stand_in.get_plotly_fig = stand_in.draw_plotly = stand_in.draw_plotly_server = absent
        sys.modules[name] = stand_in
    import open3d
    return open3d


def surface_parameters(job: Job) -> dict:
    """The step's parameters, each a number inside its bounds, whole where it must be whole;
    anything else is refused naming the parameter."""
    values = {}
    for name, (default, low, high) in SURFACE_PARAMETERS.items():
        value = job.parameter(name, default)
        whole = isinstance(default, int)
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
            raise Refused(f"{name} must be a number")
        if whole and value != int(value):
            raise Refused(f"{name} must be a whole number")
        if not (low <= value <= high):
            raise Refused(f"{name} must be between {low:g} and {high:g}")
        values[name] = int(value) if whole else float(value)
    shift = job.parameter("originShift", None)
    if shift is not None:
        if (not isinstance(shift, list) or len(shift) != 3
                or not all(isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) for v in shift)):
            raise Refused("originShift must be three finite numbers")
        values["originShift"] = [float(v) for v in shift]
    return values


def voxel_centroids(job: Job, source: Path, shift, voxel_m: float, fraction_end: float):
    """The cloud reduced to the mean point of each occupied voxel, in metres about the shift, read a
    chunk at a time. Cells straddling two chunks are merged, so the result does not depend on how
    the file is chunked. Returns the points (n, 3) and the number read."""
    import laspy
    import numpy as np

    with laspy.open(source) as reader:
        summary = read_cloud_summary(reader)
    ensure_extent(summary, source, job)
    total = max(1, summary["pointCount"])
    mins = np.asarray(summary["min"], dtype=np.float64) - np.asarray(shift)
    maxs = np.asarray(summary["max"], dtype=np.float64) - np.asarray(shift)
    counts = voxel_grid(mins, maxs, voxel_m)
    if counts is None:
        raise Refused(f"the cloud's extent is too large for a {voxel_m * 100:g} cm voxel")
    keys = np.empty(0, dtype=np.int64)
    sums = np.empty((0, 3), dtype=np.float64)
    weights = np.empty(0, dtype=np.int64)
    read = 0
    with laspy.open(source) as reader:
        for chunk in reader.chunk_iterator(CHUNK_POINTS):
            job.check()
            xyz = np.column_stack((np.asarray(chunk.x) - shift[0], np.asarray(chunk.y) - shift[1],
                                   np.asarray(chunk.z) - shift[2]))
            xyz = xyz[np.all(np.isfinite(xyz), axis=1)]
            read += len(chunk)
            if len(xyz) == 0:
                continue
            all_keys = np.concatenate((keys, voxel_keys(np.clip(xyz, mins, maxs), mins, counts, voxel_m)))
            all_sums = np.concatenate((sums, xyz))
            all_weights = np.concatenate((weights, np.ones(len(xyz), dtype=np.int64)))
            keys, inverse = np.unique(all_keys, return_inverse=True)
            sums = np.column_stack([np.bincount(inverse, weights=all_sums[:, i], minlength=len(keys)) for i in range(3)])
            weights = np.bincount(inverse, weights=all_weights, minlength=len(keys)).astype(np.int64)
            if len(keys) > SURFACE_MAX_POINTS:
                raise Refused(f"more than {SURFACE_MAX_POINTS:,} voxels hold points at {voxel_m * 100:g} cm; "
                              "choose a larger voxel")
            job.progress(fraction_end * read / total, f"voxels: {len(keys):,} from {read:,} of {total:,} points")
    return sums / weights[:, None], read


def read_trajectory_positions(path: Path, shift):
    """The scanner's positions from HDMapping's trajectory (nine numbers a line, the position third
    to fifth), in metres about the shift, or None when fewer than two lines read."""
    import numpy as np

    positions = []
    with path.open("r", encoding="utf-8", errors="replace") as handle:
        for line in handle:
            fields = re.split(r"[,\s]+", line.strip())
            if len(fields) != 9:
                continue
            try:
                xyz = [float(f) for f in fields[2:5]]
            except ValueError:
                continue
            if all(math.isfinite(v) for v in xyz):
                positions.append(xyz)
    if len(positions) < 2:
        return None
    line = np.asarray(positions, dtype=np.float64) - np.asarray(shift)
    # Walked at a fixed spacing: a scanner standing still writes thousands of poses in one place.
    kept = [line[0]]
    for point in line[1:]:
        if np.linalg.norm(point - kept[-1]) >= SURFACE_TRAJECTORY_SPACING_M:
            kept.append(point)
    samples = np.asarray(kept)
    if len(samples) > SURFACE_TRAJECTORY_MAX_SAMPLES:
        samples = samples[np.linspace(0, len(samples) - 1, SURFACE_TRAJECTORY_MAX_SAMPLES).astype(np.int64)]
    return samples


def nearest_of(points, samples, job: Job):
    """For each point, the index of the nearest sample, by brute force in blocks."""
    import numpy as np

    nearest = np.empty(len(points), dtype=np.int64)
    block = max(1, 8_000_000 // max(1, len(samples)))
    for start in range(0, len(points), block):
        job.check()
        part = points[start:start + block]
        distance = ((part[:, None, :] - samples[None, :, :]) ** 2).sum(axis=2)
        nearest[start:start + block] = np.argmin(distance, axis=1)
    return nearest


def facing_targets(points, trajectory, cell_m: float, job: Job):
    """Where each point's normal should face. A cave has no outside to point normals away from, so
    they are turned toward where the scanner was: the nearest place along the trajectory, matched a
    cell at a time. Without a trajectory, each point faces the mean of the points in its coarse
    cell, which inside a passage lies in the open space between its walls."""
    import numpy as np

    cell = SURFACE_MATCH_CELL_M if trajectory is not None else cell_m
    cells = np.floor((points - points.min(axis=0)) / cell).astype(np.int64)
    keys, inverse = np.unique(cells, axis=0, return_inverse=True)
    inverse = inverse.reshape(-1)
    if trajectory is not None:
        centres = (keys + 0.5) * cell + points.min(axis=0)
        return trajectory[nearest_of(centres, trajectory, job)][inverse]
    means = np.column_stack([np.bincount(inverse, weights=points[:, i], minlength=len(keys)) for i in range(3)])
    means /= np.bincount(inverse, minlength=len(keys))[:, None]
    return means[inverse]


def write_binary_stl(path: Path, vertices, triangles) -> None:
    """A binary STL: an 80-byte header, the facet count, then per facet its normal, its three
    corners as 32-bit floats, and two bytes nobody reads."""
    import numpy as np

    corners = vertices[triangles].astype(np.float64)
    normals = np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0])
    length = np.linalg.norm(normals, axis=1)
    normals = np.where(length[:, None] > 0, normals / np.maximum(length, 1e-30)[:, None], 0.0)
    record = np.dtype([("normal", "<f4", (3,)), ("corners", "<f4", (3, 3)), ("attribute", "<u2")])
    facets = np.zeros(len(triangles), dtype=record)
    facets["normal"] = normals
    facets["corners"] = corners
    with path.open("wb") as handle:
        handle.write(b"SilexGIS surface from a point cloud".ljust(80, b" "))
        handle.write(struct.pack("<I", len(triangles)))
        handle.write(facets.tobytes())


def step_surface(job: Job) -> None:
    """A closed-over surface of the cloud as a binary STL, for showing as a cave's walls.

    The cloud is reduced to one mean point per voxel, normals are estimated over a radius and
    turned toward where the scanner was, and screened Poisson reconstruction (Open3D's) makes a
    watertight surface of them. Poisson also closes over space no scan reached, and the faces it
    invents there are carried by few points: the vertices whose point density is lowest (the trim
    percentile) are removed with them. The rest is decimated to the target triangle count and
    written out.

    The surface is written in the scan's tile frame: metres about the shift the tiles were made
    with (the request's originShift, or the cloud's own centre exactly as the tiling step takes it),
    so the mesh lines up with the tiles under the scan's placement and carries no coordinate."""
    params = surface_parameters(job)
    source = job.input("pointCloudLaz")
    inputs = job.request.get("inputs") or {}
    trajectory_file = job.input("trajectory") if "trajectory" in inputs else None

    import laspy
    import numpy as np

    o3d = load_open3d()
    job.tool, job.tool_version = "open3d", tool_versions().get("open3d", "")
    o3d.utility.set_verbosity_level(o3d.utility.VerbosityLevel.Error)
    if "originShift" in params:
        shift = params["originShift"]
    else:
        with laspy.open(source) as reader:
            summary = read_cloud_summary(reader)
        ensure_extent(summary, source, job)
        shift = [float(np.floor((summary["min"][i] + summary["max"][i]) / 2.0)) for i in range(3)]

    voxel_m = params["voxelCm"] / 100.0
    points, read = voxel_centroids(job, source, shift, voxel_m, 0.25)
    if len(points) < 100:
        raise CheckFailed(f"only {len(points):,} voxels hold points; a surface needs far more")

    trajectory = None
    if trajectory_file is not None:
        trajectory = read_trajectory_positions(trajectory_file, shift)
        if trajectory is None:
            job.log("the trajectory holds fewer than two positions; normals face the passage instead")
    job.check()
    job.progress(0.28, f"normals of {len(points):,} points")
    cloud = o3d.geometry.PointCloud(o3d.utility.Vector3dVector(points))
    cloud.estimate_normals(o3d.geometry.KDTreeSearchParamHybrid(
        radius=params["normalRadiusCm"] / 100.0, max_nn=params["normalNeighbours"]))
    normals = np.asarray(cloud.normals)
    targets = facing_targets(points, trajectory, params["orientCellM"], job)
    away = np.einsum("ij,ij->i", normals, targets - points) < 0
    normals[away] *= -1.0
    cloud.normals = o3d.utility.Vector3dVector(normals)

    job.check()
    job.progress(0.35, f"Poisson reconstruction at depth {params['poissonDepth']}")
    mesh, densities = o3d.geometry.TriangleMesh.create_from_point_cloud_poisson(
        cloud, depth=params["poissonDepth"], n_threads=int(TILE_JOBS))
    del cloud
    poisson_triangles = len(mesh.triangles)
    densities = np.asarray(densities)
    trimmed = 0
    if params["densityTrimPercent"] > 0 and len(densities):
        low = np.percentile(densities, params["densityTrimPercent"])
        remove = densities < low
        trimmed = int(remove.sum())
        mesh.remove_vertices_by_mask(remove.tolist())
    # Poisson's surface runs on past the scan, out to a box around it; nothing beyond the points'
    # own extent (by a few voxels) was measured.
    margin = 3 * voxel_m
    box = o3d.geometry.AxisAlignedBoundingBox(points.min(axis=0) - margin, points.max(axis=0) + margin)
    mesh = mesh.crop(box)
    mesh.remove_degenerate_triangles()
    mesh.remove_unreferenced_vertices()

    job.check()
    target = params["targetTriangles"]
    if len(mesh.triangles) > target:
        job.progress(0.8, f"decimating {len(mesh.triangles):,} triangles to {target:,}")
        mesh = mesh.simplify_quadric_decimation(target_number_of_triangles=target)
        mesh.remove_degenerate_triangles()
        mesh.remove_unreferenced_vertices()
    vertices = np.asarray(mesh.vertices)
    triangles = np.asarray(mesh.triangles)
    if len(triangles) == 0:
        raise CheckFailed("the reconstruction left no triangles; try a larger voxel or a smaller trim")

    job.check()
    job.progress(0.95, f"writing {len(triangles):,} triangles")
    out = job.directory / "surface.stl"
    write_binary_stl(out, vertices, triangles)
    job.produced("meshStl", out)
    job.data = {
        "pointsIn": int(read),
        "voxelPoints": int(len(points)),
        "orientation": "trajectory" if trajectory is not None else "cellMean",
        "normalsTurned": int(away.sum()),
        "poissonTriangles": int(poisson_triangles),
        "verticesTrimmed": trimmed,
        "triangles": int(len(triangles)),
        "vertices": int(len(vertices)),
        "stlBytes": out.stat().st_size,
        "originShift": shift,
        "localMin": [round(float(v), 3) for v in vertices.min(axis=0)],
        "localMax": [round(float(v), 3) for v in vertices.max(axis=0)],
        "parameters": {name: params[name] for name in SURFACE_PARAMETERS},
    }


RUNNERS = {
    "pointcloud.estimate": step_estimate,
    "pointcloud.downsample": step_downsample,
    "py3dtiles.tile": step_tile,
    "hdmapping.preflight": step_preflight,
    "hdmapping.odometry": step_odometry,
    "hdmapping.export": step_export,
    "pointcloud.check": step_check,
    "pointcloud.raster": step_raster,
    "mesh.surface": step_surface,
}


# -- the loop ----------------------------------------------------------------------------------

def finish(job: Job, status: str, reason: str = "") -> None:
    result = {
        "status": status,
        "step": job.request.get("step"),
        "reason": reason,
        "outputs": job.outputs,
        "tool": job.tool,
        "toolVersion": job.tool_version,
        "worker": WORKER_VERSION,
        "startedAt": job.started_at,
        "finishedAt": now(),
        "seconds": round(time.monotonic() - job.clock, 1),
        "peakRssBytes": resource.getrusage(resource.RUSAGE_CHILDREN).ru_maxrss * 1024
        + resource.getrusage(resource.RUSAGE_SELF).ru_maxrss * 1024,
        "logTail": job.log_tail(),
        "data": job.data,
    }
    write_json(job.directory / "result.json", result)
    say(f"{job.directory.name}: {status}" + (f" ({reason})" if reason else ""))


def claim(directory: Path, stamp: str) -> bool:
    """Marks the job taken, unless another worker took it first: the marker is created only if it
    does not exist yet, which the filesystem decides once for everybody."""
    try:
        handle = os.open(directory / "started", os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
    except FileExistsError:
        return False
    with os.fdopen(handle, "w", encoding="utf-8") as out:
        out.write(stamp + "\n")
    return True


def run_job(directory: Path, resume: bool = False) -> None:
    job = Job(directory)
    if not resume and not claim(directory, job.started_at):
        return
    try:
        try:
            job.request = json.loads((directory / "request.json").read_text(encoding="utf-8"))
        except (OSError, ValueError) as error:
            raise Refused(f"request.json cannot be read: {error}") from error
        if not isinstance(job.request, dict):
            raise Refused("request.json is not an object")
        step = job.request.get("step")
        if step not in RUNNERS or step not in ROLE_STEPS.get(ROLE, ()):
            raise Refused(f"step '{step}' is not one this worker runs")
        timeout = job.request.get("timeoutSeconds")
        if timeout is not None:
            if not isinstance(timeout, (int, float)) or timeout <= 0:
                raise Refused("timeoutSeconds must be a positive number")
            job.timeout = float(timeout)
        # Every input is judged before any tool is loaded or any file opened: a name with a path in
        # it is refused on its spelling alone.
        inputs = job.request.get("inputs", {})
        if not isinstance(inputs, dict):
            raise Refused("inputs must be an object of kind to file name")
        for kind, name in inputs.items():
            if not plain_file_name(name):
                raise Refused(f"input {kind} is not a plain file name")
        job.check()
        job.progress(0.0, "resuming" if resume else "starting")
        job.log(f"step {step} in {directory.name}" + (", resumed after its last finished stage" if resume else ""))
        RUNNERS[step](job)
        job.progress(1.0, "finished")
        finish(job, "ok")
    except Refused as error:
        finish(job, "refused", str(error))
    except Cancelled:
        finish(job, "cancelled", "the job was called off and the tool was stopped")
    except TimedOut:
        finish(job, "failed", f"the job reached its time limit of {int(job.timeout)} s and was stopped")
    except ToolFailed as error:
        finish(job, "failed", str(error))
    except ImportError as error:
        finish(job, "failed", f"this worker lacks a tool the step needs: {error}")
    except MemoryError:
        finish(job, "failed", "the worker ran out of memory")
    except Exception as error:  # noqa: BLE001 - a bug here must still answer the request
        job.log(f"unexpected: {error!r}")
        finish(job, "failed", f"unexpected error: {error!r}"[:500])


def pending_jobs(jobs: Path) -> list[Path]:
    found = []
    if not jobs.is_dir():
        return found
    for directory in sorted(jobs.iterdir()):
        if directory.is_dir() and (directory / "request.json").is_file() and not (directory / "result.json").exists():
            found.append(directory)
    return found


def requested_step(directory: Path) -> object:
    try:
        request = json.loads((directory / "request.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    return request.get("step") if isinstance(request, dict) else None


def sweep(jobs: Path) -> int:
    """Answers every waiting job once. Returns how many it took.

    A job whose step another worker announces is that worker's, waiting or abandoned, and is left
    alone; everything else (this worker's steps, and steps nobody runs, which are refused) is
    answered here."""
    taken = 0
    theirs = steps_of_other_workers() - set(ROLE_STEPS.get(ROLE, ()))
    for directory in pending_jobs(jobs):
        step = requested_step(directory)
        if step in theirs:
            continue
        if (directory / "cancel").exists():
            job = Job(directory)
            finish(job, "cancelled", "the job was called off before this worker ran it")
            continue
        if (directory / "started").exists():
            stages = read_stages(directory) if step in STAGED_STEPS else None
            if stages and stages.get("done") and stages.get("running") is None:
                run_job(directory, resume=True)
                taken += 1
                continue
            job = Job(directory)
            if stages and stages.get("running"):
                job.data = {"stagesDone": stages.get("done"), "interruptedIn": stages.get("running")}
                finish(job, "interrupted", f"the worker stopped during the {stages.get('running')} stage; "
                       "its outputs are unfinished")
            else:
                finish(job, "interrupted", "the worker stopped while this job was running; its outputs are unfinished")
            continue
        run_job(directory)
        taken += 1
    return taken


def serve(once: bool) -> int:
    if not POLL_SECONDS.isdigit():
        say(f"SILEXGIS_SCANS_POLL_SECONDS must be a whole number of seconds, not '{POLL_SECONDS}'")
        return 1
    for name, value in (("SILEXGIS_SCANS_JOBS", TILE_JOBS), ("SILEXGIS_SCANS_CACHE_MB", TILE_CACHE_MB),
                        ("SILEXGIS_SCANS_JOB_TIMEOUT_SECONDS", DEFAULT_TIMEOUT_SECONDS)):
        if not value.isdigit() or int(value) < 1:
            say(f"{name} must be a whole number above zero, not '{value}'")
            return 1
    if ROLE not in ROLE_STEPS:
        say(f"SILEXGIS_SCANS_WORKER_ROLE must be one of {', '.join(ROLE_STEPS)}, not '{ROLE}'")
        return 1
    jobs = ROOT / "jobs"
    try:
        jobs.mkdir(parents=True, exist_ok=True)
        if ROLE == "hdmapping":
            publish_defaults()
        announced = announce()
    except OSError as error:
        say(f"cannot write {ROOT} — the scans volume must be mounted there and owned by uid {os.getuid()}: {error}")
        return 1
    running = True

    def stop(_signal, _frame):
        nonlocal running
        running = False

    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    say(f"scan worker {WORKER_VERSION} ({ROLE}) watching {jobs} every {POLL_SECONDS}s; steps: "
        + (", ".join(s["id"] for s in announced["steps"]) or "none"))
    while running:
        sweep(jobs)
        if once:
            break
        time.sleep(int(POLL_SECONDS))
    say("scan worker stopping")
    return 0


# -- the self-test -----------------------------------------------------------------------------

def self_test(root: Path) -> int:
    """A tiny generated cloud through estimate, downsample, tile, the pictures and the surface,
    printing a verdict only."""
    import laspy
    import numpy as np

    global ROOT
    ROOT = root
    jobs = root / "jobs"
    jobs.mkdir(parents=True, exist_ok=True)
    write_json(root / "capabilities.json", capabilities())

    # 20 000 points on a noisy tube 8 m long, placed at UTM-sized coordinates on purpose, so that
    # the tiles are proven to carry none of them.
    rng = np.random.default_rng(7)
    n = 20_000
    t = rng.uniform(0, 8, n)
    angle = rng.uniform(0, 2 * np.pi, n)
    radius = 1.5 + rng.normal(0, 0.02, n)
    x = 500_000.0 + t
    y = 4_700_000.0 + radius * np.cos(angle)
    z = 300.0 + radius * np.sin(angle)
    header = laspy.LasHeader(point_format=3, version="1.2")
    header.scales = np.array([0.001, 0.001, 0.001])
    header.offsets = np.array([500_000.0, 4_700_000.0, 300.0])
    las = laspy.LasData(header)
    las.x, las.y, las.z = x, y, z
    las.intensity = rng.integers(0, 65535, n, dtype=np.uint16)
    las.red = las.green = las.blue = rng.integers(0, 65535, n, dtype=np.uint16)

    def job(name: str, step: str, parameters: dict, inputs: dict, files: dict) -> dict:
        directory = jobs / name
        directory.mkdir()
        for file_name, source in files.items():
            if isinstance(source, Path):
                shutil.copyfile(source, directory / file_name)
            else:
                source.write(directory / file_name)
        write_json(directory / "request.json",
                   {"step": step, "parameters": parameters, "inputs": inputs, "timeoutSeconds": 600})
        sweep(jobs)
        return json.loads((directory / "result.json").read_text(encoding="utf-8"))

    failures = []

    def expect(condition: bool, what: str) -> None:
        print(("ok   " if condition else "FAIL ") + what)
        if not condition:
            failures.append(what)

    estimate = job("estimate", "pointcloud.estimate", {}, {"pointCloudLaz": "cloud.laz"}, {"cloud.laz": las})
    expect(estimate["status"] == "ok", f"estimate ran: {estimate['status']} {estimate['reason']}")
    rows = {r["voxelCm"]: r["points"] for r in estimate.get("data", {}).get("voxels", [])}
    expect(rows.get(1, 0) > rows.get(10, 0) > 0, f"coarser voxels keep fewer points: {rows}")
    expect(estimate["data"]["pointCount"] == n, "estimate counts every point")

    down = job("downsample", "pointcloud.downsample", {"method": "voxel", "voxelCm": 10},
               {"pointCloudLaz": "cloud.laz"}, {"cloud.laz": jobs / "estimate" / "cloud.laz"})
    expect(down["status"] == "ok", f"downsample ran: {down['status']} {down['reason']}")
    expect(down["data"].get("pointsOut") == rows.get(10), "downsample keeps exactly the cells the estimate counted")
    display = jobs / "downsample" / "display.laz"
    with laspy.open(display) as reader:
        expect(reader.header.point_count == down["data"].get("pointsOut"), "display.laz holds the kept points")

    nth = job("everynth", "pointcloud.downsample", {"method": "everyNth", "everyNth": 4},
              {"pointCloudLaz": "cloud.laz"}, {"cloud.laz": jobs / "estimate" / "cloud.laz"})
    expect(nth["status"] == "ok" and nth["data"].get("pointsOut") == n // 4, "every 4th point keeps a quarter")

    tiled = job("tile", "py3dtiles.tile", {"specVersion": "1.0", "rgb": True, "intensity": True},
                {"pointCloudLaz": "cloud.laz", "pointCloudLazDisplay": "display.laz"},
                {"cloud.laz": jobs / "estimate" / "cloud.laz", "display.laz": display})
    expect(tiled["status"] == "ok", f"tile ran: {tiled['status']} {tiled['reason']}")
    if tiled["status"] == "ok":
        archive = jobs / "tile" / "tiles.zip"
        with zipfile.ZipFile(archive) as pack:
            names = pack.namelist()
            expect("tileset.json" in names, "the archive holds tileset.json")
            expect(any(name.endswith(".pnts") for name in names), "the archive holds .pnts tiles")
            tileset = json.loads(pack.read("tileset.json"))
        shift = tiled["data"]["originShift"]
        expect(abs(shift[0] - 500_004) <= 1 and abs(shift[1] - 4_700_000) <= 1, f"the shift is the cloud's centre: {shift}")
        # Nothing in the tileset may hold the real coordinates: every number in it stays small.
        def numbers(value):
            if isinstance(value, dict):
                for v in value.values():
                    yield from numbers(v)
            elif isinstance(value, list):
                for v in value:
                    yield from numbers(v)
            elif isinstance(value, (int, float)):
                yield abs(value)
        largest = max(numbers(tileset), default=0)
        expect(largest < 1000, f"tileset.json carries no coordinate (largest number {largest})")
        expect(tiled["data"]["pointCount"] == down["data"]["pointsOut"], "the display copy was tiled")
        expect(not (jobs / "tile" / "tiles").exists() and not (jobs / "tile" / "local.laz").exists(),
               "the tile job leaves only its products behind")

    # The pictures: a top view of the tube and a profile unrolled along its axis, 8 m long.
    class Text:
        def __init__(self, text: str):
            self.text = text

        def write(self, path: Path) -> None:
            path.write_text(self.text, encoding="utf-8")

    axis = {"type": "Feature", "properties": {},
            "geometry": {"type": "LineString", "coordinates": [[500_000.0, 4_700_000.0, 300.0], [500_008.0, 4_700_000.0, 300.0]]}}
    for colour in ("height", "intensity"):
        drawn = job(f"raster-{colour}", "pointcloud.raster", {"pixelCm": 10, "colourBy": colour},
                    {"pointCloudLaz": "cloud.laz", "centreline": "centreline.geojson"},
                    {"cloud.laz": jobs / "estimate" / "cloud.laz", "centreline.geojson": Text(json.dumps(axis))})
        expect(drawn["status"] == "ok", f"pictures by {colour} drawn: {drawn['status']} {drawn['reason']}")
        if drawn["status"] != "ok":
            continue
        data = drawn["data"]
        expect(sorted(o["kind"] for o in drawn["outputs"]) == ["planImage", "profileImage"], "a top view and a profile")
        for name, size in (("plan.png", data["plan"]), ("profile.png", data["profile"])):
            head = (jobs / f"raster-{colour}" / name).read_bytes()[:24]
            expect(head[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", head[16:24]) == (size["width"], size["height"]),
                   f"{name} is a PNG of {size['width']}x{size['height']}")
        expect(data["colourBy"] == colour, f"coloured by {colour}")
        expect(abs(data["profile"]["lengthM"] - 8.0) < 0.01, f"the profile is the axis's length: {data['profile']['lengthM']}")
        # 8 m by about 3 m at 10 cm; the profile runs a quarter-metre sample spacing past the line's end.
        expect(data["plan"]["width"] in (80, 81) and 29 <= data["plan"]["height"] <= 33
               and 80 <= data["profile"]["width"] <= 84 and 29 <= data["profile"]["height"] <= 33,
               f"10 cm pixels: {data['plan']}, {data['profile']}")
        largest = max(abs(v) for v in (data["valueLow"], data["valueHigh"], data["profile"]["lengthM"]))
        expect(largest < 70_000, f"the pictures' figures carry no coordinate (largest {largest})")

    # The surface: the tube made into a mesh, once with the scanner's path along its axis and once
    # without one, its walls facing inward both times, and no coordinate in the file.
    try:
        load_open3d()
        has_open3d = True
    except ImportError:
        has_open3d = False
        print("skip the surface: open3d is not installed here")
    if has_open3d:
        poses = "\n".join(f"{i}, 0, {500_000.0 + 0.1 * i:.3f}, 4700000.0, 300.0, 0, 0, 0, 1" for i in range(81))
        parameters = {"voxelCm": 5, "normalRadiusCm": 20, "poissonDepth": 7, "densityTrimPercent": 2,
                      "targetTriangles": 4_000}
        # The tiles were made from the display copy, whose centre is not the original's; the
        # application hands the tiles' shift over, so the surface lands in their frame. Without it
        # the surface takes the original cloud's own centre.
        tiles_shift = tiled["data"]["originShift"] if tiled["status"] == "ok" else [500_003.0, 4_699_999.0, 300.0]
        for name, inputs, files, shift in (
                ("surface", {"pointCloudLaz": "cloud.laz", "trajectory": "trajectory.csv"},
                 {"cloud.laz": jobs / "estimate" / "cloud.laz", "trajectory.csv": Text(poses)}, tiles_shift),
                ("surface-bare", {"pointCloudLaz": "cloud.laz"}, {"cloud.laz": jobs / "estimate" / "cloud.laz"},
                 None)):
            made = job(name, "mesh.surface", dict(parameters, **({"originShift": shift} if shift else {})), inputs, files)
            expect(made["status"] == "ok", f"{name} made: {made['status']} {made['reason']}")
            if made["status"] != "ok":
                continue
            data = made["data"]
            expect([o["kind"] for o in made["outputs"]] == ["meshStl"], "one STL")
            stl = (jobs / name / "surface.stl").read_bytes()
            count = struct.unpack("<I", stl[80:84])[0]
            expect(len(stl) == 84 + 50 * count and count == data["triangles"] and 0 < count <= 4_000,
                   f"a binary STL of {count:,} triangles, at most the target")
            facets = np.frombuffer(stl[84:], dtype=np.dtype([("n", "<f4", (3,)), ("v", "<f4", (3, 3)), ("a", "<u2")]))
            corners = facets["v"].astype(np.float64)
            expect(float(np.abs(corners).max()) < 1000, "the STL carries no coordinate")
            centre = corners.mean(axis=1)
            normal = np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0])
            # Inward on the tube's wall: toward its axis, which runs along x at y = z = 0 here.
            wall = np.hypot(centre[:, 1], centre[:, 2]) > 1.0
            inward = (normal[wall, 1] * -centre[wall, 1] + normal[wall, 2] * -centre[wall, 2]) > 0
            share = float(inward.mean()) if inward.size else 0.0
            expect(share > 0.9, f"{name}: the walls face inward ({share:.0%}, oriented by {data['orientation']})")
            with laspy.open(jobs / "estimate" / "cloud.laz") as reader:
                own_centre = [float(np.floor((lo + hi) / 2.0)) for lo, hi in zip(reader.header.mins, reader.header.maxs)]
            expect(data["originShift"] == (shift or own_centre), f"{name}: written about {'the tiles' if shift else 'its own'} shift")
        refused = job("surface-refused", "mesh.surface", {"poissonDepth": 40}, {"pointCloudLaz": "cloud.laz"},
                      {"cloud.laz": jobs / "estimate" / "cloud.laz"})
        expect(refused["status"] == "refused" and "poissonDepth" in refused["reason"], "a depth out of bounds is refused")

    unknown = job("unknown", "nothing.at.all", {}, {}, {})
    expect(unknown["status"] == "refused", "an unknown step is refused")

    print("self-test " + ("PASSED" if not failures else f"FAILED ({len(failures)})"))
    return 0 if not failures else 1


def main(argv: list[str]) -> int:
    if len(argv) >= 2 and argv[1] == "--self-test":
        target = Path(argv[2]) if len(argv) > 2 else ROOT / "self-test"
        if target.exists():
            shutil.rmtree(target)
        return self_test(target)
    if len(argv) >= 2 and argv[1] == "--once":
        return serve(once=True)
    if len(argv) >= 2:
        say(f"unknown argument {argv[1]!r}; use --once or --self-test [dir]")
        return 2
    return serve(once=False)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
