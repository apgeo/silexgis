// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Segmented, Typography } from 'antd';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  KARST_DENSITY_MODES,
  getKarstDensityLoadState,
  subscribeKarstDensityLoadState,
  type KarstDensityLoadState,
  type KarstDensityMode,
} from '../../map/karstDensityLayer.ts';
import { KarstDensityLegend } from './KarstLegend.tsx';

/** The refusals this row has words of its own for; anything else is said generically. */
const REFUSAL_KEYS: Record<string, string> = {
  'density.too_many_cells': 'map.karstDensity.refused.tooWide',
  // The overlay only ever asks for a multiple of the published floor, so the size validator
  // refusing it means the multiple needed for this window is beyond the largest cell served.
  'validation.failed': 'map.karstDensity.refused.tooWide',
  'density.cell_below_protection_grid': 'map.karstDensity.refused.tooFine',
  'map.invalid_bbox': 'map.karstDensity.refused.window',
};

/**
 * The density overlay's row in the layer panel: which reading fills the cells, the cell size the
 * server actually answered at, and a refusal said as one — an empty overlay and a refused window
 * look the same on a map, and only one of them means there are no caves here.
 */
export default function KarstDensityControls({
  mode,
  onModeChange,
}: {
  mode: KarstDensityMode;
  onModeChange: (mode: KarstDensityMode) => void;
}) {
  const { t } = useTranslation();
  const [load, setLoad] = useState<KarstDensityLoadState>(getKarstDensityLoadState);
  useEffect(() => subscribeKarstDensityLoadState(setLoad), []);

  return (
    <div style={{ marginTop: 8 }} data-testid="map-karst-density">
      <Segmented
        size="small"
        value={mode}
        onChange={(value) => onModeChange(value as KarstDensityMode)}
        aria-label={t('map.karstDensity.reading')}
        options={KARST_DENSITY_MODES.map((value) => ({
          value,
          label: t(`map.karstDensity.mode.${value}`),
        }))}
      />
      <div style={{ marginTop: 4 }}>
        {load.status === 'refused' ? (
          <Alert
            type="warning"
            showIcon
            data-testid="map-karst-density-refused"
            title={t(REFUSAL_KEYS[load.refusal ?? ''] ?? 'map.karstDensity.refused.other')}
          />
        ) : load.status === 'error' ? (
          <Alert type="warning" showIcon title={t('map.karstDensity.failed')} />
        ) : (
          <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="map-karst-density-cell">
            {load.status === 'ok' && load.cellMetres !== null
              ? t('map.karstDensity.cell', {
                  metres: Math.round(load.cellMetres),
                  floor: Math.round(load.minimumCellMetres ?? load.cellMetres),
                })
              : t('map.karstDensity.loading')}
          </Typography.Text>
        )}
        <KarstDensityLegend state={load} />
      </div>
    </div>
  );
}
