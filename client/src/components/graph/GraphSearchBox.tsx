// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Input, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

export interface GraphSearchBoxProps {
  value: string;
  onChange: (text: string) => void;
  /** How many loaded items the text finds. */
  matchCount: number;
  /** One-based place of the selected item among the matches, or 0 when it is not one. */
  position: number;
  /** Enter: the next match. */
  onNext: () => void;
  /** Shift+Enter: the previous match. */
  onPrevious: () => void;
}

/** Finds items already on the graph by their title; the matches light up and Enter walks them. */
export default function GraphSearchBox(p: GraphSearchBoxProps) {
  const { t } = useTranslation();
  const typed = p.value.trim() !== '';
  return (
    <Flex align="center" gap={8}>
      <Input.Search
        value={p.value}
        onChange={(e) => p.onChange(e.target.value)}
        onSearch={() => p.onNext()}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && e.shiftKey) {
            e.preventDefault();
            p.onPrevious();
          }
        }}
        allowClear
        enterButton={false}
        placeholder={t('graph.search.placeholder')}
        aria-label={t('graph.search.label')}
        style={{ width: 240 }}
        data-testid="graph-search-input"
      />
      {typed && (
        <Typography.Text type="secondary" role="status" data-testid="graph-search-count">
          {p.matchCount === 0
            ? t('graph.search.none')
            : p.position > 0
              ? t('graph.search.position', { position: p.position, count: p.matchCount })
              : t('graph.search.count', { count: p.matchCount })}
        </Typography.Text>
      )}
    </Flex>
  );
}
