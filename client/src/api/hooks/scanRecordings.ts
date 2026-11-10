// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';
import { queryKeys } from './queryKeys.ts';
import type { PointCloudUploadPhase } from './surveyUploads.ts';

export type SurveyUploadRecording = components['schemas']['SurveyUploadRecordingDto'];
export type SurveyRecordingCapabilities = components['schemas']['SurveyRecordingCapabilitiesDto'];

/**
 * The limits a recording is checked against before the capabilities have answered: the server's
 * own defaults (a 4 GiB file, 20 GiB together, 5,000 files, odometry needing 22 times the
 * recording on the worker's disk). The server checks the real ones again when the upload opens.
 */
export const DEFAULT_RECORDING_CAPABILITIES: SurveyRecordingCapabilities = {
  maxFileBytes: 4 * 1024 * 1024 * 1024,
  maxTotalBytes: 20 * 1024 * 1024 * 1024,
  maxFiles: 5000,
  acceptedExtensions: ['.laz', '.csv', '.sn', '.gnss', '.nmea', '.json'],
  odometryWorkerFactor: 22,
};

/** What one file of a scanner's recording folder is, by its name. */
export type RecordingFileKind = 'lidar' | 'imu' | 'serial' | 'gnss' | 'nmea' | 'calibration';

export interface RecordingFileName {
  kind: RecordingFileKind;
  /** The chunk the file belongs to, for the files written once per chunk. */
  chunk: number | null;
}

const CHUNKED: readonly [RegExp, RecordingFileKind][] = [
  [/^lidar(\d{1,6})\.laz$/i, 'lidar'],
  [/^imu(\d{1,6})\.csv$/i, 'imu'],
  [/^lidar(\d{1,6})\.sn$/i, 'serial'],
  [/^gnss(\d{1,6})\.gnss$/i, 'gnss'],
];

/**
 * What a file name is in a MANDEYE recording, or null for a name a recording does not hold.
 *
 * The same names the server accepts: a lidar chunk with its IMU log, optionally the sensors'
 * serial numbers, a GNSS log, NMEA files and the calibration. The server is the authority — it
 * refuses any other name — so this copy only picks the files out of a dropped folder; if the two
 * ever drift apart, the upload is refused with the name rather than accepted wrongly.
 */
export function recordingFileKind(name: string): RecordingFileName | null {
  for (const [pattern, kind] of CHUNKED) {
    const match = pattern.exec(name);
    if (match) {
      return { kind, chunk: Number.parseInt(match[1]!, 10) };
    }
  }
  if (name.toLowerCase().endsWith('.nmea') && name.length > '.nmea'.length) {
    return { kind: 'nmea', chunk: null };
  }
  return name.toLowerCase() === 'calibration.json' ? { kind: 'calibration', chunk: null } : null;
}

/** A file as a folder pick or a drop gives it: its bare name, its size and its path inside what was dropped. */
export interface RecordingCandidate {
  name: string;
  size: number;
  /** `webkitRelativePath`: empty for a file dropped or picked on its own, `folder/name` inside a folder. */
  relativePath: string;
}

/** Something about the chosen folder that the server would refuse, said before anything is sent. */
export type RecordingProblem =
  | { code: 'noLidar' }
  | { code: 'unpaired'; chunk: number }
  | { code: 'tooManyFiles'; max: number }
  | { code: 'tooLarge'; max: number }
  | { code: 'fileTooLarge'; name: string; max: number }
  | { code: 'duplicate'; name: string }
  | { code: 'empty'; name: string };

export interface RecordingSelection<T extends RecordingCandidate> {
  /** The files of the recording, in name order: what the upload declares and sends. */
  files: T[];
  /** Files that were dropped but are not the recording's: camera pictures, other folders, anything else. */
  skipped: number;
  /** The recording's files together, in bytes. */
  totalBytes: number;
  /** How many lidar chunks it holds. */
  chunkCount: number;
  /** The folder the recording was taken from, inside what was dropped; empty for loose files. */
  folder: string;
  hasGnss: boolean;
  hasCalibration: boolean;
  /** What would be refused, the first of each kind; empty when it can be sent. */
  problems: RecordingProblem[];
}

function folderOf(relativePath: string): string {
  const slash = relativePath.lastIndexOf('/');
  return slash < 0 ? '' : relativePath.slice(0, slash);
}

function depthOf(folder: string): number {
  return folder === '' ? 0 : folder.split('/').length;
}

