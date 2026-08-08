// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Image, Input, Select, Space, Table, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCaveTypes,
  useEntranceTypes,
  useFeatureTypes,
  type ImportTargetKind,
  type PhotoCandidate,
  type PhotoDecision,
  type PhotoImportOptions,
} from '../../api/hooks.ts';
import PositionSourceTag from './PositionSourceTag.tsx';

interface Props {
  items: PhotoCandidate[];
  defaultKind: PhotoImportOptions['defaultKind'];
  decisions: Record<string, PhotoDecision>;
  selected: ReadonlySet<string>;
  selectableKeys: string[];
  onSelectedChange: (next: ReadonlySet<string>) => void;
  onDecision: (key: string, decision: PhotoDecision | null) => void;
  onFocus: (key: string | null) => void;
  page: number;
  pageSize: number;
  total: number;
  onPageChange: (page: number, pageSize: number) => void;
}

/**
 * The review table for a drop of photographs. One row is one *place*, however many pictures
 * show it — the gallery in the first column is the whole reason twelve shots of an entrance are
 * not twelve rows to reject one at a time.
 *
 * The three things a reviewer actually decides are the three editable columns: what it becomes,
 * what to call it, and whether it is something already in the registry. The proximity list is
 * offered as a choice rather than applied, because "there is a cave eleven metres away" is
 * evidence, not an answer.
 */
