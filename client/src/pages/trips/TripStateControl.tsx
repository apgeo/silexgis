// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  CheckCircleOutlined,
  NotificationOutlined,
  RollbackOutlined,
  StopOutlined,
} from '@ant-design/icons';
import { App, Button, Flex, Popconfirm } from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useMoveTripLog, type ActivityState } from '../../api/hooks.ts';

interface Props {
  tripId: string;
  state: ActivityState;
  /** The trip's own write permission, already decided by the page. */
  canEdit: boolean;
}

interface Move {
  /** The state the trip moves to — the whole of what the request says. */
  to: ActivityState;
  label: string;
  success: string;
  icon: ReactNode;
  /** Asked before it happens, for a move that tells people or that reads as an ending. */
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
 * States a trip cannot hold are absent because they cannot be arrived at; states it can hold but
 * has no move out of would render nothing, which is why the whole control disappears rather than
 * showing an empty row.
 */
const MOVES: Partial<Record<ActivityState, Move[]>> = {
  draft: [ANNOUNCE, RECORD_AS_DONE, CALL_OFF],
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
 * the trip back into draft. Calling a trip off is confirmed because it reads as an ending, even
 * though it is undone by the same door a withdrawal comes back through. The rest send nothing and
 * lose nothing, so they are one click.
 */
export default function TripStateControl({ tripId, state, canEdit }: Props) {
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
