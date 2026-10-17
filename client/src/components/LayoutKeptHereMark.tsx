// SPDX-License-Identifier: AGPL-3.0-or-later
import { Tag, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';

/**
 * Says, beside a saved arrangement's name, that the arrangement is kept in this browser only —
 * the account's stored preferences were full when it was written — and why, so nobody looks for
 * it in vain on another computer. Draws nothing for an arrangement the account holds.
 */
export default function LayoutKeptHereMark({ layoutId }: { layoutId: string }) {
  const { t } = useTranslation();
  const keptHere = useUiPrefsStore((state) => state.layoutsKeptHere.includes(layoutId));
  if (!keptHere) {
    return null;
  }
  return (
    <Tooltip title={t('panel.layoutKeptHereWhy')}>
      <Tag variant="filled" data-testid="layout-kept-here">
        {t('panel.layoutKeptHere')}
      </Tag>
    </Tooltip>
  );
}
