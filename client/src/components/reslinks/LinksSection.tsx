// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  ExportOutlined,
  LinkOutlined,
  MoreOutlined,
  PlusOutlined,
} from '@ant-design/icons';
import { App, Button, Card, Dropdown, Flex, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import { useDeleteResLink, useResLinksForTarget, type ResLink } from '../../api/hooks.ts';
import AddMemberModal from './AddMemberModal.tsx';
import MemberChip from './MemberChip.tsx';
import { mayEditResLink } from './permissions.ts';
import { relationPhraseFor } from './relations.ts';
import { linkPageRoute, linkPageUrl, type ResLinkTargetType } from './registry.ts';

/** Chips beyond this collapse into a "+n" tag; a row stays one line's worth of reading. */
const MAX_CHIPS = 4;

/**
 * How many links one entity's panel asks for. Well past what any entity is expected to
 * carry, and still a bound rather than a promise — the badge counts what the server
 * admits, so the section says so when it is showing fewer rows than it counted.
 */
const MAX_ROWS = 200;

export interface LinksSectionProps {
  entityType: ResLinkTargetType;
  entityId: string;
  /**
   * `card` for a page with room for a section of its own; `compact` for a dock or popover,
   * where the links hide behind a counted button until asked for.
   */
  variant?: 'card' | 'compact';
  /**
   * Controlled disclosure. When a host draws the header — the selection panel's section shell —
   * it owns whether the list is open, and a closed list fetches nothing.
   */
  open?: boolean;
  /**
   * Offers the record-a-link action, which opens the picker with this entity already in
   * place as the first member. A surface that only reports leaves it off.
   */
  canAdd?: boolean;
  /** How this entity should read as a member while the link is being composed. */
  entityTitle?: string | null;
}

function LinkRow({
  link,
  entityType,
  entityId,
}: {
  link: ResLink;
  entityType: string;
  entityId: string;
}) {
  const { t } = useTranslation();
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
  const deleteLink = useDeleteResLink();

  const mayEdit = mayEditResLink(link);

  // The entity whose page this is speaks for itself; repeating its chip in its own row
  // would say nothing. Everything else is what the row is actually about.
  const others = link.members.filter(
    (member) => !(member.targetType === entityType && member.targetId === entityId),
  );
  const shown = others.slice(0, MAX_CHIPS);
  const hidden = others.length - shown.length;

  // A link is allowed to hold a single item, but one on its own asserts nothing — so the
  // row says so quietly rather than rendering a relation phrase with nothing to relate.
  // The wording stops at what this reader can see: a membership withheld from them leaves
  // the response entirely, so a row can look like this while the stored link is complete.
  const lonely = link.members.length === 1;

  const copyUrl = async () => {
    try {
      await navigator.clipboard.writeText(linkPageUrl(link.shortCode));
      message.success(t('resLinks.urlCopied'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const confirmDelete = () => {
    modal.confirm({
      title: t('resLinks.deleteConfirm'),
      okButtonProps: { danger: true },
      okText: t('resLinks.delete'),
      onOk: async () => {
        try {
          await deleteLink.mutateAsync(link.id);
          message.success(t('common.saved'));
        } catch {
          message.error(t('common.saveFailed'));
        }
      },
    });
  };

  return (
    <Flex align="baseline" gap={8} wrap>
      <Typography.Text type="secondary">
        {relationPhraseFor(link, entityType, entityId, t)}
      </Typography.Text>
      <Flex align="center" gap={4} wrap>
        {shown.map((member) => (
          <MemberChip key={member.id} member={member} />
        ))}
        {hidden > 0 && (
          <Link to={linkPageRoute(link.shortCode)}>
            <Tag style={{ cursor: 'pointer', marginInlineEnd: 0 }}>
              {t('resLinks.moreMembers', { count: hidden })}
            </Tag>
          </Link>
        )}
        {lonely && (
          <Tooltip title={t('resLinks.incompleteHint')}>
            <Tag style={{ marginInlineEnd: 0 }}>{t('resLinks.incomplete')}</Tag>
          </Tooltip>
        )}
      </Flex>
      <Dropdown
        trigger={['click']}
        menu={{
          items: [
            { key: 'open', icon: <ExportOutlined />, label: t('resLinks.openPage') },
            { key: 'copy', icon: <CopyOutlined />, label: t('resLinks.copyUrl') },
            ...(mayEdit
              ? [
                  { key: 'edit', icon: <EditOutlined />, label: t('resLinks.edit') },
                  { key: 'delete', icon: <DeleteOutlined />, label: t('resLinks.delete'), danger: true },
                ]
              : []),
          ],
          onClick: ({ key }) => {
            if (key === 'open') {
              void navigate(linkPageRoute(link.shortCode));
            } else if (key === 'copy') {
              void copyUrl();
            } else if (key === 'edit') {
              void navigate(`${linkPageRoute(link.shortCode)}?edit=1`);
            } else if (key === 'delete') {
              confirmDelete();
            }
          },
        }}
      >
        <Button
          size="small"
          type="text"
          icon={<MoreOutlined />}
          aria-label={t('resLinks.rowActions')}
        />
      </Dropdown>
    </Flex>
  );
}

/**
 * The links one entity takes part in, on every surface that can be a member.
 *
 * The count comes from the panel query itself, which is also what withholds: a link this
 * caller may not be told about is not counted, so an entity with links can honestly show
 * none. Nothing here reconstructs a count from another source.
 */
export default function LinksSection({
  entityType,
  entityId,
  variant = 'card',
  canAdd,
  entityTitle,
  open,
}: LinksSectionProps) {
  const { t } = useTranslation();
  const [selfExpanded, setSelfExpanded] = useState(false);
  const [adding, setAdding] = useState(false);
  // A host that draws its own disclosure owns the open state; on its own the section keeps its
  // own. Uncontrolled, the state reset on every selection because this component remounts —
  // which read as the panel forgetting what had just been opened.
  const controlled = open !== undefined;
  const expanded = controlled ? open : selfExpanded;
  const { data } = useResLinksForTarget(
    entityType,
    entityId,
    { pageSize: MAX_ROWS },
    // A closed section asks for nothing. The count in the header is the one thing that would
    // want the query anyway, and a header nobody has opened does not need it.
    !controlled || open,
  );

  const links = data?.items ?? [];
  const total = data?.totalItems ?? 0;
  const withheldByPaging = total - links.length;

  const addButton = canAdd && (
    <Button size="small" type="link" icon={<PlusOutlined />} onClick={() => setAdding(true)}>
      {t('resLinks.addAction')}
    </Button>
  );

  // Mounted only while open: the picker asks the server for a vocabulary and a target feed,
  // and a page carrying several of these sections should ask for none of it until someone
  // actually starts recording a link.
  const dialog = adding && (
    <AddMemberModal
      open
      onClose={() => setAdding(false)}
      origin={{ targetType: entityType, targetId: entityId, title: entityTitle }}
    />
  );

  const rows = (
    <Flex vertical gap={8}>
      {links.map((link) => (
        <LinkRow key={link.id} link={link} entityType={entityType} entityId={entityId} />
      ))}
      {/* The badge counts what the server admits, which can be more than one page holds.
          Saying so beats a count that silently disagrees with the rows under it. */}
      {withheldByPaging > 0 && (
        <Typography.Text type="secondary">
          {t('resLinks.showingFirst', { count: links.length, total })}
        </Typography.Text>
      )}
    </Flex>
  );

  // Nothing recorded: no empty section, just the quiet invitation to record the first one.
  if (total === 0) {
    return addButton ? (
      <div style={{ marginTop: 12 }}>
        {addButton}
        {dialog}
      </div>
    ) : null;
  }

  if (variant === 'compact') {
    // Controlled by a host: the host already drew a header and a disclosure, so this draws the
    // rows and nothing else. Two disclosures for one list is the shape that makes a panel feel
    // like it was assembled rather than designed.
    if (controlled) {
      return (
        <div>
          {addButton}
          <div style={{ marginTop: 8 }}>{rows}</div>
          {dialog}
        </div>
      );
    }

    return (
      <div style={{ marginTop: 12 }}>
        <Flex align="center" gap={4} wrap>
          <Button size="small" type="text" icon={<LinkOutlined />} onClick={() => setSelfExpanded((x) => !x)}>
            {t('resLinks.titleCount', { count: total })}
          </Button>
          {expanded && addButton}
        </Flex>
        {expanded && <div style={{ marginTop: 8 }}>{rows}</div>}
        {dialog}
      </div>
    );
  }

  return (
    <Card
      size="small"
      title={t('resLinks.titleCount', { count: total })}
      style={{ marginTop: 16 }}
      extra={addButton}
    >
      {rows}
      {dialog}
    </Card>
  );
}
