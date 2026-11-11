// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert } from 'antd';
import { useTranslation } from 'react-i18next';

/**
 * Said wherever a map sheet drawn from a document is, or is about to be, linked to a cave: the
 * document stays a file of its owner's, and whoever it is shared with can read the sheet — and
 * so where the cave is — from it, whatever the cave's own protection says.
 */
export default function CaveLinkWarning() {
  const { t } = useTranslation();
  return <Alert type="warning" showIcon title={t('geodata.caveLinkWarning')} data-testid="sheet-cave-warning" />;
}
