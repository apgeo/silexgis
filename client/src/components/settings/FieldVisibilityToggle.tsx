// SPDX-License-Identifier: AGPL-3.0-or-later
import { GlobalOutlined, LockOutlined, TeamOutlined } from '@ant-design/icons';
import type { ReactNode } from 'react';
import { Button, Dropdown, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FieldVisibility } from '../../api/hooks.ts';

const ICONS: Record<FieldVisibility, ReactNode> = {
  private: <LockOutlined />,
  team: <TeamOutlined />,
  authenticated: <GlobalOutlined />,
};

const VISIBILITY_VALUES: FieldVisibility[] = ['private', 'team', 'authenticated'];

interface Props {
  value?: FieldVisibility;
  onChange?: (value: FieldVisibility) => void;
}

/**
 * Who may see one profile field. Deliberately a small icon that rides inside the field's own
 * label rather than a second column: seven fields each needing an audience would otherwise
 * double the width of the form and bury the fields themselves.
 *
 * Shaped as a value/onChange control so it can sit in a form item and be saved by the same
 * submit as the field it governs — there is no separate save for privacy.
 */
export default function FieldVisibilityToggle({ value = 'private', onChange }: Props) {
  const { t } = useTranslation();

  return (
    <Dropdown
      trigger={['click']}
      menu={{
        selectable: true,
        selectedKeys: [value],
        items: VISIBILITY_VALUES.map((key) => ({
          key,
          icon: ICONS[key],
          label: t(`settings.visibilityValues.${key}`),
        })),
        onClick: ({ key }) => onChange?.(key as FieldVisibility),
      }}
    >
      <Tooltip title={t('settings.visibility.tooltip', { audience: t(`settings.visibilityValues.${value}`) })}>
        <Button
          type="text"
          size="small"
          icon={ICONS[value]}
          aria-label={t('settings.visibility.label')}
          onClick={(e) => e.preventDefault()}
        />
      </Tooltip>
    </Dropdown>
  );
}
