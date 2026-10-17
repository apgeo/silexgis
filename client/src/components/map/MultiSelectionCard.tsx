// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { AimOutlined, ClearOutlined, SaveOutlined, TagsOutlined } from '@ant-design/icons';
import { App, Descriptions, Empty, Flex, Form, Input, Modal, Tag, Tooltip, Typography } from 'antd';
import Button from '../Button.tsx';
import { useTranslation } from 'react-i18next';
import { useFeatures, type FeatureListItem } from '../../api/hooks.ts';
import { useCreateSavedFilter } from '../../api/hooks/savedFilters.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import { viewFitGeometry } from '../../workspace/viewCamera.ts';

/**
 * What several selected objects have in common, and what can be done to all of them.
 *
 * Selecting more than one thing used to show nothing at all, which read as the selection having
 * failed. What a person wants at that moment is the count, whether the group is homogeneous, and
 * one place to act on it.
 *
 * The summary is computed from a list read that is already visibility-filtered, so an object in
 * the set that the caller may not read simply is not in the summary — the count says how many
 * were *asked* about and the summary says what came back, and the two are allowed to differ
 * without the panel inventing a "3 hidden" line, which would itself be a disclosure.
 */
export default function MultiSelectionCard() {
  const { t, i18n } = useTranslation();
  const selectionSet = useWorkspaceStore((s) => s.selectionSet);
  const clear = useWorkspaceStore((s) => s.clearSelectionSet);
  const setSelection = useWorkspaceStore((s) => s.setSelection);
  const source = useWorkspaceStore((s) => s.selectionSetSource);
  const { message } = App.useApp();
  const create = useCreateSavedFilter();
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<{ name: string; description?: string }>();

  // The filter vocabulary has no "these ids" condition, so the selection is saved as the filter
  // that produced it — offered only while the set still is that result. A hand-picked set, or one
  // cut to the map's window, has no filter that describes it, and saving the nearest one would
  // keep a different question under a name that claims this one.
  const savable = source?.document ?? null;
  const save = async () => {
    if (savable === null) return;
    const values = await form.validateFields();
    try {
      const created = await create.mutateAsync({
        name: values.name.trim(),
        description: values.description?.trim() || null,
        document: savable,
        visibility: 'private',
      });
      setSaving(false);
      form.resetFields();
      message.success(t('listFilters.saved', { name: created.name }));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const ids = selectionSet.map((ref) => ref.id);
  const { data } = useFeatures({ ids, pageSize: ids.length || 1 }, ids.length > 0);
  const items = data?.items ?? [];

  const shared = <K extends keyof FeatureListItem>(key: K): FeatureListItem[K] | undefined => {
    if (items.length === 0) {
      return undefined;
    }
    const first = items[0][key];
    return items.every((item) => item[key] === first) ? first : undefined;
  };

  const sharedType = shared('featureTypeCode');
  const sharedVisibility = shared('visibility');

  return (
    <div style={{ padding: 12, overflow: 'auto', height: '100%' }} data-testid="multi-selection">
      <Flex align="center" justify="space-between" gap={8} style={{ marginBottom: 8 }}>
        <Typography.Title level={5} style={{ margin: 0 }}>
          {t('panel.multiTitle', { count: selectionSet.length })}
        </Typography.Title>
        <Tooltip title={t('panel.clearSelection')}>
          <Button
            size="small"
            type="text"
            icon={<ClearOutlined />}
            aria-label={t('panel.clearSelection')}
            onClick={clear}
          />
        </Tooltip>
      </Flex>

      {source !== null && source.total > selectionSet.length && (
        <Typography.Text type="secondary" data-testid="multi-selection-capped" style={{ display: 'block', marginBottom: 8 }}>
          {t('workingSet.firstOf', {
            taken: selectionSet.length.toLocaleString(i18n.language),
            total: source.total.toLocaleString(i18n.language),
          })}
        </Typography.Text>
      )}

      <Descriptions column={1} size="small">
        <Descriptions.Item label={t('panel.multiReadable')}>{items.length}</Descriptions.Item>
        <Descriptions.Item label={t('features.type')}>
          {sharedType ?? <Typography.Text type="secondary">{t('panel.multiMixed')}</Typography.Text>}
        </Descriptions.Item>
        <Descriptions.Item label={t('features.visibility')}>
          {sharedVisibility ? (
            <Tag>{t(`caves.visibilityValues.${sharedVisibility}`)}</Tag>
          ) : (
            <Typography.Text type="secondary">{t('panel.multiMixed')}</Typography.Text>
          )}
        </Descriptions.Item>
      </Descriptions>

      <Flex gap={8} wrap style={{ marginTop: 12 }}>
        <Tooltip title={t('panel.zoomToAll')}>
          <Button
            size="small"
            icon={<AimOutlined />}
            disabled={items.every((item) => !item.geometry)}
            onClick={() => {
              // One envelope over everything that has a shape; a set whose members are all
              // withheld has nothing to fit and the control is off rather than doing nothing.
              const shapes = items.flatMap((item) => (item.geometry ? [item.geometry] : []));
              if (shapes.length > 0) {
                viewFitGeometry({
                  type: 'GeometryCollection',
                  geometries: shapes,
                } as never);
              }
            }}
          >
            {t('panel.zoomToAll')}
          </Button>
        </Tooltip>
        <Tooltip title={t('panel.bulkTagHint')}>
          <Button size="small" icon={<TagsOutlined />} disabled>
            {t('panel.bulkTag')}
          </Button>
        </Tooltip>
        {savable !== null && (
          <Button
            size="small"
            icon={<SaveOutlined />}
            data-testid="multi-selection-save-filter"
            onClick={() => setSaving(true)}
          >
            {t('workingSet.saveAsFilter')}
          </Button>
        )}
      </Flex>

      <Modal
        open={saving}
        title={t('workingSet.saveTitle')}
        okText={t('common.save')}
        confirmLoading={create.isPending}
        onOk={() => void save()}
        onCancel={() => setSaving(false)}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">{t('workingSet.saveHint')}</Typography.Paragraph>
        <Form form={form} layout="vertical" preserve={false}>
          <Form.Item
            name="name"
            label={t('savedFilters.name')}
            rules={[{ required: true, whitespace: true, max: 200 }]}
          >
            <Input data-testid="multi-selection-filter-name" autoFocus />
          </Form.Item>
          <Form.Item name="description" label={t('savedFilters.description')} rules={[{ max: 2000 }]}>
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Modal>

      <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginTop: 12 }}>
        {t('panel.multiHint')}
      </Typography.Text>

      {items.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('panel.multiNothingReadable')} />
      ) : (
        <Flex vertical gap={2} style={{ marginTop: 8 }}>
          {items.map((item) => (
            <Button
              key={item.id}
              type="text"
              size="small"
              style={{ justifyContent: 'flex-start' }}
              onClick={() => setSelection({ kind: 'feature', featureId: item.id })}
            >
              {item.name ?? t('features.unnamed')}
            </Button>
          ))}
        </Flex>
      )}
    </div>
  );
}
