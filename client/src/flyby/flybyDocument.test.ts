// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  FLYBY_VERSION,
  flybyHoldsPlace,
  flybyLength,
  flybyProblems,
  normalizeFlybyDocument,
  sectionLength,
  type FlybyDocument,
} from './flybyDocument.ts';
import { buildPreset, DEFAULT_FLYBY_PRESET_OPTIONS } from './flybyPresets.ts';
import {
  flybyFileName,
  flybyFileText,
  normalizeFlybyLibrary,
  parseFlybyFile,
  withFlybySaved,
  withoutFlyby,
  FLYBY_LIBRARY_LIMIT,
} from './flybyLibrary.ts';

function flyby(overrides: Partial<FlybyDocument> = {}): FlybyDocument {
  return {
    version: FLYBY_VERSION,
    name: 'Around the cave',
    speed: 1,
    loop: false,
    subjects: [{ kind: 'cave', caveId: 'c1' }],
    start: { subject: 0, pose: { heading: 0, pitch: -20, range: 6, lift: 0, clearance: 60 } },
    sections: [
      { id: 'approach-1', kind: 'approach', subject: 0, seconds: 6, to: { range: 3 } },
      { id: 'orbit-2', kind: 'orbit', subject: 0, seconds: 24, to: { headingBy: 360 } },
    ],
    ...overrides,
  };
}

describe('a flyby document', () => {
  it('finds nothing wrong with one that can be played', () => {
    expect(flybyProblems(flyby())).toEqual([]);
  });

  it('names every reason one cannot be played', () => {
    const broken = flyby({
      speed: 9,
      sections: [
        { id: 'a', kind: 'orbit', subject: 3, seconds: 0, to: { range: 0, pitch: -120 } },
        { id: 'a', kind: 'hold', subject: 0, seconds: 5, speed: 0, to: {} },
      ],
    });
    expect(flybyProblems(broken)).toEqual([
      { code: 'badSpeed' },
      { code: 'unknownSubject', section: 'a' },
      { code: 'badSeconds', section: 'a' },
      { code: 'badRange', section: 'a' },
      { code: 'badPitch', section: 'a' },
      { code: 'duplicateSection', section: 'a' },
      { code: 'badSpeed', section: 'a' },
    ]);
    expect(flybyProblems(flyby({ subjects: [], sections: [] }))).toEqual([
      { code: 'noSubjects' },
      { code: 'noSections' },
      { code: 'unknownSubject' },
    ]);
  });

  it('lasts as long as its sections at its own speed and theirs', () => {
    const document = flyby({
      speed: 2,
      sections: [
        { id: 'a', kind: 'approach', subject: 0, seconds: 6, to: {} },
        { id: 'b', kind: 'orbit', subject: 0, seconds: 24, speed: 0.5, to: { headingBy: 360 } },
      ],
    });
    expect(sectionLength(document, document.sections[0])).toBe(3);
    expect(sectionLength(document, document.sections[1])).toBe(24);
    expect(flybyLength(document)).toBe(27);
  });

  it('holds a place only when a subject was pinned', () => {
    expect(flybyHoldsPlace(flyby())).toBe(false);
    expect(
      flybyHoldsPlace(flyby({ subjects: [{ kind: 'place', longitude: 25, latitude: 45, radiusM: 500 }] })),
    ).toBe(true);
  });

  it('comes back from its own file as it went in', () => {
    const document = flyby({ preset: 'aroundCave', loop: true });
    expect(parseFlybyFile(flybyFileText(document))).toEqual(document);
    // And with every part a preset can write: the underground visit names the places it looks at.
    const underground = buildPreset('tour', [{ kind: 'cave', caveId: 'a' }, { kind: 'cave', caveId: 'b' }], {
      ...DEFAULT_FLYBY_PRESET_OPTIONS,
      underground: true,
    }, 'Tour');
    expect(parseFlybyFile(flybyFileText(underground))).toEqual(underground);
  });

  it('is refused whole when any part of a stored one is not a flyby', () => {
    const good = JSON.parse(JSON.stringify(flyby())) as Record<string, unknown>;
    expect(normalizeFlybyDocument(good)).not.toBeNull();
    expect(normalizeFlybyDocument({ ...good, version: 2 })).toBeNull();
    expect(normalizeFlybyDocument({ ...good, subjects: [{ kind: 'cave' }] })).toBeNull();
    expect(normalizeFlybyDocument({ ...good, subjects: [{ kind: 'place', longitude: 400, latitude: 0, radiusM: 5 }] })).toBeNull();
    expect(normalizeFlybyDocument({ ...good, sections: [{ id: 'x', kind: 'loop', subject: 0, seconds: 5, to: {} }] })).toBeNull();
    expect(normalizeFlybyDocument({ ...good, sections: [{ id: 'x', kind: 'orbit', subject: 0, seconds: 5, to: { pitch: 'down' } }] })).toBeNull();
    expect(normalizeFlybyDocument({ ...good, sections: [{ id: 'x', kind: 'dive', subject: 0, seconds: 5, to: { aim: 'sideways' } }] })).toBeNull();
    expect(normalizeFlybyDocument({ ...good, start: { subject: 0, pose: { pitch: -20, range: 3 } } })).toBeNull();
    expect(normalizeFlybyDocument('flyby')).toBeNull();
    expect(parseFlybyFile('{not json')).toBeNull();
  });

  it('carries nothing a stranger added to a file', () => {
    const stored = { ...JSON.parse(JSON.stringify(flyby())), secret: 'x', subjects: [{ kind: 'cave', caveId: 'c1', name: 'Hidden' }] };
    const read = normalizeFlybyDocument(stored);
    expect(read).toEqual(flyby());
    expect(JSON.stringify(read)).not.toContain('Hidden');
  });
});

