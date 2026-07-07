// SPDX-License-Identifier: AGPL-3.0-or-later
import { Checkbox, Divider, Radio, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { MapLayerInfo } from '../../api/hooks.ts';

interface LayerPanelProps {
  layers: MapLayerInfo[];
  activeBaseId: number | undefined;
  onBaseChange: (id: number) => void;
  entrancesVisible: boolean;
  onEntrancesVisibleChange: (visible: boolean) => void;
}

export default function LayerPanel({
  layers,
  activeBaseId,
  onBaseChange,
  entrancesVisible,
  onEntrancesVisibleChange,
}: LayerPanelProps) {
  const { t } = useTranslation();

  return (
    <div style={{ padding: 12, overflow: 'auto', height: '100%' }}>
      <Typography.Text strong>{t('map.baseLayers')}</Typography.Text>
      <Radio.Group
        style={{ display: 'flex', flexDirection: 'column', gap: 6, marginTop: 8 }}
        value={activeBaseId}
        onChange={(e) => onBaseChange(e.target.value as number)}
        options={layers.filter((l) => l.isBase).map((l) => ({ value: Number(l.id), label: l.name }))}
      />
      <Divider style={{ margin: '12px 0' }} />
      <Typography.Text strong>{t('map.overlays')}</Typography.Text>
      <div style={{ marginTop: 8 }}>
        <Checkbox checked={entrancesVisible} onChange={(e) => onEntrancesVisibleChange(e.target.checked)}>
          {t('map.entrances')}
        </Checkbox>
      </div>
    </div>
  );
}
