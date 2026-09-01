// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import Point from 'ol/geom/Point';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat, toLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Circle as CircleStyle, Fill, Stroke, Style, Text } from 'ol/style';

/** Romania, wide enough to see all of the karst the catalogue covers. */
const DEFAULT_CENTER: [number, number] = [25.0, 45.9];

export interface PlacedCave {
  id: number;
  title: string;
  longitude: number;
  latitude: number;
}

interface Props {
  placed: PlacedCave[];
  /** The cave being positioned. While one is set, the whole map is a target for placing it. */
  placingId: number | null;
  placingTitle?: string;
  onPlace: (id: number, position: [number, number]) => void;
  height?: number | string;
}

/**
 * The map half of a catalogue import: somewhere to put the caves the catalogue could not place.
 *
 * It draws only what somebody has positioned by hand, because that is all there is — the source
 * register publishes no coordinates, so an unplaced cave has nothing to draw and is not
 * represented here at all. That emptiness is honest and is the point of the screen it sits on.
 *
 * Builds its own throwaway map rather than borrowing the workspace one, for the same reason the
 * other import maps do: the workspace map is a module-level singleton whose layers own shared
 * sources and bounding-box loaders, and a review screen must not disturb them.
 */
export default function CataloguePlacementMap({
  placed,
  placingId,
  placingTitle,
  onPlace,
  height = 380,
}: Props) {
  const map = useRef<Map | null>(null);
  const source = useRef(new VectorSource<Feature<Point>>());
  const place = useRef(onPlace);
  const placing = useRef(placingId);
  place.current = onPlace;
  placing.current = placingId;

  const teardown = () => {
    map.current?.setTarget(undefined);
    map.current = null;
  };

  useEffect(() => () => teardown(), []);

  const onTargetRef = (el: HTMLDivElement | null) => {
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
      view: new View({ center: fromLonLat(DEFAULT_CENTER), zoom: 6 }),
    });

    instance.on('singleclick', (event) => {
      if (placing.current === null) {
        return;
      }
      const [lon, lat] = toLonLat(event.coordinate);
      place.current(placing.current, [lon, lat]);
    });

    map.current = instance;
  };

  useEffect(() => {
    source.current.clear();

    for (const cave of placed) {
      const point = new Feature({ geometry: new Point(fromLonLat([cave.longitude, cave.latitude])) });
      point.set('label', cave.title);
      point.set('placing', cave.id === placingId);
      source.current.addFeature(point);
    }

    const extent = source.current.getExtent();
    if (placed.length > 0 && Number.isFinite(extent[0])) {
      map.current?.getView().fit(extent, { padding: [40, 40, 40, 40], maxZoom: 14, duration: 200 });
    }
  }, [placed, placingId]);

  return (
    <div
      ref={onTargetRef}
      style={{ height, width: '100%', cursor: placingId !== null ? 'crosshair' : undefined }}
      title={placingTitle}
      data-testid="catalogue-placement-map"
    />
  );
}

function styleOf(feature: { get: (key: string) => unknown }) {
  const isPlacing = Boolean(feature.get('placing'));

  return new Style({
    image: new CircleStyle({
      radius: isPlacing ? 8 : 6,
      fill: new Fill({ color: isPlacing ? '#d4380d' : '#722ed1' }),
      stroke: new Stroke({ color: '#fff', width: 2 }),
    }),
    text: new Text({
      text: String(feature.get('label') ?? ''),
      offsetY: -16,
      font: '12px sans-serif',
      fill: new Fill({ color: '#000' }),
      stroke: new Stroke({ color: '#fff', width: 3 }),
    }),
  });
}