export default function PhotoCandidateTable({
  items,
  defaultKind,
  decisions,
  selected,
  selectableKeys,
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

  const decisionOf = (row: PhotoCandidate) => decisions[row.key];
  const effectiveKind = (row: PhotoCandidate): ImportTargetKind =>
    decisionOf(row)?.kind ?? defaultKind ?? 'caveEntrance';

  const typeOptions = (kind: ImportTargetKind) => {
    if (kind === 'cave') {
      return (caveTypes.data ?? []).map((type) => ({ value: type.code, label: type.name }));
    }
    if (kind === 'caveEntrance') {
      return (entranceTypes.data ?? []).map((type) => ({ value: type.code, label: type.name }));
    }
    return (featureTypes.data ?? []).map((type) => ({ value: type.code, label: type.name }));
  };

  const typeValue = (row: PhotoCandidate) => {
    const decision = decisionOf(row);
    const kind = effectiveKind(row);
    if (kind === 'cave') {
      return decision?.caveTypeCode ?? undefined;
    }
    if (kind === 'caveEntrance') {
      return decision?.entranceTypeCode ?? undefined;
    }
    return decision?.featureTypeCode ?? undefined;
  };

  const setType = (row: PhotoCandidate, code: string | undefined) => {
    const kind = effectiveKind(row);
    const key = kind === 'cave' ? 'caveTypeCode' : kind === 'caveEntrance' ? 'entranceTypeCode' : 'featureTypeCode';
    onDecision(row.key, { ...decisionOf(row), [key]: code ?? null });
  };

  return (
    <Table<PhotoCandidate>
      rowKey="key"
      size="small"
      dataSource={items}
      data-testid="photo-candidate-table"
      onRow={(row) => ({ onMouseEnter: () => onFocus(row.key), onMouseLeave: () => onFocus(null) })}
      rowSelection={{
        selectedRowKeys: [...selected],
        // A place nothing has put anywhere cannot be created, so it cannot be ticked. The
        // server answers which rows those are, because a decision made on page one has to
        // count for a row on page three.
        getCheckboxProps: (row) => ({ disabled: !selectableKeys.includes(row.key) }),
        onChange: (keys) => onSelectedChange(new Set(keys.map(String))),
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
          title: t('photoImport.columns.pictures'),
          key: 'pictures',
          width: 190,
          render: (_, row) => (
            <Flex gap={4} wrap>
              <Image.PreviewGroup>
                {row.members.slice(0, 4).map((member) => (
                  <Image
                    key={member.fileId}
                    src={member.thumbnailUrl ?? undefined}
                    alt={member.originalName}
                    width={40}
                    height={40}
                    style={{ objectFit: 'cover', borderRadius: 4 }}
                  />
                ))}
              </Image.PreviewGroup>
              {row.members.length > 4 && (
                <Typography.Text type="secondary">+{row.members.length - 4}</Typography.Text>
              )}
            </Flex>
          ),
        },
        {
          title: t('photoImport.columns.name'),
          key: 'name',
          render: (_, row) => (
            <Flex vertical gap={2}>
              <Input
                size="small"
                placeholder={row.proposedName ?? t('photoImport.unnamed')}
                value={decisionOf(row)?.name ?? row.proposedName ?? ''}
                onChange={(e) =>
                  onDecision(row.key, { ...decisionOf(row), name: e.target.value || null })
                }
              />
              <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                {row.members[0]?.originalName}
                {row.members.length > 1 && ` +${row.members.length - 1}`}
              </Typography.Text>
            </Flex>
          ),
        },
        {
          title: t('photoImport.columns.becomes'),
          key: 'kind',
          width: 150,
          render: (_, row) => (
            <Select<ImportTargetKind>
              size="small"
              style={{ width: '100%' }}
              value={effectiveKind(row)}
              onChange={(kind) => onDecision(row.key, { ...decisionOf(row), kind })}
              options={(['cave', 'caveEntrance', 'surfaceFeature'] as const).map((kind) => ({
                value: kind,
                label: t(`vectorImport.kinds.${kind}`),
              }))}
            />
          ),
        },
        {
          title: t('photoImport.columns.type'),
          key: 'type',
          width: 150,
          render: (_, row) => (
            <Select
              size="small"
              allowClear
              style={{ width: '100%' }}
              value={typeValue(row)}
              placeholder={t('photoImport.typePlaceholder')}
              options={typeOptions(effectiveKind(row))}
              onChange={(code: string | undefined) => setType(row, code)}
            />
          ),
        },
        {
          title: t('photoImport.columns.position'),
          key: 'position',
          width: 170,
          render: (_, row) => (
            <Flex vertical gap={2}>
              <PositionSourceTag
                source={row.positionSource}
                confidence={row.confidence}
                trackMatchSeconds={row.trackMatchSecondsFromFix}
              />
              {row.altitudeMeters !== null && (
                <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                  {t('photoImport.altitude', { value: Math.round(row.altitudeMeters) })}
                </Typography.Text>
              )}
              {row.directionDegrees !== null && (
                <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                  {t(row.directionIsMagnetic ? 'photoImport.bearingMagnetic' : 'photoImport.bearing', {
                    value: Math.round(row.directionDegrees),
                  })}
                </Typography.Text>
              )}
            </Flex>
          ),
        },
        {
          title: t('photoImport.columns.nearby'),
          key: 'nearby',
          width: 240,
          render: (_, row) => {
            if (row.nearby.length === 0) {
              return <Typography.Text type="secondary">{t('photoImport.nothingNearby')}</Typography.Text>;
            }
            const decision = decisionOf(row);
            const attached = decision?.action === 'attach' ? decision.attachToFeatureId : undefined;
            return (
              <Space orientation="vertical" size={2} style={{ width: '100%' }}>
                <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                  {t('photoImport.nearbyCount', { count: row.nearby.length })}
                </Typography.Text>
                <Select
                  size="small"
                  allowClear
                  style={{ width: '100%' }}
                  value={attached ?? undefined}
                  placeholder={t('photoImport.createNew')}
                  onChange={(featureId: string | undefined) =>
                    onDecision(
                      row.key,
                      featureId
                        ? { ...decision, action: 'attach', attachToFeatureId: featureId }
                        : { ...decision, action: 'create', attachToFeatureId: null },
                    )
                  }
                  options={row.nearby.map((hit) => ({
                    value: hit.featureId,
                    label: t('photoImport.nearbyOption', {
                      name: hit.caveName ?? hit.name ?? t('photoImport.unnamed'),
                      distance: Math.round(hit.distanceMeters),
                    }),
                  }))}
                />
              </Space>
            );
          },
        },
        {
          title: t('photoImport.columns.decision'),
          key: 'decision',
          width: 110,
          render: (_, row) => {
            const action = decisionOf(row)?.action ?? 'create';
            return (
              <Tooltip title={t(`photoImport.actions.${action}Hint`)}>
                <Tag color={action === 'skip' ? 'default' : action === 'attach' ? 'blue' : 'green'}>
                  {t(`photoImport.actions.${action}`)}
                </Tag>
              </Tooltip>
            );
          },
        },
      ]}
    />
  );
}
