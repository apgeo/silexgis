// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import type { Rule } from 'antd/es/form';
import type { Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { isConcurrencyConflict } from '../../api/client.ts';
import { useRecordTrackingEvents, type TripPositionEventKind } from '../../api/hooks.ts';
import { trackingProblemMessage } from './trackingProblems.ts';

/**
 * Recording what somebody underground said, wherever it was said from.
 *
 * <b>There are two ways into this and there must be one rule behind them.</b> A report is filled in
 * on the card under the watch, and it is also recorded by pressing a station on the model — which is
 * the faster of the two by a long way, and is the one somebody uses while a voice on the phone is
 * still talking. What must not differ between them is what actually reaches the server: which of
 * the station and the depth travels, what an empty note becomes, how a named moment is written
 * down, and which refusal is shown as a warning rather than an error. Every one of those is a rule
 * about the log rather than about a form, so all of them live here and neither surface holds a
 * second copy.
 *
 * The one thing deliberately *not* here is what happens afterwards. The card clears its fields and
 * lets the table's selection go; the dialog closes. Both are about the surface and not about the
 * report.
 */

/** What either surface has filled in, in the form's own types. */
export interface TrackingReportValues {
  kind: TripPositionEventKind;
  stationName?: string;
  depthM?: number | null;
  teamId?: string | null;
  note?: string;
  recordedAt?: Dayjs | null;
}

/**
 * The request one report makes, whichever surface filled it in.
 *
 * <b>A station name belongs to a station report and a depth to a depth report.</b> The server
 * refuses a request carrying the other one rather than quietly ignoring it, so what is not being
 * reported is sent as null rather than as whatever was left in a field the reader changed their
 * mind about.
 *
 * <b>An empty moment means the server's clock</b>, which is what a report made as it happens wants;
 * a filled one is written as an instant with its offset, because the reader's phone and the server
 * are not in the same place as often as they are.
 */
export function trackingReportBody(
  tripLogId: string,
  caverIds: readonly string[],
  values: TrackingReportValues,
) {
  const note = values.note?.trim();
  return {
    tripLogId,
    caverIds: [...caverIds],
    kind: values.kind,
    stationName: values.kind === 'atStation' ? (values.stationName ?? '').trim() : null,
    depthM: values.kind === 'atDepth' ? (values.depthM ?? null) : null,
    teamId: values.teamId ?? null,
    note: note ? note : null,
    recordedAt: values.recordedAt ? values.recordedAt.toISOString() : null,
  };
}

/**
 * What a station report has to say before it is worth sending.
 *
 * Shared rather than restated so that the dialog opened from the model cannot drift into accepting
 * a report the card would have refused — which is the shape this defect takes: the dialog's station
 * arrives pre-filled from a click, so the field is almost never empty and a missing rule there
 * would go unnoticed until the one time somebody cleared it.
 */
export function trackingStationRules(t: ReturnType<typeof useTranslation>['t']): Rule[] {
  return [{ required: true, message: t('trips.tracking.reportStationRequired') }];
}

/**
 * The one way a report is sent, and the one place its answer is worded.
 *
 * Answers whether the report landed, so a surface can decide what to do with itself — the card
 * keeps standing and clears its fields, the dialog closes — without either of them having to know
 * how a refusal is told apart from a success.
 *
 * A concurrent write is said as a warning and everything else as an error, because that one is not
 * a mistake anybody made: somebody else wrote to the same watch in the same moment, and the answer
 * is to look and try again rather than to change anything.
 */
export function useTrackingReport() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const record = useRecordTrackingEvents();

  const send = async (
    tripLogId: string,
    caverIds: readonly string[],
    values: TrackingReportValues,
  ): Promise<boolean> => {
    try {
      const created = await record.mutateAsync(trackingReportBody(tripLogId, caverIds, values));
      message.success(t('trips.tracking.recorded', { count: created.length }));
      return true;
    } catch (error) {
      if (isConcurrencyConflict(error)) {
        message.warning(trackingProblemMessage(error, t));
      } else {
        message.error(trackingProblemMessage(error, t));
      }
      return false;
    }
  };

  return { send, isPending: record.isPending };
}
