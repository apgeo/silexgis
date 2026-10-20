// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import { emptyDocument, withWhere } from './document.ts';
import {
  galleryDocument,
  galleryToken,
  galleryWhere,
  PHOTO_WORLD,
  readGalleryControls,
  readGalleryDocument,
  readGalleryFilter,
  sameGalleryQuestion,
  writeGalleryFilter,
} from './gallery.ts';
import type { FilterNode } from './types.ts';
import { decodeFilter, encodeFilter } from './url.ts';

const TITLED: FilterNode = {
  node: 'condition',
  field: 'title',
  op: 'contains',
  values: [{ type: 'text', value: 'entrance' }],
};

describe('the gallery filter in the address', () => {
  it('round-trips the conditions and the saved filter they came from, keeping the controls', () => {
    const before = new URLSearchParams({ caverId: 'c-1', bbox: '1,2,3,4' });
    const written = writeGalleryFilter(before, { where: TITLED, savedFilterId: 'f-1' });

    expect(written.get('caverId')).toBe('c-1');
    expect(written.get('bbox')).toBe('1,2,3,4');
    expect(readGalleryFilter(written)).toEqual({ where: TITLED, unreadableLink: false, savedFilterId: 'f-1' });
  });

  it('leaves no token behind when the conditions narrow nothing', () => {
    const before = writeGalleryFilter(new URLSearchParams(), { where: TITLED, savedFilterId: 'f-1' });
    const cleared = writeGalleryFilter(before, { where: null });

    expect(cleared.has('filter')).toBe(false);
    expect(cleared.has('saved')).toBe(false);
    expect(galleryToken(null)).toBeUndefined();
    expect(galleryDocument(null)).toBeNull();
  });

  it('writes a document about photographs and nothing else', () => {
    const document = decodeFilter(galleryToken(TITLED))!;
    expect(document.scope).toEqual([{ world: PHOTO_WORLD, where: TITLED }]);
  });

  it('says a link was unreadable rather than opening it as though it were read', () => {
    expect(readGalleryFilter(new URLSearchParams({ filter: 'not-a-token' }))).toMatchObject({
      where: null,
      unreadableLink: true,
    });
  });

  it('does not take a filter about documents as one about photographs', () => {
    const aboutDocuments = withWhere(emptyDocument(['document']), 'document', TITLED);

    expect(readGalleryDocument(aboutDocuments)).toBeUndefined();
    expect(readGalleryFilter(new URLSearchParams({ filter: encodeFilter(aboutDocuments) }))).toMatchObject({
      where: null,
      unreadableLink: true,
    });
  });

  it('reads an empty address as no filter and no complaint', () => {
    expect(readGalleryFilter(new URLSearchParams())).toEqual({
      where: null,
      unreadableLink: false,
      savedFilterId: undefined,
    });
  });

  it('tells apart two sets of conditions that ask different questions', () => {
    expect(sameGalleryQuestion(TITLED, { ...TITLED })).toBe(true);
    expect(sameGalleryQuestion(TITLED, null)).toBe(false);
  });
});

