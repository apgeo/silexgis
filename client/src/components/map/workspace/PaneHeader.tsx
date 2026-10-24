// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import {
  CheckOutlined,
  CloseOutlined,
  DragOutlined,
  FullscreenExitOutlined,
  FullscreenOutlined,
} from '@ant-design/icons';
import { Dropdown, Flex, Tooltip, Typography } from 'antd';
import type { MenuProps } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../../Button.tsx';
import type { PanePlace } from '../../../stores/workspacePrefs.ts';

/** Where a pane can be sent from its own menu: one of the places in the window, or a window. */
export type PaneDestination = PanePlace | 'window';

interface Props {
  /** The pane's name, which the strip shows and every control's label says. */
  title: ReactNode;
  /** The name as plain words, for the controls' labels. */
  name: string;
  /** Prefix for the controls' test ids. */
  testId: string;
  place: PanePlace;
  onMove: (destination: PaneDestination) => void;
  /** Whether "in its own window" is on offer; a pane with nothing in it has nothing to send. */
  mayPopOut?: boolean;
  maximised: boolean;
  onToggleMaximised: () => void;
  onClose: () => void;
  /** Buttons of the pane's own, drawn before the workspace's. */
  extra?: ReactNode;
}

/**
 * The strip over a pane that can be moved: its name, and where to put it — left or right of the
 * map, under it, or in a window of its own — with the controls to fill the workspace with it and
 * to shut it.
 *
 * A menu rather than dragging, deliberately: four named places are reachable by keyboard and read
 * out by a screen reader as what they are, which a drop target is not, and a drag is what would
 * tempt a layout library into moving the drawing surface it carries.
 */
export default function PaneHeader({
  title,
  name,
  testId,
  place,
  onMove,
  mayPopOut = true,
  maximised,
  onToggleMaximised,
  onClose,
  extra,
}: Props) {
  const { t } = useTranslation();
  const destinations: { key: PaneDestination; label: string }[] = [
    { key: 'left', label: t('workspace.place.left') },
    { key: 'right', label: t('workspace.place.right') },
    { key: 'bottom', label: t('workspace.place.bottom') },
    { key: 'window', label: t('workspace.place.window') },
  ];
  const items: MenuProps['items'] = destinations.map(({ key, label }) => ({
    key,
    label,
    // The place it is in now is marked and not offered: moving a pane where it already is would be
    // a press that does nothing.
    icon: key === place ? <CheckOutlined /> : <span className="workspace-menu-icon-gap" />,
    disabled: key === place || (key === 'window' && !mayPopOut),
    'data-testid': `${testId}-move-${key}`,
  }));
  const maximiseLabel = maximised ? t('workspace.restore', { name }) : t('workspace.maximise', { name });

  return (
    <Flex align="center" justify="space-between" className="workspace-pane-header" gap={8}>
      <Typography.Text ellipsis strong className="workspace-pane-title">
        {title}
      </Typography.Text>
      <Flex gap={4} align="center">
        {extra}
        <Dropdown
          trigger={['click']}
          menu={{ items, onClick: ({ key }) => onMove(key as PaneDestination) }}
        >
          <Tooltip title={t('workspace.move', { name })}>
            <Button
              size="small"
              type="text"
              icon={<DragOutlined />}
              aria-label={t('workspace.move', { name })}
              aria-haspopup="menu"
              data-testid={`${testId}-move`}
            />
          </Tooltip>
        </Dropdown>
        <Tooltip title={`${maximiseLabel} (M)`}>
          <Button
            size="small"
            type="text"
            icon={maximised ? <FullscreenExitOutlined /> : <FullscreenOutlined />}
            aria-label={maximiseLabel}
            aria-pressed={maximised}
            aria-keyshortcuts="M"
            onClick={onToggleMaximised}
            data-testid={`${testId}-maximise`}
          />
        </Tooltip>
        <Tooltip title={t('workspace.close', { name })}>
          <Button
            size="small"
            type="text"
            icon={<CloseOutlined />}
            aria-label={t('workspace.close', { name })}
            onClick={onClose}
            data-testid={`${testId}-close`}
          />
        </Tooltip>
      </Flex>
    </Flex>
  );
}
