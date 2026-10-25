// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, App, Card, Checkbox, Flex, Skeleton, Switch, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import {
  normaliseAlwaysOutputs,
  useSaveSurveyUploadPreferences,
  useSurveyUploadCapabilities,
  useSurveyUploadPreferences,
} from '../../api/hooks.ts';

/**
 * The account's choices for surveys it uploads from a device: which results are always produced,
 * and whether an upload's results get a public link.
 *
 * The list of outputs comes from what the installation says it produces, never from a list written
 * into the page, so a result added on the server shows up here without a client change. What is
 * saved is the whole pair at once, because that is what the server replaces; the draft is held
 * only until saved, and the saved copy is what the form falls back to.
 */
export default function SurveyUploadSettingsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const capabilities = useSurveyUploadCapabilities();
  const preferences = useSurveyUploadPreferences();
  const save = useSaveSurveyUploadPreferences();
  const [draftOutputs, setDraftOutputs] = useState<string[] | null>(null);
  const [draftLinks, setDraftLinks] = useState<boolean | null>(null);

  if (capabilities.isPending || preferences.isPending) {
    return <Skeleton active paragraph={{ rows: 4 }} />;
  }
  if (capabilities.isError || preferences.isError) {
    return (
      <Alert
        type="error"
        showIcon
        title={t('settings.surveyUploads.loadFailed')}
        action={
          <Button
            onClick={() => {
              void capabilities.refetch();
              void preferences.refetch();
            }}
          >
            {t('common.retry')}
          </Button>
        }
      />
    );
  }

  const { catalogue, defaults } = capabilities.data.outputs;
  const outputs = draftOutputs ?? normaliseAlwaysOutputs(preferences.data.alwaysOutputs, catalogue);
  const publicLinks = draftLinks ?? preferences.data.publicLinks;
  const dirty = draftOutputs !== null || draftLinks !== null;
  const label = (name: string) => t(`surveyUploads.outputs.${name}`, { defaultValue: name });

  const saveAll = async () => {
    try {
      await save.mutateAsync({ alwaysOutputs: normaliseAlwaysOutputs(outputs, catalogue), publicLinks });
      setDraftOutputs(null);
      setDraftLinks(null);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Flex vertical gap={16}>
      <Card size="small" title={t('settings.surveyUploads.outputsTitle')}>
        <Flex vertical gap={12}>
          <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
            {t('settings.surveyUploads.outputsIntro', { defaults: defaults.map(label).join(', ') })}
          </Typography.Paragraph>
          <Checkbox.Group
            value={outputs}
            onChange={(values) => setDraftOutputs(values.map(String))}
            aria-label={t('settings.surveyUploads.outputsTitle')}
          >
            <Flex vertical gap={4}>
              {catalogue.map((name) => (
                <Checkbox key={name} value={name}>
                  {label(name)}
                </Checkbox>
              ))}
            </Flex>
          </Checkbox.Group>
        </Flex>
      </Card>

      <Card size="small" title={t('settings.surveyUploads.linksTitle')}>
        <Flex vertical gap={12}>
          <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
            {t('settings.surveyUploads.linksIntro')}
          </Typography.Paragraph>
          <Flex gap={8} align="center">
            <Switch
              checked={publicLinks}
              onChange={setDraftLinks}
              aria-label={t('settings.surveyUploads.linksSwitch')}
            />
            <Typography.Text>{t('settings.surveyUploads.linksSwitch')}</Typography.Text>
          </Flex>
        </Flex>
      </Card>

      <Flex>
        <Button type="primary" disabled={!dirty} loading={save.isPending} onClick={() => void saveAll()}>
          {t('common.save')}
        </Button>
      </Flex>
    </Flex>
  );
}
