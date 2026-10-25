// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  keepPreviousData,
  useMutation,
  useQueries,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid, useInvalidateHistory } from './shared.ts';
import type { ImportBatchListParams, SpeologieSearchParams, Visibility } from './types.ts';
import { queryKeys } from './queryKeys.ts';
import { useTripLogWritten } from './trips.ts';
import type { TripLogInfo } from './trips.ts';

/**
 * Replaces the trip's sketch with the track in a GPX file, in one request. A trip write like
 * the update above, and finished the same way: the answer is the trip as it now stands and
 * carries the version the write produced, so the page redraws from it and the next save needs no
 * read in between.
 */
export function useImportTripTrack() {
  const written = useTripLogWritten();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, file }: { id: string; file: File }): Promise<TripLogInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      // Multipart: hand the FormData through untouched (the browser sets the boundary).
      return unwrap(api.POST('/api/v1/trip-logs/{id}/geometry/gpx', {
        params: { path: { id } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: (trip) => {
      invalidateHistory();
      written(trip);
    },
  });
}

// ---------------------------------------------------------------------------
// Staged vector import: the club's term rules, the review of a loaded file, and
// the confirmations it produced.
// ---------------------------------------------------------------------------

export type TermRuleSetInfo = components['schemas']['TermRuleSetDto'];
export type TermRuleSetDetail = components['schemas']['TermRuleSetDetailDto'];
export type TermRule = components['schemas']['TermRule'];
export type TermRuleDocument = components['schemas']['TermRuleDocument'];
export type TermRuleSetCreate = components['schemas']['TermRuleSetCreateRequest'];
export type TermRuleSetWrite = components['schemas']['TermRuleSetWriteRequest'];
export type TermRuleSetScopeWrite = components['schemas']['TermRuleSetScopeRequest'];
export type TermRuleSetImport = components['schemas']['TermRuleSetImportRequest'];
export type TermRuleScope = components['schemas']['TermRuleScope'];
export type TermMatchMode = components['schemas']['TermMatchMode'];
export type TermStripMode = components['schemas']['TermStripMode'];
export type ImportTargetKind = components['schemas']['ImportTargetKind'];
export type ImportOptions = components['schemas']['ImportOptions'];
export type ImportDecision = components['schemas']['ImportDecision'];
export type ImportCandidate = components['schemas']['ImportCandidateDto'];
export type ImportPreview = components['schemas']['ImportPreviewDto'];
export type ImportPreviewRequest = components['schemas']['ImportPreviewRequest'];
export type ImportSession = components['schemas']['ImportSessionDto'];
export type ImportCommitResult = components['schemas']['ImportCommitResultDto'];
export type ImportBatch = components['schemas']['ImportBatchDto'];
export type ImportBatchItem = components['schemas']['ImportBatchItemDto'];
export type ImportBatchUndoItem = components['schemas']['ImportBatchUndoItemDto'];
export type ImportBatchRevertRequest = components['schemas']['ImportBatchRevertRequest'];
export type ImportFailure = components['schemas']['ImportFailureDto'];
export type ImportBatchDetail = components['schemas']['ImportBatchDetailDto'];
export type ImportProvenance = components['schemas']['ImportProvenanceDto'];
export type GeofileSourceOptions = components['schemas']['GeofileSourceOptions'];

export function useTermRuleSets() {
  return useQuery({
    queryKey: queryKeys.termRuleSets,
    queryFn: () => unwrap(api.GET('/api/v1/term-rule-sets')),
  });
}

/** The set an import starts from when the reviewer names none. */
export function useEffectiveTermRuleSet() {
  return useQuery({
    queryKey: queryKeys.effectiveTermRuleSet,
    queryFn: () => unwrap(api.GET('/api/v1/term-rule-sets/effective')),
  });
}

export function useTermRuleSet(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.termRuleSet(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/term-rule-sets/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
  });
}

/**
 * The whole prefix, not one row: which set applies to somebody is derived from all of them,
 * so promoting one changes what another account's next import starts from.
 */
