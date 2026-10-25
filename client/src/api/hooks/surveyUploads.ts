// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap } from './shared.ts';
import { queryKeys } from './queryKeys.ts';

export type SurveyUpload = components['schemas']['SurveyUploadDto'];
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

/** How often an unfinished upload is asked about, in milliseconds. */
export const SURVEY_UPLOAD_POLL_MS = 2000;

/**
 * The stages a run goes through, in the order it goes through them. The server reports only the
 * stage it is in; every other stage's state is worked out from this order by
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
 */
export function surveyUploadStageStates(input: StageInputs): StageProgress[] {
  const index = input.stage === null ? -1 : SURVEY_UPLOAD_STAGE_ORDER.indexOf(input.stage);
  const notCompiling = input.compile === false;

  return SURVEY_UPLOAD_STAGE_ORDER.map((stage, i) => {
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
    mutationFn: ({ id, pipeline }: { id: string; pipeline?: SurveyUploadPipelineRequest | null }) =>
      unwrap(
        api.POST('/api/v1/survey-uploads/{id}/rerun', {
          params: { path: { id } },
          body: { pipeline: pipeline ?? null },
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
