// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { theme } from 'antd';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import { never } from 'ol/events/condition';
import Point from 'ol/geom/Point';
import Polygon from 'ol/geom/Polygon';
import { Draw, Modify } from 'ol/interaction';
import ImageLayer from 'ol/layer/Image';
import VectorLayer from 'ol/layer/Vector';
import Projection from 'ol/proj/Projection';
import ImageStatic from 'ol/source/ImageStatic';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style, Text } from 'ol/style';
import type { PointPair } from '../../../geodata/placement/placement.ts';
import { coarsePointer } from '../../../map/pointer.ts';
import { fromMapCoordinate, imageExtent, toMapCoordinate, type ImageSize } from '../../../rastermap/coordinates.ts';

export type Fraction = [number, number];

interface Props {
  /** The drawn page as a picture. */
  imageUrl: string;
  alt: string;
  /**
   * The drawing's size in pixels. Only its proportions matter: the picture is stretched over this
   * box, and every point is read off it as a fraction.
   */
  size: ImageSize;
  points: readonly PointPair[];
  /** Rows drawn as suspect: their residual is far above the rest. */
  outliers?: ReadonlySet<number>;
  /** Answers a click on the sheet with the point in picture fractions; absent while not picking. */
  onPick?: (u: number, v: number) => void;
  /** A point's picture half dragged somewhere else; absent where points are not moved. */
  onMovePoint?: (index: number, u: number, v: number) => void;
  /** The part of the sheet that is the map, as fractions; null for the whole sheet. */
  mask?: readonly Fraction[] | null;
  /** While true, presses draw a new outline instead of choosing points. */
  drawingMask?: boolean;
  onMaskChange?: (mask: Fraction[] | null) => void;
  active: boolean;
}

const clamp = (value: number) => Math.min(1, Math.max(0, value));

/**
 * The sheet itself, where the picture half of each control point is chosen and moved, and where
 * the outline of the part that is the map is drawn — all as fractions measured from the top-left
 * corner, which is the frame a placement is kept in.
 *
 * A throwaway map over a pixel projection of its own (the drawing's size is the whole coordinate
 * space), so pan, zoom, drawing and dragging come from the map library. OpenLayers objects live
 * in refs; points and outline travel in and out as fractions.
 */