function useInvalidateTermRuleSets() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['term-rule-sets'] });
}

export function useCreateTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: (body: TermRuleSetCreate) => unwrap(api.POST('/api/v1/term-rule-sets', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TermRuleSetWrite }) =>
      unwrap(api.PUT('/api/v1/term-rule-sets/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/term-rule-sets/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

export function useSetTermRuleSetScope() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TermRuleSetScopeWrite }) =>
      unwrap(api.POST('/api/v1/term-rule-sets/{id}/scope', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useImportTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: (body: TermRuleSetImport) =>
      unwrap(api.POST('/api/v1/term-rule-sets/import', { body })),
    onSuccess: invalidate,
  });
}

export function useImportSession(geofileId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.importSession(geofileId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/geofiles/{geofileId}/import/session', {
          params: { path: { geofileId: geofileId! } },
        }),
      ),
    enabled: Boolean(geofileId),
  });
}

/**
 * Saves the review as the reviewer works. Deliberately does not invalidate the session
 * query: the browser already holds what it just sent, and refetching would make every tick
 * of the table fight the answer coming back.
 */
export function useSaveImportSession() {
  return useMutation({
    mutationFn: ({
      geofileId,
      body,
    }: {
      geofileId: string;
      body: { options: ImportOptions; decisions: Record<string, ImportDecision> };
    }) =>
      unwrap(
        api.PUT('/api/v1/geofiles/{geofileId}/import/session', {
          params: { path: { geofileId } },
          body,
        }),
      ),
  });
}

/** The dry run. A POST because the options are a body, but it creates nothing. */
export function useImportPreview(
  geofileId: string | undefined,
  body: ImportPreviewRequest,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.importPreview(geofileId ?? '', body),
    queryFn: () =>
      unwrap(
        api.POST('/api/v1/geofiles/{geofileId}/import/preview', {
          params: { path: { geofileId: geofileId! } },
          body,
        }),
      ),
    enabled: Boolean(geofileId) && enabled,
    placeholderData: keepPreviousData,
  });
}

export function useCommitImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      geofileId,
      body,
    }: {
      geofileId: string;
      body: {
        options: ImportOptions;
        selection: number[];
        decisions: Record<string, ImportDecision>;
        withoutReview: boolean;
      };
    }) =>
      unwrap(
        api.POST('/api/v1/geofiles/{geofileId}/import/commit', {
          params: { path: { geofileId } },
          body,
        }),
      ),
    onSuccess: () => {
      // Only the review is spent here. The caves, entrances and features do not exist yet —
      // the confirmation was queued — so invalidating the registry now would refetch it early
      // and show the reader an unchanged map as though nothing had been created.
      // `useProcessingJob` invalidates the rest when the job finishes.
      void queryClient.invalidateQueries({ queryKey: ['import-session'] });
    },
  });
}

export type ProcessingJob = components['schemas']['ProcessingJobDto'];

/** Whether a job is still going, which is the only thing worth asking again about. */
export function jobUnsettled(status: ProcessingJob['status'] | undefined): boolean {
  return status === 'queued' || status === 'running';
}

/**
 * One job, polled while it is unfinished.
 *
 * A caller may always read a job they asked for, so this needs no right of its own — the server
 * answers a job belonging to somebody else exactly as it answers one that never existed.
 */
export function useProcessingJob(jobId: number | undefined) {
  const queryClient = useQueryClient();
  return useQuery({
    queryKey: queryKeys.processingJob(jobId ?? 0),
    queryFn: async () => {
      const job = await unwrap(
        api.GET('/api/v1/jobs/{id}', { params: { path: { id: jobId! } } }),
      );
      if (!jobUnsettled(job.status)) {
        // The moment the work is actually done — not when it was asked for. Everything a
        // confirmation creates lands at once, so the surfaces that show it go stale together.
        void queryClient.invalidateQueries({ queryKey: ['features'] });
        void queryClient.invalidateQueries({ queryKey: ['caves'] });
        void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      }
      return job;
    },
    enabled: jobId !== undefined,
    // Stopped by the answer rather than by a timer: a settled job is asked about no more.
    refetchInterval: (query) => (jobUnsettled(query.state.data?.status) ? 1500 : false),
    retry: false,
  });
}

