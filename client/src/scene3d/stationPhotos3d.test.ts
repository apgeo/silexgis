// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { ResLink, ResLinkMember, SurveyModelInfo } from '../api/hooks.ts';
import { ANCHORED_TO_SURFACE } from './altitude3d.ts';
import type { Scene3DMarker, Scene3DVectorSource } from './scene3dEngine.ts';
import {
  attachStationPhotos3d,
  isStationPhotosPickId,
  STATION_PHOTOS_SOURCE_ID,
  type StationPhotos3DFetchers,
  type StationPhotos3DStation,
} from './stationPhotos3d.ts';

const settle = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

function model(overrides: Partial<SurveyModelInfo> = {}): SurveyModelInfo {
  return {
    id: 'plot-1',
    caveId: 'cave-1',
    name: 'Coiba Mare',
    format: 'survex3d',
    status: 'ready',
    isCurrent: true,
    ...overrides,
  } as SurveyModelInfo;
}

function member(overrides: Partial<ResLinkMember>): ResLinkMember {
  return {
    id: 'member',
    targetType: 'document',
    targetId: 'doc',
    isMain: false,
    sortOrder: 0,
    note: null,
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
    anchorState: 'exact',
    display: null,
    ...overrides,
  } as ResLinkMember;
}

const station = (modelId: string, name: string) =>
  member({
    id: `station-${name}`,
    targetType: 'surveyModel',
    targetId: modelId,
    anchorKind: 'modelStation',
    anchor: { station: name } as unknown as ResLinkMember['anchor'],
  });

const photo = (id: string) =>
  member({
    id,
    targetId: id,
    display: {
      title: id,
      subtitle: null,
      route: null,
      thumbnailUrl: `http://files.local/${id}/thumb?token=t`,
      mediaType: 'image/jpeg',
    } as unknown as ResLinkMember['display'],
  });

const link = (members: ResLinkMember[]): ResLink =>
  ({ id: 'link', shortCode: 'ABCD1234', relationType: null, description: null, mayEdit: true, members }) as unknown as ResLink;

/** A marker source that records what it was handed. */
class FakeSource implements Scene3DVectorSource<Scene3DMarker> {
  items: readonly Scene3DMarker[] = [];
  removed = false;
  replace(items: readonly Scene3DMarker[]) {
    this.items = items;
  }
  clear() {
    this.items = [];
  }
  setVisible() {}
  setOpacity() {}
  remove() {
    this.removed = true;
  }
}

function scene() {
  const sources = new Map<string, FakeSource>();
  return {
    sources,
    createMarkerSource(id: string) {
      const source = new FakeSource();
      sources.set(id, source);
      return source;
    },
  };
}

function fetchers(
  models: SurveyModelInfo[],
  links: Record<string, ResLink[]>,
  stations: Record<string, StationPhotos3DStation[]>,
  asked: string[][] = [],
): StationPhotos3DFetchers {
  return {
    models: () => Promise.resolve(models),
    links: (modelId) => Promise.resolve(links[modelId] ?? []),
    stations: (modelId, names) => {
      asked.push([modelId, ...names]);
      return Promise.resolve(
        (stations[modelId] ?? []).filter((s) => names.includes(s.viewerName) || names.includes(s.name)),
      );
    },
  };
}

const at = (name: string, viewerName = name, altitudeM = 1100): StationPhotos3DStation => ({
  name,
  viewerName,
  longitude: 22.8,
  latitude: 46.5,
  altitudeM,
});

