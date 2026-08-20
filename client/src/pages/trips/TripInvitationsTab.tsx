// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EyeInvisibleOutlined, StarFilled, StarOutlined, UserAddOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  AutoComplete,
  Button,
  Card,
  Flex,
  Input,
  Popconfirm,
  Select,
  Spin,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useAnswerTripInvitation,
  useCavers,
  useInviteToTrip,
  usePromoteTripInvitations,
  useRemoveTripInvitation,
  useSelectForTrip,
  useTripInvitations,
  type TripInvitationAnswer,
  type TripInvitationInfo,
  type TripLogInfo,
} from '../../api/hooks.ts';
import List from '../../components/List.tsx';
import { caverReference } from '../../components/trips/roster.ts';

/**
 * The states in which writing the list into the trip's own roster means anything: the trip has
 * happened. Offering it earlier would offer an act the rules refuse, and the refusal is still the
 * server's — this only decides what a reader is shown a control for.
 */
const PROMOTABLE: readonly TripLogInfo['state'][] = ['done', 'published'];

/** The longest note the server will store against an answer; a longer one is refused. */
const NOTE_MAX = 500;

const ANSWERS: readonly TripInvitationAnswer[] = ['yes', 'maybe', 'no'];

const ANSWER_COLOUR: Record<TripInvitationAnswer, string | undefined> = {
  pending: undefined,
  yes: 'success',
  maybe: 'warning',
  no: 'default',
};

/** What one row's controls currently read, while somebody is editing them and before they are sent. */
interface Draft {
  response: TripInvitationAnswer;
  note: string;
}

/**
 * Who has been asked on a trip, what each has said, and who holds one of its places.
 *
 * Almost nothing on this surface is worked out here. The order the rows arrive in *is* the order
 * people answered in; the place beside a row and whether that row holds a place or is waiting are
 * the server's conclusions about a limit and a hand-picked team; and whether this caller may write
 * an answer for a given person is a right over the trip, not something a name can be compared
 * against. Re-deriving any of them would be a second copy of a rule already enforced, free to
 * disagree with it — and disagreeing quietly, because every wrong answer still looks like an
 * answer. So the rows are rendered as received, in the order received.
 *
 * A row here is intent and never attendance. Who said they would come and who was underground are
 * different facts, kept in different places on purpose, and nothing that counts who went may count
 * these rows. Turning the first into the second is one deliberate act, offered below.
 */
