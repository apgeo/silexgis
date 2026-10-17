// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CloseOutlined, SaveOutlined, SettingOutlined } from '@ant-design/icons';
import { Alert, App, Flex, Form, Input, Modal, Select, Tag, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import Button from '../Button.tsx';
import QuickFilterChips from './QuickFilterChips.tsx';
import { documentOf, useCreateSavedFilter, useSavedFilters, type SavedFilter } from '../../api/hooks/savedFilters.ts';
import type { FilterDocument, FilterNode } from '../../filters/types.ts';

interface Props<R extends { extra: FilterNode[] }> {
  /** What the list's controls and any extra conditions say now. */
  reading: R;
  /** The document that reading composes, or null when it narrows nothing (nothing to save). */
  document: FilterDocument | null;
  /** A saved filter's document as this list's controls can hold it, or null when it is about something else. */
  read: (document: FilterDocument) => R | null;
  /** Whether two readings ask the same question. */
  same: (a: R, b: R) => boolean;
  /** The saved filter the conditions were taken from, if any. */
  savedFilterId?: string;
  /** Prefix of every test id, so two lists on one screen stay apart. */
  testIdPrefix: string;
  /** The message shown when a saved filter is about another kind of thing than this list. */
  notAboutThisListKey: string;
  onApply: (reading: R, savedFilterId: string) => void;
  onSaved: (savedFilterId: string) => void;
  /** Drops the conditions the controls cannot show. */
  onClearExtra: () => void;
  /** Stops naming a saved filter, keeping the conditions as they are. */
  onForget: () => void;
  /** Lifts the applied saved filter: no conditions, no saved filter named. */
  onRelease: () => void;
}

/**
 * Saved filters on a list: apply one, see which is applied and whether the controls have moved
 * since, pin-chips, and save what is on screen as a new one.
 *
 * Applying a filter only fills the controls. The list is then asked with those conditions under
 * this account's own access, so a filter somebody shared shows exactly the rows this person could
 * have found by setting the same controls by hand — nothing more.
 */
export default function ListSavedFilters<R extends { extra: FilterNode[] }>({
  reading,
  document,
  read,
  same,
  savedFilterId,
  testIdPrefix,
  notAboutThisListKey,
  onApply,
  onSaved,
  onClearExtra,
  onForget,
  onRelease,
}: Props<R>) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { data: savedFilters, isSuccess } = useSavedFilters();
  const create = useCreateSavedFilter();
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<{ name: string; description?: string }>();

  const readable = (filter: SavedFilter): R | null => {
    const saved = documentOf(filter);
    return filter.state === 'unreadable' || saved === null ? null : read(saved);
  };

  const active = savedFilterId === undefined
    ? undefined
    : savedFilters?.find((filter) => filter.id === savedFilterId);
  const activeReading = active ? readable(active) : null;
  const changed = activeReading !== null && !same(activeReading, reading);

  const apply = (id: string) => {
    const filter = savedFilters?.find((candidate) => candidate.id === id);
    if (!filter) return;
    if (filter.state === 'unreadable') {
      message.warning(t('listFilters.savedUnreadable'));
      return;
    }
    const found = readable(filter);
    if (found === null) {
      message.warning(t(notAboutThisListKey));
      return;
    }
    onApply(found, filter.id);
  };

  const save = async () => {
    if (document === null) return;
    const values = await form.validateFields();
    try {
      const created = await create.mutateAsync({
        name: values.name.trim(),
        description: values.description?.trim() || null,
        document,
        visibility: 'private',
      });
      setSaving(false);
      form.resetFields();
      message.success(t('listFilters.saved', { name: created.name }));
      onSaved(created.id);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <>
      <Flex gap={8} wrap align="center" style={{ marginBottom: 12 }}>
        <Select
          style={{ width: 260 }}
          placeholder={t('listFilters.applySaved')}
          aria-label={t('listFilters.applySaved')}
          value={null}
          data-testid={`${testIdPrefix}-apply-saved`}
          showSearch
          optionFilterProp="label"
          notFoundContent={t('listFilters.noSaved')}
          options={(savedFilters ?? []).map((filter) => ({
            value: filter.id,
            label: filter.name,
            disabled: filter.state === 'unreadable',
          }))}
          onChange={(id: string | null) => {
            if (id) apply(id);
          }}
        />
        <Button
          icon={<SaveOutlined />}
          disabled={document === null}
          data-testid={`${testIdPrefix}-save-filter`}
          onClick={() => setSaving(true)}
        >
          {t('listFilters.saveAs')}
        </Button>
        <Button
          type="link"
          icon={<SettingOutlined />}
          data-testid={`${testIdPrefix}-manage-filters`}
          onClick={() => navigate('/saved-filters')}
        >
          {t('listFilters.manage')}
        </Button>
        {savedFilterId !== undefined && (
          <Tag
            color={changed ? 'gold' : 'blue'}
            data-testid={`${testIdPrefix}-active-filter`}
            closable
            closeIcon={<CloseOutlined aria-label={t('listFilters.forget')} />}
            onClose={(event) => {
              event.preventDefault();
              onForget();
            }}
          >
            {active
              ? t('listFilters.active', { name: active.name })
              : isSuccess
                ? t('listFilters.activeUnavailable')
                : '…'}
            {changed && (
              <span data-testid={`${testIdPrefix}-active-changed`}>{` · ${t('listFilters.changed')}`}</span>
            )}
          </Tag>
        )}
        {active?.state === 'stale' && (
          <Tooltip title={t('savedFilters.staleHint')}>
            <Tag color="orange" data-testid={`${testIdPrefix}-active-stale`}>
              {t('savedFilters.states.stale')}
            </Tag>
          </Tooltip>
        )}
      </Flex>
      <QuickFilterChips
        className={`${testIdPrefix}-quick-filters`}
        activeId={changed ? null : savedFilterId}
        onApply={(filter) => apply(filter.id)}
        onRelease={onRelease}
        data-testid={`${testIdPrefix}-quick-filters`}
      />
      {reading.extra.length > 0 && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid={`${testIdPrefix}-extra`}
          title={t('listFilters.extra', { count: reading.extra.length })}
          action={
            <Button size="small" onClick={onClearExtra}>
              {t('listFilters.clearExtra')}
            </Button>
          }
        />
      )}
      <Modal
        open={saving}
        title={t('listFilters.saveTitle')}
        okText={t('common.save')}
        confirmLoading={create.isPending}
        onOk={() => void save()}
        onCancel={() => setSaving(false)}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" preserve={false}>
          <Form.Item
            name="name"
            label={t('savedFilters.name')}
            rules={[{ required: true, whitespace: true, max: 200 }]}
          >
            <Input data-testid="saved-filter-name" autoFocus />
          </Form.Item>
          <Form.Item name="description" label={t('savedFilters.description')} rules={[{ max: 2000 }]}>
            <Input.TextArea rows={3} data-testid="saved-filter-description" />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
