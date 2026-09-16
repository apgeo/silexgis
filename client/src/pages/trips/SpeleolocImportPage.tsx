// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { ArrowLeftOutlined, InboxOutlined } from '@ant-design/icons';
import { Alert, App, Button, Flex, Space, Spin, Tag, Typography, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import {
  useCavers,
  useCommitSpeleolocImport,
  useDocument,
  useSaveSpeleolocImportSession,
  useSpeleolocImportPreview,
  useSpeleolocImportSession,
  useSpeleolocRecordings,
  useUploadFile,
  type SpeleolocImportCommitResult,
  type SpeleolocImportOptions,
  type SpeleolocImportPreviewRequest,
  type SpeleolocPoint,
  type SpeleolocPointAction,
  type SpeleolocPointDecision,
} from '../../api/hooks.ts';
import SpeleolocImportOptionsPanel from '../../components/import/SpeleolocImportOptionsPanel.tsx';
import SpeleolocImportResultModal from '../../components/import/SpeleolocImportResultModal.tsx';
import SpeleolocImportSummary, {
  type SpeleolocBlocker,
} from '../../components/import/SpeleolocImportSummary.tsx';
import SpeleolocPeoplePanel from '../../components/import/SpeleolocPeoplePanel.tsx';
import SpeleolocPointTable from '../../components/import/SpeleolocPointTable.tsx';
import {
  clampCandidateCount,
  couldBeRecorded,
} from '../../components/import/speleolocSelection.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';

/** What the review starts from before the server's session has arrived. */
const BLANK_OPTIONS: SpeleolocImportOptions = {
  tripUuid: null,
  tripLogId: null,
  createTrip: false,
  surveyModelId: null,
  cavers: {},
  cavingGroupId: null,
  visibility: 'private',
  candidateCount: 5,
};

/**
 * The refusal a duplicate upload answers with names the document that already holds the bytes, and
 * the identifier is what leads the reader back to the review they already have rather than being
 * told to go and find it.
 *
 * Read from the refusal's own member and never from its detail sentence: only the code and the
 * members are a contract, and recovering an identifier out of prose turns a rewording — or a
 * translation — into a silent failure that reports an upload problem that does not exist.
 */
function duplicateDocumentId(error: unknown): string | null {
  if (!(error instanceof ApiError) || error.code !== 'file.duplicate') {
    return null;
  }
  return error.member('duplicateOfDocumentId') ?? null;
}

/** Two references to the same upload, whichever spelling of the identifier each arrived in. */
function sameFile(left: string | undefined | null, right: string | undefined | null) {
  return Boolean(left) && Boolean(right) && left!.toLowerCase() === right!.toLowerCase();
}

/**
 * Reads a trip recorded on a phone into a tracked trip's position history — one scan at a time,
 * with nothing inferred.
 *
 * A SpeleoLoc archive is a copy of the phone's own database, and a recording inside it is a list
 * of scans of physical markers: a place, and the moment somebody passed it. A tracked trip's
 * history is made of survey stations and people. Neither translation follows from the archive, so
 * neither is made here: the station is proposed from the depth the register holds for the marker
 * and settled by the reviewer, and who a device account is has no proposal at all.
 *
 * Nothing is written until the button at the bottom. The review itself is the server's, saved as
 * the reviewer works, so a closed tab costs nothing; and a confirmation that records nothing
 * leaves the review exactly as it was, to be fixed and tried again.
 */
export default function SpeleolocImportPage() {
  const { t } = useTranslation();
  const { fileId } = useParams<{ fileId: string }>();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const isMobile = useIsMobile();

  const session = useSpeleolocImportSession(fileId);
  const saveSession = useSaveSpeleolocImportSession();
  const commit = useCommitSpeleolocImport();
  const upload = useUploadFile();
  const recordings = useSpeleolocRecordings(fileId);
  const roster = useCavers();

  const [options, setOptions] = useState<SpeleolocImportOptions>(BLANK_OPTIONS);
  const [decisions, setDecisions] = useState<Record<string, SpeleolocPointDecision>>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [result, setResult] = useState<SpeleolocImportCommitResult | null>(null);
  const [duplicateOf, setDuplicateOf] = useState<string | null>(null);
  /**
   * Which archive the review on screen is of, or null while there is none.
   *
   * State rather than a flag on a ref, and named after the archive rather than after the act of
   * loading, because this screen answers two addresses — one with an upload's identifier and one
   * without — and moving between them does not build the page again. A boolean "already loaded"
   * left the previous archive's review standing under the next archive's name, which is bad enough
   * on screen and worse underneath it: the review is saved as the reviewer works, and the save
   * replaces what is stored, so the first archive's decisions were written over the second's.
   */
  const [reviewing, setReviewing] = useState<string | null>(null);

  /**
   * Whether the stored review's contents are being kept from this reader. The saved decisions are
   * still there and the server still says so; what comes back is an empty set, which is a
   * different thing from a review with nothing in it and must never be saved as though it were.
   */
  const withheld = session.data?.positionsWithheld === true;

  // The saved review arrives once per archive and then the browser owns it: refetching over the top
  // would undo whatever the reviewer changed while the request was in flight. A review is adopted
  // only when the server says it is this archive's, so an answer still arriving for the previous
  // one cannot be taken for this one's.
  useEffect(() => {
    if (!fileId) {
      return;
    }
    if (reviewing && !sameFile(reviewing, fileId)) {
      setReviewing(null);
      setOptions(BLANK_OPTIONS);
      setDecisions({});
      setPage(1);
      setResult(null);
      setDuplicateOf(null);
      return;
    }
    if (!reviewing && session.data && sameFile(session.data.fileId, fileId)) {
      setReviewing(fileId);
      setOptions({
        ...BLANK_OPTIONS,
        ...session.data.options,
        // A stored review may name fewer offers than can be shown honestly — see the rule the
        // number is clamped by — so it is brought inside that range as it is read.
        candidateCount: clampCandidateCount(session.data.options?.candidateCount),
      });
      setDecisions({ ...session.data.decisions });
    }
  }, [fileId, reviewing, session.data]);

  const previewRequest = useMemo<SpeleolocImportPreviewRequest>(
    () => ({ options, page, pageSize }),
    [options, page, pageSize],
  );
  // Asked only once a recording has been named. The archive carries every trip the phone ever
  // recorded, so there is no such thing as "the recording" until somebody says which — and asking
  // without one is a refusal that would sit on the screen as though something had gone wrong.
  const preview = useSpeleolocImportPreview(
    fileId,
    previewRequest,
    sameFile(reviewing, fileId) && Boolean(options.tripUuid),
  );

  // Autosave, debounced through the serialised value so that a reviewer setting aside twenty scans
  // sends one request rather than twenty. What is saved is also what the next preview reads: the
  // server works out which scans may be taken from the decisions it has stored.
  //
  // Two things it may not do. It may not save under an archive whose review is not the one on
  // screen — that is one archive's work written over another's. And it may not save at all while
  // the stored decisions are being withheld: what is on screen then is an empty set standing in for
  // decisions that still exist, and a save replaces rather than merges, so the first quiet moment
  // after opening the page would destroy every station the reviewer had chosen.
  const snapshot = JSON.stringify({ options, decisions });
  const pending = useDebouncedValue(snapshot, 800);
  useEffect(() => {
    if (!fileId || !sameFile(reviewing, fileId) || withheld) {
      return;
    }
    // What settled has to be what is on screen. The debounced value trails the state on purpose,
    // and this runs on more than that value changing — at the moment a stored review is adopted,
    // what has settled is still the blank the page opened with, and sending that would replace a
    // stored review with nothing at all. Where they differ another emission is already coming.
    if (pending !== snapshot) {
      return;
    }
    saveSession.mutate({ fileId, body: { options, decisions } });
    // saveSession is a stable mutation object; including it would resend on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pending, snapshot, fileId, reviewing, withheld]);

  /**
   * A change to the choices, with the one consequence they have for decisions already made.
   *
   * A station name is a name in one model's vocabulary. Change the model and the names chosen under
   * the old one are not names of the new one: the dry run would go on offering those scans, because
   * a named station is what makes a scan takeable, and the confirmation would refuse every one of
   * them as a station the model does not hold — a dry run promising what the button then rejects.
   * So the names go when the model does. Nothing else about the decisions is touched: whether a
   * scan is taken and whose it is are statements about the recording, not about the survey.
   */
  const changeOptions = (next: SpeleolocImportOptions) => {
    setOptions(next);
    if (next.surveyModelId === options.surveyModelId) {
      return;
    }
    setDecisions((current) => {
      const cleared: Record<string, SpeleolocPointDecision> = {};
      for (const [pointId, decision] of Object.entries(current)) {
        cleared[pointId] = decision.stationName ? { ...decision, stationName: null } : decision;
      }
      return cleared;
    });
  };

  /** A patch onto one scan's decision — the station chosen, or whether it is taken, or both. */
  const setDecision = (pointId: string, patch: Partial<SpeleolocPointDecision> | null) =>
    setDecisions((current) => {
      const next = { ...current };
      if (patch === null) {
        delete next[pointId];
      } else {
        next[pointId] = { ...next[pointId], ...patch };
      }
      return next;
    });

  /** The same decision for many scans at once, written in one state update rather than N. */
  const setBulkDecision = (pointIds: readonly string[], action: SpeleolocPointAction) =>
    setDecisions((current) => {
      const next = { ...current };
      for (const pointId of pointIds) {
        next[pointId] = { ...next[pointId], action };
      }
      return next;
    });

  /**
   * What a refusal says, in this screen's own words, chosen by the code it carries and never by
   * its sentence. Only the code is a contract; the sentence beside it is prose written for a
   * person, and matching on that turns a rewording — or a translation — into a wrong message.
   *
   * One function rather than one per place a refusal is shown, because the same codes reach the
   * button, the archive read and the dry run, and two lists of them drift.
   */
  const errorText = (
    error: unknown,
    fallback: 'commitFailed' | 'previewFailed' | 'archiveUnreadable' = 'commitFailed',
  ) => {
    const code = error instanceof ApiError ? error.code : undefined;
    switch (code) {
      case 'file.not_found':
        return t('speleolocImport.errors.fileNotFound');
      case 'trip_log.not_found':
        return t('speleolocImport.errors.tripNotFound');
      case 'access.create_forbidden':
        return t('speleolocImport.errors.createForbidden');
      case 'access.caving_group_binding_forbidden':
        return t('speleolocImport.errors.groupBindingForbidden');
      case 'speleoloc_import.archive_unreadable':
        return t('speleolocImport.errors.archiveUnreadable');
      case 'speleoloc_import.archive_too_large':
        return t('speleolocImport.errors.archiveTooLarge');
      case 'speleoloc_import.recording_too_large':
        return t('speleolocImport.errors.recordingTooLarge');
      case 'speleoloc_import.recording_not_found':
        return t('speleolocImport.errors.recordingNotFound');
      case 'speleoloc_import.trip_missing':
        return t('speleolocImport.errors.tripMissing');
      case 'speleoloc_import.trip_ambiguous':
        return t('speleolocImport.errors.tripAmbiguous');
      case 'speleoloc_import.model_missing':
        return t('speleolocImport.errors.modelMissing');
      case 'speleoloc_import.model_unavailable':
        return t('speleolocImport.errors.modelUnavailable');
      case 'speleoloc_import.reference_unknown':
        return t('speleolocImport.errors.referenceUnknown');
      case 'speleoloc_import.selection_empty':
        return t('speleolocImport.errors.selectionEmpty');
      case 'speleoloc_import.selection_too_large':
        return t('speleolocImport.errors.selectionTooLarge');
      case 'speleoloc_import.nothing_created':
        return t('speleolocImport.errors.nothingCreated');
      default:
        // What a refusal nothing here recognises is called depends on what was being attempted:
        // "the recording could not be imported" over a dry run would name an act nobody asked for.
        return t(`speleolocImport.errors.${fallback}`);
    }
  };

  const failed = (error: unknown) => message.error(errorText(error));

  const uploadArchive = async (file: File, allowDuplicate: boolean) => {
    try {
      const created = await upload.mutateAsync({ file, allowDuplicate });
      setDuplicateOf(null);
      navigate(`/trip-logs/speleoloc-import/${created.id}`);
    } catch (error) {
      const documentId = duplicateDocumentId(error);
      if (documentId) {
        setDuplicateOf(documentId);
        return;
      }
      message.error(t('speleolocImport.errors.uploadFailed'));
    }
  };

  if (!fileId) {
    return (
      <UploadArm duplicateOf={duplicateOf} onUpload={uploadArchive} pending={upload.isPending} />
    );
  }

  const data = preview.data;
  const previewError = preview.error;
  const pagePoints = data?.points ?? [];
  const pageIds = new Set(pagePoints.map((point) => point.pointId));
  const serverSelectable = data?.selectablePointIds ?? [];
  const offRoster = new Set(data?.caversNotOnRoster ?? []);

  /**
   * Whether one scan on this page *could* be recorded — everything the confirmation asks except
   * what the reviewer decides. A station either proposed or named, somebody this scan is about,
   * and that somebody on the roster of the trip it is going onto.
   *
   * The server answers the same question for the whole recording in `selectablePointIds`, but it
   * answers it from the decisions it has *stored*, and a decision made half a second ago has not
   * been saved yet. So the rows on screen are decided here, over the local decisions, and the rest
   * of the recording keeps the server's answer: the count on the button never lags a switch that
   * has just been flipped, and it never disagrees with the confirmation either, because it is the
   * same questions in the same order.
   */
  const canTake = (point: SpeleolocPoint) =>
    couldBeRecorded(point, decisions[point.pointId], offRoster);

  /** The same, plus the one thing that is the reviewer's to say: whether it is taken. */
  const takenHere = (point: SpeleolocPoint) =>
    decisions[point.pointId]?.action !== 'skip' && canTake(point);

  const takenIds = [
    ...pagePoints.filter(takenHere).map((point) => point.pointId),
    ...serverSelectable.filter(
      (pointId) => !pageIds.has(pointId) && decisions[pointId]?.action !== 'skip',
    ),
  ];

  // An archive that was read and holds nothing. Not the same as one that could not be read, and not
  // the same as one nobody has read yet — both of which leave this false.
  const emptyArchive = recordings.data !== undefined && recordings.data.recordings.length === 0;

  const blockers: SpeleolocBlocker[] = [];
  if (!options.tripUuid) {
    blockers.push(emptyArchive ? 'archiveEmpty' : 'recording');
  }
  if (!options.createTrip && !options.tripLogId) {
    blockers.push('destination');
  }
  if (!options.surveyModelId) {
    blockers.push('model');
  } else if (data && !data.modelUsable) {
    blockers.push('modelUnusable');
  }
  if (takenIds.length === 0) {
    blockers.push('selection');
  }

  const onCommit = async () => {
    try {
      const outcome = await commit.mutateAsync({
        fileId,
        body: { options, pointIds: takenIds, decisions },
      });
      setResult(outcome);
      // The scans that became positions are set aside, and only they. Clearing the decisions
      // instead would leave the whole selection standing: the button recomputes itself from the
      // preview still in hand and would offer to record the same scans a second time, which is
      // reversible only as a separate act. The ones that failed stay taken, because those are what
      // somebody may want to fix a mapping for and try again.
      const failedIds = new Set(outcome.failures.map((failure) => failure.pointId));
      setBulkDecision(
        takenIds.filter((pointId) => !failedIds.has(pointId)),
        'skip',
      );
    } catch (error) {
      // Nothing local is touched. A refused confirmation wrote nothing, so the review it was made
      // from is still the review — every station chosen and every person mapped stands, and the
      // button can be pressed again once whatever it named has been dealt with.
      failed(error);
    }
  };

  if (session.isError) {
    return (
      <div style={{ padding: 24 }}>
        {/* Two different refusals reach here and they lead to different places. A caller who may
            not record trips is refused the review outright, and telling them the archive is
            missing sends them looking for a file sitting exactly where they left it. */}
        <Alert
          type="error"
          showIcon
          data-testid="speleoloc-import-session-error"
          title={
            session.error instanceof ApiError && session.error.code === 'access.create_forbidden'
              ? t('speleolocImport.errors.createForbidden')
              : t('speleolocImport.errors.fileNotFound')
          }
        />
      </div>
    );
  }

  return (
    <div style={{ padding: isMobile ? 12 : 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }} gap={12} wrap>
        <Space wrap>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/trip-logs')}>
            {t('common.back')}
          </Button>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {t('speleolocImport.title')}
          </Typography.Title>
          {session.data?.fileName && <Tag>{session.data.fileName}</Tag>}
        </Space>
        {/* Dead until everything the confirmation demands has been settled, and dead again once it
            has succeeded: the selection recomputes itself from the preview the moment the result
            appears, so a live button would offer to record the same scans a second time. */}
        <Button
          type="primary"
          disabled={blockers.length > 0 || result !== null}
          loading={commit.isPending}
          onClick={() => void onCommit()}
          data-testid="speleoloc-import-commit"
        >
          {t('speleolocImport.recordSelected', { count: takenIds.length })}
        </Button>
      </Flex>

      {recordings.isError && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="speleoloc-import-archive-error"
          title={errorText(recordings.error, 'archiveUnreadable')}
          description={recordings.error instanceof ApiError ? recordings.error.detail : undefined}
        />
      )}

      {/* An archive with nothing in it to choose. Said here rather than left to be inferred from a
          chooser that opens empty: the reviewer has been asked to name a recording, and an empty
          list with no explanation reads as a screen that is broken or an upload that failed. */}
      {emptyArchive && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="speleoloc-import-archive-empty"
          title={t('speleolocImport.archiveEmptyTitle')}
          description={t('speleolocImport.archiveEmptyBody')}
        />
      )}

      {/* A review whose decisions are being withheld is not a review with no decisions in it, and
          the two look identical. Said plainly, because every station this reviewer chose is still
          stored and would be acted on by a confirmation they can no longer see the contents of. */}
      {withheld && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="speleoloc-import-withheld"
          title={t('speleolocImport.withheldTitle')}
          description={t('speleolocImport.withheldBody')}
        />
      )}

      {previewError && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="speleoloc-import-preview-error"
          title={errorText(previewError, 'previewFailed')}
          description={previewError instanceof ApiError ? previewError.detail : undefined}
        />
      )}

      <Flex gap={16} align="start" wrap>
        <Flex
          vertical
          gap={16}
          style={{ flex: isMobile ? '1 1 100%' : '1 1 640px', minWidth: isMobile ? 0 : 420 }}
        >
          <SpeleolocImportOptionsPanel
            options={options}
            onChange={changeOptions}
            recordings={recordings.data?.recordings ?? []}
            recordingsLoading={recordings.isFetching}
          />

          <Spin spinning={preview.isFetching && !data}>
            <SpeleolocPointTable
              items={pagePoints}
              decisions={decisions}
              onDecision={setDecision}
              isTaken={takenHere}
              canTake={canTake}
              selectablePointIds={serverSelectable}
              onBulkDecision={setBulkDecision}
              nameOf={(caverId) =>
                (roster.data ?? []).find((caver) => caver.id === caverId)?.name
                ?? t('speleolocImport.unnamedCaver')
              }
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

        <Flex
          vertical
          gap={16}
          style={{ flex: isMobile ? '1 1 100%' : '1 1 380px', minWidth: isMobile ? 0 : 320 }}
        >
          <SpeleolocImportSummary
            preview={data}
            takenCount={takenIds.length}
            blockers={blockers}
            createTrip={options.createTrip ?? false}
          />
          <SpeleolocPeoplePanel
            options={options}
            onChange={changeOptions}
            deviceUsers={data?.deviceUsers ?? []}
            unmappedDeviceUsers={data?.unmappedDeviceUsers ?? []}
            caversNotOnRoster={data?.caversNotOnRoster ?? []}
          />
        </Flex>
      </Flex>

      <SpeleolocImportResultModal result={result} onClose={() => setResult(null)} />
    </div>
  );
}

