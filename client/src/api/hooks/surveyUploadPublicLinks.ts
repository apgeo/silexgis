// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Public links to a survey upload's results: the anonymous result page, and making / withdrawing
// the link from the status page.
//
// The anonymous answer is limited by the server to the survey title, the cave's *name* only, the
// upload moment, status, stages, progress, message, error, the artefacts that can be opened and the
// compiler's log (its tail, and where to download the whole of it). It never carries the archive,
// the Therion sources, the team, a cave id or a coordinate of its own, so the page built from it
// has nothing to hide: the log is the holder's to read, like the model it was compiled into.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError } from '../client.ts';
import type { components } from '../schema';
import { retryUnlessRefused, unwrap, unwrapVoid } from './shared.ts';
import { queryKeys } from './queryKeys.ts';
import {
  surveyUploadPollInterval,
  surveyUploadStageStates,
  type StageProgress,
  type SurveyUploadStatus,
} from './surveyUploads.ts';

export type PublicSurveyUpload = components['schemas']['PublicSurveyUploadDto'];
export type SurveyUploadPublicLink = components['schemas']['SurveyUploadPublicLinkDto'];
export type SurveyUploadLinkAudience = components['schemas']['SurveyUploadLinkAudience'];
export type SurveyUploadPublicLinkMintRequest = components['schemas']['SurveyUploadPublicLinkMintRequest'];

/** Who a link opens for, in the order the forms offer them. */
export const SURVEY_UPLOAD_LINK_AUDIENCES: readonly SurveyUploadLinkAudience[] = ['public', 'organization'];

/** The server's code for a link made for the organization, opened without signing in. */
export const PUBLIC_LINK_SIGN_IN_REQUIRED = 'survey_upload.public_link_sign_in_required';

/**
 * An artefact as the shared list draws it. Both answers satisfy it: the public one always has a
 * size and an address; the signed-in one may lack them for an output the run was asked for and
 * did not produce, and then carries that output's own `error`.
 */
export interface SurveyUploadArtefactView {
  kind: string;
  name: string;
  sizeBytes: number | null;
  contentUrl: string | null;
  viewUrl?: string | null;
  error?: { code: string; message: string | null } | null;
}

/** Why an upload has no public link — the codes the server writes in `publicLinkRefusal`. */
export type PublicLinkRefusal =
  | 'installation-off'
  | 'account-off'
  | 'not-wanted'
  | 'no-share-right'
  | 'protected-cave'
  | 'revoked';

export const KNOWN_REFUSALS: readonly PublicLinkRefusal[] = [
  'installation-off',
  'account-off',
  'not-wanted',
  'no-share-right',
  'protected-cave',
  'revoked',
];

/**
 * The reason the server gave for an upload having no public link. A code this client does not
 * know reads as "no reason known" rather than being shown raw.
 */
export function surveyUploadPublicLinkRefusal(upload: { publicLinkRefusal: string | null }): PublicLinkRefusal | null {
  const code = upload.publicLinkRefusal;
  return KNOWN_REFUSALS.find((known) => known === code) ?? null;
}

/** What the result page shows. */
export interface PublicResultView {
  title: string;
  caveName: string;
  version: string;
  status: SurveyUploadStatus;
  progress: number;
  message: string | null;
  error: { code: string; message: string | null } | null;
  stages: StageProgress[];
  artefacts: SurveyUploadArtefactView[];
  /** The last lines the compiler wrote; null before a compile. */
  logTail: string | null;
  /** The compiler's whole log, downloaded from a route of the same link; null when there is none. */
  log: PublicSurveyLog | null;
}

/** The whole log of a run, as the public answer offers it. */
export interface PublicSurveyLog {
  name: string;
  sizeBytes: number;
  url: string;
}

/**
 * The public answer as the page reads it. Stage states are the server's; a state this client does
 * not know is read as pending rather than dropped, so the list never loses a stage, and an answer
 * naming no stages at all has them worked out from `stage` and `status` as the signed-in page does.
 */
export function publicResultView(answer: PublicSurveyUpload): PublicResultView {
  const known: StageProgress['state'][] = ['done', 'active', 'pending', 'failed', 'skipped'];
  const stages: StageProgress[] = answer.stages.length
    ? answer.stages.map((s) => ({ stage: s.stage, state: known.includes(s.state) ? s.state : 'pending' }))
    : surveyUploadStageStates({ status: answer.status, stage: answer.stage });
  return {
    title: answer.survey.title,
    caveName: answer.cave.name,
    version: answer.version,
    status: answer.status,
    progress: answer.progress,
    message: answer.message,
    error: answer.error,
    stages,
    artefacts: answer.artefacts,
    logTail: answer.logTail ?? null,
    log: answer.log ?? null,
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
    queryFn: () => unwrap(api.GET('/api/v1/public/survey-uploads/{token}', { params: { path: { token: token! } } })),
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

/**
 * Whether a failed call means the link works but was made for the installation's signed-in
 * accounts, and this reader has not signed in. The server says so only once everything else about
 * the link has said it opens, so it is a settled answer: signing in is the way on, not a retry.
 */
export function isSignInRequiredPublicLink(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401 && error.code === PUBLIC_LINK_SIGN_IN_REQUIRED;
}

function refreshUpload(queryClient: ReturnType<typeof useQueryClient>, id: string) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.surveyUpload(id) });
}

/**
 * Makes the upload's public link, or a new one when it already has one (the old address stops
 * working). The upload's status is asked again so `publicLink` on the page is the server's word.
 *
 * `audience` empty leaves the choice to the server (the replaced link's, the upload's, the
 * account's default). `allowProtected` is the person's explicit acknowledgement that the cave's
 * position is protected and the results show where its passages are; without it the server
 * refuses a protected cave.
 */
export function useMintSurveyUploadPublicLink() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, audience = null, allowProtected = null }: { id: string } & Partial<SurveyUploadPublicLinkMintRequest>) =>
      unwrap(
        api.POST('/api/v1/survey-uploads/{id}/public-link', {
          params: { path: { id } },
          body: { audience, allowProtected },
        }),
      ),
    onSuccess: (_link, { id }) => refreshUpload(queryClient, id),
  });
}

/** Withdraws the upload's public link; the address answers like one that never existed. */
export function useRevokeSurveyUploadPublicLink() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/survey-uploads/{id}/public-link', { params: { path: { id } } })),
    onSuccess: (_void, id) => refreshUpload(queryClient, id),
  });
}
