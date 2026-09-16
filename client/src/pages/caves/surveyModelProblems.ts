// SPDX-License-Identifier: AGPL-3.0-or-later
import type { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';

/**
 * The refusals an upload can answer with, in the words of somebody who has to act on them.
 *
 * A code the server keeps stable so a client can say something better than a status number.
 * Anything not listed falls back to the general failure sentence: a paraphrase of a code nobody
 * has written wording for would be a guess presented as an explanation.
 *
 * Exported so the wording can be checked against both locales. These keys are looked up through a
 * variable rather than written out at the call, so the scan that catches a key nobody translated
 * cannot see a single one of them — and a refusal shown as its own key would land at the exact
 * moment somebody needs to read why the file was turned away.
 */
export const SURVEY_MODEL_PROBLEM_MESSAGE_KEYS: Record<string, string> = {
  'survey_model.format_unsupported': 'surveyModels.problems.formatUnsupported',
  'survey_model.size_invalid': 'surveyModels.problems.sizeInvalid',
  'survey_model.crs_invalid': 'surveyModels.problems.crsInvalid',
  'survey_model.height_invalid': 'surveyModels.problems.heightInvalid',
  'survey_model.origin_invalid': 'surveyModels.problems.originInvalid',
  'survey_model.not_found': 'surveyModels.problems.notFound',
  // A refusal of a delete rather than of an upload, and the one refusal here that is not about the
  // file at all: a trip's live tracking is armed on this model, so removing it would blank the
  // surface a co-ordinator is placing a party on. Generic wording would be the worst possible
  // answer to it — "could not be saved" invites a retry of the very act that was refused on
  // purpose, and says nothing about the two ways forward.
  'survey_model.tracking_armed': 'surveyModels.problems.trackingArmed',
  'cave.not_found': 'surveyModels.problems.caveNotFound',
  'acl.forbidden': 'surveyModels.problems.forbidden',
};

/**
 * What to tell somebody a refusal happened to, in their own language.
 *
 * @param fallback what to say when the server sent no code, or one nobody has written wording for.
 *   It is the caller's because this map now serves more than the upload: a delete refused for a
 *   reason nobody has worded must not fall back to a sentence about an upload failing.
 */
export function surveyModelProblemMessage(
  error: unknown,
  t: ReturnType<typeof useTranslation>['t'],
  fallback = 'surveyModels.uploadFailed',
): string {
  if (!(error instanceof ApiError) || !error.code) {
    return t(fallback);
  }
  // The one refusal here that carries a fact rather than only a reason, and the fact is the whole
  // route out of it.
  //
  // <b>"Close that watch" is an instruction only to somebody who can find the watch.</b> An armed
  // watch stays armed until a person ends it, and a party that came out without anybody telling
  // the application is the ordinary state of things rather than an exception — so this refusal is
  // routinely about a trip nobody is thinking about any more. Told which trip it is, whoever is
  // deleting the survey goes and closes it; told nothing, they are holding a refusal whose
  // instruction they cannot carry out, about a survey that is now undeletable.
  //
  // The server names only the trips this account may read, so the two sentences are two different
  // situations rather than two phrasings: one says which watch to close, the other says who can
  // close it instead. Read from a member of the refusal and not out of its detail sentence, which
  // is prose written for a person and is not a contract.
  if (error.code === 'survey_model.tracking_armed') {
    const trips = error.member('armedTrips');
    if (trips !== undefined && trips.length > 0) {
      return t('surveyModels.trackingArmedNamed', { trips });
    }
  }
  const key = SURVEY_MODEL_PROBLEM_MESSAGE_KEYS[error.code];
  return key ? t(key) : t(fallback);
}
