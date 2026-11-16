// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { App, Alert, Card, Flex, Space, Spin, Tag, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import {
  useCommitImport,
  useEffectiveTermRuleSet,
  useGeofile,
  useImportPreview,
  useImportSession,
  useSaveImportSession,
  type ImportCandidate,
  type ImportDecision,
  type ImportOptions,
  type ImportPreviewRequest,
  useProcessingJob,
  useImportBatch,
} from '../../api/hooks.ts';
import CandidateMap from '../../components/import/CandidateMap.tsx';
import CandidateTable from '../../components/import/CandidateTable.tsx';
import ImportOptionsPanel from '../../components/import/ImportOptionsPanel.tsx';
import ImportResultModal from '../../components/import/ImportResultModal.tsx';
import RuleHitList from '../../components/import/RuleHitList.tsx';
import SelectionToolbar, { type CandidateFilters } from '../../components/import/SelectionToolbar.tsx';
import { refusalText } from '../../components/geodata/refusalText.ts';
import { ApiError } from '../../api/client.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { useSavedReview } from './useSavedReview.ts';

/** What the review starts from before the server's session has arrived. */
const BLANK_OPTIONS: ImportOptions = {
  languages: [],
  mapping: {},
  elevation: 'keep',
  tracks: 'ignore',
  visibility: 'private',
  locationProtected: false,
  tagIds: [],
  duplicateRadiusMeters: 50,
};

/**
 * The staged-import workspace: a file's candidates as a table beside a map, with what the
 * rules propose already filled in and nothing committed until the button at the bottom.
 *
 * The review is the server's, not the browser's — options and per-row decisions are saved as
 * the reviewer works, so a closed tab costs nothing and four hundred waypoints can be gone
 * through over two sittings.
 */
