// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';

/**
 * The sentence that says what a registry figure was computed over, in the reader's language.
 *
 * The server names which of its sentences applies (`readable`, `placeable` or `area`) next to the
 * English text it also sends. The kind is chosen from the same scope predicate the figures were
 * computed with, so the client never works it out itself. A kind this build has no sentence for
 * (a newer server) falls back to the server's own English text rather than showing a key.
 */
export function basisSentence(t: TFunction, kind: string | undefined, serverText: string): string {
  if (!kind) return serverText;
  return t(`registryStats.basis.${kind}`, { defaultValue: serverText });
}
