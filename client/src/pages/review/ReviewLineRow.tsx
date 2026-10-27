// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { CheckOutlined, CloseOutlined, DownOutlined, LockOutlined, RightOutlined, UndoOutlined } from '@ant-design/icons';
import { Checkbox, Flex, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useFeature, type HeldLine } from '../../api/hooks.ts';
import Button from '../../components/Button.tsx';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import {
  actOf,
  candidateEntries,
  defaultFields,
  foreignDependenciesOf,
  fieldDiffs,
  isCreation,
  ownIdOf,
  pointText,
  recordOf,
  shortValue,
  titleOf,
  type CurrentRow,
  type FieldDiff,
} from '../../components/review/reviewModel.ts';

const DECISION_COLOR: Record<string, string> = {
  pending: 'gold',
  accepted: 'green',
  rejected: 'red',
  superseded: 'default',
  failed: 'volcano',
};

/** A date as the reader's locale writes it, or the text itself when it is not one. */
function when(value: unknown, locale: string): string {
  if (typeof value !== 'string') {
    return '';
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString(locale);
}

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <Typography.Text type="secondary">{label}: </Typography.Text>
      {children}
    </div>
  );
}

/** What the server holds now of a feature row an edit names, read when the line is opened. */
function useCurrentRow(rowId: string | null, enabled: boolean): { current: CurrentRow | undefined; withheld: boolean; loading: boolean } {
  const feature = useFeature(enabled && rowId ? rowId : undefined);
  const dto = feature.data?.feature;
  if (!dto) {
    return { current: undefined, withheld: false, loading: feature.isLoading };
  }
  // A position the reader is shown only approximately is not a position to compare against.
  const withheld = dto.approximateLocation || dto.omittedLocation;
  return {
    current: {
      name: dto.name,
      description: dto.description,
      properties: dto.properties,
      geometry: withheld ? undefined : dto.geometry,
    },
    withheld,
    loading: false,
  };
}

function PositionValue({ value }: { value: unknown }) {
  const { t } = useTranslation();
  if (value === undefined || value === null) {
    return <Typography.Text type="secondary">{t('review.positionNotShown')}</Typography.Text>;
  }
  return <span>{pointText(value) ?? shortValue(value)}</span>;
}

function FieldValue({ field, value }: { field: string; value: unknown }) {
  if (field === 'position') {
    return <PositionValue value={value} />;
  }
  const text = shortValue(value);
  return text === '' ? <Typography.Text type="secondary">-</Typography.Text> : <span style={{ wordBreak: 'break-word' }}>{text}</span>;
}

/**
 * An edit of a feature row: each field the reviewer may accept on its own, the row now beside the
 * phone's version, with a box per field.
 */
function RowEdit({
  line,
  chosen,
  onChoose,
  editable,
}: {
  line: HeldLine;
  /** The fields ticked; undefined until the reviewer touches a box, which stands for the default. */
  chosen: string[] | undefined;
  onChoose: (fields: string[]) => void;
  editable: boolean;
}) {
  const { t } = useTranslation();
  const { current, withheld, loading } = useCurrentRow(line.rowId, true);
  const diffs = fieldDiffs(line, current);
  const ticked = chosen ?? defaultFields(diffs);
  const untouched = chosen === undefined;

  // Once the row is open and compared, what is ticked on screen becomes what is sent: a box left
  // alone must not mean "everything", nor write a field the reviewer was not shown.
  useEffect(() => {
    if (!loading && editable && untouched) {
      onChoose(defaultFields(diffs));
    }
  }, [loading, editable, untouched, diffs, onChoose]);

  if (loading) {
    return <Typography.Text type="secondary">{t('common.loading')}</Typography.Text>;
  }
  return (
    <Flex vertical gap={6} data-testid="review-field-diff">
      {line.rowId === null && (
        <Typography.Text type="secondary">
          <LockOutlined /> {t('review.rowHidden')}
        </Typography.Text>
      )}
      {current === undefined && line.rowId !== null && (
        <Typography.Text type="secondary">{t('review.currentUnknown')}</Typography.Text>
      )}
      {withheld && <Typography.Text type="secondary">{t('review.positionWithheld')}</Typography.Text>}
      <Table<FieldDiff>
        size="small"
        pagination={false}
        rowKey="field"
        dataSource={diffs}
        columns={[
          {
            title: '',
            width: 40,
            render: (_: unknown, d) => (
              <Checkbox
                data-testid={`review-field-${d.field}`}
                aria-label={t('review.acceptField', { field: t(`review.field.${d.field}`, { defaultValue: d.field }) })}
                disabled={!editable}
                checked={ticked.includes(d.field)}
                onChange={(e) =>
                  onChoose(e.target.checked ? [...ticked, d.field] : ticked.filter((f) => f !== d.field))
                }
              />
            ),
          },
          { title: t('review.field.label'), render: (_: unknown, d) => t(`review.field.${d.field}`, { defaultValue: d.field }) },
          {
            title: t('review.current'),
            render: (_: unknown, d) => <FieldValue field={d.field} value={d.current} />,
          },
          {
            title: t('review.proposed'),
            render: (_: unknown, d) => (
              <Flex gap={6} align="center" wrap>
                <FieldValue field={d.field} value={d.proposed} />
                {!d.differs && <Tag>{t('review.unchanged')}</Tag>}
              </Flex>
            ),
          },
        ]}
      />
    </Flex>
  );
}