// ---------------------------------------------------------------------------
// A club's trip spreadsheet into trips
// ---------------------------------------------------------------------------

export type TripImportOptions = components['schemas']['TripImportOptions'];
export type TripImportDecision = components['schemas']['TripImportDecision'];
export type TripImportRowAction = components['schemas']['TripImportRowAction'];
export type TripImportSession = components['schemas']['TripImportSessionDto'];
export type TripImportColumns = components['schemas']['TripImportColumnsDto'];
export type TripImportPreview = components['schemas']['TripImportPreviewDto'];
export type TripImportPreviewRequest = components['schemas']['TripImportPreviewRequest'];
export type TripImportRow = components['schemas']['TripImportRowDto'];
export type TripImportProblem = components['schemas']['TripImportProblemDto'];
export type TripImportProposals = components['schemas']['TripImportProposalsDto'];
export type TripImportPersonMatch = components['schemas']['TripImportPersonMatch'];
export type TripImportFeatureMatch = components['schemas']['TripImportFeatureMatch'];
export type TripImportTermMatch = components['schemas']['TripImportTermMatch'];
export type TripImportCommitResult = components['schemas']['TripImportCommitResultDto'];
export type TripCsvField = components['schemas']['TripCsvField'];
export type TripCsvDateOrder = components['schemas']['TripCsvDateOrder'];
export type TripCsvDateOrderSource = components['schemas']['TripCsvDateOrderSource'];
export type TripCsvEncoding = components['schemas']['TripCsvEncoding'];
export type TripCsvEncodingSource = components['schemas']['TripCsvEncodingSource'];
export type TripCsvDiagnosticCode = components['schemas']['TripCsvDiagnosticCode'];

/**
 * The saved review of one uploaded sheet. Answers with defaults rather than a 404 when
 * nobody has reviewed this file yet, so the screen has something to open with.
 */
export function useTripImportSession(fileId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.tripImportSession(fileId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-imports/{fileId}/session', { params: { path: { fileId: fileId! } } }),
      ),
    enabled: Boolean(fileId),
  });
}

/**
 * Saves the review as the reviewer works. Deliberately does not invalidate the session
 * query: the browser already holds what it just sent, and refetching would make every tick
 * of the table fight the answer coming back.
 */
export function useSaveTripImportSession() {
  return useMutation({
    mutationFn: ({
      fileId,
      body,
    }: {
      fileId: string;
      body: { options: TripImportOptions; decisions: Record<string, TripImportDecision> };
    }) =>
      unwrap(
        api.PUT('/api/v1/trip-imports/{fileId}/session', { params: { path: { fileId } }, body }),
      ),
  });
}

/**
 * The sheet's own header line, for the mapping controls. Read from the stored file rather
 * than from a parse, so it still answers when the parse itself failed — which is exactly
 * when somebody needs to re-point a column.
 */
export function useTripImportColumns(fileId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.tripImportColumns(fileId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-imports/{fileId}/columns', { params: { path: { fileId: fileId! } } }),
      ),
    enabled: Boolean(fileId),
    // A refusal here is the server's settled answer about this file, and asking again three
    // times only holds the screen in its loading state through the whole backoff.
    retry: false,
  });
}

