// SPDX-License-Identifier: AGPL-3.0-or-later
import { LockOutlined, StarFilled } from '@ant-design/icons';
import { Tag, Tooltip, Typography } from 'antd';
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
 */
export default function MemberChip({ member }: { member: ResLinkMember }) {
  const { t } = useTranslation();
  const entry = targetTypeEntry(member.targetType);
  const Icon = entry.icon;

  if (!member.display) {
    return (
      <Tooltip title={t('resLinks.restrictedHint')}>
        <Tag icon={<LockOutlined />}>{t('resLinks.restricted')}</Tag>
      </Tooltip>
    );
  }

  const summary = anchorSummary(member.anchorKind, member.anchor, t);
  const stateNote = anchorStateNote(member.anchorState, t);
  const route = memberRoute(member.targetType, member.targetId, member.display);
  const hint = [t(entry.labelKey), member.display.subtitle, stateNote].filter(Boolean).join(' · ');

  const chip = (
    <Tag icon={<Icon />} style={route ? { cursor: 'pointer', marginInlineEnd: 0 } : { marginInlineEnd: 0 }}>
      {member.isMain && (
        <StarFilled aria-label={t('resLinks.mainMember')} style={{ marginInlineEnd: 4 }} />
      )}
      {member.display.title}
      {summary && (
        <Typography.Text type="secondary" style={{ marginInlineStart: 4 }}>
          {summary}
        </Typography.Text>
      )}
    </Tag>
  );

  const withHint = <Tooltip title={hint}>{chip}</Tooltip>;
  return route ? <Link to={route}>{withHint}</Link> : withHint;
}