/** A feature row the phone creates: the values it would be created with. */
function RowCreation({ line }: { line: HeldLine }) {
  const { t } = useTranslation();
  const row = recordOf(line);
  const position = pointText(row.geometry);
  return (
    <Flex vertical gap={2} data-testid="review-creation">
      {typeof row.kind === 'string' && <Fact label={t('review.field.kind')}>{row.kind}</Fact>}
      {typeof row.name === 'string' && <Fact label={t('review.field.name')}>{row.name}</Fact>}
      {typeof row.description === 'string' && row.description && (
        <Fact label={t('review.field.description')}>{row.description}</Fact>
      )}
      {typeof row.featureTypeCode === 'string' && <Fact label={t('review.field.type')}>{row.featureTypeCode}</Fact>}
      {typeof row.caveTypeCode === 'string' && <Fact label={t('review.field.type')}>{row.caveTypeCode}</Fact>}
      {typeof row.entranceTypeCode === 'string' && <Fact label={t('review.field.type')}>{row.entranceTypeCode}</Fact>}
      {row.geometry !== undefined && row.geometry !== null ? (
        <Fact label={t('review.field.position')}>{position ?? t('review.positionShape')}</Fact>
      ) : (
        <Fact label={t('review.field.position')}>
          <Typography.Text type="secondary">{t('review.positionNotShown')}</Typography.Text>
        </Fact>
      )}
      {typeof row.altitude === 'number' && <Fact label={t('review.field.altitude')}>{row.altitude}</Fact>}
    </Flex>
  );
}

/** A removal asked for by the phone: the row it would take away. */
function RowRemoval({ line }: { line: HeldLine }) {
  const { t } = useTranslation();
  const { current } = useCurrentRow(line.rowId, true);
  return (
    <Flex vertical gap={2}>
      <Typography.Text>{t('review.removalExplain')}</Typography.Text>
      {current?.name && <Fact label={t('review.field.name')}>{current.name}</Fact>}
    </Flex>
  );
}

