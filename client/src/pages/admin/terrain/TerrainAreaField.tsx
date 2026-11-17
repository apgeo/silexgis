// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useRef, useState } from 'react';
import { BorderOutlined, DeleteOutlined } from '@ant-design/icons';
import { Flex, Typography } from 'antd';
import Button from '../../../components/Button.tsx';
import Feature from 'ol/Feature';
import type Geometry from 'ol/geom/Geometry';
import Polygon, { fromExtent } from 'ol/geom/Polygon';
import { Draw } from 'ol/interaction';
import { createBox } from 'ol/interaction/Draw';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { useSmallMap } from '../../../components/map/useSmallMap.ts';
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
 * object graph on every render.
 */
export default function TerrainAreaField({ value, onChange, height = 320 }: Props) {
  const { t } = useTranslation();
  const source = useRef(new VectorSource<Feature<Geometry>>());
  const draw = useRef<Draw | null>(null);
  // The value this component last produced. The host echoes it straight back as the next `value`,
  // and re-reading it would refit the view over a rectangle the person is still looking at.
  const emitted = useRef<TerrainBbox | null>(null);
  const emit = useRef(onChange);
  emit.current = onChange;
  const current = useRef(value);
  current.current = value;

  const [armed, setArmed] = useState(false);
  const [bbox, setBbox] = useState<TerrainBbox | null>(value ?? null);

  const publish = (next: TerrainBbox | null) => {
    emitted.current = next;
    setBbox(next);
    emit.current?.(next);
  };

  // Drawn over the background the administrator chose on the workspace map, else the
  // installation's default; the hook disposes the map, its layers and interactions when the page
  // goes away.
  const { targetRef, map, ready, frame } = useSmallMap({
    center: DEFAULT_CENTER,
    zoom: 8,
    layers: () => [new VectorLayer({ source: source.current, style: sketchStyle })],
    onCreate: () => {
      draw.current = null;
    },
  });

  // Stable, because `frame` is: the effects below name it without re-running.
  const drawValue = useCallback((next: TerrainBbox | null | undefined) => {
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
    frame(next.join(','), source.current.getExtent(), { padding: [24, 24, 24, 24], maxZoom: 12 });
  }, [frame]);


  // A map once built frames the rectangle the host holds at that moment.
  useEffect(() => {
    if (ready) {
      drawValue(emitted.current ?? current.current);
    }
  }, [ready, drawValue]);

  // The rectangle the host holds, whenever it is not the one this component just handed it.
  useEffect(() => {
    if (value !== emitted.current) {
      emitted.current = value ?? null;
      setBbox(value ?? null);
      drawValue(value);
    }
  }, [value, drawValue]);

  // Arming attaches the draw; disarming, finishing or unmounting removes it, so arming twice can
  // never leave two attached.
  useEffect(() => {
    const instance = map.current;
    if (!instance || !ready || !armed) {
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
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the map ref is named by `ready`
  }, [armed, ready]);

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
      <div ref={targetRef} style={{ height, width: '100%' }} data-testid="terrain-area-map" />
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
