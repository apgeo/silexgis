// SPDX-License-Identifier: AGPL-3.0-or-later
import { Button, Card, Flex, Input, Segmented, Select, Space, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { ImportTargetKind } from '../../api/hooks.ts';

/**
 * What the review table is narrowed to. Lives with the toolbar that produces it rather than
 * with the page that holds it, so the two do not import each other.
 */
export interface CandidateFilters {
  rule?: string;
  kind?: ImportTargetKind;
  geometry?: 'point' | 'line' | 'area' | 'other';
  search?: string;
  hasDuplicate?: boolean;
}

interface RuleHit {
  ruleId: string;
  ruleName: string;
  count: number;
}

interface Props {
  filters: CandidateFilters;
  onFiltersChange: (next: CandidateFilters) => void;
  ruleHits: RuleHit[];
  selected: ReadonlySet<number>;
  onSelectedChange: (next: ReadonlySet<number>) => void;
  /** Every row matching the current filter, not just the page — what the count is out of. */
  filteredIds: number[];
  /**
   * Those of them something says what to become. What "all" and "invert" act on: a row nobody
   * has given a kind cannot be created, and taking it anyway would turn one press into a
   * failure line per row at confirmation.
   */
  selectableIds: number[];
  disabled: boolean;
  counts: { candidates: number; matched: number; unmatched: number; tracks: number; areas: number };
}

/**
 * Picking rows without paging through them. "All" means everything the filter matches rather
 * than everything on screen, which is the whole point of filtering by rule or by kind first —
 * one filter and one click is how four hundred waypoints get selected.
 */
export default function SelectionToolbar({
  filters,
  onFiltersChange,
  ruleHits,
  selected,
  onSelectedChange,
  filteredIds,
  selectableIds,
  disabled,
  counts,
}: Props) {
  const { t } = useTranslation();

  const selectAll = () => onSelectedChange(new Set(selectableIds));
  const selectNone = () => onSelectedChange(new Set());
  const invert = () => {
    const next = new Set(selected);
    for (const id of selectableIds) {
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
    }
    onSelectedChange(next);
  };

  return (
    <Card size="small" styles={{ body: { paddingBlock: 10 } }}>
      <Flex vertical gap={10}>
        <Flex gap={8} wrap align="center">
          <Space.Compact>
            <Button size="small" disabled={disabled} onClick={selectAll} data-testid="import-select-all">
              {t('vectorImport.selectAll')}
            </Button>
            <Button size="small" disabled={disabled} onClick={selectNone}>
              {t('vectorImport.selectNone')}
            </Button>
            <Button size="small" disabled={disabled} onClick={invert}>
              {t('vectorImport.selectInvert')}
            </Button>
          </Space.Compact>
          <Typography.Text type="secondary">
            {t('vectorImport.selectedCount', { count: selected.size, total: filteredIds.length })}
          </Typography.Text>
        </Flex>

        <Flex gap={8} wrap align="center">
          <Input.Search
            size="small"
            allowClear
            style={{ maxWidth: 220 }}
            placeholder={t('vectorImport.searchPlaceholder')}
            value={filters.search ?? ''}
            onChange={(e) => onFiltersChange({ ...filters, search: e.target.value || undefined })}
          />
          <Select
            size="small"
            allowClear
            style={{ minWidth: 190 }}
            placeholder={t('vectorImport.filterByRule')}
            value={filters.rule}
            onChange={(value?: string) => onFiltersChange({ ...filters, rule: value })}
            data-testid="import-filter-rule"
            options={[
              { value: 'none', label: t('vectorImport.noRule') },
              ...ruleHits.map((hit) => ({
                value: hit.ruleId,
                label: `${hit.ruleName} (${hit.count})`,
              })),
            ]}
          />
          <Select
            size="small"
            allowClear
            style={{ minWidth: 170 }}
            placeholder={t('vectorImport.filterByKind')}
            value={filters.kind}
            onChange={(value?: ImportTargetKind) => onFiltersChange({ ...filters, kind: value })}
            options={(['cave', 'caveEntrance', 'surfaceFeature'] as const).map((kind) => ({
              value: kind,
              label: t(`vectorImport.kinds.${kind}`),
            }))}
          />
          <Segmented
            size="small"
            value={filters.geometry ?? 'all'}
            onChange={(value) =>
              onFiltersChange({
                ...filters,
                geometry: value === 'all' ? undefined : (value as CandidateFilters['geometry']),
              })
            }
            options={[
              { value: 'all', label: t('vectorImport.geometryAll') },
              { value: 'point', label: t('vectorImport.geometry.point') },
              { value: 'line', label: t('vectorImport.geometry.line') },
              { value: 'area', label: t('vectorImport.geometry.area') },
            ]}
          />
          <Button
            size="small"
            type={filters.hasDuplicate ? 'primary' : 'default'}
            onClick={() =>
              onFiltersChange({ ...filters, hasDuplicate: filters.hasDuplicate ? undefined : true })
            }
          >
            {t('vectorImport.onlyWithNearby')}
          </Button>
        </Flex>

        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('vectorImport.fileSummary', {
            candidates: counts.candidates,
            matched: counts.matched,
            unmatched: counts.unmatched,
            tracks: counts.tracks,
            areas: counts.areas,
          })}
        </Typography.Text>
      </Flex>
    </Card>
  );
}
