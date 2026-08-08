// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Flex, Spin, Typography } from 'antd';
import Map from 'ol/Map';
import View from 'ol/View';
import TileLayer from 'ol/layer/Tile';
import { fromLonLat } from 'ol/proj';
import XYZ from 'ol/source/XYZ';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { fetchSharedView } from '../api/hooks.ts';

interface SharedConfig {
  configVersion?: number;
  center?: [number, number];
  zoom?: number;
}

/**
 * Anonymous read-only page for a shared map view. It renders the shared camera over a
 * public base map; protected data never appears here — the share token only carries the
 * view document, and every data endpoint keeps enforcing its own access rules.
 */
export default function SharedViewPage() {
  const { t } = useTranslation();
  const { token } = useParams<{ token: string }>();
  const mapTarget = useRef<HTMLDivElement>(null);
  const [name, setName] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let disposed = false;
    let map: Map | null = null;

    const boot = async () => {
      try {
        const shared = await fetchSharedView(token!);
        if (disposed) {
          return;
        }
        setName(shared.name);
        const config = shared.config as SharedConfig;
        map = new Map({
          target: mapTarget.current ?? undefined,
          layers: [
            new TileLayer({
              source: new XYZ({
                url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                attributions: '© OpenStreetMap contributors',
              }),
            }),
          ],
          view: new View({
            center: fromLonLat(config.center ?? [25.3, 45.7]),
            zoom: config.zoom ?? 8,
          }),
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
      <div ref={mapTarget} style={{ flex: 1 }} />
    </Flex>
  );
}
