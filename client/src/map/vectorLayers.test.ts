// SPDX-License-Identifier: AGPL-3.0-or-later
import VectorLayer from 'ol/layer/Vector';
import VectorTileLayer from 'ol/layer/VectorTile';
import VectorSource from 'ol/source/Vector';
import VectorTileSource from 'ol/source/VectorTile';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const applyStyle = vi.fn();
const applyBackground = vi.fn();
vi.mock('ol-mapbox-style', () => ({
  applyStyle: (...args: unknown[]) => applyStyle(...args),
  applyBackground: (...args: unknown[]) => applyBackground(...args),
}));

const getHeader = vi.fn();
vi.mock('pmtiles', () => ({
  PMTiles: class {
    readonly url: string;
    constructor(url: string) {
      this.url = url;
    }
    getHeader() {
      return getHeader(this.url);
    }
  },
}));

// The real source reads the archive as soon as it is made; this one only says what it was given.
vi.mock('ol-pmtiles', async () => {
  const { default: Source } = await import('ol/source/VectorTile');
  return {
    PMTilesVectorSource: class extends Source {
      readonly archive: string;
      constructor(options: { url: string; maxZoom?: number; attributions?: string }) {
        super({ url: 'pmtiles://{z}/{x}/{y}', maxZoom: options.maxZoom, attributions: options.attributions });
        this.archive = options.url;
      }
    },
  };
});

const { createVectorLayer, localiseStyle, MAP_FONT_TEMPLATE, vectorZoomBand } = await import('./vectorLayers.ts');
type VectorEntry = Parameters<typeof createVectorLayer>[0];

const BASE = 'https://silexgis.example.invalid/map';

function entry(
  overrides: Omit<Partial<VectorEntry>, 'vector'> & { vector?: Partial<VectorEntry['vector']> } = {},
): VectorEntry {
  const { vector, ...rest } = overrides;
  return {
    urlTemplate: 'https://tiles.example.invalid/{z}/{x}/{y}.pbf',
    attribution: '© OpenStreetMap contributors',
    minZoom: 0,
    maxZoom: 14,
    isBase: true,
    ...rest,
    vector: { format: 'mvt', style: null, styleSource: null, labelField: null, scene3dStandIn: null, ...vector },
  };
}

/** A fetch that answers each address from the table, and 404 for anything else. */
function fetchFrom(documents: Record<string, unknown>) {
  return vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    return url in documents
      ? new Response(JSON.stringify(documents[url]), { status: 200 })
      : new Response('not here', { status: 404 });
  }) as unknown as typeof fetch & ReturnType<typeof vi.fn>;
}

function tileUrls(layer: VectorTileLayer | VectorLayer): string[] | undefined {
  return (layer.getSource() as VectorTileSource | null)?.getUrls() ?? undefined;
}

beforeEach(() => {
  applyStyle.mockReset().mockResolvedValue(undefined);
  applyBackground.mockReset().mockResolvedValue(undefined);
  getHeader.mockReset().mockResolvedValue({ minZoom: 0, maxZoom: 15 });
});

