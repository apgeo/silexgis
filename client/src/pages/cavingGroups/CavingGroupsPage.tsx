// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import {
  BarChartOutlined,
  DeleteOutlined,
  NotificationOutlined,
  PlusOutlined,
  TeamOutlined,
} from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Drawer,
  Flex,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Skeleton,
  Table,
  Tag,
  Typography,
} from 'antd';
import List from '../../components/List.tsx';
import TripStatisticsPanel from '../../components/statistics/TripStatisticsPanel.tsx';
import { useTranslation } from 'react-i18next';
import {
  useAnnounceToCavingGroup,
  useCan,
  useCavers,
  useCavingGroupAnnouncementAudience,
  useCreateCavingGroup,
  useRemoveCavingGroupMember,
  useCavingGroupMembers,
  useCavingGroups,
  useUpsertCavingGroupMember,
  type CavingGroupInfo,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

function MemberDrawer({ group, onClose }: { group: CavingGroupInfo; onClose: () => void }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: members } = useCavingGroupMembers(group.id);
  const upsert = useUpsertCavingGroupMember(group.id);
  const remove = useRemoveCavingGroupMember(group.id);
  const [caverQuery, setCaverQuery] = useState('');
  const debounced = useDebouncedValue(caverQuery);
  const { data: cavers } = useCavers(debounced || undefined);
  const [selectedCaver, setSelectedCaver] = useState<string>();

  // A roster lists people, so the picker searches people — including those with no account,
  // who are exactly the members a club list would otherwise lose.
  const candidates = useMemo(
    () => (cavers ?? []).filter((caver) => !members?.some((m) => m.caverId === caver.id)),
    [cavers, members],
  );

  const add = async () => {
    if (!selectedCaver) {
      return;
    }
    try {
      await upsert.mutateAsync({ caverId: selectedCaver, role: 'member' });
      setSelectedCaver(undefined);
      setCaverQuery('');
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Drawer title={group.name} open onClose={onClose} size={460}>
      <Flex gap={8} style={{ marginBottom: 12 }}>
        <Select
          style={{ flex: 1 }}
          showSearch
          filterOption={false}
          placeholder={t('cavers.pick')}
          value={selectedCaver}
          onSearch={setCaverQuery}
          onChange={setSelectedCaver}
          options={candidates.map((caver) => ({
            value: caver.id,
            label: caver.name,
          }))}
          notFoundContent={null}
        />
        <Button icon={<PlusOutlined />} onClick={() => void add()} disabled={!selectedCaver}>
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
                  upsert.mutateAsync({ caverId: member.caverId, role }).catch(() =>
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
                  remove.mutateAsync(member.caverId).catch(() => message.error(t('common.saveFailed')))
                }
              >
                <Button size="small" type="text" danger icon={<DeleteOutlined />} />
              </Popconfirm>,
            ]}
          >
            <Flex gap={8} align="center">
              {member.name}
              {!member.userId && <Tag>{t('cavers.noAccount')}</Tag>}
            </Flex>
          </List.Item>
        )}
      />
    </Drawer>
  );
}

/** How long a notice may be, matching what the server refuses past. */
const MAX_ANNOUNCEMENT_LENGTH = 200;

/**
 * Writing to everyone on a caving group's roster.
 *
 * Three things this has that an ordinary form does not, and each is here because the act is a
 * broadcast rather than an edit. **The size of the audience is shown before anything is written**,
 * counted by the server over the same roster the send itself reads — a person about to tell two
 * hundred people something should see two hundred while they can still change their mind.
 * **Sending is a second, deliberate act**: the first button only moves to a step that repeats the
 * wording back and names the number, so a broadcast is never one careless click. And **what
 * actually happened is shown afterwards**, including the case where a club is large enough that
 * the notices are still being written when the answer comes back — otherwise the sender is left
 * wondering why nobody has replied.
 */
