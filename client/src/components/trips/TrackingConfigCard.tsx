// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined, PlayCircleOutlined, StopOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
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
  useCaveNames,
  useCreateTrackingTeam,
  useDeleteTrackingTeam,
  useRenameTrackingTeam,
  useSetTripTracking,
  useSurveyModelsForCaves,
  type TrackingState,
} from '../../api/hooks.ts';
import { trackingProblemMessage } from './trackingProblems.ts';

interface ConfigForm {
  surveyModelId: string | null;
  referenceStationName: string;
  depthFilter: string[];
}

interface Props {
  tripLogId: string;
  /** The caves this trip names, which is where the models a position can be placed in come from. */
  caveIds: readonly string[];
  tracking: TrackingState;
  canEdit: boolean;
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
  onStale,
}: Props) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<ConfigForm>();
  const save = useSetTripTracking();
  const createTeam = useCreateTrackingTeam();
  const renameTeam = useRenameTrackingTeam();
  const deleteTeam = useDeleteTrackingTeam();
  const [newTeam, setNewTeam] = useState('');
  const [renaming, setRenaming] = useState<{ id: string; title: string } | null>(null);
  const [replacing, setReplacing] = useState(false);

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

  return (
    <Card size="small" title={t('trips.tracking.setup')} style={{ marginBottom: 16 }}>
      <Descriptions column={1} size="small" data-testid="trip-tracking-state">
        <Descriptions.Item label={t('trips.tracking.state')}>
          <Tag color={armed ? 'blue' : 'default'}>
            {t(`trips.tracking.stateValues.${tracking.state}`)}
          </Tag>
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
                    size="small"
                    onClick={() => setReplacing(true)}
                    data-testid="trip-tracking-config-replace"
                  >
                    {t('trips.tracking.configReplace')}
                  </Button>
                ) : undefined
              }
            />
          )}
          <Form<ConfigForm>
            form={form}
            layout="vertical"
            requiredMark={false}
            disabled={configLocked}
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
                options={models.data.map((model) => ({
                  value: model.id,
                  label: caveNames.get(model.caveId)
                    ? `${model.name} · ${caveNames.get(model.caveId)}`
                    : model.name,
                }))}
              />
            </Form.Item>
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

          <Flex gap="small" wrap style={{ marginBottom: 16 }}>
            <Button
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
              >
                <Button icon={<StopOutlined />} data-testid="trip-tracking-close">
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
          <Flex gap={8} wrap>
            {tracking.teams.map((team) => (
              <Tag key={team.id}>
                {team.title}
                {canEdit && (
                  <>
                    <Button
                      type="link"
                      size="small"
                      icon={<EditOutlined />}
                      aria-label={t('trips.tracking.teamRename')}
                      onClick={() => setRenaming({ id: team.id, title: team.title })}
                      data-testid={`trip-tracking-team-rename-${team.id}`}
                    />
                    <Popconfirm
                      title={t('trips.tracking.teamDeleteConfirm')}
                      onConfirm={() => void onDeleteTeam(team.id)}
                    >
                      <Button
                        type="link"
                        size="small"
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
        <Space.Compact style={{ width: '100%', marginTop: 8 }}>
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