describe('a vector catalogue entry drawn on the flat map', () => {
  it('draws a tile template straight away, up to the last level the tiles hold', async () => {
    const { layer, ready } = createVectorLayer(entry({ minZoom: 3, maxZoom: 12 }), {
      language: 'ro',
      zIndex: 0.5,
      baseUrl: BASE,
      fetch: fetchFrom({}),
    });

    await expect(ready).resolves.toBe(true);
    expect(layer).toBeInstanceOf(VectorTileLayer);
    expect(tileUrls(layer)).toEqual(['https://tiles.example.invalid/{z}/{x}/{y}.pbf']);
    expect((layer.getSource() as VectorTileSource).getTileGrid()?.getMaxZoom()).toBe(12);
    expect(layer.getMinZoom()).toBe(3);
    expect(layer.getZIndex()).toBe(0.5);
    expect(layer.getVisible()).toBe(false);
    // Labels kept upright as the map turns, and never drawn over one another.
    // (OpenLayers names the declutter group; any group means decluttered.)
    expect((layer as VectorTileLayer).getDeclutter()).toBeTruthy();
    expect(applyStyle).not.toHaveBeenCalled();
  });

  it('finds this build’s tiles in a TileJSON document, and says so when it cannot', async () => {
    const tileJson = 'https://tiles.example.invalid/planet';
    const fetchDocument = fetchFrom({ [tileJson]: { tiles: ['20261020_001001_pt/{z}/{x}/{y}.pbf'], maxzoom: 14 } });
    const found = createVectorLayer(entry({ urlTemplate: tileJson }), { language: 'en', baseUrl: BASE, fetch: fetchDocument });

    await expect(found.ready).resolves.toBe(true);
    expect(fetchDocument).toHaveBeenCalledWith(tileJson);
    // Read against the document's own address.
    expect(tileUrls(found.layer)).toEqual(['https://tiles.example.invalid/20261020_001001_pt/{z}/{x}/{y}.pbf']);

    const missing = createVectorLayer(entry({ urlTemplate: 'https://tiles.example.invalid/gone' }), {
      language: 'en',
      baseUrl: BASE,
      fetch: fetchFrom({}),
    });
    await expect(missing.ready).resolves.toBe(false);
    expect(missing.layer.getSource()).toBeNull();
  });

  it('reads a PMTiles archive once its header has come back, and draws nothing when it does not', async () => {
    const archive = entry({ urlTemplate: '/basemap/romania.pmtiles', maxZoom: 15, vector: { format: 'pmtiles' } });
    const read = createVectorLayer(archive, { language: 'ro', baseUrl: BASE, fetch: fetchFrom({}) });

    await expect(read.ready).resolves.toBe(true);
    // A relative address is the installation's own, read against the page.
    expect(getHeader).toHaveBeenCalledWith('https://silexgis.example.invalid/basemap/romania.pmtiles');
    expect((read.layer.getSource() as unknown as { archive: string }).archive).toBe(
      'https://silexgis.example.invalid/basemap/romania.pmtiles',
    );

    getHeader.mockRejectedValueOnce(new Error('404'));
    const absent = createVectorLayer(archive, { language: 'ro', baseUrl: BASE, fetch: fetchFrom({}) });
    await expect(absent.ready).resolves.toBe(false);
    expect(absent.layer.getSource()).toBeNull();
  });

  it('draws a GeoJSON file as one vector layer', async () => {
    const { layer, ready } = createVectorLayer(
      entry({ urlTemplate: 'https://files.example.invalid/natura.geojson', isBase: false, vector: { format: 'geojson' } }),
      { language: 'ro', baseUrl: BASE, fetch: fetchFrom({}) },
    );

    await expect(ready).resolves.toBe(true);
    expect(layer).toBeInstanceOf(VectorLayer);
    expect(layer.getSource()).toBeInstanceOf(VectorSource);
    expect((layer.getSource() as VectorSource).getUrl()).toBe('https://files.example.invalid/natura.geojson');
  });

  it('applies the style’s layers to the entry’s own data, in the application’s fonts, and paints a basemap’s background', async () => {
    const styleUrl = 'https://tiles.example.invalid/styles/liberty';
    const style = {
      version: 8,
      sources: { openmaptiles: { type: 'vector', url: 'https://elsewhere.invalid/tiles' } },
      layers: [
        { id: 'town', type: 'symbol', layout: { 'text-field': ['get', 'name:latin'], 'text-font': ['Noto Sans Bold'] } },
      ],
    };
    const { layer, ready } = createVectorLayer(
      entry({ vector: { style: styleUrl, styleSource: 'openmaptiles', labelField: 'name:{lang}' } }),
      { language: 'ro', baseUrl: BASE, fetch: fetchFrom({ [styleUrl]: style }) },
    );

    await expect(ready).resolves.toBe(true);
    expect(applyStyle).toHaveBeenCalledTimes(1);
    const [styledLayer, applied, options] = applyStyle.mock.calls[0];
    expect(styledLayer).toBe(layer);
    // The style's own source is never used to fetch the data: the entry's address is.
    expect(options).toEqual({ source: 'openmaptiles', updateSource: false, styleUrl, webfonts: MAP_FONT_TEMPLATE });
    expect(applied.layers[0].layout).toEqual({
      'text-field': ['coalesce', ['get', 'name:ro'], ['get', 'name']],
      'text-font': ['Noto Sans Bold'],
    });
    expect(tileUrls(layer)).toEqual(['https://tiles.example.invalid/{z}/{x}/{y}.pbf']);
    expect(applyBackground).toHaveBeenCalledWith(layer, applied, { styleUrl });
  });

  it('leaves an overlay’s background alone, and draws the plain default look when the style does not arrive or does not apply', async () => {
    const styleUrl = '/map-styles/light.json';
    const overlay = entry({ isBase: false, vector: { style: styleUrl } });
    const ok = createVectorLayer(overlay, {
      language: 'ro',
      baseUrl: BASE,
      fetch: fetchFrom({ 'https://silexgis.example.invalid/map-styles/light.json': { version: 8, layers: [] } }),
    });
    await expect(ok.ready).resolves.toBe(true);
    expect(applyBackground).not.toHaveBeenCalled();

    const unreachable = createVectorLayer(overlay, { language: 'ro', baseUrl: BASE, fetch: fetchFrom({}) });
    await expect(unreachable.ready).resolves.toBe(false);
    // The data is still drawn.
    expect(unreachable.layer.getSource()).not.toBeNull();

    applyStyle.mockRejectedValueOnce(new Error('Nothing to show for source []'));
    const unusable = createVectorLayer(overlay, {
      language: 'ro',
      baseUrl: BASE,
      fetch: fetchFrom({ 'https://silexgis.example.invalid/map-styles/light.json': { version: 8, layers: [] } }),
    });
    await expect(unusable.ready).resolves.toBe(false);
  });

  it('can wait to be shown before it fetches anything at all', async () => {
    const tileJson = 'https://tiles.example.invalid/planet';
    const styleUrl = 'https://tiles.example.invalid/styles/liberty';
    const fetchDocument = fetchFrom({ [tileJson]: { tiles: ['t/{z}/{x}/{y}.pbf'] }, [styleUrl]: { version: 8, layers: [] } });
    const { layer, ready } = createVectorLayer(entry({ urlTemplate: tileJson, vector: { style: styleUrl } }), {
      language: 'ro',
      baseUrl: BASE,
      fetch: fetchDocument,
      deferUntilShown: true,
    });

    await Promise.resolve();
    expect(fetchDocument).not.toHaveBeenCalled();
    expect(layer.getSource()).toBeNull();

    layer.setVisible(true);
    await expect(ready).resolves.toBe(true);
    expect(fetchDocument).toHaveBeenCalledWith(tileJson);
    expect(layer.getSource()).not.toBeNull();
  });

  it('takes the zoom band from the entry, and a band with no ceiling from where vector tiles commonly stop', () => {
    expect(vectorZoomBand({ minZoom: 2, maxZoom: 15 })).toEqual({ min: 2, max: 15 });
    expect(vectorZoomBand({ minZoom: 0, maxZoom: 0 })).toEqual({ min: 0, max: 14 });
  });
});

