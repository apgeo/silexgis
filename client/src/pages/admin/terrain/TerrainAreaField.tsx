// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { BorderOutlined, DeleteOutlined } from '@ant-design/icons';
import { Button, Flex, Typography } from 'antd';
import Feature from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import type Geometry from 'ol/geom/Geometry';
import Polygon, { fromExtent } from 'ol/geom/Polygon';
import { Draw } from 'ol/interaction';
import { createBox } from 'ol/interaction/Draw';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat, transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { type TerrainBbox, areaSquareDegrees, formatBbox } from './terrainArea.ts';

/** Braşov: the middle of the karst this application was written for. */
const DEFAULT_CENTER: [number, number] = [25.6, 45.65];

const SKETCH_COLOUR = '#146262';

const sketchStyle = new Style({
  stroke: new Stroke({ color: SKETCH_COLOUR, width: 3 }),
  fill: new Fill({ color: 'rgba(20, 98, 98, 0.2)' }),
});

interface Props {
  /** Supplied by antd's Form.Item when used as a field. */
  value?: TerrainBbox | null;
  onChange?: (next: TerrainBbox | null) => void;
  height?: number;
}

/**
 * Draws the rectangle a terrain build covers, and hands out its four degrees.
 *
 * Builds a throwaway map of its own rather than borrowing the workspace one: that map is a
 * module-level singleton whose layers own shared sources and bounding-box loaders, and an
 * OpenLayers map has exactly one target, so attaching a second view to it would take the canvas
 * away from the main map. For the same reason the drawing is done by an interaction this component
 * owns, over a vector source of its own, rather than by the workspace editor — which draws into the
 * shared surface-feature source and would file a build's rectangle as a surface feature.
 *
 * Every map object lives in a ref. Holding one in React state would make React compare a mutable
 * object graph on every render, and an inline initialiser would construct a fresh map each time,
 * each leaking its layers, sources and subscriptions until a disposal that never comes.
 */
export default function TerrainAreaField({ value, onChange, height = 320 }: Props) {
  const { t } = useTranslation();
  const target = useRef<HTMLDivElement | null>(null);
  const map = useRef<Map | null>(null);
  const source = useRef(new VectorSource<Feature<Geometry>>());
  const draw = useRef<Draw | null>(null);
  // The value this component last produced. The host echoes it straight back as the next `value`,
  // and re-reading it would refit the view over a rectangle the person is still looking at.
  const emitted = useRef<TerrainBbox | null>(null);
  const emit = useRef(onChange);
  emit.current = onChange;

  const [armed, setArmed] = useState(false);
  const [bbox, setBbox] = useState<TerrainBbox | null>(value ?? null);

  const publish = (next: TerrainBbox | null) => {
    emitted.current = next;
    setBbox(next);
    emit.current?.(next);
  };

  const teardown = () => {
    map.current?.setTarget(undefined);
    // Every layer, source and interaction the map owns stays subscribed until the map is
    // disposed; dropping the reference alone leaks the whole graph for the life of the page.
    map.current?.dispose();
    map.current = null;
    draw.current = null;
  };

  // Detach when the page navigates away. This is the only teardown site: doing it from the target
  // ref would run on every re-render, because a callback ref with a fresh identity is detached and
  // re-attached each time, and the map would be destroyed and rebuilt.
  useEffect(() => () => teardown(), []);

  const drawValue = (next: TerrainBbox | null | undefined) => {
    source.current.clear();
    if (!next) {
      return;
    }
    const [west, south, east, north] = next;
    source.current.addFeature(
      new Feature({
        geometry: new Polygon(
          fromExtent(transformExtent([west, south, east, north], 'EPSG:4326', 'EPSG:3857'))
            .getCoordinates(),
        ),
      }),
    );
    const extent = source.current.getExtent();
    // An empty source reports an infinite extent, and fitting one throws.
    if (Number.isFinite(extent[0])) {
      map.current?.getView().fit(extent, { padding: [24, 24, 24, 24], maxZoom: 12 });
    }
  };

  const buildMap = () => {
    if (map.current || !target.current) {
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
      view: new View({ center: fromLonLat(DEFAULT_CENTER), zoom: 8 }),
    });
    drawValue(value);
  };

  const onTargetRef = (el: HTMLDivElement | null) => {
    target.current = el;
    buildMap();
  };

  // The rectangle the host holds, whenever it is not the one this component just handed it.
  useEffect(() => {
    if (value !== emitted.current) {
      emitted.current = value ?? null;
      setBbox(value ?? null);
      drawValue(value);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reacts to the incoming value alone
  }, [value]);

  // Arming attaches the draw; disarming, finishing or unmounting removes it, so arming twice can
  // never leave two attached.
  useEffect(() => {
    const instance = map.current;
    if (!instance || !armed) {
      return;
    }
    // A box is a two-corner drag rather than a vertex-by-vertex polygon, which is why it is drawn
    // as a circle with a geometry function that squares it off: one drag, one drawend, and no
    // double-click to finish. Without stopClick every corner also runs the map's own click
    // handling, and on touch the finishing tap both finishes the shape and zooms.
    const interaction = new Draw({
      source: source.current,
      type: 'Circle',
      geometryFunction: createBox(),
      stopClick: true,
    });
    // One rectangle at a time: a new drag replaces whatever was there rather than adding to it.
    interaction.on('drawstart', () => source.current.clear());
    interaction.on('drawend', (event) => {
      const extent = transformExtent(
        event.feature.getGeometry()!.getExtent(),
        'EPSG:3857',
        'EPSG:4326',
      );
      // Six decimals is about a tenth of a metre — finer than any elevation model describes, and
      // the same precision every other coordinate this application hands to a form carries.
      publish(extent.map((n) => Number(n.toFixed(6))) as TerrainBbox);
      setArmed(false);
    });
    instance.addInteraction(interaction);
    draw.current = interaction;
    return () => {
      instance.removeInteraction(interaction);
      if (draw.current === interaction) {
        draw.current = null;
      }
    };
  }, [armed]);

  const clear = () => {
    source.current.clear();
    setArmed(false);
    publish(null);
  };

  return (
    <Flex vertical gap={8}>
      <Flex gap={8} align="center" wrap>
        <Button
          icon={<BorderOutlined />}
          type={armed ? 'primary' : 'default'}
          onClick={() => setArmed(!armed)}
        >
          {t('terrain.area.draw')}
        </Button>
        <Button icon={<DeleteOutlined />} disabled={!bbox} onClick={clear}>
          {t('terrain.area.clear')}
        </Button>
      </Flex>
      <div ref={onTargetRef} style={{ height, width: '100%' }} data-testid="terrain-area-map" />
      <Typography.Text type="secondary" data-testid="terrain-area-extent">
        {bbox
          ? t('terrain.area.chosen', {
              extent: formatBbox(bbox),
              area: areaSquareDegrees(bbox).toFixed(2),
            })
          : t('terrain.area.hint')}
      </Typography.Text>
    </Flex>
  );
}
