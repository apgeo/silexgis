// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined, PushpinFilled, PushpinOutlined, SearchOutlined } from '@ant-design/icons';
import { App, Flex, Form, Input, Modal, Popconfirm, Space, Table, Tag, Tooltip, Typography } from 'antd';
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
import { readGalleryDocument, writeGalleryFilter } from '../../filters/gallery.ts';
import { describeCaveRegistry, type ConditionPart } from '../../filters/describe.ts';
import { MAX_PINNED_FILTERS, useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import { describeComparison } from '../../filters/typedComparison.ts';
import SavedFilterAudienceField from '../../components/filters/SavedFilterAudienceField.tsx';
import { savedFilterSaveFailedKey, useSavedFilterAudienceChoice } from '../../components/filters/useSavedFilterAudienceChoice.ts';
import { audienceFields, type SavedFilterAudience } from '../../filters/savedFilterAudience.ts';

interface EditValues {
  name: string;
  description?: string;
  audience: SavedFilterAudience;
}

/** The audience a stored filter has, as the share control holds it. */
function audienceOf(filter: SavedFilter): SavedFilterAudience {
  if (filter.visibility === 'cavingGroup' && filter.cavingGroupId) {
    return { visibility: 'cavingGroup', cavingGroupId: filter.cavingGroupId };
  }
  return filter.visibility === 'authenticated' ? { visibility: 'authenticated' } : { visibility: 'private' };
}

/** Where a saved filter opens: the screen that owns its world, or nowhere when none here does. */
type OpenTarget =
  | { screen: 'caves'; reading: NonNullable<ReturnType<typeof readCaveRegistry>> }
  | { screen: 'gallery'; where: NonNullable<ReturnType<typeof readGalleryDocument>> | null }
  | null;

/**
 * Which screen can open this filter. A filter this account cannot read opens nowhere, and so does
 * one about a kind of record this screen has no list for — that is a statement about the screen,
 * not about the reader's access, and the two must not be worded alike.
 */
function openTargetOf(filter: SavedFilter): OpenTarget {
  const document = documentOf(filter);
  if (filter.state === 'unreadable' || document === null) {
    return null;
  }
  const reading = readCaveRegistry(document);
  if (reading !== null) {
    return { screen: 'caves', reading };
  }
  const where = readGalleryDocument(document);
  return where === undefined ? null : { screen: 'gallery', where };
}

/**
 * The saved filters this account may read: its own, and those its caving groups were given.
 *
 * Kept with the account rather than with any one screen, because a filter is an object of its own
 * — saved from the cave registry today, and later the thing a map layer or a quick chip is made of.
 * What a filter says is shown by name only through lists this account may already read (the cave
 * types, the tags): a value it cannot resolve is shown as not available to it, never as an id.
 *
 * A shared filter names who saved it as the server names that member to this reader — never an
 * address — or as "a club member" when the account is gone. Rename and delete are offered where
 * the server says this reader may make them (its author, or a full administrator on any filter),
 * not from whether the reader wrote it.
 */
export default function SavedFiltersPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { data: filters, isLoading } = useSavedFilters();
  const { data: caveTypes } = useCaveTypes();
  const { data: tags } = useTags('');
  // Every club, to name the group a listed filter is shared with. The share control offers the
  // member's own groups first and every other club after them, which the server may refuse.
  const { data: cavingGroups } = useCavingGroups();
  const { groups: shareGroups, otherGroups } = useSavedFilterAudienceChoice();
  const update = useUpdateSavedFilter();
  const remove = useDeleteSavedFilter();
  const [editing, setEditing] = useState<SavedFilter | null>(null);
  const [form] = Form.useForm<EditValues>();
  const pinnedFilterIds = useUiPrefsStore((s) => s.pinnedFilterIds);
  const togglePinnedFilter = useUiPrefsStore((s) => s.togglePinnedFilter);
  const pinsFull = pinnedFilterIds.length >= MAX_PINNED_FILTERS;

  const groupName = (id: string | null | undefined) =>
    cavingGroups?.find((group) => group.id === id)?.name ?? t('savedFilters.someGroup');

  const open = (filter: SavedFilter) => {
    const target = openTargetOf(filter);
    if (filter.state === 'unreadable') {
      message.warning(t('caveRegistry.savedUnreadable'));
      return;
    }
    if (target === null) {
      message.info(t('savedFilters.noScreenToOpen'));
      return;
    }
    if (target.screen === 'gallery') {
      const params = writeGalleryFilter(new URLSearchParams(), { where: target.where, savedFilterId: filter.id });
      navigate(`/gallery?${params.toString()}`);
      return;
    }
    const params = writeCaveRegistryAddress({ ...target.reading, savedFilterId: filter.id, page: 1, pageSize: 20 });
    navigate(`/caves?${params.toString()}`);
  };

  const startEdit = (filter: SavedFilter) => {
    setEditing(filter);
    form.setFieldsValue({
      name: filter.name,
      description: filter.description ?? undefined,
      audience: audienceOf(filter),
    });
  };

  const saveEdit = async () => {
    if (!editing) return;
    const values = await form.validateFields();
    try {
      await update.mutateAsync({
        id: editing.id,
        write: {
          name: values.name.trim(),
          description: values.description?.trim() || null,
          // Null keeps the stored question: renaming or sharing one never re-sends it.
          document: null,
          ...audienceFields(values.audience),
        },
      });
      setEditing(null);
      message.success(t('common.saved'));
    } catch (error) {
      message.error(t(savedFilterSaveFailedKey(error)));
    }
  };

  const partLabel = (part: ConditionPart): string => {
    switch (part.kind) {
      case 'search':
        return t('savedFilters.parts.search', { text: part.text });
      case 'region':
        return t('savedFilters.parts.region', { text: part.text });
      case 'length': {
        const described = describeComparison({ ...part.length, unit: 'm' }, 'number');
        return t('savedFilters.parts.length', { comparison: t(described.key, described.values) });
      }
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
              if (parts === null && openTargetOf(filter)?.screen === 'gallery') {
                return <Tag data-testid={`saved-filter-world-${filter.id}`}>{t('savedFilters.photographFilter')}</Tag>;
              }
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
            title: t('savedFilters.author'),
            key: 'author',
            width: 180,
            render: (_: unknown, filter) => (
              <Typography.Text data-testid={`saved-filter-author-${filter.id}`}>
                {filter.mine ? t('savedFilters.byYou') : filter.authorName ?? t('savedFilters.someMember')}
              </Typography.Text>
            ),
          },
          {
            title: t('savedFilters.audience'),
            key: 'audience',
            width: 200,
            render: (_: unknown, filter) =>
              filter.visibility === 'cavingGroup' ? (
                <Tag color="blue">{groupName(filter.cavingGroupId)}</Tag>
              ) : filter.visibility === 'authenticated' ? (
                <Tag color="green" data-testid={`saved-filter-audience-${filter.id}`}>{t('savedFilters.authenticated')}</Tag>
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
            render: (_: unknown, filter) => {
              const pinned = pinnedFilterIds.includes(filter.id.toLowerCase());
              const pinLabel = pinned ? t('quickFilters.unpinRow') : t('quickFilters.pin');
              return (
                <Space>
                  {/* Any filter this account can read can be pinned, its own or a group's: a pin
                      is a shortcut kept with the account, and the chip runs the filter under the
                      account's own access like every other use of it. */}
                  <Tooltip title={!pinned && pinsFull ? t('quickFilters.full', { count: MAX_PINNED_FILTERS }) : pinLabel}>
                    <Button
                      size="small"
                      icon={pinned ? <PushpinFilled /> : <PushpinOutlined />}
                      aria-label={pinLabel}
                      aria-pressed={pinned}
                      disabled={!pinned && pinsFull}
                      data-testid={`saved-filter-pin-${filter.id}`}
                      onClick={() => togglePinnedFilter(filter.id)}
                    />
                  </Tooltip>
                  <Tooltip title={filter.state !== 'unreadable' && openTargetOf(filter) === null ? t('savedFilters.noScreenToOpen') : t('savedFilters.open')}>
                    <Button
                      size="small"
                      icon={<SearchOutlined />}
                      aria-label={t('savedFilters.open')}
                      data-testid={`saved-filter-open-${filter.id}`}
                      disabled={filter.state === 'unreadable' || openTargetOf(filter) === null}
                      onClick={() => open(filter)}
                    />
                  </Tooltip>
                  {filter.mayEdit && (
                    <Tooltip title={t('savedFilters.edit')}>
                      <Button
                        size="small"
                        icon={<EditOutlined />}
                        aria-label={t('savedFilters.edit')}
                        data-testid={`saved-filter-edit-${filter.id}`}
                        onClick={() => startEdit(filter)}
                      />
                    </Tooltip>
                  )}
                  {filter.mayDelete && (
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
                  )}
                </Space>
              );
            },
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
          <Form.Item name="audience" label={t('savedFilters.audience')}>
            <SavedFilterAudienceField
              groups={shareGroups}
              otherGroups={otherGroups}
              groupName={(id) => cavingGroups?.find((group) => group.id === id)?.name}
              testId="saved-filter-edit-audience"
            />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