describe('a style made ready for this application', () => {
  const style = {
    version: 8,
    layers: [
      { id: 'water', type: 'fill', paint: { 'fill-color': '#9cf' } },
      {
        id: 'place',
        type: 'symbol',
        layout: {
          'text-field': [
            'case',
            ['has', 'name:nonlatin'],
            ['concat', ['get', 'name:latin'], '\n', ['get', 'name:nonlatin']],
            ['coalesce', ['get', 'name_en'], ['get', 'name']],
          ],
          'text-font': ['Noto Sans Regular'],
        },
      },
      { id: 'legacy', type: 'symbol', layout: { 'text-field': '{name:latin} {name:nonlatin}', 'text-font': ['literal', ['Open Sans Italic']] } },
      { id: 'shield', type: 'symbol', layout: { 'text-field': ['get', 'ref'], 'text-font': ['Roboto Medium', 'Arial Unicode MS Regular'] } },
      { id: 'peak', type: 'symbol', layout: { 'text-field': '{ele} m', 'text-font': ['step', ['zoom'], ['literal', ['A']], 10, ['literal', ['B']]] } },
    ],
  };

  it('reads name labels in the reader’s language first, the plain name after, and leaves every other label alone', () => {
    const localised = localiseStyle(style, 'name:{lang}', 'en');
    const layout = (id: string) => localised.layers!.find((layer) => layer.id === id)!.layout!;

    expect(layout('place')['text-field']).toEqual(['coalesce', ['get', 'name:en'], ['get', 'name']]);
    expect(layout('legacy')['text-field']).toEqual(['coalesce', ['get', 'name:en'], ['get', 'name']]);
    expect(layout('shield')['text-field']).toEqual(['get', 'ref']);
    expect(layout('peak')['text-field']).toBe('{ele} m');
    // A fixed language is a field like any other.
    expect(localiseStyle(style, 'name:ro', 'en').layers![1].layout!['text-field']).toEqual([
      'coalesce',
      ['get', 'name:ro'],
      ['get', 'name'],
    ]);
  });

  it('draws every label in one of the fonts the application serves, by weight and slant', () => {
    const fonts = localiseStyle(style, null, 'ro').layers!.map((layer) => layer.layout?.['text-font']);

    expect(fonts).toEqual([
      undefined,
      ['Noto Sans Regular'],
      ['Noto Sans Italic'],
      ['Noto Sans Medium'],
      // Chosen by zoom: drawn as the style says.
      ['step', ['zoom'], ['literal', ['A']], 10, ['literal', ['B']]],
    ]);
  });

  it('keeps the labels as the style writes them without a label field, and never changes the document it was given', () => {
    const before = JSON.stringify(style);
    const kept = localiseStyle(style, null, 'ro');

    expect(kept.layers![1].layout!['text-field']).toEqual(style.layers[1].layout!['text-field']);
    expect(JSON.stringify(style)).toBe(before);
  });
});
