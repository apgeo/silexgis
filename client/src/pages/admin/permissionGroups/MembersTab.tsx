// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { App, Button, Flex, Popconfirm, Select, Tag, Typography } from 'antd';
import List from '../../../components/List.tsx';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../../api/client.ts';
import {
  useAddPermissionGroupMember,
  useCavingGroups,
  usePermissionGroupMembers,
  useRemovePermissionGroupMember,
  useUserSearch,
  type AccessSubjectKind,
  type PermissionGroup,
} from '../../../api/hooks.ts';
import { useDebouncedValue } from '../../../hooks/useDebouncedValue.ts';

/**
 * The trustee list: users, and caving groups whose account-holding members inherit the
 * ruleset. Adding a trustee hands them everything the rules grant, so the server holds
 * it to the same no-amplification bound as writing the rules — surfaced here when it
 * refuses. Removing the last route into full administration is refused likewise.
 */
export default function MembersTab({ group }: { group: PermissionGroup }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: members, isPending } = usePermissionGroupMembers(group.id);
  const addMember = useAddPermissionGroupMember(group.id);
  const removeMember = useRemovePermissionGroupMember(group.id);
  const { data: cavingGroups } = useCavingGroups();
  const [memberKind, setMemberKind] = useState<AccessSubjectKind>('user');
  const [memberId, setMemberId] = useState<string>();
  const [userQuery, setUserQuery] = useState('');
  const debouncedUserQuery = useDebouncedValue(userQuery);
  const { data: users } = useUserSearch(debouncedUserQuery);

  const describeRefusal = (error: unknown): string => {
    if (error instanceof ApiError) {
      if (error.code === 'permission_group.last_full_admin') {
        return t('permissionGroups.lastFullAdmin');
      }
      if (error.code === 'access_entry.exceeds_own_rights') {
        return t('permissionGroups.exceedsOwnRights');
      }
    }
    return t('common.saveFailed');
  };

  const onAdd = async () => {
    if (!memberId) {
      return;
    }
    try {
      await addMember.mutateAsync({ memberKind, memberId });
      setMemberId(undefined);
      setUserQuery('');
    } catch (error) {
      message.error(describeRefusal(error));
    }
  };

  return (
    <Flex vertical gap={12}>
      <Flex gap={8} wrap>
        <Select
          value={memberKind}
          style={{ width: 140 }}
          onChange={(kind: AccessSubjectKind) => {
            setMemberKind(kind);
            setMemberId(undefined);
          }}
          options={[
            { value: 'user', label: t('permissions.user') },
            { value: 'cavingGroup', label: t('permissions.cavingGroup') },
          ]}
        />
        {memberKind === 'cavingGroup' ? (
          <Select
            style={{ flex: 1, minWidth: 200 }}
            showSearch
            optionFilterProp="label"
            placeholder={t('permissions.pickCavingGroup')}
            value={memberId}
            onChange={setMemberId}
            options={cavingGroups?.map((cavingGroup) => ({ value: cavingGroup.id, label: cavingGroup.name }))}
          />
        ) : (
          <Select
            style={{ flex: 1, minWidth: 200 }}
            showSearch
            filterOption={false}
            placeholder={t('permissions.pickUser')}
            value={memberId}
            onSearch={setUserQuery}
            onChange={setMemberId}
            options={users?.map((user) => ({
              value: user.id,
              label: user.email ? `${user.label} (${user.email})` : user.label,
            }))}
            notFoundContent={null}
          />
        )}
        <Button
          icon={<PlusOutlined />}
          onClick={() => void onAdd()}
          disabled={!memberId}
          loading={addMember.isPending}
        >
          {t('permissionGroups.addMember')}
        </Button>
      </Flex>

      <List
        size="small"
        loading={isPending}
        dataSource={members}
        locale={{ emptyText: t('permissionGroups.noMembers') }}
        renderItem={(member) => (
          <List.Item
            actions={[
              <Popconfirm
                key="remove"
                title={t('permissionGroups.removeMemberConfirm')}
                onConfirm={() =>
                  removeMember
                    .mutateAsync({ memberKind: member.memberKind, memberId: member.memberId })
                    .catch((error: unknown) => message.error(describeRefusal(error)))
                }
              >
                <Button size="small" type="text" danger icon={<DeleteOutlined />} />
              </Popconfirm>,
            ]}
          >
            <Flex gap={8} align="center">
              <Tag>{t(`permissions.${member.memberKind}`)}</Tag>
              <Typography.Text>{member.memberName ?? member.memberId}</Typography.Text>
            </Flex>
          </List.Item>
        )}
      />
    </Flex>
  );
}
