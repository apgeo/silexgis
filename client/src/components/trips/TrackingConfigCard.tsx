// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { DeleteOutlined, EditOutlined, PlayCircleOutlined, StopOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  ConfigProvider,
  Descriptions,
  Flex,
  Form,
  Input,
  Popconfirm,
  Select,
  Space,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { isConcurrencyConflict } from '../../api/client.ts';
import {
  surveyModelCanPlaceACaver,
  surveyModelPlacingObstacle,
  useCaveNames,
  useCreateTrackingTeam,
  useDeleteTrackingTeam,
  useRenameTrackingTeam,
  useSetTripTracking,
  useSurveyModelsForCaves,
  type TrackingState,
  type TripCalloutState,
} from '../../api/hooks.ts';
import { placeOnModel } from '../../caveview/drawableOn.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { trackingProblemMessage } from './trackingProblems.ts';

interface ConfigForm {
  surveyModelId: string | null;
  referenceStationName: string;
  depthFilter: string[];
}

/**
 * What an option in a dropdown is worth under a finger.
 *
 * The control that opens the list takes its size from the `size` it is given, but the list itself
 * does not: antd builds an option's height from this token and from nothing else, so a chooser
 * grown to forty pixels still opens onto thirty-two-pixel rows — the target that actually decides
 * whether the right survey gets picked. Given as a component token rather than as a rule pushed
 * into a stylesheet because the option's padding is derived from the same arithmetic, and because
 * the list is drawn in a portal at the end of the document where the card's own selectors cannot
 * reach it.
 */
const COARSE_SELECT = { optionHeight: 40 };

interface Props {
  tripLogId: string;
  /** The caves this trip names, which is where the models a position can be placed in come from. */
  caveIds: readonly string[];
  tracking: TrackingState;
  canEdit: boolean;
  /**
   * Whether this trip has an arrangement to notice that its party has not come back.
   *
   * <b>Read, never written.</b> Tracking and the callout are two separate arrangements and this
   * card changes only one of them; the other is here so that what this card says about it can be
   * true. A statement that sends somebody to "the callout, arranged higher up this trip" is a
   * statement that a callout has been arranged, and on a trip with none it points at a panel that
   * drew nothing — which is a scope statement introducing a second imaginary safety net in the act
   * of retiring the first.
   */
  calloutState: TripCalloutState;
  /** Re-read the watch — called when the server says this panel is holding a stale version. */
  onStale: () => void;
}

/**
 * How the watch is set up, and the two acts that start and end it.
 *
 * The configuration is what every depth on the log was resolved against, so the save here is
 * deliberately a *merge*: a field nobody touched is left out of the request entirely rather than
 * echoed back, and the server keeps what it has. That is not tidiness. Echoing the form back would
 * mean a co-ordinator who may run the trip but may not be told the cave's exact position — who is
 * therefore sent no reference station at all — silently clearing the reference station by pressing
 * Save, and every depth reported afterwards resolving against a different datum with nothing on
 * screen having said so.
 *
 * Closing sends the state and nothing else, for the same reason: ending a watch is not an occasion
 * to rewrite the vocabulary the watch was kept in.
 *
 * The other half of that rule is on the reading side and matters just as much: a configuration
 * kept from this reader must not be *drawn* as one nobody has set. Three empty fields under the
 * words "Choose a survey" and "Left empty, the highest entrance station is used" are an invitation
 * to fill them in, and filling them in is what replaces a datum this person was never shown.
 */
