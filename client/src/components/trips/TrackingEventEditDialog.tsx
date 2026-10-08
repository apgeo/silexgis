// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, App, Button, ConfigProvider, Flex, Form, Input, Modal, Select } from 'antd';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  TRACKING_EVENT_KINDS,
  useUpdateTrackingEvent,
  type TrackingEvent,
  type TrackingTeam,
  type TripPositionEventKind,
} from '../../api/hooks.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { COARSE_CONTROL_HEIGHT, useTrackingPanelTheme } from './trackingControlSizes.ts';
import { trackingProblemMessage } from './trackingProblems.ts';
import TrackingPlaceFields from './TrackingPlaceFields.tsx';
import TrackingWhenField from './TrackingWhenField.tsx';
import './TrackingReportDialog.css';

interface Props {
  tripLogId: string;
  /** The report being corrected, or null when the dialog is closed. */
  report: TrackingEvent | null;
  /**
   * Who that report is about, in the roster's words, so the dialog says whose report it is. Rows
   * about different people at the same station look alike, and the row that was pressed is behind
   * the dialog by the time anybody is reading it.
   */
  caverName?: string;
  /**
   * The watch's own survey, whose stations are offered while a station's name is typed — the
   * survey a correction is measured against when it is saved. Null where the reader is not told
   * which survey that is.
   */
  surveyModelId: string | null;
  /** When the watch was started, or null where it never was. */
  armedAt: string | null;
  teams: readonly TrackingTeam[];
  onClose: () => void;
}

interface EditForm {
  kind: TripPositionEventKind;
  stationName?: string;
  toStationName?: string;
  depthM?: number | null;
  teamId?: string | null;
  note?: string;
  recordedAt?: dayjs.Dayjs;
}

/** How big everything in the dialog is drawn — decided by the pointer, once, at the top. */
type ControlSize = 'large' | 'middle';

/**
 * Correcting a report already on the log.
 *
 * <b>Why a correction rather than a deletion and a re-entry.</b> The log used to be corrected by
 * taking a wrong report off and writing a right one, which cost two things. A re-entered report is
 * a new row with a new identity, so anything hanging off the old one — a photograph pinned to that
 * moment, a reader's link to it — is orphaned by a fixed typo. And on a watch that had been closed,
 * removing was permitted while recording was not, so a finished trip's log could be destroyed and
 * not repaired: exactly the trips that get written up afterwards, from notes, days later.
 *
 * <b>It does not offer the caver, deliberately.</b> A report about a different person is a different
 * report — the thing somebody means by changing its subject is that this one should not exist and
 * another should, which is the delete beside this on the same row. What is offered here is
 * everything about one person's report that a relayed phone call can be written down wrongly.
 *
 * <b>Nothing here decides what a valid report is.</b> Whether a station exists on the survey in
 * force, which station a depth means under the trip's datum and filter, whether a moment is in the
 * future — all of it is the server's answer, asked again on the correction exactly as it was asked
 * on the original. This form's own rules go no further than the shape of what it sends, so the two
 * surfaces cannot come to disagree about what may be recorded.
 *
 * <b>The place is asked by the block the report card asks it with.</b> A correction is where a
 * wrong place gets repaired, so it is where the cave's declared places, the survey's own station
 * names and the warning about a depth that lands far from any station are needed most — and a
 * correction opened on a depth already recorded that way says so at once, without anything being
 * retyped.
 *
 * <b>A moment before the watch was started is warned about, not refused.</b> Reports from before
 * a watch began are legitimate — a party that went in before anybody sat down to follow it — but
 * a day or an hour typed wrongly lands there far more often than a true one does, and nothing
 * else on screen would say so.
 *
 * <b>Built for a finger where there is one.</b> This is the dialog opened from a phone to fix an
 * hour typed off a call, so every control follows the pointer the way the dialog a station press
 * opens does: the same touch target, the same enlarged calendar and list, the same stylesheet that
 * keeps the dialog inside the screen and its buttons under a body that scrolls. The pointer and
 * not the width, because a phone held sideways has a desk's room across and still no pixel
 * precision.
 */
