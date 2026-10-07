// SPDX-License-Identifier: AGPL-3.0-or-later
import { useId, useState } from 'react';
import { AimOutlined, LogoutOutlined } from '@ant-design/icons';
import {
  Alert,
  Button,
  Card,
  ConfigProvider,
  Flex,
  Form,
  Input,
  InputNumber,
  Select,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import {
  TRACKING_EVENT_KINDS,
  useTrackingDepthReading,
  useTrackingPlaces,
  type TrackingDepthCandidate,
  type TrackingTeam,
  type TripPositionEventKind,
  type TripTrackingState,
} from '../../api/hooks.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import List from '../List.tsx';
import TrackingWhenField from './TrackingWhenField.tsx';
import { useTrackingPanelTheme } from './trackingControlSizes.ts';
import { trackingDepthGap } from './trackingDepthGap.ts';
import { trackingProblemMessage } from './trackingProblems.ts';
import { trackingLogWritable } from './trackingWatch.ts';
import {
  trackingStationRules,
  useTrackingReport,
  type TrackingReportValues,
} from './trackingReport.ts';

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
 * A depth is offered a preview before it is recorded. The server turns a depth into the nearest
 * station under the trip's filter and datum, and "nearest" is a decision somebody should watch
 * being made — a filter set a week ago, a datum on a station somebody renamed, and the position
 * that lands on the log is a place the party is not. The preview costs one call and makes that
 * visible while it can still be changed. Each station it offers can then be taken as the answer,
 * which is the difference between a preview and a lookup: the whole reason to ask which station a
 * depth means is that you may want to report that station rather than the depth.
 *
 * <b>And a depth that lands a long way from anything is said without being asked, because the
 * preview is on the wrong side of the form to catch it.</b> Resolution has no tolerance in it: the
 * nearest station to 1200 m in a 140 m cave is the bottom of the cave, and it is stored as
 * confidently as a station half a metre away would be. Somebody taking word off a relayed phone
 * call types a number and presses Record; if the only thing that would have shown the discrepancy
 * is a button they did not press, the party is written down at the wrong end of the system and
 * nothing on screen ever disagreed. So the same answer the preview is built on is asked for as the
 * number settles, and a gap too wide to be an estimate is drawn above the Record button — as a
 * warning, never as a refusal, because a coordinator with better information than this page has
 * must still be able to record what they were told.
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
  const depthTyped = Form.useWatch('depthM', form);
  const report = useTrackingReport();
  /** Whether the list of stations the depth could mean has been asked for. */
  const [listing, setListing] = useState(false);
  const panelTheme = useTrackingPanelTheme(coarse);
  /**
   * What ties the place chooser to its label. That chooser is the one field here the form does not
   * hold — its value is read off the depth field instead — and a form item with no field name
   * gives its label no `for`: a screen reader then announces a bare "combobox" for the quickest
   * way there is to report. Minted per card rather than written down, so a second card on a page
   * cannot hand its label to the first one's chooser.
   */
  const placeFieldId = useId();

  /**
   * The depth to ask about, once somebody has stopped typing it.
   *
   * <b>Debounced because every intermediate number is a real depth somewhere.</b> Typing 120 passes
   * through 1 and 12, and asking about each of them would be three requests and, worse, a warning
   * flickering on and off under a field somebody is still filling in. A third of a second after the
   * number settles is late enough to be quiet and early enough to be on screen before a hand moves
   * from the keyboard to Record.
   */
  const askedDepth = useDebouncedValue(
    kind === 'atDepth' && typeof depthTyped === 'number' && Number.isFinite(depthTyped)
      ? depthTyped
      : null,
  );

  /**
   * What that depth means, asked of the one place that knows.
   *
   * A watch cannot be started without a survey model, and closing it keeps the model — so wherever
   * this form is drawn at all there is a model to resolve against, and the question is never asked
   * into the void.
   */
  const reading = useTrackingDepthReading(tripLogId, askedDepth);
  const candidates: TrackingDepthCandidate[] | undefined = reading.data;

  /**
   * The places this cave has declared, which are what somebody on the phone actually names.
   *
   * Asked for whenever the form is drawn, because the chooser below is the fastest way to report
   * and a list that arrived only after somebody started typing a depth would never be used. A cave
   * that has declared nothing answers an empty list, and then no chooser is drawn at all — there is
   * nothing to say about it, and an empty select is worse than no select.
   *
   * And not before the watch has been started. The question is answered against the watch's model,
   * and a watch that has none is refused rather than answered; the hooks below run whether or not
   * the form is drawn, so asked unconditionally the refusal would be fetched while the watch was
   * off and held, and the card would open on arming with no chooser at all.
   */
  const places = useTrackingPlaces(tripLogId, writable);
  const declared = places.data ?? [];
  /**
   * The place the chooser shows: the declared one whose depth is the depth in the box, on a depth
   * report, and nothing otherwise.
   *
   * <b>Read off the field rather than remembered by the chooser, because the two are one answer.</b>
   * A chooser holding its own choice went on naming a place after the report it was chosen for had
   * landed and the depth had been emptied, and after somebody typed a different depth over it — a
   * card saying "Meandru" over an empty field, or over 120 m, with nothing to say which of the two
   * would be sent. The depth is what is sent, so the depth decides.
   */
  const chosenPlaceDepth =
    kind === 'atDepth' && typeof depthTyped === 'number'
      ? declared.find((place) => place.depthM === depthTyped)?.depthM
      : undefined;
  /** How far the station this depth would be recorded at sits from the depth itself. */
  const gap = trackingDepthGap(askedDepth, candidates?.[0]);

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

  /**
   * Taking a declared place as the answer.
   *
   * <b>Recorded as its depth, not as its station, and the difference matters.</b> What the cave
   * declared is a depth, a station and a name together, so the depth is part of what is being
   * said — and it is also what reading the same place back out of the declarations depends on.
   * Sending the depth means the server resolves it through the very declaration that was chosen
   * here, so the place the person picked and the station that lands on the log cannot disagree.
   */
  const choosePlace = (depthM: number | null | undefined) => {
    if (depthM === null || depthM === undefined) {
      // Clearing the place takes back the depth it filled in: the chooser shows what the field
      // holds, so a cleared chooser over a depth it put there would still be reporting it.
      form.setFieldValue('depthM', null);
      setListing(false);
      return;
    }

    form.setFieldsValue({ kind: 'atDepth', depthM, stationName: undefined });
    setListing(false);
  };

  const send = async (values: TrackingReportValues) => {
    if ((await report.send(tripLogId, caverIds, values)).recorded) {
      form.resetFields(['stationName', 'depthM', 'note', 'recordedAt']);
      setListing(false);
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

  /**
   * Taking one of the offered stations as the answer.
   *
   * The report changes kind as well as value, and it has to: the candidates only exist under a
   * depth report, and what is being said once a station has been chosen is "they are at this
   * station", which is a different claim about the world from "they are this far down". Recording
   * the depth instead would leave the server to resolve it a second time, against a filter that
   * could have moved in between — so the station somebody looked at and the station that lands on
   * the log would be two answers to one question.
   */
  const onChooseCandidate = (candidate: TrackingDepthCandidate) => {
    form.setFieldsValue({ kind: 'atStation', stationName: candidate.stationName });
    setListing(false);
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
              onChange={() => setListing(false)}
              options={TRACKING_EVENT_KINDS.map((value) => ({
                value,
                label: t(`trips.tracking.kinds.${value}`),
              }))}
            />
          </Form.Item>

          {/* Before either of the two fields below, because it is the one most reports are made
              with: a caver relaying word says where they are by name, and a chooser that came
              after the depth field would be read as an afterthought to it. Drawn only where the
              cave has declared something, and only for the kinds that claim a place. */}
          {declared.length > 0 && (kind === 'atStation' || kind === 'atDepth') && (
            <Form.Item
              label={t('trips.tracking.reportPlace')}
              htmlFor={placeFieldId}
              extra={t('trips.tracking.reportPlaceHelp')}
            >
              <Select
                id={placeFieldId}
                allowClear
                showSearch
                optionFilterProp="label"
                placeholder={t('trips.tracking.reportPlacePlaceholder')}
                data-testid="trip-tracking-place"
                value={chosenPlaceDepth}
                onChange={choosePlace}
                options={declared.map((place) => ({
                  // The depth is the value because the depth is what gets recorded: the server
                  // consults the same declarations and answers with the station the club named,
                  // so what lands on the log is the declared place and not a second guess at it.
                  value: place.depthM,
                  label: place.placeLabel
                    ? t('trips.tracking.reportPlaceOption', {
                        place: place.placeLabel,
                        depth: place.depthM,
                      })
                    : t('trips.tracking.reportPlaceOptionUnnamed', {
                        station: place.stationName,
                        depth: place.depthM,
                      }),
                }))}
              />
            </Form.Item>
          )}

          {kind === 'atStation' && (
            <Form.Item
              name="stationName"
              label={t('trips.tracking.reportStation')}
              extra={t('trips.tracking.reportStationHelp')}
              rules={trackingStationRules(t)}
            >
              <Input data-testid="trip-tracking-station" />
            </Form.Item>
          )}

          {kind === 'atDepth' && (
            <>
              <Form.Item
                name="depthM"
                label={t('trips.tracking.reportDepth')}
                extra={t('trips.tracking.reportDepthHelp')}
                rules={[{ required: true, message: t('trips.tracking.reportDepthRequired') }]}
              >
                <InputNumber
                  style={{ width: '100%' }}
                  data-testid="trip-tracking-depth"
                />
              </Form.Item>
              {/* Said while the answer is still coming, because silence has to mean one thing.
                  Everything below turns on a warning being absent, and absence is only worth
                  trusting if it cannot also mean "not asked yet" — a number typed and Record
                  pressed within the second is exactly the hurry a callout is conducted in. */}
              {askedDepth !== null && reading.data === undefined && reading.error == null && (
                <Typography.Text
                  type="secondary"
                  style={{ display: 'block', marginBottom: 12 }}
                  data-testid="trip-tracking-depth-checking"
                >
                  {t('trips.tracking.depthChecking')}
                </Typography.Text>
              )}
              {/* <b>Drawn without being asked for, and this is the half of the preview that catches
                  the typo.</b> The button below opens a list somebody chooses from; this appears on
                  its own when what the depth resolves to is further away than any estimate could
                  be. It is a warning and not a bar: the number may be right and the survey thin,
                  and a coordinator who knows that must still be able to record what they heard.
                  What it must never do is stay quiet, which is what a form with the answer one
                  unpressed button away was doing. */}
              {/* Two reasons a station can sit that far from the number, and each is said as
                  itself. A depth the cave has declared lands on the station the club named for
                  it, whatever the survey measures; explaining that as "the nearest station" would
                  name the right place for a false reason and point the reader at the number when
                  what they might want to check is the declaration. */}
              {gap?.wide === true && (
                <Alert
                  type="warning"
                  showIcon
                  title={t(
                    gap.declared
                      ? 'trips.tracking.depthGapDeclaredTitle'
                      : 'trips.tracking.depthGapTitle',
                    { station: gap.stationName, gap: gap.gapM },
                  )}
                  description={t(
                    gap.declared
                      ? 'trips.tracking.depthGapDeclaredBody'
                      : 'trips.tracking.depthGapBody',
                    {
                      asked: Math.abs(askedDepth ?? 0),
                      station: gap.stationName,
                      depth: gap.stationDepthM,
                    },
                  )}
                  style={{ marginBottom: 12 }}
                  data-testid="trip-tracking-depth-gap"
                />
              )}
              {/* <b>Said whenever the check fails, not only once somebody has asked for the list —
                  because the whole value of the warning above is that nobody has to ask.</b> A card
                  whose reading failed is otherwise pixel-identical to a card whose depth resolved
                  half a metre from a station: no warning either way. Gated behind a button nobody
                  pressed, one dropped request restores the original defect in full — the party is
                  written down at the bottom of the cave and nothing on screen ever disagreed. So
                  the absence of a warning is only allowed to mean "checked, and it is fine"; where
                  it was not checked, that is what is drawn.

                  In the Alert rather than as a passing toast for the same reason: a toast over a
                  form somebody is filling in during a callout is gone by the time they look up. */}
              {askedDepth !== null && reading.error != null && (
                <Alert
                  type="error"
                  showIcon
                  title={t('trips.tracking.depthCheckFailed')}
                  description={
                    <>
                      <div>{trackingProblemMessage(reading.error, t)}</div>
                      {/* What it means for the act in front of them, which the refusal itself does
                          not say: recording is not blocked and resolves on the server's own path,
                          so the number goes in unchecked unless they check it. */}
                      <div>{t('trips.tracking.depthCheckFailedBody')}</div>
                    </>
                  }
                  action={
                    <Button
                      size={controlSize}
                      onClick={() => void reading.refetch()}
                      loading={reading.isFetching}
                      data-testid="trip-tracking-depth-check-again"
                    >
                      {t('common.retry')}
                    </Button>
                  }
                  style={{ marginBottom: 12 }}
                  data-testid="trip-tracking-depth-check-failed"
                />
              )}
              <Flex gap="small" wrap style={{ marginBottom: 12 }}>
                <Button
                  size={controlSize}
                  icon={<AimOutlined />}
                  onClick={() => setListing(true)}
                  loading={listing && reading.isFetching}
                  data-testid="trip-tracking-depth-preview"
                >
                  {t('trips.tracking.depthPreview')}
                </Button>
              </Flex>
              {listing && candidates !== undefined && (
                <div style={{ marginBottom: 12 }} data-testid="trip-tracking-depth-candidates">
                  {candidates.length === 0 ? (
                    <Typography.Text type="secondary">
                      {t('trips.tracking.depthCandidatesNone')}
                    </Typography.Text>
                  ) : (
                    <>
                      <Typography.Text type="secondary">
                        {t('trips.tracking.depthCandidates')}
                      </Typography.Text>
                      <List
                        size="small"
                        dataSource={candidates}
                        renderItem={(candidate) => (
                          <List.Item
                            actions={[
                              <Button
                                key="choose"
                                size={controlSize}
                                onClick={() => onChooseCandidate(candidate)}
                                data-testid={`trip-tracking-depth-choose-${candidate.stationName}`}
                              >
                                {t('trips.tracking.depthCandidateChoose')}
                              </Button>,
                            ]}
                          >
                            <List.Item.Meta
                              title={candidate.stationName}
                              description={`${t('trips.tracking.depthCandidate', {
                                depth: candidate.depthM,
                                delta: candidate.deltaM,
                              })}${candidate.surveyName ? ` · ${candidate.surveyName}` : ''}`}
                            />
                          </List.Item>
                        )}
                      />
                    </>
                  )}
                </div>
              )}
            </>
          )}

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
