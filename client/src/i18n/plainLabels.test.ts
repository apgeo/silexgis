// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import ro from './locales/ro.json';

describe('labels that say what a member sees', () => {
  it('titles the forgotten-password page apart from the page that sets the new password', () => {
    // The first page asks for an address; only the emailed link lets anybody choose a password.
    for (const words of [en, ro]) {
      expect(words.auth.forgotTitle).not.toBe(words.auth.resetPasswordTitle);
    }
    expect(en.auth.forgotTitle).toBe('Reset your password');
  });

  it('names the helper layers in the layer list by what they draw, not how they work', () => {
    expect(en.tableMap.highlightLayer).not.toMatch(/pointed at/i);
    expect(en.map.overburdenHighlight).not.toMatch(/overburden/i);
    expect(ro.map.overburdenHighlight).not.toMatch(/^Citirea/);
  });

  it('names cancelling a trip, a camp or an event the same way on the button, the question and the notice', () => {
    const asked = [
      en.trips.callOffConfirm,
      en.trips.callOffSuccess,
      en.expeditions.callOffConfirm,
      en.events.callOffConfirm,
      en.events.callOffSuccess,
      en.calendar.toggleCancelled,
    ];
    for (const words of asked) {
      expect(words).not.toMatch(/call(ed)?\b.*\boff/i);
      expect(words).toMatch(/cancel/i);
    }
    expect(en.trips.callOff).toBe('Cancel trip');
  });
});
