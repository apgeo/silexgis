// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import {
  BorderOutlined,
  DeleteOutlined,
  ExclamationCircleOutlined,
  EnvironmentOutlined,
  LineOutlined,
} from '@ant-design/icons';
import { Flex, Popover, Typography, theme } from 'antd';
import Button from '../Button.tsx';
import Feature from 'ol/Feature';
import type Map from 'ol/Map';
import type Geometry from 'ol/geom/Geometry';
import { Draw, Modify, Snap } from 'ol/interaction';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { tripPalette as palette } from '../../map/markerPalette.ts';
import { coarsePointer } from '../../map/pointer.ts';
import { useSmallMap } from '../map/useSmallMap.ts';
import { readTripGeometry, writeTripGeometry, type TripGeometry, type TripShape } from './tripGeometry.ts';

/** Braşov: the middle of the karst this application was written for. */
const DEFAULT_CENTER: [number, number] = [25.6, 45.65];

const sketchStyle = new Style({
  image: new CircleStyle({
    radius: 7,
    fill: new Fill({ color: palette.sketch }),
    stroke: new Stroke({ color: palette.stroke, width: 2 }),
  }),
  stroke: new Stroke({ color: palette.sketch, width: 3 }),
  fill: new Fill({ color: palette.sketchFill }),
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
  /**
   * Names this map's controls apart from another one's. A trip page can show two of these at
   * once — the trip's own sketch and where its party gathers — and a driver that addressed
   * "the map" would then be addressing whichever of the two it happened to reach first.
   */
  testId?: string;
  /**
   * What the warning beside the toolbar says this shape discloses. The default states the
   * sketch's bargain; a host drawing something else must state that shape's own, because the
   * warning is the only place a reader who cannot edit the trip meets it, and a meeting point
   * standing a few hundred metres from an entrance discloses more than a shape drawn "roughly
   * here" does.
   */
  warningTitle?: string;
  warningDetail?: string;
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
  testId = 'trip-geometry',
  warningTitle,
  warningDetail,
}: Props) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const source = useRef(new VectorSource<Feature<Geometry>>());
  const draw = useRef<Draw | null>(null);
  const modify = useRef<Modify | null>(null);
  const snap = useRef<Snap | null>(null);
  // The value this component last produced. The host echoes it straight back as the next `value`,
  // and re-reading it would replace the very feature the user is still working on.
  const emitted = useRef<TripGeometry | null>(null);
  const emit = useRef(onChange);
  emit.current = onChange;
  const current = useRef(value);
  current.current = value;

  const [shape, setShape] = useState<TripShape | null>(null);
  const [hasShape, setHasShape] = useState(value != null);

  const publish = (geometry: Geometry | null) => {
    const next = geometry ? writeTripGeometry(geometry) : null;
    emitted.current = next;
    setHasShape(next != null);
    emit.current?.(next);
  };

  /**
   * Snapping is re-attached rather than added once: the map hands a pointer event to its
   * interactions last-added first, so a snap sitting below a later-added draw would adjust the
   * coordinate only after the draw had already used it.
   */
  const attachSnap = (instance: Map) => {
    if (snap.current) {
      instance.removeInteraction(snap.current);
    }
    snap.current = new Snap({ source: source.current });
    instance.addInteraction(snap.current);
  };

  // The map is drawn over the background the reader chose on the workspace map, else the
  // installation's default. It is built once its container is laid out and `active` is true, and
  // disposed (with its interactions) when the dialog closes or the page goes away; the sketch's
  // source outlives it, so a map built again draws the same shape.
  const { targetRef, map, ready, frame } = useSmallMap({
    center: DEFAULT_CENTER,
    zoom: 9,
    active,
    layers: () => [new VectorLayer({ source: source.current, style: sketchStyle })],
    onCreate: (instance) => {
      draw.current = null;
      modify.current = null;
      snap.current = null;
      if (readOnly) {
        return;
      }
      const interaction = new Modify({
        source: source.current,
        pixelTolerance: coarsePointer() ? 16 : 10,
      });
      interaction.on('modifyend', () => {
        publish(source.current.getFeatures()[0]?.getGeometry() ?? null);
      });
      instance.addInteraction(interaction);
      modify.current = interaction;
      attachSnap(instance);
    },
  });

  // Stable, because `frame` is: the effects below name it without re-running.
  const drawValue = useCallback((geom: TripGeometry | null | undefined) => {
    source.current.clear();
    const geometry = readTripGeometry(geom);
    if (!geometry) {
      return;
    }
    source.current.addFeature(new Feature({ geometry }));
    frame(JSON.stringify(geom), source.current.getExtent(), { padding: [24, 24, 24, 24], maxZoom: 15 });
  }, [frame]);


  // A map built (or built again) frames the shape the host holds at that moment.
  useEffect(() => {
    if (ready) {
      drawValue(emitted.current ?? current.current);
    }
  }, [ready, drawValue]);

  // The shape the host holds, whenever it is not the one this component just handed it.
  useEffect(() => {
    if (value !== emitted.current) {
      emitted.current = value ?? null;
      setHasShape(value != null);
      drawValue(value);
    }
  }, [value, drawValue]);

  // Arming a shape attaches a draw interaction; disarming, finishing or unmounting removes it, so
  // arming twice can never leave two attached.
  useEffect(() => {
    const instance = map.current;
    if (!instance || !ready) {
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
    attachSnap(instance);
    return () => {
      instance.removeInteraction(interaction);
      if (draw.current === interaction) {
        draw.current = null;
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the map ref is named by `ready`
  }, [shape, readOnly, ready]);

  const clear = () => {
    source.current.clear();
    setShape(null);
    publish(null);
  };

  const warningHeading = warningTitle ?? t('trips.geometryWarning');
  const warning = (
    <Popover
      title={warningHeading}
      content={
        <div style={{ maxWidth: 320 }}>{warningDetail ?? t('trips.geometryWarningDetail')}</div>
      }
      trigger="click"
    >
      <Button
        type="text"
        size="small"
        aria-label={warningHeading}
        data-testid={`${testId}-warning`}
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
    // The whole field carries the name too, not only the map and the buttons that already had
    // one: the trip form shows two of these at once, and the shape buttons are told apart by
    // their wording alone, which is identical in both. Addressing them inside their own field
    // is what keeps "draw a point" meaning the one the caller asked for.
    <Flex vertical gap={8} data-testid={testId}>
      <Flex gap={8} align="center" wrap>
        {!readOnly && (
          <>
            {shapeButton('Point', t('trips.drawPoint'), <EnvironmentOutlined />)}
            {shapeButton('LineString', t('trips.drawLine'), <LineOutlined />)}
            {shapeButton('Polygon', t('trips.drawArea'), <BorderOutlined />)}
            <Button
              icon={<DeleteOutlined />}
              disabled={!hasShape}
              onClick={clear}
              data-testid={`${testId}-clear`}
            >
              {t('trips.clearGeometry')}
            </Button>
          </>
        )}
        {warning}
      </Flex>
      <div ref={targetRef} style={{ height, width: '100%' }} data-testid={`${testId}-map`} />
      {!readOnly && (
        <Typography.Text type="secondary">
          {shape ? t('trips.drawHint') : t('trips.geometryHint')}
        </Typography.Text>
      )}
    </Flex>
  );
}
