// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { ArrowLeftOutlined, CheckOutlined, CloseOutlined, DownOutlined } from '@ant-design/icons';
import { App, Alert, Card, Checkbox, Dropdown, Empty, Flex, Input, Modal, Result, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import {
  changedSinceIds,
  REVIEW_NOT_PENDING,
  useAcceptReviewBatch,
  useAcceptReviewItem,
  useCan,
  useMe,
  useRejectReviewBatch,
  useRejectReviewItem,
  useReviewBatch,
  type HeldLine,
} from '../../api/hooks.ts';
import Button from '../../components/Button.tsx';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import ReviewLineRow from './ReviewLineRow.tsx';
import { SectionTags } from '../../components/review/ReviewParts.tsx';
import {
  dependenciesOf,
  dependentsOf,
  featureNames,
  foreignDependenciesOf,
  groupByCave,
  sourceLabel,
  titleOf,
  type FeatureNames,
} from '../../components/review/reviewModel.ts';
import { reviewProblem } from './reviewProblems.ts';

/**
 * The name a group of lines is listed under: the cave they hang on, by the name the server gives
 * it with the batch — for a reviewer every existing cave, for anybody else one they may read.
 */
function GroupTitle({ caveId, lines, names }: { caveId: string | null; lines: readonly HeldLine[]; names: FeatureNames }) {
  const { t } = useTranslation();
  if (caveId === null) {
    return <span>{t('review.groupOther')}</span>;
  }
  // A cave the batch itself creates is not on the server yet; its name is in the proposal.
  const inBatch = lines.find((l) => l.section === 'rows' && l.rowId === caveId && l.proposal !== null);
  const known = names.get(caveId);
  const name = (inBatch ? titleOf(inBatch) : null) ?? (known ? known.name || t('review.untitled') : null);
  return <span>{name ?? t('review.groupHiddenCave')}</span>;
}

interface Conflict {
  /** The review ids the server said changed. */
  ids: string[];
  /** How many of the selected lines were not yet sent when the decision stopped. */
  rest: number;
  /** Repeats the refused decision for the lines shown, this time over the current rows. */
  writeOver: () => Promise<void>;
}

/** The whole-batch decision is refused as one; this stands for it in the set of lines agreed to be written over. */
const WHOLE_BATCH = '*';

/** A decision stopped at one line because rows changed since the upload; the lines after it were not sent. */
class ChangedSinceStop extends Error {
  /** The line (or the whole batch) that was refused. */
  readonly reviewId: string;
  /** The lines the server named as changed. */
  readonly ids: string[];
  /** How many selected lines were not yet sent. */
  readonly rest: number;

  constructor(reviewId: string, ids: string[], rest: number) {
    super('changed since');
    this.reviewId = reviewId;
    this.ids = ids;
    this.rest = rest;
  }
}

/**
 * One held batch: its lines grouped by cave, with the controls a reviewer decides by.
 *
 * Decisions can be made on the whole batch, on selected lines, or on one line, and an edit can be
 * accepted field by field. What the server refuses (an edit whose row moved since the phone read
 * it, a line already decided) is turned into a choice here rather than a bare error.
 */
export default function ReviewBatchPage() {
  const { id } = useParams<{ id: string }>();
  const { t, i18n } = useTranslation();
  const { message, modal } = App.useApp();
  const detail = useReviewBatch(id);
  const me = useMe();
  const mayWrite = useCan('uploadReview', 'write');
  const acceptBatch = useAcceptReviewBatch();
  const rejectBatch = useRejectReviewBatch();
  const acceptItem = useAcceptReviewItem();
  const rejectItem = useRejectReviewItem();

  const [showDecided, setShowDecided] = useState(false);
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [choices, setChoices] = useState<Record<string, string[]>>({});
  const [note, setNote] = useState('');
  const [conflict, setConflict] = useState<Conflict | null>(null);
  const [working, setWorking] = useState(false);

  const batch = detail.data?.batch;
  const lines = useMemo(() => detail.data?.lines ?? [], [detail.data]);
  const names = useMemo(() => featureNames(detail.data?.features), [detail.data]);
  const pendingLines = useMemo(() => lines.filter((l) => l.decision === 'pending'), [lines]);
  // Once nothing waits, the decided lines are the page: an empty list would say nothing happened.
  const visible = showDecided || pendingLines.length === 0 ? lines : pendingLines;
  const groups = useMemo(() => groupByCave(visible), [visible]);
  const isOwn = batch !== undefined && me.data?.id === batch.uploaderId;
  const canDecide = mayWrite && !isOwn;
  const nameOf = (line: HeldLine) => titleOf(line) ?? t('review.untitled');

  if (detail.isLoading) {
    return <Spin style={{ margin: 48 }} />;
  }
  if (detail.error || !batch) {
    const notFound = detail.error instanceof ApiError && detail.error.status === 404;
    return (
      <Result
        status={notFound ? '404' : 'error'}
        title={notFound ? t('review.batchNotFound') : t('review.problem.generic')}
        extra={<Link to="/review">{t('review.backToQueue')}</Link>}
      />
    );
  }

  /**
   * Runs a decision; a changed-since refusal becomes the choice dialog and anything else a message.
   * `over` holds the lines the reviewer has agreed to write over the current rows: only those are
   * sent that way, so a later line that also changed gets its own refusal and its own question.
   */
  const decide = async (attempt: (over: ReadonlySet<string>) => Promise<void>, over: ReadonlySet<string> = new Set()) => {
    setWorking(true);
    try {
      await attempt(over);
      setConflict(null);
      setNote('');
      message.success(t('review.decided'));
    } catch (error) {
      if (error instanceof ChangedSinceStop) {
        setConflict({
          ids: error.ids,
          rest: error.rest,
          writeOver: () => decide(attempt, new Set(over).add(error.reviewId)),
        });
      } else if (error instanceof ApiError && error.code === REVIEW_NOT_PENDING) {
        setConflict(null);
        message.warning(reviewProblem(error, t));
      } else {
        setConflict(null);
        message.error(reviewProblem(error, t));
      }
    } finally {
      setWorking(false);
      setSelected(new Set());
    }
  };

  /**
   * Accepts lines one after another; a line a dependency already decided is skipped. The choice of
   * fields sent is exactly the one on screen for an opened edit, and the whole line for one never opened.
   */
  const acceptLines = (ids: string[]) => {
    const done = new Set<string>();
    return async (over: ReadonlySet<string>) => {
      for (const [index, reviewId] of ids.entries()) {
        if (done.has(reviewId)) {
          continue;
        }
        try {
          const result = await acceptItem.mutateAsync({
            reviewId,
            fields: choices[reviewId],
            note,
            overCurrent: over.has(reviewId),
          });
          result.decided.forEach((d) => done.add(d.reviewId));
          done.add(reviewId);
        } catch (error) {
          const changed = changedSinceIds(error);
          if (changed) {
            throw new ChangedSinceStop(reviewId, changed, ids.slice(index + 1).filter((i) => !done.has(i)).length);
          }
          throw error;
        }
      }
    };
  };

  const rejectLines = (ids: string[]) => {
    const done = new Set<string>();
    return async () => {
      for (const reviewId of ids) {
        if (done.has(reviewId)) {
          continue;
        }
        const result = await rejectItem.mutateAsync({ reviewId, note });
        result.decided.forEach((d) => done.add(d.reviewId));
        done.add(reviewId);
      }
    };
  };

  /** Asks first when a decision reaches lines the reviewer did not pick. */
  const confirmReach = (reach: HeldLine[], key: 'pullIn' | 'cascade', go: () => void, foreignCount = 0) => {
    if (reach.length === 0 && foreignCount === 0) {
      go();
      return;
    }
    modal.confirm({
      title: t(`review.${key}Title`),
      content: (
        <div data-testid={`review-${key}`}>
          <Typography.Paragraph>{t(`review.${key}Explain`)}</Typography.Paragraph>
          <ul>
            {reach.map((l) => (
              <li key={l.reviewId}>{nameOf(l)}</li>
            ))}
          </ul>
          {foreignCount > 0 && (
            <Typography.Paragraph type="secondary" data-testid="review-reach-other">
              {t('review.pullInOther')}
            </Typography.Paragraph>
          )}
        </div>
      ),
      okText: t(key === 'pullIn' ? 'review.accept' : 'review.reject'),
      cancelText: t('common.cancel'),
      onOk: go,
    });
  };

  const unique = (reach: HeldLine[], exclude: ReadonlySet<string>) => {
    const seen = new Set<string>();
    return reach.filter((l) => !exclude.has(l.reviewId) && !seen.has(l.reviewId) && seen.add(l.reviewId));
  };

  const acceptSelection = (ids: string[]) => {
    const picked = new Set(ids);
    const reach = unique(ids.flatMap((i) => dependenciesOf(i, lines)), picked);
    // Dependencies first: a line is decided after what it needs, so none is refused for it.
    const ordered = [...ids].sort((a, b) => dependenciesOf(a, lines).length - dependenciesOf(b, lines).length);
    const foreign = new Set(
      [...ids, ...reach.map((l) => l.reviewId)].flatMap((i) => foreignDependenciesOf(i, lines)),
    );
    confirmReach(reach, 'pullIn', () => void decide(acceptLines(ordered)), foreign.size);
  };

  const rejectSelection = (ids: string[]) => {
    const picked = new Set(ids);
    const reach = unique(ids.flatMap((i) => dependentsOf(i, lines)), picked);
    confirmReach(reach, 'cascade', () => void decide(rejectLines(ids)));
  };

  const acceptAll = () =>
    void decide(async (over) => {
      try {
        await acceptBatch.mutateAsync({ id: batch.id, note, overCurrent: over.has(WHOLE_BATCH) });
      } catch (error) {
        const changed = changedSinceIds(error);
        throw changed ? new ChangedSinceStop(WHOLE_BATCH, changed, 0) : error;
      }
    });

  const rejectAll = () =>
    modal.confirm({
      title: t('review.rejectAllTitle'),
      content: t('review.rejectAllExplain'),
      okText: t('review.rejectAll'),
      okButtonProps: { danger: true },
      cancelText: t('common.cancel'),
      onOk: () =>
        void decide(async () => {
          await rejectBatch.mutateAsync({ id: batch.id, note });
        }),
    });

  const selectedIds = [...selected].filter((i) => pendingLines.some((l) => l.reviewId === i));
  const setBulk = (ids: string[]) => setSelected(new Set(ids));
  const bySection = (section: string) =>
    pendingLines.filter((l) => (l.section ?? '') === section).map((l) => l.reviewId);
  const sectionNames = [...new Set(pendingLines.map((l) => l.section ?? ''))];

  const conflictLines = conflict ? lines.filter((l) => conflict.ids.includes(l.reviewId)) : [];

  return (
    <div style={{ padding: 24, maxWidth: 1100 }} data-testid="review-batch">
      <Link to={isOwn ? '/geodata?tab=batches' : '/review'}>
        <ArrowLeftOutlined /> {t('review.backToQueue')}
      </Link>
      <Typography.Title level={3} style={{ marginTop: 8 }}>
        {t('review.batchTitle')}
      </Typography.Title>
      <Flex gap={16} wrap style={{ marginBottom: 12 }} data-testid="review-batch-facts">
        <span>
          {t('review.uploader')}: <strong>{batch.uploaderName ?? t('review.unknownUploader')}</strong>
        </span>
        <span>
          {t('review.heldAt')}: {batch.heldAt ? new Date(batch.heldAt).toLocaleString(i18n.resolvedLanguage) : '-'}
        </span>
        <span>
          {t('review.source.label')}: {sourceLabel(t, batch.source)}
        </span>
        <span>
          {t('review.state.label')}: <Tag data-testid="review-batch-state">{t(`review.state.${batch.reviewState}`)}</Tag>
        </span>
        {batch.heldBytes > 0 && (
          <span>
            {t('review.heldBytes')}: {formatSize(batch.heldBytes)}
          </span>
        )}
        {batch.decidedByName && (
          <span>
            {t('review.decidedBy')}: {batch.decidedByName}
          </span>
        )}
      </Flex>
      <SectionTags sections={batch.sections} />
      {batch.reviewNote && (
        <Alert type="info" showIcon style={{ marginTop: 12 }} title={t('review.batchNote', { note: batch.reviewNote })} />
      )}
      {isOwn && <Alert type="info" showIcon style={{ marginTop: 12 }} title={t('review.ownBatch')} data-testid="review-own" />}
      {!isOwn && !mayWrite && (
        <Alert type="info" showIcon style={{ marginTop: 12 }} title={t('review.readOnly')} data-testid="review-readonly" />
      )}
      {!isOwn && (
        // A reviewer is shown every held change whole, whatever they may read elsewhere.
        <Alert type="warning" showIcon style={{ marginTop: 12 }} title={t('review.wholeView')} data-testid="review-whole-view" />
      )}

      {canDecide && pendingLines.length > 0 && (
        <Card size="small" style={{ marginTop: 16 }} data-testid="review-controls">
          <Flex vertical gap={12}>
            <Flex gap={8} wrap align="center">
              <Button type="primary" icon={<CheckOutlined />} loading={working} onClick={acceptAll} data-testid="review-accept-all">
                {t('review.acceptAll')}
              </Button>
              <Button danger icon={<CloseOutlined />} disabled={working} onClick={rejectAll} data-testid="review-reject-all">
                {t('review.rejectAll')}
              </Button>
              <Dropdown
                trigger={['click']}
                menu={{
                  items: [
                    { key: 'all', label: t('review.selectAllWaiting') },
                    ...sectionNames.map((s) => ({
                      key: `section:${s}`,
                      label: t('review.selectKind', {
                        kind: t(`review.section.${s || 'none'}`, { defaultValue: s || t('review.section.none') }),
                      }),
                    })),
                    { key: 'none', label: t('review.selectNone') },
                  ],
                  onClick: ({ key }) => {
                    if (key === 'all') {
                      setBulk(pendingLines.map((l) => l.reviewId));
                    } else if (key === 'none') {
                      setBulk([]);
                    } else {
                      setBulk(bySection(key.slice('section:'.length)));
                    }
                  },
                }}
              >
                <Button data-testid="review-select-menu">
                  {t('review.select')} <DownOutlined />
                </Button>
              </Dropdown>
              <Button
                type="primary"
                ghost
                disabled={selectedIds.length === 0 || working}
                onClick={() => acceptSelection(selectedIds)}
                data-testid="review-accept-selected"
              >
                {t('review.acceptSelected', { count: selectedIds.length })}
              </Button>
              <Button
                danger
                disabled={selectedIds.length === 0 || working}
                onClick={() => rejectSelection(selectedIds)}
                data-testid="review-reject-selected"
              >
                {t('review.rejectSelected', { count: selectedIds.length })}
              </Button>
            </Flex>
            <Typography.Text type="secondary" data-testid="review-accept-all-whole">
              {t('review.acceptAllWhole')}
            </Typography.Text>
            <div>
              <Typography.Text type="secondary">{t('review.noteLabel')}</Typography.Text>
              <Input.TextArea
                value={note}
                maxLength={2000}
                rows={2}
                onChange={(e) => setNote(e.target.value)}
                placeholder={t('review.notePlaceholder')}
                data-testid="review-note"
              />
            </div>
          </Flex>
        </Card>
      )}

      <Flex justify="space-between" align="center" style={{ marginTop: 16 }}>
        <Typography.Text type="secondary">
          {t('review.waitingOfTotal', { waiting: pendingLines.length, total: lines.length })}
        </Typography.Text>
        <Checkbox checked={showDecided} onChange={(e) => setShowDecided(e.target.checked)} data-testid="review-show-decided">
          {t('review.showDecided')}
        </Checkbox>
      </Flex>

      {groups.length === 0 && <Empty description={t('review.noLines')} style={{ marginTop: 24 }} />}
      {groups.map((group) => {
        const waiting = group.lines.filter((l) => l.decision === 'pending').map((l) => l.reviewId);
        const allPicked = waiting.length > 0 && waiting.every((i) => selected.has(i));
        return (
          <Card
            key={group.caveId ?? 'other'}
            size="small"
            style={{ marginTop: 12 }}
            data-testid="review-group"
            title={
              <Flex gap={8} align="center">
                {canDecide && waiting.length > 0 && (
                  <Checkbox
                    checked={allPicked}
                    indeterminate={!allPicked && waiting.some((i) => selected.has(i))}
                    onChange={(e) => {
                      const next = new Set(selected);
                      waiting.forEach((i) => (e.target.checked ? next.add(i) : next.delete(i)));
                      setSelected(next);
                    }}
                    aria-label={t('review.selectGroup')}
                    data-testid="review-group-select"
                  />
                )}
                <GroupTitle caveId={group.caveId} lines={lines} names={names} />
              </Flex>
            }
          >
            {group.lines.map((line) => (
              <ReviewLineRow
                key={line.reviewId}
                line={line}
                lines={lines}
                names={names}
                canDecide={canDecide}
                selected={selected.has(line.reviewId)}
                onSelect={(checked) => {
                  const next = new Set(selected);
                  if (checked) {
                    next.add(line.reviewId);
                  } else {
                    next.delete(line.reviewId);
                  }
                  setSelected(next);
                }}
                chosen={choices[line.reviewId]}
                onChoose={(fields) => setChoices((prev) => ({ ...prev, [line.reviewId]: fields }))}
                onAccept={() => acceptSelection([line.reviewId])}
                onReject={() => rejectSelection([line.reviewId])}
                busy={working}
              />
            ))}
          </Card>
        );
      })}

      <Modal
        open={conflict !== null}
        title={t('review.changedSinceTitle')}
        onCancel={() => setConflict(null)}
        destroyOnHidden
        footer={[
          <Button key="cancel" onClick={() => setConflict(null)}>
            {t('common.cancel')}
          </Button>,
          <Button
            key="reject"
            danger
            disabled={working}
            data-testid="review-conflict-reject"
            onClick={() => {
              const ids = conflictLines.map((l) => l.reviewId);
              const rest = conflict?.rest ?? 0;
              setConflict(null);
              void decide(rejectLines(ids)).then(() => {
                if (rest > 0) {
                  message.warning(t('review.stoppedRest', { count: rest }));
                }
              });
            }}
          >
            {t('review.rejectThese')}
          </Button>,
          <Button key="over" type="primary" loading={working} data-testid="review-conflict-over" onClick={() => void conflict?.writeOver()}>
            {t('review.writeOver')}
          </Button>,
        ]}
      >
        <div data-testid="review-conflict">
          <Typography.Paragraph>{t('review.changedSinceExplain')}</Typography.Paragraph>
          <ul>
            {conflictLines.map((l) => (
              <li key={l.reviewId}>{nameOf(l)}</li>
            ))}
          </ul>
        </div>
      </Modal>
    </div>
  );
}
