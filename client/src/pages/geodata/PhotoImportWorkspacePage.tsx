// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { ArrowLeftOutlined, EnvironmentOutlined, InboxOutlined } from '@ant-design/icons';
import { App, Alert, Card, Flex, Space, Spin, Tag, Typography, Upload } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
  useCommitPhotoImport,
  usePhotoImportPreview,
  usePhotoImportSession,
  useSavePhotoImportSession,
  useUploadFile,
  type PhotoDecision,
  type PhotoImportOptions,
  type PhotoPreviewRequest,
} from '../../api/hooks.ts';
import ImportResultModal from '../../components/import/ImportResultModal.tsx';
import PhotoCandidateMap from '../../components/import/PhotoCandidateMap.tsx';
import PhotoCandidateTable from '../../components/import/PhotoCandidateTable.tsx';
import PhotoImportOptionsPanel from '../../components/import/PhotoImportOptionsPanel.tsx';
import { refusalText } from '../../components/geodata/refusalText.ts';
import { uploadErrorText } from '../../components/geodata/uploadErrorText.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { useSavedReview } from './useSavedReview.ts';

/** What a review starts from before the server's session has arrived. */
const BLANK_OPTIONS: PhotoImportOptions = {
  defaultKind: 'caveEntrance',
  clusterRadiusMeters: 25,
  proximityRadiusMeters: 80,
  visibility: 'private',
  locationProtected: false,
  tagIds: [],
  elevation: 'discard',
  cameraClockOffsetSeconds: 0,
  trackMatchToleranceSeconds: 120,
};

/**
 * The photo review: a trip's worth of photographs as places, table beside map, with nothing
 * committed until the button at the bottom.
 *
 * The review is the server's, not the browser's — which pictures are being looked at, the
 * options and the per-place decisions are saved as the reviewer works, so a closed tab costs
 * nothing and a drop can be gone through over two sittings.
 */
