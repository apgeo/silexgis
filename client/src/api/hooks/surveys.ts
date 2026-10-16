// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components, paths } from '../schema';
import { unwrap } from './shared.ts';
import { queryKeys } from './queryKeys.ts';
import type { MapFilterParams } from './map.ts';

export type SurveyModelInfo = components['schemas']['SurveyModelDto'];

/** The signed model URLs live 10 minutes; refresh before they lapse mid-view. */
const SURVEY_MODEL_URL_REFRESH_MS = 8 * 60_000;
/** How often a model whose processing has not finished yet is asked about. */
const SURVEY_MODEL_CONVERSION_POLL_MS = 2000;

/** How often a queued log reading is re-asked about, until it is no longer queued. */
const SURVEY_COMPILATION_POLL_MS = 2000;

/**
 * A model with work still outstanding on it. Both kinds of upload have some: a wall mesh is
 * converted into what the 3D scene draws, and a line plot is read into its stations and shots.
 * The line plot stays viewable throughout — the viewer reads the file as uploaded — so what is
 * outstanding there is the record behind it, not the picture.
 *
 * A failure counts as settled. The worker may retry it and move the row on again, but
 * the reader has been told the outcome and is owed nothing further until they act on it; asking
 * every two seconds forever on the chance somebody re-queues the job is a page that never goes
 * quiet.
 */
export function surveyModelUnsettled(status: SurveyModelInfo['status']): boolean {
  return status === 'pending' || status === 'processing';
}

/**
 * A model the server still has work queued or running for, whether or not it has anything to show
 * meanwhile.
 *
 * Wider than {@link surveyModelUnsettled} by one case, and the difference is the point. A model
 * that has been read and is being read again stays ready: its stations, its figures and its
 * drawing are all still there until the new reading replaces them, so everything that asks "can
 * this survey be used" must go on being told yes. What that model shares with an unsettled one is
 * only that its row will change by itself — so a list watching for that, and an action that would
 * queue a second reading behind the first, ask this question and nothing else does.
 */
export function surveyModelWorkOutstanding(model: {
  status: SurveyModelInfo['status'];
  readingAgain?: boolean;
}): boolean {
  return surveyModelUnsettled(model.status) || model.readingAgain === true;
}

/**
 * Whether the embedded survey viewer can read this model itself.
 *
 * The viewer parses the line-plot formats natively, choosing its parser by the extension of the
 * file name it is handed. A wall mesh is not one of them: it is drawn by the 3D scene, out of the
 * file the server converts it into, and handing the raw upload to the viewer produces a parse
 * failure rather than a picture.
 *
 * One home, because more than one page decides this — the cave page's model list, and the
 * popped-out survey panel, which is a separate window nobody testing the cave page would ever see.
 * Two copies diverge the first time a format is added on one side only: either a viewer is offered
 * a file it cannot parse, or a readable file is quietly skipped.
 */
export function surveyModelReadableByViewer(model: { format: SurveyModelInfo['format'] }): boolean {
  return model.format === 'lox' || model.format === 'survex3d';
}

/**
 * Whether this survey has stations behind it, and so whether anybody can be put on a place in it.
 *
 * <b>Stations are the whole of it.</b> A tracked position is the name of a station, resolved
 * against the rows the server stored when it read the file — so a survey with no stored stations
 * is one on which no report can ever claim a place. Exactly one thing writes those rows, and it
 * runs only for a line plot that was read right through: a wall mesh is turned into a picture and
 * never yields a station, and a reading that was queued, is running, or failed has stored none.
 * A failed reading is the case worth naming, because it is the one that looks finished: the survey
 * is listed, it has a name and a date, and it holds nothing.
 *
 * <b>One home, because three surfaces ask it</b> — which surveys a watch may be pointed at, whether
 * the watch already on screen can place anybody, and whether the model panel has something to draw.
 * Answered differently in any of them and the application offers a survey it will then refuse to
 * place anybody on, which is the failure this exists to prevent.
 *
 * <b>What it cannot see.</b> The answer is inferred from the status and the format, because that is
 * what the survey list carries — a reading that finished but found nothing still says "ready", and
 * would pass here. Closing that gap means the server stating the station count on the survey it
 * describes; until it does, this is the honest answer rather than a guaranteed one.
 */
