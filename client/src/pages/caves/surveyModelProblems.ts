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
  'cave.not_found': 'surveyModels.problems.caveNotFound',
  'acl.forbidden': 'surveyModels.problems.forbidden',
};

export function surveyModelProblemMessage(
  error: unknown,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  if (!(error instanceof ApiError) || !error.code) {
    return t('surveyModels.uploadFailed');
  }
  const key = SURVEY_MODEL_PROBLEM_MESSAGE_KEYS[error.code];
  return key ? t(key) : t('surveyModels.uploadFailed');
}