function AnnounceDialog({ group, onClose }: { group: CavingGroupInfo; onClose: () => void }) {
  const { t } = useTranslation();
  const { data: audience, isPending: counting } = useCavingGroupAnnouncementAudience(group.id);
  const announce = useAnnounceToCavingGroup(group.id);
  const [message, setMessage] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [sent, setSent] = useState<{ recipients: number; queued: boolean } | null>(null);
  const [failed, setFailed] = useState(false);

  const written = message.trim();
  const recipients = audience?.recipients ?? 0;

  const send = async () => {
    setFailed(false);
    try {
      const result = await announce.mutateAsync({ message: written });
      setSent({ recipients: result.recipients, queued: result.queued });
    } catch {
      setFailed(true);
    }
  };

  const footer = sent
    ? [
        <Button key="close" type="primary" onClick={onClose}>
          {t('common.close')}
        </Button>,
      ]
    : confirming
      ? [
          <Button key="back" onClick={() => setConfirming(false)}>
            {t('common.back')}
          </Button>,
          <Button key="send" type="primary" loading={announce.isPending} onClick={() => void send()}>
            {t('cavingGroups.announce.send')}
          </Button>,
        ]
      : [
          <Button key="cancel" onClick={onClose}>
            {t('common.cancel')}
          </Button>,
          <Button
            key="continue"
            type="primary"
            disabled={written.length === 0 || recipients === 0}
            onClick={() => setConfirming(true)}
          >
            {t('common.continue')}
          </Button>,
        ];

  return (
    <Modal
      title={t('cavingGroups.announce.title', { name: group.name })}
      open
      onCancel={onClose}
      footer={footer}
      destroyOnHidden
    >
      {sent ? (
        <Alert
          type="success"
          showIcon
          data-testid="announcement-result"
          title={
            sent.queued
              ? t('cavingGroups.announce.queued', { people: sent.recipients })
              : t('cavingGroups.announce.done', { people: sent.recipients })
          }
        />
      ) : (
        <>
          {/* The count first, and before anything is typed: it is the fact that decides whether
              this should be written at all, and putting it under the box would show it after the
              decision had already been made. */}
          {counting ? (
            <Skeleton.Input active size="small" style={{ marginBottom: 12 }} />
          ) : (
            <Alert
              type={recipients === 0 ? 'warning' : 'info'}
              showIcon
              data-testid="announcement-audience"
              style={{ marginBottom: 12 }}
              title={
                recipients === 0
                  ? t('cavingGroups.announce.nobody')
                  : t('cavingGroups.announce.reaches', { people: recipients })
              }
              description={recipients === 0 ? undefined : t('cavingGroups.announce.reachesHint')}
            />
          )}

          {confirming ? (
            <>
              <Typography.Paragraph strong>
                {t('cavingGroups.announce.confirm')}
              </Typography.Paragraph>
              {/* Repeated back rather than left in an editable box: the step exists to be read,
                  and a field that still looks editable invites another glance at the keyboard
                  instead of at the words. */}
              <Typography.Paragraph type="secondary">"{written}"</Typography.Paragraph>
            </>
          ) : (
            <Input.TextArea
              rows={3}
              value={message}
              maxLength={MAX_ANNOUNCEMENT_LENGTH}
              showCount
              aria-label={t('cavingGroups.announce.message')}
              placeholder={t('cavingGroups.announce.placeholder')}
              onChange={(event) => setMessage(event.target.value.replace(/[\r\n]+/g, ' '))}
            />
          )}

          {failed && (
            <Alert
              type="error"
              showIcon
              style={{ marginTop: 12 }}
              title={t('cavingGroups.announce.failed')}
            />
          )}
        </>
      )}
    </Modal>
  );
}

/** CavingGroups directory: browse for everyone, create for managers, manage members inline. */
export default function CavingGroupsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: cavingGroups, isFetching } = useCavingGroups();
  const createCavingGroup = useCreateCavingGroup();
  const [creating, setCreating] = useState(false);
  const [managing, setManaging] = useState<CavingGroupInfo | null>(null);
  const [counting, setCounting] = useState<CavingGroupInfo | null>(null);
  const [announcing, setAnnouncing] = useState<CavingGroupInfo | null>(null);
  const [form] = Form.useForm<{
    name: string;
    type: CavingGroupInfo['type'];
    description?: string;
    website?: string;
  }>();

  const canCreate = useCan('cavingGroups', 'create');

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
            width: 380,
            render: (_, group) => (
              <Flex gap={8}>
                <Button size="small" onClick={() => setManaging(group)}>
                  {t('cavingGroups.manage')}
                </Button>
                {/* What the club has done is counted over the trips this reader may see, so it
                    needs no gate of its own beyond being able to read the club at all. */}
                <Button size="small" icon={<BarChartOutlined />} onClick={() => setCounting(group)}>
                  {t('statistics.open')}
                </Button>
                {/* Offered per club rather than from the account's domain-wide rights: the right
                    to write to a roster can be given for one club and not another, and a check
                    that names no club cannot see such a rule at all. The server answers it per
                    row for exactly this. */}
                {group.canAnnounce && (
                  <Button
                    size="small"
                    icon={<NotificationOutlined />}
                    onClick={() => setAnnouncing(group)}
                  >
                    {t('cavingGroups.announce.open')}
                  </Button>
                )}
              </Flex>
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

      {announcing && (
        <AnnounceDialog group={announcing} onClose={() => setAnnouncing(null)} />
      )}

      <Drawer
        title={counting?.name}
        open={counting !== null}
        onClose={() => setCounting(null)}
        size={520}
        destroyOnHidden
      >
        {counting && <TripStatisticsPanel subject="cavingGroup" id={counting.id} />}
      </Drawer>
    </div>
  );
}
