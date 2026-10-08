// SPDX-License-Identifier: AGPL-3.0-or-later
import { LogoutOutlined } from '@ant-design/icons';
import { Alert, Button, Card, ConfigProvider, Flex, Form, Input, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  TRACKING_EVENT_KINDS,
  type TrackingTeam,
  type TripPositionEventKind,
  type TripTrackingState,
} from '../../api/hooks.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import TrackingPlaceFields from './TrackingPlaceFields.tsx';
import TrackingWhenField from './TrackingWhenField.tsx';
import { useTrackingPanelTheme } from './trackingControlSizes.ts';
import { trackingLogWritable } from './trackingWatch.ts';
import { useTrackingReport, type TrackingReportValues } from './trackingReport.ts';

interface ReportForm extends TrackingReportValues {
  kind: TripPositionEventKind;
}

interface Props {
  tripLogId: string;
  /**
   * The state the watch is in, which decides two things here. Whether its log may be written at
   * all: it has been started, and is either still running or has been closed since — a watch never
   * started is the one state that refuses every report. And whether a report is being made as it
   * happens or written up afterwards, which is what a closed watch means and changes what "when"
   * may be left to say.
   */
  state: TripTrackingState;
  /**
   * The survey the watch is on, whose stations are offered while a station's name is typed. Null
   * where the reader is not told which survey that is; the field is then a plain text box.
   */
  surveyModelId: string | null;
  /**
   * The cave that survey belongs to, so that a cave declaring no places can be pointed at. Null
   * wherever the survey is.
   */
  caveId: string | null;
  /** The people this report is about — the table's selection, in the order it holds them. */
  caverIds: readonly string[];
  teams: readonly TrackingTeam[];
  /** Called once a report has landed, so the selection that produced it can be let go. */
  onRecorded: () => void;
}

/**
 * Recording what somebody underground just said.
 *
 * One request for however many people the report is about, and that is the point of the selection
 * rather than a saving of round trips: a party that reached a station reached it together, at one
 * moment, and a request per person would put that moment on the log several times over with
 * nothing saying they were the same report.
 *
 * Where the report says somebody is — a declared place, a station, a depth and everything said
 * about a depth before it is recorded — is asked by one block that the dialog for correcting a
 * report draws as well, so the surface a place is first written on and the one it is repaired on
 * cannot come to offer different things.
 *
 * The form is drawn once the watch has been started, and stays drawn after it is closed: a finished
 * trip is written up afterwards from notes, and the server takes those reports exactly as it takes
 * live ones. A watch never started refuses every report, and relying on that refusal would mean
 * offering somebody a form that cannot work at the moment they most need one — the wording here
 * says which act is missing instead.
 *
 * What a report actually *is* — which fields travel, how a moment is written, which refusal is a
 * warning — is not decided here. This card and the dialog opened by pressing a station on the model
 * are two ways to the same act, and that act has one home.
 */