/** The dry run. A POST because the options are a body, but it creates nothing. */
export function useTripImportPreview(
  fileId: string | undefined,
  body: TripImportPreviewRequest,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.tripImportPreview(fileId ?? '', body),
    queryFn: () =>
      unwrap(
        api.POST('/api/v1/trip-imports/{fileId}/preview', {
          params: { path: { fileId: fileId! } },
          body,
        }),
      ),
    enabled: Boolean(fileId) && enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * Confirms the review. The options travel with it rather than being read back from the saved
 * row: a second tab left open on different choices must not decide what a thousand trips
 * become.
 */
export function useCommitTripImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      fileId,
      body,
    }: {
      fileId: string;
      body: {
        options: TripImportOptions;
        lines: number[];
        decisions: Record<string, TripImportDecision>;
      };
    }) =>
      unwrap(
        api.POST('/api/v1/trip-imports/{fileId}/commit', { params: { path: { fileId } }, body }),
      ),
    onSuccess: () => {
      // A confirmation writes trips, may add area and cave features, records a batch that can
      // be undone, and spends the review that produced it — five surfaces go stale at once.
      void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['trip-import-session'] });
    },
  });
}

// ---------------------------------------------------------------------------
// A phone's trip recording into a tracked trip
// ---------------------------------------------------------------------------

export type SpeleolocImportOptions = components['schemas']['SpeleolocImportOptions'];
export type SpeleolocPointDecision = components['schemas']['SpeleolocPointDecision'];
export type SpeleolocPointAction = components['schemas']['SpeleolocPointAction'];
export type SpeleolocPointState = components['schemas']['SpeleolocPointState'];
export type SpeleolocRecording = components['schemas']['SpeleolocRecordingDto'];
export type SpeleolocRecordings = components['schemas']['SpeleolocRecordingsDto'];
export type SpeleolocImportSession = components['schemas']['SpeleolocImportSessionDto'];
export type SpeleolocPoint = components['schemas']['SpeleolocPointDto'];
export type SpeleolocStationCandidate = components['schemas']['SpeleolocStationCandidate'];
export type SpeleolocImportPreview = components['schemas']['SpeleolocImportPreviewDto'];
export type SpeleolocImportPreviewRequest = components['schemas']['SpeleolocImportPreviewRequest'];
export type SpeleolocImportCommitResult = components['schemas']['SpeleolocImportCommitResultDto'];
export type SpeleolocImportFailure = components['schemas']['SpeleolocImportFailureDto'];

/**
 * The recordings one uploaded archive holds, newest first.
 *
 * Not retried: every refusal this answers with — the upload is not there, the caller may not
 * record trips, the file is not an archive — is the server's settled answer about this file, and
 * asking twice more only holds the screen in its loading state through the whole backoff.
 */
export function useSpeleolocRecordings(fileId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.speleolocRecordings(fileId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/speleoloc-imports/{fileId}/recordings', {
          params: { path: { fileId: fileId! } },
        }),
      ),
    enabled: Boolean(fileId),
    retry: false,
  });
}

/**
 * The caller's review of one archive, resumed where they left it. Answers with defaults rather
 * than a 404 when nobody has reviewed this archive yet, so the screen has something to open with.
 */
export function useSpeleolocImportSession(fileId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.speleolocImportSession(fileId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/speleoloc-imports/{fileId}/session', {
          params: { path: { fileId: fileId! } },
        }),
      ),
    enabled: Boolean(fileId),
    retry: false,
  });
}

/**
 * Saves the review as the reviewer works. Deliberately does not invalidate the session query, for
 * the reason the spreadsheet review does not: the browser already holds what it just sent, and
 * refetching would make every switch on the table fight the answer coming back.
 */
export function useSaveSpeleolocImportSession() {
  return useMutation({
    mutationFn: ({
      fileId,
      body,
    }: {
      fileId: string;
      body: components['schemas']['SpeleolocImportSessionWriteRequest'];
    }) =>
      unwrap(
        api.PUT('/api/v1/speleoloc-imports/{fileId}/session', {
          params: { path: { fileId } },
          body,
        }),
      ),
  });
}

