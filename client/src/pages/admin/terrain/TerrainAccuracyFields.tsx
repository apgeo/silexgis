// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Form, Input, InputNumber, Select } from 'antd';
import { useTranslation } from 'react-i18next';

/**
 * The four fields that state how accurate an elevation is, shared by the form that starts a build
 * and the editor that corrects one afterwards so the two cannot drift apart in wording or limits.
 */
export default function TerrainAccuracyFields() {
  const { t } = useTranslation();
  return (
    <Flex gap={16} wrap>
      <Form.Item
        name="resolutionM"
        label={t('terrain.accuracy.resolution')}
        extra={t('terrain.accuracy.resolutionHint')}
        style={{ flex: '1 1 200px' }}
      >
        <InputNumber min={0.01} max={100000} step={1} style={{ width: '100%' }} data-testid="terrain-accuracy-resolution" />
      </Form.Item>
      <Form.Item
        name="accuracyM"
        label={t('terrain.accuracy.error')}
        extra={t('terrain.accuracy.errorHint')}
        style={{ flex: '1 1 200px' }}
      >
        <InputNumber min={0.01} max={10000} step={0.1} style={{ width: '100%' }} data-testid="terrain-accuracy-error" />
      </Form.Item>
      <Form.Item
        name="accuracyStatistic"
        label={t('terrain.accuracy.statistic')}
        style={{ flex: '1 1 140px' }}
      >
        <Select
          options={[
            { value: 'RMSE', label: t('terrain.accuracy.statistics.RMSE') },
            { value: 'LE90', label: t('terrain.accuracy.statistics.LE90') },
            { value: 'LE95', label: t('terrain.accuracy.statistics.LE95') },
          ]}
        />
      </Form.Item>
      <Form.Item
        name="accuracySource"
        label={t('terrain.accuracy.source')}
        extra={t('terrain.accuracy.sourceHint')}
        style={{ flex: '1 1 100%' }}
      >
        <Input maxLength={500} data-testid="terrain-accuracy-source" />
      </Form.Item>
    </Flex>
  );
}
