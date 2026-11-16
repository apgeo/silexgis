// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';

/**
 * The refusals a write to a camp has words of its own for, by the stable code the server sends —
 * saving the camp, deleting it, and removing a stay from its roster.
 *
 * One table for the camp's writes rather than one per surface, so the same refusal reads the same
 * on the form, on the page and on the roster. The lifecycle and the roster dialog keep their own
 * narrower wording, because the codes they meet are theirs alone.
 */
export const EXPEDITION_PROBLEMS: Readonly<Record<string, string>> = {
  'acl.forbidden': 'expeditions.problems.forbidden',
  'access.create_forbidden': 'expeditions.problems.createForbidden',
  'access.caving_group_binding_forbidden': 'expeditions.problems.cavingGroupForbidden',
  // One sentence for "gone" and "no longer yours to see", because the server gives them one answer.
  'expedition.not_found': 'expeditions.problems.gone',
  'expedition.geometry_invalid': 'expeditions.problems.geometryInvalid',
  'expedition_roster.not_found': 'expeditions.problems.stayGone',
};

/**
 * The translation key for a failed camp write, or the caller's own general phrase when the server
 * said nothing this table has words for. The fallback is the caller's because "save failed" is the
 * wrong verb for a delete.
 */
export const expeditionProblemKey = (error: unknown, fallback: string): string =>
  (error instanceof ApiError ? EXPEDITION_PROBLEMS[error.code ?? ''] : undefined) ?? fallback;