/**
 * The recording out of what was dropped or picked.
 *
 * The scanner writes a recording as one folder of files with fixed names, beside camera folders
 * (`CAMERA_*`) whose pictures are not uploaded. A drop can carry more than the recording — the
 * parent folder, a camera folder, a second recording — so the recording is the folder of the
 * shallowest lidar chunk (the first by path when two are as shallow), and only the files of that
 * folder whose names a recording holds are taken. Everything else is counted as left out, so the
 * person sees that it was, rather than having the server refuse a camera picture by name.
 *
 * The limits are the server's, published with its capabilities, and checked here only so the
 * dialog can say what is wrong before gigabytes are sent: the server checks them again.
 */
export function recordingSelection<T extends RecordingCandidate>(
  candidates: readonly T[],
  limits: Pick<SurveyRecordingCapabilities, 'maxFileBytes' | 'maxTotalBytes' | 'maxFiles'>,
): RecordingSelection<T> {
  const lidarFolders = candidates
    .filter((c) => recordingFileKind(c.name)?.kind === 'lidar')
    .map((c) => folderOf(c.relativePath))
    .sort((a, b) => depthOf(a) - depthOf(b) || a.localeCompare(b));
  const folder = lidarFolders[0] ?? '';

  const files: T[] = [];
  const seen = new Set<string>();
  const problems: RecordingProblem[] = [];
  const lidar = new Set<number>();
  const imu = new Set<number>();
  let hasGnss = false;
  let hasCalibration = false;
  let duplicate = false;
  let oversize = false;
  let empty = false;
  for (const candidate of candidates) {
    const kind = recordingFileKind(candidate.name);
    if (!kind || folderOf(candidate.relativePath) !== folder) {
      continue;
    }
    const key = candidate.name.toLowerCase();
    if (seen.has(key)) {
      if (!duplicate) {
        problems.push({ code: 'duplicate', name: candidate.name });
        duplicate = true;
      }
      continue;
    }
    seen.add(key);
    files.push(candidate);
    if (candidate.size <= 0 && !empty) {
      problems.push({ code: 'empty', name: candidate.name });
      empty = true;
    }
    if (candidate.size > limits.maxFileBytes && !oversize) {
      problems.push({ code: 'fileTooLarge', name: candidate.name, max: limits.maxFileBytes });
      oversize = true;
    }
    if (kind.kind === 'lidar') lidar.add(kind.chunk!);
    if (kind.kind === 'imu') imu.add(kind.chunk!);
    if (kind.kind === 'gnss' || kind.kind === 'nmea') hasGnss = true;
    if (kind.kind === 'calibration') hasCalibration = true;
  }
  files.sort((a, b) => a.name.localeCompare(b.name));

  const totalBytes = files.reduce((sum, file) => sum + file.size, 0);
  if (lidar.size === 0) {
    problems.push({ code: 'noLidar' });
  }
  const unpaired = [...lidar].sort((a, b) => a - b).find((chunk) => !imu.has(chunk));
  if (unpaired !== undefined) {
    problems.push({ code: 'unpaired', chunk: unpaired });
  }
  if (files.length > limits.maxFiles) {
    problems.push({ code: 'tooManyFiles', max: limits.maxFiles });
  }
  if (totalBytes > limits.maxTotalBytes) {
    problems.push({ code: 'tooLarge', max: limits.maxTotalBytes });
  }

  return {
    files,
    skipped: candidates.length - files.length,
    totalBytes,
    chunkCount: lidar.size,
    folder,
    hasGnss,
    hasCalibration,
    problems,
  };
}

/** The candidate a browser `File` is, with the folder path a folder pick or a drop gives it. */
export function candidateOf(file: File): RecordingCandidate & { file: File } {
  return { name: file.name, size: file.size, relativePath: file.webkitRelativePath ?? '', file };
}

/** The recording's name from its folder: what the survey is called unless the uploader says otherwise. */
export function titleFromRecordingFolder(folder: string): string {
  const slash = folder.lastIndexOf('/');
  return (slash < 0 ? folder : folder.slice(slash + 1)).trim();
}

/**
 * Whether an upload is a raw recording's: by its facts once the run has read it, and before that
 * by the IMU logs it was declared with — a recording always has one beside each lidar chunk, and
 * a single point cloud never has any. A refined session sent back as a recording's next version
 * runs the recording's later stages, so its one session archive counts too.
 */
export function surveyUploadIsRecording(upload: {
  pointCloud?: { recording: unknown } | null;
  files: readonly { name: string; role: string }[];
}): boolean {
  return (
    !!upload.pointCloud?.recording ||
    upload.files.some(
      (file) =>
        file.role === 'archive' &&
        (recordingFileKind(file.name)?.kind === 'imu' || file.name.toLowerCase() === REFINED_SESSION_NAME),
    )
  );
}