describe('the gallery controls as conditions', () => {
  // Node re-reads the zone when TZ is assigned. "Etc/GMT-3" is UTC+3 and "Etc/GMT+5" is UTC−5.
  const originalZone = process.env.TZ;
  afterEach(() => {
    if (originalZone === undefined) delete process.env.TZ;
    else process.env.TZ = originalZone;
  });

  it('writes each control as the condition that matches what the control always matched', () => {
    process.env.TZ = 'Etc/GMT-3';
    expect(galleryWhere({ search: ' cascade ', tag: 'entrance', from: '2024-03-01', to: '2024-03-31' })).toEqual({
      node: 'allOf',
      of: [
        {
          node: 'anyOf',
          of: [
            { node: 'condition', field: 'title', op: 'contains', values: [{ type: 'text', value: 'cascade' }] },
            { node: 'condition', field: 'caption', op: 'contains', values: [{ type: 'text', value: 'cascade' }] },
            { node: 'condition', field: 'placeName', op: 'contains', values: [{ type: 'text', value: 'cascade' }] },
          ],
        },
        { node: 'condition', field: 'tag', op: 'equals', values: [{ type: 'id', value: 'entrance' }] },
        // Taken, else uploaded, from the first day's start up to the start of the day after the
        // last — the reader's own days, as the condition editor bounds a day on the same field.
        { node: 'condition', field: 'datedAt', op: 'greaterOrEqual', values: [{ type: 'instant', value: '2024-03-01T00:00:00.000+03:00' }] },
        { node: 'condition', field: 'datedAt', op: 'lessThan', values: [{ type: 'instant', value: '2024-04-01T00:00:00.000+03:00' }] },
      ],
    });
  });

  it.each([['UTC+3', 'Etc/GMT-3'], ['UTC−5', 'Etc/GMT+5']])(
    'bounds the range by the reader\'s own days at %s and reads it back as those days',
    (_name, zone) => {
      process.env.TZ = zone;
      const where = galleryWhere({ from: '2024-12-31', to: '2025-01-01' });
      const [from, to] = (where as Extract<FilterNode, { node: 'allOf' }>).of
        .map((n) => ((n as Extract<FilterNode, { node: 'condition' }>).values[0] as { value: string }).value);

      // As moments, the reader's midnight of the first day and of the day after the last.
      expect(new Date(from!).getTime()).toBe(new Date(2024, 11, 31).getTime());
      expect(new Date(to!).getTime()).toBe(new Date(2025, 0, 2).getTime());
      expect(readGalleryControls(where).controls).toEqual({ from: '2024-12-31', to: '2025-01-01' });
    },
  );

  it('still reads a range written at midnight UTC, as older links carry it, as its days', () => {
    const old: FilterNode = {
      node: 'allOf',
      of: [
        { node: 'condition', field: 'datedAt', op: 'greaterOrEqual', values: [{ type: 'instant', value: '2024-03-01T00:00:00Z' }] },
        { node: 'condition', field: 'datedAt', op: 'lessThan', values: [{ type: 'instant', value: '2024-04-01T00:00:00Z' }] },
      ],
    };
    expect(readGalleryControls(old)).toEqual({ controls: { from: '2024-03-01', to: '2024-03-31' }, extra: [] });
  });

  it('narrows nothing when no control is set', () => {
    expect(galleryWhere({})).toBeNull();
    expect(galleryWhere({ search: '   ' })).toBeNull();
    expect(readGalleryControls(null)).toEqual({ controls: {}, extra: [] });
  });

  it('reads its own conditions back into the controls and keeps everything else as extra', () => {
    const controls = { search: 'cascade', tag: 'entrance', from: '2024-12-30', to: '2024-12-31' };
    const where = galleryWhere(controls, [TITLED]);

    expect(readGalleryControls(where)).toEqual({ controls, extra: [TITLED] });
  });

  it('keeps a second condition of a control as extra, where it still narrows', () => {
    const tag = (slug: string): FilterNode => ({
      node: 'condition', field: 'tag', op: 'equals', values: [{ type: 'id', value: slug }],
    });
    const where: FilterNode = { node: 'allOf', of: [tag('a'), tag('b')] };

    expect(readGalleryControls(where)).toEqual({ controls: { tag: 'a' }, extra: [tag('b')] });
  });

  it('does not take a date condition that is not at a day boundary as the range control', () => {
    const later: FilterNode = {
      node: 'condition', field: 'datedAt', op: 'greaterOrEqual', values: [{ type: 'instant', value: '2024-03-01T12:00:00Z' }],
    };

    expect(readGalleryControls(later)).toEqual({ controls: {}, extra: [later] });
  });

  it('treats a bare condition and the same condition in an all-of as one question', () => {
    expect(sameGalleryQuestion(TITLED, { node: 'allOf', of: [TITLED] })).toBe(true);
    expect(sameGalleryQuestion(galleryWhere({ tag: 'a' }), galleryWhere({ tag: 'b' }))).toBe(false);
  });
});
