// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';
import { queryKeys } from './queryKeys.ts';

export type SurveyUpload = components['schemas']['SurveyUploadDto'];

// The figures an upload's reading produced, as the cave's statistics panels draw them. Named
// here so those panels read the generated schema through this module, like every other caller.
export type SurveyUploadStatistics = components['schemas']['SurveyUploadStatisticsDto'];
export type SurveyUploadTopology = components['schemas']['SurveyUploadTopologyDto'];
export type SurveyUploadOrientation = components['schemas']['SurveyUploadOrientationDto'];
export type SurveyUploadDepthProfile = components['schemas']['SurveyUploadDepthProfileDto'];
export type SurveyUploadListItem = components['schemas']['SurveyUploadListItemDto'];
export type SurveyUploadPage = components['schemas']['SurveyUploadPageDto'];
export type SurveyUploadArtefact = components['schemas']['SurveyUploadArtefactDto'];
export type SurveyUploadFile = components['schemas']['SurveyUploadFileDto'];
export type SurveyUploadCapabilities = components['schemas']['SurveyUploadCapabilitiesDto'];
export type SurveyUploadPipelineRequest = components['schemas']['SurveyUploadPipelineRequest'];
export type SurveyUploadPreferences = components['schemas']['SurveyUploadPreferencesDto'];
export type SurveyUploadStatus = components['schemas']['SurveyUploadStatus'];
export type SurveyUploadStage = components['schemas']['SurveyUploadStage'];
export type SurveyOrigin = components['schemas']['SurveyOrigin'];
export type SurveyUploadPointCloud = components['schemas']['SurveyUploadPointCloudDto'];
export type SurveyUploadPointCloudEstimateRow = components['schemas']['SurveyUploadPointCloudEstimateRowDto'];
export type SurveyUploadDisplayRequest = components['schemas']['SurveyUploadDisplayRequest'];

/** How often an unfinished upload is asked about, in milliseconds. */
export const SURVEY_UPLOAD_POLL_MS = 2000;

/**
 * The stages a survey archive's run goes through, in the order it goes through them. The server
 * reports only the stage it is in; every other stage's state is worked out from this order by
 * {@link surveyUploadStageStates}.
 */
export const SURVEY_UPLOAD_STAGE_ORDER: readonly SurveyUploadStage[] = [
  'archive',
  'unpack',
  'scaffold',
  'compile',
  'store',
  'read',
];

/**
 * The stages a point cloud's run goes through. Nothing is unpacked or compiled: the file's header
 * is read (inspect), the cloud is estimated and, when chosen, thinned (downsample), tiled for the
 * 3D scene (tile) and the tiles stored. A run that stops to ask how much to show ends at
 * downsample; a server without a tiling worker ends at tile.
 */
export const POINT_CLOUD_STAGE_ORDER: readonly SurveyUploadStage[] = [
  'archive',
  'inspect',
  'downsample',
  'tile',
  'store',
];

/** The file extensions a point-cloud upload is made of; the server accepts exactly one such file. */
export const POINT_CLOUD_EXTENSIONS = ['.laz', '.las'] as const;

/** How the advice level is spelled on the wire. */
export type PointCloudAdviceLevel = 'none' | 'warn' | 'advise';

/** How much of a cloud to show: the whole of it, or one point per cell of the given size. */
export type PointCloudDisplayChoice = { method: 'full' } | { method: 'voxel'; voxelCm: number };

/**
 * Whether the file is a point cloud by its name. Decided here, once, so the upload entry, the
 * status page and the cave page agree on which uploads are clouds.
 */
export function isPointCloudFileName(name: string): boolean {
  const lower = name.toLowerCase();
  return POINT_CLOUD_EXTENSIONS.some((extension) => lower.endsWith(extension));
}

/**
 * Whether an upload is a point cloud's. The single upload's answer does not name its source, so
 * it is told by its facts once the header has been read, and before that by the one archive file
 * it was declared with — a cloud is always exactly one `.laz` or `.las` file declared as the
 * archive, and no survey archive is ever named so.
 */
