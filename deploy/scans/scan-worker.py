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

    capabilities.json           rewritten by this worker at every start: which steps it can run,
                                with which tool at which version. Absent = no worker.
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

Three things this loop deliberately does not do: it does not judge the outcome beyond the tool's
exit code (the log is kept for the side that reads the result); it does not delete anything outside
the job directory it is working in; and it does not retry — a job it took and never finished (the
container was stopped mid-run) is answered "interrupted" rather than run again, because only the
application can decide whether its half-written outputs are worth anything.

Only the standard library is needed to run the loop and answer a request; laspy, numpy and py3dtiles
are imported when a step needs them, so the protocol can be exercised without them.
"""
from __future__ import annotations

import datetime as _dt
import hashlib
import json
import os
import resource
import shutil
import signal
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

STEPS = ("pointcloud.estimate", "pointcloud.downsample", "py3dtiles.tile")
LOG_TAIL_LINES = 30


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


def tool_versions() -> dict[str, str]:
    versions: dict[str, str] = {}
    try:
        from importlib.metadata import version
        for package in ("py3dtiles", "laspy", "lazrs", "numpy"):
            try:
                versions[package] = version(package)
            except Exception:  # noqa: BLE001 - absent is a fact to report, not an error
                pass
    except Exception:  # noqa: BLE001
        pass
    return versions


def capabilities() -> dict:
    versions = tool_versions()
    steps = []
    if "laspy" in versions and "numpy" in versions:
        steps.append({"id": "pointcloud.estimate", "tool": "laspy", "version": versions["laspy"]})
        steps.append({"id": "pointcloud.downsample", "tool": "laspy", "version": versions["laspy"]})
    if "py3dtiles" in versions:
        steps.append({"id": "py3dtiles.tile", "tool": "py3dtiles", "version": versions["py3dtiles"]})
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
    hdmapping = os.environ.get("HDMAPPING_COMMIT")
    if hdmapping:
        value["hdmapping"] = hdmapping
    return value


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

    def run_tool(self, command: list[str], progress_from_line=None) -> None:
        """Runs a command line into worker.log, watching for a withdrawal and the time limit."""
        self.log("running: " + " ".join(command))
        with self.log_path.open("ab") as log:
            process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT,
                                       cwd=self.directory, start_new_session=True)
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
        return lines[-LOG_TAIL_LINES:]


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


RUNNERS = {
    "pointcloud.estimate": step_estimate,
    "pointcloud.downsample": step_downsample,
    "py3dtiles.tile": step_tile,
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


def run_job(directory: Path) -> None:
    job = Job(directory)
    (directory / "started").write_text(job.started_at + "\n", encoding="utf-8")
    try:
        try:
            job.request = json.loads((directory / "request.json").read_text(encoding="utf-8"))
        except (OSError, ValueError) as error:
            raise Refused(f"request.json cannot be read: {error}") from error
        if not isinstance(job.request, dict):
            raise Refused("request.json is not an object")
        step = job.request.get("step")
        if step not in RUNNERS:
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
        job.progress(0.0, "starting")
        job.log(f"step {step} in {directory.name}")
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


def sweep(jobs: Path) -> int:
    """Answers every waiting job once. Returns how many it took."""
    taken = 0
    for directory in pending_jobs(jobs):
        if (directory / "cancel").exists():
            job = Job(directory)
            finish(job, "cancelled", "the job was called off before this worker ran it")
            continue
        if (directory / "started").exists():
            job = Job(directory)
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
    jobs = ROOT / "jobs"
    try:
        jobs.mkdir(parents=True, exist_ok=True)
        write_json(ROOT / "capabilities.json", capabilities())
    except OSError as error:
        say(f"cannot write {ROOT} — the scans volume must be mounted there and owned by uid {os.getuid()}: {error}")
        return 1
    running = True

    def stop(_signal, _frame):
        nonlocal running
        running = False

    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    say(f"scan worker {WORKER_VERSION} watching {jobs} every {POLL_SECONDS}s; steps: "
        + ", ".join(s["id"] for s in capabilities()["steps"]))
    while running:
        sweep(jobs)
        if once:
            break
        time.sleep(int(POLL_SECONDS))
    say("scan worker stopping")
    return 0


# -- the self-test -----------------------------------------------------------------------------

def self_test(root: Path) -> int:
    """A tiny generated cloud through estimate, downsample and tile, printing a verdict only."""
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
