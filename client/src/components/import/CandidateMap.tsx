// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import Point from 'ol/geom/Point';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import type { ImportCandidate } from '../../api/hooks.ts';

/** Braşov: the middle of the karst this application was written for. */
const DEFAULT_CENTER: [number, number] = [25.6, 45.65];

/**
 * One colour per proposal, so the map answers "what did the rules make of this file" at a
 * glance — which is the question a reviewer has before they read a single row.
 */
const KIND_COLOURS: Record<string, string> = {
  cave: '#146262',
  caveEntrance: '#2f9e9e',
  surfaceFeature: '#b8860b',
};

const UNMATCHED_COLOUR = '#9aa0a6';

const SELECTED_STROKE = '#d4380d';

interface Props {
  candidates: ImportCandidate[];
  selected: ReadonlySet<number>;
  /** The row the table is focused on, drawn larger so the two panels point at one thing. */
  focused?: number | null;
  onPick?: (sourceId: number) => void;
  height?: number | string;
}

/**
 * The map half of the staged-import workspace: the page's candidates as points, coloured by
 * what the rules propose and outlined when selected.
 *
 * Builds its own throwaway map instance rather than borrowing the workspace one, for the same
 * reason the point picker does: the workspace map is a module-level singleton whose layers own
 * shared sources and bbox loaders, and attaching a second view to it would have this panel
 * fighting the main map for the same data.
 */
export default function CandidateMap({ candidates, selected, focused, onPick, height = 480 }: Props) {
  const target = useRef<HTMLDivElement | null>(null);
  const map = useRef<Map | null>(null);
  const source = useRef(new VectorSource<Feature<Point>>());
  const pick = useRef(onPick);
  pick.current = onPick;

  const teardown = () => {
    map.current?.setTarget(undefined);
    map.current = null;
  };

  useEffect(() => () => teardown(), []);

  const onTargetRef = (el: HTMLDivElement | null) => {
    target.current = el;
    if (!el) {
      teardown();
      return;
    }
    if (map.current) {
      return;
    }

    const instance = new Map({
      target: el,
      controls: [],
      layers: [
        new TileLayer({
          source: new XYZ({
            url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
            attributions: '© OpenStreetMap contributors',
          }),
        }),
        new VectorLayer({ source: source.current, style: styleOf }),
      ],
      view: new View({ center: fromLonLat(DEFAULT_CENTER), zoom: 8 }),
    });

    instance.on('singleclick', (event) => {
      const hit = instance.forEachFeatureAtPixel(event.pixel, (feature) => feature);
      const sourceId = hit?.get('sourceId') as number | undefined;
      if (sourceId !== undefined) {
        pick.current?.(sourceId);
      }
    });

    map.current = instance;
  };

  // Redraw whenever the page, the selection or the focused row changes. The candidate list is
  // one page of a table, so this is tens of points, not the whole file.
  useEffect(() => {
    source.current.clear();
    const features = candidates
      .filter((candidate) => candidate.geom?.type === 'Point')
      .map((candidate) => {
        const [lon, lat] = candidate.geom!.coordinates as number[];
        const feature = new Feature({ geometry: new Point(fromLonLat([lon, lat])) });
        feature.set('sourceId', candidate.sourceId);
        feature.set('kind', candidate.proposedKind ?? '');
        feature.set('selected', selected.has(candidate.sourceId));
        feature.set('focused', focused === candidate.sourceId);
        return feature;
      });
    source.current.addFeatures(features);

    const extent = source.current.getExtent();
    if (features.length > 0 && Number.isFinite(extent[0])) {
      map.current?.getView().fit(extent, { padding: [32, 32, 32, 32], maxZoom: 15, duration: 200 });
    }
  }, [candidates, selected, focused]);

  return <div ref={onTargetRef} style={{ height, width: '100%' }} data-testid="import-candidate-map" />;
}

function styleOf(feature: { get: (key: string) => unknown }) {
  const kind = feature.get('kind') as string;
  const isSelected = Boolean(feature.get('selected'));
  const isFocused = Boolean(feature.get('focused'));
  return new Style({
    image: new CircleStyle({
      radius: isFocused ? 10 : 6,
      fill: new Fill({ color: KIND_COLOURS[kind] ?? UNMATCHED_COLOUR }),
      stroke: new Stroke({
        color: isSelected ? SELECTED_STROKE : '#fff',
        width: isSelected ? 3 : 1.5,
      }),
    }),
  });
}
