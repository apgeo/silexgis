// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/**
 * The refusals a write to a list can meet, in the words of the person who has to act on them.
 * Exported so the wording can be checked against both locales: the keys are looked up through a
 * variable, so the scan that catches an untranslated key at a `t('…')` call cannot see them.
 */
export const CHECKLIST_PROBLEM_MESSAGE_KEYS: Record<string, string> = {
  // Removed, or no longer shared with this reader, since the page was drawn.
  'checklist.not_found': 'checklists.problems.notFound',
  'access.create_forbidden': 'checklists.problems.createForbidden',
  'access.caving_group_binding_forbidden': 'checklists.problems.cavingGroupForbidden',
  'acl.forbidden': 'checklists.problems.forbidden',
};

/** The sentence for a failed save or delete of a list. */
export function checklistProblem(error: unknown, t: TFunction): string {
  const key = error instanceof ApiError && error.code ? CHECKLIST_PROBLEM_MESSAGE_KEYS[error.code] : undefined;
  return t(key ?? 'checklists.saveFailed');
}
