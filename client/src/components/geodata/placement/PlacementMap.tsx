// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useLayoutEffect, useRef } from 'react';
import { theme } from 'antd';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import { never } from 'ol/events/condition';
import Point from 'ol/geom/Point';
import Polygon from 'ol/geom/Polygon';
import { Modify, Translate } from 'ol/interaction';
import ImageLayer from 'ol/layer/Image';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat, toLonLat, transformExtent } from 'ol/proj';
import ImageStatic from 'ol/source/ImageStatic';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Circle as CircleStyle, Fill, Stroke, Style, Text } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { CORNER_IDS, type CornerId, type Corners, type PointPair } from '../../../geodata/placement/placement.ts';
import { mapLonLatExtent } from '../../../map/mapContext.ts';
import { coarsePointer } from '../../../map/pointer.ts';

type Extent = [number, number, number, number];

export interface PlacementMapProps {
  /** The sheet's corners in degrees, or null before the editor has chosen any. */
  corners: Corners | null;
  /** The corners after a drag of one handle or of the whole sheet has ended. */
  onCornersChange: (next: Corners) => void;
  /** The box the map first shows, in its own projection, once it is laid out. */
  onViewReady: (extent3857: Extent) => void;
  /** Where to look first, in degrees west/south/east/north; the workspace map's view otherwise. */
  initialLonLatExtent: Extent | null;
  /** The last preview the server answered, already in the map's projection. */
  preview: { url: string; imageExtent: Extent } | null;
  opacity: number;
  mode: 'corners' | 'points';
  /** The control points of the two-point mode; the complete ones are drawn on the ground. */
  points: readonly PointPair[];
  /** Answers a click on the map with its point in degrees; absent while a click means nothing. */
  onMapClick?: (lonLat: [number, number]) => void;
  /** Whether the dialog has finished opening, so the map can measure its box again. */
  active: boolean;
}

/** Brașov and the Southern Carpathians, when nothing else says where to look. */
const DEFAULT_CENTER: [number, number] = [25.3, 45.7];

/**
 * The ground the sheet is laid on: a background, the server's preview of the sheet where the
 * current corners put it, the sheet's outline and a handle on each corner.
 *
 * A throwaway map of its own, never the workspace one — that map is a module-level singleton with
 * one target, and attaching it here would take its canvas away from the map page. Everything
 * OpenLayers owns lives in refs; the corners travel in and out as degrees.
 *
 * The outline follows the handles while they are dragged without asking anybody, so the drag
 * feels live; the corners are handed on only when the drag ends, which is when the editor asks
 * the server for a picture.
 */
