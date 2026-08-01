// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, MergeCellsOutlined, PlusOutlined } from '@ant-design/icons';
import {
  App,
  Button,
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
  useCreateCaver,
  useDeleteCaver,
  useMergeCavers,
  useUpdateCaver,
  type CaverInfo,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

interface CaverForm {
  fullName: string;
  email?: string;
  phone?: string;
  notes?: string;
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
  const { data: cavers, isFetching } = useCavers(debounced || undefined);
  const createCaver = useCreateCaver();
  const deleteCaver = useDeleteCaver();
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<CaverInfo | null>(null);
  const [merging, setMerging] = useState<CaverInfo | null>(null);
  const [form] = Form.useForm<CaverForm>();

  // Contact fields and roster edits sit behind Cavers · Write (the label level every
  // account reads is not this page's business to gate).
  const canKeepRoster = useCan('cavers', 'write');
  const updateCaver = useUpdateCaver(editing?.id ?? '');

  const submit = async () => {
    const values = await form.validateFields();
    const body = {
      fullName: values.fullName,
      email: values.email || null,
      phone: values.phone || null,
      notes: values.notes || null,
    };
    try {
      if (editing) {
        await updateCaver.mutateAsync(body);
      } else {
        await createCaver.mutateAsync(body);
      }
      setCreating(false);
      setEditing(null);
      form.resetFields();
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const openEdit = (caver: CaverInfo) => {
    setEditing(caver);
    form.setFieldsValue({
      fullName: caver.name,
      email: caver.email ?? undefined,
      phone: caver.phone ?? undefined,
      notes: caver.notes ?? undefined,
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
                      <Popconfirm
                        title={t('cavers.deleteConfirm')}
                        onConfirm={() =>
                          deleteCaver
                            .mutateAsync(caver.id)
                            .catch(() => message.error(t('cavers.deleteRefused')))
                        }
                      >
                        <Button size="small" type="text" danger icon={<DeleteOutlined />} />
                      </Popconfirm>
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
        confirmLoading={createCaver.isPending || updateCaver.isPending}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Form.Item name="fullName" label={t('cavers.name')} rules={[{ required: true }]}>
            <Input maxLength={200} />
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
        </Form>
      </Modal>

      {merging && <MergeModal target={merging} onClose={() => setMerging(null)} />}
    </div>
  );
}
