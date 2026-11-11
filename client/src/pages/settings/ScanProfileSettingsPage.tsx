// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, App, Card, Dropdown, Flex, Popconfirm, Select, Skeleton, Tag, Typography } from 'antd';
import { CopyOutlined, DeleteOutlined, DownloadOutlined, EditOutlined, EyeOutlined, PlusOutlined, UploadOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  exportsAsToml,
  processingProfileExportUrl,
  PROCESSING_PROFILE_SCOPES,
  PROFILE_STEP_IDS,
  useCavingGroups,
  useCopyProcessingProfile,
  useDeleteProcessingProfile,
  useProcessingProfiles,
  type ProcessingProfile,
  type ProcessingProfileScope,
} from '../../api/hooks.ts';
import { downloadFile } from '../../api/download.ts';
import Button from '../../components/Button.tsx';
import List from '../../components/List.tsx';
import ProfileEditorDrawer from '../../components/scanProfiles/ProfileEditorDrawer.tsx';
import ProfileImportModal from '../../components/scanProfiles/ProfileImportModal.tsx';
import { profilesByScope } from '../../components/scanProfiles/profileRules.ts';
import { SCOPE_LABEL_KEYS, STEP_LABEL_KEYS } from '../../components/scanProfiles/scopeLabels.ts';

/** Where a profile came from, as the list says it: shipped, edited, or read from a named file. */
function originLabel(origin: string, t: (key: string, options?: Record<string, unknown>) => string): string | null {
  if (origin.startsWith('imported:')) return t('settings.scanProfiles.origin.imported', { file: origin.slice('imported:'.length) });
  return null;
}

/**
 * Settings › Scans › Processing profiles: the named parameter sets a scan processing step can run
 * with, listed per scope (the installation's, the caving groups', one's own), each opened in an
 * editor generated from the step's schema. Built-ins can be read, copied and exported but never
 * changed; what else can be changed or deleted is what the server says per profile, never a rule
 * written into this page.
 */
