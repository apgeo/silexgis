// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MouseEvent } from 'react';
import { CloseOutlined, LockOutlined, StarFilled } from '@ant-design/icons';
import { Flex, Popconfirm, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import type { ResLinkMember } from '../../api/hooks.ts';
import { anchorStateNote, anchorSummary, memberRoute, targetTypeEntry } from './registry.ts';

/**
 * One member of a link, as a chip: type icon, title, and the compact part label when the
 * member addresses only a part of its target.
 *
 * A member whose target this caller may not read arrives with no display at all, and is
 * shown as an anonymous "restricted item". Its note and anchor kind do still travel, and
 * are deliberately not rendered: a part label or an author's note beside an otherwise
 * nameless chip narrows down what the hidden thing is, which is exactly what withholding
 * the display was for.
 *
 * `onRemove` turns the chip into a removable one. A restricted chip is removable too when
 * the caller may curate the link: whether a membership may be struck out is a question
 * about the link, not about the thing on the other end of it — and a member somebody
 * cannot read is exactly the one they are most likely to have to correct blind.
 *
 * `showPath` draws the target's containment ahead of its name, for the surfaces where the
 * name alone is ambiguous — two caves of one massif each have a "Galeria Mare". It is
 * derived by the server from the hierarchy at read time and never stored beside the
 * membership: a path written down is a path that goes stale the first time somebody moves
 * a passage. The hover hint carries it whether or not the chip shows it, since it costs
 * nothing there.
 */
export default function MemberChip({
  member,
  onRemove,
  showPath,
}: {
  member: ResLinkMember;
  onRemove?: () => void;
  showPath?: boolean;
}) {
  const { t } = useTranslation();
  const entry = targetTypeEntry(member.targetType);
  const Icon = entry.icon;

  // The chip may sit inside a link to its target, so the close click has to say it is not
  // a navigation before anything else reads it as one — and it never removes anything by
  // itself: striking a membership out is a hard delete of somebody's record of what was
  // done, with no undo, on a target small enough to hit by accident while aiming at the
  // chip. The confirmation is the same one the link's own page puts on the same act, so
  // the two surfaces agree about how dangerous it is.
  const closeProps = onRemove
    ? {
        closable: true,
        closeIcon: (
          <Popconfirm
            title={t('resLinks.removeMemberConfirm')}
            okButtonProps={{ danger: true }}
            onConfirm={onRemove}
          >
            <CloseOutlined aria-label={t('resLinks.removeMember')} />
          </Popconfirm>
        ),
        onClose: (event: MouseEvent<HTMLElement>) => {
          // The tag would remove itself and follow any surrounding navigation; the
          // confirmation inside the icon has already taken the click.
          event.preventDefault();
          event.stopPropagation();
        },
      }
    : {};

  if (!member.display) {
    return (
      <Tooltip title={t('resLinks.restrictedHint')}>
        <Tag icon={<LockOutlined />} {...closeProps}>
          {t('resLinks.restricted')}
        </Tag>
      </Tooltip>
    );
  }

  const summary = anchorSummary(member.anchorKind, member.anchor, t);
  const stateNote = anchorStateNote(member.anchorState, t);
  const route = memberRoute(member.targetType, member.targetId, member.display);
  const path = member.display.path ?? [];
  const hint = [
    t(entry.labelKey),
    member.display.subtitle,
    path.length > 0 ? path.join(' › ') : null,
    stateNote,
  ]
    .filter(Boolean)
    .join(' · ');

  const chip = (
    <Tag
      icon={<Icon />}
      style={route ? { cursor: 'pointer', marginInlineEnd: 0 } : { marginInlineEnd: 0 }}
      {...closeProps}
    >
      {member.isMain && (
        <StarFilled aria-label={t('resLinks.mainMember')} style={{ marginInlineEnd: 4 }} />
      )}
      {/* Ahead of the name and quieter than it: the path answers "which one", which is a
          question the reader only asks once the name has already been read. */}
      {showPath && path.length > 0 && (
        <Typography.Text type="secondary" style={{ marginInlineEnd: 4 }}>
          {path.join(' › ')} ›
        </Typography.Text>
      )}
      {member.display.title}
      {summary && (
        <Typography.Text type="secondary" style={{ marginInlineStart: 4 }}>
          {summary}
        </Typography.Text>
      )}
    </Tag>
  );

  // A linked thing that has a picture shows it on hover. Worth the few lines: a link list is a
  // column of near-identical chips, and for a photograph or a scanned survey the picture is what
  // tells them apart — far faster than reading five titles that all begin with the same word.
  //
  // Nothing is prefetched and nothing is cached here: the image is only requested when a tooltip
  // actually opens, and the browser's own cache serves the second hover. A cache of our own would
  // be a second copy of something the platform already does, holding delivery URLs whose tokens
  // expire underneath it.
  const preview = member.display.thumbnailUrl ? (
    <Flex vertical gap={4} style={{ maxWidth: 220 }}>
      <img
        src={member.display.thumbnailUrl}
        alt=""
        loading="lazy"
        style={{ width: '100%', borderRadius: 4, display: 'block' }}
      />
      <span>{hint}</span>
    </Flex>
  ) : (
    hint
  );

  const withHint = (
    <Tooltip title={preview} mouseEnterDelay={member.display.thumbnailUrl ? 0.4 : 0.1}>
      {chip}
    </Tooltip>
  );
  return route ? <Link to={route}>{withHint}</Link> : withHint;
}
