// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { DeleteOutlined, GroupOutlined, PlusOutlined } from '@ant-design/icons';
import {
  App, Alert, Button, Drawer, Flex, Form, Input, List, Modal, Popconfirm, Select, Spin, Table, Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import {
  hasAccessAction,
  useCapabilities,
  useCreateFeatureSet,
  useDeleteFeatureSet,
  useFeatures,
  useFeatureSetMembers,
  useFeatureSets,
  useReplaceFeatureSetMembers,
  useUpdateFeatureSet,
  type FeatureSetInfo,
  type FeatureKind,
} from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

interface DraftMember {
  id: string;
  name: string | null;
  kind: FeatureKind;
}

/**
 * Membership editor for one set. Editing membership moves access — rules hang on sets —
 * which is why the server audits the replace like a rule edit; nothing extra to do here
 * beyond saying so in the intro line.
 */
function MembersDrawer({ set, onClose }: { set: FeatureSetInfo; onClose: () => void }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: members, isPending } = useFeatureSetMembers(set.id);
  const replaceMembers = useReplaceFeatureSetMembers(set.id);
  const [drafts, setDrafts] = useState<DraftMember[]>([]);
  const [featureQuery, setFeatureQuery] = useState('');
  const [picked, setPicked] = useState<string>();
  const debouncedQuery = useDebouncedValue(featureQuery);
  const { data: featurePage } = useFeatures(
    { search: debouncedQuery || undefined, pageSize: 20 },
    debouncedQuery.trim().length >= 2,
  );

  useEffect(() => {
    if (members) {
      setDrafts(members.map((member) => ({ id: member.id, name: member.name, kind: member.kind })));
    }
  }, [members]);

  const options = (featurePage?.items ?? [])
    .filter((feature) => !drafts.some((draft) => draft.id === feature.id))
    .map((feature) => ({
      value: feature.id,
      label: feature.name ?? `${t(`featureKinds.${feature.kind}`)} ${feature.id.slice(0, 8)}`,
      kind: feature.kind,
    }));

  const addPicked = () => {
    const option = options.find((o) => o.value === picked);
    if (!option) {
      return;
    }
    setDrafts([...drafts, { id: option.value, name: option.label, kind: option.kind }]);
    setPicked(undefined);
    setFeatureQuery('');
  };

  const onSave = async () => {
    try {
      await replaceMembers.mutateAsync(drafts.map((draft) => draft.id));
      message.success(t('common.saved'));
      onClose();
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Drawer
      title={t('featureSets.membersTitle', { name: set.name })}
      open
      onClose={onClose}
      width={560}
      destroyOnHidden
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button type="primary" onClick={() => void onSave()} loading={replaceMembers.isPending}>
            {t('common.save')}
          </Button>
        </Flex>
      }
    >
      <Flex vertical gap={12}>
        <Alert type="info" showIcon message={t('featureSets.membershipMovesAccess')} />
        <Flex gap={8}>
          <Select
            style={{ flex: 1 }}
            showSearch
            filterOption={false}
            placeholder={t('featureSets.searchFeature')}
            value={picked}
            onSearch={setFeatureQuery}
            onChange={setPicked}
            options={options}
            notFoundContent={null}
          />
          <Button icon={<PlusOutlined />} onClick={addPicked} disabled={!picked}>
            {t('featureSets.addFeature')}
          </Button>
        </Flex>
        <List
          size="small"
          loading={isPending}
          dataSource={drafts}
          locale={{ emptyText: t('featureSets.noMembers') }}
          renderItem={(draft) => (
            <List.Item
              actions={[
                <Button
                  key="remove"
                  size="small"
                  type="text"
                  danger
                  icon={<DeleteOutlined />}
                  onClick={() => setDrafts(drafts.filter((d) => d.id !== draft.id))}
                />,
              ]}
            >
              <Flex gap={8} align="center">
                <Tag>{t(`featureKinds.${draft.kind}`)}</Tag>
                {draft.name ?? draft.id}
              </Flex>
            </List.Item>
          )}
        />
      </Flex>
    </Drawer>
  );
}

/**
 * Named feature sets — the way a rule says "these particular caves" when no area of the
 * containment hierarchy contains exactly them. Part of the security surface: a set with
 * rules pointing at it refuses deletion server-side, surfaced as such here.
 */
