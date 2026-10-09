// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { BarChartOutlined, DeleteOutlined, MergeCellsOutlined, PlusOutlined } from '@ant-design/icons';
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
  Space,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCan,
  useCavers,
  useCavingGroups,
  useChangeCaverCavingGroups,
  useCreateCaver,
  useDeleteCaver,
  useMergeCavers,
  useUpdateCaver,
  type CaverInfo,
} from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';
import { CaverHeldByTripsModal } from '../../components/cavers/CaverHeldByTripsModal.tsx';
import {
  CAVER_HELD_BY_CAMP_CODE,
  caverHeldByTrips,
  type CaverHeldByTrips,
} from '../../components/cavers/caverHeldByTrips.ts';
import TripStatisticsPanel from '../../components/statistics/TripStatisticsPanel.tsx';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

interface CaverForm {
  fullName: string;
  shortName?: string;
  email?: string;
  phone?: string;
  notes?: string;
  cavingGroupIds?: string[];
}

/** Folds a duplicate entry into the one being kept; trips and memberships move with it. */
function MergeModal({ target, onClose }: { target: CaverInfo; onClose: () => void }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [query, setQuery] = useState('');
  const debounced = useDebouncedValue(query);
  const { data: candidates } = useCavers(debounced || undefined);
  const [source, setSource] = useState<string>();
  const merge = useMergeCavers(target.id);

  const run = async () => {
    if (!source) {
      return;
    }
    try {
      await merge.mutateAsync(source);
      message.success(t('common.saved'));
      onClose();
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Modal
      title={t('cavers.mergeInto', { name: target.name })}
      open
      onCancel={onClose}
      onOk={() => void run()}
      okButtonProps={{ disabled: !source }}
      confirmLoading={merge.isPending}
      destroyOnHidden
    >
      <Typography.Paragraph type="secondary">{t('cavers.mergeHint')}</Typography.Paragraph>
      <Select
        style={{ width: '100%' }}
        showSearch
        filterOption={false}
        placeholder={t('cavers.pick')}
        value={source}
        onSearch={setQuery}
        onChange={setSource}
        options={(candidates ?? [])
          .filter((c) => c.id !== target.id)
          .map((c) => ({ value: c.id, label: c.name }))}
        notFoundContent={null}
      />
    </Modal>
  );
}

/**
 * The roster of people. Most have no account — they are here so trips, statistics and credits can
 * name them — so the table leads with the name and marks who can sign in.
 */
export default function CaversPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search);
  // The previous rows stay while a changed search is answered: an emptied table takes with it
  // whatever stood open on a row — a delete confirmation somebody was about to answer.
  const { data: cavers, isFetching } = useCavers(debounced || undefined, undefined, {
    keepRows: true,
  });
  const createCaver = useCreateCaver();
  const deleteCaver = useDeleteCaver();
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<CaverInfo | null>(null);
  const [merging, setMerging] = useState<CaverInfo | null>(null);
  const [counting, setCounting] = useState<CaverInfo | null>(null);
  const [held, setHeld] = useState<{ caver: CaverInfo; by: CaverHeldByTrips } | null>(null);
  const [form] = Form.useForm<CaverForm>();

  // Contact fields and roster edits sit behind Cavers · Write (the label level every
  // account reads is not this page's business to gate).
  const canKeepRoster = useCan('cavers', 'write');
  // Deleting a person asks a right of its own, which an account that may edit the roster
  // need not hold; without it the button could only ever be answered by a refusal.
  const canDelete = useCan('cavers', 'delete');
  const updateCaver = useUpdateCaver(editing?.id ?? '');
  const { data: cavingGroups } = useCavingGroups();
  const changeCavingGroups = useChangeCaverCavingGroups();
  // The groups the person was in when the form opened, which is what a save is compared with.
  const groupsBefore = (editing?.cavingGroups ?? []).map((g) => g.cavingGroupId);
  const groupsChosen = Form.useWatch('cavingGroupIds', form);
  const groupsChanged =
    groupsChosen !== undefined &&
    (groupsChosen.length !== groupsBefore.length || groupsChosen.some((id) => !groupsBefore.includes(id)));
  // Every group the list offers, and any the person is already in that this account is not
  // shown in the list: a membership with no option would be drawn as a bare identifier.
  const groupOptions = [
    ...(cavingGroups ?? []).map((g) => ({ value: g.id, label: g.name })),
    ...(editing?.cavingGroups ?? [])
      .filter((g) => !(cavingGroups ?? []).some((known) => known.id === g.cavingGroupId))
      .map((g) => ({ value: g.cavingGroupId, label: g.name })),
  ];

  const submit = async () => {
    const values = await form.validateFields();
    const body = {
      fullName: values.fullName,
      // Sent on every save, an empty one included: the server writes what it is given, so a
      // form that left the field out would take the short name off whoever was edited.
      shortName: values.shortName?.trim() || null,
      email: values.email || null,
      phone: values.phone || null,
      notes: values.notes || null,
    };
    try {
      const saved = editing ? await updateCaver.mutateAsync(body) : await createCaver.mutateAsync(body);
      // The person first, then the groups: a membership needs somebody to be a member. Only what
      // the form changed is written, so a save that never touched the field writes no roster.
      const chosen = values.cavingGroupIds ?? groupsBefore;
      const join = chosen.filter((id) => !groupsBefore.includes(id));
      const leave = groupsBefore.filter((id) => !chosen.includes(id));
      const { refused } =
        join.length + leave.length > 0
          ? await changeCavingGroups.mutateAsync({ caverId: saved.id, join, leave })
          : { refused: [] as string[] };
      setCreating(false);
      setEditing(null);
      form.resetFields();
      if (refused.length > 0) {
        const names = refused.map((id) => groupOptions.find((option) => option.value === id)?.label ?? id);
        message.warning(t('cavers.cavingGroupsRefused', { names: names.join(', ') }), 8);
      } else {
        message.success(t('common.saved'));
      }
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const openEdit = (caver: CaverInfo) => {
    setEditing(caver);
    form.setFieldsValue({
      fullName: caver.name,
      shortName: caver.shortName ?? undefined,
      email: caver.email ?? undefined,
      phone: caver.phone ?? undefined,
      notes: caver.notes ?? undefined,
      cavingGroupIds: caver.cavingGroups.map((g) => g.cavingGroupId),
    });
  };

  return (
    <div style={{ padding: 24, maxWidth: 1000 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }} gap={12}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('cavers.title')}
        </Typography.Title>
        <Flex gap={8}>
          <Input.Search
            allowClear
            placeholder={t('cavers.search')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            style={{ width: 240 }}
          />
          {canKeepRoster && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
              {t('cavers.new')}
            </Button>
          )}
        </Flex>
      </Flex>

      <Table<CaverInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching && !cavers}
        dataSource={cavers}
        pagination={false}
        columns={[
          {
            title: t('cavers.name'),
            dataIndex: 'name',
            render: (name: string, caver) => (
              <Space>
                {name}
                {caver.shortName && <Typography.Text type="secondary">({caver.shortName})</Typography.Text>}
                {!caver.userId && <Tag>{t('cavers.noAccount')}</Tag>}
              </Space>
            ),
          },
          { title: t('cavers.email'), dataIndex: 'email', ellipsis: true },
          { title: t('cavers.phone'), dataIndex: 'phone' },
          {
            title: t('cavers.cavingGroups'),
            dataIndex: 'cavingGroups',
            render: (groups: CaverInfo['cavingGroups']) => (
              <Space size={4} wrap>
                {groups.map((g) => (
                  <Tag key={g.cavingGroupId}>{g.name}</Tag>
                ))}
              </Space>
            ),
          },
          {
            // Open to anyone who may read the roster: what comes back is already cut to the
            // trips this reader may see, so there is nothing further to gate here.
            title: '',
            key: 'statistics',
            width: 140,
            render: (_: unknown, caver: CaverInfo) => (
              <Button size="small" icon={<BarChartOutlined />} onClick={() => setCounting(caver)}>
                {t('statistics.open')}
              </Button>
            ),
          },
          ...(canKeepRoster
            ? [
                {
                  title: '',
                  key: 'actions',
                  width: 180,
                  render: (_: unknown, caver: CaverInfo) => (
                    <Space>
                      <Button size="small" onClick={() => openEdit(caver)}>
                        {t('common.edit')}
                      </Button>
                      <Button
                        size="small"
                        icon={<MergeCellsOutlined />}
                        onClick={() => setMerging(caver)}
                        title={t('cavers.merge')}
                      />
                      {canDelete && (
                        <Popconfirm
                          title={t('cavers.deleteConfirm')}
                          onConfirm={() =>
                            deleteCaver.mutateAsync(caver.id).catch((error: unknown) => {
                              // Trips hold them: the refusal says which, and the dialog is where
                              // something can be done about each.
                              const by = caverHeldByTrips(error);
                              if (by) {
                                setHeld({ caver, by });
                              } else if (error instanceof ApiError && error.code === CAVER_HELD_BY_CAMP_CODE) {
                                message.error(t('cavers.deleteRefusedCamp'));
                              } else {
                                message.error(t('cavers.deleteRefused'));
                              }
                            })
                          }
                        >
                          <Button
                            size="small"
                            type="text"
                            danger
                            icon={<DeleteOutlined />}
                            data-testid={`caver-delete-${caver.id}`}
                          />
                        </Popconfirm>
                      )}
                    </Space>
                  ),
                },
              ]
            : []),
        ]}
      />

      <Modal
        title={editing ? t('cavers.edit') : t('cavers.new')}
        open={creating || editing !== null}
        onCancel={() => {
          setCreating(false);
          setEditing(null);
          form.resetFields();
        }}
        onOk={() => void submit()}
        confirmLoading={createCaver.isPending || updateCaver.isPending || changeCavingGroups.isPending}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Form.Item name="fullName" label={t('cavers.name')} rules={[{ required: true }]}>
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="shortName" label={t('cavers.shortName')} extra={t('cavers.shortNameHint')}>
            <Input maxLength={80} data-testid="caver-short-name" />
          </Form.Item>
          <Form.Item name="email" label={t('cavers.email')}>
            <Input maxLength={320} />
          </Form.Item>
          <Form.Item name="phone" label={t('cavers.phone')}>
            <Input maxLength={40} />
          </Form.Item>
          <Form.Item name="notes" label={t('cavers.notes')} extra={t('cavers.notesHint')}>
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item name="cavingGroupIds" label={t('cavers.cavingGroups')} extra={t('cavers.cavingGroupsHint')}>
            <Select
              mode="multiple"
              allowClear
              optionFilterProp="label"
              options={groupOptions}
              data-testid="caver-caving-groups"
            />
          </Form.Item>
          {/* Said only once the choice differs from what is stored: a warning that is always on
              the form is one nobody reads by the time it matters. */}
          {groupsChanged && (
            <Alert
              type="warning"
              showIcon
              title={t('cavers.cavingGroupsWarningTitle')}
              description={t('cavers.cavingGroupsWarning')}
              data-testid="caver-caving-groups-warning"
            />
          )}
        </Form>
      </Modal>

      {merging && <MergeModal target={merging} onClose={() => setMerging(null)} />}

      {held && (
        <CaverHeldByTripsModal
          caver={held.caver}
          held={held.by}
          onClose={() => setHeld(null)}
          onMerge={() => {
            setMerging(held.caver);
            setHeld(null);
          }}
        />
      )}

      <Drawer
        title={counting?.name}
        open={counting !== null}
        onClose={() => setCounting(null)}
        size={520}
        destroyOnHidden
      >
        {counting && <TripStatisticsPanel subject="caver" id={counting.id} />}
      </Drawer>
    </div>
  );
}
