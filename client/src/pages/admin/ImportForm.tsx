// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  App,
  Alert,
  Form,
  InputNumber,
  Switch,
} from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client.ts';
import { type ImportSettings } from '../../api/hooks.ts';
import { useSectionFields } from './useSectionFields.ts';
import { isSettingsConflict, sectionIfMatch, settingsSaveProblem } from './settingsProblems.ts';
import { type SectionProps } from './settingsSection.tsx';

/**
 * How much this installation trusts a vector file to become registry objects on its own.
 *
 * Off is the shipped answer and the one to keep unless the club's rules have been tuned and
 * proved: rules are a guess about somebody's naming habits, and the difference between a bad
 * review and a bad auto-import is four hundred objects in the registry.
 */
export default function ImportForm({ settings, onSaved, onStale }: SectionProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<ImportSettings>();
  const [saving, setSaving] = useState(false);

  useSectionFields(form, settings.import);

  const save = async (values: ImportSettings) => {
    setSaving(true);
    try {
      // A save replaces the whole stored section, and the photo importers' two radii are part of
      // it without being fields here: sent as they are stored, or every save would reset them.
      const { data, error } = await api.PUT('/api/v1/admin/settings/import', {
        headers: sectionIfMatch(settings.versions?.import),
        body: { ...settings.import, ...values },
      });
      if (error !== undefined || !data) {
        message.error(settingsSaveProblem(error, t));
        if (isSettingsConflict(error)) {
          onStale?.();
        }
        return;
      }
      onSaved(data);
      message.success(t('common.saved'));
    } catch {
      // The request never reached the server (a dropped connection): said, not left unhandled.
      message.error(t('common.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Form form={form} layout="vertical" onFinish={save} requiredMark={false} style={{ maxWidth: 640 }}>
      <Alert type="info" showIcon title={t('admin.messaging.importIntro')} style={{ marginBottom: 16 }} />
      <Form.Item
        name="allowCreateWithoutReview"
        label={t('admin.messaging.allowWithoutReview')}
        valuePropName="checked"
        extra={t('admin.messaging.allowWithoutReviewHint')}
      >
        <Switch />
      </Form.Item>
      <Form.Item
        name="duplicateRadiusMeters"
        label={t('admin.messaging.duplicateRadius')}
        extra={t('admin.messaging.duplicateRadiusHint')}
      >
        {/* The unit is in the label rather than an addon: antd deprecated addonAfter here, and
            the warning it prints is a console error the browser run refuses. */}
        <InputNumber min={0} max={5000} step={5} />
      </Form.Item>
      <Form.Item
        name="duplicateNameSimilarity"
        label={t('admin.messaging.duplicateSimilarity')}
        extra={t('admin.messaging.duplicateSimilarityHint')}
      >
        <InputNumber min={0} max={1} step={0.05} />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={saving}>
        {t('common.save')}
      </Button>
    </Form>
  );
}
