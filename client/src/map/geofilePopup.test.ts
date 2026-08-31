// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { geofilePopupNodes } from './geofilePopup.ts';
import {
  GEOFILE_DESCRIPTION_PROPERTY,
  GEOFILE_ELEVATION_PROPERTY,
  GEOFILE_LABEL_PROPERTY,
  geofileLabel,
} from './geofileProperties.ts';

/** The rendered popup as one string, which is what these assertions are actually about. */
function render(props: Record<string, unknown>): HTMLElement {
  const host = document.createElement('div');
  host.replaceChildren(...geofilePopupNodes(props));
  return host;
}

describe('geofileLabel', () => {
  it('reads the name the server resolved', () => {
    expect(geofileLabel({ [GEOFILE_LABEL_PROPERTY]: 'Peștera Ursilor' })).toBe('Peștera Ursilor');
  });

  it('treats a blank or missing name as no name', () => {
    // A GPX waypoint with an empty <name> is ordinary, and a tooltip reading "" is a grey box
    // following the cursor with nothing in it.
    expect(geofileLabel({})).toBeUndefined();
    expect(geofileLabel({ [GEOFILE_LABEL_PROPERTY]: '   ' })).toBeUndefined();
  });

  it("ignores the row's own column of the same bare name", () => {
    // The prefix is the whole point: a source with a column called `label` must not be able to
    // rename somebody else's waypoint by having one.
    expect(geofileLabel({ label: 'not this' })).toBeUndefined();
  });
});

describe('geofilePopupNodes', () => {
  it('shows the name, description and altitude the server resolved', () => {
    const host = render({
      [GEOFILE_LABEL_PROPERTY]: 'Avenul din Şesuri',
      [GEOFILE_DESCRIPTION_PROPERTY]: 'Entrance shaft, rigged 2026',
      [GEOFILE_ELEVATION_PROPERTY]: 1152.4,
    });

    expect(host.querySelector('.map-geofile-popup-title')?.textContent).toBe('Avenul din Şesuri');
    expect(host.querySelector('.map-geofile-popup-description')?.textContent).toBe('Entrance shaft, rigged 2026');
    // Rounded: a tenth of a metre is finer than a handheld GPS knows, and printing it claims a
    // precision the reading does not have.
    expect(host.querySelector('.map-geofile-popup-elevation')?.textContent).toBe('1152 m');
  });

  it('lists the columns the source file carried', () => {
    // This is what the popup is FOR. Before it, the only way to see what a GPS unit recorded
    // beside a waypoint was to open the import review screen for the file it came from.
    const host = render({
      [GEOFILE_LABEL_PROPERTY]: 'WPT0142',
      sym: 'Cave',
      time: '2026-07-14T09:12:00Z',
      hdop: 3.4,
    });

    const terms = Array.from(host.querySelectorAll('dt')).map((n) => n.textContent);
    const values = Array.from(host.querySelectorAll('dd')).map((n) => n.textContent);
    expect(terms).toEqual(['sym', 'time', 'hdop']);
    expect(values).toEqual(['Cave', '2026-07-14T09:12:00Z', '3.4']);
  });

  it('does not repeat the resolved values among the columns', () => {
    const host = render({
      [GEOFILE_LABEL_PROPERTY]: 'A',
      [GEOFILE_DESCRIPTION_PROPERTY]: 'B',
      [GEOFILE_ELEVATION_PROPERTY]: 12,
      id: 99,
    });

    expect(host.querySelectorAll('dt')).toHaveLength(0);
  });

  it('hides the source columns the resolved values were read out of', () => {
    // Measured on an ordinary GPX: the title and the altitude were each printed a second time,
    // as the `name` and `ele` columns they came from. WHICH column that was is the server's
    // decision and is not knowable here by name — but a column whose value is already on screen
    // a line above adds nothing, whatever it is called.
    const host = render({
      [GEOFILE_LABEL_PROPERTY]: 'WPT0143',
      [GEOFILE_ELEVATION_PROPERTY]: 861,
      name: 'WPT0143',
      ele: 861,
      sym: 'Flag, Blue',
    });

    expect(Array.from(host.querySelectorAll('dt')).map((n) => n.textContent)).toEqual(['sym']);
  });

  it("never interprets a file's text as markup", () => {
    // The strings here come from an uploaded file, which is the least trustworthy text in the
    // application. A waypoint named with a script tag is a stored cross-site scripting attempt
    // that would fire for every viewer who clicked near it.
    const host = render({
      [GEOFILE_LABEL_PROPERTY]: '<img src=x onerror=alert(1)>',
      note: '<script>alert(2)</script>',
    });

    expect(host.querySelector('img')).toBeNull();
    expect(host.querySelector('script')).toBeNull();
    expect(host.querySelector('.map-geofile-popup-title')?.textContent).toBe('<img src=x onerror=alert(1)>');
  });

  it('shows a row with no name at all rather than nothing', () => {
    // An unnamed row is exactly the row somebody needs to look inside, so the balloon still opens
    // and still lists what the file recorded.
    const host = render({ sym: 'Flag, Blue' });

    expect(host.querySelector('.map-geofile-popup-title')?.textContent).toBe('—');
    expect(Array.from(host.querySelectorAll('dt')).map((n) => n.textContent)).toEqual(['sym']);
  });

  it('skips empty columns and renders structured ones as JSON', () => {
    const host = render({ blank: '', missing: null, nested: { a: 1 } });

    expect(Array.from(host.querySelectorAll('dt')).map((n) => n.textContent)).toEqual(['nested']);
    // Not "[object Object]", which is what a nested attribute from a GeoJSON import would
    // otherwise read as.
    expect(host.querySelector('dd')?.textContent).toBe('{"a":1}');
  });
});