export function surveyUploadIsPointCloud(
  upload: Pick<SurveyUpload, 'pointCloud'> & { files: readonly Pick<SurveyUploadFile, 'name' | 'role'>[] },
): boolean {
  return (
    !!upload.pointCloud ||
    upload.files.some((file) => file.role === 'archive' && isPointCloudFileName(file.name))
  );
}

/**
 * Whether the run stopped to ask how much of the cloud to show: it ended at the downsample stage
 * with the estimate on the facts and no tiles made. The person's choice goes back as a rerun with
 * a display request, and the run carries on from the estimate.
 */
export function pointCloudChoiceNeeded(
  upload: Pick<SurveyUpload, 'status' | 'stage' | 'pointCloud'>,
): boolean {
  return (
    upload.status === 'succeeded' &&
    upload.stage === 'downsample' &&
    !!upload.pointCloud &&
    upload.pointCloud.estimate !== null &&
    upload.pointCloud.tilesetUrl === null
  );
}

/**
 * The display choice the estimate table pre-selects: the server's suggested voxel when it advised
 * thinning, the full cloud otherwise. A suggestion names a row of the table, so the choice is the
 * row rather than a free number.
 */
export function suggestedDisplayChoice(cloud: Pick<SurveyUploadPointCloud, 'advice'>): PointCloudDisplayChoice {
  const suggested = cloud.advice?.suggestedVoxelCm;
  return suggested !== null && suggested !== undefined && cloud.advice?.level !== 'none'
    ? { method: 'voxel', voxelCm: suggested }
    : { method: 'full' };
}

/** The request body for a display choice, as the server reads it. */
export function displayRequestOf(choice: PointCloudDisplayChoice): SurveyUploadDisplayRequest {
  return choice.method === 'full' ? { method: 'full' } : { method: 'voxel', voxelCm: choice.voxelCm };
}

/**
 * The choice the run was made with, read back from the facts: the voxel used, in centimetres, or
 * the full cloud. Null while nothing has been tiled yet.
 */
export function displayChoiceUsed(
  cloud: Pick<SurveyUploadPointCloud, 'tilesetUrl' | 'displayVoxelM'>,
): PointCloudDisplayChoice | null {
  if (cloud.tilesetUrl === null) {
    return null;
  }
  return cloud.displayVoxelM === null
    ? { method: 'full' }
    : { method: 'voxel', voxelCm: Math.round(cloud.displayVoxelM * 100 * 100) / 100 };
}

/**
 * Whether the upload is still going somewhere. A session still receiving files counts: its files
 * come from a phone, and the page showing it is the only thing that will notice the last one.
 */
export function surveyUploadUnsettled(status: SurveyUploadStatus): boolean {
  return status === 'receiving' || status === 'queued' || status === 'running';
}

/**
 * How long to wait before asking about one upload again, or `false` for "stop asking".
 *
 * Exported and pure so the rule that a finished, failed or abandoned upload is left alone can be
 * asserted directly instead of by watching a live query do nothing for two seconds.
 */
export function surveyUploadPollInterval(status: SurveyUploadStatus | undefined): number | false {
  return status !== undefined && surveyUploadUnsettled(status) ? SURVEY_UPLOAD_POLL_MS : false;
}

/** The same rule for a listing: keep asking only while something on it is unfinished. */
export function surveyUploadListPollInterval(
  items: { status: SurveyUploadStatus }[] | undefined,
): number | false {
  return (items ?? []).some((item) => surveyUploadUnsettled(item.status)) ? SURVEY_UPLOAD_POLL_MS : false;
}

/** What one stage of the list is doing. `skipped` is a stage the run did not need or did not reach. */
export type StageState = 'done' | 'active' | 'pending' | 'failed' | 'skipped';

export interface StageProgress {
  stage: SurveyUploadStage;
  state: StageState;
}

/** What {@link surveyUploadStageStates} reads: the same three members on the signed-in answer and on the public one. */
export interface StageInputs {
  status: SurveyUploadStatus;
  stage: SurveyUploadStage | null;
  /** Whether the run was asked to compile. Unknown (undefined) is treated as asked: nothing is hidden for a guess. */
  compile?: boolean;
  /** Whether the upload is a point cloud, whose run has stages of its own. Unknown is a survey archive. */
  pointCloud?: boolean;
}

