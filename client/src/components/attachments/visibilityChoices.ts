// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import type { Visibility } from '../../api/hooks.ts';

// Keyed by the API's own enum, so a value it does not have — or one it gained — fails the
// type-check here rather than reaching a form as an option the server refuses on save.
const audiences: Record<Visibility, true> = {
  private: true,
  cavingGroup: true,
  authenticated: true,
  public: true,
};

/** Every audience a document, an album or a cabinet's default can be given, narrowest first. */
export const DOCUMENT_VISIBILITIES = Object.keys(audiences) as readonly Visibility[];

/** The audiences as select options, each named in the reader's language. */
export function visibilityOptions(t: TFunction): { value: Visibility; label: string }[] {
  return DOCUMENT_VISIBILITIES.map((value) => ({ value, label: t(`caves.visibilityValues.${value}`) }));
}
