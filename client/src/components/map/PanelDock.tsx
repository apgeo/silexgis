// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  ArrowLeftOutlined,
  ArrowRightOutlined,
  ColumnWidthOutlined,
  DeleteOutlined,
  LayoutOutlined,
  PushpinFilled,
  PushpinOutlined,
  SaveOutlined,
} from '@ant-design/icons';
import { App, Button, Dropdown, Flex, Input, Segmented, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import type { PanelScope } from '../../stores/panelPrefs.ts';
import type { DensityPref } from '../../stores/uiPrefsStore.ts';

/**
 * The panel's own chrome: where you have been, how the panel sits, and the named arrangements.
 *
 * Icon buttons throughout. This strip sits above the narrowest column in the application, and
 * three labelled buttons would be a second row before anything useful is on screen.
 */
export default function PanelDock({ scope }: { scope: PanelScope }) {
  const { t } = useTranslation();
  const { modal } = App.useApp();

  const goBack = useWorkspaceStore((s) => s.goBackSelection);
  const goForward = useWorkspaceStore((s) => s.goForwardSelection);
  const cursor = useWorkspaceStore((s) => s.selectionCursor);
  const historyLength = useWorkspaceStore((s) => s.selectionHistory.length);

  const prefs = useUiPrefsStore((s) => s.panels[scope]);
  const setPanelPrefs = useUiPrefsStore((s) => s.setPanelPrefs);
  const appearanceDensity = useUiPrefsStore((s) => s.appearance.density);
  const mapChromeHidden = useUiPrefsStore((s) => s.mapChromeHidden);
  const layouts = useUiPrefsStore((s) => s.layouts);
  const saveLayout = useUiPrefsStore((s) => s.saveLayout);
  const applyLayout = useUiPrefsStore((s) => s.applyLayout);
  const deleteLayout = useUiPrefsStore((s) => s.deleteLayout);

  const [name, setName] = useState('');
  const pinned = prefs?.pinned ?? true;
  const density: DensityPref = prefs?.density ?? appearanceDensity;

  const onSave = () => {
    const trimmed = name.trim();
    if (!trimmed) {
      return;
    }
    saveLayout(trimmed, mapChromeHidden);
    setName('');
  };

  return (
    <Flex align="center" gap={2} style={{ padding: '2px 4px' }} data-testid="panel-dock">
      <Tooltip title={t('panel.back')}>
        <Button
          size="small"
          type="text"
          icon={<ArrowLeftOutlined />}
          aria-label={t('panel.back')}
          disabled={cursor <= 0}
          onClick={goBack}
        />
      </Tooltip>
      <Tooltip title={t('panel.forward')}>
        <Button
          size="small"
          type="text"
          icon={<ArrowRightOutlined />}
          aria-label={t('panel.forward')}
          disabled={cursor >= historyLength - 1}
          onClick={goForward}
        />
      </Tooltip>

      <span style={{ flex: 1 }} />

      <Tooltip title={pinned ? t('panel.unpin') : t('panel.pin')}>
        <Button
          size="small"
          type="text"
          icon={pinned ? <PushpinFilled /> : <PushpinOutlined />}
          aria-label={pinned ? t('panel.unpin') : t('panel.pin')}
          onClick={() => setPanelPrefs(scope, { pinned: !pinned })}
        />
      </Tooltip>

      <Dropdown
        trigger={['click']}
        menu={{
          items: [
            {
              key: 'density',
              label: (
                <Segmented<DensityPref>
                  size="small"
                  value={density}
                  onChange={(value) => setPanelPrefs(scope, { density: value })}
                  options={[
                    {
                      value: 'comfortable',
                      label: t('settings.accessibility.densityValues.comfortable'),
                    },
                    { value: 'compact', label: t('settings.accessibility.densityValues.compact') },
                  ]}
                />
              ),
            },
          ],
        }}
      >
        <Tooltip title={t('panel.density')}>
          <Button
            size="small"
            type="text"
            icon={<ColumnWidthOutlined />}
            aria-label={t('panel.density')}
          />
        </Tooltip>
      </Dropdown>

      {/* Layouts are hidden behind this menu deliberately: they are set up once and then used,
          and a list of saved arrangements taking permanent space would cost more than it earns. */}
      <Dropdown
        trigger={['click']}
        menu={{
          items: [
            ...layouts.map((layout) => ({
              key: layout.id,
              label: (
                <Flex align="center" gap={8} justify="space-between" style={{ minWidth: 180 }}>
                  <span>{layout.name}</span>
                  <Button
                    size="small"
                    type="text"
                    danger
                    icon={<DeleteOutlined />}
                    aria-label={t('panel.deleteLayout', { name: layout.name })}
                    onClick={(event) => {
                      event.stopPropagation();
                      modal.confirm({
                        title: t('panel.deleteLayoutConfirm', { name: layout.name }),
                        okButtonProps: { danger: true },
                        onOk: () => deleteLayout(layout.id),
                      });
                    }}
                  />
                </Flex>
              ),
              onClick: () => applyLayout(layout.id),
            })),
            ...(layouts.length > 0 ? [{ type: 'divider' as const, key: 'sep' }] : []),
            {
              key: 'save',
              label: (
                <Flex gap={4} onClick={(event) => event.stopPropagation()}>
                  <Input
                    size="small"
                    placeholder={t('panel.layoutName')}
                    value={name}
                    onChange={(event) => setName(event.target.value)}
                    onPressEnter={onSave}
                    style={{ width: 150 }}
                  />
                  <Button size="small" icon={<SaveOutlined />} onClick={onSave} disabled={!name.trim()} />
                </Flex>
              ),
            },
          ],
        }}
      >
        <Tooltip title={t('panel.layouts')}>
          <Button
            size="small"
            type="text"
            icon={<LayoutOutlined />}
            aria-label={t('panel.layouts')}
            data-testid="panel-layouts"
          />
        </Tooltip>
      </Dropdown>
    </Flex>
  );
}
