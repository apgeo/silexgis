// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError, isConcurrencyConflict } from '../../api/client.ts';

/** What the page was doing when it was refused: each failure names its own act. */
export type ChecklistAct = 'open' | 'save' | 'delete';

const FAILED_KEYS: Record<ChecklistAct, string> = {
  open: 'checklists.problems.openFailed',
  save: 'checklists.saveFailed',
  delete: 'checklists.problems.deleteFailed',
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

  if (error instanceof ApiError) {
    switch (error.code) {
      case 'checklist.not_found':
        return t('checklists.problems.notFound');
      case 'acl.forbidden':
        return t('checklists.problems.forbidden');
      // Whether a new list may be made depends on the group it is made under, so the refusal
      // says that much: retried as it stands, it would be refused the same way every time.
      case 'access.create_forbidden':
        return t('checklists.problems.createForbidden');
      case 'access.caving_group_binding_forbidden':
        return t('checklists.problems.groupNotAllowed');
      case 'validation.failed':
        return t('checklists.problems.invalid');
    }
  }

  return t(FAILED_KEYS[act]);
}