export default function TrackingReportForm({
  tripLogId,
  state,
  surveyModelId,
  caveId,
  caverIds,
  teams,
  onRecorded,
}: Props) {
  const { t } = useTranslation();
  const writable = trackingLogWritable(state);
  /**
   * Whether this report is being written up after the trip, rather than taken as it comes in.
   *
   * On a closed watch nothing is happening now, so "now" is never when anything was said: the
   * moment is asked for outright, by every way this card has of sending a report.
   */
  const afterClose = state === 'closed';
  // Every control here is pressed, and how big it has to be follows the pointer and not the width:
  // a phone in landscape has a desk's room across and still no pixel precision.
  const coarse = useCoarsePointer();
  const [form] = Form.useForm<ReportForm>();
  const kind = Form.useWatch('kind', form) ?? 'entered';
  const report = useTrackingReport();
  const panelTheme = useTrackingPanelTheme(coarse);

  if (!writable) {
    return (
      <Alert
        type="info"
        showIcon
        title={t('trips.tracking.notArmedTitle')}
        description={t('trips.tracking.notArmedBody')}
        style={{ marginBottom: 16 }}
        data-testid="trip-tracking-not-armed"
      />
    );
  }

  const send = async (values: TrackingReportValues) => {
    if ((await report.send(tripLogId, caverIds, values)).recorded) {
      form.resetFields(['stationName', 'depthM', 'note', 'recordedAt']);
      onRecorded();
    }
  };

  const onRecord = async () => {
    let values: ReportForm;
    try {
      values = await form.validateFields();
    } catch {
      // The form is already showing why. Swallowed rather than rethrown so a refused validation
      // does not surface as an unhandled rejection in the browser, which is watched for.
      return;
    }
    await send(values);
  };

  // Saying somebody is out is the one report that is worth its own control: it is the commonest
  // thing anybody records, it carries no position, and asking for it through the picker is three
  // actions at the moment a party is walking out.
  //
  // On a closed watch it asks for the moment like every other report here. It was the one path that
  // never read the form, so left alone it would have gone on stamping "out" with the hour somebody
  // sat down to write the trip up — days after everybody was home.
  const onMarkOut = async () => {
    let recordedAt: ReportForm['recordedAt'];
    if (afterClose) {
      try {
        ({ recordedAt } = await form.validateFields(['recordedAt']));
      } catch {
        // The field is already saying what is missing.
        return;
      }
    }
    await send({ kind: 'exited', teamId: form.getFieldValue('teamId') ?? null, recordedAt });
  };

  const nobody = caverIds.length === 0;
  /**
   * How big everything on this card is drawn. `large` is where the forty pixels come from — the
   * component library builds it out of `controlHeightLG`, the touch target the rest of this
   * application uses — and asking by size rather than by height means the padding, line height
   * and icon inside each control are built for the size the control believes it is.
   */
  const controlSize: 'large' | 'middle' = coarse ? 'large' : 'middle';

  return (
    <Card size="small" title={t('trips.tracking.report')} style={{ marginBottom: 16 }}>
      {/* Said before the fields rather than under the one it changes: it is why the card behaves
          differently from the last time this person used it, and a required field met half-way
          down a familiar form reads as a fault. */}
      {afterClose && (
        <Alert
          type="info"
          showIcon
          title={t('trips.tracking.recordAfterCloseTitle')}
          description={t('trips.tracking.recordAfterCloseBody')}
          style={{ marginBottom: 12 }}
          data-testid="trip-tracking-record-after"
        />
      )}
      {/* Around the form rather than around each chooser: a `Form.Item` hands its value and its
          change handler to the single element it is given, so anything put between the two takes
          them instead of the control. */}
      <ConfigProvider theme={panelTheme}>
        <Form<ReportForm>
          form={form}
          layout="vertical"
          requiredMark={false}
          size={controlSize}
          initialValues={{ kind: 'entered' as TripPositionEventKind }}
        >
          <Form.Item name="kind" label={t('trips.tracking.reportKind')}>
            <Select
              data-testid="trip-tracking-kind"
              options={TRACKING_EVENT_KINDS.map((value) => ({
                value,
                label: t(`trips.tracking.kinds.${value}`),
              }))}
            />
          </Form.Item>

          {/* Where they are. Drawn by the block the correction dialog draws too, inside this form,
              whose kind, station and depth it reads and fills. */}
          <TrackingPlaceFields
            tripLogId={tripLogId}
            kind={kind}
            size={controlSize}
            idPrefix="trip-tracking"
            surveyModelId={surveyModelId}
            caveId={caveId}
          />

          {teams.length > 0 && (
            <Form.Item name="teamId" label={t('trips.tracking.reportTeam')}>
              <Select
                allowClear
                placeholder={t('trips.tracking.reportTeamNone')}
                data-testid="trip-tracking-team"
                options={teams.map((team) => ({ value: team.id, label: team.title }))}
              />
            </Form.Item>
          )}

          <Form.Item name="note" label={t('trips.tracking.reportNote')}>
            <Input.TextArea rows={2} data-testid="trip-tracking-note" />
          </Form.Item>

          {/* Left empty the server stamps the report with its own clock, which is what a report made
              as it happens wants. It is filled in for the other case — word relayed out of the cave
              some time after it was said — and a time in the future is refused rather than stored.
              Once the watch is closed there is no "as it happens" left, and the moment is asked for. */}
          <TrackingWhenField
            size={controlSize}
            coarse={coarse}
            idPrefix="trip-tracking"
            required={afterClose}
          />
        </Form>
      </ConfigProvider>

      {nobody && (
        <Alert
          type="warning"
          showIcon
          title={t('trips.tracking.selectNobody')}
          style={{ marginBottom: 12 }}
          data-testid="trip-tracking-nobody"
        />
      )}

      <Flex gap="small" wrap>
        <Button
          type="primary"
          size={controlSize}
          disabled={nobody}
          loading={report.isPending}
          onClick={() => void onRecord()}
          data-testid="trip-tracking-record"
        >
          {t('trips.tracking.recordFor', { count: caverIds.length })}
        </Button>
        <Button
          size={controlSize}
          icon={<LogoutOutlined />}
          disabled={nobody}
          loading={report.isPending}
          onClick={() => void onMarkOut()}
          data-testid="trip-tracking-mark-out"
        >
          {t('trips.tracking.markOut', { count: caverIds.length })}
        </Button>
      </Flex>
    </Card>
  );
}
