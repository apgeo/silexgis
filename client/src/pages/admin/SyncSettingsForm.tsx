// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, App, Form, InputNumber, Select, Switch } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import { api } from '../../api/client.ts';
import type { AdminSettings, SyncSettings } from '../../api/hooks.ts';

/** The server refuses anything outside this; stated here so the field says so before it is pressed. */
const MIN_DAYS = 1;
const MAX_DAYS = 365;

/**
 * What this installation does with the phone app: whether phones are given the list of people and
 * whose names it holds, whether a trip recorded on a phone is made into a trip when part of it
 * could not be matched, and how long file bytes that no change uses are kept before they are
 * deleted.
 *
 * Saving replaces the whole section, so every field is carried on every save — a form that
 * forgot one would put it back to its default.
 */
export default function SyncSettingsForm({
  settings,
  onSaved,
}: {
  settings: AdminSettings;
  onSaved: (next: AdminSettings) => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<SyncSettings>();
  const [saving, setSaving] = useState(false);
  const scope = Form.useWatch('peopleDirectoryScope', form);
  const directoryOn = Form.useWatch('peopleDirectoryEnabled', form);

  useEffect(() => {
    form.setFieldsValue(settings.sync);
  }, [settings, form]);

  const save = async (values: SyncSettings) => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/sync', { body: values });
      if (error !== undefined || !data) {
        message.error(t('common.saveFailed'));
        return;
      }
      onSaved(data);
      message.success(t('common.saved'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Form
      form={form}
      layout="vertical"
      onFinish={save}
      requiredMark={false}
      style={{ maxWidth: 640 }}
      data-testid="sync-settings-form"
    >
      <Alert type="info" showIcon title={t('admin.messaging.syncIntro')} style={{ marginBottom: 16 }} />
      <Form.Item
        name="peopleDirectoryEnabled"
        label={t('admin.messaging.syncPeopleDirectoryEnabled')}
        valuePropName="checked"
        extra={t('admin.messaging.syncPeopleDirectoryEnabledHint')}
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="peopleDirectoryScope"
        label={t('admin.messaging.syncPeopleDirectoryScope')}
        extra={t('admin.messaging.syncPeopleDirectoryScopeHint')}
      >
        <Select
          // The list is not offered at all while the switch above is off, and a scope chosen for
          // a list nobody receives would read as if it did something.
          disabled={directoryOn === false}
          options={[
            { value: 'myGroups', label: t('admin.messaging.syncScopeMyGroups') },
            { value: 'everyone', label: t('admin.messaging.syncScopeEveryone') },
          ]}
        />
      </Form.Item>
      {scope === 'everyone' && directoryOn !== false && (
        <Alert
          type="warning"
          showIcon
          title={t('admin.messaging.syncScopeEveryoneWarning')}
          style={{ marginBottom: 24 }}
          data-testid="sync-scope-everyone-warning"
        />
      )}
      <Form.Item
        name="convertTripsWithProblems"
        label={t('admin.messaging.syncConvertTripsWithProblems')}
        valuePropName="checked"
        extra={t('admin.messaging.syncConvertTripsWithProblemsHint')}
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="stagedUploadRetentionDays"
        label={t('admin.messaging.syncStagedUploadRetentionDays')}
        extra={t('admin.messaging.syncStagedUploadRetentionDaysHint')}
        rules={[{ required: true, type: 'number', min: MIN_DAYS, max: MAX_DAYS }]}
      >
        <InputNumber min={MIN_DAYS} max={MAX_DAYS} precision={0} />
      </Form.Item>
      <Form.Item
        name="rejectedHeldBytesRetentionDays"
        label={t('admin.messaging.syncRejectedHeldBytesRetentionDays')}
        extra={t('admin.messaging.syncRejectedHeldBytesRetentionDaysHint')}
        rules={[{ required: true, type: 'number', min: MIN_DAYS, max: MAX_DAYS }]}
      >
        <InputNumber min={MIN_DAYS} max={MAX_DAYS} precision={0} />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={saving}>
        {t('common.save')}
      </Button>
    </Form>
  );
}
