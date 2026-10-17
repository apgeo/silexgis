// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Flex, InputNumber, Select, Space, Tooltip } from 'antd';
import { ArrowDownOutlined, ArrowUpOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { anchorScopes, isOnGlobe } from '../../filters/spatial.ts';
import type { FilterAnchor, SortKey } from '../../filters/types.ts';
import Button from '../Button.tsx';
import ObjectSelector from '../selector/ObjectSelector.tsx';

/**
 * How results are ordered, and — for nearest first — what "near" is measured from.
 *
 * Nearest first has no direction: the server always ranks the rows the reader may place exactly by
 * distance, nearest first, and puts every other row after them in the ordinary order. So the
 * direction button is not offered with it, rather than offered and ignored. The anchor is a point
 * typed here or an object picked from this world; whether the reader may measure from that object
 * is the server's to say, and a refusal of it is shown with the filter as a whole.
 */

export interface SortState {
  sort: SortKey;
  descending: boolean;
  anchor: FilterAnchor | null;
}

export interface SortControlProps {
  world: string;
  sorts: readonly SortKey[];
  value: SortState;
  onChange: (value: SortState) => void;
}

export default function SortControl({ world, sorts, value, onChange }: SortControlProps) {
  const { t } = useTranslation();
  const [kind, setKind] = useState<'object' | 'point'>(value.anchor?.anchor === 'point' ? 'point' : 'object');
  const [lon, setLon] = useState<number | null>(value.anchor?.anchor === 'point' ? value.anchor.longitude : null);
  const [lat, setLat] = useState<number | null>(value.anchor?.anchor === 'point' ? value.anchor.latitude : null);
  const nearest = value.sort === 'proximity';

  const putPoint = (nextLon: number | null, nextLat: number | null) => {
    setLon(nextLon);
    setLat(nextLat);
    onChange({
      ...value,
      anchor: isOnGlobe(nextLon, nextLat) ? { anchor: 'point', longitude: nextLon!, latitude: nextLat! } : null,
    });
  };

  return (
    <Flex gap={8} wrap align="center" data-testid="filter-sort">
      <Select
        aria-label={t('filters.editor.sort')}
        data-testid="filter-sort-key"
        value={value.sort}
        onChange={(sort: SortKey) => onChange({
          sort,
          // Title reads A to Z; everything else here is read newest first.
          descending: sort !== 'title',
          anchor: sort === 'proximity' ? value.anchor : null,
        })}
        options={sorts.map((s) => ({ value: s, label: t(`filters.sorts.${s}`) }))}
        style={{ width: 180 }}
      />
      {!nearest && (
        <Tooltip title={t(value.descending ? 'filters.editor.descending' : 'filters.editor.ascending')}>
          <Button
            aria-label={t(value.descending ? 'filters.editor.descending' : 'filters.editor.ascending')}
            aria-pressed={value.descending}
            icon={value.descending ? <ArrowDownOutlined /> : <ArrowUpOutlined />}
            onClick={() => onChange({ ...value, descending: !value.descending })}
            data-testid="filter-sort-direction"
          />
        </Tooltip>
      )}
      {nearest && (
        <>
          <Select
            aria-label={t('filters.editor.spatial.anchorKind')}
            data-testid="filter-sort-anchor-kind"
            value={kind}
            onChange={(next: 'object' | 'point') => {
              setKind(next);
              setLon(null);
              setLat(null);
              onChange({ ...value, anchor: null });
            }}
            options={(['object', 'point'] as const).map((k) => ({
              value: k,
              label: t(`filters.editor.spatial.anchorKinds.${k}`),
            }))}
            style={{ width: 170 }}
          />
          {kind === 'point' && (
            <Space.Compact>
              <InputNumber
                aria-label={t('filters.editor.spatial.longitude')}
                placeholder={t('filters.editor.spatial.longitude')}
                data-testid="filter-sort-anchor-lon"
                min={-180}
                max={180}
                value={lon}
                onChange={(n) => putPoint(typeof n === 'number' ? n : null, lat)}
              />
              <InputNumber
                aria-label={t('filters.editor.spatial.latitude')}
                placeholder={t('filters.editor.spatial.latitude')}
                data-testid="filter-sort-anchor-lat"
                min={-90}
                max={90}
                value={lat}
                onChange={(n) => putPoint(lon, typeof n === 'number' ? n : null)}
              />
            </Space.Compact>
          )}
          {kind === 'object' && (
            <ObjectSelector
              scopes={anchorScopes(world, 'object')}
              presentation="dropdown"
              sorts={[]}
              value={value.anchor?.anchor === 'object' ? value.anchor.id : null}
              onChange={(id) => onChange({
                ...value,
                anchor: typeof id === 'string' ? { anchor: 'object', world, id } : null,
              })}
              aria-label={t('filters.editor.spatial.object')}
              data-testid="filter-sort-anchor-object"
            />
          )}
        </>
      )}
    </Flex>
  );
}