/** What a section other than the feature rows proposes. */
function SectionDetail({ line, lines }: { line: HeldLine; lines: readonly HeldLine[] }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const rec = recordOf(line);
  switch (line.section) {
    case 'documents':
      return (
        <Flex vertical gap={2} data-testid="review-document">
          {typeof rec.title === 'string' && <Fact label={t('review.field.title')}>{rec.title}</Fact>}
          {typeof rec.description === 'string' && rec.description && (
            <Fact label={t('review.field.description')}>{rec.description}</Fact>
          )}
          {typeof rec.fileName === 'string' && <Fact label={t('review.file')}>{rec.fileName}</Fact>}
          {typeof rec.sizeBytes === 'number' && <Fact label={t('review.size')}>{formatSize(rec.sizeBytes)}</Fact>}
          {typeof rec.capturedAt === 'string' && <Fact label={t('review.capturedAt')}>{when(rec.capturedAt, locale)}</Fact>}
          <Typography.Text type="secondary">{t('review.noPreviewHeld')}</Typography.Text>
        </Flex>
      );
    case 'documentLinks':
      return (
        <Flex vertical gap={2}>
          {typeof rec.role === 'string' && <Fact label={t('review.role')}>{rec.role}</Fact>}
          {typeof rec.caption === 'string' && rec.caption && <Fact label={t('review.caption')}>{rec.caption}</Fact>}
        </Flex>
      );
    case 'people':
      return (
        <Flex vertical gap={2}>
          {typeof rec.username === 'string' && <Fact label={t('review.username')}>{rec.username}</Fact>}
          {typeof rec.caverId === 'string' && <Typography.Text>{t('review.linkedToCaver')}</Typography.Text>}
        </Flex>
      );
    case 'tripRecordings': {
      const id = ownIdOf(line);
      const points = lines.filter(
        (l) => l.section === 'tripPoints' && id !== null && recordOf(l).recordingId === id,
      );
      return (
        <Flex vertical gap={2} data-testid="review-recording">
          {typeof rec.title === 'string' && <Fact label={t('review.field.title')}>{rec.title}</Fact>}
          {typeof rec.description === 'string' && rec.description && (
            <Fact label={t('review.field.description')}>{rec.description}</Fact>
          )}
          {typeof rec.log === 'string' && rec.log && <Fact label={t('review.log')}>{rec.log}</Fact>}
          {typeof rec.startedAt === 'string' && <Fact label={t('review.startedAt')}>{when(rec.startedAt, locale)}</Fact>}
          {typeof rec.endedAt === 'string' && <Fact label={t('review.endedAt')}>{when(rec.endedAt, locale)}</Fact>}
          <Fact label={t('review.points')}>{points.length}</Fact>
          {points.length > 0 && (
            <ul style={{ margin: 0, paddingInlineStart: 20 }}>
              {points.map((p) => {
                const point = recordOf(p);
                return (
                  <li key={p.reviewId}>
                    {when(point.scannedAt, locale)}
                    {typeof point.notes === 'string' && point.notes ? ` - ${point.notes}` : ''}
                  </li>
                );
              })}
            </ul>
          )}
        </Flex>
      );
    }
    case 'tripPoints':
      return (
        <Flex vertical gap={2}>
          {typeof rec.scannedAt === 'string' && <Fact label={t('review.scannedAt')}>{when(rec.scannedAt, locale)}</Fact>}
          {typeof rec.notes === 'string' && rec.notes && <Fact label={t('review.notes')}>{rec.notes}</Fact>}
        </Flex>
      );
    case 'rasterMaps':
      return (
        <Flex vertical gap={2}>
          {typeof rec.title === 'string' && <Fact label={t('review.field.title')}>{rec.title}</Fact>}
          {typeof rec.mapType === 'string' && <Fact label={t('review.mapType')}>{rec.mapType}</Fact>}
          {typeof rec.fileName === 'string' && <Fact label={t('review.file')}>{rec.fileName}</Fact>}
          {typeof rec.sizeBytes === 'number' && <Fact label={t('review.size')}>{formatSize(rec.sizeBytes)}</Fact>}
          <Typography.Text type="secondary">{t('review.noPreviewHeld')}</Typography.Text>
        </Flex>
      );
    case 'rasterPins':
      // The server leaves the pin's place off when this reader may not place the object it is on.
      return typeof rec.x === 'number' && typeof rec.y === 'number' ? (
        <Fact label={t('review.pin')}>{`${rec.x}, ${rec.y}`}</Fact>
      ) : (
        <Typography.Text type="secondary">
          <LockOutlined /> {t('review.pinNotShown')}
        </Typography.Text>
      );
    default: {
      // A file import's candidate: what it names and the choices it would be committed with.
      const proposal = line.proposal as { decision?: unknown; names?: unknown } | null;
      const entries = candidateEntries(proposal?.decision);
      return (
        <Flex vertical gap={2} data-testid="review-candidate">
          {entries.map(([k, v]) => (
            <Fact key={k} label={t(`review.candidate.${k}`)}>
              {k === 'action'
                ? t(`review.candidate.actions.${v.toLowerCase()}`, { defaultValue: v })
                : k === 'createTrip'
                  ? t(v === 'true' ? 'review.candidate.yes' : 'review.candidate.no')
                  : v}
            </Fact>
          ))}
          {entries.length === 0 && <Typography.Text type="secondary">{t('review.noDetail')}</Typography.Text>}
        </Flex>
      );
    }
  }
}

export interface ReviewLineRowProps {
  line: HeldLine;
  lines: readonly HeldLine[];
  /** Whether this reader may decide; false for a reader of the queue and for the uploader. */
  canDecide: boolean;
  selected: boolean;
  onSelect: (checked: boolean) => void;
  chosen: string[] | undefined;
  onChoose: (fields: string[]) => void;
  onAccept: () => void;
  onReject: () => void;
  busy: boolean;
}

