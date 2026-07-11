// SPDX-License-Identifier: AGPL-3.0-or-later
import { GlobalOutlined } from '@ant-design/icons';
import { Button, Popover, Radio } from 'antd';
import { useTranslation } from 'react-i18next';
import type { MapLayerInfo } from '../../api/hooks.ts';

interface BaseLayerSwitcherProps {
  layers: MapLayerInfo[];
  activeBaseId?: number;
  onChange: (id: number) => void;
}

/**
 * Compact on-canvas base-map picker (mirrors the layer panel's base radio) so the
 * user can swap OSM/aerial/topo without opening the left dock. Reference software
 * kept the same control floating on the map.
 */
export default function BaseLayerSwitcher({ layers, activeBaseId, onChange }: BaseLayerSwitcherProps) {
  const { t } = useTranslation();
  const bases = layers.filter((l) => l.isBase);
  if (bases.length === 0) {
    return null;
  }

  const content = (
    <Radio.Group
      style={{ display: 'flex', flexDirection: 'column', gap: 4 }}
      value={activeBaseId}
      onChange={(e) => onChange(e.target.value as number)}
      options={bases.map((l) => ({ value: Number(l.id), label: l.name }))}
    />
  );

  return (
    <Popover content={content} title={t('map.baseLayers')} trigger="click" placement="bottomRight">
      <Button size="small" icon={<GlobalOutlined />} aria-label={t('map.baseLayers')} />
    </Popover>
  );
}