export default function SheetPicker({
  imageUrl,
  alt,
  size,
  points,
  outliers,
  onPick,
  onMovePoint,
  mask = null,
  drawingMask = false,
  onMaskChange,
  active,
}: Props) {
  const { token } = theme.useToken();
  const target = useRef<HTMLDivElement | null>(null);
  const map = useRef<Map | null>(null);
  const pointSource = useRef(new VectorSource<Feature<Point>>());
  const maskSource = useRef(new VectorSource<Feature<Polygon>>());
  const pointModify = useRef<Modify | null>(null);
  const maskModify = useRef<Modify | null>(null);
  const draw = useRef<Draw | null>(null);
  // Listeners are attached once; what they call changes with every render.
  const callbacks = useRef({ onPick, onMovePoint, onMaskChange, size });
  callbacks.current = { onPick, onMovePoint, onMaskChange, size };
  const canMove = onMovePoint !== undefined;

  const ringToFractions = (ring: number[][]): Fraction[] => {
    const s = callbacks.current.size;
    const open = ring.length > 1 ? ring.slice(0, -1) : ring;
    return open.map((c) => [clamp(c[0] / s.width), clamp((s.height - c[1]) / s.height)]);
  };

  const teardown = () => {
    map.current?.setTarget(undefined);
    // Layers, sources and interactions stay subscribed until the map is disposed.
    map.current?.dispose();
    map.current = null;
  };
  useEffect(() => () => teardown(), []);

  const drawPoints = () => {
    pointSource.current.clear();
    points.forEach((point, index) => {
      const feature = new Feature({ geometry: new Point(toMapCoordinate({ x: point.u, y: point.v }, size)) });
      feature.set('index', index);
      feature.set('label', point.label?.trim() || String(index + 1));
      feature.set('outlier', outliers?.has(index) ?? false);
      pointSource.current.addFeature(feature);
    });
  };

  const drawMask = () => {
    maskSource.current.clear();
    if (mask && mask.length >= 3) {
      const ring = mask.map(([u, v]) => toMapCoordinate({ x: u, y: v }, size));
      maskSource.current.addFeature(new Feature({ geometry: new Polygon([[...ring, ring[0]]]) }));
    }
  };

  const buildMap = () => {
    if (map.current || !target.current) {
      return;
    }
    const extent = imageExtent(size);
    // A private projection instance, never registered by code: two pickers must not share one.
    const projection = new Projection({ code: 'placement-sheet-pixels', units: 'pixels', extent });
    const source = new ImageStatic({ url: imageUrl, imageExtent: extent, projection });
    const el = target.current;
    // What the browser test waits on before it presses the sheet.
    el.dataset.ready = 'no';
    source.on('imageloadend', () => {
      el.dataset.ready = 'yes';
    });
    const pointStyle = (feature: { get: (key: string) => unknown }) => {
      const colour = feature.get('outlier') === true ? token.colorError : token.colorPrimary;
      return new Style({
        image: new CircleStyle({
          radius: coarsePointer() ? 10 : 7,
          fill: new Fill({ color: colour }),
          stroke: new Stroke({ color: '#ffffff', width: 2 }),
        }),
        text: new Text({
          text: String(feature.get('label') ?? ''),
          offsetY: -16,
          font: '600 12px sans-serif',
          fill: new Fill({ color: colour }),
          stroke: new Stroke({ color: 'rgba(255, 255, 255, 0.9)', width: 3 }),
        }),
      });
    };
    const instance = new Map({
      target: el,
      layers: [
        new ImageLayer({ source }),
        new VectorLayer({
          source: maskSource.current,
          style: new Style({
            stroke: new Stroke({ color: token.colorWarning, width: 2, lineDash: [6, 4] }),
            fill: new Fill({ color: 'rgba(250, 173, 20, 0.08)' }),
          }),
        }),
        new VectorLayer({ source: pointSource.current, style: pointStyle }),
      ],
      view: new View({ projection, center: [size.width / 2, size.height / 2], zoom: 1 }),
    });
    const measured = instance.getSize();
    instance.getView().fit(extent, {
      size: measured && measured[0] > 0 && measured[1] > 0 ? measured : [480, 460],
      padding: [16, 16, 16, 16],
    });
    map.current = instance;

    maskModify.current = new Modify({ source: maskSource.current, pixelTolerance: 10 });
    maskModify.current.on('modifyend', () => {
      const ring = maskSource.current.getFeatures()[0]?.getGeometry()?.getCoordinates()[0];
      if (ring) {
        callbacks.current.onMaskChange?.(ringToFractions(ring));
      }
    });
    instance.addInteraction(maskModify.current);
    // Added after the outline's, so a press on a point moves the point rather than the outline.
    pointModify.current = new Modify({
      source: pointSource.current,
      pixelTolerance: 12,
      deleteCondition: never,
    });
    pointModify.current.setActive(canMove);
    pointModify.current.on('modifyend', (event) => {
      for (const feature of event.features.getArray()) {
        const index = feature.get('index') as number | undefined;
        const coordinate = (feature.getGeometry() as Point | undefined)?.getCoordinates();
        const at = coordinate ? fromMapCoordinate(coordinate, callbacks.current.size) : null;
        if (index !== undefined && at) {
          callbacks.current.onMovePoint?.(index, at.x, at.y);
        }
      }
    });
    instance.addInteraction(pointModify.current);
    instance.on('singleclick', (event) => {
      const at = fromMapCoordinate(event.coordinate, callbacks.current.size);
      if (at && !draw.current) {
        callbacks.current.onPick?.(at.x, at.y);
      }
    });
    drawPoints();
    drawMask();
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
    drawPoints();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- drawing reads the props it names
  }, [points, outliers]);

  useEffect(() => {
    drawMask();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- drawing reads the props it names
  }, [mask]);

  useEffect(() => {
    pointModify.current?.setActive(!drawingMask && canMove);
    maskModify.current?.setActive(!drawingMask);
    const instance = map.current;
    if (!instance || !drawingMask) {
      return undefined;
    }
    // No source of its own: the finished outline is handed up, and comes back as the `mask` prop
    // to be drawn like any other — a feature added here as well would be drawn twice.
    const interaction = new Draw({ type: 'Polygon', stopClick: true });
    // A sheet has one outline: a new one replaces the old.
    interaction.on('drawstart', () => maskSource.current.clear());
    interaction.on('drawend', (event) => {
      const ring = (event.feature.getGeometry() as Polygon | undefined)?.getCoordinates()[0];
      if (ring) {
        callbacks.current.onMaskChange?.(ringToFractions(ring));
      }
    });
    instance.addInteraction(interaction);
    draw.current = interaction;
    return () => {
      instance.removeInteraction(interaction);
      draw.current = null;
    };
  }, [drawingMask, canMove]);

  return (
    <div
      ref={onTargetRef}
      role="application"
      aria-label={alt}
      data-testid="placement-sheet"
      data-ready="no"
      style={{ height: 460, width: '100%', cursor: onPick || drawingMask ? 'crosshair' : undefined }}
    />
  );
}
