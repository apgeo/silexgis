// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, App, Button, Flex, Popconfirm, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useAnswerTripInvitation,
  useInviteToTrip,
  usePromoteTripInvitations,
  useRemoveTripInvitation,
  useSelectForTrip,
  useTripInvitations,
  type TripInvitationAnswer,
  type TripLogInfo,
} from '../../api/hooks.ts';
import InvitationsPanel from '../../components/invitations/InvitationsPanel.tsx';

/**
 * The states in which writing the list into the trip's own roster means anything: the trip has
 * happened. Offering it earlier would offer an act the rules refuse, and the refusal is still the
 * server's — this only decides what a reader is shown a control for.
 */
const PROMOTABLE: readonly TripLogInfo['state'][] = ['done', 'published'];

/**
 * Who has been asked on a trip, what each has said, and who holds one of its places.
 *
 * The list itself is drawn by the panel both subjects share, because there is one answering
 * mechanism behind a trip's answers and a club event's. What is here is what belongs to a trip
 * alone: the words its refusals are reported in, and the one act that turns intent into
 * attendance.
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

  const onInvite = async (caverId: string) => {
    try {
      await invite.mutateAsync({ tripLogId: trip.id, caverId });
      message.success(t('trips.invitations.invited'));
      return true;
    } catch (e) {
      failed(e);
      return false;
    }
  };

  const onAnswer = async (
    caverId: string,
    response: TripInvitationAnswer,
    note: string | null,
  ) => {
    try {
      await answer.mutateAsync({ tripLogId: trip.id, caverId, response, note });
      message.success(t('common.saved'));
      return true;
    } catch (e) {
      failed(e);
      return false;
    }
  };

  const onSelect = async (caverId: string, selected: boolean) => {
    try {
      await select.mutateAsync({ tripLogId: trip.id, caverId, selected });
    } catch (e) {
      failed(e);
    }
  };

  const onRemove = async (caverId: string) => {
    try {
      await remove.mutateAsync({ tripLogId: trip.id, caverId });
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

  return (
    <InvitationsPanel
      data={data}
      canEdit={canEdit}
      keys="trips.invitations"
      idPrefix="trip"
      inviting={invite.isPending}
      onInvite={onInvite}
      onAnswer={onAnswer}
      onSelect={onSelect}
      onRemove={onRemove}
      onUnknownInvitee={() => message.error(t('trips.invitations.pickFromDirectory'))}
      footer={
        // Turning what people said into who was on the trip is its own act, offered only once
        // the trip has happened. Nobody is told by it: everybody written in was told when they
        // were asked. An event has no such act — it keeps no list of who turned up at all.
        canEdit && PROMOTABLE.includes(trip.state) ? (
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
        ) : null
      }
    />
  );
}
