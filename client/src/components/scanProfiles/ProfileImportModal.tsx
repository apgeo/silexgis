// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, App, Descriptions, Flex, Input, Modal, Space, Tag, Typography, Upload } from 'antd';
import { InboxOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  useImportProcessingProfile,
  type ProcessingProfileImportReport,
  type ProcessingProfileImportResult,
} from '../../api/hooks.ts';
import Button from '../Button.tsx';
import ProfileScopePicker from './ProfileScopePicker.tsx';
import { profileImportErrorText, scopeChoiceComplete, type ProfileScopeChoice } from './profileRules.ts';
import { STEP_LABEL_KEYS } from './scopeLabels.ts';

interface PickedFile {
  name: string;
  content: string;
}

/**
 * Reading a parameter file into a profile: HDMapping's own TOML (including the copy its odometry
 * writes next to every run) or a profile file SilexGIS exported.
 *
 * The file is read on the server twice. First as a dry run, which stores nothing and answers
 * what the import would make of it, key by key; that report is shown, and only an accepted one
 * can be kept. Then once more for real, with the name and scope chosen meanwhile. An import never
 * runs anything.
 */
export default function ProfileImportModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  return (
    <Modal
      open={open}
      title={t('settings.scanProfiles.import.title')}
      onCancel={onClose}
      footer={null}
      destroyOnHidden
      width={640}
    >
      {open && <ImportBody onClose={onClose} />}
    </Modal>
  );
}

