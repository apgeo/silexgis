// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, App, Flex, InputNumber, Modal, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { useRunCavePotentialModel, type CavePotentialModelInfo } from '../../api/hooks/cavePotential.ts';
import { useFeatures } from '../../api/hooks/features.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

/** The cell sizes the server accepts, in metres. Stated here only to bound the control; the server refuses outside them. */
const MIN_CELL_METERS = 5;
const MAX_CELL_METERS = 1000;

interface Props {
  model: CavePotentialModelInfo;
  onClose: () => void;
}

/**
 * Asking for a model to be run: over which area, and at what cell size.
 *
 * The area is found by its name among the areas this reader may read. Whether it can be used — an
 * outline the reader may place — is the server's to say, and a refusal is shown in its own words:
 * an area too large for the cell size chosen is told so with the limit, which no wording held here
 * could carry.
 */
export default function CavePotentialRunForm({ model, onClose }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const run = useRunCavePotentialModel();
  const [typed, setTyped] = useState('');
  const search = useDebouncedValue(typed);
  const [area, setArea] = useState<{ value: string; label: string } | null>(null);
  const [cellMeters, setCellMeters] = useState<number | null>(50);
  const [refusal, setRefusal] = useState<string | null>(null);

  // Asked only once something is typed: an unnarrowed first page of the whole register is mostly
  // not areas at all.
  const { data, isFetching } = useFeatures({ search: search.trim(), pageSize: 50 }, search.trim().length > 0);
  const options = (data?.items ?? [])
    .filter((feature) => feature.geometry?.type === 'Polygon' || feature.geometry?.type === 'MultiPolygon')
    .map((feature) => ({ value: feature.id, label: feature.name ?? feature.id }));

  const start = async () => {
    if (area === null || cellMeters === null) {
      return;
    }
    setRefusal(null);
    try {
      await run.mutateAsync({ modelId: model.id, areaFeatureId: area.value, cellMeters });
      message.success(t('cavePotential.run.queued'));
      onClose();
    } catch (error) {
      setRefusal(error instanceof ApiError && error.detail ? error.detail : t('cavePotential.run.failed'));
    }
  };

  return (
    <Modal
      open
      title={t('cavePotential.run.title', { name: model.name })}
      onCancel={onClose}
      okText={t('cavePotential.run.start')}
      cancelText={t('common.cancel')}
      onOk={() => void start()}
      okButtonProps={{
        disabled: area === null || cellMeters === null,
        loading: run.isPending,
        'data-testid': 'cave-potential-run-start',
      }}
      destroyOnHidden
    >
      <Flex vertical gap={12} data-testid="cave-potential-run-form">
        <label>
          <Typography.Text type="secondary">{t('cavePotential.run.area')}</Typography.Text>
          <Select
            aria-label={t('cavePotential.run.area')}
            data-testid="cave-potential-run-area"
            style={{ width: '100%', display: 'block' }}
            showSearch
            labelInValue
            filterOption={false}
            placeholder={t('cavePotential.run.areaPlaceholder')}
            notFoundContent={search.trim() === '' ? t('cavePotential.run.areaPlaceholder') : t('cavePotential.run.noArea')}
            loading={isFetching}
            value={area}
            onSearch={setTyped}
            onChange={(next: { value: string; label: string } | null) => setArea(next)}
            options={options}
          />
        </label>
        <label>
          <Typography.Text type="secondary">{t('cavePotential.run.cell')}</Typography.Text>
          <InputNumber
            aria-label={t('cavePotential.run.cell')}
            data-testid="cave-potential-run-cell"
            min={MIN_CELL_METERS}
            max={MAX_CELL_METERS}
            value={cellMeters}
            onChange={setCellMeters}
            style={{ width: 160, display: 'block' }}
          />
        </label>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('cavePotential.run.cellHint', { min: MIN_CELL_METERS, max: MAX_CELL_METERS })}
        </Typography.Text>
        {model.usesPositions && (
          <Alert type="warning" showIcon title={t('cavePotential.run.positionsNotice')} />
        )}
        {refusal && <Alert type="error" showIcon data-testid="cave-potential-run-refusal" title={refusal} />}
      </Flex>
    </Modal>
  );
}
