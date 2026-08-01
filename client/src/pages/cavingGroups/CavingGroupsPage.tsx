// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { DeleteOutlined, PlusOutlined, TeamOutlined } from '@ant-design/icons';
import {
  App,
  Button,
  Drawer,
  Flex,
  Form,
  Input,
  List,
  Modal,
  Popconfirm,
  Select,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCreateCavingGroup,
  useMe,
  useRemoveCavingGroupMember,
  useCavingGroupMembers,
  useCavingGroups,
  useUpsertCavingGroupMember,
  useUserSearch,
  type CavingGroupInfo,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

function MemberDrawer({ group, onClose }: { group: CavingGroupInfo; onClose: () => void }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: members } = useCavingGroupMembers(group.id);
  const upsert = useUpsertCavingGroupMember(group.id);
  const remove = useRemoveCavingGroupMember(group.id);
  const [userQuery, setUserQuery] = useState('');
  const debounced = useDebouncedValue(userQuery);
  const { data: users } = useUserSearch(debounced);
  const [selectedUser, setSelectedUser] = useState<string>();

  const candidates = useMemo(
    () => (users ?? []).filter((user) => !members?.some((m) => m.userId === user.id)),
    [users, members],
  );

  const add = async () => {
    if (!selectedUser) {
      return;
    }
    try {
      await upsert.mutateAsync({ userId: selectedUser, role: 'member' });
      setSelectedUser(undefined);
      setUserQuery('');
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Drawer title={group.name} open onClose={onClose} width={420}>
      <Flex gap={8} style={{ marginBottom: 12 }}>
        <Select
          style={{ flex: 1 }}
          showSearch
          filterOption={false}
          placeholder={t('permissions.pickUser')}
          value={selectedUser}
          onSearch={setUserQuery}
          onChange={setSelectedUser}
          options={candidates.map((user) => ({
            value: user.id,
            // The address is only present when the person shares it; the label always is.
            label: user.email ? `${user.label} (${user.email})` : user.label,
          }))}
          notFoundContent={null}
        />
        <Button icon={<PlusOutlined />} onClick={() => void add()} disabled={!selectedUser}>
          {t('cavingGroups.addMember')}
        </Button>
      </Flex>
      <List
        size="small"
        dataSource={members}
        renderItem={(member) => (
          <List.Item
            actions={[
              <Select
                key="role"
                size="small"
                value={member.role}
                style={{ width: 110 }}
                onChange={(role) =>
                  upsert.mutateAsync({ userId: member.userId, role }).catch(() =>
                    message.error(t('common.saveFailed')),
                  )
                }
                options={(['member', 'admin', 'owner'] as const).map((role) => ({
                  value: role,
                  label: t(`cavingGroups.roles.${role}`),
                }))}
              />,
              <Popconfirm
                key="remove"
                title={t('cavingGroups.removeConfirm')}
                onConfirm={() =>
                  remove.mutateAsync(member.userId).catch(() => message.error(t('common.saveFailed')))
                }
              >
                <Button size="small" type="text" danger icon={<DeleteOutlined />} />
              </Popconfirm>,
            ]}
          >
            {member.displayName}
          </List.Item>
        )}
      />
    </Drawer>
  );
}

/** CavingGroups directory: browse for everyone, create for managers, manage members inline. */
export default function CavingGroupsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: cavingGroups, isFetching } = useCavingGroups();
  const { data: me } = useMe();
  const createCavingGroup = useCreateCavingGroup();
  const [creating, setCreating] = useState(false);
  const [managing, setManaging] = useState<CavingGroupInfo | null>(null);
  const [form] = Form.useForm<{
    name: string;
    type: CavingGroupInfo['type'];
    description?: string;
    website?: string;
  }>();

  const canCreate = me?.roles.some((r) => ['Admin', 'Manager'].includes(r)) ?? false;

  const onCreate = async () => {
    const values = await form.validateFields();
    try {
      await createCavingGroup.mutateAsync({
        name: values.name,
        type: values.type,
        description: values.description ?? null,
        website: values.website ?? null,
      });
      setCreating(false);
      form.resetFields();
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <div style={{ padding: 24, maxWidth: 900 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('cavingGroups.title')}
        </Typography.Title>
        {canCreate && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('cavingGroups.new')}
          </Button>
        )}
      </Flex>

      <Table<CavingGroupInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching && !cavingGroups}
        dataSource={cavingGroups}
        pagination={false}
        columns={[
          { title: t('cavingGroups.name'), dataIndex: 'name' },
          {
            title: t('cavingGroups.type'),
            dataIndex: 'type',
            width: 150,
            render: (type: CavingGroupInfo['type']) => t(`cavingGroups.types.${type}`),
          },
          { title: t('features.description'), dataIndex: 'description', ellipsis: true },
          {
            title: t('cavingGroups.members'),
            dataIndex: 'memberCount',
            width: 110,
            align: 'right',
            render: (count: number) => <Tag icon={<TeamOutlined />}>{count}</Tag>,
          },
          {
            title: '',
            key: 'actions',
            width: 140,
            render: (_, group) => (
              <Button size="small" onClick={() => setManaging(group)}>
                {t('cavingGroups.manage')}
              </Button>
            ),
          },
        ]}
      />

      <Modal
        title={t('cavingGroups.new')}
        open={creating}
        onCancel={() => setCreating(false)}
        onOk={() => void onCreate()}
        confirmLoading={createCavingGroup.isPending}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" initialValues={{ type: 'cavingClub' }}>
          <Form.Item name="name" label={t('cavingGroups.name')} rules={[{ required: true }]}>
            <Input maxLength={120} />
          </Form.Item>
          <Form.Item name="type" label={t('cavingGroups.type')} rules={[{ required: true }]}>
            <Select
              options={(['cavingClub', 'group', 'organization'] as const).map((type) => ({
                value: type,
                label: t(`cavingGroups.types.${type}`),
              }))}
            />
          </Form.Item>
          <Form.Item name="description" label={t('features.description')}>
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item name="website" label={t('caves.fields.website')}>
            <Input maxLength={255} />
          </Form.Item>
        </Form>
      </Modal>

      {managing && <MemberDrawer group={managing} onClose={() => setManaging(null)} />}
    </div>
  );
}
