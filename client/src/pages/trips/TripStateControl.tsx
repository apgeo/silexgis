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
import { useMoveTripLog, type ActivityState, type Visibility } from '../../api/hooks.ts';

interface Props {
  tripId: string;
  state: ActivityState;
  /** Who may read the trip, so the announcement can say who it is announcing to. */
  visibility: Visibility;
  /** The trip's own write permission, already decided by the page. */
  canEdit: boolean;
}

interface Move {
  /** The state the trip moves to — the whole of what the request says. */
  to: ActivityState;
  label: string;
  success: string;
  icon: ReactNode;
  /**
   * Asked before it happens, for a move that tells people or that reads as an ending. The
   * announcement's question also names the audience it is about to announce to, because
   * announcing is the moment somebody finds out who has been able to read the trip all along
   * — and it is the last moment before the notification goes out and cannot be recalled.
   */
  confirm?: string;
  primary?: boolean;
}

const ANNOUNCE: Move = {
  to: 'published',
  label: 'trips.publish',
  success: 'trips.publishSuccess',
  icon: <NotificationOutlined />,
  confirm: 'trips.publishConfirm',
  primary: true,
};

const BACK_TO_DRAFT: Move = {
  to: 'draft',
  label: 'trips.unpublish',
  success: 'trips.unpublishSuccess',
  icon: <RollbackOutlined />,
};

const FLOAT_IT: Move = {
  to: 'proposed',
  label: 'trips.propose',
  success: 'trips.proposeSuccess',
  icon: <BulbOutlined />,
};

const ORGANISE: Move = {
  to: 'planned',
  label: 'trips.organise',
  success: 'trips.organiseSuccess',
  icon: <ScheduleOutlined />,
};

const CONFIRM_GOING: Move = {
  to: 'confirmed',
  label: 'trips.confirmGoing',
  success: 'trips.confirmGoingSuccess',
  icon: <CalendarOutlined />,
};

const PUT_BACK: Move = {
  to: 'delayed',
  label: 'trips.delay',
  success: 'trips.delaySuccess',
  icon: <ClockCircleOutlined />,
};

/**
 * Out of a postponement, and it goes back to being organised rather than to going ahead: a date
 * has to be settled again before anybody is told the trip is on. It is a different button from
 * the one that starts organising a draft because it says a different thing to the person reading
 * it, even though both land on the same state.
 */
const SETTLE_A_NEW_DATE: Move = {
  to: 'planned',
  label: 'trips.reschedule',
  success: 'trips.rescheduleSuccess',
  icon: <ScheduleOutlined />,
};

const RECORD_AS_DONE: Move = {
  to: 'done',
  label: 'trips.markDone',
  success: 'trips.markDoneSuccess',
  icon: <CheckCircleOutlined />,
};

const CALL_OFF: Move = {
  to: 'cancelled',
  label: 'trips.callOff',
  success: 'trips.callOffSuccess',
  icon: <StopOutlined />,
  confirm: 'trips.callOffConfirm',
};

/**
 * What this control offers from each state, and it is the client's reading of a table the server
 * holds. The two must agree about what is worth showing, and only the server decides what is
 * allowed: a move offered here that the rules refuse comes back as a conflict, and a move the
 * rules allow that is missing here is a button nobody drew — never a rule quietly relaxed.
 *
 * Every state a trip may hold is listed, and each of them offers at least the way back to the
 * workshop, so no trip can arrive somewhere this control has nothing to say about. The whole
 * control still disappears rather than showing an empty row, which is what a value from outside
 * the trip's vocabulary — the enum is shared with activities that are not trips — gets.
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
 * Moving a trip through its lifecycle.
 *
 * Every move goes through the one route that names the state it moves to, so a state the trip may
 * hold is reachable the moment the table admits it — there is no verb to write per move, and no
 * state that exists on the row while no call can produce it.
 *
 * Announcing is confirmed before it happens because it is not only a change of label: it is the
 * moment the people named on the trip are told about it, and that cannot be recalled by putting
 * the trip back into draft. The question names who can read the trip and says that announcing it
 * does not change that — a plan is club-visible from the moment it is made and an account written
 * up afterwards is private, so the one thing somebody might expect this button to do is the one
 * thing it must never do quietly. Widening the audience is an edit, taken on purpose,
 * elsewhere. Calling a trip off is confirmed because it reads as an ending, even
 * though it is undone by the same door a withdrawal comes back through. The rest send nothing and
 * lose nothing, so they are one click.
 */
export default function TripStateControl({ tripId, state, visibility, canEdit }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const move = useMoveTripLog();

  if (!canEdit) {
    return null;
  }

  const offered = MOVES[state] ?? [];
  if (offered.length === 0) {
    return null;
  }

  const run = async (choice: Move) => {
    try {
      await move.mutateAsync({ id: tripId, state: choice.to });
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
          <Popconfirm
            key={choice.to}
            title={t(choice.confirm, { audience: t(`trips.publishAudience.${visibility}`) })}
            onConfirm={() => void run(choice)}
          >
            {button}
          </Popconfirm>
        ) : (
          button
        );
      })}
    </Flex>
  );
}