describe('the flybys a person has kept', () => {
  it('replaces the one of the same name and puts the newest first', () => {
    const first = flyby({ name: 'One' });
    const second = flyby({ name: 'Two' });
    const library = withFlybySaved(withFlybySaved([], first), second);
    expect(library.map((kept) => kept.name)).toEqual(['Two', 'One']);
    const again = withFlybySaved(library, flyby({ name: 'One', speed: 2 }));
    expect(again.map((kept) => [kept.name, kept.speed])).toEqual([
      ['One', 2],
      ['Two', 1],
    ]);
    expect(withoutFlyby(again, 'One').map((kept) => kept.name)).toEqual(['Two']);
  });

  it('keeps no more than its limit', () => {
    let library: FlybyDocument[] = [];
    for (let index = 0; index < FLYBY_LIBRARY_LIMIT + 5; index += 1) {
      library = withFlybySaved(library, flyby({ name: `Flyby ${index}` }));
    }
    expect(library).toHaveLength(FLYBY_LIBRARY_LIMIT);
    expect(library[0].name).toBe(`Flyby ${FLYBY_LIBRARY_LIMIT + 4}`);
  });

  it('reads what was stored and leaves out what is not a flyby', () => {
    const stored = [flyby({ name: 'Kept' }), { nonsense: true }, flyby({ name: '' }), flyby({ name: 'Kept' })];
    expect(normalizeFlybyLibrary(JSON.parse(JSON.stringify(stored))).map((kept) => kept.name)).toEqual(['Kept']);
    expect(normalizeFlybyLibrary('no')).toEqual([]);
  });

  it('names a file by the flyby, safely', () => {
    expect(flybyFileName({ name: 'Peștera Urșilor — tur!' })).toBe('pestera-ursilor-tur.flyby.json');
    expect(flybyFileName({ name: '///' })).toBe('flyby.flyby.json');
  });
});

describe('the lens in a document', () => {
  it('is read back as written, and refused outside what a lens can be', async () => {
    const { normalizeFlybyDocument, flybyProblems } = await import('./flybyDocument.ts');
    const { buildPreset } = await import('./flybyPresets.ts');
    const document = {
      ...buildPreset('aroundCave', [{ kind: 'cave', caveId: 'a' }]),
      look: { lens: 35 },
      changes: [{ id: 'lens-1', kind: 'lens' as const, degrees: 90, section: 'orbit-2', at: 0, seconds: 3 }],
    };
    expect(normalizeFlybyDocument(JSON.parse(JSON.stringify(document)))).toEqual(document);
    expect(flybyProblems({ ...document, look: { lens: 3 } })).toContainEqual({ code: 'badLook' });
    expect(flybyProblems({ ...document, changes: [{ ...document.changes[0], degrees: 120 }] })).toContainEqual({
      code: 'badChange',
      change: 'lens-1',
      section: 'orbit-2',
    });
  });
});
