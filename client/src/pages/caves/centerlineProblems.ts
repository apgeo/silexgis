// SPDX-License-Identifier: AGPL-3.0-or-later
import type { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';

/**
 * The refusals a centerline upload, edit or delete can answer with, in the words of somebody who
 * has to act on them. Most upload refusals are about the file — a kind the server does not read, a
 * file with only points in it — and "upload failed" would have the person send the same file again.
 * Anything not listed falls back to the caller's general sentence: a paraphrase of a code nobody
 * has written wording for would be a guess presented as an explanation.
 *
 * Exported so the wording can be checked against both locales: these keys are looked up through a
 * variable, so the scan that catches an untranslated key cannot see them.
 */
export const CENTERLINE_PROBLEM_MESSAGE_KEYS: Record<string, string> = {
  'centerline.format_unsupported': 'centerlines.problems.formatUnsupported',
  'centerline.size_invalid': 'centerlines.problems.sizeInvalid',
  'centerline.unreadable': 'centerlines.problems.unreadable',
  'centerline.no_lines': 'centerlines.problems.noLines',
  'centerline.survey_model_invalid': 'centerlines.problems.surveyModelInvalid',
  'centerline.default_required': 'centerlines.problems.defaultRequired',
  'centerline.not_found': 'centerlines.problems.notFound',
  'cave.not_found': 'centerlines.problems.caveNotFound',
  'acl.forbidden': 'centerlines.problems.forbidden',
};

export function centerlineProblemMessage(
  error: unknown,
  t: ReturnType<typeof useTranslation>['t'],
  fallbackKey: string,
): string {
  const key = error instanceof ApiError && error.code ? CENTERLINE_PROBLEM_MESSAGE_KEYS[error.code] : undefined;
  return t(key ?? fallbackKey);
}
