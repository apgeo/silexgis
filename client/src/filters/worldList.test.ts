// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { emptyDocument, withWhere } from './document.ts';
import type { FilterNode } from './types.ts';
import { decodeFilter } from './url.ts';
import {
  readWorldDocument,
  readWorldFilter,
  sameWorldQuestion,
  worldDocument,
  worldEditorDocument,
  worldToken,
  writeWorldFilter,
} from './worldList.ts';

const FORMAT_IS_GPX: FilterNode = {
  node: 'condition', field: 'format', op: 'equals', values: [{ type: 'id', value: 'gpx' }],
};

describe('a list filter in the address', () => {
  it('round-trips the conditions and the saved filter they came from, keeping the tab', () => {
    const written = writeWorldFilter(new URLSearchParams({ tab: 'raster' }), 'geofile', {
      where: FORMAT_IS_GPX,
      savedFilterId: 'f-1',
    });

    expect(written.get('tab')).toBe('raster');
    expect(readWorldFilter(written, 'geofile')).toEqual({
      where: FORMAT_IS_GPX, unreadableLink: false, savedFilterId: 'f-1',
    });
  });

  it('leaves no token behind when the conditions narrow nothing', () => {
    const before = writeWorldFilter(new URLSearchParams(), 'geofile', { where: FORMAT_IS_GPX, savedFilterId: 'f-1' });
    const cleared = writeWorldFilter(before, 'geofile', { where: null });

    expect(cleared.has('filter')).toBe(false);
    expect(cleared.has('saved')).toBe(false);
    expect(worldToken('geofile', null)).toBeUndefined();
    expect(worldDocument('geofile', null)).toBeNull();
  });

  it('writes a document about its own world and nothing else', () => {
    expect(decodeFilter(worldToken('geofile', FORMAT_IS_GPX))!.scope).toEqual([
      { world: 'geofile', where: FORMAT_IS_GPX },
    ]);
  });

  it('says a link was unreadable rather than opening it as though it were read', () => {
    expect(readWorldFilter(new URLSearchParams({ filter: 'not-a-token' }), 'geofile')).toMatchObject({
      where: null, unreadableLink: true,
    });
  });

  it('does not take a filter about another world as its own, and says so', () => {
    const aboutMaps = withWhere(emptyDocument(['georeferencedMap']), 'georeferencedMap', FORMAT_IS_GPX);

    expect(readWorldDocument(aboutMaps, 'geofile')).toBeUndefined();
    const params = writeWorldFilter(new URLSearchParams(), 'georeferencedMap', { where: FORMAT_IS_GPX });
    expect(readWorldFilter(params, 'geofile')).toMatchObject({ where: null, unreadableLink: true });
  });

  it('opens the editor on an empty document for the world when there are no conditions', () => {
    expect(worldEditorDocument('geofile', null)).toEqual(emptyDocument(['geofile']));
    expect(worldEditorDocument('geofile', FORMAT_IS_GPX).scope[0].where).toEqual(FORMAT_IS_GPX);
  });

  it('compares two sets of conditions by what they say', () => {
    expect(sameWorldQuestion(FORMAT_IS_GPX, { ...FORMAT_IS_GPX })).toBe(true);
    expect(sameWorldQuestion(FORMAT_IS_GPX, null)).toBe(false);
  });
});