export default function ScanProfileSettingsPage() {
  const { t } = useTranslation();
  const [stepId, setStepId] = useState<string>(PROFILE_STEP_IDS[0]);
  const profiles = useProcessingProfiles();
  const groups = useCavingGroups();
  const [editing, setEditing] = useState<{ profile: ProcessingProfile | null } | null>(null);
  const [importing, setImporting] = useState(false);

  const stepLabel = (id: string) => (STEP_LABEL_KEYS[id] ? t(STEP_LABEL_KEYS[id]!) : id);
  const groupName = (id: string | null) => groups.data?.find((g) => g.id === id)?.name;

  return (
    <Flex vertical gap={16}>
      <Typography.Title level={4} style={{ margin: 0 }}>
        {t('settings.scanProfiles.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('settings.scanProfiles.intro')}
      </Typography.Paragraph>

      <Flex gap={8} wrap align="center">
        <Select
          aria-label={t('settings.scanProfiles.stepLabel')}
          value={stepId}
          onChange={setStepId}
          options={PROFILE_STEP_IDS.map((id) => ({ value: id, label: stepLabel(id) }))}
          style={{ minWidth: 260 }}
          data-testid="profile-step"
        />
        <Button icon={<PlusOutlined />} onClick={() => setEditing({ profile: null })} data-testid="profile-new">
          {t('settings.scanProfiles.new')}
        </Button>
        <Button icon={<UploadOutlined />} onClick={() => setImporting(true)} data-testid="profile-import">
          {t('settings.scanProfiles.import.open')}
        </Button>
      </Flex>

      {profiles.isPending ? (
        <Skeleton active paragraph={{ rows: 4 }} />
      ) : profiles.isError ? (
        <Alert
          type="error"
          showIcon
          title={t('settings.scanProfiles.loadFailed')}
          action={<Button onClick={() => void profiles.refetch()}>{t('common.retry')}</Button>}
        />
      ) : (
        <ScopeLists
          byScope={profilesByScope(profiles.data, stepId)}
          groupName={groupName}
          onOpen={(profile) => setEditing({ profile })}
        />
      )}

      <ProfileEditorDrawer
        open={editing !== null}
        stepId={editing?.profile?.stepId ?? stepId}
        profile={editing?.profile ?? null}
        onClose={() => setEditing(null)}
      />
      <ProfileImportModal open={importing} onClose={() => setImporting(false)} />
    </Flex>
  );
}

function ScopeLists({
  byScope,
  groupName,
  onOpen,
}: {
  byScope: Record<ProcessingProfileScope, ProcessingProfile[]>;
  groupName: (id: string | null) => string | undefined;
  onOpen: (profile: ProcessingProfile) => void;
}) {
  const { t } = useTranslation();
  return (
    <>
      {PROCESSING_PROFILE_SCOPES.map((scope) => (
        <Card key={scope} size="small" title={t(SCOPE_LABEL_KEYS[scope])} data-testid={`profile-scope-${scope}`}>
          <List
            size="small"
            dataSource={byScope[scope]}
            locale={{ emptyText: t('settings.scanProfiles.emptyScope') }}
            renderItem={(profile) => (
              <ProfileRow key={profile.id} profile={profile} groupName={groupName(profile.cavingGroupId)} onOpen={onOpen} />
            )}
          />
        </Card>
      ))}
    </>
  );
}

function ProfileRow({
  profile,
  groupName,
  onOpen,
}: {
  profile: ProcessingProfile;
  groupName: string | undefined;
  onOpen: (profile: ProcessingProfile) => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const copy = useCopyProcessingProfile();
  const remove = useDeleteProcessingProfile();
  const origin = originLabel(profile.origin, t);
  const changeable = profile.mayEdit && !profile.builtIn;

  const duplicate = async () => {
    try {
      // The copy is one's own; the server names it after the original.
      await copy.mutateAsync({ id: profile.id });
      message.success(t('settings.scanProfiles.duplicated'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const download = (format: 'json' | 'toml') =>
    downloadFile(processingProfileExportUrl(profile.id, format)).catch(() => message.error(t('settings.scanProfiles.exportFailed')));

  return (
    <List.Item
      data-testid="profile-row"
      data-profile-id={profile.id}
      actions={[
        <Button
          key="open"
          size="small"
          icon={changeable ? <EditOutlined /> : <EyeOutlined />}
          onClick={() => onOpen(profile)}
          data-testid="profile-open"
        >
          {changeable ? t('common.edit') : t('settings.scanProfiles.view')}
        </Button>,
        <Button
          key="copy"
          size="small"
          icon={<CopyOutlined />}
          loading={copy.isPending}
          onClick={() => void duplicate()}
          aria-label={t('settings.scanProfiles.duplicate', { name: profile.name })}
          data-testid="profile-duplicate"
        />,
        exportsAsToml(profile.stepId) ? (
          <Dropdown
            key="export"
            // Opened by a press, not a hover, so it works the same on a phone.
            trigger={['click']}
            menu={{
              items: [
                { key: 'toml', label: t('settings.scanProfiles.exportToml'), onClick: () => void download('toml') },
                { key: 'json', label: t('settings.scanProfiles.exportJson'), onClick: () => void download('json') },
              ],
            }}
          >
            <Button
              size="small"
              icon={<DownloadOutlined />}
              aria-label={t('settings.scanProfiles.export', { name: profile.name })}
              data-testid="profile-export"
            />
          </Dropdown>
        ) : (
          <Button
            key="export"
            size="small"
            icon={<DownloadOutlined />}
            onClick={() => void download('json')}
            aria-label={t('settings.scanProfiles.export', { name: profile.name })}
            data-testid="profile-export"
          />
        ),
        ...(profile.mayDelete
          ? [
              <Popconfirm
                key="delete"
                title={t('settings.scanProfiles.deleteConfirm', { name: profile.name })}
                okText={t('iconButtons.delete')}
                okButtonProps={{ danger: true }}
                onConfirm={() =>
                  remove
                    .mutateAsync(profile.id)
                    .then(() => message.success(t('settings.scanProfiles.deleted')))
                    .catch(() => message.error(t('common.saveFailed')))
                }
              >
                <Button
                  size="small"
                  danger
                  icon={<DeleteOutlined />}
                  aria-label={t('settings.scanProfiles.delete', { name: profile.name })}
                  data-testid="profile-delete"
                />
              </Popconfirm>,
            ]
          : []),
      ]}
    >
      <List.Item.Meta
        title={
          <Flex gap={6} align="center" wrap>
            <span data-testid="profile-name-label">{profile.name}</span>
            {profile.builtIn && <Tag>{t('settings.scanProfiles.builtIn')}</Tag>}
            {profile.scope === 'cavingGroup' && groupName && <Tag color="blue">{groupName}</Tag>}
          </Flex>
        }
        description={[profile.description, origin].filter(Boolean).join(' · ') || undefined}
      />
    </List.Item>
  );
}
