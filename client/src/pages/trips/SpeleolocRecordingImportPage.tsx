// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { Alert, App, Flex, Space, Spin, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import {
  useCavers,
  useCommitRecordingImport,
  useRecordingImportPreview,
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
import { couldBeRecorded } from '../../components/import/speleolocSelection.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';

/** What the review starts from: nothing chosen, and no trip named or asked for yet. */
const blankOptions = (recordingId: string): SpeleolocImportOptions => ({
  // The recording is the one in the address, so the choice every other review begins with is
  // already made.
  tripUuid: recordingId,
  tripLogId: null,
  createTrip: false,
  surveyModelId: null,
  cavers: {},
  cavingGroupId: null,
  visibility: 'private',
  candidateCount: 5,
});

/**
 * Makes one phone recording the installation already keeps into a trip report by hand — the same
 * review as an exported archive's, reading the kept recording instead of a file.
 *
 * Two differences from the archive review, both because there is no file. There is no recording to
 * choose, so the recording is named and not offered. And there is no saved review: the server keeps
 * nothing between visits on this route, so the choices and the decisions live on this screen and
 * travel with the confirmation, and a closed tab starts the review again.
 *
 * Nothing is inferred. A station is proposed from the depth the register holds for the scanned
 * place and settled by the reviewer, and who a phone user is has no proposal at all — the people
 * panel takes the mapping from what the Phone users page already says and asks about the rest.
 */
