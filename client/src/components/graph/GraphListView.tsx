// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { LockOutlined } from '@ant-design/icons';
import { Empty, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { neighboursOf, type GraphView, type ViewNode } from '../../graph/view.ts';
import type { NodeKey } from '../../graph/types.ts';
import type { GraphText } from './useGraphPresentation.ts';

export interface GraphListViewProps {
  view: GraphView;
  text: GraphText;
  onSelect: (key: NodeKey) => void;
  /** Load the next page of one category of an expanded item; the list's "+N more" buttons call it. */
  onMore?: (nodeKey: NodeKey, category: string) => void;
}

// A bare button: no border or fill of its own, but the browser's focus ring is left alone.
const bareButton = {
  background: 'none',
  border: 'none',
  font: 'inherit',
  color: 'inherit',
  textAlign: 'left',
  cursor: 'pointer',
} as const;

interface Group {
  type: string;
  nodes: ViewNode[];
}

/**
 * The visible nodes grouped by kind, each with the relations that reach it. It is the drawing's
 * accessible equivalent — everything the canvas shows, reachable by keyboard and read aloud — and
 * the surface browser tests drive, because choosing a row does exactly what pressing the node does.
 * It lists the view, not the model, so a filtered-out item is absent here as on the canvas.
 */
export default function GraphListView({ view, text, onSelect, onMore }: GraphListViewProps) {
  const { t } = useTranslation();
  const stubs = useMemo(() => view.nodes.filter((vn) => vn.kind === 'more' && vn.more !== null), [view]);
  const groups = useMemo(() => {
    const byType = new Map<string, ViewNode[]>();
    for (const vn of view.nodes) {
      if (vn.kind === 'more') {
        continue;
      }
      (byType.get(vn.type) ?? byType.set(vn.type, []).get(vn.type)!).push(vn);
    }
    return [...byType].map(([type, nodes]): Group => ({ type, nodes }));
  }, [view]);

  if (groups.length === 0) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('graph.list.empty')} />;
  }
  const selectedKey = view.selection?.kind === 'node' ? view.selection.key : null;
  return (
    <nav aria-label={t('graph.list.title')} data-testid="graph-list">
      {groups.map((group) => (
        <section key={group.type} aria-label={text.typeName(group.type)} style={{ marginBottom: 16 }}>
          <Typography.Title level={5} style={{ marginTop: 0 }}>
            {text.typeName(group.type)} ({group.nodes.length})
          </Typography.Title>
          <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
            {group.nodes.map((vn) => {
              const label = text.node(vn);
              const isSelected = vn.key === selectedKey;
              const neighbours = neighboursOf(view, vn.key);
              return (
                <li key={vn.key} style={{ marginBottom: 6 }} data-testid="graph-list-item" data-key={vn.key}>
                  <button
                    type="button"
                    onClick={() => onSelect(vn.key)}
                    aria-pressed={isSelected}
                    data-testid="graph-list-select"
                    style={{
                      ...bareButton,
                      display: 'block',
                      width: '100%',
                      boxSizing: 'border-box',
                      padding: '4px 8px',
                      borderRadius: 4,
                      fontWeight: isSelected ? 600 : 400,
                      background: isSelected ? 'var(--ant-color-primary-bg, rgba(20, 98, 98, 0.12))' : undefined,
                    }}
                  >
                    {vn.restricted && <LockOutlined aria-hidden style={{ marginRight: 6 }} />}
                    {label}
                    {vn.isSeed && (
                      <Tag style={{ marginLeft: 6 }} variant="filled">
                        {t('graph.list.seed')}
                      </Tag>
                    )}
                    {vn.node?.subtitle && !vn.restricted && (
                      <Typography.Text type="secondary" style={{ display: 'block', fontSize: 12 }}>
                        {vn.node.subtitle}
                      </Typography.Text>
                    )}
                  </button>
                  {neighbours.length > 0 && (
                    <ul
                      aria-label={t('graph.list.relations', { title: label })}
                      style={{ listStyle: 'none', margin: '2px 0 0 12px', padding: 0, fontSize: 12 }}
                    >
                      {neighbours.map(({ edge, other, reading }) => (
                        <li key={edge.key} data-testid="graph-list-relation">
                          <Typography.Text type="secondary">{text.relation(edge, reading)}</Typography.Text>{' '}
                          <button
                            type="button"
                            onClick={() => onSelect(other.key)}
                            style={{ ...bareButton, padding: 0, textDecoration: 'underline' }}
                          >
                            {text.node(other)}
                          </button>
                        </li>
                      ))}
                    </ul>
                  )}
                </li>
              );
            })}
          </ul>
        </section>
      ))}
      {onMore && stubs.length > 0 && (
        <section aria-label={t('graph.legend.more')} style={{ marginBottom: 8 }}>
          <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
            {stubs.map((vn) => {
              const parent = view.nodes.find((n) => n.key === vn.more!.nodeKey);
              const category = text.categoryName(vn.more!.category);
              return (
                <li key={vn.key}>
                  <button
                    type="button"
                    data-testid="graph-list-more"
                    data-key={vn.key}
                    aria-label={t('graph.moreButton.label', { count: vn.more!.remaining, category, title: parent ? text.node(parent) : '' })}
                    onClick={() => onMore(vn.more!.nodeKey, vn.more!.category)}
                    style={{ ...bareButton, padding: '2px 8px', textDecoration: 'underline' }}
                  >
                    {t('graph.moreButton.button', { count: vn.more!.remaining, category })}
                  </button>
                </li>
              );
            })}
          </ul>
        </section>
      )}
    </nav>
  );
}
