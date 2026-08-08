// SPDX-License-Identifier: AGPL-3.0-or-later
import { Button, Checkbox, Flex, Select, Table, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCaveTypes,
  useEntranceTypes,
  useFeatureTypes,
  type ImportCandidate,
  type ImportDecision,
  type ImportOptions,
  type ImportTargetKind,
} from '../../api/hooks.ts';

interface Props {
  items: ImportCandidate[];
  /** The import's own altitude policy, which a row may answer against. */
  elevationPolicy: ImportOptions['elevation'];
  decisions: Record<string, ImportDecision>;
  selected: ReadonlySet<number>;
  onSelectedChange: (next: ReadonlySet<number>) => void;
  onDecision: (sourceId: number, decision: ImportDecision | null) => void;
  onFocus: (sourceId: number | null) => void;
  page: number;
  pageSize: number;
  total: number;
  onPageChange: (page: number, pageSize: number) => void;
}

const KIND_COLOURS: Record<string, string> = {
  cave: 'cyan',
  caveEntrance: 'blue',
  surfaceFeature: 'gold',
};

/**
 * The review table. Every row arrives with what the rules proposed already filled in, so
 * agreeing costs a tick and nothing else; the columns beside it are the three things a
 * reviewer actually checks — what it would be called, what it would become, and whether there
 * is already something there.
 */