export default function TrackingEventEditDialog({
  tripLogId,
  report,
  caverName,
  surveyModelId,
  armedAt,
  teams,
  onClose,
}: Props) {
  const { t, i18n } = useTranslation();
  const coarse = useCoarsePointer();
  const panelTheme = useTrackingPanelTheme(coarse);
  const controlSize: ControlSize = coarse ? 'large' : 'middle';
  // The portalled panels' own sizes, plus the height every `large` control in here is built from.
  const theme = {
    ...panelTheme,
    token: coarse ? { controlHeightLG: COARSE_CONTROL_HEIGHT } : {},
  };
  return (
    <ConfigProvider theme={theme}>
      <Modal
        // The stylesheet shared with the dialog a station press opens: never wider than the
        // screen, a body that scrolls under a footer that never leaves it, and a corner X a
        // finger can hit.
        className="tracking-report-dialog"
        open={report !== null}
        // Whose report and of when, where the surface that opened this knows: the row is behind
        // the dialog now, and "this report" is otherwise whichever one somebody believes it is.
        title={
          report !== null && caverName
            ? t('trips.tracking.eventEditAbout', {
                name: caverName,
                when: new Date(report.recordedAt).toLocaleString(i18n.language),
              })
            : t('trips.tracking.eventEditTitle')
        }
        footer={null}
        onCancel={onClose}
        destroyOnHidden
        data-testid="trip-tracking-event-edit"
      >
        {/* Keyed on the report, so opening a second row builds a second form rather than reusing
            the first one's values. A single form filled from whichever report was pressed would
            offer one report's place as a correction to another's the moment a fill was missed — and
            it would look like working software. This makes that impossible rather than remembered. */}
        {report !== null && (
          <CorrectionForm
            key={report.id}
            tripLogId={tripLogId}
            report={report}
            surveyModelId={surveyModelId}
            armedAt={armedAt}
            teams={teams}
            coarse={coarse}
            controlSize={controlSize}
            onClose={onClose}
          />
        )}
      </Modal>
    </ConfigProvider>
  );
}

