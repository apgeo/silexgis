// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { Alert, App, Drawer, Flex, Form, Input, Modal, Skeleton, Switch, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCopyProcessingProfile,
  useCreateProcessingProfile,
  useScanStepParameterSchema,
  useUpdateProcessingProfile,
  type ProcessingProfile,
} from '../../api/hooks.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import Button from '../Button.tsx';
import ProfileParameterForm from './ProfileParameterForm.tsx';
import ProfileScopePicker from './ProfileScopePicker.tsx';
import { profileSaveErrorText, scopeChoiceComplete, type ProfileScopeChoice } from './profileRules.ts';
import { changedFields, parameterGroups } from './parameterSchema.ts';
import { STEP_LABEL_KEYS } from './scopeLabels.ts';

interface ProfileEditorDrawerProps {
  open: boolean;
  stepId: string;
  /** The profile being edited; null for a new one, which starts from the step's defaults. */
  profile: ProcessingProfile | null;
  onClose: () => void;
}

/**
 * One profile's settings in a form generated from the step's schema.
 *
 * A built-in, or a shared profile the caller may not change, opens all the same: its settings
 * can be changed in the form and kept as a copy of one's own, which is how a built-in is ever
 * "edited" — the original never changes in place. "Only what differs" narrows the form to the
 * settings that are not the installed program's defaults, which is the short answer to "what
 * does this profile actually do".
 */
export default function ProfileEditorDrawer({ open, stepId, profile, onClose }: ProfileEditorDrawerProps) {
  const { t } = useTranslation();
  const isMobile = useIsMobile();
  return (
    <Drawer
      open={open}
      onClose={onClose}
      size={isMobile ? '100%' : 760}
      destroyOnHidden
      title={
        profile
          ? t('settings.scanProfiles.editor.titleEdit', { name: profile.name })
          : t('settings.scanProfiles.editor.titleNew', {
              step: STEP_LABEL_KEYS[stepId] ? t(STEP_LABEL_KEYS[stepId]) : stepId,
            })
      }
      data-testid="profile-editor"
    >
      {open && <EditorBody stepId={stepId} profile={profile} onClose={onClose} />}
    </Drawer>
  );
}