export default function SpeleolocRecordingImportPage() {
  const { t } = useTranslation();
  const { recordingId } = useParams<{ recordingId: string }>();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const isMobile = useIsMobile();

  const commit = useCommitRecordingImport();
  const roster = useCavers();

  const [options, setOptions] = useState<SpeleolocImportOptions>(() =>
    blankOptions(recordingId ?? ''),
  );
  const [decisions, setDecisions] = useState<Record<string, SpeleolocPointDecision>>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [result, setResult] = useState<SpeleolocImportCommitResult | null>(null);

  const previewRequest = useMemo<SpeleolocImportPreviewRequest>(
    () => ({ options, page, pageSize }),
    [options, page, pageSize],
  );
  const preview = useRecordingImportPreview(recordingId, previewRequest);

  // A station name is a name in one model's vocabulary, so changing the model drops the names
  // chosen under the old one: the dry run would otherwise go on offering scans the confirmation
  // then refuses as stations the new model does not hold.
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

  const setBulkDecision = (pointIds: readonly string[], action: SpeleolocPointAction) =>
    setDecisions((current) => {
      const next = { ...current };
      for (const pointId of pointIds) {
        next[pointId] = { ...next[pointId], action };
      }
      return next;
    });

  /** A refusal in this screen's own words, chosen by its code and never by the sentence beside it. */
  const errorText = (
    error: unknown,
    fallback: 'commitFailed' | 'recordingPreviewFailed' = 'commitFailed',
  ) => {
    switch (error instanceof ApiError ? error.code : undefined) {
      case 'trip_recording.not_found':
        return t('speleolocImport.errors.keptRecordingNotFound');
      case 'import.recording_already_converted':
        return t('speleolocImport.errors.recordingAlreadyConverted');
      case 'import.recording_not_writable':
        return t('speleolocImport.errors.recordingNotWritable');
      case 'trip_log.not_found':
        return t('speleolocImport.errors.tripNotFound');
      case 'access.create_forbidden':
        return t('speleolocImport.errors.createForbidden');
      case 'access.caving_group_binding_forbidden':
        return t('speleolocImport.errors.groupBindingForbidden');
      case 'speleoloc_import.trip_missing':
        return t('speleolocImport.errors.tripMissing');
      case 'speleoloc_import.model_missing':
        return t('speleolocImport.errors.modelMissing');
      case 'speleoloc_import.model_unavailable':
        return t('speleolocImport.errors.modelUnavailable');
      case 'speleoloc_import.selection_empty':
        return t('speleolocImport.errors.selectionEmpty');
      case 'speleoloc_import.selection_too_large':
        return t('speleolocImport.errors.selectionTooLarge');
      case 'speleoloc_import.nothing_created':
        return t('speleolocImport.errors.nothingCreated');
      default:
        return t(`speleolocImport.errors.${fallback}`);
    }
  };

  const data = preview.data;
  const pagePoints = data?.points ?? [];
  const pageIds = new Set(pagePoints.map((point) => point.pointId));
  const serverSelectable = data?.selectablePointIds ?? [];
  const offRoster = new Set(data?.caversNotOnRoster ?? []);

  // This route stores no decisions, so the server's list of takeable scans is computed from none:
  // the rows on screen are decided here over the local decisions, and the rest of the recording
  // keeps the server's answer.
  const canTake = (point: SpeleolocPoint) =>
    couldBeRecorded(point, decisions[point.pointId], offRoster);
  const takenHere = (point: SpeleolocPoint) =>
    decisions[point.pointId]?.action !== 'skip' && canTake(point);
  const takenIds = [
    ...pagePoints.filter(takenHere).map((point) => point.pointId),
    ...serverSelectable.filter(
      (pointId) => !pageIds.has(pointId) && decisions[pointId]?.action !== 'skip',
    ),
  ];

  const blockers: SpeleolocBlocker[] = [];
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
    if (!recordingId) {
      return;
    }
    try {
      const outcome = await commit.mutateAsync({
        recordingId,
        body: { options, pointIds: takenIds, decisions },
      });
      // An account under review records nothing yet: the confirmation waits for a reviewer.
      if (outcome.held) {
        message.info(t('importReview.held'));
        navigate(-1);
        return;
      }
      setResult(outcome);
      const failedIds = new Set(outcome.failures.map((failure) => failure.pointId));
      setBulkDecision(
        takenIds.filter((pointId) => !failedIds.has(pointId)),
        'skip',
      );
    } catch (error) {
      // Nothing local is touched: a refused confirmation wrote nothing, so the review is still
      // the review and the button can be pressed again once what was named has been dealt with.
      message.error(errorText(error));
    }
  };

  // A recording that is not here, or already has its report, has nothing to review. Said instead
  // of drawing an options panel that can only fail.
  const refused = preview.isError && !data;

  return (
    <div style={{ padding: isMobile ? 12 : 24 }} data-testid="speleoloc-recording-import">
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }} gap={12} wrap>
        <Space wrap>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate(-1)}>
            {t('common.back')}
          </Button>
          <Typography.Title level={3} style={{ margin: 0 }}>
            {t('speleolocImport.recordingTitle')}
          </Typography.Title>
        </Space>
        <Button
          type="primary"
          disabled={refused || blockers.length > 0 || result !== null}
          loading={commit.isPending}
          onClick={() => void onCommit()}
          data-testid="speleoloc-import-commit"
        >
          {t('speleolocImport.recordSelected', { count: takenIds.length })}
        </Button>
      </Flex>

      {preview.isError && (
        <Alert
          type={refused ? 'error' : 'warning'}
          showIcon
          style={{ marginBottom: 16 }}
          data-testid="speleoloc-import-preview-error"
          title={errorText(preview.error, 'recordingPreviewFailed')}
        />
      )}

      {!refused && (
        <Flex gap={16} align="start" wrap>
          <Flex
            vertical
            gap={16}
            style={{ flex: isMobile ? '1 1 100%' : '1 1 640px', minWidth: isMobile ? 0 : 420 }}
          >
            <SpeleolocImportOptionsPanel
              options={options}
              onChange={changeOptions}
              recordings={[]}
              recordingsLoading={false}
              lockedRecording={
                data
                  ? {
                      title: data.recordingTitle,
                      startedAt: data.recordingStartedAt,
                      endedAt: data.recordingEndedAt,
                      scans: data.totalItems,
                      documents: data.documentCount,
                    }
                  : { title: '…', startedAt: '', endedAt: null, scans: 0, documents: 0 }
              }
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
              knownLinks={data?.knownLinks ?? {}}
              caversNotOnRoster={data?.caversNotOnRoster ?? []}
            />
          </Flex>
        </Flex>
      )}

      <SpeleolocImportResultModal
        result={result}
        onClose={() => {
          const trip = result?.tripLogId;
          setResult(null);
          if (trip) {
            navigate(`/trip-logs/${trip}`);
          }
        }}
      />
    </div>
  );
}
