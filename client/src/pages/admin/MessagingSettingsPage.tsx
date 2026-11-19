// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  AimOutlined,
  AppstoreOutlined,
  BellOutlined,
  CameraOutlined,
  CloudUploadOutlined,
  EnvironmentOutlined,
  ExperimentOutlined,
  FileImageOutlined,
  GlobalOutlined,
  ImportOutlined,
  MailOutlined,
  MobileOutlined,
  SafetyOutlined,
  SyncOutlined,
} from '@ant-design/icons';
import {
  Alert,
  Flex,
  Tabs,
  Typography,
} from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import MapBackgroundsForm from './MapBackgroundsForm.tsx';
import WorkspaceDefaultForm from './WorkspaceDefaultForm.tsx';
import SurveyUploadsSettingsForm from './SurveyUploadsSettingsForm.tsx';
import TestDataSettingsForm from './TestDataSettingsForm.tsx';
import MapSymbolsSettingsForm from './MapSymbolsSettingsForm.tsx';
import SelfHostedBasemapPanel from './SelfHostedBasemapPanel.tsx';
import SyncSettingsForm from './SyncSettingsForm.tsx';
import { useTranslation } from 'react-i18next';
import {
  hasAccessAction,
  queryKeys,
  useAdminSettings,
  useCapabilities,
  useMe,
  type AdminSettings,
} from '../../api/hooks.ts';
import MailForm from './MailForm.tsx';
import SmsForm from './SmsForm.tsx';
import PolicyForm from './PolicyForm.tsx';
import ProtectionForm from './ProtectionForm.tsx';
import ImportForm from './ImportForm.tsx';
import NotificationsForm from './NotificationsForm.tsx';
import AnnouncementsForm from './AnnouncementsForm.tsx';
import PhotoLibrariesForm from './PhotoLibrariesForm.tsx';

/**
 * Mail, SMS and sign-in policy for the whole installation.
 *
 * The same values can come from the deployment's environment; anything saved here replaces them.
 * Secrets are write-only — the page is told only whether one is stored, so a saved password can
 * never be read back out of it.
 */
export default function MessagingSettingsPage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { data: me } = useMe();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.settings, 'read');
  const { data: settings, isLoading } = useAdminSettings(canRead);

  const onSaved = (next: AdminSettings) => queryClient.setQueryData(queryKeys.adminSettings, next);

  // While capabilities load, render nothing rather than flashing a refusal at people
  // who do hold the right; the server enforces regardless.
  if (!capabilities) {
    return null;
  }
  if (!canRead) {
    return <Alert type="error" showIcon title={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  return (
    <Flex vertical gap={16} style={{ padding: 16 }}>
      <Typography.Title level={4} style={{ margin: 0 }}>
        <MailOutlined /> {t('admin.messaging.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('admin.messaging.intro')}
      </Typography.Paragraph>

      {isLoading || !settings ? null : (
        <Tabs
          items={[
            {
              key: 'mail',
              label: (
                <span>
                  <MailOutlined /> {t('admin.messaging.mailTab')}
                </span>
              ),
              children: <MailForm settings={settings} onSaved={onSaved} defaultTestRecipient={me?.email ?? ''} />,
            },
            {
              key: 'sms',
              label: (
                <span>
                  <MobileOutlined /> {t('admin.messaging.smsTab')}
                </span>
              ),
              children: <SmsForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'policy',
              label: (
                <span>
                  <SafetyOutlined /> {t('admin.messaging.policyTab')}
                </span>
              ),
              children: <PolicyForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'protection',
              label: (
                <span>
                  <EnvironmentOutlined /> {t('admin.messaging.protectionTab')}
                </span>
              ),
              children: <ProtectionForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'import',
              label: (
                <span>
                  <ImportOutlined /> {t('admin.messaging.importTab')}
                </span>
              ),
              children: <ImportForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'sync',
              label: (
                <span>
                  <SyncOutlined /> {t('admin.messaging.syncTab')}
                </span>
              ),
              children: <SyncSettingsForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'notifications',
              label: (
                <span>
                  <BellOutlined /> {t('admin.messaging.notificationsTab')}
                </span>
              ),
              children: (
                <Flex vertical gap={32}>
                  <NotificationsForm settings={settings} onSaved={onSaved} />
                  <AnnouncementsForm settings={settings} onSaved={onSaved} />
                </Flex>
              ),
            },
            {
              key: 'photo-libraries',
              label: (
                <span>
                  <CameraOutlined /> {t('admin.messaging.photoLibrariesTab')}
                </span>
              ),
              children: <PhotoLibrariesForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'survey-uploads',
              label: (
                <span>
                  <CloudUploadOutlined /> {t('admin.messaging.surveyUploadsTab')}
                </span>
              ),
              children: <SurveyUploadsSettingsForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'map-symbols',
              label: (
                <span>
                  <AimOutlined /> {t('mapSymbols.tab')}
                </span>
              ),
              children: <MapSymbolsSettingsForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'test-data',
              label: (
                <span>
                  <ExperimentOutlined /> {t('admin.messaging.testDataTab')}
                </span>
              ),
              children: <TestDataSettingsForm settings={settings} onSaved={onSaved} />,
            },
            {
              key: 'workspace',
              label: (
                <span>
                  <AppstoreOutlined /> {t('workspace.admin.tab')}
                </span>
              ),
              children: <WorkspaceDefaultForm settings={settings} onSaved={onSaved} />,
            },
            {
              // Not a section of the stored settings: it is kept on the catalogue's own rows,
              // so the form reads and writes for itself. It sits here because it is governed by
              // the same right and is the same kind of thing — the installation's answer to a
              // question only its administrators can answer.
              key: 'map-backgrounds',
              label: (
                <span>
                  <FileImageOutlined /> {t('admin.mapBackgrounds.tab')}
                </span>
              ),
              children: <MapBackgroundsForm />,
            },
            {
              // The installation's own vector basemap: read and downloaded on its own routes,
              // under the same right as the backgrounds above it.
              key: 'basemap',
              label: (
                <span>
                  <GlobalOutlined /> {t('admin.basemap.tab')}
                </span>
              ),
              children: <SelfHostedBasemapPanel />,
            },
          ]}
        />
      )}
    </Flex>
  );
}