export function surveyModelCanPlaceACaver(model: {
  status: SurveyModelInfo['status'];
  format: SurveyModelInfo['format'];
}): boolean {
  return surveyModelPlacingObstacle(model) === null;
}

/**
 * The three reasons a survey holds no stations, told apart — because the answer to each is a
 * different act.
 *
 * <b>"No stations" is one fact with three causes, and a surface that names only the loudest one
 * lies about the other two.</b> A file of cave walls converted perfectly and will never hold a
 * station however many times it is imported; a reading still queued or running holds none *yet*
 * and holds some the moment it finishes; a reading that failed holds none and never will unless
 * the file is put through again. Told "its import did not come through", the owner of the first
 * re-imports a mesh forever and the owner of the second acts on a job that was about to succeed.
 * Waiting fixes exactly one of the three, and re-importing fixes exactly one of the three.
 *
 * <b>Format before status, because format is the permanent half.</b> A wall mesh whose conversion
 * failed is still a wall mesh: re-reading it cannot produce a station, so the advice that fits it
 * is "choose a line plot", not "import it again".
 *
 * The question above is answered out of this one rather than beside it, so the list a chooser
 * narrows and the sentence a card prints can never disagree about a single model.
 */
export type SurveyModelPlacingObstacle = 'notALinePlot' | 'stillReading' | 'importFailed';

export function surveyModelPlacingObstacle(model: {
  status: SurveyModelInfo['status'];
  format: SurveyModelInfo['format'];
}): SurveyModelPlacingObstacle | null {
  if (!surveyModelReadableByViewer(model)) return 'notALinePlot';
  if (surveyModelUnsettled(model.status)) return 'stillReading';
  if (model.status === 'failed') return 'importFailed';
  return null;
}

/**
 * Whether any of a cave's uploaded surveys can have produced a measured passage network.
 *
 * The same question as the one above asked of a whole cave, and deliberately answered by it: a
 * measured network and a placeable station are both the stations-and-shots the server stored when
 * it read a line plot, so two spellings of the rule would be two chances to disagree. A cave with
 * none of those is the ordinary case — most caves have never had a survey file uploaded at all —
 * and asking the server about its network anyway is a request that is certain to be refused. That
 * refusal is not free: the browser reports every failed request to its console, so a panel that
 * asked regardless would put an error on the console of every cave page in the application,
 * drowning the real ones in an expected one.
 */
export function caveHasMeasurableSurvey(
  models: { status: SurveyModelInfo['status']; format: SurveyModelInfo['format'] }[] | undefined,
): boolean {
  return (models ?? []).some(surveyModelCanPlaceACaver);
}

/**
 * How often the list re-asks. Two intervals meet in this one number, which is why it is a named
 * rule and not a literal at the query: work in flight is worth a couple of seconds, and
 * once everything has settled the list must still come back before the signed URLs on it lapse.
 *
 * Exported so the rule — a list holding nothing but finished models stops watching them — can be
 * asserted directly, rather than inferred from a live query somebody would have to watch for two
 * seconds to prove it did nothing.
 */
export function surveyModelPollInterval(
  models: { status: SurveyModelInfo['status']; readingAgain?: boolean }[] | undefined,
): number {
  return (models ?? []).some(surveyModelWorkOutstanding)
    ? SURVEY_MODEL_CONVERSION_POLL_MS
    : SURVEY_MODEL_URL_REFRESH_MS;
}

