// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Flex, Input, Select, Table, Tag, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useTripTypes, type TripImportOptions, type TripImportTermMatch } from '../../api/hooks.ts';

interface Props {
  types: readonly TripImportTermMatch[];
  options: TripImportOptions;
  onChange: (next: TripImportOptions) => void;
}

/**
 * Every distinct value of the sheet's type column, with what it was taken for and what
 * confirming would do about it.
 *
 * This is one line per word rather than one per row, and that is the whole point: a term written
 * on four hundred trips is one decision, and the row table — which answers "what does this trip
 * become" — cannot show it as one. The question here is the other one, "what does this sheet do
 * to the installation", and it is the question somebody is really being asked before they press
 * the button.
 *
 * It matters more for types than for anything else this importer touches, because the trip-type
 * vocabulary is installation-wide, append-only, and shown in every trip form — and taking the
 * import back does not remove a word it added. A sheet that writes "explorare", "Explorare ",
 * "expl." and "TOPO" would otherwise be reviewed as the single figure "New trip types: 4" and
 * confirmed, leaving four spellings of two ideas in a list nobody can tidy afterwards.
 *
 * So an unmatched value is offered two ways out, both of which the server already honours and
 * neither of which is a guess: rename the word that would be created, or point the value at a
 * type that already exists. Pointing is not held to answering to the name — aiming "topo" at the
 * surveying type is exactly what the control is for.
 */
export default function TripImportTypeTable({ types, options, onChange }: Props) {
  const { t } = useTranslation();
  const vocabulary = useTripTypes();

  const choices = options.tripTypeChoices ?? {};
  const names = options.tripTypeNames ?? {};

  const point = (source: string, id: number | undefined) => {
    const next = { ...choices };
    if (id === undefined) {
      delete next[source];
    } else {
      next[source] = id;
    }
    onChange({ ...options, tripTypeChoices: next });
  };

  const rename = (source: string, name: string) => {
    const next = { ...names };
    // An empty box is not a choice to create a word with no name; it is the reviewer taking the
    // rename back, so the sheet's own spelling stands again.
    if (name.trim() === '') {
      delete next[source];
    } else {
      next[source] = name;
    }
    onChange({ ...options, tripTypeNames: next });
  };

  const columns: ColumnsType<TripImportTermMatch> = [
    {
      title: t('tripImport.typeColumns.value'),
      key: 'source',
      width: 180,
      render: (_, row) => <Typography.Text code>{row.source}</Typography.Text>,
    },
    {
      title: t('tripImport.typeColumns.matched'),
      key: 'matched',
      width: 200,
      render: (_, row) => {
        if (row.state === 'matched') {
          return <Tag color="green">{row.name ?? row.source}</Tag>;
        }
        if (row.state === 'ambiguous') {
          return <Tag color="gold">{t('tripImport.typeAmbiguous')}</Tag>;
        }
        return <Typography.Text type="secondary">{t('tripImport.typeNoMatch')}</Typography.Text>;
      },
    },
    {
      title: t('tripImport.typeColumns.outcome'),
      key: 'outcome',
      render: (_, row) => {
        // Said in terms of what happens to the installation, not in terms of the flag that
        // decided it: "a word is added" and "nothing is added" are the two outcomes a reviewer
        // is agreeing to, and the second is worth stating rather than leaving blank.
        if (row.state === 'matched') {
          return (
            <Typography.Text type="secondary">
              {t('tripImport.typeOutcomeLinked', { name: row.name ?? row.source })}
            </Typography.Text>
          );
        }
        if (row.willCreate) {
          return (
            <Flex gap={8} align="center" wrap>
              <Tag color="blue">{t('tripImport.typeOutcomeCreated')}</Tag>
              <Input
                size="small"
                style={{ maxWidth: 200 }}
                placeholder={row.source}
                value={names[row.source] ?? ''}
                onChange={(e) => rename(row.source, e.target.value)}
                data-testid={`trip-import-type-name-${row.source}`}
              />
            </Flex>
          );
        }
        return (
          <Typography.Text type="secondary">{t('tripImport.typeOutcomeNothing')}</Typography.Text>
        );
      },
    },
    {
      title: t('tripImport.typeColumns.instead'),
      key: 'instead',
      width: 240,
      render: (_, row) =>
        row.state === 'matched' && choices[row.source] === undefined ? null : (
          <Select
            allowClear
            showSearch
            size="small"
            optionFilterProp="label"
            style={{ minWidth: 200 }}
            loading={vocabulary.isLoading}
            placeholder={t('tripImport.typeUseExisting')}
            value={choices[row.source] ?? undefined}
            onChange={(value?: number) => point(row.source, value)}
            data-testid={`trip-import-type-choice-${row.source}`}
            options={(vocabulary.data ?? []).map((type) => ({ value: type.id, label: type.name }))}
          />
        ),
    },
  ];

  return (
    <Card size="small" title={t('tripImport.typesTitle')} data-testid="trip-import-types">
      <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
        {t('tripImport.typesHint')}
      </Typography.Paragraph>
      <Table<TripImportTermMatch>
        rowKey="source"
        size="small"
        scroll={{ x: 'max-content' }}
        columns={columns}
        dataSource={[...types]}
        pagination={types.length > 10 ? { pageSize: 10 } : false}
        locale={{ emptyText: t('tripImport.typesNone') }}
      />
    </Card>
  );
}