describe('attachStationPhotos3d', () => {
  it('marks each station a photograph is linked to, at that station, and asks only for those stations', async () => {
    const engine = scene();
    const asked: string[][] = [];
    const photos = attachStationPhotos3d(
      engine,
      () => 1150,
      fetchers(
        [model()],
        { 'plot-1': [link([station('plot-1', 'a.1'), photo('p1'), photo('p2')]), link([station('plot-1', 'a.7'), photo('p3')])] },
        { 'plot-1': [at('a.1'), at('a.7', 'a.7', 1080), at('a.9')] },
        asked,
      ),
    );

    photos.setCave('cave-1');
    await settle();

    expect(asked).toEqual([['plot-1', 'a.1', 'a.7']]);
    const markers = engine.sources.get(STATION_PHOTOS_SOURCE_ID)!.items;
    expect(markers).toHaveLength(2);
    // Hung from the cave's top, as its lines and walls are.
    expect(markers.map((m) => m.position.height).sort((a, b) => a - b)).toEqual([-70, -50]);
    expect(photos.getState()).toMatchObject({ status: 'drawn', places: 2, pictures: 3 });

    const first = markers.find((m) => m.position.height === -50)!;
    expect(isStationPhotosPickId(first.id)).toBe(true);
    const place = photos.placeOf((first.id as { key: string }).key)!;
    expect(place.station).toBe('a.1');
    expect(place.pictures.map((p) => p.documentId)).toEqual(['p1', 'p2']);
  });

  it('finds a station under the name its rows hold as well as the one the drawing shows', async () => {
    const engine = scene();
    const photos = attachStationPhotos3d(
      engine,
      () => 1150,
      fetchers(
        [model({ format: 'lox' })],
        { 'plot-1': [link([station('plot-1', 'Galerie.1'), photo('p1')])] },
        { 'plot-1': [at('Sys.Galerie.1', 'Galerie.1')] },
      ),
    );
    photos.setCave('cave-1');
    await settle();
    expect(photos.getState().places).toBe(1);
  });

  it('puts no photograph at a station the survey does not have, or one withheld from the reader', async () => {
    const engine = scene();
    const photos = attachStationPhotos3d(
      engine,
      () => 1150,
      fetchers([model()], { 'plot-1': [link([station('plot-1', 'gone.1'), photo('p1')])] }, { 'plot-1': [] }),
    );
    photos.setCave('cave-1');
    await settle();
    expect(engine.sources.get(STATION_PHOTOS_SOURCE_ID)!.items).toHaveLength(0);
    expect(photos.getState()).toMatchObject({ status: 'drawn', places: 0 });
  });

  it('reads only line plots that have been read: walls and unread surveys have no stations', async () => {
    const engine = scene();
    const asked: string[][] = [];
    const links: Record<string, ResLink[]> = {
      walls: [link([station('walls', 'a.1'), photo('p1')])],
      unread: [link([station('unread', 'a.1'), photo('p2')])],
    };
    const photos = attachStationPhotos3d(
      engine,
      () => 1150,
      fetchers(
        [model({ id: 'walls', format: 'stl' }), model({ id: 'unread', status: 'processing' })],
        links,
        {},
        asked,
      ),
    );
    photos.setCave('cave-1');
    await settle();
    expect(asked).toEqual([]);
  });

  it('lays a place on the ground until the cave top is known on a bare globe, and hangs it once it is', async () => {
    const engine = scene();
    let top: number | undefined;
    const photos = attachStationPhotos3d(
      engine,
      () => top,
      fetchers([model()], { 'plot-1': [link([station('plot-1', 'a.1'), photo('p1')])] }, { 'plot-1': [at('a.1')] }),
    );
    photos.setAltitudePlacement(ANCHORED_TO_SURFACE);
    photos.setCave('cave-1');
    await settle();
    expect(engine.sources.get(STATION_PHOTOS_SOURCE_ID)!.items[0].clampToGround).toBe(true);

    top = 1150;
    photos.refreshSurveyTops();
    const marker = engine.sources.get(STATION_PHOTOS_SOURCE_ID)!.items[0];
    expect(marker.clampToGround).toBeUndefined();
    expect(marker.position.height).toBe(-50);
  });

  it('clears the markers when the selection goes or the layer is switched off, and lets the source go on detach', async () => {
    const engine = scene();
    const photos = attachStationPhotos3d(
      engine,
      () => 1150,
      fetchers([model()], { 'plot-1': [link([station('plot-1', 'a.1'), photo('p1')])] }, { 'plot-1': [at('a.1')] }),
    );
    const source = () => engine.sources.get(STATION_PHOTOS_SOURCE_ID)!;
    photos.setCave('cave-1');
    await settle();
    expect(source().items).toHaveLength(1);

    photos.setVisible(false);
    await settle();
    expect(source().items).toHaveLength(0);
    expect(photos.getState().status).toBe('off');

    photos.setVisible(true);
    await settle();
    photos.setCave(undefined);
    await settle();
    expect(source().items).toHaveLength(0);

    photos.detach();
    expect(source().removed).toBe(true);
  });

  it('says it could not look, rather than that the cave has no photographs, when a request fails', async () => {
    const engine = scene();
    const photos = attachStationPhotos3d(engine, () => 1150, {
      models: () => Promise.reject(new Error('no')),
      links: () => Promise.resolve([]),
      stations: () => Promise.resolve([]),
    });
    photos.setCave('cave-1');
    await settle();
    expect(photos.getState().status).toBe('failed');
  });
});
