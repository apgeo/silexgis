// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Checkbox, Flex, Typography } from 'antd';
import Button from '../Button.tsx';
import { DownOutlined, RightOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { isCategoryEnabled, isFiltered, isTypeEnabled, relationFilterKey, type FilterFacets } from '../../graph/filters.ts';
import type { GraphEdge, GraphFilters } from '../../graph/types.ts';
import type { GraphText } from './useGraphPresentation.ts';

export interface GraphFilterPanelProps {
  facets: FilterFacets;
  /** One edge per relation code, to word the code the way the canvas does. */
  samples: ReadonlyMap<string, GraphEdge>;
  filters: GraphFilters;
  text: GraphText;
  onToggleCategory: (category: string, enabled: boolean) => void;
  onToggleRelation: (category: string, relation: string, enabled: boolean) => void;
  onToggleType: (type: string, enabled: boolean) => void;
  onOnlyCategory: (category: string) => void;
  onOnlyType: (type: string) => void;
  onReset: () => void;
}

/**
 * What to show: relation categories (and inside each, single relation codes) and item types, any
 * number on or off at once. It offers only what the explored graph holds, so it grows as the graph
 * does. Nothing here fetches; the view is rebuilt locally from the same model.
 */
export default function GraphFilterPanel(p: GraphFilterPanelProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set());
  const toggleOpen = (category: string) =>
    setOpen((prev) => {
      const next = new Set(prev);
      if (!next.delete(category)) {
        next.add(category);
      }
      return next;
    });

  const categories = [...p.facets.categories].sort((a, b) => p.text.categoryName(a.category).localeCompare(p.text.categoryName(b.category)));
  const types = [...p.facets.types].sort((a, b) => p.text.typeName(a.type).localeCompare(p.text.typeName(b.type)));

  return (
    <Flex vertical gap={12} data-testid="graph-filters" role="group" aria-label={t('graph.filters.title')}>
      <Flex justify="space-between" align="center">
        <Typography.Text strong>{t('graph.filters.title')}</Typography.Text>
        <Button size="small" type="link" disabled={!isFiltered(p.filters)} onClick={p.onReset} data-testid="graph-filter-all">
          {t('graph.filters.all')}
        </Button>
      </Flex>

      <Typography.Text type="secondary">{t('graph.filters.categories')}</Typography.Text>
      {categories.length === 0 && <Typography.Text type="secondary">{t('graph.filters.none')}</Typography.Text>}
      <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
        {categories.map((c) => {
          const enabled = isCategoryEnabled(p.filters, c.category);
          const offCodes = c.relations.filter((r) => p.filters.disabledRelations.has(relationFilterKey(c.category, r.relation))).length;
          const expanded = open.has(c.category);
          const name = p.text.categoryName(c.category);
          return (
            <li key={c.category} data-testid="graph-filter-category" data-category={c.category} style={{ marginBottom: 4 }}>
              <Flex align="center" gap={4}>
                <Button
                  size="small"
                  type="text"
                  aria-expanded={expanded}
                  aria-label={t(expanded ? 'graph.filters.hideRelations' : 'graph.filters.showRelations', { name })}
                  icon={expanded ? <DownOutlined /> : <RightOutlined />}
                  onClick={() => toggleOpen(c.category)}
                  data-testid="graph-filter-expand"
                />
                <Checkbox
                  checked={enabled}
                  indeterminate={enabled && offCodes > 0}
                  onChange={(e) => p.onToggleCategory(c.category, e.target.checked)}
                  data-testid="graph-filter-category-check"
                >
                  {name} <Typography.Text type="secondary">({c.edges})</Typography.Text>
                </Checkbox>
                <Button size="small" type="link" onClick={() => p.onOnlyCategory(c.category)} data-testid="graph-filter-category-only">
                  {t('graph.filters.only')}
                </Button>
              </Flex>
              {expanded && (
                <ul style={{ listStyle: 'none', margin: '4px 0 4px 32px', padding: 0 }}>
                  {c.relations.map((r) => {
                    const sample = p.samples.get(relationFilterKey(c.category, r.relation));
                    const label = sample ? p.text.relation({ ...sample, isMore: false }, 'forward') : r.relation;
                    return (
                      <li key={r.relation}>
                        <Checkbox
                          checked={enabled && !p.filters.disabledRelations.has(relationFilterKey(c.category, r.relation))}
                          disabled={!enabled}
                          onChange={(e) => p.onToggleRelation(c.category, r.relation, e.target.checked)}
                          data-testid="graph-filter-relation-check"
                          data-relation={r.relation}
                        >
                          {label || r.relation} <Typography.Text type="secondary">({r.edges})</Typography.Text>
                        </Checkbox>
                      </li>
                    );
                  })}
                </ul>
              )}
            </li>
          );
        })}
      </ul>

      <Typography.Text type="secondary">{t('graph.filters.types')}</Typography.Text>
      <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
        {types.map((ty) => (
          <li key={ty.type} data-testid="graph-filter-type" data-type={ty.type} style={{ marginBottom: 4 }}>
            <Flex align="center" gap={4}>
              <Checkbox
                checked={isTypeEnabled(p.filters, ty.type)}
                onChange={(e) => p.onToggleType(ty.type, e.target.checked)}
                data-testid="graph-filter-type-check"
              >
                {p.text.typeName(ty.type)} <Typography.Text type="secondary">({ty.nodes})</Typography.Text>
              </Checkbox>
              <Button size="small" type="link" onClick={() => p.onOnlyType(ty.type)} data-testid="graph-filter-type-only">
                {t('graph.filters.only')}
              </Button>
            </Flex>
          </li>
        ))}
      </ul>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {t('graph.filters.hint')}
      </Typography.Text>
    </Flex>
  );
}
