// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
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
const english = new Map(strings(en));

function englishOf(key: string): string {
  return english.get(key) ?? '';
}

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

  it('addresses the reader as "tu", never with the polite plural', () => {
    // The interface spoke to its reader both ways, often on the same screen. The polite forms
    // are found by their pronouns and by the verbs a sentence to the reader is made of; a plural
    // participle such as "selectați" or a noun such as "pereți" is not one of them.
    const polite =
      /(?<!\p{L})(dumneavoastr\p{L}*|vă|vi|v-a|v-ați|ați|puteți|aveți|sunteți|doriți|vedeți|alegeți|introduceți|încercați|apăsați|deschideți|verificați|copiați|ștergeți|salvați|reveniți)(?!\p{L})/iu;
    const offenders = all.filter(([, text]) => polite.test(text)).map(([key]) => key);

    expect(offenders).toEqual([]);
  });

  it('calls a centerline "drumuire" and a line plot "model de drumuire"', () => {
    const offenders = all
      .filter(([, text]) => /poligona(ți|l)|plan(ul|uri|urile|ului)? de linii/iu.test(text))
      .map(([key]) => key);

    expect(offenders).toEqual([]);
    expect(ro.features.kinds.centerline).toBe('Drumuire');
  });

  it('calls a feature "obiect" and its kind "tip de obiect"', () => {
    // "Element", "fenomen" and "obiectiv" all stood for the same record on the map. A surface
    // feature keeps its karst name, "fenomen de suprafață"; "element" stays for a member of a
    // link, which is not a feature.
    const offenders = all
      .filter(([key, text]) => {
        const aboutLinkItems = key.startsWith('resLinks.') || key.startsWith('admin.relationTypes.');
        return (
          /(?<!\p{L})fenomen(?!(e|ul|ului|elor|ele)? de suprafa)/iu.test(text) ||
          /tip(ul|uri|urile)? de fenomen/iu.test(text) ||
          (!aboutLinkItems && /tip(ul|uri|urile)? de element/iu.test(text)) ||
          (!aboutLinkItems &&
            /(?<!\p{L})element(e|ul|ului|elor|ele)?(?!\p{L})/iu.test(text) &&
            /\bfeature/i.test(englishOf(key)))
        );
      })
      .map(([key]) => key);

    expect(offenders).toEqual([]);
    expect(ro.admin.featureTypes.title).toBe('Tipuri de obiecte');
  });

  it('calls the set of caves the "registru", keeping "cadastral" for land', () => {
    // The national cave cadastre is the name of an outside register and keeps its own word.
    const offenders = all
      .filter(([key, text]) => key !== 'externalIds.systems.national_cadastre' && /cadastr(?!al)/iu.test(text))
      .map(([key]) => key);

    expect(offenders).toEqual([]);
  });

  it('names what is kept in one browser with one label', () => {
    expect(ro.panel.layoutKeptHere).toBe('Doar în acest browser');
    expect(ro.settings.accessibility.deviceOnly).toBe('Doar în acest browser');
    expect(all.filter(([, text]) => /Doar acest browser/u.test(text)).map(([key]) => key)).toEqual([]);
  });
});