/**
 * The survey figures a cave's page works out from its line work, dropped whenever that line work
 * changes.
 *
 * These two queries are computed per request from a cave's segments, so every upload, deletion or
 * finished background extraction that changes the segments changes the answers — but they are
 * keyed under the cave rather than under the centerlines or the survey models, so none of the
 * invalidations that refresh those lists reaches them. Without this a reader who drops a file into
 * the centerline card watches that card fill in while the two panels directly beneath it go on
 * reporting the cave as it was before the upload, with nothing on screen to say the figures are
 * stale.
 */
function invalidateCaveSurveyFigures(queryClient: QueryClient, caveId: string) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.caveSurveyStatistics(caveId) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.caveOrientation(caveId) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.caveCrossSection(caveId) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.cavePattern(caveId) });
  // Not a figure, and changed by the same events all the same: each declared place is sent with
  // whether the cave's current survey holds its station. A survey being read, removed or made the
  // current one changes that answer without a declaration being touched — and the card showing it
  // tells its reader to go and check which survey is current, on the same page, so a mark that
  // outlived the change it asked for would contradict the very act it prompted.
  void queryClient.invalidateQueries({ queryKey: queryKeys.caveDepthPlaces(caveId) });
  // Stored beside the survey rather than recomputed per request, but changed by exactly the same
  // events: the figures are rewritten when a file is read, and a cave whose answering upload was
  // deleted is measured from a different one or from none at all.
  //
  // From none at all is the case that needs care. This is the one figure the server refuses
  // outright for a cave with no read line plot, and the panel showing it asks only of a cave that
  // has one — but it learns that from the list of uploads as it last rendered it. Invalidated the
  // moment the last survey is deleted, the reading is fetched again before the panel has rendered
  // the list that says there is nothing left to measure, and the refusal lands on the console of a
  // page that did nothing wrong. So when the list already says so, the reading is dropped instead
  // of asked for again; the panel's next render finds no survey and does not ask.
  const models = queryClient.getQueryData<SurveyModelInfo[]>(queryKeys.surveyModels(caveId));
  if (models !== undefined && !caveHasMeasurableSurvey(models)) {
    queryClient.removeQueries({ queryKey: queryKeys.caveTopology(caveId) });
  } else {
    void queryClient.invalidateQueries({ queryKey: queryKeys.caveTopology(caveId) });
  }
}

/**
 * One survey model, addressed by its own id rather than found in a cave's list.
 *
 * For the places that were sent to a particular model — a pane opened from the cave page, a
 * pop-out window opened on one — and which have an id and no cave. Listing the cave's models and
 * picking through them is not an alternative: the caller does not know which cave it belongs to,
 * and the guess it would otherwise make is "the first readable one", which is how the pop-out
 * viewer behaves and is exactly the behaviour a chosen model is meant to replace.
 *
 * Refetched on the same interval as the list, because the answer carries a signed URL with a ten
 * minute life and a window left open on a model outlives it.
 *
 * A model whose cave's location is withheld from this reader answers **404**, not an empty result:
 * a cave's models are its location, so their existence is withheld along with them. Callers must
 * treat the failure as "there is nothing here for you" and not as an error worth reporting.
 */
export function useSurveyModel(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.surveyModel(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/survey-models/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
    staleTime: 5 * 60_000,
    refetchInterval: SURVEY_MODEL_URL_REFRESH_MS,
    retry: false,
  });
}

/**
 * Several models asked for by id, each exactly as {@link useSurveyModel} asks for one.
 *
 * For a list of ids read off something else — the surveys a trip's reports were recorded on — where
 * some may have been deleted since and some belong to a cave this reader may not place. Both answer
 * 404, and both are simply left out of what comes back: what is returned is the models that still
 * exist and that this reader may open, which is the only list a chooser may offer. Nothing is
 * retried and nothing is reported, for the reason given on the single read.
 *
 * The same key and the same refresh as the single read, so a model already on screen is not
 * fetched a second time and its signed address is kept alive the same way.
 */
