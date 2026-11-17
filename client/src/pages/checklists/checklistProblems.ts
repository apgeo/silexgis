// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError, isConcurrencyConflict } from '../../api/client.ts';

/** What the page was doing when it was refused: each failure names its own act. */
export type ChecklistAct = 'open' | 'save' | 'delete';

/**
 * The general failure of each act, for a refusal this page has no phrase of its own for. Exported
 * with the per-code table below so the wording can be checked against both locales: the keys are
 * looked up through a variable, so the scan that catches an untranslated key at a `t('…')` call
 * cannot see them.
 */
export const CHECKLIST_FAILED_KEYS: Record<ChecklistAct, string> = {
  open: 'checklists.problems.openFailed',
  save: 'checklists.saveFailed',
  delete: 'checklists.problems.deleteFailed',
};

/** The refusals a read or write of a list can meet, in the words of the person who has to act on them. */
export const CHECKLIST_PROBLEM_MESSAGE_KEYS: Record<string, string> = {
  // Removed, or no longer shared with this reader, since the page was drawn.
  'checklist.not_found': 'checklists.problems.notFound',
  'acl.forbidden': 'checklists.problems.forbidden',
  // Whether a new list may be made depends on the group it is made under, so the refusal says
  // that much: retried as it stands, it would be refused the same way every time.
  'access.create_forbidden': 'checklists.problems.createForbidden',
  'access.caving_group_binding_forbidden': 'checklists.problems.groupNotAllowed',
  'validation.failed': 'checklists.problems.invalid',
};

/**
 * Why a checklist could not be opened, saved or deleted, in words a person can act on — or
 * nothing, for a stale version.
 *
 * A stale version is worded once for every write, by the application's own conflict warning; a
 * second sentence beside it would contradict it, so it answers `null` and the page says nothing
 * more. The server's own sentence is never shown in place of these: it is not translated, and a
 * refusal this page has a phrase for reads the same in both languages.
 */
export function checklistProblem(error: unknown, act: ChecklistAct, t: TFunction): string | null {
  if (isConcurrencyConflict(error)) {
    return null;
  }

  const key =
    error instanceof ApiError && error.code ? CHECKLIST_PROBLEM_MESSAGE_KEYS[error.code] : undefined;
  return t(key ?? CHECKLIST_FAILED_KEYS[act]);
}