/** The dry run. A POST because the choices are a body, but it writes nothing. */
export function useSpeleolocImportPreview(
  fileId: string | undefined,
  body: SpeleolocImportPreviewRequest,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.speleolocImportPreview(fileId ?? '', body),
    queryFn: () =>
      unwrap(
        api.POST('/api/v1/speleoloc-imports/{fileId}/preview', {
          params: { path: { fileId: fileId! } },
          body,
        }),
      ),
    enabled: Boolean(fileId) && enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * Confirms the review. The choices travel with it rather than being read back from the saved row,
 * for the reason the spreadsheet confirmation carries its own: a second tab left open on a
 * different survey model must not decide where a trip's whole history was.
 */
export function useCommitSpeleolocImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      fileId,
      body,
    }: {
      fileId: string;
      body: components['schemas']['SpeleolocImportCommitRequest'];
    }) =>
      unwrap(
        api.POST('/api/v1/speleoloc-imports/{fileId}/commit', {
          params: { path: { fileId } },
          body,
        }),
      ),
    onSuccess: () => {
      // A confirmation writes positions onto a trip, may create the trip itself, records a batch
      // that can be taken back, and spends the review that produced it. The trip key covers the
      // tracking reads too, which are held under it.
      void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['speleoloc-import-session'] });
    },
  });
}

// ---------------------------------------------------------------------------
// Photographs into the registry
// ---------------------------------------------------------------------------

export type PhotoImportOptions = components['schemas']['PhotoImportOptions'];
export type PhotoDecision = components['schemas']['PhotoDecision'];
export type PhotoCandidate = components['schemas']['PhotoCandidateDto'];
export type PhotoCandidateMember = components['schemas']['PhotoCandidateMemberDto'];
export type PhotoNearby = components['schemas']['PhotoNearbyDto'];
export type PhotoPreview = components['schemas']['PhotoPreviewDto'];
export type PhotoPreviewRequest = components['schemas']['PhotoPreviewRequest'];
export type PhotoSession = components['schemas']['PhotoSessionDto'];
export type PhotoTrackOption = components['schemas']['PhotoTrackOptionDto'];
export type PhotoPosition = components['schemas']['PhotoPositionDto'];
export type PhotoPositionSource = components['schemas']['PhotoPositionSource'];
export type PhotoExif = components['schemas']['PhotoExif'];
export type PositionConfidenceBand = components['schemas']['PositionConfidenceBand'];

export function usePhotoImportSession() {
  return useQuery({
    queryKey: queryKeys.photoImportSession,
    queryFn: () => unwrap(api.GET('/api/v1/photo-import/session')),
  });
}

/**
 * Saves the review as the reviewer works. Deliberately does not invalidate the session query,
 * for the same reason the vector one does not: the browser already holds what it just sent.
 */
export function useSavePhotoImportSession() {
  return useMutation({
    mutationFn: (body: {
      fileIds: string[];
      options: PhotoImportOptions;
      decisions: Record<string, PhotoDecision>;
    }) => unwrap(api.PUT('/api/v1/photo-import/session', { body })),
  });
}

/** Groups the drop into places. A POST because the options are a body, but it creates nothing. */
export function usePhotoImportPreview(body: PhotoPreviewRequest, enabled = true) {
  return useQuery({
    queryKey: queryKeys.photoImportPreview(body),
    queryFn: () => unwrap(api.POST('/api/v1/photo-import/preview', { body })),
    enabled: enabled && body.fileIds.length > 0,
    placeholderData: keepPreviousData,
  });
}

export function useCommitPhotoImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      options: PhotoImportOptions;
      fileIds: string[];
      selection: string[];
      decisions: Record<string, PhotoDecision>;
    }) => unwrap(api.POST('/api/v1/photo-import/commit', { body })),
    onSuccess: () => {
      // A confirmation creates objects, hangs pictures on them and spends the review that
      // produced them, so every surface built from any of those goes stale at once.
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['attachments'] });
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['photo-import-session'] });
    },
  });
}

/** Uploaded tracks a picture with no fix of its own can be placed against. */
export function usePhotoImportTracks(enabled = true) {
  return useQuery({
    queryKey: queryKeys.photoImportTracks,
    queryFn: () => unwrap(api.GET('/api/v1/photo-import/tracks')),
    enabled,
  });
}

