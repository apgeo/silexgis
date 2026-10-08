// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import ro from './locales/ro.json';

/**
 * Romanian words that were settled once and then drifted.
 *
 * A term that is right in the glossary and wrong in a dozen sentences is not something a reader
 * of one screen can see; it shows only side by side. These checks read every Romanian string
 * and name the ones that use the unsettled word, so the next sentence written about the same
 * thing cannot bring it back unnoticed.
 */

function strings(node: unknown, path = ''): [string, string][] {
  if (typeof node === 'string') {
    return [[path, node]];
  }
  return Object.entries(node as Record<string, unknown>).flatMap(([key, value]) =>
    strings(value, path === '' ? key : `${path}.${key}`),
  );
}

const all = strings(ro);

describe('Romanian wording', () => {
  it('says a watch that is over is "încheiată", never "închisă"', () => {
    // The state is named "Urmărire încheiată" and the button that ends one "Încheie
    // urmărirea". "Închisă" is what a door, a panel or a page is; of a watch it reads as a
    // second, different state. A sentence is judged on its own, so a string may still close
    // a panel in one sentence and speak of a watch in the next.
    // No `\b` here: to a pattern without the Unicode flag "î" is not a letter, so a word
    // boundary in front of it is found only where there is none.
    const closes = /(?<!\p{L})închi(s|d|z)/iu;
    const watch = /urmărir/i;
    const offenders = all
      .filter(([, text]) =>
        text
          .split(/(?<=[.;:!?—])\s+/)
          .some(
            (sentence) =>
              (closes.test(sentence) && watch.test(sentence)) || /după închidere/i.test(sentence),
          ),
      )
      .map(([key]) => key);

    expect(offenders).toEqual([]);
    // The settled word is really the one in use, so the check above is not passing over a
    // file that never speaks of a watch ending at all.
    expect(ro.trips.tracking.stateValues.closed).toBe('Urmărire încheiată');
    expect(ro.trips.tracking.close).toBe('Încheie urmărirea');
  });

  it('calls the ground above a cave "relief" throughout the movie dialog', () => {
    // The dialog's own switch is "Relieful de deasupra peșterii"; two sentences beside it
    // said "teren" of the same thing.
    const offenders = all
      .filter(([key, text]) => key.startsWith('caveview.movie.') && /\bteren/i.test(text))
      .map(([key]) => key);

    expect(offenders).toEqual([]);
    expect(ro.caveview.movie.terrain).toBe('Relieful de deasupra peșterii');
  });
});