/**
 * The state of every stage, from the one stage the server names.
 *
 * - Running: stages before the current one are done, the current one is active, the rest wait.
 * - Failed: the same, with the current stage marked failed — that is where the run stopped.
 * - Succeeded: stages up to the last one the run reached are done and any after it are `skipped`.
 *   A run that archived and read the files but did not compile ends at the unpack stage, and
 *   showing compile, store and read as done would claim work that never happened.
 * - Queued, receiving and abandoned: nothing has started, so everything waits.
 * - A run that was not asked to compile has no scaffold or compile stage, whatever else is true.
 * - A point cloud's run walks its own list of stages, and nothing in it is a compile.
 */
export function surveyUploadStageStates(input: StageInputs): StageProgress[] {
  const order = input.pointCloud ? POINT_CLOUD_STAGE_ORDER : SURVEY_UPLOAD_STAGE_ORDER;
  const index = input.stage === null ? -1 : order.indexOf(input.stage);
  const notCompiling = !input.pointCloud && input.compile === false;

  return order.map((stage, i) => {
    if (notCompiling && (stage === 'scaffold' || stage === 'compile')) {
      return { stage, state: 'skipped' };
    }
    switch (input.status) {
      case 'running':
        // Running with no stage yet is the moment between the job starting and its first report.
        if (index < 0) {
          return { stage, state: i === 0 ? 'active' : 'pending' };
        }
        return { stage, state: i < index ? 'done' : i === index ? 'active' : 'pending' };
      case 'failed':
        if (index < 0) {
          return { stage, state: i === 0 ? 'failed' : 'pending' };
        }
        return { stage, state: i < index ? 'done' : i === index ? 'failed' : 'pending' };
      case 'succeeded':
        if (index < 0) {
          return { stage, state: 'done' };
        }
        return { stage, state: i <= index ? 'done' : 'skipped' };
      default:
        return { stage, state: 'pending' };
    }
  });
}

/** Whether an artefact has a page of its own in the application to open, besides the file to download. */
export function surveyUploadArtefactIsViewable(artefact: Pick<SurveyUploadArtefact, 'viewUrl'>): boolean {
  return !!artefact.viewUrl;
}

/**
 * The text of a public link to copy: the server writes a path (`/p/survey/<token>`) so the link
 * stays right behind whatever address the installation is reached on, and what is pasted into a
 * message has to carry that address. An address that is already complete is left as it is.
 */
export function surveyUploadLinkHref(url: string, origin: string): string {
  return /^https?:\/\//i.test(url) ? url : new URL(url, origin).toString();
}

/**
 * The outputs to save as the account's *always produce* list: only names the installation
 * produces, each once, in the catalogue's own order — what the server will store whatever it is
 * sent, and it refuses a name it does not know.
 */
export function normaliseAlwaysOutputs(selected: readonly string[], catalogue: readonly string[]): string[] {
  const wanted = new Set(selected.map((name) => name.trim().toLowerCase()));
  return catalogue.filter((name) => wanted.has(name));
}

/** One survey of a cave, with every upload of it (a version each), newest first. */
export interface CaveSurveyGroup {
  id: string;
  title: string;
  source: SurveyOrigin;
  /** Uploads that have been handed over completely, newest version first. */
  versions: SurveyUploadListItem[];
  /** How many of them there are: a session still receiving files, or abandoned, is not a version. */
  versionCount: number;
  /** The newest version's moment, which is what orders the surveys of a cave. */
  newestVersionAt: string;
}

/**
 * A cave's surveys from its uploads.
 *
 * The server lists uploads; a survey is the thing they are versions of. A session that never
 * finished receiving, or was abandoned, produced nothing a reader can open, so it is left out
 * rather than shown as an empty version. Surveys come newest first by their newest version.
 */