export function useSurveyModelsById(ids: readonly string[]) {
  return useQueries({
    queries: ids.map((id) => ({
      queryKey: queryKeys.surveyModel(id),
      queryFn: () => unwrap(api.GET('/api/v1/survey-models/{id}', { params: { path: { id } } })),
      staleTime: 5 * 60_000,
      refetchInterval: SURVEY_MODEL_URL_REFRESH_MS,
      retry: false,
    })),
    combine: (results) => results.flatMap((result) => (result.data ? [result.data] : [])),
  });
}

export type SurveyStation = components['schemas']['SurveyStationDto'];

/**
 * How many stations one search asks for. A chooser shows a short list under a field and asks
 * again on the next keystroke; the answer says how many there are in all, so the list can say
 * that there are more and that typing further narrows them.
 */
export const STATION_SEARCH_PAGE_SIZE = 20;

/**
 * The stations of one survey whose names begin with what somebody has typed, for a field that
 * offers them while the name is still being written.
 *
 * <b>It offers; it never decides.</b> Whether a station exists is the server's answer when the
 * report or the declaration is saved. This only saves somebody spelling a name from memory, so a
 * field fed by it still takes any text at all.
 *
 * <b>A reader who may not place the cave gets an empty list, exactly as for a survey that does not
 * exist</b> — the server answers the same page for both — so the field falls back to plain typing
 * and says nothing about why. Nothing is asked until something has been typed: a survey holds
 * tens of thousands of stations, and "all of them" is not a list anybody chooses from.
 *
 * The previous answer stays on screen while the next one is fetched, so the list does not blink
 * shut between two keystrokes — but only an answer about the same survey: names from one survey
 * offered under a field that is now about another would be wrong names.
 */
export function useSurveyModelStationSearch(surveyModelId: string | undefined, q: string) {
  const asked = q.trim();
  return useQuery({
    queryKey: queryKeys.surveyModelStationSearch(surveyModelId ?? '', asked),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/survey-models/{id}/stations', {
          params: {
            path: { id: surveyModelId! },
            query: { q: asked, pageSize: STATION_SEARCH_PAGE_SIZE },
          },
        }),
      ),
    enabled: !!surveyModelId && asked.length > 0,
    staleTime: 60_000,
    placeholderData: (previous, previousQuery) =>
      previousQuery?.queryKey[1] === surveyModelId ? previous : undefined,
  });
}

export function useSurveyModels(caveId: string | undefined) {
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: queryKeys.surveyModels(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/survey-models', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    refetchInterval: (query) => surveyModelPollInterval(query.state.data),
  });

  // Reading a line plot records the survey as the cave's own centerline, and it is a background
  // job that does it — nothing the browser did. So the only sign a page watching this list gets
  // is the moment the outstanding work stops being outstanding, and that moment is here: this is
  // the one query that keeps asking. Without this the centerline table beside it stays as it was
  // found, empty, until somebody reloads the page and wonders why the reload was needed.
  //
  // Keyed on the transition rather than the state, so a page that arrives after everything has
  // settled — the ordinary visit to a cave whose survey was read weeks ago — asks for nothing.
  const outstanding = (query.data ?? []).some(surveyModelWorkOutstanding);
  const wasOutstanding = useRef(outstanding);
  useEffect(() => {
    if (wasOutstanding.current && !outstanding && caveId) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.centerlines(caveId) });
      invalidateCaveSurveyFigures(queryClient, caveId);
    }
    wasOutstanding.current = outstanding;
  }, [outstanding, caveId, queryClient]);

  return query;
}

