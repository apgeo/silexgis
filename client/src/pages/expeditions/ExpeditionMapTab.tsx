// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, Empty, Spin, Typography } from 'antd';
import type { FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { useExpeditionMap } from '../../api/hooks.ts';
import Button from '../../components/Button.tsx';
import { useSmallMap } from '../../components/map/useSmallMap.ts';
import { tripPalette as palette } from '../../map/markerPalette.ts';

/** Braşov: the middle of the karst this application was written for. */
const DEFAULT_CENTER = [25.6, 45.65] as const;

const AREA_COLOUR = '#7a5195';

const areaStyle = new Style({
  stroke: new Stroke({ color: AREA_COLOUR, width: 2, lineDash: [6, 6] }),
  fill: new Fill({ color: 'rgba(122, 81, 149, 0.12)' }),
});

const tripStyle = new Style({
  image: new CircleStyle({
    radius: 6,
    fill: new Fill({ color: palette.sketch }),
    stroke: new Stroke({ color: palette.stroke, width: 2 }),
  }),
  stroke: new Stroke({ color: palette.sketch, width: 3 }),
  fill: new Fill({ color: palette.sketchFill }),
});

const entranceStyle = new Style({
  image: new CircleStyle({
    radius: 5,
    fill: new Fill({ color: palette.meeting }),
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
  const { data, isPending, isError, refetch } = useExpeditionMap(expeditionId, active);
  const [source] = useState(() => new VectorSource());
  // Built only while the tab is the one on screen, over the background the reader has on the
  // workspace map; the hook disposes it, with its layers, when the tab or the page goes.
  const { targetRef, map, ready, frame } = useSmallMap({
    center: DEFAULT_CENTER,
    zoom: 9,
    active,
    layers: () => [new VectorLayer({ source, style: styleFor })],
  });

  useEffect(() => {
    source.clear();
    if (!data || !ready) {
      return;
    }
    // The container is laid out only while there is something to draw, and a cached empty answer
    // arrives before the first paint — so the map can have been built against a hidden container
    // and measured 0x0. Re-measuring here, after the render that laid the container out, is what
    // stops a later non-empty answer un-hiding a blank tile grid. Cheap and idempotent otherwise.
    map.current?.updateSize();
    source.addFeatures(new GeoJSON().readFeatures(data, { featureProjection: 'EPSG:3857' }));
    const extent = source.getExtent();
    // Framed again only when what is shown moves, so a re-read of the same camp keeps wherever
    // the reader had panned to.
    frame(`${expeditionId}|${extent?.join(',')}`, extent, { padding: [24, 24, 24, 24], maxZoom: 15 });
  }, [data, ready, source, map, frame, expeditionId]);

  const nothingToDraw = !isPending && data?.features.length === 0;
  // A read that failed leaves nothing on the map; the empty frame is hidden and the failure said,
  // so a blank map is never read as a camp that went nowhere.
  const readFailed = isError && !data;

  return (
    <div data-testid="expedition-map-tab">
      {isPending && <Spin />}
      {readFailed && (
        <Alert
          type="warning"
          showIcon
          data-testid="expedition-map-unavailable"
          title={t('expeditions.mapUnavailable')}
          action={
            <Button size="small" onClick={() => void refetch()}>
              {t('common.retry')}
            </Button>
          }
        />
      )}
      {nothingToDraw && <Empty description={t('expeditions.mapEmpty')} />}
      <div
        ref={targetRef}
        data-testid="expedition-map"
        style={{ width: '100%', height, display: nothingToDraw || readFailed ? 'none' : undefined }}
      />
      {/* A yes or no and never a number: how many entrances were left off would say how many
          caves the camp reached, which for a protected cave is exactly what is withheld. */}
      {data?.truncated && (
        <Alert
          type="warning"
          showIcon
          style={{ marginTop: 8 }}
          data-testid="expedition-map-truncated"
          title={t('expeditions.mapTruncated')}
        />
      )}
      <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
        {t('expeditions.mapCaveat')}
      </Typography.Paragraph>
    </div>
  );
}
