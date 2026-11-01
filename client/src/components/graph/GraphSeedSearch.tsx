// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { SearchOutlined } from '@ant-design/icons';
import { AutoComplete, Flex, Input, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useGraphSearch } from '../../api/hooks/graph.ts';
import type { GraphSearchHit } from '../../api/hooks/types.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { graphAddress } from '../../graph/explorerStore.ts';
import { nodeTypeLabel, type Translate } from '../../graph/visuals.ts';

/** Quiet period after the last keystroke before the server is asked. */
export const SEED_SEARCH_DEBOUNCE_MS = 250;

export interface GraphSeedSearchProps {
  /** Fill the container instead of the fixed toolbar width. */
  fullWidth?: boolean;
  autoFocus?: boolean;
}

/**
 * Chooses the item to explore: types a name, the server answers with a few matches per kind (as
 * this person may see them), and picking one opens the graph for it. Nothing is asked until
 * something is typed.
 */
export default function GraphSeedSearch({ fullWidth = false, autoFocus = false }: GraphSeedSearchProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [query, setQuery] = useState('');
  const debounced = useDebouncedValue(query, SEED_SEARCH_DEBOUNCE_MS);
  const { data: hits } = useGraphSearch(debounced);

  const options = useMemo(() => {
    const byType = new Map<string, GraphSearchHit[]>();
    for (const hit of hits ?? []) {
      (byType.get(hit.type) ?? byType.set(hit.type, []).get(hit.type)!).push(hit);
    }
    return [...byType].map(([type, group]) => ({
      label: nodeTypeLabel(type, t as unknown as Translate),
      options: group.map((hit) => ({
        value: hit.key,
        label: (
          <Flex vertical>
            <span>{hit.title}</span>
            {hit.subtitle && <Typography.Text type="secondary" style={{ fontSize: 12 }}>{hit.subtitle}</Typography.Text>}
          </Flex>
        ),
      })),
    }));
  }, [hits, t]);

  const settled = debounced.trim() !== '' && debounced === query;
  const empty = settled && hits !== undefined && hits.length === 0;

  const onSelect = (key: string) => {
    const hit = hits?.find((h) => h.key === key);
    if (hit !== undefined) {
      setQuery('');
      void navigate(graphAddress({ type: hit.type, id: hit.id }));
    }
  };

  return (
    <AutoComplete
      value={query}
      options={options}
      onSearch={setQuery}
      onChange={setQuery}
      onSelect={onSelect}
      style={{ width: fullWidth ? '100%' : 280, maxWidth: '100%' }}
      popupMatchSelectWidth={fullWidth ? true : 360}
      notFoundContent={empty ? t('graph.seedSearch.none') : null}
      data-testid="graph-seed-search"
    >
      <Input
        prefix={<SearchOutlined />}
        placeholder={t('graph.seedSearch.placeholder')}
        aria-label={t('graph.seedSearch.label')}
        allowClear
        autoFocus={autoFocus}
        data-testid="graph-seed-search-input"
      />
    </AutoComplete>
  );
}
