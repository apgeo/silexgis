// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  fetchResLinksForTarget,
  fetchSurveyModels,
  fetchSurveyStationsNamed,
  type ResLink,
  type SurveyModelInfo,
} from '../api/hooks.ts';
import type { CaveViewMediaEntry } from '../caveview/loadCaveView.ts';
import { stationMediaFromLinks } from '../caveview/stationMedia.ts';
import { ANCHORED_TO_SURFACE, drawnAltitude, samePlacement, type Altitude3DPlacement } from './altitude3d.ts';
import { photoIcon } from './markerIcons3d.ts';
import type { Scene3DMarker, Scene3DVectorSource, Scene3DVectorSources } from './scene3dEngine.ts';

// Where the photographs of the selected cave were taken, in the 3D scene.
//
// A photograph in a cave has no place of its own: there is no satellite fix underground, and the
// position a phone wrote into the picture is where it last had one, outside. What it does have is
// what somebody said about it — a link between the photograph and a station of the cave's survey,
// made by clicking the station in the survey viewer. That station is its place. So the markers are
// read the way the survey viewer reads the pictures it shows at its stations (one derivation, in
// `stationMedia.ts`), and placed at those stations' measured positions, by the same height rule
// as everything else drawn of the cave.
//
// Driven by the selection, as the cave's walls are, and for the same reason: answering for a
// whole view would mean reading every link of every cave in it. Everything it asks the server is
// asked through routes that already decide what this reader may see — the links route withholds a
// link or a picture the reader may not have, and the stations route answers nothing for a survey
// whose position is withheld from them — so no rule about who may see what is restated here.

/** The source id the markers are drawn under. */
export const STATION_PHOTOS_SOURCE_ID = 'station-photos';

/** How many of one model's links are read for pictures: the bound the survey viewer uses. */
const MAX_LINKS = 200;

/** One station of the selected cave with the photographs linked to it. */
export interface StationPhotos3DPlace {
  /** Identifies the place among the markers, and is what a pick of its marker carries. */
  key: string;
  modelId: string;
  /** The station as the drawing names it, which is how the photographs were linked to it. */
  station: string;
  longitude: number;
  latitude: number;
  altitudeM: number;
  pictures: CaveViewMediaEntry[];
}

export type StationPhotos3DStatus = 'off' | 'looking' | 'drawn' | 'failed';

export interface StationPhotos3DState {
  status: StationPhotos3DStatus;
  caveId?: string;
  /** How many stations carry photographs, and how many photographs that is in all. */
  places: number;
  pictures: number;
}

export const EMPTY_STATION_PHOTOS_3D_STATE: StationPhotos3DState = { status: 'off', places: 0, pictures: 0 };

/** What a marker of a photograph's place carries back on a pick. */
export interface StationPhotosPickId {
  kind: 'stationPhotos';
  key: string;
}

/** Whether a pick's id is one of these markers. */
export function isStationPhotosPickId(id: unknown): id is StationPhotosPickId {
  return (
    typeof id === 'object' &&
    id !== null &&
    (id as Record<string, unknown>).kind === 'stationPhotos' &&
    typeof (id as Record<string, unknown>).key === 'string'
  );
}

/** One measured station, as much of it as is needed here. */
export interface StationPhotos3DStation {
  name: string;
  viewerName: string;
  longitude: number;
  latitude: number;
  altitudeM: number;
}

/** What the loader asks the server, as arguments so every decision can be tested without one. */
export interface StationPhotos3DFetchers {
  models(caveId: string): Promise<SurveyModelInfo[]>;
  links(modelId: string): Promise<ResLink[]>;
  stations(modelId: string, names: readonly string[]): Promise<StationPhotos3DStation[]>;
}

export const DEFAULT_STATION_PHOTOS_FETCHERS: StationPhotos3DFetchers = {
  models: fetchSurveyModels,
  links: (modelId) => fetchResLinksForTarget('surveyModel', modelId, { pageSize: MAX_LINKS }),
  stations: fetchSurveyStationsNamed,
};

export interface StationPhotos3DHandle {
  /** Which cave is selected; undefined clears the markers. */
  setCave(caveId: string | undefined): void;
  setVisible(visible: boolean): void;
  setAltitudePlacement(placement: Altitude3DPlacement): void;
  /** Re-places the markers against the cave top known now, which arrives after the markers do. */
  refreshSurveyTops(): void;
  /** The place a marker stands for, by the key its pick carries. */
  placeOf(key: string): StationPhotos3DPlace | undefined;
  getState(): StationPhotos3DState;
  subscribe(listener: (state: StationPhotos3DState) => void): () => void;
  detach(): void;
}