export function groupCaveSurveys(items: readonly SurveyUploadListItem[]): CaveSurveyGroup[] {
  const groups = new Map<string, CaveSurveyGroup>();
  for (const item of items) {
    if (item.status === 'receiving' || item.status === 'abandoned') {
      continue;
    }
    const existing = groups.get(item.caveSurvey.id);
    if (existing) {
      existing.versions.push(item);
    } else {
      groups.set(item.caveSurvey.id, {
        id: item.caveSurvey.id,
        title: item.caveSurvey.title,
        source: item.source,
        versions: [item],
        versionCount: 0,
        newestVersionAt: item.version,
      });
    }
  }
  const byVersionDescending = (a: SurveyUploadListItem, b: SurveyUploadListItem) =>
    Date.parse(b.version) - Date.parse(a.version);
  return [...groups.values()]
    .map((group) => {
      const versions = [...group.versions].sort(byVersionDescending);
      return { ...group, versions, versionCount: versions.length, newestVersionAt: versions[0]!.version };
    })
    .sort((a, b) => Date.parse(b.newestVersionAt) - Date.parse(a.newestVersionAt));
}

const LIST_PAGE_SIZE = 100;
// A cave with more versions than this has more than a page can usefully show; the cap only
// stops a misbehaving answer from looping.
const LIST_PAGE_LIMIT = 20;

async function fetchAllUploadsOfCave(caveId: string): Promise<SurveyUploadListItem[]> {
  const items: SurveyUploadListItem[] = [];
  for (let page = 1; page <= LIST_PAGE_LIMIT; page += 1) {
    const answer = await unwrap(
      api.GET('/api/v1/survey-uploads', { params: { query: { caveId, page, pageSize: LIST_PAGE_SIZE } } }),
    );
    items.push(...answer.items);
    if (items.length >= answer.total || answer.items.length === 0) {
      break;
    }
  }
  return items;
}

/** What the installation accepts and produces: the list of outputs the *always produce* choices come from. */
export function useSurveyUploadCapabilities() {
  return useQuery({
    queryKey: queryKeys.surveyUploadCapabilities,
    queryFn: () => unwrap(api.GET('/api/v1/survey-uploads/capabilities')),
    staleTime: 5 * 60_000,
  });
}

/** The account's survey-upload choices: outputs always produced, and whether its uploads get a public link. */
export function useSurveyUploadPreferences() {
  return useQuery({
    queryKey: queryKeys.surveyUploadPreferences,
    queryFn: () => unwrap(api.GET('/api/v1/me/survey-upload-preferences')),
  });
}

export function useSaveSurveyUploadPreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SurveyUploadPreferences) =>
      unwrap(api.PUT('/api/v1/me/survey-upload-preferences', { body })),
    onSuccess: (saved) => queryClient.setQueryData(queryKeys.surveyUploadPreferences, saved),
  });
}

/**
 * The cave's surveys, grouped from its uploads, asking again while any of them is unfinished.
 *
 * The listing only carries uploads of caves whose position the caller may see exactly, so a
 * protected cave answers with nothing for a caller without that access — an empty list, not an
 * error. The page decides what an empty list says.
 */
export function useCaveSurveys(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveSurveys(caveId ?? ''),
    queryFn: () => fetchAllUploadsOfCave(caveId!),
    enabled: !!caveId,
    select: groupCaveSurveys,
    refetchInterval: (query) => surveyUploadListPollInterval(query.state.data),
  });
}

/**
 * One upload with its files, artefacts and — for someone who may write the cave — the log tail.
 *
 * Asked again every couple of seconds while the run is unfinished and never afterwards. File and
 * artefact links in the answer are short-lived signed addresses, so a page left open on a finished
 * upload should ask again (a reload does) rather than keep using links it was given earlier.
 */
export function useSurveyUpload(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.surveyUpload(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/survey-uploads/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
    refetchInterval: (query) => surveyUploadPollInterval(query.state.data?.status),
  });
}

/**
 * Runs the pipeline again over the files already archived, with the same options or new ones.
 * The answer is the upload as it now stands (queued), so it replaces the cached status at once and
 * the page starts polling without waiting for the next request.
 */
