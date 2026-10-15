// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Flex, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useAdminUser, type AdminUserPermissionGroup } from '../../api/hooks.ts';
import List from '../../components/List.tsx';

interface Props {
  userId: string;
  /** Whether the caller may open the permission-groups page at all; a link they cannot follow is a dead end. */
  linkable: boolean;
}

/**
 * The permission groups an account belongs to, read when its row is opened.
 *
 * <b>Read-only on purpose.</b> Membership is a grant, and granting is governed by its own rights
 * and guards on the permission-groups page; a second place to edit it would be a second place for
 * those guards to be missing. Each group links there instead.
 *
 * <b>Each path is named.</b> An account can hold a group itself and through any number of caving
 * groups at once, and taking it out of one path leaves the others standing — which is exactly what
 * somebody trying to understand why an account can still do something needs to see.
 */
export default function UserPermissionGroups({ userId, linkable }: Props) {
  const { t } = useTranslation();
  const detail = useAdminUser(userId);

  if (detail.isPending) {
    return <Spin size="small" />;
  }
  if (detail.isError) {
    return <Alert type="error" showIcon title={t('admin.users.groupsFailed')} />;
  }

  const groups = detail.data.permissionGroups;
  return (
    <Flex vertical gap={4} data-testid={`account-groups-${userId}`}>
      <Typography.Text strong>{t('admin.users.groupsTitle')}</Typography.Text>
      <Typography.Text type="secondary">{t('admin.users.groupsIntro')}</Typography.Text>
      {groups.length === 0 ? (
        <Typography.Text>{t('admin.users.groupsNone')}</Typography.Text>
      ) : (
        <List<AdminUserPermissionGroup>
          size="small"
          dataSource={groups}
          renderItem={(group) => (
            <List.Item>
              <Flex gap={8} wrap align="center">
                {linkable ? (
                  <Link to={`/admin/permission-groups?group=${encodeURIComponent(group.id)}`}>{group.name}</Link>
                ) : (
                  <Typography.Text>{group.name}</Typography.Text>
                )}
                {group.direct && <Tag>{t('admin.users.groupDirect')}</Tag>}
                {group.through.length > 0 && (
                  <Tag color="purple">
                    {t('admin.users.groupThrough', { groups: group.through.map((c) => c.name).join(', ') })}
                  </Tag>
                )}
              </Flex>
            </List.Item>
          )}
        />
      )}
    </Flex>
  );
}