/**
 * The survey models of several caves at once, as one list.
 *
 * For the surfaces that belong to something naming more than one cave — a trip is the one that
 * needs it — where a chooser has to offer every model the thing could mean. Asked cave by cave
 * because that is the read the server publishes, and each answer is held under that cave's own key,
 * so a page that already listed one cave's models pays for nothing twice.
 *
 * A cave whose exact location is withheld from this reader answers with an empty list rather than
 * a refusal, and that shape is carried straight through: fewer models offered is what "there is
 * nothing here for you" looks like, and it is never an error worth reporting. Which is also why
 * an empty list on its own proves nothing about the cave: a surface that wants to say "nothing
 * was uploaded" has to ask `useCaveExactLocationAccess` first, because the withheld shape and the
 * empty one are the same list.
 *
 * A request that failed is not folded into that emptiness. It is reported as `isError`, with a
 * `refetch` to try again, because a chooser drawn empty over a failed read would say the cave has
 * no surveys when nothing about the cave is known at all.
 */
export function useSurveyModelsForCaves(caveIds: readonly string[]) {
  return useQueries({
    queries: caveIds.map((caveId) => ({
      queryKey: queryKeys.surveyModels(caveId),
      queryFn: () =>
        unwrap(api.GET('/api/v1/caves/{caveId}/survey-models', { params: { path: { caveId } } })),
      staleTime: 5 * 60_000,
      // Watched while anything on the list is still being read, exactly as the single-cave list
      // is, and for a sharper reason here. The surface this feeds narrows its chooser to the
      // surveys somebody can actually be placed on, so a reading still running is not merely
      // drawn as unfinished — it is absent. Without this the ordinary order of work, upload the
      // survey and then go and set the watch up, meets an empty chooser and keeps meeting one
      // after the job has finished, until the answer happens to go stale minutes later.
      refetchInterval: (query: { state: { data?: SurveyModelInfo[] } }) =>
        surveyModelPollInterval(query.state.data),
    })),
    combine: (results) => ({
      data: results.flatMap((result) => result.data ?? []),
      isPending: results.some((result) => result.isPending),
      isError: results.some((result) => result.isError),
      refetch: () => {
        for (const result of results) {
          if (result.isError) {
            void result.refetch();
          }
        }
      },
    }),
  });
}

/**
 * Whether any of these caves keeps its exact location from this reader.
 *
 * The one fact that tells a cave with no surveys from a cave whose surveys are withheld: the
 * survey-model list answers both with an empty list, on purpose, and the cave's own summary is
 * where the server says which it is. Read under the summary's ordinary key, so a page that
 * already holds the cave's summary pays nothing more for the answer. A cave that cannot be read at all counts as
 * withheld — nothing of it is being shown, and a notice about uploading to it would be wrong.
 */
export function useCaveExactLocationAccess(ids: readonly string[]) {
  return useQueries({
    queries: ids.map((id) => ({
      queryKey: queryKeys.caveSummary(id),
      queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/summary', { params: { path: { id } } })),
      staleTime: 300_000,
      retry: false,
    })),
    combine: (results) => ({
      withheld: results.some(
        (result) =>
          result.isError ||
          (result.data !== undefined && !result.data.permissions.canViewExactLocation),
      ),
      isPending: results.some((result) => result.isPending),
    }),
  });
}

/**
 * Imperative fetch of a cave's survey models, used by the 3D scene rather than by a component.
 *
 * The scene is not a React tree — it decides which cave's wall mesh to hold from where the
 * viewer's selection is, outside the render cycle — so it asks directly instead of mounting a
 * hook. A cave whose exact location is withheld from this caller answers with an empty list
 * rather than a refusal, which is the same answer as a cave with no models and needs no special
 * casing here.
 */
export async function fetchSurveyModels(caveId: string): Promise<SurveyModelInfo[]> {
  return unwrap(
    api.GET('/api/v1/caves/{caveId}/survey-models', { params: { path: { caveId } } }),
  );
}

function useInvalidateSurveyModels() {
  const queryClient = useQueryClient();
  return (caveId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.surveyModels(caveId) });
    invalidateCaveSurveyFigures(queryClient, caveId);
  };
}

/** The multipart body the upload endpoint declares, as the generated contract states it. */
type SurveyModelUploadBody =
  paths['/api/v1/caves/{caveId}/survey-models']['post']['requestBody']['content']['multipart/form-data'];

