// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, App, Flex, Select, Typography } from 'antd';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import { api } from '../../api/client.ts';
import { queryKeys, type AdminSettings } from '../../api/hooks.ts';
import { installationPresetOf, WORKSPACE_PRESETS } from '../../components/map/workspace/workspacePresets.ts';
import { readPanelDefaults, withStartingPreset } from './workspaceDefault.ts';

const BUILT_IN = 'built-in';

/**
 * Which workspace preset a new account starts with on the map.
 *
 * Written into the installation's starting arrangement — the document the panels' default
 * sections already live in, read by every client through the starting-arrangement route — so no
 * new setting and no new route: a default, which a member's own arrangement always outranks.
 */
export default function WorkspaceDefaultForm({
  settings,
  onSaved,
}: {
  settings: AdminSettings;
  onSaved: (next: AdminSettings) => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const defaults = readPanelDefaults(settings.interface.panelDefaults);
  const [choice, setChoice] = useState<string>(installationPresetOf(defaults)?.id ?? BUILT_IN);
  const [saving, setSaving] = useState(false);

  if (defaults === null) {
    return <Alert type="warning" showIcon title={t('workspace.admin.unreadable')} style={{ maxWidth: 640 }} />;
  }

  const save = async () => {
    setSaving(true);
    try {
      const { data, error } = await api.PUT('/api/v1/admin/settings/interface', {
        body: { panelDefaults: withStartingPreset(defaults, choice === BUILT_IN ? null : choice) },
      });
      if (error !== undefined || !data) {
        message.error(t('common.saveFailed'));
        return;
      }
      onSaved(data);
      // Every page reads the starting arrangement through its own query, cached for minutes.
      await queryClient.invalidateQueries({ queryKey: queryKeys.uiDefaults });
      message.success(t('workspace.admin.saved'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Flex vertical gap={16} style={{ maxWidth: 640 }}>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('workspace.admin.intro')}
      </Typography.Paragraph>
      <Flex vertical gap={4}>
        <Typography.Text strong id="workspace-default-label">
          {t('workspace.admin.preset')}
        </Typography.Text>
        <Select
          aria-labelledby="workspace-default-label"
          value={choice}
          onChange={setChoice}
          style={{ maxWidth: 360 }}
          data-testid="workspace-default-preset"
          options={[
            { value: BUILT_IN, label: t('workspace.admin.builtIn') },
            // A preset that opens a window cannot be a start: a browser opens a window only when
            // somebody presses something, and a first visit to the map presses nothing.
            ...WORKSPACE_PRESETS.filter((preset) => preset.id !== 'map' && preset.snapshot.scene3d !== 'window').map(
              (preset) => ({ value: preset.id, label: t(`workspace.preset.${preset.id}`) }),
            ),
          ]}
        />
      </Flex>
      <div>
        <Button type="primary" loading={saving} onClick={() => void save()} data-testid="workspace-default-save">
          {t('common.save')}
        </Button>
      </div>
    </Flex>
  );
}