function ImportBody({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const importer = useImportProcessingProfile();
  const [file, setFile] = useState<PickedFile | null>(null);
  const [scope, setScope] = useState<ProfileScopeChoice>({ scope: 'personal', cavingGroupId: null });
  const [name, setName] = useState('');
  const [preview, setPreview] = useState<ProcessingProfileImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const dryRun = async (picked: PickedFile) => {
    setPreview(null);
    setError(null);
    try {
      setPreview(
        await importer.mutateAsync({
          fileName: picked.name,
          content: picked.content,
          scope: 'personal',
          cavingGroupId: null,
          name: null,
          description: null,
          dryRun: true,
        }),
      );
    } catch (e) {
      setError(profileImportErrorText(e, t));
    }
  };

  const keep = async () => {
    if (!file) return;
    setError(null);
    try {
      await importer.mutateAsync({
        fileName: file.name,
        content: file.content,
        scope: scope.scope,
        cavingGroupId: scope.cavingGroupId,
        name: name.trim() || null,
        description: null,
        dryRun: false,
      });
      message.success(t('settings.scanProfiles.import.saved'));
      onClose();
    } catch (e) {
      setError(profileImportErrorText(e, t));
    }
  };

  return (
    <Flex vertical gap={16}>
      <Upload.Dragger
        accept=".toml,.json"
        multiple={false}
        maxCount={1}
        showUploadList={false}
        beforeUpload={(picked) => {
          void picked.text().then((content) => {
            const next = { name: picked.name, content };
            setFile(next);
            void dryRun(next);
          });
          return false;
        }}
        data-testid="profile-import-file"
      >
        <p className="ant-upload-drag-icon">
          <InboxOutlined />
        </p>
        <p className="ant-upload-text">{file ? file.name : t('settings.scanProfiles.import.pick')}</p>
        <p className="ant-upload-hint">{t('settings.scanProfiles.import.hint')}</p>
      </Upload.Dragger>

      {error && <Alert type="error" showIcon title={error} data-testid="profile-import-error" />}
      {importer.isPending && !preview && <Typography.Text type="secondary">{t('settings.scanProfiles.import.reading')}</Typography.Text>}
      {preview && <ImportReport report={preview.report} />}

      {preview?.report.accepted && (
        <>
          <Flex vertical gap={4}>
            <Typography.Text>{t('settings.scanProfiles.editor.name')}</Typography.Text>
            <Input
              value={name}
              maxLength={200}
              placeholder={t('settings.scanProfiles.import.namePlaceholder')}
              onChange={(event) => setName(event.target.value)}
              data-testid="profile-import-name"
            />
          </Flex>
          <Flex vertical gap={4}>
            <Typography.Text>{t('settings.scanProfiles.editor.scope')}</Typography.Text>
            <ProfileScopePicker value={scope} onChange={setScope} />
          </Flex>
        </>
      )}

      <Flex gap={8} justify="flex-end">
        <Button onClick={onClose}>{t('common.cancel')}</Button>
        <Button
          type="primary"
          disabled={!preview?.report.accepted || !scopeChoiceComplete(scope)}
          loading={importer.isPending && !!preview}
          onClick={() => void keep()}
          data-testid="profile-import-keep"
        >
          {t('settings.scanProfiles.import.keep')}
        </Button>
      </Flex>
    </Flex>
  );
}

function KeyList({ keys, color }: { keys: readonly string[]; color?: string }) {
  return (
    <Space size={[4, 4]} wrap>
      {keys.map((key) => (
        <Tag key={key} color={color}>
          <Typography.Text code style={{ fontSize: 'inherit' }}>
            {key}
          </Typography.Text>
        </Tag>
      ))}
    </Space>
  );
}

/**
 * What an import made of a file. Keys taken and keys filled from the defaults are counted (an
 * HDMapping dump has a few hundred of them, and naming them all says nothing); what was dropped,
 * held fixed or refused is named, because those are what the person has to know about.
 */
export function ImportReport({ report }: { report: ProcessingProfileImportReport }) {
  const { t } = useTranslation();
  const step = STEP_LABEL_KEYS[report.stepId] ? t(STEP_LABEL_KEYS[report.stepId]!) : report.stepId;
  return (
    <Flex vertical gap={12} data-testid="profile-import-report">
      {report.accepted ? (
        <Alert type="success" showIcon title={t('settings.scanProfiles.import.accepted')} />
      ) : (
        <Alert
          type="error"
          showIcon
          title={t('settings.scanProfiles.import.refusedTitle')}
          description={
            <ul style={{ margin: 0, paddingInlineStart: 20 }} data-testid="profile-import-refused">
              {report.refused.map((refusal) => (
                <li key={`${refusal.key}:${refusal.reason}`}>
                  {refusal.key && <Typography.Text code>{refusal.key}</Typography.Text>} {refusal.reason}
                </li>
              ))}
            </ul>
          }
        />
      )}
      {report.versionDiffers && (
        <Alert
          type="warning"
          showIcon
          data-testid="profile-import-version"
          title={t('settings.scanProfiles.import.versionDiffers', {
            file: report.fileVersion ?? '—',
            installed: report.installedVersion ?? '—',
          })}
        />
      )}
      <Descriptions size="small" column={1} bordered>
        <Descriptions.Item label={t('settings.scanProfiles.import.step')}>{step}</Descriptions.Item>
        <Descriptions.Item label={t('settings.scanProfiles.import.format')}>{report.format.toUpperCase()}</Descriptions.Item>
        <Descriptions.Item label={t('settings.scanProfiles.import.taken')}>
          <span data-testid="profile-import-taken">{report.taken.length}</span>
        </Descriptions.Item>
        <Descriptions.Item label={t('settings.scanProfiles.import.defaulted')}>{report.defaulted.length}</Descriptions.Item>
        {report.unknown.length > 0 && (
          <Descriptions.Item label={t('settings.scanProfiles.import.unknown')}>
            <span data-testid="profile-import-unknown">
              <KeyList keys={report.unknown} color="orange" />
            </span>
          </Descriptions.Item>
        )}
        {report.fixed.length > 0 && (
          <Descriptions.Item label={t('settings.scanProfiles.import.fixed')}>
            <KeyList keys={report.fixed} />
          </Descriptions.Item>
        )}
        {report.droppedSections.length > 0 && (
          <Descriptions.Item label={t('settings.scanProfiles.import.droppedSections')}>
            <KeyList keys={report.droppedSections} />
          </Descriptions.Item>
        )}
      </Descriptions>
    </Flex>
  );
}