/**
 * What an uploader may say about where a survey file's numbers sit in the world: which coordinate
 * system they are in, and what altitude the plane the file calls zero sits at.
 *
 * Either a projected system by code, or — for a file exported about a local origin, which is the
 * ordinary case — the position that origin sits at. Never both: with a code, the file's own
 * coordinates say where it is, and a second answer could only contradict the first.
 *
 * Every half is optional here, and which of them a given file must actually answer is the uploading
 * screen's to enforce, because only it knows the format: a wall mesh carries no coordinate system
 * and one of the two line-plot formats has no field for one either, so both have to be told, while
 * the other line-plot format may state its own and then needs none of this. What is left unanswered
 * is left off the request rather than sent empty.
 *
 * Taken from the generated contract rather than restated here, so that a field renamed or re-typed
 * on the server fails this build instead of failing every upload at run time. Only the file itself
 * is set aside, because it is appended separately.
 */
export type SurveySourceDeclaration = Omit<SurveyModelUploadBody, 'file' | 'makeCurrent'>;

/** The contract's name for the request that a model take the cave's current mark once read. */
const MAKE_CURRENT_FIELD: keyof SurveyModelUploadBody = 'makeCurrent';

export function useUploadSurveyModel() {
  const invalidate = useInvalidateSurveyModels();
  return useMutation({
    mutationFn: async ({
      caveId,
      file,
      declaration,
      makeCurrent,
    }: {
      caveId: string;
      file: File;
      /** What the uploader said about where the file sits; absent when the file says it itself. */
      declaration?: SurveySourceDeclaration;
      /**
       * That the cave be represented by this model once it has been read, rather than by the
       * model of its kind that represents it now. The server applies it when the reading ends.
       */
      makeCurrent?: boolean;
    }): Promise<SurveyModelInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      // The field names are the declaration's own keys, so the names on the wire and the names in
      // the contract are one thing rather than two lists to keep in step. Plain `String(number)`
      // writes a dot decimal whatever the reader's locale is set to, which is what the server
      // parses these as; a half that was not answered is left out rather than sent empty.
      for (const [field, value] of Object.entries(declaration ?? {})) {
        if (value !== undefined) {
          form.append(field, String(value));
        }
      }
      if (makeCurrent === true) {
        form.append(MAKE_CURRENT_FIELD, 'true');
      }
      return unwrap(api.POST('/api/v1/caves/{caveId}/survey-models', {
        params: { path: { caveId } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export function useDeleteSurveyModel() {
  const queryClient = useQueryClient();
  const invalidate = useInvalidateSurveyModels();
  return useMutation({
    mutationFn: async ({ id }: { id: string; caveId: string }) => {
      const { error, response } = await api.DELETE('/api/v1/survey-models/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: (_, { id, caveId }) => {
      // Off the list at once, before anything is asked again: the upload is gone, and what the
      // cave's figures may be asked next depends on which uploads are left.
      queryClient.setQueryData<SurveyModelInfo[]>(queryKeys.surveyModels(caveId), (models) =>
        models?.filter((model) => model.id !== id),
      );
      invalidate(caveId);
    },
  });
}

/**
 * Makes a model the one its cave is represented by, among the cave's models of the same kind: a
 * line plot (.lox/.3d) or a wall mesh (.stl). The newest upload of a kind takes the mark on
 * arrival; this hands it back to an older one.
 *
 * The same invalidation as a delete, figures included: the current line plot is what the
 * measurements are read off, so choosing another one changes every number on the cave's page.
 */
export function useMakeSurveyModelCurrent() {
  const invalidate = useInvalidateSurveyModels();
  return useMutation({
    mutationFn: ({ id }: { id: string; caveId: string }): Promise<SurveyModelInfo> =>
      unwrap(api.PUT('/api/v1/survey-models/{id}/current', { params: { path: { id } } })),
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

/**
 * Asks for a model's stored file to be read again, replacing what the last reading produced.
 *
 * The list is asked for again whichever way the request ends. Accepted, the model has a reading
 * on its way — waiting if it held nothing, still ready and marked as being read again if it held
 * one — and the list's own poll takes over until the reading is done. Refused because a reading
 * is already under way, the row on screen was out of date — or the action would not have been
 * offered — and saying so without refreshing it would leave the same button inviting the same
 * refusal.
 *
 * The model's own record is asked for again as well. It is what a watch's drawing is built from,
 * on another page, and it is otherwise read only every few minutes: a model whose failed reading
 * has just been queued again would go on being described there as unreadable.
 */
export function useReadSurveyModelAgain() {
  const queryClient = useQueryClient();
  const invalidate = useInvalidateSurveyModels();
  return useMutation({
    mutationFn: ({ id }: { id: string; caveId: string }): Promise<SurveyModelInfo> =>
      unwrap(api.POST('/api/v1/survey-models/{id}/reading', { params: { path: { id } } })),
    onSettled: (_data, _error, { id, caveId }) => {
      invalidate(caveId);
      void queryClient.invalidateQueries({ queryKey: queryKeys.surveyModel(id) });
    },
  });
}

export type SurveySourceInfo = components['schemas']['SurveySourceDto'];
export type SurveySourceKind = SurveySourceInfo['kind'];

/**
 * The raw material a cave's compiled surveys were produced from — the survey languages, a project
 * configuration, a survey app's export bundle, and the log a compilation wrote.
 *
 * Kept apart from the models list because these are not models: nothing draws them, nothing reads
 * them, and what they are for is the day the compiled export can no longer be re-made from itself.
 */
export function useSurveySources(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.surveySources(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/survey-sources', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
  });
}

function useInvalidateSurveySources() {
  const queryClient = useQueryClient();
  return (caveId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.surveySources(caveId) });
    // Archiving a compilation log queues a reading of it, and removing one takes the figures read
    // from it away again, so the closure panel is stale the moment this list changes — and it is a
    // different query under a different key, which none of the archive's own invalidations reach.
    void queryClient.invalidateQueries({ queryKey: queryKeys.surveyCompilations(caveId) });
  };
}

export function useUploadSurveySource() {
  const invalidate = useInvalidateSurveySources();
  return useMutation({
    mutationFn: async ({
      caveId,
      file,
      description,
    }: {
      caveId: string;
      file: File;
      description?: string;
    }): Promise<SurveySourceInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      if (description !== undefined && description.trim() !== '') {
        form.append('description', description.trim());
      }
      return unwrap(api.POST('/api/v1/caves/{caveId}/survey-sources', {
        params: { path: { caveId } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export function useDeleteSurveySource() {
  const invalidate = useInvalidateSurveySources();
  return useMutation({
    mutationFn: async ({ id }: { id: string; caveId: string }) => {
      const { error, response } = await api.DELETE('/api/v1/survey-sources/{id}', {
        params: { path: { id } },
      });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export type SurveyCompilationInfo = components['schemas']['SurveyCompilationDto'];
export type SurveyLoopError = components['schemas']['SurveyLoopErrorDto'];

/**
 * A compilation log is read in the background, so a freshly archived one arrives here queued and
 * becomes figures a moment later with nothing the browser did to mark the change. This is the only
 * query watching for it; without the wait a reader who archives a log sees "queued" until they
 * reload and wonder why the reload was needed.
 *
 * A reading that could not be done counts as settled: the reader has been told, and asking every
 * two seconds forever on the chance somebody re-queues it is a page that never goes quiet.
 */
export function surveyCompilationUnsettled(status: SurveyCompilationInfo['status']): boolean {
  return status === 'pending';
}

/**
 * How well a cave's surveys closed, as the compiler that compiled them reported it.
 *
 * Nothing here is re-derived from the stored survey: these are the numbers printed in the log the
 * surveyor archived. A cave whose exact location is withheld from this reader answers with an empty
 * list rather than a refusal — the same answer as a cave nobody has archived a log for, and it
 * needs no special casing.
 */
export function useSurveyCompilations(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.surveyCompilations(caveId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/caves/{caveId}/survey-compilations', {
          params: { path: { caveId: caveId! } },
        }),
      ),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    refetchInterval: (query) =>
      (query.state.data ?? []).some((c) => surveyCompilationUnsettled(c.status))
        ? SURVEY_COMPILATION_POLL_MS
        : false,
  });
}

export type CenterlineInfo = components['schemas']['CenterlineDto'];

export function useCenterlines(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.centerlines(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/centerlines', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
  });
}

function useInvalidateCenterlines() {
  const queryClient = useQueryClient();
  return (caveId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.centerlines(caveId) });
    invalidateCaveSurveyFigures(queryClient, caveId);
  };
}

export function useUploadCenterline() {
  const invalidate = useInvalidateCenterlines();
  return useMutation({
    mutationFn: async ({ caveId, file }: { caveId: string; file: File }): Promise<CenterlineInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/caves/{caveId}/centerlines', {
        params: { path: { caveId } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export type CenterlineUpdate = components['schemas']['CenterlineUpdateRequest'];

/** Centerline metadata update, including promoting one to the cave's default shape. */
export function useUpdateCenterline() {
  const invalidate = useInvalidateCenterlines();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; caveId: string; body: CenterlineUpdate }) =>
      unwrap(api.PUT('/api/v1/centerlines/{id}', { params: { path: { id } }, body })),
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export function useDeleteCenterline() {
  const invalidate = useInvalidateCenterlines();
  return useMutation({
    mutationFn: async ({ id }: { id: string; caveId: string }) => {
      const { error, response } = await api.DELETE('/api/v1/centerlines/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export type CenterlineFeatureCollection =
  components['schemas']['CenterlineFeatureCollection'];

/**
 * Imperative fetch used by the map centerline loaders (not a hook). The zoom decides
 * whether the server sends the splay-free skeleton or clipped full detail; `detailZoom` and
 * `maxPaths` carry the viewer's own overrides, which the server bounds.
 *
 * `z` opts into surveyed altitudes. It costs a larger payload and is answered per row — the
 * stored display skeleton is a flat shape by construction, so a row served from it comes back
 * without them and the response counts how many did. The flat map leaves it off.
 */
export async function fetchCenterlineFeatures(
  bbox: string,
  zoom: number,
  detailZoom?: number,
  maxPaths?: number,
  z?: boolean,
  filter: MapFilterParams = {},
): Promise<CenterlineFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/cave-centerlines', {
    params: { query: { bbox, zoom, detailZoom, maxPaths, z, ...filter } },
  }));
}

/** The wall meshes of the caves in a view, and how many caves there were in all. */
export type CaveMeshesInView = components['schemas']['CaveMeshesInViewDto'];
/** The one wall mesh a cave is drawn by, with what a scene needs to decide whether to fetch it. */
export type CaveMeshInView = components['schemas']['CaveMeshInViewDto'];

/**
 * Imperative fetch used by the 3D scene's walls-in-view loader (not a hook).
 *
 * One mesh per cave, already chosen by the server, nearest the middle of the box first, and only
 * for caves this caller may both read and place exactly — a cave left out for either reason is in
 * neither the items nor the total, so nothing here can be used to work out that one is there.
 *
 * `maxCaves` is how many caves the viewer's own limit allows; the server holds it to the
 * installation's ceiling and answers with its default when none is sent.
 */
export async function fetchCaveMeshesInView(
  bbox: string,
  maxCaves?: number,
  filter: MapFilterParams = {},
): Promise<CaveMeshesInView> {
  return unwrap(api.GET('/api/v1/map/cave-meshes', { params: { query: { bbox, maxCaves, ...filter } } }));
}