export default function TripInvitationsTab({
  trip,
  canEdit,
}: {
  trip: TripLogInfo;
  canEdit: boolean;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data, isPending, error, refetch } = useTripInvitations(trip.id);

  // The name being typed into the picker, and the person it last named. Held apart because they
  // can come apart: text edited after somebody was chosen no longer names them.
  const [invitee, setInvitee] = useState<{ caverId?: string; loadedName?: string; name: string }>({
    name: '',
  });
  const { data: cavers } = useCavers(invitee.name || undefined);

  // Only rows somebody is part way through editing. A row nobody has touched reads the server's
  // answer directly, so a list that refreshes underneath cannot take back what is being typed.
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});

  const invite = useInviteToTrip();
  const answer = useAnswerTripInvitation();
  const select = useSelectForTrip();
  const remove = useRemoveTripInvitation();
  const promote = usePromoteTripInvitations();

  /**
   * A refusal in the words the person can act on where the server gave a reason of its own, and
   * in the generic ones otherwise. Two of them are worth naming: an answer written at the same
   * moment as somebody else's is not a failure but a stale reading, so the list is re-read; and a
   * trip that has not happened yet cannot have its list written in.
   */
  const failed = (e: unknown) => {
    const code = e instanceof ApiError ? e.code : undefined;
    switch (code) {
      case 'trip_invitation.concurrent_answer':
        void refetch();
        message.warning(t('trips.invitations.concurrentAnswer'));
        return;
      case 'trip_invitation.caver_unknown':
        message.error(t('trips.invitations.caverUnknown'));
        return;
      case 'trip_invitation.select_not_attending':
        message.error(t('trips.invitations.selectNotAttending'));
        return;
      case 'trip_invitation.promote_too_early':
        message.error(t('trips.invitations.promoteTooEarly'));
        return;
      case 'trip_invitation.note_invalid':
        message.error(t('trips.invitations.noteInvalid'));
        return;
      default:
        message.error(t('common.saveFailed'));
    }
  };

  const onInvite = async () => {
    // The one rule that decides who a row still names, called rather than restated. This form can
    // only ask somebody the club already knows, so a row that has stopped naming its person is
    // refused here instead of being sent as a name the list has no way to hold.
    const { caverId } = caverReference(invitee);
    if (!caverId) {
      message.error(t('trips.invitations.pickFromDirectory'));
      return;
    }
    try {
      await invite.mutateAsync({ tripLogId: trip.id, caverId });
      setInvitee({ name: '' });
      message.success(t('trips.invitations.invited'));
    } catch (e) {
      failed(e);
    }
  };

  const onAnswer = async (row: TripInvitationInfo, draft: Draft) => {
    try {
      await answer.mutateAsync({
        tripLogId: trip.id,
        caverId: row.caverId,
        response: draft.response,
        // Explicitly nothing rather than left out: an answer given without words is an answer
        // without words, not one still wearing the words of the answer before it.
        note: draft.note.trim() === '' ? null : draft.note.trim(),
      });
      setDrafts((current) => {
        const { [row.caverId]: _done, ...rest } = current;
        return rest;
      });
      message.success(t('common.saved'));
    } catch (e) {
      failed(e);
    }
  };

  const onSelect = async (row: TripInvitationInfo, selected: boolean) => {
    try {
      await select.mutateAsync({ tripLogId: trip.id, caverId: row.caverId, selected });
    } catch (e) {
      failed(e);
    }
  };

  const onRemove = async (row: TripInvitationInfo) => {
    try {
      await remove.mutateAsync({ tripLogId: trip.id, caverId: row.caverId });
      message.success(t('common.deleted'));
    } catch (e) {
      failed(e);
    }
  };

  const onPromote = async () => {
    try {
      const result = await promote.mutateAsync({ tripLogId: trip.id });
      message.success(
        t('trips.invitations.promoted', {
          promoted: result.promoted,
          alreadyNamed: result.alreadyNamed,
        }),
      );
    } catch (e) {
      failed(e);
    }
  };

  if (error) {
    return (
      <Alert
        type="warning"
        showIcon
        data-testid="trip-invitations-unavailable"
        message={t('trips.invitations.unavailable')}
      />
    );
  }

  if (isPending || !data) {
    return <Spin />;
  }

  // A name the caller may not be given arrives as no name at all — the server decides that, and
  // this only presents it. Today it decides it for nobody: a caver's name is readable by every
  // signed-in caller, so the count below stands at nought until that changes. It is drawn rather
  // than assumed away because the alternative is a list that quietly shortens itself.
  //
  // Such a row is counted and never drawn: a blank row in a list still says somebody is there and where they stand in the order,
  // which is most of what a name would have said. The shortfall is a state of this surface rather
  // than a gap in it, so it is written out as a number.
  const named = data.invitations.filter((row) => row.caverName.trim() !== '');
  const withheld = data.invitations.length - named.length;

  return (
    <div data-testid="trip-invitations">
      <Card size="small" title={t('trips.invitations.title')}>
        {/* The limit and the counts as the server worked them out. A hand-picked person holds a
            place wherever they stand in the order, so these numbers cannot be recovered from the
            rows by counting them. */}
        <Typography.Paragraph type="secondary" data-testid="trip-invitations-limit">
          {data.maxParticipants == null
            ? t('trips.invitations.noLimit', { attending: data.attendingCount })
            : t('trips.invitations.limit', {
                attending: data.attendingCount,
                max: data.maxParticipants,
                waiting: data.waitingCount,
              })}
        </Typography.Paragraph>

        {canEdit && (
          <Flex gap={8} wrap style={{ marginBottom: 12 }}>
            <AutoComplete
              style={{ minWidth: 240 }}
              value={invitee.name}
              options={(cavers ?? []).map((caver) => ({ value: caver.name, id: caver.id }))}
              // Choosing names a person; typing after that names whoever the text names, which
              // this form cannot ask. Both are recorded and the difference is read on send.
              onSelect={(value, option) =>
                setInvitee({ caverId: (option as { id: string }).id, loadedName: value, name: value })
              }
              onChange={(value) => setInvitee((current) => ({ ...current, name: value }))}
            >
              {/* Drawn as a plain text box rather than left to the suggestion control's own, so
                  that what is on screen is the text this form reads and there is no second copy
                  of it to disagree. */}
              <Input
                data-testid="trip-invite-name"
                placeholder={t('trips.invitations.invitePlaceholder')}
                onPressEnter={() => void onInvite()}
              />
            </AutoComplete>
            <Button
              type="primary"
              icon={<UserAddOutlined />}
              loading={invite.isPending}
              onClick={() => void onInvite()}
              data-testid="trip-invite"
            >
              {t('trips.invitations.invite')}
            </Button>
          </Flex>
        )}

        <List
          size="small"
          dataSource={named}
          // A list with nothing drawable in it is not the same as a list with nobody on it, and
          // saying nobody has been asked when somebody has would be a false statement rather than
          // a withheld one. Which of the two this is, is the only thing said here; how many are
          // withheld is the number below, in both cases.
          locale={{
            emptyText: t(
              withheld > 0 ? 'trips.invitations.emptyWithheld' : 'trips.invitations.empty',
            ),
          }}
          renderItem={(row) => {
            const draft: Draft = drafts[row.caverId] ?? {
              response: row.response,
              note: row.note ?? '',
            };
            const dirty = drafts[row.caverId] !== undefined;
            return (
              <List.Item
                data-testid={`trip-invitation-${row.caverId}`}
                actions={[
                  ...(row.mayAnswer
                    ? [
                        <Select<TripInvitationAnswer>
                          key="answer"
                          size="small"
                          style={{ width: 110 }}
                          value={draft.response}
                          data-testid={`trip-invitation-answer-${row.caverId}`}
                          onChange={(value) =>
                            setDrafts((current) => ({
                              ...current,
                              [row.caverId]: { ...draft, response: value },
                            }))
                          }
                          options={[
                            // Not answered is where every row starts and is not a thing anybody
                            // can choose, so it is offered as a label for what is stored and
                            // never as an option — without it the control would fall back to
                            // drawing the stored value itself, in an untranslated word.
                            ...(draft.response === 'pending'
                              ? [
                                  {
                                    value: 'pending' as TripInvitationAnswer,
                                    label: t('trips.invitations.responseValues.pending'),
                                    disabled: true,
                                  },
                                ]
                              : []),
                            ...ANSWERS.map((value) => ({
                              value,
                              label: t(`trips.invitations.responseValues.${value}`),
                              disabled: false,
                            })),
                          ]}
                        />,
                        <Input
                          key="note"
                          size="small"
                          style={{ width: 180 }}
                          maxLength={NOTE_MAX}
                          value={draft.note}
                          placeholder={t('trips.invitations.notePlaceholder')}
                          data-testid={`trip-invitation-note-${row.caverId}`}
                          onChange={(e) =>
                            setDrafts((current) => ({
                              ...current,
                              [row.caverId]: { ...draft, note: e.target.value },
                            }))
                          }
                        />,
                        <Button
                          key="save"
                          size="small"
                          type="primary"
                          disabled={!dirty}
                          onClick={() => void onAnswer(row, draft)}
                          data-testid={`trip-invitation-save-${row.caverId}`}
                        >
                          {t('common.save')}
                        </Button>,
                      ]
                    : []),
                  ...(canEdit
                    ? [
                        // Picking somebody out is the proposer's override and sits beside the
                        // order rather than instead of it: the order underneath is unchanged and
                        // stays visible. The server refuses picking anybody who has not said they
                        // are coming, so the control is only offered where it would be honoured.
                        <Tooltip
                          key="select"
                          title={
                            row.selectedAt
                              ? t('trips.invitations.deselect')
                              : t('trips.invitations.select')
                          }
                        >
                          <Button
                            size="small"
                            disabled={row.response !== 'yes'}
                            icon={row.selectedAt ? <StarFilled /> : <StarOutlined />}
                            onClick={() => void onSelect(row, !row.selectedAt)}
                            data-testid={`trip-invitation-select-${row.caverId}`}
                          />
                        </Tooltip>,
                        <Popconfirm
                          key="remove"
                          title={t('trips.invitations.removeConfirm')}
                          onConfirm={() => void onRemove(row)}
                        >
                          <Button
                            size="small"
                            danger
                            icon={<DeleteOutlined />}
                            data-testid={`trip-invitation-remove-${row.caverId}`}
                          />
                        </Popconfirm>,
                      ]
                    : []),
                ]}
              >
                <List.Item.Meta
                  title={
                    <Flex gap={6} wrap align="center">
                      <span>{row.caverName}</span>
                      <Tag color={ANSWER_COLOUR[row.response]}>
                        {t(`trips.invitations.responseValues.${row.response}`)}
                      </Tag>
                      {/* Absent for anything that is not a yes, and absent rather than nought:
                          nobody holds place zero in the order people answered in. */}
                      {row.place != null && (
                        <Tag data-testid={`trip-invitation-place-${row.caverId}`}>
                          {t('trips.invitations.place', { place: row.place })}
                        </Tag>
                      )}
                      <Tag color={row.attending ? 'blue' : undefined}>
                        {row.attending
                          ? t('trips.invitations.attending')
                          : t('trips.invitations.waiting')}
                      </Tag>
                      {row.selectedAt && (
                        <Tag color="gold">{t('trips.invitations.selected')}</Tag>
                      )}
                    </Flex>
                  }
                  description={row.note}
                />
              </List.Item>
            );
          }}
        />

        {withheld > 0 && (
          <Tooltip title={t('trips.invitations.withheldDetail')}>
            <Tag icon={<EyeInvisibleOutlined />} data-testid="trip-invitations-withheld">
              {t('trips.invitations.withheld', { count: withheld })}
            </Tag>
          </Tooltip>
        )}

        {/* Turning what people said into who was on the trip is its own act, offered only once
            the trip has happened. Nobody is told by it: everybody written in was told when they
            were asked. */}
        {canEdit && PROMOTABLE.includes(trip.state) && (
          <Flex justify="flex-end" style={{ marginTop: 12 }}>
            <Popconfirm
              title={t('trips.invitations.promoteConfirm')}
              onConfirm={() => void onPromote()}
            >
              <Button loading={promote.isPending} data-testid="trip-invitations-promote">
                {t('trips.invitations.promote')}
              </Button>
            </Popconfirm>
          </Flex>
        )}
      </Card>
    </div>
  );
}
