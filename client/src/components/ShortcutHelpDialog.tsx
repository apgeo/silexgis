// SPDX-License-Identifier: AGPL-3.0-or-later
import { Fragment } from 'react';
import { Button, Flex, Modal, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  FOCUSED_SHORTCUTS,
  shortcutRows,
  useShortcutRegistry,
  type ShortcutRow,
} from '../hooks/shortcutRegistry.ts';
import './ShortcutHelpDialog.css';

interface ShortcutHelpDialogProps {
  open: boolean;
  onClose: () => void;
}

/**
 * Every keyboard shortcut the screen behind it answers to: the document-level ones exactly as
 * they are bound at this moment, then the keys that act on a focused element.
 *
 * Read from the registry rather than written out here, so a binding added to a screen is in this
 * list from the moment it works; a hand-kept list is the one that goes out of date.
 */
export default function ShortcutHelpDialog({ open, onClose }: ShortcutHelpDialogProps) {
  const { t } = useTranslation();
  const bound = useShortcutRegistry((state) => state.bound);
  const screenRows = shortcutRows(Object.values(bound).flat());

  const table = (caption: string, rows: ShortcutRow[], testId: string) => (
    <table className="shortcut-help-table" data-testid={testId}>
      <caption>
        <Typography.Text strong>{caption}</Typography.Text>
      </caption>
      <thead>
        <tr>
          <th scope="col">{t('shortcuts.keys')}</th>
          <th scope="col">{t('shortcuts.does')}</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.labelKey}>
            <td>
              <Flex gap={4} wrap align="center">
                {row.combinations.map((caps, index) => (
                  <Fragment key={caps.join('+')}>
                    {index > 0 && <Typography.Text type="secondary">/</Typography.Text>}
                    {caps.map((cap, capIndex) => (
                      <Fragment key={cap}>
                        {capIndex > 0 && <span aria-hidden>+</span>}
                        <Typography.Text keyboard>{cap}</Typography.Text>
                      </Fragment>
                    ))}
                  </Fragment>
                ))}
              </Flex>
            </td>
            <td>{t(row.labelKey)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );

  return (
    <Modal
      open={open}
      title={t('shortcuts.title')}
      onCancel={onClose}
      footer={<Button onClick={onClose}>{t('common.close')}</Button>}
    >
      <Flex vertical gap={16}>
        {screenRows.length > 0 && table(t('shortcuts.context.screen'), screenRows, 'shortcut-help-screen')}
        {FOCUSED_SHORTCUTS.map((group) => (
          <Fragment key={group.contextKey}>
            {table(t(group.contextKey), shortcutRows(group.shortcuts), `shortcut-help-${group.contextKey.split('.').pop()}`)}
          </Fragment>
        ))}
      </Flex>
    </Modal>
  );
}