/**
 * Gives a picture a position by hand, or forgets one. `replaceRecordedFix` has to be true when
 * the camera recorded a fix — the server refuses otherwise, so that a drag cannot quietly
 * replace a measurement.
 */
export function useSetPhotoPosition() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      position,
      replaceRecordedFix,
    }: {
      id: string;
      position: number[] | null;
      replaceRecordedFix: boolean;
    }) =>
      unwrap(
        api.PUT('/api/v1/files/{id}/position', {
          params: { path: { id } },
          body: { position, replaceRecordedFix },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['attachments'] });
      void queryClient.invalidateQueries({ queryKey: ['photo-import-preview'] });
    },
  });
}

/**
 * Moves an object to where one of its pictures was taken. An ordinary geometry write: it needs
 * write access to the object and lands in that object's own history.
 */
export function useFeaturePositionFromPhoto() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ featureId, fileId }: { featureId: string; fileId: string }) =>
      unwrap(
        api.POST('/api/v1/features/{id}/position-from-photo', {
          params: { path: { id: featureId } },
          body: { fileId },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['history'] });
    },
  });
}

export function useImportBatches(params: ImportBatchListParams) {
  return useQuery({
    queryKey: queryKeys.importBatches(params),
    queryFn: () => unwrap(api.GET('/api/v1/import-batches', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useImportBatch(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.importBatch(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/import-batches/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
  });
}

/**
 * Takes an import back. `itemIds` takes back only those lines (a phone upload only) and `force`
 * puts back fields that were changed after the upload. The answer says, line by line, what was
 * put back and what was left; it is empty for an import that is undone as one unit.
 */
export function useRevertImportBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, itemIds, force }: { id: string } & Partial<ImportBatchRevertRequest>) =>
      unwrap(
        api.POST('/api/v1/import-batches/{id}/revert', {
          params: { path: { id } },
          body: itemIds || force ? { itemIds: itemIds ?? null, force: force ?? null } : undefined,
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      // An undo deletes the trips a batch created, so the list they leave and the list of
      // deleted trips they arrive on are both behind.
      void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
      // A phone upload's undo writes history of its own.
      void queryClient.invalidateQueries({ queryKey: ['history'] });
    },
  });
}

/**
 * Where an object came from. Absent for anything nobody imported, which is most of them —
 * hence no retry: a 404 here is the normal answer, not a failure worth asking again about.
 */
export function useImportProvenance(featureId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.importProvenance(featureId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{featureId}/import-provenance', {
          params: { path: { featureId: featureId! } },
        }),
      ),
    enabled: Boolean(featureId) && enabled,
    retry: false,
  });
}

// ---------------------------------------------------------------------------
// The Romanian community cave catalogue (speologie.org)
// ---------------------------------------------------------------------------

export type SpeologieStatus = components['schemas']['SpeologieStatusDto'];
export type SpeologieCave = components['schemas']['SpeologieCaveDto'];
export type SpeologieSearchResult = components['schemas']['SpeologieSearchDto'];
export type SpeologieDecision = components['schemas']['SpeologieDecisionDto'];
export type SpeologieAction = components['schemas']['SpeologieAction'];
export type SpeologieImportResult = components['schemas']['SpeologieImportResultDto'];
export type SpeologieBasin = components['schemas']['SpeologieBasinDto'];

/**
 * The catalogue's hydrographic basin tree, which its own programmatic interface does not publish —
 * this installation carries a copy so a cave's basin number can be read as a place.
 *
 * Held for the session: it is a table shipped with the application rather than an answer about
 * anything, so re-fetching it on every visit to the screen would be asking the server to repeat
 * itself six hundred times over.
 */
export function useSpeologieBasins() {
  return useQuery({
    queryKey: queryKeys.speologieBasins,
    queryFn: () => unwrap(api.GET('/api/v1/catalogue/speologie/basins')),
    staleTime: Infinity,
    gcTime: Infinity,
  });
}

