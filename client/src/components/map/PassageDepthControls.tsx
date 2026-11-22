// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Typography, theme } from 'antd';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { passageDepthPalette } from '../../map/markerPalette.ts';
import { passageDepthClassLabels } from './passageDepthLabels.ts';
import {
  getPassageDepthLoadState,
  subscribePassageDepthLoadState,
  type PassageDepthLoadState,
} from '../../map/passageDepthLayer.ts';
import { FONT_SIZE } from '../../theme.ts';

/** The key: one swatch per class, in metres of rock over the passage. */
export function PassageDepthLegend({ state }: { state: PassageDepthLoadState }) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const labels = passageDepthClassLabels(state.classEdgesM, t);
  return (
    <ul data-testid="map-passage-depth-legend" style={{ listStyle: 'none', margin: '4px 0 0', padding: 0, fontSize: FONT_SIZE.sm }}>
      {labels.map((label, index) => (
        <li key={index} data-testid="map-passage-depth-legend-row">
          <span
            aria-hidden
            data-testid="map-passage-depth-legend-swatch"
            style={{
              display: 'inline-block',
              width: 22,
              height: 4,
              marginInlineEnd: 6,
              verticalAlign: 'middle',
              background: passageDepthPalette.classes[Math.min(index, passageDepthPalette.classes.length - 1)],
              boxShadow: `0 0 0 1px ${token.colorBorder}`,
            }}
          />
          {label}
        </li>
      ))}
    </ul>
  );
}

/**
 * The depth-coloured passage overlay's row in the layer panel: its key, a note when the window held
 * more caves than one answer draws, and a refusal, a missing terrain or a failure said as such
 * rather than left as an empty map.
 */
export default function PassageDepthControls() {
  const { t } = useTranslation();
  const [load, setLoad] = useState<PassageDepthLoadState>(getPassageDepthLoadState);
  useEffect(() => subscribePassageDepthLoadState(setLoad), []);

  return (
    <div style={{ marginTop: 8 }} data-testid="map-passage-depth">
      {load.status === 'refused' ? (
        <Alert
          type="warning"
          showIcon
          data-testid="map-passage-depth-refused"
          title={t(
            load.refusal === 'map.invalid_bbox'
              ? 'map.passageDepth.refused.window'
              : load.refusalStatus === 403
                ? 'map.passageDepth.refused.terrain'
                : 'map.passageDepth.refused.other',
          )}
        />
      ) : load.status === 'error' ? (
        <Alert type="warning" showIcon title={t('map.passageDepth.failed')} />
      ) : load.status === 'ok' && !load.hasTerrain ? (
        <Alert type="info" showIcon data-testid="map-passage-depth-no-terrain" title={t('map.passageDepth.noTerrain')} />
      ) : load.status === 'ok' ? (
        <>
          <Typography.Text type="secondary" className="hint-text" data-testid="map-passage-depth-note">
            {load.caveCount > 0
              ? t('map.passageDepth.shown', { count: load.caveCount })
              : t('map.passageDepth.none')}
          </Typography.Text>
          <PassageDepthLegend state={load} />
          {load.truncated && (
            <Alert
              type="info"
              showIcon
              style={{ marginTop: 4 }}
              data-testid="map-passage-depth-truncated"
              title={t('map.passageDepth.truncated', { max: load.maxCaves ?? load.caveCount })}
            />
          )}
        </>
      ) : (
        <Typography.Text type="secondary" className="hint-text">
          {t('map.passageDepth.loading')}
        </Typography.Text>
      )}
    </div>
  );
}
