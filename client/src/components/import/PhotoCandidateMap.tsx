// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import Feature from 'ol/Feature';
import LineString from 'ol/geom/LineString';
import Point from 'ol/geom/Point';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat, toLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import type { PhotoCandidate } from '../../api/hooks.ts';
import { useSmallMap } from '../map/useSmallMap.ts';

/** Braşov: the middle of the karst this application was written for. */
const DEFAULT_CENTER: [number, number] = [25.6, 45.65];

/** One colour per way a picture came to be where it is — the same vocabulary as the table's tag. */
const SOURCE_COLOURS: Record<string, string> = {
  exif: '#237804',
  trackMatch: '#1677ff',
  manual: '#722ed1',
};

const SELECTED_STROKE = '#d4380d';

/** How far the view cone is drawn, in metres. Long enough to read, short enough not to imply range. */
const CONE_METRES = 60;

interface Props {
  candidates: PhotoCandidate[];
  selected: ReadonlySet<string>;
  focused?: string | null;
  onPick?: (key: string) => void;
  /**
   * Called with [longitude, latitude] when the map is clicked while a place is being positioned
   * by hand. Null turns placing off, which is what makes an ordinary click select instead.
   */
  placingKey?: string | null;
  onPlace?: (key: string, position: [number, number]) => void;
  height?: number | string;
}

/**
 * The map half of the photo review: one point per place, coloured by how it came to be there,
 * with a cone showing which way the camera looked where the picture recorded a bearing.
 *
 * The cone is worth the code. An entrance photograph that says which way the lens faced turns
 * "somewhere on this slope" into a hole somebody can walk back to — it is the single most useful
 * thing a phone records about a picture of a hole in a forest, and nothing else on the screen
 * shows it.
 *
 * Builds its own throwaway map rather than borrowing the workspace one, for the same reason the
 * vector candidate map does: the workspace map is a module-level singleton whose layers own
 * shared sources and bbox loaders.
 */
export default function PhotoCandidateMap({
  candidates,
  selected,
  focused,
  onPick,
  placingKey,
  onPlace,
  height = 420,
}: Props) {
  const source = useRef(new VectorSource<Feature<Point | LineString>>());
  const pick = useRef(onPick);
  const place = useRef(onPlace);
  const placing = useRef(placingKey);
  pick.current = onPick;
  place.current = onPlace;
  placing.current = placingKey;

  const { targetRef, ready, frame } = useSmallMap({
    center: DEFAULT_CENTER,
    zoom: 8,
    layers: () => [new VectorLayer({ source: source.current, style: styleOf })],
    onCreate: (instance) => {
      instance.on('singleclick', (event) => {
        // While a place is being positioned by hand, the whole map is the target: clicking a
        // point to select it would make the one spot you most want to correct unclickable.
        if (placing.current) {
          const [lon, lat] = toLonLat(event.coordinate);
          place.current?.(placing.current, [lon, lat]);
          return;
        }

        const hit = instance.forEachFeatureAtPixel(event.pixel, (feature) => feature);
        const key = hit?.get('candidateKey') as string | undefined;
        if (key !== undefined) {
          pick.current?.(key);
        }
      });
    },
  });

  useEffect(() => {
    source.current.clear();
    const drawn: Feature<Point | LineString>[] = [];

    for (const candidate of candidates) {
      if (candidate.geom?.type !== 'Point') {
        continue;
      }
      const [lon, lat] = candidate.geom.coordinates as number[];
      const point = new Feature({ geometry: new Point(fromLonLat([lon, lat])) });
      point.set('candidateKey', candidate.key);
      point.set('source', candidate.positionSource);
      point.set('selected', selected.has(candidate.key));
      point.set('focused', focused === candidate.key);
      drawn.push(point as Feature<Point | LineString>);

      if (candidate.directionDegrees !== null) {
        const cone = new Feature({
          geometry: new LineString([
            fromLonLat([lon, lat]),
            fromLonLat(offsetByBearing(lon, lat, candidate.directionDegrees, CONE_METRES)),
          ]),
        });
        cone.set('candidateKey', candidate.key);
        cone.set('isCone', true);
        drawn.push(cone as Feature<Point | LineString>);
      }
    }

    source.current.addFeatures(drawn);
  }, [candidates, selected, focused]);

  // The camera follows the set of places, not the selection or a place moved by hand: either
  // would otherwise jump the reader away from the spot they were looking at.
  const placesKey = candidates.map((candidate) => candidate.key).join('\u0000');
  useEffect(() => {
    if (ready) {
      frame(placesKey, source.current.getExtent(), { maxZoom: 16, duration: 200 });
    }
  }, [ready, placesKey, frame]);

  return (
    <div
      ref={targetRef}
      style={{ height, width: '100%', cursor: placingKey ? 'crosshair' : undefined }}
      data-testid="photo-candidate-map"
    />
  );
}

/**
 * A point the given distance from another along a bearing. Flat-earth arithmetic, which is
 * exact enough at sixty metres and keeps this a drawing helper rather than a geodesy library.
 */
function offsetByBearing(
  lon: number,
  lat: number,
  bearingDegrees: number,
  metres: number,
): [number, number] {
  const radians = (bearingDegrees * Math.PI) / 180;
  const metresPerDegreeLat = 111_320;
  const metresPerDegreeLon = metresPerDegreeLat * Math.cos((lat * Math.PI) / 180);
  return [
    lon + (Math.sin(radians) * metres) / (metresPerDegreeLon || 1),
    lat + (Math.cos(radians) * metres) / metresPerDegreeLat,
  ];
}

function styleOf(feature: { get: (key: string) => unknown }) {
  if (feature.get('isCone')) {
    return new Style({ stroke: new Stroke({ color: 'rgba(114, 46, 209, 0.8)', width: 2 }) });
  }

  const source = feature.get('source') as string;
  const isSelected = Boolean(feature.get('selected'));
  const isFocused = Boolean(feature.get('focused'));
  return new Style({
    image: new CircleStyle({
      radius: isFocused ? 10 : 6,
      fill: new Fill({ color: SOURCE_COLOURS[source] ?? '#9aa0a6' }),
      stroke: new Stroke({
        color: isSelected ? SELECTED_STROKE : '#fff',
        width: isSelected ? 3 : 1.5,
      }),
    }),
  });
}
