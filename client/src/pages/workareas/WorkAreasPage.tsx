// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { Alert, Breadcrumb, Card, Empty, Flex, Spin, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import List from '../../components/List.tsx';
import { EnvironmentOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import type Feature from 'ol/Feature';
import type { FeatureLike } from 'ol/Feature';
import { Fill, Stroke, Style, Text } from 'ol/style';
import { transformExtent } from 'ol/proj';
import { ScaleLine } from 'ol/control';
import { useWorkAreas } from '../../api/hooks.ts';
import { useSmallMap } from '../../components/map/useSmallMap.ts';
import { childrenOf, colourFor, extentOfAll, pathTo, topLevel } from '../../workareas/tree.ts';

/**
 * The overview of the ground this club works: every area at one level, told apart by colour and
 * named on the map, with the level beneath each one a click away.
 *
 * A map of its own rather than the workspace map. That one is a module-level singleton — one
 * instance for the whole application, holding the reader's layers, filters and camera — so
 * showing this view through it would take the map away from the map page and hand it back
 * scrolled somewhere else with different layers on. This canvas draws one thing and owns nothing.
 */
export default function WorkAreasPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data, isLoading, isError, refetch } = useWorkAreas();

  /** The area whose inside is being shown, or null at the top level. */
  const [openId, setOpenId] = useState<string | null>(null);

  const areas = useMemo(() => data?.items ?? [], [data]);
  const trail = useMemo(() => (openId ? pathTo(areas, openId) : []), [areas, openId]);
  const level = useMemo(
    () => (openId ? childrenOf(areas, openId) : topLevel(areas)),
    [areas, openId],
  );

  const source = useRef(new VectorSource());
  // The click handler outlives renders on the map, so it reads the current list and navigation
  // through a ref: a stale one would drill into whichever area used to be under that pixel.
  const latest = useRef({ areas, navigate });
  latest.current = { areas, navigate };

  // Built once its container is on the page — which is not on the first render while the areas
  // are still loading — and kept: the level's shapes are swapped on the source below rather than
  // by rebuilding the map, so drilling in does not re-create a canvas the reader is looking at.
  const { targetRef, ready, frame } = useSmallMap({
    center: [0, 0],
    zoom: 2,
    layers: () => [new VectorLayer({ source: source.current, style: areaStyle })],
    // Plain tiles only: this overview draws one background and has no use for a map service, whose
    // pictures are rendered on request by somebody's server and are kept to the workspace map and
    // the 3D scene.
    backgrounds: (entry) => entry.layerKind === 'xyz',
    onCreate: (map) => {
      map.addControl(new ScaleLine());
      // Clicking a shape does what clicking its row in the list does.
      map.on('click', (event) => {
        const hit = map.forEachFeatureAtPixel(
          event.pixel,
          (feature) => feature.get('areaId') as string | undefined,
        );
        const area = hit ? latest.current.areas.find((a) => a.id === hit) : undefined;
        if (!area) {
          return;
        }
        // An area with nothing inside it is where drilling stops and the real map takes over.
        if (area.childCount > 0) {
          setOpenId(area.id);
        } else {
          latest.current.navigate(`/map?area=${encodeURIComponent(area.id)}`);
        }
      });
    },
  });

  // The shapes of the level being shown, and the camera framed on them once per level.
  const levelKey = level.map((area) => area.id).join(',');
  useEffect(() => {
    const format = new GeoJSON();
    const features: Feature[] = [];
    level.forEach((area, index) => {
      if (!area.geometry) {
        // Listed beside the map but not drawn on it: an area nobody has outlined yet has no
        // boundary, and inventing one would put a shape on the map that nobody agreed to.
        return;
      }
      const feature = format.readFeature(
        { type: 'Feature', geometry: area.geometry, properties: {} },
        { dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857' },
      ) as Feature;
      feature.set('areaId', area.id);
      feature.set('name', area.name);
      feature.set('colour', colourFor(index));
      features.push(feature);
    });
    source.current.clear();
    source.current.addFeatures(features);
  }, [level]);

  useEffect(() => {
    if (!ready) {
      return;
    }
    const extent = extentOfAll(level);
    frame(levelKey, extent ? transformExtent(extent, 'EPSG:4326', 'EPSG:3857') : null, {
      padding: [40, 40, 40, 40],
      duration: 250,
      maxZoom: 14,
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the level is named by its key
  }, [ready, levelKey, frame]);

  if (isLoading) {
    return <Flex justify="center" style={{ padding: 48 }}><Spin /></Flex>;
  }
  if (isError) {
    return (
      <Alert
        type="error"
        showIcon
        title={t('workAreas.loadFailed')}
        action={<Button onClick={() => void refetch()}>{t('common.retry')}</Button>}
        style={{ margin: 24 }}
      />
    );
  }

  return (
    <Flex vertical gap={16} style={{ padding: 24, height: '100%' }}>
      <Flex justify="space-between" align="center" wrap gap={12}>
        <Typography.Title level={4} style={{ margin: 0 }}>{t('workAreas.title')}</Typography.Title>
        <Breadcrumb
          items={[
            // Buttons, not bare anchors: an anchor without an address is skipped by Tab, so the
            // trail could only be climbed with a pointer.
            {
              title: (
                <Button type="link" size="small" className="link-button" onClick={() => setOpenId(null)}>
                  {t('workAreas.allAreas')}
                </Button>
              ),
            },
            ...trail.map((area) => ({
              title: (
                <Button type="link" size="small" className="link-button" onClick={() => setOpenId(area.id)}>
                  {area.name ?? t('workAreas.untitled')}
                </Button>
              ),
            })),
          ]}
        />
      </Flex>

      {data?.truncated ? <Alert type="warning" showIcon title={t('workAreas.truncated')} /> : null}

      {areas.length === 0 ? (
        <Empty description={t('workAreas.none')} />
      ) : (
        <Flex gap={16} align="stretch" style={{ flex: 1, minHeight: 360 }} wrap>
          <Card
            size="small"
            title={t('workAreas.thisLevel')}
            style={{ width: 280, flexShrink: 0, overflow: 'auto' }}
            styles={{ body: { padding: 0 } }}
          >
            <List
              size="small"
              dataSource={level}
              locale={{ emptyText: t('workAreas.noSubAreas') }}
              renderItem={(area, index) => (
                <List.Item
                  style={{ cursor: 'pointer', paddingInline: 12 }}
                  onClick={() => (area.childCount > 0
                    ? setOpenId(area.id)
                    : navigate(`/map?area=${encodeURIComponent(area.id)}`))}
                  actions={[
                    <Button
                      key="map"
                      type="link"
                      size="small"
                      icon={<EnvironmentOutlined />}
                      onClick={(event) => {
                        event.stopPropagation();
                        navigate(`/map?area=${encodeURIComponent(area.id)}`);
                      }}
                    >
                      {t('workAreas.openOnMap')}
                    </Button>,
                  ]}
                >
                  <List.Item.Meta
                    avatar={(
                      <span
                        aria-hidden
                        style={{
                          display: 'inline-block', width: 12, height: 12, borderRadius: 2,
                          background: colourFor(index),
                        }}
                      />
                    )}
                    title={area.name ?? t('workAreas.untitled')}
                    description={
                      area.childCount > 0
                        ? t('workAreas.subAreaCount', { count: area.childCount })
                        : area.description
                    }
                  />
                </List.Item>
              )}
            />
          </Card>
          <div
            ref={targetRef}
            data-testid="work-areas-map"
            style={{ flex: 1, minWidth: 320, minHeight: 360, borderRadius: 4, overflow: 'hidden' }}
          />
        </Flex>
      )}
    </Flex>
  );
}

function areaStyle(feature: FeatureLike) {
  return new Style({
    fill: new Fill({ color: `${feature.get('colour') as string}33` }),
    stroke: new Stroke({ color: feature.get('colour') as string, width: 2 }),
    text: new Text({
      text: (feature.get('name') as string | null) ?? '',
      font: '600 13px sans-serif',
      fill: new Fill({ color: '#1f1f1f' }),
      // The names sit over aerial imagery as often as over a plain background, so the halo is
      // what keeps them readable rather than the fill colour.
      stroke: new Stroke({ color: 'rgba(255,255,255,0.85)', width: 3 }),
      overflow: true,
    }),
  });
}
