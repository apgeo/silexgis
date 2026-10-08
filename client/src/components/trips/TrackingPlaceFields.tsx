// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useId, useState } from 'react';
import { AimOutlined } from '@ant-design/icons';
import { Alert, Button, Flex, Form, InputNumber, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import {
  useTrackingDepthReading,
  useTrackingPlaces,
  type TrackingDepthCandidate,
  type TripPositionEventKind,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import SurveyStationInput from '../caves/SurveyStationInput.tsx';
import List from '../List.tsx';
import { trackingDepthGap } from './trackingDepthGap.ts';
import { trackingProblemMessage } from './trackingProblems.ts';
import { trackingStationRules, trackingToStationRules } from './trackingReport.ts';

/** How big the controls are drawn — decided by the pointer, by the surface around this. */
type ControlSize = 'large' | 'middle';

interface StationFieldProps {
  /**
   * What every element this draws is named by, for the tests and the browser checks that drive
   * each surface apart: the report card, the correction dialog and the dialog a station press
   * opens each have their own.
   */
  idPrefix: string;
  /**
   * The watch's own survey, whose stations are offered while a name is typed — or null where the
   * reader is not told which survey that is, and the field is then a plain text box.
   *
   * <b>Always the watch's survey and never one somebody is merely looking at,</b> because that is
   * the survey a report is measured against when it is saved: names offered from any other would
   * be names the save then refuses, or worse, accepts as a different place.
   */
  surveyModelId: string | null;
  /** What is said under the field. The surfaces differ in why somebody is typing a station. */
  help: string;
}

/**
 * The station a report is at, as a form field.
 *
 * On its own for the one surface that asks for nothing else about a place: the dialog a station
 * press opens states the pressed station and only turns it into a field when the server has
 * refused that spelling. It is the same field the card and the correction dialog draw, so a name
 * one of them accepts is never one another would have refused.
 */
export function TrackingStationField({ idPrefix, surveyModelId, help }: StationFieldProps) {
  const { t } = useTranslation();
  return (
    <Form.Item
      name="stationName"
      label={t('trips.tracking.reportStation')}
      extra={help}
      rules={trackingStationRules(t)}
    >
      <SurveyStationInput surveyModelId={surveyModelId} data-testid={`${idPrefix}-station`} />
    </Form.Item>
  );
}

interface Props {
  tripLogId: string;
  /** What the form around this says is being reported. Only two kinds claim a place. */
  kind: TripPositionEventKind;
  size: ControlSize;
  idPrefix: string;
  /** See {@link StationFieldProps.surveyModelId}. */
  surveyModelId: string | null;
  /**
   * The cave whose declared places the watch offers, so that a cave declaring nothing can be
   * pointed at — or null where the reader is not told which cave it is, and nothing is pointed at.
   */
  caveId?: string | null;
  /**
   * The depth the form opens holding, where it opens on a report that already has one.
   *
   * <b>What makes the warning below appear at once on a correction.</b> The depth is asked about
   * only after it has stopped changing, and a form's own values are not readable on the render
   * that mounts it — so without this, a correction opened on a depth recorded a long way from any
   * station would sit for a moment looking exactly like one recorded half a metre from one, and
   * that moment is when somebody reads the dialog.
   */
  seedDepth?: number | null;
}

/** The fields this block reads and writes, by the names every surface's form gives them. */
interface PlaceFormValues {
  kind: TripPositionEventKind;
  stationName?: string;
  toStationName?: string;
  depthM?: number | null;
}

/**
 * Where a report says somebody is: a declared place, a station, or a depth.
 *
 * <b>One block, because three surfaces ask this and used to ask it three ways.</b> The card under
 * the watch offered the cave's declared places, checked a typed depth against the survey and
 * warned when it landed far from anything; the dialog for correcting a report already on the log
 * offered a bare station box and a bare number. So the surface used to repair a wrong place was
 * the one that could not show why it was wrong, or offer the place by its name. What a place may
 * be is one question, and it is asked here once.
 *
 * It is drawn inside the form of whichever surface uses it and reads that form — the kind, the
 * station and the depth are fields of the report, not of this block — so each surface still sends
 * exactly what its own form holds.
 *
 * <b>A depth is offered a preview before it is recorded.</b> The server turns a depth into the
 * nearest station under the trip's filter and datum, and "nearest" is a decision somebody should
 * watch being made. Each station the preview offers can then be taken as the answer: the whole
 * reason to ask which station a depth means is that you may want to report that station instead.
 *
 * <b>And a depth that lands a long way from anything is said without being asked.</b> Resolution
 * has no tolerance in it: the nearest station to 1200 m in a 140 m cave is the bottom of the cave,
 * stored as confidently as a station half a metre away. So the same answer the preview is built on
 * is asked for as the number settles, and a gap too wide to be an estimate is drawn — as a
 * warning, never as a refusal, because a coordinator with better information than this page has
 * must still be able to record what they were told.
 */
export default function TrackingPlaceFields({
  tripLogId,
  kind,
  size,
  idPrefix,
  surveyModelId,
  caveId,
  seedDepth,
}: Props) {
  const { t } = useTranslation();
  const form = Form.useFormInstance<PlaceFormValues>();
  const watchedDepth = Form.useWatch('depthM', form);
  // Undefined is the form not having answered yet, which is the one moment the seed stands in for
  // it. An emptied field answers null, and null is then the answer.
  const depthTyped = watchedDepth === undefined ? (seedDepth ?? null) : watchedDepth;
  const claimsPlace = kind === 'atStation' || kind === 'atDepth';
  /** Whether the list of stations the depth could mean has been asked for. */
  const [listing, setListing] = useState(false);
  /**
   * What ties the place chooser to its label. That chooser is the one field here the form does not
   * hold — its value is read off the depth field instead — and a form item with no field name
   * gives its label no `for`: a screen reader then announces a bare "combobox" for the quickest
   * way there is to report. Minted per block rather than written down, so two surfaces open at
   * once cannot hand one's label to the other's chooser.
   */
  const placeFieldId = useId();

  /**
   * The depth to ask about, once somebody has stopped typing it.
   *
   * <b>Debounced because every intermediate number is a real depth somewhere.</b> Typing 120 passes
   * through 1 and 12, and asking about each of them would be three requests and, worse, a warning
   * flickering on and off under a field somebody is still filling in. A third of a second after the
   * number settles is late enough to be quiet and early enough to be on screen before a hand moves
   * from the keyboard to the button.
   */
  const askedDepth = useDebouncedValue(
    kind === 'atDepth' && typeof depthTyped === 'number' && Number.isFinite(depthTyped)
      ? depthTyped
      : null,
  );

  /**
   * What that depth means, asked of the one place that knows.
   *
   * A watch cannot be started without a survey, and closing it keeps the survey — so wherever a
   * report can be written at all there is one to resolve against.
   */
  const reading = useTrackingDepthReading(tripLogId, askedDepth);
  const candidates: TrackingDepthCandidate[] | undefined = reading.data;

  /**
   * The places this cave has declared, which are what somebody on the phone actually names.
   *
   * Asked for whenever the block is drawn, because the chooser is the fastest way to report and a
   * list that arrived only after somebody started typing a depth would never be used. A cave that
   * has declared nothing answers an empty list, and then no chooser is drawn — an empty select is
   * worse than no select — and a line says where places are declared instead.
   */
  const places = useTrackingPlaces(tripLogId);
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

  // The list belongs to the depth it was asked about. Once the report is of another kind, or the
  // depth has been emptied — which is what a report landing does to it — the list is let go, so
  // the next depth typed is not met by an answer nobody asked for.
  const depthEmpty = depthTyped === null;
  useEffect(() => setListing(false), [kind, depthEmpty]);

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

    form.setFieldsValue({
      kind: 'atDepth',
      depthM,
      stationName: undefined,
      // A stretch belongs to the station report this stops being.
      toStationName: undefined,
    });
    setListing(false);
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
    form.setFieldsValue({
      kind: 'atStation',
      stationName: candidate.stationName,
      // Whatever far end an earlier station report left in the form was said about another
      // station, and would otherwise come back beside this one as if somebody had chosen it.
      toStationName: undefined,
    });
    setListing(false);
  };

  if (!claimsPlace) {
    return null;
  }

  const placeName = (place: (typeof declared)[number]) =>
    place.placeLabel
      ? t('trips.tracking.reportPlaceOption', { place: place.placeLabel, depth: place.depthM })
      : t('trips.tracking.reportPlaceOptionUnnamed', {
          station: place.stationName,
          depth: place.depthM,
        });

  return (
    <>
      {/* Before either of the two fields below, because it is the one most reports are made with:
          a caver relaying word says where they are by name, and a chooser that came after the
          depth field would be read as an afterthought to it. Drawn only where the cave has
          declared something. */}
      {declared.length > 0 && (
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
            data-testid={`${idPrefix}-place`}
            value={chosenPlaceDepth}
            onChange={choosePlace}
            options={declared.map((place) => ({
              // The depth is the value because the depth is what gets recorded: the server
              // consults the same declarations and answers with the station the club named,
              // so what lands on the log is the declared place and not a second guess at it.
              value: place.depthM,
              // A declaration naming a station the watch's survey does not hold is not followed:
              // the depth then lands on whichever station is nearest, like any undeclared depth.
              // Said in the list, where the choice is made, rather than discovered on the log.
              label:
                place.stationInModel === false
                  ? t('trips.tracking.reportPlaceOptionMissing', { place: placeName(place) })
                  : placeName(place),
            }))}
          />
        </Form.Item>
      )}
      {/* Only once the cave has answered that it declares nothing — never while the answer is
          still coming, and never where it was refused: "this cave declares no places" said to
          somebody who was simply not told would be a false statement about the cave. */}
      {places.data !== undefined && declared.length === 0 && (
        <Typography.Paragraph
          type="secondary"
          style={{ marginBottom: 12 }}
          data-testid={`${idPrefix}-place-none`}
        >
          {t('trips.tracking.reportPlaceNone')}
          {caveId != null && (
            <>
              {' '}
              <Link to={`/caves/${caveId}`} data-testid={`${idPrefix}-place-none-cave`}>
                {t('trips.tracking.reportPlaceNoneLink')}
              </Link>
            </>
          )}
        </Typography.Paragraph>
      )}

      {kind === 'atStation' && (
        <TrackingStationField
          idPrefix={idPrefix}
          surveyModelId={surveyModelId}
          help={t('trips.tracking.reportStationHelp')}
        />
      )}
      {/* The far end of a stretch, under the station it starts from. Word from underground is
          often "past the pitch, not yet at the sump", and either station alone would write down a
          place nobody reported. Offered from the survey's names exactly as the first is, and
          optional: left empty, this is a report at the one station above. */}
      {kind === 'atStation' && (
        <Form.Item
          name="toStationName"
          label={t('trips.tracking.reportToStation')}
          extra={t('trips.tracking.reportToStationHelp')}
          dependencies={['stationName']}
          rules={trackingToStationRules(t)}
        >
          <SurveyStationInput
            surveyModelId={surveyModelId}
            data-testid={`${idPrefix}-to-station`}
          />
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
            <InputNumber style={{ width: '100%' }} data-testid={`${idPrefix}-depth`} />
          </Form.Item>
          {/* Said while the answer is still coming, because silence has to mean one thing.
              Everything below turns on a warning being absent, and absence is only worth
              trusting if it cannot also mean "not asked yet" — a number typed and the button
              pressed within the second is exactly the hurry a callout is conducted in. */}
          {askedDepth !== null && reading.data === undefined && reading.error == null && (
            <Typography.Text
              type="secondary"
              style={{ display: 'block', marginBottom: 12 }}
              data-testid={`${idPrefix}-depth-checking`}
            >
              {t('trips.tracking.depthChecking')}
            </Typography.Text>
          )}
          {/* <b>Drawn without being asked for, and this is the half of the preview that catches
              the typo.</b> The button below opens a list somebody chooses from; this appears on
              its own when what the depth resolves to is further away than any estimate could
              be. It is a warning and not a bar: the number may be right and the survey thin,
              and a coordinator who knows that must still be able to record what they heard. */}
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
                gap.declared ? 'trips.tracking.depthGapDeclaredBody' : 'trips.tracking.depthGapBody',
                {
                  asked: Math.abs(askedDepth ?? 0),
                  station: gap.stationName,
                  depth: gap.stationDepthM,
                },
              )}
              style={{ marginBottom: 12 }}
              data-testid={`${idPrefix}-depth-gap`}
            />
          )}
          {/* <b>Said whenever the check fails, not only once somebody has asked for the list —
              because the whole value of the warning above is that nobody has to ask.</b> A form
              whose reading failed is otherwise pixel-identical to one whose depth resolved half
              a metre from a station: no warning either way. So the absence of a warning is only
              allowed to mean "checked, and it is fine"; where it was not checked, that is what
              is drawn.

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
                      not say: saving is not blocked and resolves on the server's own path, so
                      the number goes in unchecked unless they check it. */}
                  <div>{t('trips.tracking.depthCheckFailedBody')}</div>
                </>
              }
              action={
                <Button
                  size={size}
                  onClick={() => void reading.refetch()}
                  loading={reading.isFetching}
                  data-testid={`${idPrefix}-depth-check-again`}
                >
                  {t('common.retry')}
                </Button>
              }
              style={{ marginBottom: 12 }}
              data-testid={`${idPrefix}-depth-check-failed`}
            />
          )}
          <Flex gap="small" wrap style={{ marginBottom: 12 }}>
            <Button
              size={size}
              icon={<AimOutlined />}
              onClick={() => setListing(true)}
              loading={listing && reading.isFetching}
              data-testid={`${idPrefix}-depth-preview`}
            >
              {t('trips.tracking.depthPreview')}
            </Button>
          </Flex>
          {listing && candidates !== undefined && (
            <div style={{ marginBottom: 12 }} data-testid={`${idPrefix}-depth-candidates`}>
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
                            size={size}
                            onClick={() => onChooseCandidate(candidate)}
                            data-testid={`${idPrefix}-depth-choose-${candidate.stationName}`}
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
    </>
  );
}
