// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Select, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { ImportLithologyValue } from '../../api/hooks/imports.ts';
import { LITHOLOGY_CODES, lithologyLabel } from '../geology/lithology.ts';

interface Props {
  /** Each distinct value of the outlines' rock column, as the preview answered it. */
  values: readonly ImportLithologyValue[];
  /** The reviewer's answers so far: source value to class code. */
  mapping: Readonly<Record<string, string>>;
  onChange: (next: Record<string, string>) => void;
}

/**
 * The mapping step of a geology import: every rock name the file's outlines carry, how many
 * outlines carry it, and which class it becomes. A name the server recognised on its own is
 * already answered and can still be overridden; a name nothing matched is listed first and is
 * created unclassified unless the reviewer picks a class for it here.
 */
export default function LithologyMappingCard({ values, mapping, onChange }: Props) {
  const { t } = useTranslation();
  if (values.length === 0) return null;

  const unmatched = values.filter((v) => v.match === 'unmatched').length;
  const matchLabel: Record<ImportLithologyValue['match'], string> = {
    mapped: t('vectorImport.lithologyStep.match.mapped'),
    recognised: t('vectorImport.lithologyStep.match.recognised'),
    unmatched: t('vectorImport.lithologyStep.match.unmatched'),
  };

  const choose = (value: string, code: string | undefined) => {
    const next = { ...mapping };
    if (code) next[value] = code;
    else delete next[value];
    onChange(next);
  };

  return (
    <Card size="small" title={t('vectorImport.lithologyStep.title')} data-testid="import-lithology-mapping">
      <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
        {unmatched > 0
          ? t('vectorImport.lithologyStep.unmatched', { count: unmatched })
          : t('vectorImport.lithologyStep.allMatched')}
      </Typography.Paragraph>
      <Table<ImportLithologyValue>
        size="small"
        rowKey="value"
        pagination={false}
        dataSource={[...values]}
        columns={[
          {
            title: t('vectorImport.lithologyStep.sourceValue'),
            dataIndex: 'value',
            render: (value: string) => <Typography.Text code>{value}</Typography.Text>,
          },
          { title: t('vectorImport.lithologyStep.count'), dataIndex: 'count', width: 90, align: 'right' },
          {
            title: t('vectorImport.lithologyStep.becomes'),
            key: 'code',
            render: (_: unknown, row) => (
              <Select
                allowClear
                size="small"
                style={{ minWidth: 200 }}
                placeholder={t('geology.unclassified')}
                aria-label={t('vectorImport.lithologyStep.becomesFor', { value: row.value })}
                value={mapping[row.value] ?? row.code ?? undefined}
                onChange={(code?: string) => choose(row.value, code)}
                options={LITHOLOGY_CODES.map((code) => ({ value: code, label: lithologyLabel(t, code) }))}
              />
            ),
          },
          {
            title: t('vectorImport.lithologyStep.how'),
            key: 'match',
            width: 140,
            render: (_: unknown, row) => (
              <Tag color={row.match === 'unmatched' ? 'warning' : row.match === 'mapped' ? 'blue' : 'default'}>
                {matchLabel[row.match]}
              </Tag>
            ),
          },
        ]}
      />
    </Card>
  );
}
