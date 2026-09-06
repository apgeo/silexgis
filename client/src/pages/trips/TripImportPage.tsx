// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { ArrowLeftOutlined, InboxOutlined } from '@ant-design/icons';
import { Alert, App, Button, Flex, Input, Space, Spin, Tag, Typography, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import {
  useCommitTripImport,
  useDocument,
  useSaveTripImportSession,
  useTripImportColumns,
  useTripImportPreview,
  useTripImportSession,
  useUploadFile,
  type TripImportCommitResult,
  type TripImportDecision,
  type TripImportOptions,
  type TripImportPreviewRequest,
} from '../../api/hooks.ts';
import TripImportOptionsPanel from '../../components/import/TripImportOptionsPanel.tsx';
import TripImportProblemList from '../../components/import/TripImportProblemList.tsx';
import TripImportResultModal from '../../components/import/TripImportResultModal.tsx';
import TripImportRowTable from '../../components/import/TripImportRowTable.tsx';
import TripImportSummary from '../../components/import/TripImportSummary.tsx';
import TripImportTypeTable from '../../components/import/TripImportTypeTable.tsx';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

/** What the review starts from before the server's session has arrived. */
const BLANK_OPTIONS: TripImportOptions = {
  delimiter: ',',
  multiValueSeparators: ';,',
  slashSeparatedFields: [],
  dateOrder: 'dayFirst',
  columns: {},
  visibility: 'private',
  cavingGroupId: null,
  createMissingCaves: false,
  createMissingAreas: false,
  createMissingCavers: false,
  createMissingTripTypes: false,
};

/**
 * The refusal a duplicate upload answers with names the document that already holds the bytes,
 * and the identifier is what leads the reader back to the review they already have rather than
 * being told to go and find it.
 *
 * Read from the refusal's own member and never from its detail sentence. Only the code and the
 * members are a contract; the detail is prose written for a person, and recovering an identifier
 * by matching it out of that prose turns a rewording — or a translation — into a silent failure
 * that tells the reader their upload failed instead of offering them the review they already have.
 */
function duplicateDocumentId(error: unknown): string | null {
  if (!(error instanceof ApiError) || error.code !== 'file.duplicate') {
    return null;
  }
  return error.member('duplicateOfDocumentId') ?? null;
}

/**
 * Reads a club's trip spreadsheet into trips: a single screen holding the choices that apply to
 * the whole file, the rows the reader made of it, everything it could not read, and what
 * confirming would add — with nothing written until the button at the bottom.
 *
 * The review is the server's, not the browser's. Options and per-row decisions are saved as the
 * reviewer works, so a closed tab costs nothing and fifteen years of trips can be gone through
 * over several sittings.
 */
export default function TripImportPage() {
  const { t } = useTranslation();
  const { fileId } = useParams<{ fileId: string }>();
  const navigate = useNavigate();
  const { message } = App.useApp();

  const session = useTripImportSession(fileId);
  const saveSession = useSaveTripImportSession();
  const commit = useCommitTripImport();
  const upload = useUploadFile();

  const [options, setOptions] = useState<TripImportOptions>(BLANK_OPTIONS);
  const [decisions, setDecisions] = useState<Record<string, TripImportDecision>>({});
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [result, setResult] = useState<TripImportCommitResult | null>(null);
  const [duplicateOf, setDuplicateOf] = useState<string | null>(null);
  const loaded = useRef(false);

  // The saved review arrives once and then the browser owns it: refetching over the top would
  // undo whatever the reviewer changed while the request was in flight.
  useEffect(() => {
    if (session.data && !loaded.current) {
      loaded.current = true;
      setOptions(session.data.options);
      setDecisions({ ...session.data.decisions });
    }
  }, [session.data]);

  const debouncedSearch = useDebouncedValue(search);
  const previewRequest = useMemo<TripImportPreviewRequest>(
    () => ({ options, page, pageSize, search: debouncedSearch || null }),
    [options, page, pageSize, debouncedSearch],
  );
  const preview = useTripImportPreview(fileId, previewRequest, loaded.current);
  // The header read straight from the stored file. It is what the mapping controls fall back on
  // when the preview has no answer — a parse that failed, a file too large, a refusal — which is
  // precisely the moment somebody needs to re-point a column and the preview cannot say what the
  // columns are. Without it the screen asked them to type header names from memory.
  const columns = useTripImportColumns(fileId);

  // Autosave, debounced through the serialised value so that a reviewer setting aside twenty
  // rows sends one request rather than twenty. The saved decisions are also what the next
  // preview reads: a skip is not reflected in the counts until it has been written.
  const pending = useDebouncedValue(JSON.stringify({ options, decisions }), 800);
  useEffect(() => {
    if (!fileId || !loaded.current) {
      return;
    }
    const body = JSON.parse(pending) as {
      options: TripImportOptions;
      decisions: Record<string, TripImportDecision>;
    };
    saveSession.mutate({ fileId, body });
    // saveSession is a stable mutation object; including it would resend on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pending, fileId]);

  const setDecision = (line: number, decision: TripImportDecision | null) =>
    setDecisions((current) => {
      const next = { ...current };
      if (decision === null) {
        delete next[String(line)];
      } else {
        next[String(line)] = decision;
      }
      return next;
    });

  /** The same decision for many lines at once, written in one state update rather than N. */
  const setBulkDecision = (lines: readonly number[], action: TripImportDecision['action']) =>
    setDecisions((current) => {
      const next = { ...current };
      for (const line of lines) {
        next[String(line)] = { action };
      }
      return next;
    });

  const failed = (error: unknown) => {
    const code = error instanceof ApiError ? error.code : undefined;
    switch (code) {
      case 'file.not_found':
        message.error(t('tripImport.errors.fileNotFound'));
        return;
      case 'access.create_forbidden':
        message.error(t('tripImport.errors.createForbidden'));
        return;
      case 'access.caving_group_binding_forbidden':
        message.error(t('tripImport.errors.groupBindingForbidden'));
        return;
      case 'trip_import.file_unreadable':
        message.error(t('tripImport.errors.fileUnreadable'));
        return;
      case 'trip_import.file_too_large':
        message.error(t('tripImport.errors.fileTooLarge'));
        return;
      case 'trip_import.selection_empty':
        message.error(t('tripImport.errors.selectionEmpty'));
        return;
      case 'trip_import.selection_too_large':
        message.error(t('tripImport.errors.selectionTooLarge'));
        return;
      case 'trip_import.nothing_created':
        message.error(t('tripImport.errors.nothingCreated'));
        return;
      default:
        message.error(t('tripImport.errors.commitFailed'));
    }
  };

  const uploadSheet = async (file: File, allowDuplicate: boolean) => {
    try {
      const created = await upload.mutateAsync({ file, allowDuplicate });
      setDuplicateOf(null);
      navigate(`/trip-logs/import/${created.id}`);
    } catch (error) {
      const documentId = duplicateDocumentId(error);
      if (documentId) {
        setDuplicateOf(documentId);
        return;
      }
      message.error(t('tripImport.errors.uploadFailed'));
    }
  };

  if (!fileId) {
    return <UploadArm duplicateOf={duplicateOf} onUpload={uploadSheet} pending={upload.isPending} />;
  }

  const previewError = preview.error;
  const data = preview.data;
  const filteredLines = data?.filteredLines ?? [];
  const selectableLines = new Set(data?.selectableLines ?? []);

  // What confirming would attempt. The server's answer decides which rows can be taken at all;
  // a decision the reviewer has made but the autosave has not yet written is applied over it, so
  // the count on the button never lags a switch that has just been flipped.
  const takenLines = filteredLines.filter((line) => {
    const local = decisions[String(line)]?.action;
    return local === undefined ? selectableLines.has(line) : local !== 'skip';
  });

  // Counted here rather than read off the preview. The server's figure is worked out from the
  // decisions it has stored, and the preview is neither keyed on those decisions nor refetched
  // when the autosave writes them — so the server's answer sat at zero while a reviewer set forty
  // rows aside, and only caught up if they happened to change an option or turn a page. The same
  // local decisions that decide the button's count decide this one, so the two always agree.
  const skippedRowCount = filteredLines.filter((line) => {
    const local = decisions[String(line)]?.action;
    return local === undefined ? !selectableLines.has(line) : local === 'skip';
  }).length;

  const onCommit = async () => {
    try {
      const outcome = await commit.mutateAsync({ fileId, body: { options, lines: takenLines, decisions } });
      setResult(outcome);
      // The rows that were created are set aside, and only they. Clearing the decisions instead
      // left the whole selection standing: the button recomputed itself from the preview and read
      // "Create 2 trips" again over rows that had just been created, so closing the modal and
      // pressing it once more wrote a second batch of the same trips — reversible only as a
      // separate act of its own. The rows that failed stay taken, because those are the ones
      // somebody may want to fix an option for and try again.
      const failedLines = new Set(outcome.failures.map((failure) => failure.line));
      setBulkDecision(
        takenLines.filter((line) => !failedLines.has(line)),
        'skip',
      );
    } catch (error) {
      failed(error);
    }
  };

  if (session.isError) {
    return (
      <div style={{ padding: 24 }}>
        {/* Two different refusals reach here and they lead to different places. A caller who may
            not create trips is refused the session outright, and telling them the sheet is
            missing sends them looking for a file that is sitting exactly where they left it. */}
        <Alert
          type="error"
          showIcon
          title={
            session.error instanceof ApiError && session.error.code === 'access.create_forbidden'
              ? t('tripImport.errors.createForbidden')
              : t('tripImport.errors.fileNotFound')
          }
        />
      </div>
    );
  }

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }} gap={12} wrap>
        <Space>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/trip-logs')}>
            {t('common.back')}
          </Button>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {t('tripImport.title')}
          </Typography.Title>
          {session.data && <Tag>{session.data.fileName}</Tag>}
        </Space>
        {/* Dead once a confirmation has succeeded, until the reviewer closes the result. The
            selection is recomputed from the preview the moment the modal appears, so a button
            left live reads "Create 2 trips" again over rows that have just been created —
            and a reviewer who dismisses the modal and clicks once more gets a second batch of
            the same trips, undoable only as a separate act. */}
        <Button
          type="primary"
          disabled={takenLines.length === 0 || result !== null}
          loading={commit.isPending}
          onClick={() => void onCommit()}
          data-testid="trip-import-commit"
        >
          {t('tripImport.createSelected', { count: takenLines.length })}
        </Button>
      </Flex>

      {previewError && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="trip-import-preview-error"
          title={t('tripImport.errors.previewFailed')}
          description={previewError instanceof ApiError ? previewError.detail : undefined}
        />
      )}

      {/* A shortened list reads exactly like a complete one, so a sheet longer than one review
          reads says so rather than quietly showing the part that fitted. */}
      {data?.truncated && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="trip-import-truncated"
          title={t('tripImport.truncatedTitle')}
          description={t('tripImport.truncated', { scanned: data.rowCount })}
        />
      )}

      <Flex gap={16} align="start" wrap>
        <Flex vertical gap={16} style={{ flex: '1 1 640px', minWidth: 420 }}>
          <TripImportOptionsPanel
            options={options}
            onChange={setOptions}
            // The preview's header when there is one, the file's own when there is not. The
            // fallback is the whole reason the columns read exists: it answers from the stored
            // file, so a mapping can still be re-pointed after the parse it would fix has failed.
            header={data?.header ?? columns.data?.header ?? []}
            dateOrderSource={data?.dateOrderSource}
            effectiveDateOrder={data?.dateOrder}
            ambiguousDateRows={data?.ambiguousDateRows ?? 0}
            unmappedColumns={data?.unmappedColumns ?? columns.data?.unmappedColumns ?? []}
          />

          <Flex gap={12} align="center" wrap>
            <Input.Search
              allowClear
              style={{ maxWidth: 320 }}
              placeholder={t('tripImport.searchPlaceholder')}
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
              }}
              data-testid="trip-import-search"
            />
            <Typography.Text type="secondary" data-testid="trip-import-counts">
              {t('tripImport.rowCounts', {
                readable: data?.readableRowCount ?? 0,
                failed: data?.failedRowCount ?? 0,
                skipped: skippedRowCount,
              })}
            </Typography.Text>
          </Flex>

          <Spin spinning={preview.isFetching && !data}>
            <TripImportRowTable
              items={data?.items ?? []}
              decisions={decisions}
              onDecision={setDecision}
              selectableLines={data?.selectableLines ?? []}
              onBulkDecision={setBulkDecision}
              page={data?.page ?? 1}
              pageSize={data?.pageSize ?? pageSize}
              total={data?.totalItems ?? 0}
              onPageChange={(nextPage, nextSize) => {
                setPage(nextPage);
                setPageSize(nextSize);
              }}
            />
          </Spin>
        </Flex>

        <Flex vertical gap={16} style={{ flex: '1 1 380px', minWidth: 320 }}>
          {data && (
            <TripImportSummary proposals={data.proposals} options={options} onChange={setOptions} />
          )}
          {/* One line per distinct type word rather than one per trip. A vocabulary this import
              grows is installation-wide and is not removed by taking the import back, so what
              the sheet would add to it is reviewed as its own list and not as a single figure. */}
          {data && (
            <TripImportTypeTable
              types={data.proposals.tripTypes}
              options={options}
              onChange={setOptions}
            />
          )}
          <TripImportProblemList
            problems={data?.problems ?? []}
            failedRowCount={data?.failedRowCount ?? 0}
          />
        </Flex>
      </Flex>

      <TripImportResultModal result={result} onClose={() => setResult(null)} />
    </div>
  );
}

