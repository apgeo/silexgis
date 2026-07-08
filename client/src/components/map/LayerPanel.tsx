// SPDX-License-Identifier: AGPL-3.0-or-later
import { Checkbox, Divider, Radio, Select, Slider, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useTags, type GeofileInfo, type MapLayerInfo, type RasterMapInfo } from '../../api/hooks.ts';

interface LayerPanelProps {
  layers: MapLayerInfo[];
  activeBaseId: number | undefined;
  onBaseChange: (id: number) => void;
  entrancesVisible: boolean;
  onEntrancesVisibleChange: (visible: boolean) => void;
  surfaceFeaturesVisible: boolean;
  onSurfaceFeaturesVisibleChange: (visible: boolean) => void;
  geofiles: GeofileInfo[];
  visibleGeofileIds: string[];
  onGeofileVisibleChange: (id: string, visible: boolean) => void;
  rasters: RasterMapInfo[];
  visibleRasterIds: string[];
  onRasterVisibleChange: (id: string, visible: boolean) => void;
  rasterOpacity: Record<string, number>;
  onRasterOpacityChange: (id: string, opacity: number) => void;
  tagFilter: string | null;
  onTagFilterChange: (slug: string | null) => void;
}

export default function LayerPanel({
  layers,
  activeBaseId,
  onBaseChange,
  entrancesVisible,
  onEntrancesVisibleChange,
  surfaceFeaturesVisible,
  onSurfaceFeaturesVisibleChange,
  geofiles,
  visibleGeofileIds,
  onGeofileVisibleChange,
  rasters,
  visibleRasterIds,
  onRasterVisibleChange,
  rasterOpacity,
  onRasterOpacityChange,
  tagFilter,
  onTagFilterChange,
}: LayerPanelProps) {
  const { t } = useTranslation();
  const { data: tags } = useTags('');

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
      <Select
        allowClear
        showSearch
        size="small"
        optionFilterProp="label"
        placeholder={t('tags.filterPlaceholder')}
        style={{ width: '100%', marginTop: 8 }}
        value={tagFilter ?? undefined}
        options={tags?.map((x) => ({ value: x.slug, label: x.name }))}
        onChange={(value?: string) => onTagFilterChange(value ?? null)}
      />
      <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 6 }}>
        <Checkbox checked={entrancesVisible} onChange={(e) => onEntrancesVisibleChange(e.target.checked)}>
          {t('map.entrances')}
        </Checkbox>
        <Checkbox
          checked={surfaceFeaturesVisible}
          onChange={(e) => onSurfaceFeaturesVisibleChange(e.target.checked)}
        >
          {t('map.surfaceFeatures')}
        </Checkbox>
      </div>
      {geofiles.length > 0 && (
        <>
          <Divider style={{ margin: '12px 0' }} />
          <Typography.Text strong>{t('map.geofiles')}</Typography.Text>
          <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 6 }}>
            {geofiles.map((geofile) => (
              <Checkbox
                key={geofile.id}
                checked={visibleGeofileIds.includes(geofile.id)}
                onChange={(e) => onGeofileVisibleChange(geofile.id, e.target.checked)}
              >
                {geofile.name}
              </Checkbox>
            ))}
          </div>
        </>
      )}
      {rasters.length > 0 && (
        <>
          <Divider style={{ margin: '12px 0' }} />
          <Typography.Text strong>{t('map.rasterMaps')}</Typography.Text>
          <div style={{ marginTop: 8, display: 'flex', flexDirection: 'column', gap: 4 }}>
            {rasters.map((raster) => {
              const visible = visibleRasterIds.includes(raster.id);
              return (
                <div key={raster.id}>
                  <Checkbox
                    checked={visible}
                    onChange={(e) => onRasterVisibleChange(raster.id, e.target.checked)}
                  >
                    {raster.name}
                  </Checkbox>
                  {visible && (
                    <Slider
                      min={0}
                      max={1}
                      step={0.05}
                      style={{ margin: '0 8px 8px 24px' }}
                      value={rasterOpacity[raster.id] ?? Number(raster.defaultOpacity)}
                      onChange={(value) => onRasterOpacityChange(raster.id, value)}
                    />
                  )}
                </div>
              );
            })}
          </div>
        </>
      )}
    </div>
  );
}
