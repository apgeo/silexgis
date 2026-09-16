// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, ConfigProvider, Flex, Form, Input, Modal, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { TrackingTeam, TripPositionEventKind } from '../../api/hooks.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import TrackingWhenField from './TrackingWhenField.tsx';
import { useTrackingPanelTheme } from './trackingControlSizes.ts';
import {
  trackingStationRules,
  useTrackingReport,
  type TrackingReportValues,
} from './trackingReport.ts';
import './TrackingReportDialog.css';

/**
 * The touch target every control in this dialog is built to.
 *
 * <b>Forty-four rather than the forty the surface behind it uses, and only here.</b> This is the one
 * place in the feature where a reader is being asked to fill a form in while looking at a model they
 * have just pressed, one-handed, with the other hand holding a phone to their ear — the case the
 * whole dialog exists for. The card under the watch keeps the forty pixels it was measured at,
 * because those measurements were taken against a calendar whose geometry depends on them; this
 * surface has none of that history and can simply be built larger.
 *
 * Given as the token every `large` control derives from, so the padding, the line height and the
 * icon inside each of them are built for the size the control believes it is.
 */
const COARSE_CONTROL_HEIGHT = 44;

/**
 * What a report opened by pressing a station may be, and why the list is shorter than the card's.
 *
 * A press names one place, and the four kinds here are the ones that statement can survive: the
 * report at that station, and the three that carry no place at all — went in, came out, a note —
 * which the dialog says are carrying none rather than quietly dropping the station.
 *
 * <b>A depth report is deliberately not offered.</b> It is the opposite act to this one: the server
 * turns a depth into the nearest station under the trip's filter and datum, which is a decision
 * worth watching being made, so the card under the watch offers it a preview and each candidate as
 * something to take. Reproducing that here would be a second copy of a flow whose whole value is
 * that somebody looks at it — and asking for a depth on the surface that exists because a station
 * was pressed has nothing to recommend it.
 *
 * `atStation` first because it is the default and the reason this dialog was opened; the rest in
 * the order a trip runs.
 */
const DIALOG_KINDS: readonly TripPositionEventKind[] = ['atStation', 'entered', 'exited', 'note'];

/** What the dialog asks for. The station is a fact rather than a field, and see below for why. */
interface DialogForm extends TrackingReportValues {
  caverIds: string[];
  kind: TripPositionEventKind;
  stationName?: string;
}

interface Props {
  open: boolean;
  tripLogId: string;
  /** The station the model was pressed at, as the viewer spells it. Null while nothing is picked. */
  station: string | null;
  /** Everybody the watch names, in the roster's words. */
  cavers: readonly { caverId: string; name: string }[];
  teams: readonly TrackingTeam[];
  /** Who is ticked on the table below — the likeliest answer, offered rather than imposed. */
  defaultCaverIds: readonly string[];
  onClose(): void;
  /** Called once a report has landed, so the selection that produced it can be let go. */
  onRecorded(): void;
}

