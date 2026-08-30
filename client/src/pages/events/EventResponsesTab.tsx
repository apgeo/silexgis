// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, App, Spin } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useAnswerEventInvitation,
  useEventInvitations,
  useInviteToEvent,
  useRemoveEventInvitation,
  useSelectForEvent,
  type EventInfo,
  type TripInvitationAnswer,
} from '../../api/hooks.ts';
import InvitationsPanel from '../../components/invitations/InvitationsPanel.tsx';

/**
 * Who has been asked about a club event and what each has said.
 *
 * The same rows a trip's answers are, drawn by the same panel: there is one answering mechanism
 * and one table behind both, so a second copy of how an order and a limit read would be a second
 * rule free to drift from the one people see on a trip.
 *
 * Two things a trip has are absent here and are absent on purpose. An event keeps no list of who
 * turned up, so there is no act that turns what people said into who was there — the answers are
 * the whole record of who was coming. And a kind of event nobody comes to takes no answers at all;
 * the server refuses the whole group for one under its own code, and this tab is not drawn for it.
 */
export default function EventResponsesTab({
  event,
  canEdit,
}: {
  event: EventInfo;
  canEdit: boolean;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data, isPending, error, refetch } = useEventInvitations(event.id);

  const invite = useInviteToEvent();
  const answer = useAnswerEventInvitation();
  const select = useSelectForEvent();
  const remove = useRemoveEventInvitation();

  /**
   * A refusal in the words the person can act on where the server gave a reason of its own, and
   * in the generic ones otherwise. An answer written at the same moment as somebody else's is not
   * a failure but a stale reading, so the list is re-read rather than the write reported as lost.
   */
  const failed = (e: unknown) => {
    const code = e instanceof ApiError ? e.code : undefined;
    switch (code) {
      case 'event_invitation.concurrent_answer':
        void refetch();
        message.warning(t('events.responses.concurrentAnswer'));
        return;
      case 'event_invitation.caver_unknown':
        message.error(t('events.responses.caverUnknown'));
        return;
      case 'event_invitation.select_not_attending':
        message.error(t('events.responses.selectNotAttending'));
        return;
      case 'event_invitation.note_invalid':
        message.error(t('events.responses.noteInvalid'));
        return;
      // A kind of event nobody comes to answers every one of these routes the same way. The tab
      // is not offered for one, so reaching this means the kind changed under a page already
      // open — which is a stale reading and says so, rather than a failure to save.
      case 'event_invitation.kind_takes_no_responses':
        message.warning(t('events.responses.kindTakesNoResponses'));
        return;
      default:
        message.error(t('common.saveFailed'));
    }
  };

  const onInvite = async (caverId: string) => {
    try {
      await invite.mutateAsync({ eventId: event.id, caverId });
      message.success(t('events.responses.invited'));
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
      await answer.mutateAsync({ eventId: event.id, caverId, response, note });
      message.success(t('common.saved'));
      return true;
    } catch (e) {
      failed(e);
      return false;
    }
  };

  const onSelect = async (caverId: string, selected: boolean) => {
    try {
      await select.mutateAsync({ eventId: event.id, caverId, selected });
    } catch (e) {
      failed(e);
    }
  };

  const onRemove = async (caverId: string) => {
    try {
      await remove.mutateAsync({ eventId: event.id, caverId });
      message.success(t('common.deleted'));
    } catch (e) {
      failed(e);
    }
  };

  if (error) {
    return (
      <Alert
        type="warning"
        showIcon
        data-testid="event-invitations-unavailable"
        message={t('events.responses.unavailable')}
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
      keys="events.responses"
      idPrefix="event"
      inviting={invite.isPending}
      onInvite={onInvite}
      onAnswer={onAnswer}
      onSelect={onSelect}
      onRemove={onRemove}
      onUnknownInvitee={() => message.error(t('events.responses.pickFromDirectory'))}
    />
  );
}