function EditorBody({ stepId, profile, onClose }: Omit<ProfileEditorDrawerProps, 'open'>) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const schema = useScanStepParameterSchema(stepId);
  const create = useCreateProcessingProfile();
  const update = useUpdateProcessingProfile();
  const copy = useCopyProcessingProfile();
  const [values, setValues] = useState<unknown>(profile?.values);
  const [name, setName] = useState(profile?.name ?? '');
  const [description, setDescription] = useState(profile?.description ?? '');
  const [scope, setScope] = useState<ProfileScopeChoice>({ scope: 'personal', cavingGroupId: null });
  const [onlyChanged, setOnlyChanged] = useState(false);
  const [saveAsOpen, setSaveAsOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const groups = useMemo(
    () => (schema.data ? parameterGroups(schema.data.schema, schema.data.defaults) : []),
    [schema.data],
  );

  if (schema.isPending) return <Skeleton active paragraph={{ rows: 6 }} />;
  if (schema.isError) {
    return (
      <Alert
        type="error"
        showIcon
        title={t('settings.scanProfiles.editor.schemaFailed')}
        action={<Button onClick={() => void schema.refetch()}>{t('common.retry')}</Button>}
      />
    );
  }

  // A new profile starts as the step's defaults, written out, so what is saved is what it ran with.
  const current = values ?? schema.data.defaults;
  const changed = changedFields(groups, current);
  const mayChangeInPlace = !!profile && profile.mayEdit && !profile.builtIn;
  const busy = create.isPending || update.isPending || copy.isPending;

  const saved = (text: string) => {
    message.success(text);
    onClose();
  };
  const failed = (e: unknown) => setError(profileSaveErrorText(e, t));

  const save = async () => {
    setError(null);
    try {
      if (profile) {
        await update.mutateAsync({ id: profile.id, name: name.trim(), description: description.trim() || null, values: current });
      } else {
        await create.mutateAsync({
          stepId,
          name: name.trim(),
          description: description.trim() || null,
          scope: scope.scope,
          cavingGroupId: scope.cavingGroupId,
          values: current,
        });
      }
      saved(t('settings.scanProfiles.editor.saved'));
    } catch (e) {
      failed(e);
    }
  };

  const saveAs = async (choice: { name: string; scope: ProfileScopeChoice }) => {
    setError(null);
    try {
      if (profile) {
        await copy.mutateAsync({
          id: profile.id,
          name: choice.name,
          description: description.trim() || null,
          scope: choice.scope.scope,
          cavingGroupId: choice.scope.cavingGroupId,
          values: current,
        });
      } else {
        await create.mutateAsync({
          stepId,
          name: choice.name,
          description: description.trim() || null,
          scope: choice.scope.scope,
          cavingGroupId: choice.scope.cavingGroupId,
          values: current,
        });
      }
      setSaveAsOpen(false);
      saved(t('settings.scanProfiles.editor.saved'));
    } catch (e) {
      setSaveAsOpen(false);
      failed(e);
    }
  };

  return (
    <Flex vertical gap={16}>
      {profile?.builtIn && (
        <Alert type="info" showIcon title={t('settings.scanProfiles.editor.builtInNotice')} data-testid="profile-built-in" />
      )}
      {profile && !profile.builtIn && !profile.mayEdit && (
        <Alert type="info" showIcon title={t('settings.scanProfiles.editor.notYoursNotice')} />
      )}
      {schema.data.toolVersion && (
        <Typography.Text type="secondary">
          {t('settings.scanProfiles.editor.toolVersion', { version: schema.data.toolVersion })}
        </Typography.Text>
      )}
      {schema.data.source === 'registry' && stepId === 'hdmapping.odometry' && (
        <Alert type="warning" showIcon title={t('settings.scanProfiles.editor.noWorkerYet')} />
      )}

      <Form layout="vertical" component="div">
        <Form.Item label={t('settings.scanProfiles.editor.name')} required>
          <Input
            value={name}
            maxLength={200}
            onChange={(event) => setName(event.target.value)}
            disabled={!!profile && !mayChangeInPlace}
            data-testid="profile-name"
          />
        </Form.Item>
        <Form.Item label={t('settings.scanProfiles.editor.description')}>
          <Input.TextArea
            value={description}
            autoSize={{ minRows: 1, maxRows: 4 }}
            maxLength={2000}
            onChange={(event) => setDescription(event.target.value)}
            disabled={!!profile && !mayChangeInPlace}
          />
        </Form.Item>
        {!profile && (
          <Form.Item label={t('settings.scanProfiles.editor.scope')}>
            <ProfileScopePicker value={scope} onChange={setScope} />
          </Form.Item>
        )}
      </Form>

      <Flex justify="space-between" align="center" wrap gap={8}>
        <Flex gap={8} align="center">
          <Switch checked={onlyChanged} onChange={setOnlyChanged} id="profile-only-changed" data-testid="profile-only-changed" />
          <label htmlFor="profile-only-changed">{t('settings.scanProfiles.editor.onlyChanged')}</label>
        </Flex>
        <Tag color={changed.length > 0 ? 'gold' : undefined} data-testid="profile-changed-count">
          {t('settings.scanProfiles.editor.changedCount', { count: changed.length })}
        </Tag>
      </Flex>

      <ProfileParameterForm groups={groups} values={current} onChange={setValues} onlyChanged={onlyChanged} />

      {error && <Alert type="error" showIcon title={error} data-testid="profile-save-error" />}

      <Flex gap={8} justify="flex-end" wrap>
        <Button onClick={onClose}>{t('common.cancel')}</Button>
        {profile && (
          <Button onClick={() => setSaveAsOpen(true)} disabled={busy} data-testid="profile-save-as">
            {mayChangeInPlace ? t('settings.scanProfiles.editor.saveAs') : t('settings.scanProfiles.editor.saveCopy')}
          </Button>
        )}
        {(!profile || mayChangeInPlace) && (
          <Button
            type="primary"
            loading={busy}
            disabled={!name.trim() || (!profile && !scopeChoiceComplete(scope))}
            onClick={() => void save()}
            data-testid="profile-save"
          >
            {t('common.save')}
          </Button>
        )}
      </Flex>

      <SaveAsModal
        open={saveAsOpen}
        initialName={profile ? t('settings.scanProfiles.editor.copyName', { name: profile.name }) : name}
        busy={busy}
        onCancel={() => setSaveAsOpen(false)}
        onSave={(choice) => void saveAs(choice)}
      />
    </Flex>
  );
}

function SaveAsModal({
  open,
  initialName,
  busy,
  onCancel,
  onSave,
}: {
  open: boolean;
  initialName: string;
  busy: boolean;
  onCancel: () => void;
  onSave: (choice: { name: string; scope: ProfileScopeChoice }) => void;
}) {
  const { t } = useTranslation();
  return (
    <Modal
      open={open}
      title={t('settings.scanProfiles.editor.saveAsTitle')}
      onCancel={onCancel}
      footer={null}
      destroyOnHidden
    >
      {open && <SaveAsBody initialName={initialName} busy={busy} onCancel={onCancel} onSave={onSave} />}
    </Modal>
  );
}

function SaveAsBody({
  initialName,
  busy,
  onCancel,
  onSave,
}: {
  initialName: string;
  busy: boolean;
  onCancel: () => void;
  onSave: (choice: { name: string; scope: ProfileScopeChoice }) => void;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState(initialName);
  const [scope, setScope] = useState<ProfileScopeChoice>({ scope: 'personal', cavingGroupId: null });
  return (
    <Form layout="vertical" component="div">
      <Form.Item label={t('settings.scanProfiles.editor.name')} required>
        <Input value={name} maxLength={200} onChange={(event) => setName(event.target.value)} data-testid="profile-save-as-name" />
      </Form.Item>
      <Form.Item label={t('settings.scanProfiles.editor.scope')}>
        <ProfileScopePicker value={scope} onChange={setScope} />
      </Form.Item>
      <Flex gap={8} justify="flex-end">
        <Button onClick={onCancel}>{t('common.cancel')}</Button>
        <Button
          type="primary"
          loading={busy}
          disabled={!name.trim() || !scopeChoiceComplete(scope)}
          onClick={() => onSave({ name: name.trim(), scope })}
          data-testid="profile-save-as-confirm"
        >
          {t('common.save')}
        </Button>
      </Flex>
    </Form>
  );
}
