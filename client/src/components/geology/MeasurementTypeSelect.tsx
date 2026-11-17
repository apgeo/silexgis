// SPDX-License-Identifier: AGPL-3.0-or-later
import { Select, Space, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { MEASUREMENT_TYPES, measurementTypeLabel } from './structural.ts';

/** Which kind of structural reading a stereonet or a strike comparison reads; '' for every kind. */
export default function MeasurementTypeSelect({
  value,
  onChange,
  testId,
}: {
  value: string;
  onChange: (value: string) => void;
  testId: string;
}) {
  const { t } = useTranslation();
  return (
    <Space size={8}>
      <Typography.Text>{t('structural.stereonet.measurementType')}</Typography.Text>
      <Select
        data-testid={testId}
        aria-label={t('structural.stereonet.measurementType')}
        size="small"
        style={{ minWidth: 160 }}
        value={value}
        onChange={onChange}
        options={[
          { value: '', label: t('structural.stereonet.allTypes') },
          ...MEASUREMENT_TYPES.map((type) => ({ value: type, label: measurementTypeLabel(t, type) })),
        ]}
      />
    </Space>
  );
}