export default function PhotoImportWorkspacePage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [searchParams] = useSearchParams();
  const tripLogId = searchParams.get('tripLogId');

  const session = usePhotoImportSession();
  const saveSession = useSavePhotoImportSession();
  const commit = useCommitPhotoImport();
  const upload = useUploadFile();

  const [fileIds, setFileIds] = useState<string[]>([]);
  const [options, setOptions] = useState<PhotoImportOptions>(BLANK_OPTIONS);
  const [decisions, setDecisions] = useState<Record<string, PhotoDecision>>({});
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [focused, setFocused] = useState<string | null>(null);
  const [placing, setPlacing] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [uploading, setUploading] = useState(0);
  const [result, setResult] = useState<Awaited<ReturnType<typeof commit.mutateAsync>> | null>(null);

  const review = useSavedReview({
    saved: session.data,
    apply: (saved) => {
      setFileIds(saved.fileIds);
      setOptions(saved.options);
      setDecisions({ ...saved.decisions });
    },
    body: useMemo(() => ({ fileIds, options, decisions }), [fileIds, options, decisions]),
    save: (body) => saveSession.mutate(body),
  });

  // A drop opened from a trip is filed under it. Applied after the session has loaded so it
  // wins over whatever the last review was filed under, which is the point of arriving this way.
  useEffect(() => {
    if (review.loaded && tripLogId && options.tripLogId !== tripLogId) {
      setOptions((current) => ({ ...current, tripLogId }));
    }
  }, [review.loaded, tripLogId, options.tripLogId]);

  const previewRequest = useMemo<PhotoPreviewRequest>(
    () => ({
      options,
      fileIds,
      decisions,
      page,
      pageSize,
      placed: null,
      search: null,
    }),
    [options, fileIds, decisions, page, pageSize],
  );
  // Grouping a drop into places is real work on the server, and the decisions travel with it, so
  // it is asked for once the reviewer pauses rather than on every letter typed into a name or
  // every step of a radius slider. Nothing is asked for until the saved review is in hand.
  const settled = useDebouncedValue(
    useMemo(() => ({ hydration: review.hydration, request: previewRequest }), [review.hydration, previewRequest]),
  );
  const preview = usePhotoImportPreview(
    settled.request,
    review.loaded && settled.hydration === review.hydration,
  );

  // The table shows the answer for the settings on screen or nothing: a refused grouping must not
  // leave the previous one's places and counts looking current.
  const answer = preview.isError ? undefined : preview.data;

  // A place the settings no longer let be confirmed — or that a new grouping no longer has —
  // drops out of the selection, so the button counts what would be sent.
  useEffect(() => {
    if (!answer || preview.isPlaceholderData) {
      return;
    }
    const selectable = new Set(answer.selectableKeys);
    setSelected((current) =>
      [...current].every((key) => selectable.has(key))
        ? current
        : new Set([...current].filter((key) => selectable.has(key))));
  }, [answer, preview.isPlaceholderData]);

  const setDecision = (key: string, decision: PhotoDecision | null) =>
    setDecisions((current) => {
      const next = { ...current };
      if (decision === null) {
        delete next[key];
      } else {
        next[key] = decision;
      }
      return next;
    });

  /**
   * Uploads one dropped picture and adds it to the review.
   *
   * Per file rather than per drop, because the component announces a multi-file drop one file at
   * a time: a handler that read the whole accumulated list on each announcement would upload the
   * first picture three times, and one that disabled the drop zone while working would never
   * hear about the second and third at all.
   */
  const onDropped = async (file: File) => {
    setUploading((count) => count + 1);
    try {
      // A photo review is a sitting somebody may restart: dropping the same pictures again
      // is how a half-finished review is resumed, so a second copy is what was asked for.
      const stored = await upload.mutateAsync({ file, allowDuplicate: true });
      setFileIds((current) => (current.includes(stored.id) ? current : [...current, stored.id]));
    } catch (error) {
      // Per file and by name: in a drop of forty, "the pictures could not be uploaded" does not
      // say which one to drop again.
      message.error(t('photoImport.uploadFailedNamed', { name: file.name, reason: uploadErrorText(error, t) }));
    } finally {
      setUploading((count) => Math.max(0, count - 1));
    }
  };

  const onPlace = (key: string, position: [number, number]) => {
    // Written to the review, never back into the picture: the uploaded bytes are what somebody
    // sent, and rewriting them would be editing evidence.
    setDecision(key, { ...decisions[key], position });
    setPlacing(null);
  };

  const onCommit = async () => {
    try {
      const outcome = await commit.mutateAsync({
        options,
        fileIds,
        selection: [...selected],
        decisions,
      });
      // An account under review files nothing yet: the drop waits for a reviewer.
      if (outcome.batch.reviewState === 'pending') {
        message.info(t('importReview.held'));
      } else {
        setResult(outcome);
      }
      setSelected(new Set());
      setDecisions({});
      setFileIds([]);
      review.restart();
    } catch (error) {
      message.error(refusalText(error, t, 'photoImport.commitFailed'));
    }
  };

  const items = answer?.items ?? [];
  const unreadable = answer?.unreadableFileIds ?? [];

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }} gap={12} wrap>
        <Space>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/geodata')}>
            {t('common.back')}
          </Button>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {t('photoImport.title')}
          </Typography.Title>
          {options.tripLogId && <Tag color="blue">{t('photoImport.filedUnderTrip')}</Tag>}
        </Space>
        <Button
          type="primary"
          disabled={selected.size === 0 || preview.isError}
          loading={commit.isPending}
          onClick={() => void onCommit()}
          data-testid="photo-import-commit"
        >
          {t('photoImport.createSelected', { count: selected.size })}
        </Button>
      </Flex>

      {/* The identifier sits on a wrapper because the dragger does not forward it to its own
          element — and an unscoped file-input locator on this page would find whichever other
          upload control the layout happens to be holding. */}
      <div data-testid="photo-import-dropzone" style={{ marginBottom: 16 }}>
        <Upload.Dragger
          multiple
          accept="image/*,video/*"
          showUploadList={false}
          // False means the component transfers nothing itself; this page uploads each file so
          // it keeps the ids the review is built from. Called once per file, which is why the
          // upload happens here rather than off the change event's accumulated list.
          beforeUpload={(file) => {
            void onDropped(file as unknown as File);
            return false;
          }}
        >
          <p className="ant-upload-drag-icon">
            <InboxOutlined />
          </p>
          <p className="ant-upload-text">{t('photoImport.dropTitle')}</p>
          <p className="ant-upload-hint">{t('photoImport.dropHint')}</p>
        </Upload.Dragger>
      </div>

      {session.isError && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 16 }}
          title={refusalText(session.error, t, 'photoImport.sessionFailed')}
          action={
            <Button size="small" onClick={() => void session.refetch()}>
              {t('common.retry')}
            </Button>
          }
          data-testid="photo-import-session-error"
        />
      )}

      {preview.isError && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('photoImport.previewFailed')}
          description={refusalText(preview.error, t, 'photoImport.previewFailed')}
          action={
            <Button size="small" onClick={() => void preview.refetch()}>
              {t('common.retry')}
            </Button>
          }
          data-testid="photo-import-preview-error"
        />
      )}

      {uploading > 0 && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('photoImport.uploading', { count: uploading })}
        />
      )}

      {unreadable.length > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('photoImport.unreadable', { count: unreadable.length })}
        />
      )}

      {answer && answer.unplacedCount > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('photoImport.unplacedTitle', { count: answer.unplacedCount })}
          description={t('photoImport.unplacedHint')}
        />
      )}

      {placing && (
        <Alert
          type="info"
          showIcon
          icon={<EnvironmentOutlined />}
          style={{ marginBottom: 16 }}
          title={t('photoImport.placingTitle')}
          action={
            <Button size="small" onClick={() => setPlacing(null)}>
              {t('common.cancel')}
            </Button>
          }
        />
      )}

      <Flex gap={16} align="start" wrap>
        <Flex vertical gap={16} style={{ flex: '1 1 700px', minWidth: 460 }}>
          <PhotoImportOptionsPanel options={options} onChange={setOptions} preview={answer} />

          <Spin spinning={preview.isFetching && (!preview.data || preview.isPlaceholderData)}>
            <PhotoCandidateTable
              items={items}
              defaultKind={options.defaultKind}
              decisions={decisions}
              selected={selected}
              selectableKeys={answer?.selectableKeys ?? []}
              onSelectedChange={setSelected}
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
          <Card
            size="small"
            title={t('photoImport.mapPreview')}
            styles={{ body: { padding: 0 } }}
            extra={
              focused && (
                <Button size="small" onClick={() => setPlacing(placing === focused ? null : focused)}>
                  {t('photoImport.placeOnMap')}
                </Button>
              )
            }
          >
            <PhotoCandidateMap
              candidates={items}
              selected={selected}
              focused={focused}
              onPick={setFocused}
              placingKey={placing}
              onPlace={onPlace}
            />
          </Card>
        </Flex>
      </Flex>

      {/* Photographs are still created inside the request that confirms them — the ceiling is
          250, which a request carries comfortably — so this arrives already finished. */}
      <ImportResultModal
        batch={result?.batch ?? null}
        failures={result?.failures ?? []}
        queued={0}
        status="succeeded"
        error={null}
        open={result !== null}
        onClose={() => setResult(null)}
      />
    </div>
  );
}
