// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Public links to a survey upload's results: the anonymous result page, and minting / revoking the
// link from the status page.
//
// WHY THIS IS HAND-TYPED. The server does not publish these routes in its OpenAPI document, so the
// generated client (`../schema`) has no member for them. Everything that depends on their shape is
// kept in this one module, so that once they are published the swap is local: delete the
// `PublicLinkPaths` / `PublicSurveyUploadEnvelope` declarations, take the types from
// `components['schemas']`, and call `api` instead of `publicLinkApi`. Nothing outside this file,
// and nothing in the screens, names a route or a member of the answer.
//
// What is fixed and what this assumes:
//  - fixed: the anonymous route `GET /api/v1/public/survey-uploads/{token}` and the members its
//    answer may carry (survey title, the cave's *name* only, the upload moment, status, stages,
//    progress, message, error, artefacts with delivery addresses). It never carries the archive,
//    the Therion sources, the log, the team, station names, a cave id or a coordinate.
//  - assumed: the revoke and re-mint addresses, `DELETE` and `POST` on
//    `/api/v1/survey-uploads/{id}/public-link`. The status DTO's `publicLink` member already exists.
//  - assumed: the status DTO may carry `publicLinkRefusal`, a code saying why an upload has no link
//    (see `surveyUploadPublicLinkRefusal`); absent means the reason is not known.
//  - assumed: the envelope names `stages` (a state per stage). The adapter does not depend on it:
//    when it is absent the states are worked out from `stage` and `status` exactly as for the
//    signed-in page.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import createClient from 'openapi-fetch';
import { ApiError, carriesTheAccountsBearer, sendsTheReadingLanguage } from '../client.ts';
import type { components } from '../schema';
import { retryUnlessRefused, unwrap, unwrapVoid } from './shared.ts';
import { queryKeys } from './queryKeys.ts';
import {
  surveyUploadPollInterval,
  surveyUploadStageStates,
  type StageProgress,
  type SurveyUploadStage,
  type SurveyUploadStatus,
} from './surveyUploads.ts';

type PublicLink = components['schemas']['SurveyUploadPublicLinkDto'];
type Artefact = components['schemas']['SurveyUploadArtefactDto'];

/** An artefact as the public answer lists it: no ids of documents, models or sources — only what can be opened or fetched. */
export type PublicSurveyArtefact = Pick<Artefact, 'kind' | 'name' | 'sizeBytes' | 'contentUrl'> & {
  viewUrl?: string | null;
};

/** The anonymous answer for a public link. */
export interface PublicSurveyUploadEnvelope {
  survey: { title: string };
  cave: { name: string };
  /** The upload's moment, which is also its version. */
  version: string;
  status: SurveyUploadStatus;
  stage: SurveyUploadStage | null;
  /** A state per stage, when the server states them. */
  stages?: { stage: SurveyUploadStage; state: StageProgress['state'] }[];
  /** 0–100 */
  progress: number;
  message: string | null;
  error: { code: string; message: string | null } | null;
  artefacts: PublicSurveyArtefact[];
  createdAt: string;
  finishedAt: string | null;
}

interface PublicLinkPaths {
  '/api/v1/public/survey-uploads/{token}': {
    parameters: { query?: never; header?: never; path?: never; cookie?: never };
    get: {
      parameters: { query?: never; header?: never; path: { token: string }; cookie?: never };
      requestBody?: never;
      responses: {
        200: { headers: { [name: string]: unknown }; content: { 'application/json': PublicSurveyUploadEnvelope } };
        404: { headers: { [name: string]: unknown }; content: { 'application/problem+json': components['schemas']['ProblemDetails'] } };
      };
    };
  };
  '/api/v1/survey-uploads/{id}/public-link': {
    parameters: { query?: never; header?: never; path?: never; cookie?: never };
    post: {
      parameters: { query?: never; header?: never; path: { id: string }; cookie?: never };
      requestBody?: never;
      responses: {
        200: { headers: { [name: string]: unknown }; content: { 'application/json': PublicLink } };
        201: { headers: { [name: string]: unknown }; content: { 'application/json': PublicLink } };
      };
    };
    delete: {
      parameters: { query?: never; header?: never; path: { id: string }; cookie?: never };
      requestBody?: never;
      responses: { 204: { headers: { [name: string]: unknown }; content?: never } };
    };
  };
}

