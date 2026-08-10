// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState, type ReactNode } from 'react';
import {
  BorderOutlined,
  DeleteOutlined,
  ExclamationCircleOutlined,
  EnvironmentOutlined,
  LineOutlined,
} from '@ant-design/icons';
import { Button, Flex, Popover, Typography, theme } from 'antd';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import type Geometry from 'ol/geom/Geometry';
import { Draw, Modify, Snap } from 'ol/interaction';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { coarsePointer } from '../../map/pointer.ts';
import { readTripGeometry, writeTripGeometry, type TripGeometry, type TripShape } from './tripGeometry.ts';

/** Braşov: the middle of the karst this application was written for. */
const DEFAULT_CENTER: [number, number] = [25.6, 45.65];

const SKETCH_COLOUR = '#146262';

const sketchStyle = new Style({
  image: new CircleStyle({
    radius: 7,
    fill: new Fill({ color: SKETCH_COLOUR }),
    stroke: new Stroke({ color: '#fff', width: 2 }),
  }),
  stroke: new Stroke({ color: SKETCH_COLOUR, width: 3 }),
  fill: new Fill({ color: 'rgba(20, 98, 98, 0.2)' }),
});

interface Props {
  /** Supplied by antd's Form.Item when used as a field, or passed directly for a read-only view. */
  value?: TripGeometry | null;
  onChange?: (next: TripGeometry | null) => void;
  /** Draws the shape and nothing else — no toolbar, no interactions attached. */
  readOnly?: boolean;
  /**
   * Whether the container the map lives in is really on screen. A dialog mounts its children
   * lazily, so a map built while the open transition is still running measures nothing and
   * renders a blank tile grid; the host flips this once the content is in the document.
   */
  active?: boolean;
  height?: number;
}

/**
 * Draws and edits the one shape a trip carries for itself — a point, a line or an area, "roughly
 * here" rather than anything the feature registry should hold.
 *
 * Builds a throwaway map of its own rather than borrowing the workspace one: that map is a
 * module-level singleton whose layers own shared sources and bbox loaders, and an OpenLayers map
 * has exactly one target, so attaching a second view to it would take the canvas away from the
 * main map. For the same reason the drawing is done by interactions this component owns, over a
 * vector source of its own, instead of the workspace editor's — which draws into the shared
 * surface-feature source and would file a trip's sketch as a surface feature.
 */