/**
 * Where a sheet arrives. Separate from the review because the review needs a file to be about,
 * and a screen that pretends to review nothing is worse than one that asks for the file first.
 */
function UploadArm({
  duplicateOf,
  onUpload,
  pending,
}: {
  duplicateOf: string | null;
  onUpload: (file: File, allowDuplicate: boolean) => Promise<void>;
  pending: boolean;
}) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [held, setHeld] = useState<File | null>(null);
  // The refusal names a document; the review is about the file inside it. Reading the document
  // is how one becomes the other, and it is why the duplicate is an offer rather than a wall.
  const existing = useDocument(duplicateOf ?? undefined);

  return (
    <div style={{ padding: 24 }}>
      <Flex align="center" gap={12} style={{ marginBottom: 16 }} wrap>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/trip-logs')}>
          {t('common.back')}
        </Button>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('tripImport.uploadTitle')}
        </Typography.Title>
      </Flex>

      {duplicateOf && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="trip-import-duplicate"
          title={t('tripImport.duplicateTitle')}
          description={
            <>
              <Typography.Paragraph style={{ marginBottom: 8 }}>
                {t('tripImport.duplicateBody', { name: existing.data?.title ?? '' })}
              </Typography.Paragraph>
              <Space wrap>
                <Button
                  type="primary"
                  disabled={!existing.data}
                  onClick={() => navigate(`/trip-logs/import/${existing.data!.currentFileId}`)}
                  data-testid="trip-import-duplicate-resume"
                >
                  {t('tripImport.duplicateResume')}
                </Button>
                <Button
                  loading={pending}
                  disabled={!held}
                  onClick={() => held && void onUpload(held, true)}
                  data-testid="trip-import-duplicate-again"
                >
                  {t('tripImport.duplicateUploadAgain')}
                </Button>
              </Space>
            </>
          }
        />
      )}

      <Upload.Dragger
        accept=".csv,.tsv,.txt"
        showUploadList={false}
        disabled={pending}
        customRequest={({ file, onSuccess, onError }) => {
          const chosen = file as File;
          setHeld(chosen);
          void onUpload(chosen, false)
            .then(() => onSuccess?.({}))
            .catch((e: Error) => onError?.(e));
        }}
      >
        <p className="ant-upload-drag-icon">
          <InboxOutlined />
        </p>
        <p className="ant-upload-text">{t('tripImport.uploadPrompt')}</p>
        <p className="ant-upload-hint">{t('tripImport.uploadHint')}</p>
      </Upload.Dragger>
    </div>
  );
}