// The same sign-in and language headers as the application's client: the anonymous route ignores
// the bearer, the revoke and mint routes need it.
//
// Built on first use, not when the module loads: this module is re-exported by the hooks barrel,
// which a great many screens import, and several of their tests replace the application's client
// module with a stand-in that has neither middleware.
let publicLinkClient: ReturnType<typeof createClient<PublicLinkPaths>> | undefined;
function publicLinkApi() {
  if (!publicLinkClient) {
    publicLinkClient = createClient<PublicLinkPaths>({ baseUrl: '/' });
    publicLinkClient.use(carriesTheAccountsBearer);
    publicLinkClient.use(sendsTheReadingLanguage);
  }
  return publicLinkClient;
}

/** Why an upload has no public link, when the server says. */
export type PublicLinkRefusal = 'protected-cave' | 'account-off' | 'installation-off' | 'no-share-right';

const KNOWN_REFUSALS: readonly string[] = ['protected-cave', 'account-off', 'installation-off', 'no-share-right'];

/**
 * The reason the server gave for an upload having no public link, read from the status answer's
 * `publicLinkRefusal` member (not yet in the generated types). A code this client does not know
 * reads as "no reason known" rather than being shown raw.
 */
export function surveyUploadPublicLinkRefusal(upload: object): PublicLinkRefusal | null {
  const code = (upload as { publicLinkRefusal?: string | null }).publicLinkRefusal;
  return code && KNOWN_REFUSALS.includes(code) ? (code as PublicLinkRefusal) : null;
}

/** What the result page shows, whichever answer it was built from. */
export interface PublicResultView {
  title: string;
  caveName: string;
  version: string;
  status: SurveyUploadStatus;
  progress: number;
  message: string | null;
  error: { code: string; message: string | null } | null;
  stages: StageProgress[];
  artefacts: PublicSurveyArtefact[];
}

/**
 * The envelope as the page reads it. Stage states come from the server when it sends them and are
 * worked out otherwise; a state the server names that this client does not know is read as pending
 * rather than dropped, so the list never loses a stage.
 */
export function publicResultView(envelope: PublicSurveyUploadEnvelope): PublicResultView {
  const known: StageProgress['state'][] = ['done', 'active', 'pending', 'failed', 'skipped'];
  const named = envelope.stages;
  const stages: StageProgress[] = named?.length
    ? named.map((s) => ({ stage: s.stage, state: known.includes(s.state) ? s.state : 'pending' }))
    : surveyUploadStageStates({ status: envelope.status, stage: envelope.stage });
  return {
    title: envelope.survey.title,
    caveName: envelope.cave.name,
    version: envelope.version,
    status: envelope.status,
    progress: envelope.progress,
    message: envelope.message,
    error: envelope.error,
    stages,
    artefacts: envelope.artefacts,
  };
}

/**
 * The result for a public link, asking again while the run is unfinished.
 *
 * A wrong, revoked or withdrawn link answers 404 exactly as an invented one does; that is a settled
 * answer, so it is not retried and the page can say "this link does not work" without waiting.
 */
export function usePublicSurveyUpload(token: string | undefined) {
  return useQuery({
    queryKey: queryKeys.publicSurveyUpload(token ?? ''),
    queryFn: () =>
      unwrap(
        publicLinkApi().GET('/api/v1/public/survey-uploads/{token}', { params: { path: { token: token! } } }),
      ),
    enabled: !!token,
    retry: retryUnlessRefused,
    select: publicResultView,
    refetchInterval: (query) => surveyUploadPollInterval(query.state.data?.status),
  });
}

/** Whether a failed call means the link is simply not there (never existed, revoked, or withdrawn). */
export function isUnknownPublicLink(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404;
}

function refreshUpload(queryClient: ReturnType<typeof useQueryClient>, id: string) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.surveyUpload(id) });
}

/**
 * Makes the upload's public link, or a new one when it already has one (the old address stops
 * working). The upload's status is asked again so `publicLink` on the page is the server's word.
 */
export function useMintSurveyUploadPublicLink() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(publicLinkApi().POST('/api/v1/survey-uploads/{id}/public-link', { params: { path: { id } } })),
    onSuccess: (_link, id) => refreshUpload(queryClient, id),
  });
}

/** Withdraws the upload's public link; the address answers like one that never existed. */
export function useRevokeSurveyUploadPublicLink() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(publicLinkApi().DELETE('/api/v1/survey-uploads/{id}/public-link', { params: { path: { id } } })),
    onSuccess: (_void, id) => refreshUpload(queryClient, id),
  });
}