export function useRerunSurveyUpload() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      pipeline,
      display,
      fromStep,
    }: {
      id: string;
      pipeline?: SurveyUploadPipelineRequest | null;
      /** A point cloud's choice of how much to show; it makes the run's recipe anew. */
      display?: SurveyUploadDisplayRequest | null;
      /** A point cloud's step to do again with the recipe it has, by the step's id. */
      fromStep?: string | null;
    }) =>
      unwrap(
        api.POST('/api/v1/survey-uploads/{id}/rerun', {
          params: { path: { id } },
          body: { pipeline: pipeline ?? null, display: display ?? null, fromStep: fromStep ?? null },
        }),
      ),
    onSuccess: (upload) => {
      queryClient.setQueryData(queryKeys.surveyUpload(upload.id), upload);
      void queryClient.invalidateQueries({ queryKey: queryKeys.caveSurveys(upload.cave.id) });
    },
  });
}

/**
 * Brings the cave's own lists up to date when an upload being watched stops running.
 *
 * What a finished run adds — models, sources, documents — shows on the cave page under lists that
 * have their own cache entries and would otherwise be stale until their own timers lapsed. Keyed
 * on the move from unfinished to finished rather than on the state, so a page opened on an old,
 * finished upload asks for nothing.
 */
export function useRefreshCaveWhenUploadSettles(upload: Pick<SurveyUpload, 'status' | 'cave'> | undefined) {
  const queryClient = useQueryClient();
  const wasUnsettled = useRef(false);
  const unsettled = upload !== undefined && surveyUploadUnsettled(upload.status);
  const caveId = upload?.cave.id;

  useEffect(() => {
    if (wasUnsettled.current && !unsettled && caveId) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.surveyModels(caveId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.surveySources(caveId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.caveSurveys(caveId) });
    }
    wasUnsettled.current = unsettled;
  }, [unsettled, caveId, queryClient]);
}

/** Where a point-cloud upload has got to, for the dialog sending it. */
export type PointCloudUploadPhase = 'opening' | 'sending' | 'completing';

export interface PointCloudUploadRequest {
  caveId: string;
  file: File;
  /** What the survey is called on the cave; the file's name without its extension when nothing better is known. */
  surveyTitle: string;
  /** How much to show, decided before the estimate; left out, the run decides or stops to ask. */
  display?: PointCloudDisplayChoice;
  onPhase?: (phase: PointCloudUploadPhase) => void;
}

/**
 * Sends a point cloud the way a device sends a survey: the upload is opened with the one file
 * declared as its archive, the bytes go in one request, and the completion queues the run. The
 * answer is the upload as queued, whose status page the dialog then opens.
 *
 * One request for the bytes rather than a resumable session, because the survey-upload protocol
 * has no pieces: a declared file arrives whole or is declared again. The browser's own upload
 * progress is not readable through the fetch API, so the dialog is told which of the three steps
 * is under way and nothing finer.
 */
export function useUploadPointCloud() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ caveId, file, surveyTitle, display, onPhase }: PointCloudUploadRequest) => {
      onPhase?.('opening');
      const opened = await unwrap(
        api.POST('/api/v1/survey-uploads', {
          body: {
            source: 'pointcloud',
            client: { name: 'SilexGIS web', version: null },
            cave: { id: caveId },
            caveSurvey: null,
            newCave: null,
            surveyTitle,
            surveyName: null,
            description: null,
            files: [{ name: file.name, size: file.size, role: 'archive' }],
            pipeline: null,
            documentsVisibility: null,
            display: display ? displayRequestOf(display) : null,
          },
        }),
      );
      onPhase?.('sending');
      await unwrap(
        api.PUT('/api/v1/survey-uploads/{id}/files/{name}', {
          params: { path: { id: opened.id, name: file.name } },
          body: file as never,
          bodySerializer: (b: unknown) => b as Blob,
          headers: { 'Content-Type': 'application/octet-stream' },
        }),
      );
      onPhase?.('completing');
      return unwrap(
        api.POST('/api/v1/survey-uploads/{id}/complete', { params: { path: { id: opened.id } } }),
      );
    },
    onSuccess: (upload) => {
      queryClient.setQueryData(queryKeys.surveyUpload(upload.id), upload);
      void queryClient.invalidateQueries({ queryKey: queryKeys.caveSurveys(upload.cave.id) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.surveyModels(upload.cave.id) });
    },
  });
}
