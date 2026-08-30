// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  BulbOutlined,
  CalendarOutlined,
  CheckCircleOutlined,
  ClockCircleOutlined,
  NotificationOutlined,
  RollbackOutlined,
  ScheduleOutlined,
  StopOutlined,
} from '@ant-design/icons';
import { App, Button, Flex, Popconfirm } from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useMoveEvent, type ActivityState } from '../../api/hooks.ts';

interface Props {
  eventId: string;
  state: ActivityState;
  /** The event's own write permission, already decided by the page. */
  canEdit: boolean;
}

interface Move {
  /** The state the event moves to — the whole of what the request says. */
  to: ActivityState;
  label: string;
  success: string;
  icon: ReactNode;
  /** Asked before it happens, for a move that reads as an ending. */
  confirm?: string;
  primary?: boolean;
}

const ANNOUNCE: Move = {
  to: 'published',
  label: 'events.announce',
  success: 'events.announceSuccess',
  icon: <NotificationOutlined />,
  primary: true,
};

const BACK_TO_DRAFT: Move = {
  to: 'draft',
  label: 'events.unannounce',
  success: 'events.unannounceSuccess',
  icon: <RollbackOutlined />,
};

const FLOAT_IT: Move = {
  to: 'proposed',
  label: 'events.propose',
  success: 'events.proposeSuccess',
  icon: <BulbOutlined />,
};

const ORGANISE: Move = {
  to: 'planned',
  label: 'events.organise',
  success: 'events.organiseSuccess',
  icon: <ScheduleOutlined />,
};

const CONFIRM_GOING: Move = {
  to: 'confirmed',
  label: 'events.confirm',
  success: 'events.confirmSuccess',
  icon: <CalendarOutlined />,
};

const PUT_BACK: Move = {
  to: 'delayed',
  label: 'events.postpone',
  success: 'events.postponeSuccess',
  icon: <ClockCircleOutlined />,
};

/**
 * Out of a postponement, and it goes back to being organised rather than to going ahead: a date
 * has to be settled again before anybody is told the event is on. A different button from the one
 * that starts organising a draft because it says a different thing to whoever reads it, even
 * though both land on the same state.
 */
const SETTLE_A_NEW_DATE: Move = {
  to: 'planned',
  label: 'events.reschedule',
  success: 'events.rescheduleSuccess',
  icon: <ScheduleOutlined />,
};

const RECORD_AS_DONE: Move = {
  to: 'done',
  label: 'events.markDone',
  success: 'events.markDoneSuccess',
  icon: <CheckCircleOutlined />,
};

const CALL_OFF: Move = {
  to: 'cancelled',
  label: 'events.callOff',
  success: 'events.callOffSuccess',
  icon: <StopOutlined />,
  confirm: 'events.callOffConfirm',
};

/**
 * What this control offers from each state, and it is the client's reading of a table the server
 * holds — the events table, which is its own and not the trip's, even where the two agree today.
 * Only the server decides what is allowed: a move offered here that the rules refuse comes back
 * as a conflict, and one the rules allow that is missing here is a button nobody drew, never a
 * rule quietly relaxed.
 *
 * Every state an event may hold is listed, and each offers at least the way back to the
 * workshop, so no event can arrive somewhere this control has nothing to say about.
 */
const MOVES: Partial<Record<ActivityState, Move[]>> = {
  draft: [ANNOUNCE, FLOAT_IT, ORGANISE, RECORD_AS_DONE, CALL_OFF],
  proposed: [ORGANISE, BACK_TO_DRAFT, CALL_OFF],
  planned: [CONFIRM_GOING, PUT_BACK, BACK_TO_DRAFT, CALL_OFF],
  confirmed: [RECORD_AS_DONE, PUT_BACK, BACK_TO_DRAFT, CALL_OFF],
  delayed: [SETTLE_A_NEW_DATE, BACK_TO_DRAFT, CALL_OFF],
  done: [ANNOUNCE, BACK_TO_DRAFT],
  published: [BACK_TO_DRAFT],
  cancelled: [BACK_TO_DRAFT],
};

/**
 * Moving an event through its lifecycle, through the one route that names the state to move to.
 */
export default function EventStateControl({ eventId, state, canEdit }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const move = useMoveEvent();

  if (!canEdit) {
    return null;
  }

  const offered = MOVES[state] ?? [];
  if (offered.length === 0) {
    return null;
  }

  const run = async (choice: Move) => {
    try {
      await move.mutateAsync({ id: eventId, state: choice.to });
      message.success(t(choice.success));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Flex gap={8} wrap>
      {offered.map((choice) => {
        // One mutation drives every button, so the spinner has to be told which move is in flight
        // — otherwise all of them spin at once and one click looks like it did more than it did.
        const button = (
          <Button
            key={choice.to}
            type={choice.primary ? 'primary' : 'default'}
            icon={choice.icon}
            loading={move.isPending && move.variables?.state === choice.to}
            disabled={move.isPending}
            onClick={choice.confirm ? undefined : () => void run(choice)}
          >
            {t(choice.label)}
          </Button>
        );
        return choice.confirm ? (
          <Popconfirm key={choice.to} title={t(choice.confirm)} onConfirm={() => void run(choice)}>
            {button}
          </Popconfirm>
        ) : (
          button
        );
      })}
    </Flex>
  );
}