export default function ImportWorkspacePage() {
  const { t } = useTranslation();
  const { geofileId } = useParams<{ geofileId: string }>();
  const navigate = useNavigate();
  const { message } = App.useApp();

  const session = useImportSession(geofileId);
  const effective = useEffectiveTermRuleSet();
  const saveSession = useSaveImportSession();
  const commit = useCommitImport();

  const [options, setOptions] = useState<ImportOptions>(BLANK_OPTIONS);
  const [decisions, setDecisions] = useState<Record<string, ImportDecision>>({});
  const [selected, setSelected] = useState<ReadonlySet<number>>(new Set());
  const [focused, setFocused] = useState<number | null>(null);
  const [filters, setFilters] = useState<CandidateFilters>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  // What was confirmed and is now being created, until the job that creates it settles. The
  // objects do not exist yet, so there is nothing to show but progress.
  const [queuedCommit, setQueuedCommit] =
    useState<{ jobId: number; batchId: string; queued: number } | null>(null);
  const job = useProcessingJob(queuedCommit?.jobId);
  const committedBatch = useImportBatch(
    job.data && job.data.status === 'succeeded' ? queuedCommit?.batchId : undefined);

  const review = useSavedReview({
    saved: session.data,
    apply: (saved) => {
      setOptions(saved.options);
      setDecisions({ ...saved.decisions });
    },
    body: useMemo(() => ({ options, decisions }), [options, decisions]),
    save: (body) => {
      if (geofileId) {
        saveSession.mutate({ geofileId, body });
      }
    },
  });

  // A set with no rules chosen starts from the one this account's imports start from.
  useEffect(() => {
    if (review.loaded && !options.termRuleSetId && effective.data?.set) {
      setOptions((current) => ({ ...current, termRuleSetId: effective.data!.set!.set.id }));
    }
  }, [review.loaded, effective.data, options.termRuleSetId]);

  const previewRequest = useMemo<ImportPreviewRequest>(
    () => ({
      options,
      page,
      pageSize,
      rule: filters.rule ?? null,
      kind: filters.kind ?? null,
      geometry: filters.geometry ?? null,
      search: filters.search || null,
      hasDuplicate: filters.hasDuplicate ?? null,
    }),
    [options, page, pageSize, filters.rule, filters.kind, filters.geometry, filters.hasDuplicate, filters.search],
  );
  // The dry run reads the whole file, so it is asked for once the reviewer pauses — not on every
  // letter of a name prefix or every step of the radius slider. The application count travels
  // with it, so nothing is asked for until the saved review is in hand.
  const settled = useDebouncedValue(
    useMemo(() => ({ hydration: review.hydration, request: previewRequest }), [review.hydration, previewRequest]),
  );
  const preview = useImportPreview(
    geofileId,
    settled.request,
    review.loaded && settled.hydration === review.hydration,
  );

  const geofile = useGeofile(geofileId).data;

  // What the table shows is the answer for the settings on screen or nothing: a refused dry run
  // must not leave the previous one's rows, counts and selection looking current.
  const answer = preview.isError ? undefined : preview.data;
  const items: ImportCandidate[] = answer?.items ?? [];
  const filteredIds = answer?.filteredSourceIds ?? [];

  // A row the settings no longer let anything be made of drops out of the selection, so the
  // button counts what would be sent. Only rows the current filter covers are judged: one ticked
  // under another filter is not in this answer at all, and that says nothing about it.
  useEffect(() => {
    if (!answer || preview.isPlaceholderData) {
      return;
    }
    const selectable = new Set(answer.selectableSourceIds);
    const stale = answer.filteredSourceIds.filter((id) => !selectable.has(id));
    if (stale.length === 0) {
      return;
    }
    setSelected((current) => {
      if (!stale.some((id) => current.has(id))) {
        return current;
      }
      const next = new Set(current);
      for (const id of stale) {
        next.delete(id);
      }
      return next;
    });
  }, [answer, preview.isPlaceholderData]);

  const setDecision = (sourceId: number, decision: ImportDecision | null) =>
    setDecisions((current) => {
      const next = { ...current };
      if (decision === null) {
        delete next[String(sourceId)];
      } else {
        next[String(sourceId)] = decision;
      }
      return next;
    });

  const onCommit = async (withoutReview: boolean) => {
    if (!geofileId) {
      return;
    }
    try {
      const outcome = await commit.mutateAsync({
        geofileId,
        body: {
          options,
          selection: withoutReview ? [] : [...selected],
          decisions,
          withoutReview,
        },
      });
      // An account under review has nothing created by its confirmation: it waits for a
      // reviewer, so there is no work to follow.
      if (outcome.held || outcome.jobId == null) {
        message.info(t('importReview.held'));
      } else {
        setQueuedCommit({ jobId: outcome.jobId, batchId: outcome.batchId, queued: outcome.queued });
      }
      setSelected(new Set());
      setDecisions({});
      review.restart();
    } catch (error) {
      message.error(refusalText(error, t, 'vectorImport.commitFailed'));
    }
  };

  if (session.isError) {
    const gone = session.error instanceof ApiError && session.error.status === 404;
    return (
      <div style={{ padding: 24 }}>
        <Alert
          type="error"
          showIcon
          title={gone ? t('vectorImport.fileNotFound') : refusalText(session.error, t, 'vectorImport.sessionFailed')}
          action={
            gone ? undefined : (
              <Button size="small" onClick={() => void session.refetch()}>
                {t('common.retry')}
              </Button>
            )
          }
          data-testid="import-session-error"
        />
      </div>
    );
  }

  const selectable = items.length > 0;
  const truncated = answer?.truncated ?? false;

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }} gap={12} wrap>
        <Space>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/geodata')}>
            {t('common.back')}
          </Button>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {t('vectorImport.title')}
          </Typography.Title>
          {geofile && <Tag>{geofile.name}</Tag>}
        </Space>
        <Space>
          {session.data?.allowCreateWithoutReview && (
            <Button
              onClick={() => void onCommit(true)}
              loading={commit.isPending}
              data-testid="import-commit-all"
            >
              {t('vectorImport.createWithoutReview')}
            </Button>
          )}
          <Button
            type="primary"
            disabled={selected.size === 0 || preview.isError}
            loading={commit.isPending}
            onClick={() => void onCommit(false)}
            data-testid="import-commit"
          >
            {t('vectorImport.createSelected', { count: selected.size })}
          </Button>
        </Space>
      </Flex>

      {preview.isError && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('vectorImport.previewFailed')}
          description={refusalText(preview.error, t, 'vectorImport.previewFailed')}
          action={
            <Button size="small" onClick={() => void preview.refetch()}>
              {t('common.retry')}
            </Button>
          }
          data-testid="import-preview-error"
        />
      )}

      {truncated && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('vectorImport.truncatedTitle')}
          description={t('vectorImport.truncated', {
            scanned: answer?.scannedRowCount ?? 0,
            total: answer?.fileRowCount ?? 0,
          })}
        />
      )}

      <Flex gap={16} align="start" wrap>
        <Flex vertical gap={16} style={{ flex: '1 1 640px', minWidth: 420 }}>
          <ImportOptionsPanel
            options={options}
            onChange={setOptions}
            geofileId={geofileId}
            isDelimited={geofile?.format === 'csv'}
          />

          <SelectionToolbar
            filters={filters}
            onFiltersChange={(next) => {
              setFilters(next);
              setPage(1);
            }}
            ruleHits={answer?.ruleHits ?? []}
            selected={selected}
            onSelectedChange={setSelected}
            filteredIds={filteredIds}
            selectableIds={answer?.selectableSourceIds ?? []}
            disabled={!selectable}
            counts={{
              candidates: answer?.candidateCount ?? 0,
              matched: answer?.matchedCount ?? 0,
              unmatched: answer?.unmatchedCount ?? 0,
              tracks: answer?.trackCount ?? 0,
              areas: answer?.areaCount ?? 0,
            }}
          />

          {/* Over the table while the answer for the settings on screen is on its way, including
              while the previous answer is still standing in for it. */}
          <Spin spinning={preview.isFetching && (!preview.data || preview.isPlaceholderData)}>
            <CandidateTable
              items={items}
              elevationPolicy={options.elevation ?? 'keep'}
              decisions={decisions}
              selected={selected}
              onSelectedChange={setSelected}
              selectableIds={answer?.selectableSourceIds ?? []}
              onDecision={setDecision}
              onFocus={setFocused}
              page={answer?.page ?? 1}
              pageSize={answer?.pageSize ?? pageSize}
              total={answer?.totalItems ?? 0}
              onPageChange={(nextPage, nextSize) => {
                setPage(nextPage);
                setPageSize(nextSize);
              }}
            />
          </Spin>
        </Flex>

        <Flex vertical gap={16} style={{ flex: '1 1 380px', minWidth: 320, position: 'sticky', top: 16 }}>
          <Card size="small" title={t('vectorImport.mapPreview')} styles={{ body: { padding: 0 } }}>
            <CandidateMap
              candidates={items}
              selected={selected}
              focused={focused}
              onPick={(sourceId) => setFocused(sourceId)}
            />
          </Card>
          <RuleHitList
            hits={answer?.ruleHits ?? []}
            unmatched={answer?.unmatchedCount ?? 0}
            activeRule={filters.rule}
            onPick={(rule) => {
              setFilters((current) => ({ ...current, rule: current.rule === rule ? undefined : rule }));
              setPage(1);
            }}
          />
        </Flex>
      </Flex>

      <ImportResultModal
        batch={committedBatch.data?.batch ?? null}
        failures={committedBatch.data?.batch.failures ?? []}
        // Open from the moment the work is queued, not from when it finishes: a confirmation of
        // several thousand rows takes minutes, and a page that looked idle through all of it
        // invites the reader to press the button again.
        queued={queuedCommit?.queued ?? 0}
        status={job.data?.status}
        error={job.data?.error ?? null}
        // Only while it runs: a job waiting for its next turn has done nothing of that turn yet.
        progress={
          job.data?.status === 'running' && job.data.progressTotal != null && job.data.progressDone != null
            ? { done: job.data.progressDone, total: job.data.progressTotal }
            : null
        }
        // Queued again after an attempt that got under way: the wait is a retry, not the first turn.
        retrying={job.data?.status === 'queued' && job.data.attempts > 0}
        open={queuedCommit !== null}
        onClose={() => setQueuedCommit(null)}
      />
    </div>
  );
}
