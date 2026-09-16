// SPDX-License-Identifier: AGPL-3.0-or-later
import { Button, Flex, Select, Space, Switch, Table, Tag, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import type {
  SpeleolocPoint,
  SpeleolocPointAction,
  SpeleolocPointDecision,
} from '../../api/hooks.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { proposalIsTied } from './speleolocSelection.ts';

interface Props {
  items: readonly SpeleolocPoint[];
  decisions: Record<string, SpeleolocPointDecision>;
  /** A patch onto one scan's decision, or null to forget what was said about it. */
  onDecision: (pointId: string, patch: Partial<SpeleolocPointDecision> | null) => void;
  /**
   * Whether one scan would be recorded if the confirmation happened now. Decided by the page, not
   * here, so the switch on a row and the count on the button are the same answer to one question.
   */
  isTaken: (point: SpeleolocPoint) => boolean;
  /**
   * Whether the reviewer's own decision is the only thing standing between this scan and being
   * recorded. False where the confirmation would refuse it whatever the switch says — no station,
   * nobody named, or somebody named who is not on the trip — and there the switch is dead rather
   * than a control that springs back and explains nothing.
   */
  canTake: (point: SpeleolocPoint) => boolean;
  /**
   * Every scan of the whole recording the server says may be taken — not the twenty-five on
   * screen. What "all" means is the server's answer, which is the only reading under which taking
   * all of them and confirming does what the reviewer just asked for.
   */
  selectablePointIds: readonly string[];
  onBulkDecision: (pointIds: readonly string[], action: SpeleolocPointAction) => void;
  /** Names a caver, for the people the mapping has settled. */
  nameOf: (caverId: string) => string;
  page: number;
  pageSize: number;
  total: number;
  onPageChange: (page: number, pageSize: number) => void;
}

/**
 * One row per scan, with the station it would become beside the marker it was made at.
 *
 * The station column is the whole screen in miniature. A scan whose place resolved carries a
 * proposal — the station nearest the depth that place is recorded at — shown with the depth it was
 * made from and the metres it is off by, so the reviewer is judging a statement rather than
 * agreeing with a word. A scan whose place resolved to nothing carries no station, no
 * pre-selection and no switch that can be turned on: the five ways a proposal cannot be made are
 * named in red, because a row that merely looked empty would be taken for one nobody had got to
 * yet, and the difference between those two is the whole point of reviewing this at all.
 */
export default function SpeleolocPointTable({
  items,
  decisions,
  onDecision,
  isTaken,
  canTake,
  selectablePointIds,
  onBulkDecision,
  nameOf,
  page,
  pageSize,
  total,
  onPageChange,
}: Props) {
  const { t, i18n } = useTranslation();
  // Sized on the pointer rather than on the width: a phone in landscape is wide enough for this
  // layout and still has no pixel precision, and a switch that decides whether somebody's position
  // is written is not a control to make people aim at.
  const controlSize = useCoarsePointer() ? 'middle' : 'small';

  const when = (value: string) => new Date(value).toLocaleString(i18n.language);

  const columns: ColumnsType<SpeleolocPoint> = [
    {
      title: t('speleolocImport.columns.take'),
      key: 'take',
      width: 80,
      render: (_, point) => (
        <Switch
          size={controlSize === 'middle' ? 'default' : 'small'}
          checked={isTaken(point)}
          // Dead where the confirmation would refuse the row whatever this says — no station
          // could be named, or nobody has said whose scan it is. A live switch there is an offer
          // the screen cannot keep, and one that silently springs back explains nothing at all.
          disabled={!canTake(point)}
          onChange={(checked) => onDecision(point.pointId, { action: checked ? 'record' : 'skip' })}
          data-testid={`speleoloc-import-take-${point.pointId}`}
        />
      ),
    },
    {
      title: t('speleolocImport.columns.when'),
      key: 'when',
      render: (_, point) => <Typography.Text>{when(point.scannedAt)}</Typography.Text>,
    },
    {
      title: t('speleolocImport.columns.place'),
      key: 'place',
      render: (_, point) => (
        <Space orientation="vertical" size={0}>
          <Typography.Text>{point.placeTitle ?? t('speleolocImport.noPlaceTitle')}</Typography.Text>
          {point.placeDepthM !== null && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('speleolocImport.placeDepth', { depth: point.placeDepthM })}
            </Typography.Text>
          )}
        </Space>
      ),
    },
    {
      title: t('speleolocImport.columns.station'),
      key: 'station',
      render: (_, point) => {
        const decision = decisions[point.pointId];
        if (point.state !== 'proposed') {
          // No proposal, and no control that pretends there could be one. The reason is named
          // rather than left as an empty cell: "nothing was proposed" and "nobody has looked at
          // this yet" look identical, and only one of them is true.
          //
          // A name given by hand is the exception, and it has to be shown. Such a row is one the
          // confirmation will record, at that name — so drawing only the red reason would say the
          // scan was left out while the station it is about to be written at sat nowhere on the
          // screen. The reason stands, because it is still true of the marker; the name stands
          // beside it, with the one control that undoes it.
          const named = decision?.stationName;
          return (
            <Space orientation="vertical" size={4}>
              <Tag color="red" data-testid={`speleoloc-import-state-${point.pointId}`}>
                {t(`speleolocImport.states.${point.state}`)}
              </Tag>
              {named ? (
                <>
                  <Space size={4} wrap>
                    <Tag color="gold" data-testid={`speleoloc-import-named-${point.pointId}`}>
                      {t('speleolocImport.namedStation', { station: named })}
                    </Tag>
                    <Button
                      size={controlSize}
                      onClick={() => onDecision(point.pointId, { stationName: null })}
                      data-testid={`speleoloc-import-clear-station-${point.pointId}`}
                    >
                      {t('speleolocImport.clearStation')}
                    </Button>
                  </Space>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {t('speleolocImport.namedStationHint')}
                  </Typography.Text>
                </>
              ) : (
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {t('speleolocImport.unresolvedHint')}
                </Typography.Text>
              )}
            </Space>
          );
        }

        const tied = proposalIsTied(point);
        return (
          <Space orientation="vertical" size={4}>
            <Tag
              color={tied ? 'gold' : 'green'}
              data-testid={`speleoloc-import-state-${point.pointId}`}
            >
              {tied ? t('speleolocImport.states.tied') : t('speleolocImport.states.proposed')}
            </Tag>
            <Select
              size={controlSize}
              style={{ minWidth: 220 }}
              // The nearest station unless the reviewer has said otherwise — which is exactly what
              // the confirmation would record, so the box shows the answer rather than an empty
              // control over a decision already taken.
              value={decision?.stationName ?? point.candidates[0]?.stationName}
              onChange={(value: string) => onDecision(point.pointId, { stationName: value })}
              data-testid={`speleoloc-import-station-${point.pointId}`}
              options={point.candidates.map((candidate) => ({
                value: candidate.stationName,
                label: `${candidate.stationName} · ${t('speleolocImport.candidate', {
                  depth: candidate.depthM,
                  delta: Math.abs(candidate.deltaM),
                })}`,
              }))}
            />
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('speleolocImport.proposedFrom', { depth: point.placeDepthM ?? 0 })}
            </Typography.Text>
          </Space>
        );
      },
    },
    {
      title: t('speleolocImport.columns.who'),
      key: 'who',
      render: (_, point) => {
        const caverId = decisions[point.pointId]?.caverId ?? point.caverId;
        if (caverId) {
          return <Tag color="green">{nameOf(caverId)}</Tag>;
        }
        return (
          <Tag color="red" data-testid={`speleoloc-import-unmapped-${point.pointId}`}>
            {point.deviceUserId
              ? t('speleolocImport.whoUnmapped')
              : t('speleolocImport.whoAnonymous')}
          </Tag>
        );
      },
    },
    {
      title: t('speleolocImport.columns.note'),
      key: 'note',
      render: (_, point) =>
        point.notes ? (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {point.notes}
          </Typography.Text>
        ) : null,
    },
  ];

  return (
    <>
      <Flex gap={8} align="center" wrap style={{ marginBottom: 8 }}>
        <Button
          size={controlSize}
          disabled={selectablePointIds.length === 0}
          onClick={() => onBulkDecision(selectablePointIds, 'record')}
          data-testid="speleoloc-import-take-all"
        >
          {t('speleolocImport.takeAll', { count: selectablePointIds.length })}
        </Button>
        <Button
          size={controlSize}
          disabled={selectablePointIds.length === 0}
          onClick={() => onBulkDecision(selectablePointIds, 'skip')}
          data-testid="speleoloc-import-skip-all"
        >
          {t('speleolocImport.skipAll')}
        </Button>
      </Flex>
      <Table<SpeleolocPoint>
        rowKey="pointId"
        size="small"
        // The columns are wider than a phone, so the table scrolls inside itself rather than
        // pushing the page sideways.
        scroll={{ x: 'max-content' }}
        data-testid="speleoloc-import-points"
        columns={columns}
        dataSource={[...items]}
        locale={{ emptyText: t('speleolocImport.noScans') }}
        pagination={{
          current: page,
          pageSize,
          total,
          showSizeChanger: true,
          onChange: onPageChange,
        }}
      />
    </>
  );
}
