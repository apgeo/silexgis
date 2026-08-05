// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import type { ResLink, ResLinkRelationType } from '../../api/hooks.ts';

/**
 * How a relation reads in words. The vocabulary ships with a fixed set of codes that are
 * translated here, and installations may add their own rows, which are shown exactly as an
 * administrator wrote them — one list, two sources of wording.
 *
 * A directed relation reads two ways: the stored name reads *from* the main member outwards
 * ("Contains", "Documented by"), the inverse name reads back from any other member
 * ("Contained in", "Documents"). Which one a panel row shows therefore depends on whether
 * the entity whose page it is happens to be the main member.
 */

/** Relation codes this client ships wording for; anything else is shown as stored. */
export const SEEDED_RELATION_CODES = [
  'same-object',
  'related-to',
  'contains',
  'documents',
  'derived-from',
  'adjacent-to',
  'duplicate-of',
  'needs-clarification',
] as const;

export type SeededRelationCode = (typeof SEEDED_RELATION_CODES)[number];

/** The subset of the above that reads differently from each end. */
export const DIRECTED_RELATION_CODES: readonly SeededRelationCode[] = [
  'contains',
  'documents',
  'derived-from',
  'duplicate-of',
];

export function isSeededRelationCode(code: string): code is SeededRelationCode {
  return (SEEDED_RELATION_CODES as readonly string[]).includes(code);
}

/** Which end of a directed relation the reader is standing on. */
export type RelationReading = 'forward' | 'inverse';

/**
 * The phrase for one relation, read from one end. Falls back to the stored wording
 * whenever the code is not one this client translates — a custom row, or a code added by a
 * server newer than this client.
 */
export function relationPhrase(
  relationType: ResLinkRelationType | null | undefined,
  reading: RelationReading,
  t: TFunction,
): string {
  if (!relationType) {
    return t('resLinks.relations.unspecified');
  }
  const wantsInverse = reading === 'inverse' && relationType.directed;
  if (isSeededRelationCode(relationType.code)) {
    return t(
      wantsInverse
        ? `resLinks.relations.${relationType.code}.inverse`
        : `resLinks.relations.${relationType.code}.name`,
    );
  }
  return (wantsInverse ? relationType.inverseName : relationType.name) ?? relationType.name;
}

/**
 * The phrase as one entity's own page should read it. The main member is the end the
 * stored name is written from, so a page that *is* the main member reads forward and every
 * other page reads back. An undirected relation reads the same either way, and a link
 * whose own member row this caller cannot see falls back to the forward wording rather
 * than guessing a direction from an incomplete membership.
 */
export function relationPhraseFor(
  link: ResLink,
  targetType: string,
  targetId: string,
  t: TFunction,
): string {
  const self = link.members.find(
    (member) => member.targetType === targetType && member.targetId === targetId,
  );
  return relationPhrase(link.relationType, self?.isMain === false ? 'inverse' : 'forward', t);
}
