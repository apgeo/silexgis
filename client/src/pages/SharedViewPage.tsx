// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Card, Flex, Spin, Typography } from 'antd';
import Button from '../components/Button.tsx';
import Map from 'ol/Map';
import View from 'ol/View';
import type { FeatureLike } from 'ol/Feature';
import type Point from 'ol/geom/Point';
import { fromLonLat, transformExtent } from 'ol/proj';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { fetchSharedView } from '../api/hooks.ts';
import { tileLayerFor } from '../map/baseLayers.ts';
import { createSharedViewLayers } from '../map/sharedViewLayers.ts';

/** What a click on the shared map shows: a public name. */
interface Picked {
  name: string | null;
}

/**
 * Anonymous read-only page for a shared map view: the view's camera over the background it was
 * saved with, and the public cave entrances and surface features it had switched on.
 *
 * Everything drawn here comes from the link's own routes, which answer for nobody in particular:
 * rows that are themselves at the widest visibility level and that nothing protects, inside the
 * view's own area, with no identifiers. A protected cave or feature never reaches the page, not even
 * approximately, so every position drawn is where the record is. The page adds nothing to that and
 * has nothing to withhold — a click shows the name the server sent. The map is held inside the area the server names, so its reader meets the edge of
 * what was shared rather than data stopping for no reason they can see.
 */
export default function SharedViewPage() {
  const { t } = useTranslation();
  const { token } = useParams<{ token: string }>();
  const mapTarget = useRef<HTMLDivElement>(null);
  const [name, setName] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [noBackground, setNoBackground] = useState(false);
  const [picked, setPicked] = useState<Picked | null>(null);
  const [cluster, setCluster] = useState<number | null>(null);

  useEffect(() => {
    let disposed = false;
    let map: Map | null = null;
    let detach: (() => void) | undefined;

    const boot = async () => {
      try {
        const shared = await fetchSharedView(token!);
        if (disposed) {
          return;
        }
        setName(shared.name);
        const camera = shared.camera;
        const reach = shared.reach;
        const data = createSharedViewLayers(token!, shared.overlays);
        const base = shared.background ? tileLayerFor(shared.background, 0) : null;
        base?.setVisible(true);
        setNoBackground(!base);
        const created = new Map({
          target: mapTarget.current ?? undefined,
          layers: [...(base ? [base] : []), ...data.layers],
          view: new View({
            center: fromLonLat(camera ? [camera.longitude, camera.latitude] : [25.3, 45.7]),
            zoom: camera?.zoom ?? 8,
            ...(reach
              ? {
                  extent: transformExtent([reach.west, reach.south, reach.east, reach.north], 'EPSG:4326', 'EPSG:3857'),
                  minZoom: reach.minZoom,
                }
              : {}),
          }),
        });
        map = created;
        detach = data.attach(created);
        created.on('singleclick', (event) => {
          const hit = created.forEachFeatureAtPixel(event.pixel, (feature: FeatureLike) => feature);
          if (!hit) {
            setPicked(null);
            setCluster(null);
            return;
          }
          if (hit.get('cluster') === true) {
            // A count over a grid cell names nothing to show; it is opened by going closer.
            setPicked(null);
            setCluster(Number(hit.get('count') ?? 0));
            const view = created.getView();
            const geometry = hit.getGeometry() as Point | undefined;
            view.animate({
              center: geometry?.getCoordinates(),
              zoom: Math.min((view.getZoom() ?? 8) + 2, 18),
              duration: 300,
            });
            return;
          }
          setCluster(null);
          setPicked({
            name: (hit.get('name') as string | null | undefined) ?? null,
          });
        });
      } catch {
        if (!disposed) {
          setFailed(true);
        }
      }
    };

    void boot();
    return () => {
      disposed = true;
      detach?.();
      map?.setTarget(undefined);
    };
  }, [token]);

  if (failed) {
    return (
      <Flex align="center" justify="center" style={{ height: '100vh' }}>
        <Alert type="warning" showIcon title={t('views.sharedNotFound')} />
      </Flex>
    );
  }

  return (
    <Flex vertical style={{ height: '100vh' }}>
      <Flex align="center" style={{ padding: '8px 16px', borderBottom: '1px solid rgba(128,128,128,0.25)' }}>
        <Typography.Title level={5} style={{ margin: 0, flex: 1 }}>
          {name ?? <Spin size="small" />}
        </Typography.Title>
        <Typography.Text type="secondary">{t('app.name')}</Typography.Text>
      </Flex>
      {noBackground && <Alert type="info" showIcon banner title={t('views.sharedNoBackground')} />}
      <div style={{ flex: 1, position: 'relative' }}>
        <div ref={mapTarget} style={{ position: 'absolute', inset: 0 }} data-testid="shared-view-map" />
        {(picked || cluster !== null) && (
          <Card
            size="small"
            style={{ position: 'absolute', left: 16, bottom: 16, maxWidth: 'calc(100% - 32px)' }}
            data-testid="shared-view-picked"
          >
            <Flex align="center" gap={8}>
              <Flex vertical style={{ flex: 1 }}>
                {picked && <Typography.Text strong>{picked.name ?? t('views.sharedUnnamed')}</Typography.Text>}
                {cluster !== null && (
                  <Typography.Text>{t('views.sharedClusterHint', { count: cluster })}</Typography.Text>
                )}
              </Flex>
              <Button
                size="small"
                type="text"
                onClick={() => {
                  setPicked(null);
                  setCluster(null);
                }}
              >
                {t('views.sharedClose')}
              </Button>
            </Flex>
          </Card>
        )}
      </div>
    </Flex>
  );
}