export default function CandidateTable({
  items,
  elevationPolicy,
  decisions,
  selected,
  onSelectedChange,
  onDecision,
  onFocus,
  page,
  pageSize,
  total,
  onPageChange,
}: Props) {
  const { t } = useTranslation();
  const caveTypes = useCaveTypes();
  const entranceTypes = useEntranceTypes();
  const featureTypes = useFeatureTypes();

  const decisionOf = (row: ImportCandidate) => decisions[String(row.sourceId)];

  const effectiveKind = (row: ImportCandidate): ImportTargetKind | null =>
    decisionOf(row)?.kind ?? row.proposedKind ?? null;

  const typeOptions = (kind: ImportTargetKind | null) => {
    if (kind === 'cave') {
      return (caveTypes.data ?? []).map((type) => ({ value: type.code, label: type.name }));
    }
    if (kind === 'caveEntrance') {
      return (entranceTypes.data ?? []).map((type) => ({ value: type.code, label: type.name }));
    }
    return (featureTypes.data ?? []).map((type) => ({ value: type.code, label: type.name }));
  };

  const typeValue = (row: ImportCandidate) => {
    const decision = decisionOf(row);
    const kind = effectiveKind(row);
    if (kind === 'cave') {
      return decision?.caveTypeCode ?? row.proposedCaveTypeCode ?? undefined;
    }
    if (kind === 'caveEntrance') {
      return decision?.entranceTypeCode ?? row.proposedEntranceTypeCode ?? undefined;
    }
    return decision?.featureTypeCode ?? row.proposedFeatureTypeCode ?? undefined;
  };

  const setType = (row: ImportCandidate, code?: string) => {
    const kind = effectiveKind(row);
    const decision = decisionOf(row) ?? {};
    onDecision(row.sourceId, {
      ...decision,
      kind: kind ?? undefined,
      caveTypeCode: kind === 'cave' ? (code ?? null) : null,
      entranceTypeCode: kind === 'caveEntrance' ? (code ?? null) : null,
      featureTypeCode: kind === 'surfaceFeature' ? (code ?? null) : null,
    });
  };

  return (
    <Table<ImportCandidate>
      rowKey="sourceId"
      size="small"
      scroll={{ x: 'max-content' }}
      dataSource={items}
      data-testid="import-candidate-table"
      onRow={(row) => ({ onMouseEnter: () => onFocus(row.sourceId), onMouseLeave: () => onFocus(null) })}
      rowSelection={{
        selectedRowKeys: [...selected],
        onChange: (keys) => onSelectedChange(new Set(keys as number[])),
        // A row nothing proposes anything for cannot be created, so it cannot be selected —
        // the reviewer gives it a kind first, which is the decision that makes it selectable.
        getCheckboxProps: (row) => ({ disabled: effectiveKind(row) === null }),
      }}
      pagination={{
        current: page,
        pageSize,
        total,
        showSizeChanger: true,
        onChange: onPageChange,
      }}
      columns={[
        {
          title: t('vectorImport.columns.name'),
          dataIndex: 'proposedName',
          render: (_: unknown, row) => (
            <Flex vertical>
              <Typography.Text strong>{row.proposedName ?? row.sourceName ?? '—'}</Typography.Text>
              {row.sourceName && row.sourceName !== row.proposedName && (
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {row.sourceName}
                </Typography.Text>
              )}
            </Flex>
          ),
        },
        {
          title: t('vectorImport.columns.kind'),
          dataIndex: 'proposedKind',
          width: 190,
          render: (_: unknown, row) => (
            <Select
              size="small"
              allowClear
              style={{ width: 170 }}
              placeholder={t('vectorImport.kindNone')}
              value={effectiveKind(row) ?? undefined}
              onChange={(value?: ImportTargetKind) =>
                onDecision(row.sourceId, { ...(decisionOf(row) ?? {}), kind: value ?? null })
              }
              options={(['cave', 'caveEntrance', 'surfaceFeature'] as const).map((kind) => ({
                value: kind,
                label: t(`vectorImport.kinds.${kind}`),
              }))}
            />
          ),
        },
        {
          title: t('vectorImport.columns.type'),
          width: 190,
          render: (_: unknown, row) => (
            <Select
              size="small"
              allowClear
              showSearch
              optionFilterProp="label"
              style={{ width: 170 }}
              disabled={effectiveKind(row) === null}
              value={typeValue(row)}
              onChange={(value?: string) => setType(row, value)}
              options={typeOptions(effectiveKind(row))}
            />
          ),
        },
        {
          title: t('vectorImport.columns.rule'),
          dataIndex: 'ruleName',
          width: 190,
          render: (_: unknown, row) =>
            row.ruleName ? (
              <Flex vertical gap={2}>
                <Tag color={KIND_COLOURS[row.proposedKind ?? ''] ?? 'default'}>{row.ruleName}</Tag>
                {row.conflictingRuleNames.length > 0 && (
                  // Order decided this, and the screen says so — no silent arbitration.
                  <Tooltip title={t('vectorImport.conflictHint', { rules: row.conflictingRuleNames.join(', ') })}>
                    <Tag color="orange">{t('vectorImport.conflict')}</Tag>
                  </Tooltip>
                )}
              </Flex>
            ) : (
              <Typography.Text type="secondary">{t('vectorImport.noRule')}</Typography.Text>
            ),
        },
        {
          title: t('vectorImport.columns.position'),
          width: 200,
          render: (_: unknown, row) => {
            if (row.geom?.type !== 'Point') {
              return <Tag>{t(`vectorImport.geometry.${row.geometry}`)}</Tag>;
            }
            const decision = decisionOf(row);
            const keeps = decision?.keepElevation ?? elevationPolicy === 'keep';
            return (
              <Flex vertical gap={2} align="start">
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {(row.geom.coordinates as number[])[1].toFixed(5)},{' '}
                  {(row.geom.coordinates as number[])[0].toFixed(5)}
                </Typography.Text>
                {/* The altitude the file carries, and whether this row keeps it. The file is
                    the only source: nothing is derived from an elevation model, because a
                    public grid beside a cliff — which is where entrances are — is wrong by a
                    hundred metres with nothing on screen to say so. */}
                {row.sourceElevation != null && (
                  <Checkbox
                    checked={keeps}
                    onChange={(e) =>
                      onDecision(row.sourceId, { ...(decision ?? {}), keepElevation: e.target.checked })
                    }
                  >
                    <Typography.Text style={{ fontSize: 12 }}>
                      {t('vectorImport.keepAltitude', { altitude: Math.round(row.sourceElevation) })}
                    </Typography.Text>
                  </Checkbox>
                )}
              </Flex>
            );
          },
        },
        {
          title: t('vectorImport.columns.nearby'),
          width: 260,
          render: (_: unknown, row) => {
            if (!row.duplicate) {
              return <Typography.Text type="secondary">{t('vectorImport.nothingNearby')}</Typography.Text>;
            }
            const decision = decisionOf(row);
            return (
              <Flex vertical gap={4} align="start">
                <Typography.Text style={{ fontSize: 12 }}>
                  {row.duplicate.name ?? t('vectorImport.unnamed')} ·{' '}
                  {t('vectorImport.metresAway', { distance: Math.round(row.duplicate.distanceMeters) })}
                </Typography.Text>
                <Flex gap={4} wrap>
                  <Button
                    size="small"
                    type={decision?.action === 'attach' ? 'primary' : 'default'}
                    onClick={() =>
                      onDecision(row.sourceId, {
                        ...(decision ?? {}),
                        action: 'attach',
                        attachToFeatureId: row.duplicate!.featureId,
                      })
                    }
                  >
                    {t('vectorImport.itIsThisOne')}
                  </Button>
                  {row.duplicate.caveFeatureId && (
                    <Button
                      size="small"
                      onClick={() =>
                        onDecision(row.sourceId, {
                          ...(decision ?? {}),
                          action: 'create',
                          kind: 'caveEntrance',
                          attachToFeatureId: row.duplicate!.caveFeatureId,
                        })
                      }
                    >
                      {t('vectorImport.secondEntranceOf', {
                        cave: row.duplicate.caveName ?? t('vectorImport.unnamed'),
                      })}
                    </Button>
                  )}
                  {decision?.action && decision.action !== 'create' && (
                    <Button size="small" type="link" onClick={() => onDecision(row.sourceId, null)}>
                      {t('common.reset')}
                    </Button>
                  )}
                </Flex>
              </Flex>
            );
          },
        },
        {
          title: t('vectorImport.columns.decision'),
          width: 130,
          render: (_: unknown, row) => {
            const action = decisionOf(row)?.action ?? 'create';
            return <Tag color={action === 'create' ? 'green' : action === 'attach' ? 'blue' : 'default'}>
              {t(`vectorImport.actions.${action}`)}
            </Tag>;
          },
        },
      ]}
    />
  );
}
