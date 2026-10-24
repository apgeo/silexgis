// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { AppstoreOutlined, CheckOutlined } from '@ant-design/icons';
import { Dropdown, Form, Input, Modal, Tooltip, Typography } from 'antd';
import type { MenuProps } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../../Button.tsx';
import { useUiPrefsStore } from '../../../stores/uiPrefsStore.ts';
import { useWorkspacePanesStore } from '../../../stores/workspacePanesStore.ts';
import { DEFAULT_ARRANGEMENT } from '../../../stores/workspacePrefs.ts';
import { applySavedLayout, applyWorkspaceSnapshot, saveCurrentLayout } from './workspaceActions.ts';
import { snapshotMatches, WORKSPACE_PRESETS, type WorkspaceSnapshot } from './workspacePresets.ts';

/** Room kept on the left of each entry for the mark of the one in force, so the names line up. */
const mark = (on: boolean) => (on ? <CheckOutlined /> : <span className="workspace-menu-icon-gap" />);

/**
 * The map workspace's arrangements, one press each, beside the map's other window controls: the
 * presets, the layouts this person saved, saving the current one, and the two switches that are
 * not a pane's own — the table under the map and which side each dock is on.
 *
 * Presets are menu entries rather than a strip of buttons: the corner they live in already holds a
 * row of buttons, and a menu is reached and read by keyboard and screen reader exactly like one.
 */
export default function WorkspaceMenu() {
  const { t } = useTranslation();
  const arrangement = useUiPrefsStore((s) => s.workspace) ?? DEFAULT_ARRANGEMENT;
  const docks = useUiPrefsStore((s) => s.docks);
  const layouts = useUiPrefsStore((s) => s.layouts);
  const setWorkspace = useUiPrefsStore((s) => s.setWorkspace);
  const scene3dOpen = useWorkspacePanesStore((s) => s.scene3dOpen);
  const caveViewShown = useWorkspacePanesStore((s) => s.caveViewPaneOpen || s.caveViewModelId !== null);
  const maximised = useWorkspacePanesStore((s) => s.maximised);
  const setMaximised = useWorkspacePanesStore((s) => s.setMaximised);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<{ name: string }>();

  const current: WorkspaceSnapshot = {
    arrangement,
    docks,
    scene3d: scene3dOpen ? 'open' : 'closed',
    caveViewPane: caveViewShown,
  };

  const items: MenuProps['items'] = [
    {
      type: 'group',
      key: 'presets',
      label: t('workspace.presetsTitle'),
      children: WORKSPACE_PRESETS.map((preset) => ({
        key: `preset:${preset.id}`,
        icon: mark(snapshotMatches(current, preset.snapshot)),
        label: (
          <span className="workspace-menu-entry">
            <span>{t(`workspace.preset.${preset.id}`)}</span>
            <Typography.Text type="secondary" className="workspace-menu-hint">
              {t(`workspace.presetHint.${preset.id}`)}
            </Typography.Text>
          </span>
        ),
      })),
    },
    ...(layouts.length > 0
      ? [
          {
            type: 'group' as const,
            key: 'layouts',
            label: t('workspace.savedTitle'),
            children: layouts.map((layout) => ({
              key: `layout:${layout.id}`,
              icon: mark(false),
              label: layout.name,
            })),
          },
        ]
      : []),
    { key: 'save', icon: mark(false), label: t('workspace.saveCurrent') },
    { type: 'divider', key: 'divider' },
    { key: 'table', icon: mark(arrangement.table), label: t('workspace.tableToggle') },
    { key: 'swap', icon: mark(arrangement.docksSwapped), label: t('workspace.swapDocks') },
    { key: 'maximise', icon: mark(maximised === 'map'), label: `${t('workspace.maximiseMap')} (M)` },
  ];

  const onClick: MenuProps['onClick'] = ({ key }) => {
    if (key.startsWith('preset:')) {
      const preset = WORKSPACE_PRESETS.find((candidate) => `preset:${candidate.id}` === key);
      if (preset) {
        applyWorkspaceSnapshot(preset.snapshot, true);
      }
    } else if (key.startsWith('layout:')) {
      applySavedLayout(key.slice('layout:'.length));
    } else if (key === 'save') {
      setSaving(true);
    } else if (key === 'table') {
      setWorkspace({ ...arrangement, table: !arrangement.table });
    } else if (key === 'swap') {
      setWorkspace({ ...arrangement, docksSwapped: !arrangement.docksSwapped });
    } else if (key === 'maximise') {
      setMaximised(maximised === 'map' ? null : 'map');
    }
  };

  const save = async () => {
    const { name } = await form.validateFields();
    saveCurrentLayout(name.trim());
    setSaving(false);
    form.resetFields();
  };

  return (
    <>
      <Dropdown trigger={['click']} menu={{ items, onClick }} rootClassName="workspace-menu">
        <Tooltip title={t('workspace.menu')}>
          <Button
            size="small"
            icon={<AppstoreOutlined />}
            aria-label={t('workspace.menu')}
            aria-haspopup="menu"
            data-testid="workspace-menu"
          />
        </Tooltip>
      </Dropdown>
      <Modal
        open={saving}
        title={t('workspace.saveTitle')}
        okText={t('common.save')}
        onOk={() => void save()}
        onCancel={() => setSaving(false)}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">{t('workspace.saveHint')}</Typography.Paragraph>
        <Form form={form} layout="vertical" preserve={false} onFinish={() => void save()}>
          <Form.Item
            name="name"
            label={t('panel.layoutName')}
            rules={[{ required: true, whitespace: true, max: 80 }]}
          >
            <Input autoFocus data-testid="workspace-save-name" />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