export interface RecordingUploadRequest {
  caveId: string;
  files: readonly File[];
  surveyTitle: string;
  /** The processing profiles chosen, at most one per step; none means every step runs with its defaults. */
  profiles?: readonly string[];
  /** The steps, by id, the person will do on another computer instead of the server's worker. */
  externalSteps?: readonly string[];
  /** Where the upload has got to; while sending, which file of how many. */
  onPhase?: (phase: PointCloudUploadPhase, progress?: { done: number; total: number }) => void;
}

/**
 * Sends a raw recording the way a device sends a survey: the upload is opened with every file of
 * the folder declared, by its bare name, as the archive, the files go one request each, and the
 * completion queues the run. One file at a time, because a recording can be thousands of files
 * and gigabytes; a file that fails stops the upload, and the session stays open on the server to
 * be abandoned or sent again.
 */
export function useUploadRecording() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ caveId, files, surveyTitle, profiles, externalSteps, onPhase }: RecordingUploadRequest) => {
      onPhase?.('opening');
      const opened = await unwrap(
        api.POST('/api/v1/survey-uploads', {
          body: {
            source: 'mandeye',
            client: { name: 'SilexGIS web', version: null },
            cave: { id: caveId },
            caveSurvey: null,
            newCave: null,
            surveyTitle,
            surveyName: null,
            description: null,
            files: files.map((file) => ({ name: file.name, size: file.size, role: 'archive' as const })),
            pipeline: null,
            documentsVisibility: null,
            display: null,
            profiles: profiles && profiles.length > 0 ? [...profiles] : null,
            externalSteps: externalSteps && externalSteps.length > 0 ? [...externalSteps] : null,
          },
        }),
      );
      for (const [index, file] of files.entries()) {
        onPhase?.('sending', { done: index, total: files.length });
        await unwrap(
          api.PUT('/api/v1/survey-uploads/{id}/files/{name}', {
            params: { path: { id: opened.id, name: file.name } },
            body: file as never,
            bodySerializer: (b: unknown) => b as Blob,
            headers: { 'Content-Type': 'application/octet-stream' },
          }),
        );
      }
      onPhase?.('completing');
      return unwrap(api.POST('/api/v1/survey-uploads/{id}/complete', { params: { path: { id: opened.id } } }));
    },
    onSuccess: (upload) => {
      queryClient.setQueryData(queryKeys.surveyUpload(upload.id), upload);
      void queryClient.invalidateQueries({ queryKey: queryKeys.caveSurveys(upload.cave.id) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.surveyModels(upload.cave.id) });
    },
  });
}

/** The name a refined HDMapping session is sent under, whatever it was called on the desktop. */
export const REFINED_SESSION_NAME = 'session.zip';

export interface RefinedSessionUploadRequest {
  /** The scan's survey, which names its cave too: the refined session becomes its next version. */
  caveSurveyId: string;
  surveyTitle: string;
  /** The session folder zipped, with `session.mjs` at its root. */
  file: File;
  onPhase?: (phase: PointCloudUploadPhase) => void;
}

/**
 * Sends a session refined in HDMapping's desktop program as the next version of the scan it was
 * made from: an upload of the scan's survey with the one archive declared under the session's
 * fixed name. Its run starts at the export, against the recording already on the server.
 */
export function useUploadRefinedSession() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ caveSurveyId, surveyTitle, file, onPhase }: RefinedSessionUploadRequest) => {
      onPhase?.('opening');
      const opened = await unwrap(
        api.POST('/api/v1/survey-uploads', {
          body: {
            source: 'mandeye',
            client: { name: 'SilexGIS web', version: null },
            // The survey alone: it names its cave, and the server refuses a cave named twice.
            cave: null,
            caveSurvey: { id: caveSurveyId },
            newCave: null,
            surveyTitle,
            surveyName: null,
            description: null,
            files: [{ name: REFINED_SESSION_NAME, size: file.size, role: 'archive' }],
            pipeline: null,
            documentsVisibility: null,
            display: null,
          },
        }),
      );
      onPhase?.('sending');
      await unwrap(
        api.PUT('/api/v1/survey-uploads/{id}/files/{name}', {
          params: { path: { id: opened.id, name: REFINED_SESSION_NAME } },
          body: file as never,
          bodySerializer: (b: unknown) => b as Blob,
          headers: { 'Content-Type': 'application/octet-stream' },
        }),
      );
      onPhase?.('completing');
      return unwrap(api.POST('/api/v1/survey-uploads/{id}/complete', { params: { path: { id: opened.id } } }));
    },
    onSuccess: (upload) => {
      queryClient.setQueryData(queryKeys.surveyUpload(upload.id), upload);
      void queryClient.invalidateQueries({ queryKey: queryKeys.caveSurveys(upload.cave.id) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.surveyModels(upload.cave.id) });
    },
  });
}