export default function TripGeometryField({
  value,
  onChange,
  readOnly = false,
  active = true,
  height = 280,
}: Props) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const target = useRef<HTMLDivElement | null>(null);
  const map = useRef<Map | null>(null);
  const source = useRef(new VectorSource<Feature<Geometry>>());
  const draw = useRef<Draw | null>(null);
  const modify = useRef<Modify | null>(null);
  const snap = useRef<Snap | null>(null);
  // The value this component last produced. The host echoes it straight back as the next `value`,
  // and re-reading it would replace the very feature the user is still working on.
  const emitted = useRef<TripGeometry | null>(null);
  const emit = useRef(onChange);
  emit.current = onChange;

  const [shape, setShape] = useState<TripShape | null>(null);
  const [hasShape, setHasShape] = useState(value != null);

  const publish = (geometry: Geometry | null) => {
    const next = geometry ? writeTripGeometry(geometry) : null;
    emitted.current = next;
    setHasShape(next != null);
    emit.current?.(next);
  };

  const teardown = () => {
    map.current?.setTarget(undefined);
    // Every layer, source and interaction the map owns stays subscribed until the map is
    // disposed; dropping the reference alone leaks the whole graph for the life of the page.
    map.current?.dispose();
    map.current = null;
    draw.current = null;
    modify.current = null;
    snap.current = null;
  };

  // Detach if the page navigates away, or the dialog closes, while the map is up. This is the
  // only place the map is torn down: doing it from the target ref instead would run on every
  // re-render, because a callback ref with a fresh identity is detached and re-attached each
  // time, and the map would be destroyed and rebuilt — losing wherever the user had panned to.
  useEffect(() => () => teardown(), []);

  const drawValue = (geom: TripGeometry | null | undefined) => {
    source.current.clear();
    const geometry = readTripGeometry(geom);
    if (!geometry) {
      return;
    }
    source.current.addFeature(new Feature({ geometry }));
    const extent = source.current.getExtent();
    // An empty source reports an infinite extent, and fitting one throws.
    if (Number.isFinite(extent[0])) {
      map.current?.getView().fit(extent, { padding: [24, 24, 24, 24], maxZoom: 15 });
    }
  };

  /**
   * Snapping is re-attached rather than added once: the map hands a pointer event to its
   * interactions last-added first, so a snap sitting below a later-added draw would adjust the
   * coordinate only after the draw had already used it.
   */
  const attachSnap = () => {
    const instance = map.current;
    if (!instance) {
      return;
    }
    if (snap.current) {
      instance.removeInteraction(snap.current);
    }
    snap.current = new Snap({ source: source.current });
    instance.addInteraction(snap.current);
  };

  const buildMap = () => {
    if (map.current || !target.current || !active) {
      return;
    }
    map.current = new Map({
      target: target.current,
      layers: [
        new TileLayer({
          source: new XYZ({
            url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
            attributions: '© OpenStreetMap contributors',
          }),
        }),
        new VectorLayer({ source: source.current, style: sketchStyle }),
      ],
      view: new View({ center: fromLonLat(DEFAULT_CENTER), zoom: 9 }),
    });
    drawValue(value);
    if (!readOnly) {
      const interaction = new Modify({
        source: source.current,
        pixelTolerance: coarsePointer() ? 16 : 10,
      });
      interaction.on('modifyend', () => {
        publish(source.current.getFeatures()[0]?.getGeometry() ?? null);
      });
      map.current.addInteraction(interaction);
      modify.current = interaction;
      attachSnap();
    }
  };

  const onTargetRef = (el: HTMLDivElement | null) => {
    target.current = el;
    buildMap();
  };

  // The map may become buildable after the target is already mounted, when a dialog reports that
  // its open transition has finished.
  useEffect(() => {
    if (active) {
      buildMap();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- building is idempotent and guarded
  }, [active]);

  // The shape the host holds, whenever it is not the one this component just handed it.
  useEffect(() => {
    if (value !== emitted.current) {
      emitted.current = value ?? null;
      setHasShape(value != null);
      drawValue(value);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reacts to the incoming value alone
  }, [value]);

  // Arming a shape attaches a draw interaction; disarming, finishing or unmounting removes it, so
  // arming twice can never leave two attached.
  useEffect(() => {
    const instance = map.current;
    if (!instance) {
      return;
    }
    if (draw.current) {
      instance.removeInteraction(draw.current);
      draw.current = null;
    }
    modify.current?.setActive(shape == null);
    if (!shape || readOnly) {
      return;
    }
    // Without stopClick every placed vertex also runs the map's own click handling, and on touch
    // the finishing double-tap both finishes the shape and zooms.
    const interaction = new Draw({ source: source.current, type: shape, stopClick: true });
    // A trip carries one shape, so a new sketch replaces whatever was there rather than adding to it.
    interaction.on('drawstart', () => source.current.clear());
    interaction.on('drawend', (event) => {
      publish(event.feature.getGeometry() ?? null);
      setShape(null);
    });
    instance.addInteraction(interaction);
    draw.current = interaction;
    attachSnap();
    return () => {
      instance.removeInteraction(interaction);
      if (draw.current === interaction) {
        draw.current = null;
      }
    };
  }, [shape, readOnly, active]);

  const clear = () => {
    source.current.clear();
    setShape(null);
    publish(null);
  };

  const warning = (
    <Popover
      title={t('trips.geometryWarning')}
      content={<div style={{ maxWidth: 320 }}>{t('trips.geometryWarningDetail')}</div>}
      trigger="click"
    >
      <Button
        type="text"
        size="small"
        aria-label={t('trips.geometryWarning')}
        data-testid="trip-geometry-warning"
        icon={<ExclamationCircleOutlined style={{ color: token.colorWarning }} />}
      />
    </Popover>
  );

  const shapeButton = (kind: TripShape, label: string, icon: ReactNode) => (
    <Button
      icon={icon}
      type={shape === kind ? 'primary' : 'default'}
      onClick={() => setShape(shape === kind ? null : kind)}
    >
      {label}
    </Button>
  );

  return (
    <Flex vertical gap={8}>
      <Flex gap={8} align="center" wrap>
        {!readOnly && (
          <>
            {shapeButton('Point', t('trips.drawPoint'), <EnvironmentOutlined />)}
            {shapeButton('LineString', t('trips.drawLine'), <LineOutlined />)}
            {shapeButton('Polygon', t('trips.drawArea'), <BorderOutlined />)}
            <Button icon={<DeleteOutlined />} disabled={!hasShape} onClick={clear}>
              {t('trips.clearGeometry')}
            </Button>
          </>
        )}
        {warning}
      </Flex>
      <div ref={onTargetRef} style={{ height, width: '100%' }} data-testid="trip-geometry-map" />
      {!readOnly && (
        <Typography.Text type="secondary">
          {shape ? t('trips.drawHint') : t('trips.geometryHint')}
        </Typography.Text>
      )}
    </Flex>
  );
}
