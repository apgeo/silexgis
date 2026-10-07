// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { markerLabelTheme } from './markerLabelTheme.ts';

/**
 * Every source file's text, read through the bundler rather than the filesystem so this stays
 * inside the app's own module world (the browser type project has no `node:fs`).
 */
const sourceText = import.meta.glob('../**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const isTest = (path: string) => /\.test\.tsx?$/.test(path);
const sources = Object.entries(sourceText).filter(([path]) => !isTest(path));

describe('markerLabelTheme', () => {
  it('adds nothing for the viewer’s own plate, and names a dark one in the viewer’s own words', () => {
    expect(markerLabelTheme('derived')).toBeUndefined();
    expect(markerLabelTheme('dark')).toEqual({
      liveMarkers: { labelBackground: '#141414', labelText: '#ffffff', labelBackgroundOpacity: 0.8 },
    });
  });

  it('is asked for the dark plate by the movie preview and by nothing else', () => {
    const asking = sources.filter(([, text]) => /markerLabelTheme\(\s*['"]dark['"]/.test(text)).map(([path]) => path);
    expect(asking).toEqual(['../components/caveview/movie/MoviePreviewHost.tsx']);
  });

  it('is an option of the shared viewer panel that no screen passes', () => {
    // The panel is what the signed-in tracking view and both published pages draw a party on. What
    // those show is not this option's to change: it exists, and whether anything uses it is a
    // decision about what members and visitors see. A screen that starts passing it — by name or
    // by spreading props that carry it — fails here, on purpose.
    const mounting = sources.filter(([, text]) => /<CaveViewPanel\b/.test(text)).map(([path]) => path);
    // The scan is known to see the screens it speaks for.
    expect(mounting).toEqual(
      expect.arrayContaining([
        '../components/trips/TrackingModelPanel.tsx',
        '../pages/public/PublicTripPage.tsx',
        '../pages/public/PublicTripEmbedPage.tsx',
      ]),
    );
    const naming = sources.filter(([, text]) => /\bmarkerLabels\b/.test(text)).map(([path]) => path);
    expect(naming).toEqual(['../components/caveview/CaveViewPanel.tsx']);
    // And nothing reads the type the option is spelled in, which is how a wrapper would forward it.
    const typed = sources.filter(([, text]) => /\bMarkerLabelPlate\b/.test(text)).map(([path]) => path);
    expect(typed.sort()).toEqual(['../components/caveview/CaveViewPanel.tsx', './markerLabelTheme.ts'].sort());
  });
});
