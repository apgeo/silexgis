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
import type { ActivityState, Visibility } from '../../api/hooks.ts';

/**
 * The names of the strings a move is drawn with. Each is looked up under `trips.` unless the
 * host supplies another key for it: the lifecycle vocabulary is the trips', shared by every kind
 * of activity that holds it, and only the sentences that name the kind — what announcing does,
 * what calling off is called — differ between a trip and a camp.
 */
export type MoveWording =
  | 'publish'
  | 'publishSuccess'
  | 'publishConfirm'
  | 'unpublish'
  | 'unpublishSuccess'
  | 'propose'
  | 'proposeSuccess'
  | 'organise'
  | 'organiseSuccess'
  | 'confirmGoing'
  | 'confirmGoingSuccess'
  | 'delay'
  | 'delaySuccess'
  | 'reschedule'
  | 'rescheduleSuccess'
  | 'markDone'
  | 'markDoneSuccess'
  | 'callOff'
  | 'callOffSuccess'
  | 'callOffConfirm';

interface Move {
  /** The state the activity moves to — the whole of what the request says. */
  to: ActivityState;
  label: MoveWording;
  success: MoveWording;
  icon: ReactNode;
  /**
   * Asked before it happens, for a move that tells people or that reads as an ending. The
   * announcement's question also names the audience it is about to announce to, because
   * announcing is the moment somebody finds out who has been able to read the activity all
   * along — and it is the last moment before anything that goes out cannot be recalled.
   */
  confirm?: MoveWording;
  primary?: boolean;
}

const ANNOUNCE: Move = {
  to: 'published',
  label: 'publish',
  success: 'publishSuccess',
  icon: <NotificationOutlined />,
  confirm: 'publishConfirm',
  primary: true,
};

const BACK_TO_DRAFT: Move = {
  to: 'draft',
  label: 'unpublish',
  success: 'unpublishSuccess',
  icon: <RollbackOutlined />,
};

const FLOAT_IT: Move = {
  to: 'proposed',
  label: 'propose',
  success: 'proposeSuccess',
  icon: <BulbOutlined />,
};

const ORGANISE: Move = {
  to: 'planned',
  label: 'organise',
  success: 'organiseSuccess',
  icon: <ScheduleOutlined />,
};

const CONFIRM_GOING: Move = {
  to: 'confirmed',
  label: 'confirmGoing',
  success: 'confirmGoingSuccess',
  icon: <CalendarOutlined />,
};

const PUT_BACK: Move = {
  to: 'delayed',
  label: 'delay',
  success: 'delaySuccess',
  icon: <ClockCircleOutlined />,
};

/**
 * Out of a postponement, and it goes back to being organised rather than to going ahead: a date
 * has to be settled again before anybody is told it is on. It is a different button from the one
 * that starts organising a draft because it says a different thing to the person reading it,
 * even though both land on the same state.
 */
const SETTLE_A_NEW_DATE: Move = {
  to: 'planned',
  label: 'reschedule',
  success: 'rescheduleSuccess',
  icon: <ScheduleOutlined />,
};

const RECORD_AS_DONE: Move = {
  to: 'done',
  label: 'markDone',
  success: 'markDoneSuccess',
  icon: <CheckCircleOutlined />,
};

const CALL_OFF: Move = {
  to: 'cancelled',
  label: 'callOff',
  success: 'callOffSuccess',
  icon: <StopOutlined />,
  confirm: 'callOffConfirm',
};

/**
 * What this control offers from each state, and it is the client's reading of a table the server
 * holds. The two must agree about what is worth showing, and only the server decides what is
 * allowed: a move offered here that the rules refuse comes back as a conflict, and a move the
 * rules allow that is missing here is a button nobody drew — never a rule quietly relaxed.
 *
 * A trip and a camp hold the same eight states and make the same moves between them, so one
 * table serves both. The server keeps a transition table per kind on purpose — two kinds whose
 * tables agree today are still two decisions — and the day they diverge this becomes a table
 * per kind as well; until then a second copy here would be a copy that drifts.
 *
 * Every state is listed, and each of them offers at least the way back to the workshop, so no
 * activity can arrive somewhere this control has nothing to say about. The whole control still
 * disappears rather than showing an empty row, which is what a value from outside the
 * vocabulary gets.
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

export interface ActivityStateControlProps {
  state: ActivityState;
  /** Who may read the activity, so the announcement can say who it is announcing to. */
  visibility: Visibility;
  /** The activity's own write permission, already decided by the page. */
  canEdit: boolean;
  /** Performs the move. Rejects on refusal; the control reports the failure. */
  onMove: (to: ActivityState) => Promise<unknown>;
  /** Whether a move is in flight, and which, so only the button that was clicked spins. */
  pending: boolean;
  pendingState?: ActivityState;
  /**
   * Translation keys that replace the trips' wording for the sentences that name the kind of
   * activity. Anything not named here reads as a trip's does.
   */
  wording?: Partial<Record<MoveWording, string>>;
}

/**
 * Moving an activity through its lifecycle.
 *
 * Every move goes through the one route that names the state it moves to, so a state the
 * activity may hold is reachable the moment the table admits it — there is no verb to write per
 * move, and no state that exists on the row while no call can produce it.
 *
 * Announcing is confirmed before it happens because it is not only a change of label: for a
 * trip it is the moment the people named on it are told, and that cannot be recalled by putting
 * it back into draft. The question names who can read the activity and says that announcing it
 * does not change that — widening the audience is an edit, taken on purpose, elsewhere. Calling
 * it off is confirmed because it reads as an ending, even though it is undone by the same door a
 * withdrawal comes back through. The rest send nothing and lose nothing, so they are one click.
 */
export default function ActivityStateControl({
  state,
  visibility,
  canEdit,
  onMove,
  pending,
  pendingState,
  wording,
}: ActivityStateControlProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();

  if (!canEdit) {
    return null;
  }

  const offered = MOVES[state] ?? [];
  if (offered.length === 0) {
    return null;
  }

  const key = (name: MoveWording) => wording?.[name] ?? `trips.${name}`;

  const run = async (choice: Move) => {
    try {
      await onMove(choice.to);
      message.success(t(key(choice.success)));
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
            loading={pending && pendingState === choice.to}
            disabled={pending}
            onClick={choice.confirm ? undefined : () => void run(choice)}
          >
            {t(key(choice.label))}
          </Button>
        );
        return choice.confirm ? (
          <Popconfirm
            key={choice.to}
            title={t(key(choice.confirm), { audience: t(`trips.publishAudience.${visibility}`) })}
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