/** A line plot that has been read has stations; nothing else does. */
function hasStations(model: SurveyModelInfo): boolean {
  return model.format !== 'stl' && model.status === 'ready';
}

/**
 * The marker for one place: on the ground until the cave's top is known in a scene with no relief,
 * because a station's surveyed altitude means nothing against a bare globe until the cave it hangs
 * from is placed — and at the station's own drawn height from then on.
 */
export function stationPhotoMarker(
  place: StationPhotos3DPlace,
  surveyTopM: number | undefined,
  placement: Altitude3DPlacement,
): Scene3DMarker {
  const icon = photoIcon(place.pictures.length);
  const id: StationPhotosPickId = { kind: 'stationPhotos', key: place.key };
  if (!placement.absolute && surveyTopM === undefined) {
    return {
      position: { longitude: place.longitude, latitude: place.latitude, height: 0 },
      clampToGround: true,
      image: icon.image,
      scale: icon.scale,
      id,
    };
  }
  return {
    position: {
      longitude: place.longitude,
      latitude: place.latitude,
      height: drawnAltitude(place.altitudeM, surveyTopM ?? 0, placement),
    },
    image: icon.image,
    scale: icon.scale,
    id,
  };
}

export function attachStationPhotos3d(
  engine: Pick<Scene3DVectorSources, 'createMarkerSource'>,
  surveyTop: (caveId: string) => number | undefined,
  fetchers: StationPhotos3DFetchers = DEFAULT_STATION_PHOTOS_FETCHERS,
): StationPhotos3DHandle {
  const source: Scene3DVectorSource<Scene3DMarker> = engine.createMarkerSource(STATION_PHOTOS_SOURCE_ID);
  let caveId: string | undefined;
  let visible = true;
  let placement: Altitude3DPlacement = ANCHORED_TO_SURFACE;
  let places: StationPhotos3DPlace[] = [];
  let state: StationPhotos3DState = { ...EMPTY_STATION_PHOTOS_3D_STATE };
  let seq = 0;
  let detached = false;
  const listeners = new Set<(next: StationPhotos3DState) => void>();

  const publish = (next: Partial<StationPhotos3DState>) => {
    state = { ...EMPTY_STATION_PHOTOS_3D_STATE, caveId, ...next };
    for (const listener of [...listeners]) {
      listener(state);
    }
  };

  const draw = () => {
    if (!visible || caveId === undefined || places.length === 0) {
      source.clear();
      return;
    }
    const top = surveyTop(caveId);
    source.replace(places.map((place) => stationPhotoMarker(place, top, placement)));
  };

  const load = async () => {
    seq += 1;
    const mine = seq;
    places = [];
    source.clear();
    if (!caveId || !visible) {
      publish({ status: 'off' });
      return;
    }
    const cave = caveId;
    publish({ status: 'looking' });
    try {
      const models = (await fetchers.models(cave)).filter(hasStations);
      const found: StationPhotos3DPlace[] = [];
      for (const model of models) {
        const media = stationMediaFromLinks(await fetchers.links(model.id), model.id);
        if (media.size === 0) {
          continue;
        }
        const stations = await fetchers.stations(model.id, [...media.keys()]);
        for (const [name, pictures] of media) {
          // The link names the station the way the drawing does; the row answers to that name or
          // to the one the file stores, and either is the same station.
          const station = stations.find((s) => s.viewerName === name || s.name === name);
          if (station === undefined) {
            // A station the survey no longer has, or one withheld from this reader: no place, so
            // no marker — a photograph is never put somewhere it was not linked to.
            continue;
          }
          found.push({
            key: `${model.id}|${name}`,
            modelId: model.id,
            station: name,
            longitude: station.longitude,
            latitude: station.latitude,
            altitudeM: station.altitudeM,
            pictures,
          });
        }
      }
      if (mine !== seq || detached) {
        return;
      }
      places = found;
      draw();
      publish({
        status: 'drawn',
        places: found.length,
        pictures: found.reduce((sum, place) => sum + place.pictures.length, 0),
      });
    } catch {
      if (mine === seq && !detached) {
        publish({ status: 'failed' });
      }
    }
  };

  return {
    setCave(next) {
      if (next === caveId) {
        return;
      }
      caveId = next;
      void load();
    },
    setVisible(next) {
      if (next === visible) {
        return;
      }
      visible = next;
      void load();
    },
    setAltitudePlacement(next) {
      if (samePlacement(next, placement)) {
        return;
      }
      placement = next;
      draw();
    },
    refreshSurveyTops() {
      draw();
    },
    placeOf(key) {
      return places.find((place) => place.key === key);
    },
    getState() {
      return state;
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    detach() {
      detached = true;
      seq += 1;
      listeners.clear();
      source.remove();
    },
  };
}