export default function PlacementMap({
  corners,
  onCornersChange,
  onViewReady,
  initialLonLatExtent,
  preview,
  opacity,
  mode,
  points,
  onMapClick,
  active,
}: PlacementMapProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const target = useRef<HTMLDivElement | null>(null);
  const map = useRef<Map | null>(null);
  const handleSource = useRef(new VectorSource<Feature<Point>>());
  const outlineSource = useRef(new VectorSource<Feature<Polygon>>());
  const pointSource = useRef(new VectorSource<Feature<Point>>());
  const imageLayer = useRef<ImageLayer<ImageStatic> | null>(null);
  const handleLayer = useRef<VectorLayer | null>(null);
  const outlineLayer = useRef<VectorLayer | null>(null);
  const pointLayer = useRef<VectorLayer | null>(null);
  const modify = useRef<Modify | null>(null);
  const translate = useRef<Translate | null>(null);
  // Set while this component itself moves the handles, so the change it causes is not taken for
  // a drag and echoed back into the outline it came from.
  const syncing = useRef(false);

  // Listeners are attached once, when the map is built; what they call changes with every render.
  const callbacks = useRef({ onCornersChange, onViewReady, onMapClick });
  callbacks.current = { onCornersChange, onViewReady, onMapClick };
  const first = useRef({ corners, initialLonLatExtent });

  const handle = (id: CornerId) => handleSource.current.getFeatureById(id) as Feature<Point> | null;

  const ringFromHandles = (): number[][] | null => {
    const ring = CORNER_IDS.map((id) => handle(id)?.getGeometry()?.getCoordinates());
    return ring.every((c) => c !== undefined) ? (ring as number[][]) : null;
  };

  const drawOutline = () => {
    const ring = ringFromHandles();
    if (ring === null) {
      return;
    }
    const closed = [...ring, ring[0]];
    const outline = outlineSource.current.getFeatures()[0];
    if (outline) {
      outline.getGeometry()?.setCoordinates([closed]);
    } else {
      outlineSource.current.addFeature(new Feature({ geometry: new Polygon([closed]) }));
    }
  };

  const cornersFromHandles = (): Corners | null => {
    const ring = ringFromHandles();
    if (ring === null) {
      return null;
    }
    const lonLat = ring.map((c) => toLonLat(c) as [number, number]);
    return { nw: lonLat[0], ne: lonLat[1], se: lonLat[2], sw: lonLat[3] };
  };

  const placeHandles = (next: Corners) => {
    syncing.current = true;
    try {
      for (const id of CORNER_IDS) {
        const at = fromLonLat(next[id]);
        const existing = handle(id);
        if (existing) {
          existing.getGeometry()?.setCoordinates(at);
        } else {
          const feature = new Feature({ geometry: new Point(at) });
          feature.setId(id);
          feature.set('label', id.toUpperCase());
          handleSource.current.addFeature(feature);
        }
      }
      drawOutline();
    } finally {
      syncing.current = false;
    }
  };

  const teardown = () => {
    map.current?.setTarget(undefined);
    // Layers, sources and interactions stay subscribed until the map is disposed.
    map.current?.dispose();
    map.current = null;
  };
  useEffect(() => () => teardown(), []);

  const buildMap = () => {
    if (map.current || !target.current) {
      return;
    }
    const handleStyle = (feature: { get: (key: string) => unknown }) =>
      new Style({
        image: new CircleStyle({
          radius: coarsePointer() ? 10 : 7,
          fill: new Fill({ color: token.colorPrimary }),
          stroke: new Stroke({ color: '#ffffff', width: 2 }),
        }),
        text: new Text({
          text: String(feature.get('label') ?? ''),
          offsetY: -16,
          font: '600 12px sans-serif',
          fill: new Fill({ color: token.colorPrimary }),
          stroke: new Stroke({ color: 'rgba(255, 255, 255, 0.9)', width: 3 }),
        }),
      });
    imageLayer.current = new ImageLayer({ opacity });
    // A faint fill so a press anywhere inside the sheet picks it up to move it whole.
    outlineLayer.current = new VectorLayer({
      source: outlineSource.current,
      style: new Style({
        stroke: new Stroke({ color: token.colorPrimary, width: 2 }),
        fill: new Fill({ color: 'rgba(22, 119, 255, 0.06)' }),
      }),
    });
    handleLayer.current = new VectorLayer({ source: handleSource.current, style: handleStyle });
    pointLayer.current = new VectorLayer({ source: pointSource.current, style: handleStyle });
    const instance = new Map({
      target: target.current,
      layers: [
        new TileLayer({
          source: new XYZ({
            url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
            attributions: '© OpenStreetMap contributors',
          }),
        }),
        imageLayer.current,
        outlineLayer.current,
        handleLayer.current,
        pointLayer.current,
      ],
      view: new View({ center: fromLonLat(DEFAULT_CENTER), zoom: 8 }),
    });
    map.current = instance;
    // Before the dialog has laid itself out the map may not know its size yet; a plausible one
    // still frames the first look, and the dialog asks for a re-measure once it is open.
    const size = instance.getSize();
    const usable = size && size[0] > 0 && size[1] > 0 ? size : [800, 480];
    const look =
      first.current.initialLonLatExtent ??
      (first.current.corners ? lonLatBox(first.current.corners) : null) ??
      mapLonLatExtent() ??
      null;
    if (look) {
      instance.getView().fit(transformExtent(look, 'EPSG:4326', 'EPSG:3857'), {
        size: usable,
        padding: [32, 32, 32, 32],
        maxZoom: 18,
      });
    }

    // Translate first and Modify after it: the map offers a press to the last-added interaction
    // first, so a press on a handle moves that corner rather than the whole sheet.
    translate.current = new Translate({ layers: [outlineLayer.current] });
    translate.current.on('translating', () => {
      const ring = outlineSource.current.getFeatures()[0]?.getGeometry()?.getCoordinates()[0];
      if (!ring) {
        return;
      }
      syncing.current = true;
      CORNER_IDS.forEach((id, index) => handle(id)?.getGeometry()?.setCoordinates(ring[index]));
      syncing.current = false;
    });
    translate.current.on('translateend', () => {
      const next = cornersFromHandles();
      if (next) {
        callbacks.current.onCornersChange(next);
      }
    });
    instance.addInteraction(translate.current);
    modify.current = new Modify({
      source: handleSource.current,
      pixelTolerance: 12,
      deleteCondition: never,
    });
    modify.current.on('modifyend', () => {
      const next = cornersFromHandles();
      if (next) {
        callbacks.current.onCornersChange(next);
      }
    });
    instance.addInteraction(modify.current);
    handleSource.current.on('changefeature', () => {
      if (!syncing.current) {
        drawOutline();
      }
    });
    instance.on('singleclick', (event) => {
      callbacks.current.onMapClick?.(toLonLat(event.coordinate) as [number, number]);
    });
    if (first.current.corners) {
      placeHandles(first.current.corners);
    }
    callbacks.current.onViewReady(instance.getView().calculateExtent(usable) as Extent);
  };

  const onTargetRef = (el: HTMLDivElement | null) => {
    target.current = el;
    buildMap();
  };

  useEffect(() => {
    if (active) {
      map.current?.updateSize();
    }
  }, [active]);

  useEffect(() => {
    if (corners && map.current) {
      placeHandles(corners);
    }
  }, [corners]);

  useEffect(() => {
    const cornersMode = mode === 'corners';
    handleLayer.current?.setVisible(cornersMode);
    outlineLayer.current?.setVisible(cornersMode);
    modify.current?.setActive(cornersMode);
    translate.current?.setActive(cornersMode);
    pointLayer.current?.setVisible(!cornersMode);
  }, [mode]);

  useEffect(() => {
    pointSource.current.clear();
    points.forEach((point, index) => {
      if (point.x !== null && point.y !== null) {
        const feature = new Feature({ geometry: new Point(fromLonLat([point.x, point.y])) });
        feature.set('label', String(index + 1));
        pointSource.current.addFeature(feature);
      }
    });
  }, [points]);

  useEffect(() => {
    imageLayer.current?.setOpacity(opacity);
  }, [opacity]);

  // Laid out with the render that brought the answer, so the marker below never says "loaded"
  // about the previous picture while the editor already shows the new answer's fit.
  useLayoutEffect(() => {
    const layer = imageLayer.current;
    const el = target.current;
    if (!layer) {
      return;
    }
    if (preview === null) {
      layer.setSource(null);
      if (el) el.dataset.preview = 'none';
      return;
    }
    const source = new ImageStatic({ url: preview.url, imageExtent: preview.imageExtent, projection: 'EPSG:3857' });
    if (el) el.dataset.preview = 'loading';
    // What the browser test waits on: the preview picture really drawn, not merely asked for.
    source.on('imageloadend', () => {
      if (el) el.dataset.preview = 'loaded';
    });
    source.on('imageloaderror', () => {
      if (el) el.dataset.preview = 'error';
    });
    layer.setSource(source);
  }, [preview]);

  return (
    <div
      ref={onTargetRef}
      role="application"
      aria-label={t('geodata.placement.mapLabel')}
      data-testid="placement-map"
      data-preview="none"
      style={{ height: 460, width: '100%', cursor: onMapClick ? 'crosshair' : undefined }}
    />
  );
}

function lonLatBox(corners: Corners): Extent {
  const xs = CORNER_IDS.map((id) => corners[id][0]);
  const ys = CORNER_IDS.map((id) => corners[id][1]);
  return [Math.min(...xs), Math.min(...ys), Math.max(...xs), Math.max(...ys)];
}