function CorrectionForm({
  tripLogId,
  report,
  surveyModelId,
  armedAt,
  teams,
  coarse,
  controlSize,
  onClose,
}: {
  tripLogId: string;
  report: TrackingEvent;
  surveyModelId: string | null;
  armedAt: string | null;
  teams: readonly TrackingTeam[];
  coarse: boolean;
  controlSize: ControlSize;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation();
  // The application's own message surface rather than the static one: the static API draws into
  // a root of its own that outlives whatever mounted it, and the correction dialog is only ever
  // shown under the application shell.
  const { message } = App.useApp();
  const [form] = Form.useForm<EditForm>();
  const update = useUpdateTrackingEvent();
  const initial: EditForm = {
    kind: report.kind,
    stationName: report.stationName ?? undefined,
    // A report between two stations opens holding both, so that correcting its hour or its note
    // does not quietly turn it into a report at the first station.
    toStationName: report.toStationName ?? undefined,
    depthM: report.depthEnteredM ?? null,
    teamId: report.teamId ?? null,
    note: report.note ?? undefined,
    recordedAt: dayjs(report.recordedAt),
  };
  // Falls back to the report's own kind so the field belonging to it is drawn on the very first
  // paint rather than a tick later: a form whose place field appears late is one somebody can press
  // Save on while it is still empty.
  const kind = Form.useWatch('kind', form) ?? initial.kind;
  // The moment as the form holds it now. Undefined is the form not having answered yet, when the
  // report's own moment is the answer; an emptied field is null and warns about nothing.
  const watchedWhen = Form.useWatch('recordedAt', form);
  const when = watchedWhen === undefined ? initial.recordedAt : watchedWhen;
  const beforeArmed = armedAt !== null && when != null && when.isBefore(dayjs(armedAt));

  const submit = async () => {
    let values: EditForm;
    try {
      values = await form.validateFields();
    } catch {
      // The form is already showing why. Swallowed rather than rethrown so a refused validation
      // does not surface as an unhandled rejection in the browser, which is watched for and
      // reported as a defect — and clearing a field before pressing Save is an ordinary act.
      return;
    }
    try {
      await update.mutateAsync({
        tripLogId,
        eventId: report.id,
        kind: values.kind,
        // Sent as null rather than left out when the kind does not carry them: the server measures
        // the whole report again, and a station left over from the kind this report used to be
        // would be refused as belonging to the wrong sort of report.
        stationName: values.kind === 'atStation' ? (values.stationName ?? null) : null,
        // An emptied second station is a report at one station, said as an absence: the server
        // refuses a blank rather than guessing that.
        toStationName:
          values.kind === 'atStation' && values.toStationName?.trim()
            ? values.toStationName.trim()
            : null,
        depthM: values.kind === 'atDepth' ? (values.depthM ?? null) : null,
        teamId: values.teamId ?? null,
        note: values.note?.trim() ? values.note.trim() : null,
        recordedAt: values.recordedAt ? values.recordedAt.toISOString() : null,
      });
      message.success(t('trips.tracking.eventCorrected'));
      onClose();
    } catch (error) {
      message.error(trackingProblemMessage(error, t));
    }
  };

  return (
    <>
      {/* Said every time rather than only on a closed watch: a reader correcting a log is changing
          what the record says happened, and the replay, the published page and anything drawn from
          the log all follow the correction. That is the point of it, and it is worth knowing. */}
      <Alert
        type="info"
        showIcon
        title={t('trips.tracking.eventEditNoticeTitle')}
        description={t('trips.tracking.eventEditNoticeBody')}
        style={{ marginBottom: 16 }}
      />
      <Form
        form={form}
        layout="vertical"
        requiredMark={false}
        size={controlSize}
        initialValues={initial}
      >
        <Form.Item name="kind" label={t('trips.tracking.reportKind')}>
          <Select
            data-testid="trip-tracking-edit-kind"
            options={TRACKING_EVENT_KINDS.map((value) => ({
              value,
              label: t(`trips.tracking.kinds.${value}`),
            }))}
          />
        </Form.Item>

        {/* Where they were, asked exactly as the report card asks it. The depth the report was
            recorded with is handed over as well as held by the form, so that what it means on the
            survey is asked the moment the dialog opens rather than after somebody retypes it. */}
        <TrackingPlaceFields
          tripLogId={tripLogId}
          kind={kind}
          size={controlSize}
          idPrefix="trip-tracking-edit"
          surveyModelId={surveyModelId}
          seedDepth={report.kind === 'atDepth' ? report.depthEnteredM : null}
        />

        {teams.length > 0 && (
          <Form.Item name="teamId" label={t('trips.tracking.reportTeam')}>
            <Select
              allowClear
              data-testid="trip-tracking-edit-team"
              options={teams.map((team) => ({ value: team.id, label: team.title }))}
            />
          </Form.Item>
        )}

        <Form.Item name="note" label={t('trips.tracking.reportNote')}>
          <Input.TextArea rows={2} data-testid="trip-tracking-edit-note" />
        </Form.Item>

        {/* `confined`: the fields here scroll inside the modal's body, whose cap keeps the buttons
            on screen and leaves too little scroll to open a finger-sized calendar under. The field
            says so, and its stylesheet pins the panel to the screen. */}
        <TrackingWhenField
          size={controlSize}
          coarse={coarse}
          idPrefix="trip-tracking-edit"
          confined
        />

        {/* Under the field it is about, and a warning rather than a rule: the server takes a
            moment before the watch began, and somebody writing up what happened before anybody
            was following must be able to save it. */}
        {beforeArmed && (
          <Alert
            type="warning"
            showIcon
            title={t('trips.tracking.eventEditBeforeArmedTitle')}
            description={t('trips.tracking.eventEditBeforeArmedBody', {
              armed: new Date(armedAt).toLocaleString(i18n.language),
            })}
            style={{ marginBottom: 16 }}
            data-testid="trip-tracking-edit-before-armed"
          />
        )}

        <Flex gap={8} justify="flex-end">
          <Button size={controlSize} onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button
            type="primary"
            size={controlSize}
            loading={update.isPending}
            onClick={() => void submit()}
          >
            {t('trips.tracking.eventEditSave')}
          </Button>
        </Flex>
      </Form>
    </>
  );
}
