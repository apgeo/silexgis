// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { Empty, Spin, Typography } from 'antd';
import type { FeatureLike } from 'ol/Feature';
import Map from 'ol/Map';
import View from 'ol/View';
import GeoJSON from 'ol/format/GeoJSON';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { useExpeditionMap } from '../../api/hooks.ts';

/** Braşov: the middle of the karst this application was written for. */
const DEFAULT_CENTER: [number, number] = [25.6, 45.65];

const AREA_COLOUR = '#7a5195';
const TRIP_COLOUR = '#146262';
const ENTRANCE_COLOUR = '#bc5090';

const areaStyle = new Style({
  stroke: new Stroke({ color: AREA_COLOUR, width: 2, lineDash: [6, 6] }),
  fill: new Fill({ color: 'rgba(122, 81, 149, 0.12)' }),
});

const tripStyle = new Style({
  image: new CircleStyle({
    radius: 6,
    fill: new Fill({ color: TRIP_COLOUR }),
    stroke: new Stroke({ color: '#fff', width: 2 }),
  }),
  stroke: new Stroke({ color: TRIP_COLOUR, width: 3 }),
  fill: new Fill({ color: 'rgba(20, 98, 98, 0.2)' }),
});

const entranceStyle = new Style({
  image: new CircleStyle({
    radius: 5,
    fill: new Fill({ color: ENTRANCE_COLOUR }),
    stroke: new Stroke({ color: '#fff', width: 2 }),
  }),
});

const styleFor = (feature: FeatureLike): Style => {
  switch (feature.get('kind')) {
    case 'area':
      return areaStyle;
    case 'entrance':
      return entranceStyle;
    default:
      return tripStyle;
  }
};

interface Props {
  expeditionId: string;
  /**
   * Whether the tab this map lives in is the one being shown. A pane that is not selected is
   * either not in the document at all or is in it with no size, and a map built against a
   * container with no size measures nothing and draws a blank tile grid that never repairs
   * itself. So the map is not built until the host says the container is really on screen, and
   * it is built then even though the container was mounted earlier.
   */
  active: boolean;
  height?: number;
}

/**
 * Where a camp worked, drawn from one answer the server assembled.
 *
 * Every shape here arrives from a single endpoint, and that is deliberate rather than
 * convenient: the camp's working area, the sketches of the member trips this reader may see and
 * the entrances of the caves those trips name are governed by three different rules, and the
 * last of them protects positions. A page that fetched the trips and then went looking for the
 * caves they name would be deciding on the client what a camp's map may show — so it asks one
 * question instead, and draws whatever comes back without filtering it.
 */
export default function ExpeditionMapTab({ expeditionId, active, height = 420 }: Props) {
  const { t } = useTranslation();
  const { data, isPending } = useExpeditionMap(expeditionId, active);
  const target = useRef<HTMLDivElement | null>(null);
  const map = useRef<Map | null>(null);
  const source = useRef(new VectorSource());

  const draw = () => {
    source.current.clear();
    if (!data) {
      return;
    }
    // The container is laid out only while there is something to draw, and a cached empty answer
    // arrives before the first paint — so the map can have been built against a hidden container
    // and measured 0x0. Re-measuring here, after the render that laid the container out, is what
    // stops a later non-empty answer un-hiding a blank tile grid. Cheap and idempotent otherwise.
    map.current?.updateSize();
    source.current.addFeatures(
      new GeoJSON().readFeatures(data, { featureProjection: 'EPSG:3857' }),
    );
    const extent = source.current.getExtent();
    // An empty source reports an infinite extent, and fitting one throws.
    if (Number.isFinite(extent[0])) {
      map.current?.getView().fit(extent, { padding: [24, 24, 24, 24], maxZoom: 15 });
    }
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
        new VectorLayer({ source: source.current, style: styleFor }),
      ],
      view: new View({ center: fromLonLat(DEFAULT_CENTER), zoom: 9 }),
    });
    draw();
  };

  const onTargetRef = (el: HTMLDivElement | null) => {
    target.current = el;
    buildMap();
  };

  // The map becomes buildable after its container is already mounted, the moment the tab strip
  // reports that this pane is the one on screen.
  useEffect(() => {
    if (active) {
      buildMap();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- building is idempotent and guarded
  }, [active]);

  useEffect(() => {
    draw();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- redraws when the answer changes
  }, [data]);

  // The only teardown site. Doing it from the target ref would run on every re-render, because a
  // callback ref with a fresh identity is detached and re-attached each time, and the map would
  // be destroyed and rebuilt — losing wherever the reader had panned to.
  useEffect(
    () => () => {
      map.current?.setTarget(undefined);
      // Every layer, source and interaction the map owns stays subscribed until the map is
      // disposed; dropping the reference alone leaks the whole graph for the life of the page.
      map.current?.dispose();
      map.current = null;
    },
    [],
  );

  const nothingToDraw = !isPending && data?.features.length === 0;

  return (
    <div data-testid="expedition-map-tab">
      {isPending && <Spin />}
      {nothingToDraw && <Empty description={t('expeditions.mapEmpty')} />}
      <div
        ref={onTargetRef}
        data-testid="expedition-map"
        style={{ width: '100%', height, display: nothingToDraw ? 'none' : undefined }}
      />
      <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
        {t('expeditions.mapCaveat')}
      </Typography.Paragraph>
    </div>
  );
}
