// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { publicNamingOf } from './trackingPublicName.ts';

describe('what a published page will call somebody', () => {
  /**
   * The rule the caption exists for, and the reason it is the *only* way somebody stays off a
   * public page: a setting flipped somewhere else must not undo a person's request.
   */
  it('lets a caption keep somebody off a page that would otherwise name them', () => {
    expect(publicNamingOf({ label: 'A club member' }, true)).toBe('caption');
    // The twin, without which this would pass against a rule that simply ignored the setting:
    // with no caption, an installation that publishes names does name them.
    expect(publicNamingOf({ label: null }, true)).toBe('realName');
  });

  /**
   * The other direction, which is the half a "hide people" reading of this field gets backwards.
   * On an installation that names nobody, a caption is what puts one name on the page — the trip
   * leader a follower can ring.
   */
  it('lets a caption name somebody on a page that would otherwise number them', () => {
    expect(publicNamingOf({ label: 'Ana, trip leader' }, false)).toBe('caption');
    expect(publicNamingOf({ label: null }, false)).toBe('placeInParty');
  });

  // A caption that has been cleared but not yet re-read arrives as blank rather than absent, and
  // blank names nobody: drawn as a caption it would be an empty quotation where a name belongs.
  it('treats a blank caption as no caption', () => {
    expect(publicNamingOf({ label: '   ' }, true)).toBe('realName');
    expect(publicNamingOf({ label: '' }, false)).toBe('placeInParty');
    expect(publicNamingOf({ label: ' A club member ' }, false)).toBe('caption');
  });

  /**
   * An unanswered setting is worded as the disclosing case, exactly as the panel that mints a link
   * words it. Being warned about names that may not appear costs a moment; the other way round
   * costs somebody else their name on a public page.
   */
  it('reads a setting it was never given as the naming case', () => {
    expect(publicNamingOf({ label: null }, undefined as unknown as boolean)).toBe('realName');
    expect(publicNamingOf({ label: null }, false)).toBe('placeInParty');
  });
});
