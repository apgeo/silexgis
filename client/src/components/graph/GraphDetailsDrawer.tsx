// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { DeleteOutlined, ExportOutlined, LockOutlined } from '@ant-design/icons';
import { Alert, Drawer, Flex, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import type { ResLinkMember } from '../../api/hooks.ts';
import { neighboursOf, type GraphView, type ViewEdge, type ViewNode } from '../../graph/view.ts';
import type { NodeKey } from '../../graph/types.ts';
import Button from '../Button.tsx';
import MemberChip from '../reslinks/MemberChip.tsx';
import { linkPageRoute } from '../reslinks/registry.ts';
import type { GraphText } from './useGraphPresentation.ts';

export interface GraphDetailsDrawerProps {
  open: boolean;
  view: GraphView;
  text: GraphText;
  onClose: () => void;
  /** Choosing a related item moves the selection to it. */
  onSelect: (key: NodeKey) => void;
  /** Forgets the item and what only it led to; offered for any item but the starting one. */
  onRemove: (key: NodeKey) => void;
}

/**
 * A resource-link member as the link chip wants it. Only the fields the chip reads are filled; a
 * placeholder carries no display, which the chip draws as an anonymous restricted item.
 */
function asMember(vn: ViewNode): ResLinkMember {
  const node = vn.node;
  return {
    id: vn.key,
    targetType: vn.type,
    targetId: node?.id ?? '',
    isMain: false,
    sortOrder: 0,
    note: null,
    anchorKind: 'whole',
    anchor: null,
    anchorFileId: null,
    anchorState: 'exact',
    display:
      vn.restricted || node === null || node.title === null
        ? null
        : { title: node.title, subtitle: node.subtitle, route: node.route, thumbnailUrl: null },
  };
}

/**
 * The details of the selected item or relation, read from the same view as the drawing.
 *
 * - An item: its title, kind, a link to its own page, and the relations that reach it grouped by
 *   category (choosing one moves the selection there).
 * - A link drawn as a hub (three or more members): its members as the link's own chips and a link to
 *   the link's page.
 * - A restricted placeholder: only that it is restricted. It names nothing and offers no action,
 *   because any further hint would narrow down what the hidden item is.
 * - A relation (an edge): what it says, between which items, and the link's page when it stands for one.
 *
 * It is a plain panel with no mask so the drawing stays usable beside it.
 */
export default function GraphDetailsDrawer({ open, view, text, onClose, onSelect, onRemove }: GraphDetailsDrawerProps) {
  const { t } = useTranslation();
  const selection = view.selection;
  const byKey = useMemo(() => new Map(view.nodes.map((n) => [n.key, n])), [view]);
  const subject: ViewNode | undefined = selection?.kind === 'node' ? byKey.get(selection.key) : undefined;
  const edge: ViewEdge | undefined = selection?.kind === 'edge' ? view.edges.find((e) => e.key === selection.key) : undefined;

  let title = t('graph.details.title');
  let body: React.ReactNode = null;

  if (subject !== undefined && subject.kind !== 'more') {
    title = text.node(subject);
    body = <NodeDetails view={view} text={text} subject={subject} onSelect={onSelect} onRemove={onRemove} />;
  } else if (edge !== undefined) {
    const source = byKey.get(edge.source);
    const target = byKey.get(edge.target);
    title = t('graph.details.relation');
    body = (
      <Flex vertical gap={8} data-testid="graph-details-edge">
        <Typography.Text strong>{text.relation(edge, 'forward')}</Typography.Text>
        <Typography.Text>
          {source ? text.node(source) : ''} {'→'} {target ? text.node(target) : ''}
        </Typography.Text>
        <Tag style={{ alignSelf: 'flex-start' }}>{text.categoryName(edge.category)}</Tag>
        {edge.link?.shortCode && (
          <Link to={linkPageRoute(edge.link.shortCode)} data-testid="graph-details-link-page">
            <ExportOutlined /> {t('graph.details.openLink')}
          </Link>
        )}
      </Flex>
    );
  }

  return (
    <Drawer
      open={open && body !== null}
      onClose={onClose}
      mask={false}
      size={360}
      title={title}
      destroyOnHidden
      data-testid="graph-details"
    >
      {body}
    </Drawer>
  );
}

function NodeDetails({
  view,
  text,
  subject,
  onSelect,
  onRemove,
}: {
  view: GraphView;
  text: GraphText;
  subject: ViewNode;
  onSelect: (key: NodeKey) => void;
  onRemove: (key: NodeKey) => void;
}) {
  const { t } = useTranslation();
  const related = useMemo(() => neighboursOf(view, subject.key), [view, subject.key]);
  const groups = useMemo(() => {
    const byCategory = new Map<string, typeof related>();
    for (const n of related) {
      (byCategory.get(n.edge.category) ?? byCategory.set(n.edge.category, []).get(n.edge.category)!).push(n);
    }
    return [...byCategory];
  }, [related]);

  if (subject.restricted) {
    return (
      <Alert
        type="info"
        showIcon
        icon={<LockOutlined />}
        title={t('graph.restricted')}
        description={t('graph.details.restrictedBody')}
        data-testid="graph-details-restricted"
      />
    );
  }

  const node = subject.node;
  const isHub = subject.kind === 'hub';
  return (
    <Flex vertical gap={12} data-testid="graph-details-node">
      <Flex gap={8} wrap align="center">
        <Tag>{text.typeName(subject.type)}</Tag>
        {node?.kind && <Tag variant="outlined">{t(`featureKinds.${node.kind}`, { defaultValue: node.kind })}</Tag>}
        {subject.isSeed && <Tag color="processing">{t('graph.list.seed')}</Tag>}
      </Flex>
      {node?.subtitle && <Typography.Text type="secondary">{node.subtitle}</Typography.Text>}
      <Flex gap={8} wrap>
        {node?.route && (
          <Link to={node.route} data-testid="graph-details-open">
            <ExportOutlined /> {t('graph.details.open')}
          </Link>
        )}
        {isHub && node?.shortCode && (
          <Link to={linkPageRoute(node.shortCode)} data-testid="graph-details-link-page">
            <ExportOutlined /> {t('graph.details.openLink')}
          </Link>
        )}
      </Flex>

      {isHub ? (
        <section aria-label={t('graph.details.members')}>
          <Typography.Title level={5}>{t('graph.details.members')}</Typography.Title>
          <Flex gap={6} wrap data-testid="graph-details-members">
            {related.map(({ other }) => (
              <MemberChip key={other.key} member={asMember(other)} />
            ))}
          </Flex>
        </section>
      ) : (
        groups.map(([category, items]) => (
          <section key={category} aria-label={text.categoryName(category)} data-testid="graph-details-category">
            <Typography.Title level={5} style={{ marginBottom: 4 }}>
              {text.categoryName(category)} ({items.length})
            </Typography.Title>
            <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
              {items.map(({ edge, other, reading }) => (
                <li key={edge.key}>
                  <Typography.Text type="secondary">{text.relation(edge, reading)}</Typography.Text>{' '}
                  <Button type="link" size="small" style={{ padding: 0 }} onClick={() => onSelect(other.key)}>
                    {text.node(other)}
                  </Button>
                </li>
              ))}
            </ul>
          </section>
        ))
      )}
      {!isHub && groups.length === 0 && <Typography.Text type="secondary">{t('graph.details.noRelations')}</Typography.Text>}

      {!subject.isSeed && (
        <Button icon={<DeleteOutlined />} onClick={() => onRemove(subject.key)} data-testid="graph-details-remove">
          {t('graph.details.remove')}
        </Button>
      )}
    </Flex>
  );
}
