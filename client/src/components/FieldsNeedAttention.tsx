// SPDX-License-Identifier: AGPL-3.0-or-later
import { Typography } from 'antd';
import { useTranslation } from 'react-i18next';

/** The line beside Save: how many fields need attention, or nothing at all. */
export default function FieldsNeedAttention({ count }: { count: number }) {
  const { t } = useTranslation();
  if (count === 0) {
    return null;
  }
  return (
    <Typography.Text type="danger" role="status" data-testid="form-attention">
      {t('common.fieldsNeedAttention', { count })}
    </Typography.Text>
  );
}
