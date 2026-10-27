// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/** The sentence for a refused decision: ours for a code we have words for, else the server's own. */
export function reviewProblem(error: unknown, t: TFunction): string {
  if (error instanceof ApiError) {
    if (error.code) {
      const key = `review.problem.${error.code.replace(/\./g, '_')}`;
      const text = t(key, { defaultValue: '' });
      if (text) {
        return text;
      }
    }
    if (error.detail) {
      return error.detail;
    }
  }
  return t('review.problem.generic');
}