/**
 * Recording a position by pressing the place on the model.
 *
 * <b>This exists because the fast path through the card below is not fast.</b> Reporting "they are
 * at P42" through that card means scrolling to the table, ticking names, scrolling to the form,
 * choosing a kind, and typing a station name exactly as the survey spells it — from memory, while
 * somebody is still on the line. Pressing the station on the model names it exactly and skips three
 * of those five acts; everything left is in this one dialog.
 *
 * <b>The station is a statement, not a question.</b> Nobody typed it: it is the place that was
 * pressed, and asking somebody to confirm a fact the application already holds is how a form makes
 * itself look like work. So it is stated — the place this report is about — and the only thing that
 * can turn it back into a field is the server saying it does not know it, which is the one case
 * where a reader has something to do about it.
 *
 * <b>That case is real rather than theoretical, and nothing here translates a name to avoid it.</b>
 * The server resolves a reported station against the model instead of comparing it with a string,
 * so a model that spells a survey path differently from this viewer — which the compiled Therion
 * format permits, though no file anyone has looked at here is written that way — is accepted rather
 * than refused. What is left is narrower and still happens: a station the file gives no name at all
 * is called by a number in punctuation the two sides do not share, and a model re-read since this
 * page loaded can have station names that no longer exist. Neither is something a reader can be
 * told about in advance, and a refusal with nowhere to act on it would strand somebody mid-call, so
 * the refusal still reveals the field with the pressed spelling already in it.
 *
 * <b>What is being reported can be changed, and the statement above it changes with it.</b> A press
 * is often not the whole of what a voice on the phone just said — "we're at P42" and "we're out"
 * arrive through the same call — so the kinds that carry no place are offered here too. None of
 * them is a report *at* a station, and the request would carry no station name whichever way this
 * dialog was written; what the dialog owes is to say so rather than to leave a place named on
 * screen above a report that does not claim it.
 *
 * <b>Who the report is about is asked here rather than read off the table.</b> The table's ticks are
 * offered as the answer, because somebody who ticked a team and then pressed a station meant that
 * team — but requiring the ticks first would put the scroll this dialog exists to remove back in
 * front of it.
 *
 * What is sent, and how a refusal is worded, is not decided here: this and the card are two ways to
 * one act, and that act has one home.
 */
