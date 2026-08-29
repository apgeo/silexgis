// SPDX-License-Identifier: AGPL-3.0-or-later
import type { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';

/**
 * The refusals archiving a survey source can answer with, in the words of somebody who has to act
 * on them. Anything not listed falls back to the general failure sentence: a paraphrase of a code
 * nobody has written wording for would be a guess presented as an explanation.
 *
 * The mismatch case is the one worth wording carefully. It is the refusal a person meets when the
 * bytes are not the format the name claims — a renamed executable, or a project bundle saved as
 * `.svx` — and a generic "upload failed" there teaches nothing about what to do next.
 *
 * Exported so the wording can be checked against both locales. These keys are looked up through a
 * variable rather than written out at the call, so the scan that catches a key nobody translated
 * cannot see a single one of them.
 */
export const SURVEY_SOURCE_PROBLEM_MESSAGE_KEYS: Record<string, string> = {
  'survey_source.format_unsupported': 'surveySources.problems.formatUnsupported',
  'survey_source.content_mismatch': 'surveySources.problems.contentMismatch',
  'survey_source.size_invalid': 'surveySources.problems.sizeInvalid',
  'survey_source.not_found': 'surveySources.problems.notFound',
  'cave.not_found': 'surveySources.problems.caveNotFound',
  'acl.forbidden': 'surveySources.problems.forbidden',
};

export function surveySourceProblemMessage(
  error: unknown,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  if (!(error instanceof ApiError) || !error.code) {
    return t('surveySources.uploadFailed');
  }
  const key = SURVEY_SOURCE_PROBLEM_MESSAGE_KEYS[error.code];
  return key ? t(key) : t('surveySources.uploadFailed');
}
