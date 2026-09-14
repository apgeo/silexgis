// SPDX-License-Identifier: AGPL-3.0-or-later
import { ConfigProvider, Form, Input, Modal, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import type { TrackingTeam } from '../../api/hooks.ts';
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

/** What the dialog asks for. The station is a field rather than a fact, and see below for why. */
interface DialogForm extends TrackingReportValues {
  caverIds: string[];
  stationName: string;
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
 * <b>The station is still a field.</b> It arrives filled in with what was pressed, and it can be
 * changed, because the viewer's spelling of a station and the server's are not guaranteed to be the
 * same string for every survey format — the server prefixes some models with the root survey's name
 * and the viewer's reader does not. When they differ the server answers that it has no station by
 * that name, and a read-only field would leave the reader with a refusal and nothing to do about it.
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
    if (await report.send(tripLogId, values.caverIds, { ...values, kind: 'atStation' })) {
      onRecorded();
      onClose();
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
        <Form<DialogForm>
          form={form}
          layout="vertical"
          requiredMark={false}
          size={controlSize}
          initialValues={{
            caverIds: [...defaultCaverIds],
            stationName: station ?? '',
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

          <Form.Item
            name="stationName"
            label={t('trips.tracking.reportStation')}
            extra={t('trips.tracking.reportStationHelp')}
            rules={trackingStationRules(t)}
          >
            <Input data-testid="trip-tracking-dialog-station" />
          </Form.Item>

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
