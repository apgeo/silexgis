// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined, SearchOutlined } from '@ant-design/icons';
import { App, Flex, Form, Input, Modal, Popconfirm, Select, Space, Table, Tag, Tooltip, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useCaveTypes, useCavingGroups, useTags } from '../../api/hooks.ts';
import {
  documentOf,
  useDeleteSavedFilter,
  useSavedFilters,
  useUpdateSavedFilter,
  type SavedFilter,
} from '../../api/hooks/savedFilters.ts';
import { readCaveRegistry, writeCaveRegistryAddress } from '../../filters/caveRegistry.ts';
import { describeCaveRegistry, type ConditionPart } from '../../filters/describe.ts';

interface EditValues {
  name: string;
  description?: string;
  /** A caving group's id, or `private`. */
  audience: string;
}

const PRIVATE = 'private';

/**
 * The saved filters this account may read: its own, and those its caving groups were given.
 *
 * Kept with the account rather than with any one screen, because a filter is an object of its own
 * — saved from the cave registry today, and later the thing a map layer or a quick chip is made of.
 * What a filter says is shown by name only through lists this account may already read (the cave
 * types, the tags): a value it cannot resolve is shown as not available to it, never as an id.
 */
export default function SavedFiltersPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { data: filters, isLoading } = useSavedFilters();
  const { data: caveTypes } = useCaveTypes();
  const { data: tags } = useTags('');
  const { data: cavingGroups } = useCavingGroups();
  const update = useUpdateSavedFilter();
  const remove = useDeleteSavedFilter();
  const [editing, setEditing] = useState<SavedFilter | null>(null);
  const [form] = Form.useForm<EditValues>();

  const groupName = (id: string | null | undefined) =>
    cavingGroups?.find((group) => group.id === id)?.name ?? t('savedFilters.someGroup');

  const open = (filter: SavedFilter) => {
    const document = documentOf(filter);
    const reading = document === null ? null : readCaveRegistry(document);
    if (filter.state === 'unreadable' || reading === null) {
      message.warning(t('caveRegistry.savedUnreadable'));
      return;
    }
    const params = writeCaveRegistryAddress({ ...reading, savedFilterId: filter.id, page: 1, pageSize: 20 });
    navigate(`/caves?${params.toString()}`);
  };

  const startEdit = (filter: SavedFilter) => {
    setEditing(filter);
    form.setFieldsValue({
      name: filter.name,
      description: filter.description ?? undefined,
      audience: filter.visibility === 'cavingGroup' && filter.cavingGroupId ? filter.cavingGroupId : PRIVATE,
    });
  };

  const saveEdit = async () => {
    if (!editing) return;
    const values = await form.validateFields();
    const shared = values.audience !== PRIVATE;
    try {
      await update.mutateAsync({
        id: editing.id,
        write: {
          name: values.name.trim(),
          description: values.description?.trim() || null,
          // Null keeps the stored question: renaming or sharing one never re-sends it.
          document: null,
          cavingGroupId: shared ? values.audience : null,
          visibility: shared ? 'cavingGroup' : 'private',
        },
      });
      setEditing(null);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const partLabel = (part: ConditionPart): string => {
    switch (part.kind) {
      case 'search':
        return t('savedFilters.parts.search', { text: part.text });
      case 'region':
        return t('savedFilters.parts.region', { text: part.text });
      case 'minLength':
        return t('savedFilters.parts.minLength', { length: part.length });
      case 'caveType': {
        const name = caveTypes?.find((type) => String(type.id) === part.id)?.name;
        return t('savedFilters.parts.caveType', { name: name ?? t('savedFilters.notAvailable') });
      }
      case 'tag': {
        const name = tags?.find((tag) => tag.slug === part.slug)?.name;
        return t('savedFilters.parts.tag', { name: name ?? t('savedFilters.notAvailable') });
      }
      case 'more':
        return t('savedFilters.parts.more', { count: part.count });
    }
  };

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('savedFilters.title')}
        </Typography.Title>
        <Button onClick={() => navigate('/caves')}>{t('savedFilters.toRegistry')}</Button>
      </Flex>
      <Typography.Paragraph type="secondary">{t('savedFilters.intro')}</Typography.Paragraph>
      <Table<SavedFilter>
        rowKey="id"
        size="middle"
        loading={isLoading}
        dataSource={filters}
        data-testid="saved-filters-table"
        locale={{ emptyText: t('savedFilters.empty') }}
        columns={[
          {
            title: t('savedFilters.name'),
            dataIndex: 'name',
            render: (_: string, filter) => (
              <Flex vertical>
                <Typography.Text strong>{filter.name}</Typography.Text>
                {filter.description && <Typography.Text type="secondary">{filter.description}</Typography.Text>}
              </Flex>
            ),
          },
          {
            title: t('savedFilters.asks'),
            key: 'asks',
            render: (_: unknown, filter) => {
              const document = documentOf(filter);
              const parts = filter.state === 'unreadable' || document === null
                ? null
                : describeCaveRegistry(document);
              return parts === null ? (
                <Typography.Text type="secondary">{t('savedFilters.cannotShow')}</Typography.Text>
              ) : (
                <Flex gap={4} wrap>
                  {parts.map((part, index) => (
                    <Tag key={index}>{partLabel(part)}</Tag>
                  ))}
                </Flex>
              );
            },
          },
          {
            title: t('savedFilters.audience'),
            key: 'audience',
            width: 200,
            render: (_: unknown, filter) =>
              filter.visibility === 'cavingGroup' ? (
                <Tag color="blue">{groupName(filter.cavingGroupId)}</Tag>
              ) : (
                <Tag>{t('savedFilters.private')}</Tag>
              ),
          },
          {
            title: t('savedFilters.state'),
            dataIndex: 'state',
            width: 140,
            render: (state: SavedFilter['state']) => (
              <Tooltip title={state === 'ready' ? undefined : t(`savedFilters.${state}Hint`)}>
                <Tag
                  color={state === 'ready' ? 'green' : state === 'stale' ? 'orange' : 'red'}
                  data-testid={`saved-filter-state-${state}`}
                >
                  {t(`savedFilters.states.${state}`)}
                </Tag>
              </Tooltip>
            ),
          },
          {
            title: '',
            key: 'actions',
            width: 200,
            render: (_: unknown, filter) => (
              <Space>
                <Tooltip title={t('savedFilters.open')}>
                  <Button
                    size="small"
                    icon={<SearchOutlined />}
                    aria-label={t('savedFilters.open')}
                    data-testid={`saved-filter-open-${filter.id}`}
                    disabled={filter.state === 'unreadable'}
                    onClick={() => open(filter)}
                  />
                </Tooltip>
                {filter.mine && (
                  <>
                    <Tooltip title={t('savedFilters.edit')}>
                      <Button
                        size="small"
                        icon={<EditOutlined />}
                        aria-label={t('savedFilters.edit')}
                        data-testid={`saved-filter-edit-${filter.id}`}
                        onClick={() => startEdit(filter)}
                      />
                    </Tooltip>
                    <Popconfirm
                      title={t('savedFilters.deleteConfirm')}
                      okText={t('savedFilters.delete')}
                      onConfirm={() =>
                        remove.mutateAsync(filter.id).catch(() => message.error(t('common.saveFailed')))}
                    >
                      <Button
                        size="small"
                        danger
                        icon={<DeleteOutlined />}
                        aria-label={t('savedFilters.delete')}
                        data-testid={`saved-filter-delete-${filter.id}`}
                      />
                    </Popconfirm>
                  </>
                )}
              </Space>
            ),
          },
        ]}
      />
      <Modal
        open={editing !== null}
        title={t('savedFilters.edit')}
        okText={t('common.save')}
        confirmLoading={update.isPending}
        onOk={() => void saveEdit()}
        onCancel={() => setEditing(null)}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label={t('savedFilters.name')} rules={[{ required: true, whitespace: true, max: 200 }]}>
            <Input data-testid="saved-filter-edit-name" />
          </Form.Item>
          <Form.Item name="description" label={t('savedFilters.description')} rules={[{ max: 2000 }]}>
            <Input.TextArea rows={3} />
          </Form.Item>
          <Form.Item
            name="audience"
            label={t('savedFilters.audience')}
            extra={t('savedFilters.audienceHint')}
          >
            <Select
              data-testid="saved-filter-edit-audience"
              options={[
                { value: PRIVATE, label: t('savedFilters.private') },
                ...(cavingGroups ?? []).map((group) => ({
                  value: group.id,
                  label: t('savedFilters.sharedWith', { name: group.name }),
                })),
              ]}
            />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
