// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/**
 * The server's refusals, said in words a caver can act on.
 *
 * Two codes are deliberately keyed by status as well as name: the same refusal means
 * different things depending on which call raised it. `reslink.member.not_found` is a bad
 * request when a link write names a member that is not in it, and a plain 404 when the
 * member itself is being written; `reslink.relation.not_found` splits the same way. Keying
 * on the code alone would pick the wrong sentence for one of each pair.
 *
 * Anything unrecognised falls back to the generic save failure the rest of the app uses —
 * an unmapped code must still produce a sentence, never a raw identifier on screen.
 */
const byStatusAndCode: Record<string, string> = {
  '400:reslink.member.not_found': 'resLinks.problems.mainNotAMember',
  '404:reslink.member.not_found': 'resLinks.problems.memberGone',
  '400:reslink.relation.not_found': 'resLinks.problems.relationUnknown',
  '404:reslink.relation.not_found': 'resLinks.problems.relationGone',
  '400:reslink.member.duplicate_whole': 'resLinks.problems.duplicateWhole',
  '409:reslink.member.duplicate_whole': 'resLinks.problems.duplicateWhole',
};

const byCode: Record<string, string> = {
  'reslink.not_found': 'resLinks.problems.linkGone',
  'reslink.code.unresolved': 'resLinks.problems.codeUnresolved',
  'reslink.code.invalid': 'resLinks.problems.codeUnresolved',
  'reslink.entity_type_unknown': 'resLinks.problems.typeUnknown',
  'reslink.target_not_found': 'resLinks.problems.targetGone',
  'reslink.member.target_not_found': 'resLinks.problems.targetGone',
  'reslink.member.target_invalid': 'resLinks.problems.targetInvalid',
  'reslink.member.type_not_linkable': 'resLinks.problems.typeNotLinkable',
  'reslink.member.invalid_anchor_kind': 'resLinks.problems.anchorKindNotAllowed',
  'reslink.member.invalid_anchor': 'resLinks.problems.anchorInvalid',
  'reslink.member.anchor_file_invalid': 'resLinks.problems.anchorFileInvalid',
  'reslink.member.anchor_pin_required': 'resLinks.problems.anchorPinRequired',
  'reslink.member.limit_reached': 'resLinks.problems.memberLimit',
  'reslink.member.last': 'resLinks.problems.lastMember',
  'reslink.main.required': 'resLinks.problems.mainRequired',
  'reslink.main.not_single': 'resLinks.problems.mainNotSingle',
  'reslink.main.not_allowed_for_relation': 'resLinks.problems.mainNotAllowed',
  'reslink.member.geo_point_unavailable': 'resLinks.problems.geoPointUnavailable',
  'access.create_forbidden': 'resLinks.problems.createForbidden',
  'acl.forbidden': 'resLinks.problems.forbidden',
  'validation.failed': 'resLinks.problems.validationFailed',
};

/** The message to show for a failed link write. */
export function resLinkProblemMessage(error: unknown, t: TFunction): string {
  if (!(error instanceof ApiError)) {
    return t('common.saveFailed');
  }
  if (error.status === 401) {
    return t('resLinks.problems.signedOut');
  }
  const code = error.code;
  if (!code) {
    return t('common.saveFailed');
  }
  const key = byStatusAndCode[`${error.status}:${code}`] ?? byCode[code];
  return key ? t(key) : t('common.saveFailed');
}
