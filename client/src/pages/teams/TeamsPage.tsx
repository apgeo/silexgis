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
  useCreateTeam,
  useMe,
  useRemoveTeamMember,
  useTeamMembers,
  useTeams,
  useUpsertTeamMember,
  useUserSearch,
  type TeamInfo,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

function MemberDrawer({ team, onClose }: { team: TeamInfo; onClose: () => void }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: members } = useTeamMembers(team.id);
  const upsert = useUpsertTeamMember(team.id);
  const remove = useRemoveTeamMember(team.id);
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
    <Drawer title={team.name} open onClose={onClose} width={420}>
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
            label: `${user.displayName ?? user.email}${user.email ? ` (${user.email})` : ''}`,
          }))}
          notFoundContent={null}
        />
        <Button icon={<PlusOutlined />} onClick={() => void add()} disabled={!selectedUser}>
          {t('teams.addMember')}
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
                  label: t(`teams.roles.${role}`),
                }))}
              />,
              <Popconfirm
                key="remove"
                title={t('teams.removeConfirm')}
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

/** Teams directory: browse for everyone, create for managers, manage members inline. */
export default function TeamsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: teams, isFetching } = useTeams();
  const { data: me } = useMe();
  const createTeam = useCreateTeam();
  const [creating, setCreating] = useState(false);
  const [managing, setManaging] = useState<TeamInfo | null>(null);
  const [form] = Form.useForm<{ name: string; description?: string; website?: string }>();

  const canCreate = me?.roles.some((r) => ['Admin', 'Manager'].includes(r)) ?? false;

  const onCreate = async () => {
    const values = await form.validateFields();
    try {
      await createTeam.mutateAsync({
        name: values.name,
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
          {t('teams.title')}
        </Typography.Title>
        {canCreate && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('teams.new')}
          </Button>
        )}
      </Flex>

      <Table<TeamInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching && !teams}
        dataSource={teams}
        pagination={false}
        columns={[
          { title: t('teams.name'), dataIndex: 'name' },
          { title: t('features.description'), dataIndex: 'description', ellipsis: true },
          {
            title: t('teams.members'),
            dataIndex: 'memberCount',
            width: 110,
            align: 'right',
            render: (count: number) => <Tag icon={<TeamOutlined />}>{count}</Tag>,
          },
          {
            title: '',
            key: 'actions',
            width: 140,
            render: (_, team) => (
              <Button size="small" onClick={() => setManaging(team)}>
                {t('teams.manage')}
              </Button>
            ),
          },
        ]}
      />

      <Modal
        title={t('teams.new')}
        open={creating}
        onCancel={() => setCreating(false)}
        onOk={() => void onCreate()}
        confirmLoading={createTeam.isPending}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label={t('teams.name')} rules={[{ required: true }]}>
            <Input maxLength={120} />
          </Form.Item>
          <Form.Item name="description" label={t('features.description')}>
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item name="website" label={t('caves.fields.website')}>
            <Input maxLength={255} />
          </Form.Item>
        </Form>
      </Modal>

      {managing && <MemberDrawer team={managing} onClose={() => setManaging(null)} />}
    </div>
  );
}
