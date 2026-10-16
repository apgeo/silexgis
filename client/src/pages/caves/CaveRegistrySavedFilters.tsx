// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CloseOutlined, SaveOutlined, SettingOutlined } from '@ant-design/icons';
import { Alert, App, Flex, Form, Input, Modal, Select, Space, Tag, Tooltip } from 'antd';
import Button from '../../components/Button.tsx';
import QuickFilterChips from '../../components/filters/QuickFilterChips.tsx';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  documentOf,
  useCreateSavedFilter,
  useSavedFilters,
  type SavedFilter,
} from '../../api/hooks/savedFilters.ts';
import {
  caveRegistryDocument,
  readCaveRegistry,
  sameQuestion,
  type CaveRegistryReading,
} from '../../filters/caveRegistry.ts';

interface Props {
  /** What the registry's controls and any extra conditions say now. */
  reading: CaveRegistryReading;
  /** The saved filter the conditions were taken from, if any. */
  savedFilterId?: string;
  onApply: (reading: CaveRegistryReading, savedFilterId: string) => void;
  onSaved: (savedFilterId: string) => void;
  /** Drops the conditions the controls cannot show. */
  onClearExtra: () => void;
  /** Opens the conditions the controls cannot show where they can be read and changed; the button pressed gets the focus back. */
  onEditExtra?: (opener: HTMLElement) => void;
  /** Stops naming a saved filter, keeping the conditions as they are. */
  onForget: () => void;
  /** Lifts the applied saved filter: no conditions, no saved filter named. */
  onRelease: () => void;
}

/**
 * Saved filters on the cave registry: apply one, see which is applied and whether the controls
 * have moved since, and save what is on screen as a new one.
 *
 * Applying a filter only fills the controls. The list is then asked with those conditions under
 * this account's own access, so a filter somebody shared shows exactly the caves this person
 * could have found by setting the same controls by hand — nothing more.
 */
export default function CaveRegistrySavedFilters({
  reading,
  savedFilterId,
  onApply,
  onSaved,
  onClearExtra,
  onEditExtra,
  onForget,
  onRelease,
}: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const { data: savedFilters, isSuccess } = useSavedFilters();
  const create = useCreateSavedFilter();
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<{ name: string; description?: string }>();

  const document = caveRegistryDocument(reading.controls, reading.extra);
  const active = savedFilterId === undefined
    ? undefined
    : savedFilters?.find((filter) => filter.id === savedFilterId);
  const activeReading = active ? readableAsCaves(active) : null;
  const changed = activeReading !== null && !sameQuestion(activeReading, reading);

  const apply = (id: string) => {
    const filter = savedFilters?.find((candidate) => candidate.id === id);
    if (!filter) return;
    if (filter.state === 'unreadable') {
      message.warning(t('caveRegistry.savedUnreadable'));
      return;
    }
    const read = readableAsCaves(filter);
    if (read === null) {
      message.warning(t('caveRegistry.savedNotAboutCaves'));
      return;
    }
    onApply(read, filter.id);
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
      message.success(t('caveRegistry.saved', { name: created.name }));
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
          placeholder={t('caveRegistry.applySaved')}
          aria-label={t('caveRegistry.applySaved')}
          value={null}
          data-testid="cave-registry-apply-saved"
          showSearch
          optionFilterProp="label"
          notFoundContent={t('caveRegistry.noSaved')}
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
          data-testid="cave-registry-save-filter"
          onClick={() => setSaving(true)}
        >
          {t('caveRegistry.saveAs')}
        </Button>
        <Button
          type="link"
          icon={<SettingOutlined />}
          data-testid="cave-registry-manage-filters"
          onClick={() => navigate('/saved-filters')}
        >
          {t('caveRegistry.manage')}
        </Button>
        {savedFilterId !== undefined && (
          <Tag
            color={changed ? 'gold' : 'blue'}
            data-testid="cave-registry-active-filter"
            closable
            closeIcon={<CloseOutlined aria-label={t('caveRegistry.forget')} />}
            onClose={(event) => {
              event.preventDefault();
              onForget();
            }}
          >
            {active
              ? t('caveRegistry.active', { name: active.name })
              : isSuccess
                ? t('caveRegistry.activeUnavailable')
                : '…'}
            {changed && (
              <span data-testid="cave-registry-active-changed">{` · ${t('caveRegistry.changed')}`}</span>
            )}
          </Tag>
        )}
        {active?.state === 'stale' && (
          <Tooltip title={t('savedFilters.staleHint')}>
            <Tag color="orange" data-testid="cave-registry-active-stale">
              {t('savedFilters.states.stale')}
            </Tag>
          </Tooltip>
        )}
      </Flex>
      <QuickFilterChips
        className="cave-registry-quick-filters"
        activeId={changed ? null : savedFilterId}
        onApply={(filter) => apply(filter.id)}
        onRelease={onRelease}
        data-testid="cave-registry-quick-filters"
      />
      {reading.extra.length > 0 && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="cave-registry-extra"
          title={t('caveRegistry.extra', { count: reading.extra.length })}
          action={
            <Space size={4}>
              {onEditExtra && (
                <Button size="small" onClick={(e) => onEditExtra(e.currentTarget)} data-testid="cave-registry-edit-extra">
                  {t('caveRegistry.editExtra')}
                </Button>
              )}
              <Button size="small" onClick={onClearExtra}>
                {t('caveRegistry.clearExtra')}
              </Button>
            </Space>
          }
        />
      )}
      <Modal
        open={saving}
        title={t('caveRegistry.saveTitle')}
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

/** A saved filter as the registry's controls can hold it, or null when it is not about caves. */
function readableAsCaves(filter: SavedFilter): CaveRegistryReading | null {
  const document = documentOf(filter);
  return filter.state === 'unreadable' || document === null ? null : readCaveRegistry(document);
}
