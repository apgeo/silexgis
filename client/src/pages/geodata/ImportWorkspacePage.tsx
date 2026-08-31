// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { App, Alert, Button, Card, Flex, Space, Spin, Tag, Typography } from 'antd';
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
} from '../../api/hooks.ts';
import CandidateMap from '../../components/import/CandidateMap.tsx';
import CandidateTable from '../../components/import/CandidateTable.tsx';
import ImportOptionsPanel from '../../components/import/ImportOptionsPanel.tsx';
import ImportResultModal from '../../components/import/ImportResultModal.tsx';
import RuleHitList from '../../components/import/RuleHitList.tsx';
import SelectionToolbar, { type CandidateFilters } from '../../components/import/SelectionToolbar.tsx';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

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
  const [result, setResult] = useState<Awaited<ReturnType<typeof commit.mutateAsync>> | null>(null);
  const loaded = useRef(false);

  // The saved review arrives once and then the browser owns it: refetching over the top would
  // undo whatever the reviewer typed while the request was in flight.
  useEffect(() => {
    if (session.data && !loaded.current) {
      loaded.current = true;
      setOptions(session.data.options);
      setDecisions({ ...session.data.decisions });
    }
  }, [session.data]);

  // A set with no rules chosen starts from the one this account's imports start from.
  useEffect(() => {
    if (loaded.current && !options.termRuleSetId && effective.data?.set) {
      setOptions((current) => ({ ...current, termRuleSetId: effective.data!.set!.set.id }));
    }
  }, [effective.data, options.termRuleSetId]);

  const search = useDebouncedValue(filters.search ?? '');
  const previewRequest = useMemo<ImportPreviewRequest>(
    () => ({
      options,
      page,
      pageSize,
      rule: filters.rule ?? null,
      kind: filters.kind ?? null,
      geometry: filters.geometry ?? null,
      search: search || null,
      hasDuplicate: filters.hasDuplicate ?? null,
    }),
    [options, page, pageSize, filters.rule, filters.kind, filters.geometry, filters.hasDuplicate, search],
  );
  const preview = useImportPreview(geofileId, previewRequest, loaded.current);

  const geofile = useGeofile(geofileId).data;

  // Autosave. Debounced through the same value the search box uses, so a reviewer ticking
  // through twenty rows sends one request rather than twenty.
  const pending = useDebouncedValue(JSON.stringify({ options, decisions }), 800);
  useEffect(() => {
    if (!geofileId || !loaded.current) {
      return;
    }
    const body = JSON.parse(pending) as { options: ImportOptions; decisions: Record<string, ImportDecision> };
    saveSession.mutate({ geofileId, body });
    // saveSession is a stable mutation object; including it would resend on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pending, geofileId]);

  const items: ImportCandidate[] = preview.data?.items ?? [];
  const filteredIds = preview.data?.filteredSourceIds ?? [];

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
      setResult(outcome);
      setSelected(new Set());
      setDecisions({});
      loaded.current = false;
    } catch {
      message.error(t('vectorImport.commitFailed'));
    }
  };

  if (session.isError) {
    return (
      <div style={{ padding: 24 }}>
        <Alert type="error" showIcon title={t('vectorImport.fileNotFound')} />
      </div>
    );
  }

  const selectable = items.length > 0;
  const truncated = preview.data?.truncated ?? false;

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
            disabled={selected.size === 0}
            loading={commit.isPending}
            onClick={() => void onCommit(false)}
            data-testid="import-commit"
          >
            {t('vectorImport.createSelected', { count: selected.size })}
          </Button>
        </Space>
      </Flex>

      {truncated && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('vectorImport.truncatedTitle')}
          description={t('vectorImport.truncated', {
            scanned: preview.data?.scannedRowCount ?? 0,
            total: preview.data?.fileRowCount ?? 0,
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
            ruleHits={preview.data?.ruleHits ?? []}
            selected={selected}
            onSelectedChange={setSelected}
            filteredIds={filteredIds}
            selectableIds={preview.data?.selectableSourceIds ?? []}
            disabled={!selectable}
            counts={{
              candidates: preview.data?.candidateCount ?? 0,
              matched: preview.data?.matchedCount ?? 0,
              unmatched: preview.data?.unmatchedCount ?? 0,
              tracks: preview.data?.trackCount ?? 0,
              areas: preview.data?.areaCount ?? 0,
            }}
          />

          <Spin spinning={preview.isFetching && !preview.data}>
            <CandidateTable
              items={items}
              elevationPolicy={options.elevation ?? 'keep'}
              decisions={decisions}
              selected={selected}
              onSelectedChange={setSelected}
              selectableIds={preview.data?.selectableSourceIds ?? []}
              onDecision={setDecision}
              onFocus={setFocused}
              page={preview.data?.page ?? 1}
              pageSize={preview.data?.pageSize ?? pageSize}
              total={preview.data?.totalItems ?? 0}
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
            hits={preview.data?.ruleHits ?? []}
            unmatched={preview.data?.unmatchedCount ?? 0}
            activeRule={filters.rule}
            onPick={(rule) => {
              setFilters((current) => ({ ...current, rule: current.rule === rule ? undefined : rule }));
              setPage(1);
            }}
          />
        </Flex>
      </Flex>

      <ImportResultModal result={result} onClose={() => setResult(null)} />
    </div>
  );
}