export default function TrackingReportDialog({
  open,
  tripLogId,
  station,
  cavers,
  teams,
  defaultCaverIds,
  onClose,
  onRecorded,
}: Props) {
  const { t } = useTranslation();
  const coarse = useCoarsePointer();
  const [form] = Form.useForm<DialogForm>();
  const report = useTrackingReport();
  const panelTheme = useTrackingPanelTheme(coarse);
  // What the button is counting. Falls back to what was offered rather than to nothing, because
  // the watch has no value on the render that opens the dialog — and a button that reads "Record
  // for 0" for one frame, on the surface built for speed, is read as a form that lost the answer.
  const chosen = Form.useWatch('caverIds', form) ?? defaultCaverIds;
  const kind = Form.useWatch('kind', form) ?? 'atStation';

  /**
   * Whether the server has said it has no station by the pressed name.
   *
   * Held for as long as this dialog stands and cleared whenever it opens on a station again: a
   * spelling the server refused once is refused every time, so leaving the field revealed would be
   * correct — but leaving it revealed for the *next* station, which nobody has disputed, would put
   * the question back on a surface built to remove it.
   */
  const [stationDisputed, setStationDisputed] = useState(false);
  useEffect(() => setStationDisputed(false), [station, open]);

  const controlSize: 'large' | 'middle' = coarse ? 'large' : 'middle';
  // The portalled panels' own sizes, plus the height every `large` control in here is built from.
  const theme = {
    ...panelTheme,
    token: coarse ? { controlHeightLG: COARSE_CONTROL_HEIGHT } : {},
  };

  const onOk = async () => {
    let values: DialogForm;
    try {
      values = await form.validateFields();
    } catch {
      // The form is already showing why. Swallowed rather than rethrown so a refused validation
      // does not surface as an unhandled rejection in the browser, which is watched for.
      return;
    }
    // The pressed station unless somebody has been given the field to correct it in. Taken from
    // the press rather than from the form store, because a field that is not on screen is not a
    // field the form is keeping an answer for.
    const stationName = stationDisputed ? values.stationName : (station ?? '');
    const outcome = await report.send(tripLogId, values.caverIds, {
      ...values,
      stationName,
    });
    if (outcome.recorded) {
      onRecorded();
      onClose();
      return;
    }
    // The one refusal this surface can do something about: the model holds no station under the
    // name that was pressed. The refusal has already been worded; what is added here is somewhere
    // to act on it.
    if (outcome.code === 'tracking.station_unknown') {
      form.setFieldValue('stationName', station ?? '');
      setStationDisputed(true);
    }
  };

  return (
    <ConfigProvider theme={theme}>
      <Modal
        className="tracking-report-dialog"
        open={open}
        title={t('trips.tracking.recordHereTitle')}
        okText={t('trips.tracking.recordFor', { count: chosen.length })}
        cancelText={t('common.cancel')}
        // Not disabled on an empty party: the field carries the rule that says so, and a refusal
        // that says which field is missing beats a button that is simply dead with no reason on
        // screen for it.
        okButtonProps={{ size: controlSize }}
        cancelButtonProps={{ size: controlSize }}
        confirmLoading={report.isPending}
        onOk={() => void onOk()}
        onCancel={onClose}
        // The form is built afresh for each station rather than carried over: a note about the last
        // report, or a moment named for it, standing in the fields of the next one is how a wrong
        // thing reaches a log that is never edited.
        destroyOnHidden
        data-testid="trip-tracking-record-here"
      >
        {kind === 'atStation' ? (
          <Flex
            gap={8}
            align="baseline"
            wrap
            style={{ marginBottom: 16 }}
            data-testid="trip-tracking-dialog-place"
          >
            <Typography.Text type="secondary">
              {t('trips.tracking.reportPlaceLabel')}
            </Typography.Text>
            <Typography.Text strong data-testid="trip-tracking-dialog-station-name">
              {station}
            </Typography.Text>
          </Flex>
        ) : (
          <Alert
            type="info"
            showIcon
            title={t('trips.tracking.reportNoPlace', { station: station ?? '' })}
            style={{ marginBottom: 16 }}
            data-testid="trip-tracking-dialog-no-place"
          />
        )}

        <Form<DialogForm>
          form={form}
          layout="vertical"
          requiredMark={false}
          size={controlSize}
          initialValues={{
            caverIds: [...defaultCaverIds],
            kind: 'atStation' as TripPositionEventKind,
          }}
        >
          <Form.Item
            name="caverIds"
            label={t('trips.tracking.reportWho')}
            rules={[{ required: true, message: t('trips.tracking.reportWhoRequired') }]}
          >
            <Select
              mode="multiple"
              allowClear
              placeholder={t('trips.tracking.reportWhoPlaceholder')}
              // A roster is a list of names somebody knows, so typing two letters of one beats
              // scrolling a party of twenty on a screen the size of a hand.
              optionFilterProp="label"
              data-testid="trip-tracking-dialog-cavers"
              options={cavers.map((caver) => ({ value: caver.caverId, label: caver.name }))}
            />
          </Form.Item>

          <Form.Item name="kind" label={t('trips.tracking.reportKind')}>
            <Select
              data-testid="trip-tracking-dialog-kind"
              options={DIALOG_KINDS.map((value) => ({
                value,
                label: t(`trips.tracking.kinds.${value}`),
              }))}
            />
          </Form.Item>

          {/* Only after the server has refused the pressed spelling. The rule is the card's own, so
              a correction this dialog accepts cannot be one the card would have refused. */}
          {kind === 'atStation' && stationDisputed && (
            <Form.Item
              name="stationName"
              label={t('trips.tracking.reportStation')}
              extra={t('trips.tracking.reportStationCorrect')}
              rules={trackingStationRules(t)}
            >
              <Input data-testid="trip-tracking-dialog-station" />
            </Form.Item>
          )}

          {teams.length > 0 && (
            <Form.Item name="teamId" label={t('trips.tracking.reportTeam')}>
              <Select
                allowClear
                placeholder={t('trips.tracking.reportTeamNone')}
                data-testid="trip-tracking-dialog-team"
                options={teams.map((team) => ({ value: team.id, label: team.title }))}
              />
            </Form.Item>
          )}

          {/* `confined`: the fields here scroll inside the modal's body, whose cap keeps the footer
              on screen and leaves 197px of scroll in all — not enough to open a finger-sized
              calendar under. The field says so, and its stylesheet pins the panel to the screen. */}
          <TrackingWhenField
            size={controlSize}
            coarse={coarse}
            idPrefix="trip-tracking-dialog"
            confined
          />

          <Form.Item name="note" label={t('trips.tracking.reportNote')}>
            <Input.TextArea rows={2} data-testid="trip-tracking-dialog-note" />
          </Form.Item>
        </Form>
      </Modal>
    </ConfigProvider>
  );
}