/**
 * Whether this installation has been given a key for the catalogue at all. Reaches nothing, so
 * it is safe to ask on every visit; the screens use it to say "your administrator has not set
 * this up" rather than to fail on the first search.
 */
export function useSpeologieStatus() {
  return useQuery({
    queryKey: queryKeys.speologieStatus,
    queryFn: () => unwrap(api.GET('/api/v1/catalogue/speologie/status')),
    staleTime: 300_000,
  });
}

/**
 * Searches the catalogue. Deliberately not fired until there is something to search for: the
 * server refuses a search that names neither a term nor a county, and asking anyway would cost
 * a round trip to be told so.
 */
export function useSpeologieSearch(params: SpeologieSearchParams, enabled = true) {
  const hasQuestion = Boolean(params.q?.trim()) || Boolean(params.county?.trim());
  return useQuery({
    queryKey: queryKeys.speologieSearch(params),
    queryFn: () => unwrap(api.GET('/api/v1/catalogue/speologie/caves', { params: { query: params } })),
    enabled: enabled && hasQuestion,
    placeholderData: keepPreviousData,
    // The far end is a volunteer-run service the server already throttles itself against.
    // Repeating a search it has just answered helps nobody.
    staleTime: 300_000,
    retry: false,
  });
}

/**
 * One catalogue cave in full, including the description already converted to the plain text an
 * import would store — the same conversion, so what is previewed is what would be kept.
 */
export function useSpeologieCave(id: number | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.speologieCave(id ?? 0),
    queryFn: () =>
      unwrap(api.GET('/api/v1/catalogue/speologie/caves/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id) && enabled,
    staleTime: 300_000,
    retry: false,
  });
}

/**
 * Several catalogue caves in full, one query each.
 *
 * One query per cave rather than one for the lot, because each answer carries a description and a
 * description in this catalogue can run to a megabyte — a batched answer would be one enormous
 * payload that is either wholly there or wholly missing. Separate queries also mean a cave the
 * catalogue has withdrawn fails on its own instead of taking the other nine with it. The server
 * throttles itself against the far end, so these queue rather than arriving together.
 */
export function useSpeologieCaves(ids: readonly number[], enabled = true) {
  return useQueries({
    queries: ids.map((id) => ({
      queryKey: queryKeys.speologieCave(id),
      queryFn: () =>
        unwrap(api.GET('/api/v1/catalogue/speologie/caves/{id}', { params: { path: { id } } })),
      enabled,
      staleTime: 300_000,
      retry: false,
    })),
  });
}

export function useImportFromSpeologie() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      selection: number[];
      decisions: Record<string, SpeologieDecision>;
      visibility: Visibility;
      cavingGroupId: string | null;
      locationProtected: boolean;
      parentId: string | null;
    }) => unwrap(api.POST('/api/v1/catalogue/speologie/import', { body })),
    onSuccess: () => {
      // Caves and features appear, a batch appears in the undo history, and every catalogue
      // search now has a different answer to "is this one already here".
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['speologie'] });
    },
  });
}

/**
 * The directories this installation may import from.
 *
 * Empty means the operator has not switched the feature on — which the page states rather than
 * offering a field that refuses everything.
 */
export function useImportRoots(enabled = true) {
  return useQuery({
    queryKey: queryKeys.importRoots,
    queryFn: () => unwrap(api.GET('/api/v1/upload-batches/import-roots')),
    enabled,
    retry: false,
    staleTime: 60 * 60_000,
  });
}

/** Starts a background import from a directory the server itself can reach. */
export function useImportDirectory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: {
      path: string;
      label?: string | null;
      cabinetId?: string | null;
      tagName?: string | null;
    }) =>
      unwrap(api.POST('/api/v1/upload-batches/import-directory', {
        body: {
          path: request.path,
          label: request.label ?? null,
          cabinetId: request.cabinetId ?? null,
          tagName: request.tagName ?? null,
        },
      })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['upload-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
    },
  });
}