/** One line of a held batch: what it is, what it would do, and what a reviewer can do about it. */
export default function ReviewLineRow({
  line,
  lines,
  canDecide,
  selected,
  onSelect,
  chosen,
  onChoose,
  onAccept,
  onReject,
  busy,
}: ReviewLineRowProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const pending = line.decision === 'pending';
  const act = actOf(line);
  const title = titleOf(line) ?? t('review.untitled');
  const sectionLabel = t(`review.section.${line.section ?? 'none'}`, { defaultValue: line.section ?? t('review.section.none') });
  const foreign = foreignDependenciesOf(line.reviewId, lines);
  const needs = line.dependsOnReviewIds
    .map((id) => lines.find((l) => l.reviewId === id))
    .filter((l): l is HeldLine => l !== undefined);
  const isRowEdit = line.section === 'rows' && !isCreation(line) && act !== 'delete';
  const editable = canDecide && pending && line.acceptableFields.length > 0;
  // Nothing ticked is not an acceptance: the box is there to narrow, not to empty.
  const nothingTicked = isRowEdit && editable && chosen !== undefined && chosen.length === 0;

  return (
    <div
      data-testid="review-line"
      data-review-id={line.reviewId}
      data-decision={line.decision}
      style={{ padding: '8px 0', borderBottom: '1px solid var(--ant-color-border-secondary, #f0f0f0)' }}
    >
      <Flex gap={8} align="start" wrap>
        {canDecide && pending && (
          <Checkbox
            checked={selected}
            onChange={(e) => onSelect(e.target.checked)}
            aria-label={t('review.selectLine', { name: title })}
            data-testid="review-line-select"
          />
        )}
        <Button
          type="text"
          size="small"
          icon={open ? <DownOutlined /> : <RightOutlined />}
          onClick={() => setOpen(!open)}
          aria-expanded={open}
          aria-label={t(open ? 'review.hideDetails' : 'review.showDetails', { name: title })}
          data-testid="review-line-toggle"
        />
        <Flex vertical gap={2} style={{ flex: 1, minWidth: 200 }}>
          <Flex gap={6} align="center" wrap>
            <Tag>{sectionLabel}</Tag>
            {act && <Tag color={act === 'delete' ? 'red' : act === 'create' ? 'green' : 'blue'}>{t(`review.act.${act}`, { defaultValue: act })}</Tag>}
            <Typography.Text strong>{title}</Typography.Text>
            <Tag color={DECISION_COLOR[line.decision]} data-testid="review-line-decision">
              {t(`review.decision.${line.decision}`)}
            </Tag>
            {line.changedSince && pending && (
              <Tag color="orange" data-testid="review-changed-since">
                {t('review.changedSince')}
              </Tag>
            )}
          </Flex>
          {needs.length > 0 && (
            <Typography.Text type="secondary" data-testid="review-needs">
              {t('review.needs', { names: needs.map((n) => titleOf(n) ?? t('review.untitled')).join(', ') })}
            </Typography.Text>
          )}
          {foreign.length > 0 && pending && (
            <Typography.Text type="secondary" data-testid="review-needs-other">
              {t('review.needsOther', { count: foreign.length })}
            </Typography.Text>
          )}
          {line.acceptedFields && line.acceptedFields.length > 0 && (
            <Typography.Text type="secondary">
              {t('review.acceptedFields', {
                fields: line.acceptedFields.map((f) => t(`review.field.${f}`, { defaultValue: f })).join(', '),
              })}
            </Typography.Text>
          )}
          {line.decision === 'failed' && (
            <Typography.Text type="danger" data-testid="review-failure">
              {line.failureDetail ?? line.failureCode}
            </Typography.Text>
          )}
          {line.note && <Typography.Text type="secondary">{t('review.lineNote', { note: line.note })}</Typography.Text>}
        </Flex>
        {canDecide && pending && (
          <Flex gap={6}>
            <Button
              size="small"
              type="primary"
              icon={<CheckOutlined />}
              disabled={busy || nothingTicked}
              onClick={onAccept}
              data-testid="review-line-accept"
            >
              {t('review.accept')}
            </Button>
            <Button size="small" danger icon={<CloseOutlined />} disabled={busy} onClick={onReject} data-testid="review-line-reject">
              {t('review.reject')}
            </Button>
          </Flex>
        )}
        {canDecide && line.decision === 'rejected' && (
          <Button size="small" icon={<UndoOutlined />} disabled={busy} onClick={onAccept} data-testid="review-line-reopen">
            {t('review.acceptAfterAll')}
          </Button>
        )}
      </Flex>
      {open && (
        <div style={{ marginInlineStart: 36, marginTop: 8 }} data-testid="review-line-detail">
          {line.proposal === null ? (
            <Typography.Text type="secondary">
              <LockOutlined /> {t('review.proposalHidden')}
            </Typography.Text>
          ) : line.section === 'rows' ? (
            act === 'delete' ? (
              <RowRemoval line={line} />
            ) : isCreation(line) ? (
              <RowCreation line={line} />
            ) : (
              <RowEdit line={line} chosen={chosen} onChoose={onChoose} editable={editable} />
            )
          ) : (
            <SectionDetail line={line} lines={lines} />
          )}
        </div>
      )}
    </div>
  );
}
