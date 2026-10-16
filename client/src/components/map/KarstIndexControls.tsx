// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Typography } from 'antd';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getKarstIndexLoadState,
  subscribeKarstIndexLoadState,
  type KarstIndexLoadState,
} from '../../map/karstIndexLayer.ts';
import { KarstIndexLegend } from './KarstLegend.tsx';

/**
 * The karstification-index overlay's row in the layer panel: its legend, a note when the window
 * held more areas than one answer carries, and a refusal or failure said as one rather than left
 * as an empty map.
 */
export default function KarstIndexControls() {
  const { t } = useTranslation();
  const [load, setLoad] = useState<KarstIndexLoadState>(getKarstIndexLoadState);
  useEffect(() => subscribeKarstIndexLoadState(setLoad), []);

  return (
    <div style={{ marginTop: 8 }} data-testid="map-karst-index">
      {load.status === 'refused' ? (
        <Alert
          type="warning"
          showIcon
          data-testid="map-karst-index-refused"
          title={t(load.refusal === 'map.invalid_bbox' ? 'map.karstIndex.refused.window' : 'map.karstIndex.refused.other')}
        />
      ) : load.status === 'error' ? (
        <Alert type="warning" showIcon title={t('map.karstIndex.failed')} />
      ) : load.status === 'ok' ? (
        <>
          {load.areaCount > 0 && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="map-karst-index-shown">
              {t('map.karstIndex.shown', { count: load.areaCount })}
            </Typography.Text>
          )}
          <KarstIndexLegend state={load} />
        </>
      ) : (
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('map.karstIndex.loading')}
        </Typography.Text>
      )}
    </div>
  );
}