export default function TrackingConfigCard({
  tripLogId,
  caveIds,
  tracking,
  canEdit,
  calloutState,
  onStale,
}: Props) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  // Every control on this card is something somebody presses, and how big it has to be depends on
  // what is pressing it — not on how much room there is. A phone in landscape has the width of a
  // desk and still no pixel precision, so the branch is made once, here, on the pointer.
  const coarse = useCoarsePointer();
  const [form] = Form.useForm<ConfigForm>();
  const save = useSetTripTracking();
  const createTeam = useCreateTrackingTeam();
  const renameTeam = useRenameTrackingTeam();
  const deleteTeam = useDeleteTrackingTeam();
  const [newTeam, setNewTeam] = useState('');
  const [renaming, setRenaming] = useState<{ id: string; title: string } | null>(null);
  const [replacing, setReplacing] = useState(false);

  /**
   * Whether the form as it stands would throw the watch's depth datum and station filter away.
   *
   * Held as state and recomputed as the form is edited, rather than derived during the render from
   * what the fields hold: the sentence it draws has to be on screen while the finger is still over
   * Save, so something has to re-render when a field changes, and the form's own values do not.
   */
  const [modelSwapClearsDatum, setModelSwapClearsDatum] = useState(false);

  const models = useSurveyModelsForCaves(caveIds);
  const caveNames = useCaveNames(caveIds.length > 1 ? caveIds : []);

  const when = (value: string | null) =>
    value ? new Date(value).toLocaleString(i18n.language) : '—';

  const failed = (error: unknown) => {
    if (isConcurrencyConflict(error)) {
      // Somebody else wrote the watch between this panel's read and this save. Re-read rather
      // than report a failure: nothing is wrong except the version in this browser's hand.
      onStale();
      message.warning(trackingProblemMessage(error, t));
      return;
    }
    message.error(trackingProblemMessage(error, t));
  };

  /**
   * The three configuration fields, but only the ones this person actually edited.
   *
   * `undefined` is how a field says "leave mine alone"; the hook turns it into the absence the
   * server merges over. An emptied field is a different statement and is sent as one — an empty
   * string clears the reference station, an empty list clears the filter.
   */
  const editedFields = () => {
    const values = form.getFieldsValue();
    return {
      surveyModelId: form.isFieldTouched('surveyModelId')
        ? (values.surveyModelId ?? null)
        : undefined,
      referenceStationName: form.isFieldTouched('referenceStationName')
        ? (values.referenceStationName ?? '').trim()
        : undefined,
      depthFilter: form.isFieldTouched('depthFilter') ? (values.depthFilter ?? []) : undefined,
    };
  };

  const write = async (state: TrackingState['state'], carryConfig: boolean) => {
    try {
      const saved = await save.mutateAsync({
        tripLogId,
        state,
        ...(carryConfig ? editedFields() : {}),
      });
      message.success(
        state === 'armed'
          ? t('trips.tracking.armedNotice')
          : state === 'closed'
            ? t('trips.tracking.closedNotice')
            : t('common.saved'),
      );
      // Refilled from what the server stored and marked untouched again, so the next save carries
      // only what is edited after this one. Resetting to the values the form was built with would
      // put the previous configuration back on screen a moment after it was replaced.
      form.setFields([
        { name: 'surveyModelId', value: saved.surveyModelId, touched: false },
        {
          name: 'referenceStationName',
          value: saved.referenceStationName ?? '',
          touched: false,
        },
        { name: 'depthFilter', value: saved.depthFilter, touched: false },
      ]);
      // A replacement that has landed is over. If the answer is still withheld, the fields close
      // again rather than staying open over three blanks that mean nothing.
      setReplacing(false);
      // Whatever the form was warning about has now either happened or been carried across, and
      // the fields it was read from have just been refilled from the answer.
      setModelSwapClearsDatum(false);
      // Said again now that it is done, because the warning above is only seen by whoever was
      // looking at the form, and what was lost is not visible in the answer: an empty datum box
      // reads as one nobody ever filled in. Read off what came back rather than off what was sent,
      // so it says this only when the server really did clear something that was there.
      if (
        saved.surveyModelId !== tracking.surveyModelId
        && saved.referenceStationName === null
        && saved.depthFilter.length === 0
        && (tracking.referenceStationName !== null || tracking.depthFilter.length > 0)
      ) {
        message.warning(t('trips.tracking.modelSwapClearedDatumNotice'));
      }
    } catch (error) {
      failed(error);
    }
  };

  const onAddTeam = async () => {
    const title = newTeam.trim();
    if (!title) {
      return;
    }
    try {
      await createTeam.mutateAsync({ tripLogId, title });
      setNewTeam('');
    } catch (error) {
      failed(error);
    }
  };

  const onRenameTeam = async () => {
    if (!renaming || !renaming.title.trim()) {
      return;
    }
    try {
      await renameTeam.mutateAsync({ tripLogId, teamId: renaming.id, title: renaming.title.trim() });
      setRenaming(null);
    } catch (error) {
      failed(error);
    }
  };

  const onDeleteTeam = async (teamId: string) => {
    try {
      await deleteTeam.mutateAsync({ tripLogId, teamId });
    } catch (error) {
      failed(error);
    }
  };

  const armed = tracking.state === 'armed';

  /**
   * How big everything on this card is drawn. `large` is where the forty pixels come from — antd
   * builds it out of `controlHeightLG`, the touch target the rest of this application uses — and
   * asking by size rather than by height means the padding, line height and icon inside each
   * control are built for the size the control believes it is. `middle` is what a desk has always
   * had here and is left exactly as it was.
   */
  const controlSize: 'large' | 'middle' = coarse ? 'large' : 'middle';
  /**
   * The same forty pixels for the glyph-only buttons that live inside a chip or an alert, which
   * are drawn small on a desk and stay that way there — sizing those up with everything else would
   * redraw a surface that has no problem on the machine it was designed for.
   */
  const chipSize: 'large' | 'small' = coarse ? 'large' : 'small';
  /** A confirmation is two more things to press, pressed by the same finger as the rest. */
  const confirmSizes = {
    okButtonProps: { size: controlSize },
    cancelButtonProps: { size: controlSize },
  };
  // Held still across renders: a fresh object is a fresh theme, and every one of those has the
  // whole select's styles derived again.
  const selectTheme = useMemo(
    () => ({ components: { Select: coarse ? COARSE_SELECT : {} } }),
    [coarse],
  );

  /**
   * Whether this answer's configuration was kept back rather than never set.
   *
   * The survey, the reference station and the depth filter are station vocabulary of the tracked
   * cave, so a reader who may not be told that cave's exact position is sent all three as
   * absences — and the same answer raises the withholding flag when it does. Nothing on the answer
   * distinguishes "withheld" from "unset" field by field, but the *shape* of the answer does:
   * a position can only have been withheld if some report claimed a place, a report only claims a
   * place on an armed watch, and a watch cannot be armed without a survey. So a withheld position
   * always arrives beside a survey; an answer with the flag raised and all three fields empty is
   * one whose configuration is being withheld.
   *
   * Drawn as anything else, this card would say a watch has no datum when it has one, and the
   * natural response to that — filling the blanks in — is what silently re-points every depth
   * already on the log.
   */
  const configWithheld =
    tracking.positionsWithheld &&
    tracking.surveyModelId === null &&
    tracking.referenceStationName === null &&
    tracking.depthFilter.length === 0;
  // Locked rather than merely marked, because the field that is easiest to destroy is the one it
  // costs nothing to touch: typing into an empty reference and thinking better of it sends an
  // empty string, which clears a datum this person was never shown. Replacing it stays possible
  // and is now a deliberate act taken with the warning on screen.
  const configLocked = configWithheld && !replacing;

  /**
   * Whether anybody's displayed place was measured in a survey other than the one in use.
   *
   * <b>Said here because the model panel says it only once opened.</b> Re-pointing an armed watch
   * at a corrected survey is allowed — a survey re-imported while a party is underground is a real
   * thing, and refusing it would be answered by closing the watch, the worst available outcome —
   * but it must not be a silent act. Every report already on the log names the survey it was made
   * in, so the moment the model changes those places stop being drawable and the party comes off
   * the model. This is the line that says so on the surface where the change was made.
   *
   * Computed from what the read already sends rather than asked for separately, and asked through
   * the one rule that decides what may be drawn on a model — so a report whose survey has been
   * deleted counts here exactly as a report measured in another survey does, which is what it is.
   *
   * <b>Silent where this watch has no model at all.</b> A reader who may not be told the model is
   * told none of the positions' models either, and a watch whose survey was deleted says so in its
   * own words beside the state. Both would otherwise raise this as a change of survey, which is
   * either a warning about something the reader cannot see or a second, vaguer account of
   * something already said plainly.
   */
  const positionsOnOtherModel =
    tracking.surveyModelId !== null
    && tracking.participants.some(
      (participant) =>
        placeOnModel(
          {
            stationName: participant.stationName,
            depthM: participant.depthM,
            surveyModelId: participant.positionSurveyModelId,
          },
          tracking.surveyModelId,
        )?.kind === 'otherModel',
    );

  /**
   * The surveys this watch may actually be pointed at.
   *
   * <b>A survey with no stations is not a choice, it is a trap.</b> The cave's list holds every
   * file ever uploaded to it, whatever became of the upload — a reading that failed stays on the
   * list with its name and its date, looking exactly like one that worked. Offered here it can be
   * chosen and armed in two presses, and the result is a watch that says tracking is on, accepts
   * every report anybody relays, and can place nobody on anything, for as long as the party is
   * underground. Failed readings are not exotic enough to leave to chance: most survey files that
   * arrive in this application do not import cleanly.
   *
   * <b>Narrowed here rather than in the request.</b> The same list answers the cave page, which
   * has to show a failed reading — that is where somebody sees it failed and uploads it again —
   * and the 3D scene, which chooses a wall mesh this would exclude. What is narrow is the question
   * this control asks, so this is where the narrowing belongs.
   */
  const placeable = useMemo(() => models.data.filter(surveyModelCanPlaceACaver), [models.data]);

  /**
   * The survey this watch is on, and — if nobody can be placed on it — which of the three reasons
   * that is.
   *
   * <b>Hiding such a survey from the chooser does not unpoint a watch that is already on one</b> —
   * armed before this rule existed, or armed on a reading that has since been replaced by one that
   * failed. Silence there is the worse half of the same failure: the model panel draws nothing, the
   * party never appears, and every report still reports successfully, so the surface that is wrong
   * is the one showing no sign of it.
   *
   * <b>The reason is carried, not flattened to a yes.</b> Three different surveys cannot be placed
   * on and the server refuses none of them: a wall mesh, whose conversion may have gone perfectly;
   * a reading still queued or running; a reading that failed. Answered with one sentence, the two
   * that are not a failure are told their import did not come through — one of them about a file
   * that will never hold a station however often it is imported, the other about a job that is
   * running as the sentence is read.
   *
   * Read out of the list this card already holds rather than asked for separately, and deliberately
   * quiet in the two cases that are not this one: while the list is still arriving nothing is known
   * yet, and a survey that is absent from the list is either deleted — which says so in its own
   * words beside the state — or withheld from this reader, who is told about the configuration at
   * all only in the terms the withholding allows.
   */
  const watchedModel =
    tracking.surveyModelId === null || models.isPending
      ? undefined
      : models.data.find((model) => model.id === tracking.surveyModelId);
  const armedModelObstacle =
    watchedModel === undefined ? null : surveyModelPlacingObstacle(watchedModel);

  /**
   * What the chooser offers, plus the survey the watch is on when that one is not among them.
   *
   * <b>A chooser that does not hold the value it is showing shows the value raw.</b> The control
   * looks its selection up among its options to find a label and falls back to the value itself
   * when there is none — so narrowing the list to the placeable surveys turned the Survey field of
   * exactly the watches this change exists to rescue into a bare identifier. That lands on the one
   * co-ordinator who has just been told the survey cannot place anybody and has opened this form
   * because of it: the field naming the survey becomes a hex string, and the obvious response to a
   * hex string in a clearable field is to press the cross, which un-points the watch on the next
   * save.
   *
   * So the survey stays named and stays marked with why it cannot be used, and is offered as
   * something already chosen rather than as something choosable — the narrowing is kept, since the
   * point of it is that this is not a choice anybody should be able to make afresh.
   */
  const modelLabel = (model: { name: string; caveId: string }) =>
    caveNames.get(model.caveId) ? `${model.name} · ${caveNames.get(model.caveId)}` : model.name;
  const modelOptions: { value: string; label: string; disabled?: boolean }[] = [
    ...(watchedModel !== undefined && armedModelObstacle !== null
      ? [
          {
            value: watchedModel.id,
            label: `${modelLabel(watchedModel)} — ${t(
              `trips.tracking.modelObstacleOption.${armedModelObstacle}`,
            )}`,
            disabled: true,
          },
        ]
      : []),
    ...placeable.map((model) => ({ value: model.id, label: modelLabel(model) })),
  ];

  /**
   * Whether the trip has a callout arranged, as this card is allowed to know it.
   *
   * The same two states the callout panel itself draws for, read the same way. Nothing here
   * arranges or changes one — this decides only which of two true sentences the scope statement
   * says, because "the callout, arranged higher up this trip" is a claim, and on a trip whose
   * callout is `none` the thing it points at rendered nothing at all for a reader who may not
   * edit, and a button rather than an arrangement for one who may.
   */
  const calloutArranged =
    calloutState === 'armed' || calloutState === 'overdue' || calloutState === 'stoodDown';

  /**
   * Why the chooser has nothing in it, when the cave has surveys and none of them can carry a
   * position.
   *
   * <b>Waiting fixes one of these and never fixes the other, so they cannot share a sentence.</b>
   * A cave whose readings are still running has a chooser that will fill itself in within
   * seconds — the list polls while anything on it is unsettled — and telling its owner to import
   * the files again sends them to undo a job that was about to finish. A cave whose readings are
   * a wall mesh and a failure has a chooser that will stay empty until somebody acts.
   */
  const chooserEmptyAndWaiting =
    models.data.some((model) => surveyModelPlacingObstacle(model) === 'stillReading');

  /**
   * Whether saving the form as it now stands would throw the depth datum and the station filter
   * away — asked of the form whenever a field on it changes.
   *
   * <b>Both name stations of the survey in use, so both go when the survey does.</b> A reference
   * station is a station of one model and a depth filter is a list of them; carried across to
   * another survey they would resolve depths against names that mean nothing there, or — worse —
   * against names that happen to exist and mean somewhere else. The server therefore clears them
   * whenever the model changes and no new reference is given in the same act, which is the right
   * thing to do and was the silent thing to do.
   *
   * <b>Silent is what this repairs, and the cost of the silence is a refusal mid-callout.</b> With
   * no datum, the next depth report is answered "the depth datum cannot be established" — during a
   * callout, to somebody who has just relayed "Ana at 120 metres" and has no idea that changing
   * the survey half an hour ago is why. So the loss is said before the act, in the same breath as
   * the act: a swap remains allowed, as it must be, and stops being a surprise.
   *
   * The condition is the server's own, read the same way it will be sent: a model that is actually
   * different, and no reference station *going with this save*. What counts is whether the datum
   * field will be part of the request at all — the box is pre-filled with the stored datum and an
   * untouched field is deliberately left out of the request, so reading the box's contents rather
   * than whether somebody edited it would see a datum in the request that is not in it. Typing one
   * is how a coordinator carries the datum across deliberately, and nothing is lost in that case.
   */
  const swapWouldClearDatum = () => {
    const values = form.getFieldsValue();
    const chosen = values.surveyModelId ?? null;
    const referenceGoingWithIt =
      form.isFieldTouched('referenceStationName')
      && (values.referenceStationName ?? '').trim().length > 0;
    return (
      chosen !== null
      && chosen !== tracking.surveyModelId
      && !referenceGoingWithIt
      && (tracking.referenceStationName !== null || tracking.depthFilter.length > 0)
    );
  };

  return (
    <Card size="small" title={t('trips.tracking.setup')} style={{ marginBottom: 16 }}>
      <Descriptions column={1} size="small" data-testid="trip-tracking-state">
        <Descriptions.Item label={t('trips.tracking.state')}>
          <Tag color={armed ? 'blue' : 'default'}>
            {t(`trips.tracking.stateValues.${tracking.state}`)}
          </Tag>
          {/* Beside the word "Armed" rather than anywhere else on the page, because that word is
              what goes wrong: a watch whose survey has been deleted keeps saying it, keeps taking
              entries and notes, and can place nobody. The state is still Armed and this does not
              pretend otherwise — it says what that is now worth. */}
          {tracking.surveyModelMissing && (
            <Tag color="error" data-testid="trip-tracking-model-missing-tag">
              {t('trips.tracking.modelMissingTag')}
            </Tag>
          )}
          {/* Beside the state for the same reason the one above it is: the state is the word that
              is wrong. A watch pointed at a survey with no stations is on, is taking reports, and
              is placing nobody, and the only place a reader meets that claim is here.

              Coloured by whether waiting fixes it: a reading still running is a normal moment in
              the life of an upload and goes on to place people by itself, so it is drawn as a
              remark rather than as a fault. */}
          {armedModelObstacle !== null && (
            <Tag
              color={armedModelObstacle === 'stillReading' ? 'processing' : 'error'}
              data-testid="trip-tracking-model-unplaceable-tag"
            >
              {t(`trips.tracking.modelObstacleTag.${armedModelObstacle}`)}
            </Tag>
          )}
        </Descriptions.Item>
        {tracking.armedAt && (
          <Descriptions.Item label={t('trips.tracking.armedAt')}>
            {when(tracking.armedAt)}
          </Descriptions.Item>
        )}
        {tracking.closedAt && (
          <Descriptions.Item label={t('trips.tracking.closedAt')}>
            {when(tracking.closedAt)}
          </Descriptions.Item>
        )}
      </Descriptions>

      {/* <b>What this watch is, said where the watch is turned on.</b>
          Tracking keeps a record of where each person was last reported. It watches no clock and
          raises no alarm, and the product's thing that does — the callout — is a separate
          arrangement that starting a watch neither makes nor asks about. In a caving application,
          a live list of people underground reads as a safety net whatever it is called, so a club
          could run a whole watch on a party that nothing in this installation will ever notice is
          missing.

          Stated for every reader and in both states rather than only to the person arming, and
          rather than only while a watch is on: whoever is reading a live watch is entitled to
          know what it is doing on their behalf, and the moment the claim has to be right is the
          moment before somebody relies on it. Info rather than warning, deliberately — nothing is
          wrong here, and a page that alarms about its own ordinary state teaches people to read
          past its alarms. */}
      <Alert
        type="info"
        showIcon
        title={t('trips.tracking.scopeTitle')}
        description={
          /* The second half of this — where the thing that *does* raise an alarm lives — is a
             claim about this trip, not a general fact, so it is said in whichever of the two forms
             is true here. Pointing an unprotected reader at "the callout, arranged higher up this
             trip" on a trip that has none sends them to a panel that drew nothing, and retires one
             imaginary safety net by inventing another. */
          calloutArranged
            ? t('trips.tracking.scopeBodyWithCallout')
            : t('trips.tracking.scopeBodyNoCallout')
        }
        style={{ marginBottom: 12 }}
        data-testid="trip-tracking-scope"
      />

      {/* Outside the editing block on purpose: whoever is reading this watch needs to know that
          the places on it were measured against a different survey, whether or not they are the
          person who may change one. */}
      {armedModelObstacle !== null && (
        <Alert
          /* A reading still running is not a fault and is not drawn as one: it is the ordinary
             middle of an upload, it ends by itself, and the only thing worth saying is that
             nobody can be placed until it does. The other two need somebody to act. */
          type={armedModelObstacle === 'stillReading' ? 'info' : 'error'}
          showIcon
          title={t(`trips.tracking.modelObstacleTitle.${armedModelObstacle}`)}
          description={t(`trips.tracking.modelObstacleBody.${armedModelObstacle}`)}
          style={{ marginBottom: 12 }}
          data-testid="trip-tracking-model-unplaceable"
        />
      )}

      {positionsOnOtherModel && (
        <Alert
          type="warning"
          showIcon
          message={t('trips.tracking.positionsOnOtherModelTitle')}
          description={t('trips.tracking.positionsOnOtherModelBody')}
          style={{ marginBottom: 12 }}
          data-testid="trip-tracking-positions-other-model"
        />
      )}

      {canEdit && (
        <>
          {configWithheld && (
            <Alert
              type="warning"
              showIcon
              message={t('trips.tracking.configWithheldTitle')}
              description={t('trips.tracking.configWithheldBody')}
              style={{ marginBottom: 12 }}
              data-testid="trip-tracking-config-withheld"
              action={
                configLocked ? (
                  <Button
                    size={chipSize}
                    onClick={() => setReplacing(true)}
                    data-testid="trip-tracking-config-replace"
                  >
                    {t('trips.tracking.configReplace')}
                  </Button>
                ) : undefined
              }
            />
          )}
          {/* Wrapped out here rather than around the chooser itself: a `Form.Item` hands its value
              and its change handler to the one element it is given, so anything put between the
              two takes them instead of the control. */}
          <ConfigProvider theme={selectTheme}>
            <Form<ConfigForm>
              form={form}
              layout="vertical"
              requiredMark={false}
              size={controlSize}
              disabled={configLocked}
              // Every edit re-asks the one question this form can answer that its answer cannot:
              // whether saving it now would take the datum with the survey.
              onValuesChange={() => setModelSwapClearsDatum(swapWouldClearDatum())}
              initialValues={{
                surveyModelId: tracking.surveyModelId,
                referenceStationName: tracking.referenceStationName ?? '',
                depthFilter: tracking.depthFilter,
              }}
            >
              <Form.Item
                name="surveyModelId"
                label={t('trips.tracking.surveyModel')}
                extra={t('trips.tracking.surveyModelHelp')}
              >
                <Select
                  allowClear
                  showSearch
                  optionFilterProp="label"
                  loading={models.isPending}
                  placeholder={t('trips.tracking.surveyModelPlaceholder')}
                  data-testid="trip-tracking-model"
                  options={modelOptions}
                />
              </Form.Item>
              {/* <b>An empty chooser has to say why it is empty.</b> Narrowing the list to the
                  surveys somebody can actually be placed on turns a cave whose readings all failed
                  from a list of useless choices into no choices at all — and an empty dropdown over
                  the words "Choose a survey" reads as a cave with no surveys, which is a different
                  problem with a different answer. This is the case where the cave has files and not
                  one of them was read right through, which is answered by importing one again, not
                  by uploading a first. */}
              {!models.isPending && models.data.length > 0 && placeable.length === 0 && (
                <Alert
                  type={chooserEmptyAndWaiting ? 'info' : 'warning'}
                  showIcon
                  title={
                    chooserEmptyAndWaiting
                      ? t('trips.tracking.noPlaceableModelStillReadingTitle')
                      : t('trips.tracking.noPlaceableModelTitle')
                  }
                  description={
                    chooserEmptyAndWaiting
                      ? t('trips.tracking.noPlaceableModelStillReadingBody')
                      : t('trips.tracking.noPlaceableModelBody')
                  }
                  style={{ marginBottom: 12 }}
                  data-testid="trip-tracking-no-placeable-model"
                />
              )}
              {modelSwapClearsDatum && (
                <Alert
                  type="warning"
                  showIcon
                  message={t('trips.tracking.modelSwapClearsDatumTitle')}
                  description={t('trips.tracking.modelSwapClearsDatumBody')}
                  style={{ marginBottom: 12 }}
                  data-testid="trip-tracking-model-swap-clears-datum"
                />
              )}
              <Form.Item
                name="referenceStationName"
                label={t('trips.tracking.referenceStation')}
                extra={t('trips.tracking.referenceStationHelp')}
              >
                <Input allowClear data-testid="trip-tracking-reference" />
              </Form.Item>
              <Form.Item
                name="depthFilter"
                label={t('trips.tracking.depthFilter')}
                extra={t('trips.tracking.depthFilterHelp')}
              >
                <Select
                  mode="tags"
                  allowClear
                  open={false}
                  suffixIcon={null}
                  placeholder={t('trips.tracking.depthFilterPlaceholder')}
                  data-testid="trip-tracking-depth-filter"
                />
              </Form.Item>
            </Form>
          </ConfigProvider>

          {/* <b>The same truth again, in one line, under the finger that is about to act.</b> The
              box above states what a watch is; this states what pressing this button does not do,
              at the only moment the distinction can still change what somebody does about it. Said
              only while there is no watch on, because that is when this button is the one that
              starts one — once tracking is running, repeating it beside Save would be noise beneath
              a box that has already said it.

              On a phone the box above is several screens away by the time this button is reached,
              which is the whole reason a line here is not a duplicate. */}
          {!armed && (
            <Typography.Paragraph
              type="secondary"
              style={{ marginBottom: 8 }}
              data-testid="trip-tracking-arm-scope"
            >
              {/* Which of the two is true here, for the same reason the box above chooses: told
                  "set a callout on this trip as well" on a trip that already has one, a
                  co-ordinator goes to arrange a second arrangement that does not exist. */}
              {calloutArranged
                ? t('trips.tracking.scopeAtArmingWithCallout')
                : t('trips.tracking.scopeAtArming')}
            </Typography.Paragraph>
          )}

          <Flex gap="small" wrap style={{ marginBottom: 16 }}>
            <Button
              size={controlSize}
              onClick={() => void write(tracking.state, true)}
              loading={save.isPending}
              data-testid="trip-tracking-save"
            >
              {t('common.save')}
            </Button>
            {/* Arming carries the form with it, so choosing a model and starting the watch is one
                act rather than two — a watch armed against nothing is refused by the server, and
                asking somebody to save first would make that the ordinary path. */}
            {!armed && (
              <Button
                type="primary"
                size={controlSize}
                icon={<PlayCircleOutlined />}
                onClick={() => void write('armed', true)}
                loading={save.isPending}
                data-testid="trip-tracking-arm"
              >
                {tracking.state === 'closed' ? t('trips.tracking.rearm') : t('trips.tracking.arm')}
              </Button>
            )}
            {armed && (
              <Popconfirm
                title={t('trips.tracking.closeConfirm')}
                onConfirm={() => void write('closed', false)}
                {...confirmSizes}
              >
                <Button size={controlSize} icon={<StopOutlined />} data-testid="trip-tracking-close">
                  {t('trips.tracking.close')}
                </Button>
              </Popconfirm>
            )}
          </Flex>
        </>
      )}

      {/* Teams label reports and nothing else. Deleting one leaves every report it labelled where
          it is, holding the caver it was always about — which is why this is offered at all
          rather than being treated as a destructive act on the log. */}
      <Typography.Text strong>{t('trips.tracking.teams')}</Typography.Text>
      <div style={{ marginTop: 8 }} data-testid="trip-tracking-teams">
        {tracking.teams.length === 0 ? (
          <Typography.Text type="secondary">{t('trips.tracking.teamsNone')}</Typography.Text>
        ) : (
          <Flex gap={8} wrap align="center">
            {/* The chip grows to hold its own buttons, so a finger-sized rename and delete simply
                make a taller chip rather than a pair of targets crammed into a 26px one. */}
            {tracking.teams.map((team) => (
              <Tag key={team.id} className="tracking-team-tag">
                {team.title}
                {canEdit && (
                  <>
                    <Button
                      type="link"
                      size={chipSize}
                      icon={<EditOutlined />}
                      aria-label={t('trips.tracking.teamRename')}
                      onClick={() => setRenaming({ id: team.id, title: team.title })}
                      data-testid={`trip-tracking-team-rename-${team.id}`}
                    />
                    <Popconfirm
                      title={t('trips.tracking.teamDeleteConfirm')}
                      onConfirm={() => void onDeleteTeam(team.id)}
                      {...confirmSizes}
                    >
                      <Button
                        type="link"
                        size={chipSize}
                        icon={<DeleteOutlined />}
                        aria-label={t('trips.tracking.teamDelete')}
                        data-testid={`trip-tracking-team-delete-${team.id}`}
                      />
                    </Popconfirm>
                  </>
                )}
              </Tag>
            ))}
          </Flex>
        )}
      </div>

      {canEdit && (
        <Space.Compact style={{ width: '100%', marginTop: 8 }} size={controlSize}>
          <Input
            value={renaming ? renaming.title : newTeam}
            onChange={(event) =>
              renaming
                ? setRenaming({ ...renaming, title: event.target.value })
                : setNewTeam(event.target.value)
            }
            placeholder={t('trips.tracking.teamTitlePlaceholder')}
            data-testid="trip-tracking-team-title"
          />
          <Button
            onClick={() => void (renaming ? onRenameTeam() : onAddTeam())}
            loading={createTeam.isPending || renameTeam.isPending}
            data-testid="trip-tracking-team-submit"
          >
            {renaming ? t('trips.tracking.teamRename') : t('trips.tracking.teamAdd')}
          </Button>
          {renaming && (
            <Button onClick={() => setRenaming(null)} data-testid="trip-tracking-team-cancel">
              {t('common.cancel')}
            </Button>
          )}
        </Space.Compact>
      )}
    </Card>
  );
}