/**
 * Where an archive arrives. Separate from the review because the review needs a file to be about,
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
  // The refusal names a document; the review is about the file inside it. Reading the document is
  // how one becomes the other, and it is why the duplicate is an offer rather than a wall.
  const existing = useDocument(duplicateOf ?? undefined);

  return (
    <div style={{ padding: 24 }}>
      <Flex align="center" gap={12} style={{ marginBottom: 16 }} wrap>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/trip-logs')}>
          {t('common.back')}
        </Button>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('speleolocImport.uploadTitle')}
        </Typography.Title>
      </Flex>

      {duplicateOf && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="speleoloc-import-duplicate"
          title={t('speleolocImport.duplicateTitle')}
          description={
            <>
              <Typography.Paragraph style={{ marginBottom: 8 }}>
                {t('speleolocImport.duplicateBody', { name: existing.data?.title ?? '' })}
              </Typography.Paragraph>
              <Space wrap>
                <Button
                  type="primary"
                  disabled={!existing.data}
                  onClick={() =>
                    navigate(`/trip-logs/speleoloc-import/${existing.data!.currentFileId}`)
                  }
                  data-testid="speleoloc-import-duplicate-resume"
                >
                  {t('speleolocImport.duplicateResume')}
                </Button>
                <Button
                  loading={pending}
                  disabled={!held}
                  onClick={() => held && void onUpload(held, true)}
                  data-testid="speleoloc-import-duplicate-again"
                >
                  {t('speleolocImport.duplicateUploadAgain')}
                </Button>
              </Space>
            </>
          }
        />
      )}

      <Upload.Dragger
        accept=".zip,.sqlite,.sqlite3,.db"
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
        <p className="ant-upload-text">{t('speleolocImport.uploadPrompt')}</p>
        <p className="ant-upload-hint">{t('speleolocImport.uploadHint')}</p>
      </Upload.Dragger>
    </div>
  );
}
