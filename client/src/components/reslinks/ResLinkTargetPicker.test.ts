// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { textFieldOf } from '../selector/useObjectSelector.ts';
import { WORLD_OF } from './ResLinkTargetPicker.tsx';
import { RESLINK_TARGET_TYPES } from './registry.ts';

/**
 * Picking the other end of a link is done by typing part of its name. For the kinds the picker
 * hands to the object selector, what was typed reaches the server as a condition on one field of
 * that kind's world — and a world the selector knows no such field for is asked with nothing of
 * what was typed, so nothing is ever found. Two of the four kinds were in that state.
 */
describe('the link picker', () => {
  it.each(Object.entries(WORLD_OF))(
    'can search the %s kind by what is typed',
    (_kind, world) => {
      expect(textFieldOf(world!)).toEqual(expect.any(String));
    },
  );

  // The server answers its own target search only for these three and refuses it for every
  // other kind, so a kind the picker still sent there would find nothing however it was typed.
  it('sends only people, clubs and camps to the link search, and every other kind to a world', () => {
    const searched = RESLINK_TARGET_TYPES.filter((kind) => !WORLD_OF[kind]);
    expect(searched).toEqual(['caver', 'cavingGroup', 'expedition']);
  });
});