export default function FeatureSetsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.featureSets, 'read');
  const canCreate = hasAccessAction(capabilities?.domains.featureSets, 'create');
  const canWrite = hasAccessAction(capabilities?.domains.featureSets, 'write');
  const canDelete = hasAccessAction(capabilities?.domains.featureSets, 'delete');
  const { data: sets, isPending } = useFeatureSets(canRead);
  const createSet = useCreateFeatureSet();
  const deleteSet = useDeleteFeatureSet();
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<FeatureSetInfo | null>(null);
  const [renaming, setRenaming] = useState<FeatureSetInfo | null>(null);
  const updateSet = useUpdateFeatureSet(renaming?.id ?? '');
  const [form] = Form.useForm<{ name: string; description?: string }>();

  if (!capabilities) {
    return <Spin style={{ display: 'block', marginTop: '20vh' }} />;
  }
  if (!canRead) {
    return <Alert type="error" showIcon message={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  const submit = async () => {
    const values = await form.validateFields();
    const body = { name: values.name, description: values.description ?? null };
    try {
      if (renaming) {
        await updateSet.mutateAsync(body);
      } else {
        await createSet.mutateAsync(body);
      }
      setCreating(false);
      setRenaming(null);
      form.resetFields();
      message.success(t('common.saved'));
    } catch (error) {
      message.error(
        error instanceof ApiError && error.code === 'feature_set.name_taken'
          ? t('featureSets.nameTaken')
          : t('common.saveFailed'),
      );
    }
  };

  const onDelete = async (set: FeatureSetInfo) => {
    try {
      await deleteSet.mutateAsync(set.id);
      message.success(t('common.deleted'));
    } catch (error) {
      message.error(
        error instanceof ApiError && error.code === 'feature_set.in_use'
          ? t('featureSets.inUse')
          : t('common.saveFailed'),
      );
    }
  };

  return (
    <div style={{ padding: 24, maxWidth: 900 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          <GroupOutlined /> {t('featureSets.title')}
        </Typography.Title>
        {canCreate && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('featureSets.new')}
          </Button>
        )}
      </Flex>
      <Typography.Paragraph type="secondary">{t('featureSets.intro')}</Typography.Paragraph>

      <Table<FeatureSetInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isPending}
        dataSource={sets}
        pagination={false}
        columns={[
          { title: t('featureSets.name'), dataIndex: 'name' },
          { title: t('featureSets.description'), dataIndex: 'description', ellipsis: true },
          {
            title: t('featureSets.membersColumn'),
            dataIndex: 'memberCount',
            width: 110,
            align: 'right',
          },
          {
            title: '',
            key: 'actions',
            width: 220,
            render: (_, set) => (
              <Flex gap={8}>
                {canWrite && (
                  <>
                    <Button size="small" onClick={() => setEditing(set)}>
                      {t('featureSets.editMembers')}
                    </Button>
                    <Button
                      size="small"
                      onClick={() => {
                        setRenaming(set);
                        form.setFieldsValue({ name: set.name, description: set.description ?? undefined });
                      }}
                    >
                      {t('common.edit')}
                    </Button>
                  </>
                )}
                {canDelete && (
                  <Popconfirm title={t('featureSets.deleteConfirm')} onConfirm={() => void onDelete(set)}>
                    <Button size="small" type="text" danger icon={<DeleteOutlined />} />
                  </Popconfirm>
                )}
              </Flex>
            ),
          },
        ]}
      />

      <Modal
        title={renaming ? t('featureSets.edit') : t('featureSets.new')}
        open={creating || renaming !== null}
        onCancel={() => {
          setCreating(false);
          setRenaming(null);
          form.resetFields();
        }}
        onOk={() => void submit()}
        confirmLoading={createSet.isPending || updateSet.isPending}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" requiredMark={false}>
          <Form.Item name="name" label={t('featureSets.name')} rules={[{ required: true }, { max: 100 }]}>
            <Input />
          </Form.Item>
          <Form.Item name="description" label={t('featureSets.description')} rules={[{ max: 1000 }]}>
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>

      {editing && <MembersDrawer set={editing} onClose={() => setEditing(null)} />}
    </div>
  );
}
