// SPDX-License-Identifier: AGPL-3.0-or-later
import { EditOutlined } from '@ant-design/icons';
import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import type { ActivityState } from '../../api/hooks.ts';

/**
 * Keyed by the wire vocabulary rather than by string, so a state added on the server fails to
 * compile here until somebody decides how it should look. The lifecycle is shared with the
 * activities that are not trips yet, which is why states no trip can hold are coloured too.
 */
const STATE_COLOURS: Record<ActivityState, string> = {
  draft: 'default',
  proposed: 'default',
  planned: 'cyan',
  confirmed: 'blue',
  done: 'blue',
  published: 'green',
  cancelled: 'red',
  delayed: 'orange',
};

/**
 * Where a trip has got to, as one badge.
 *
 * A draft carries a pencil as well as its colour: it is the one state that means "nobody has been
 * told about this yet", and an author scanning a list of their own trips needs to see that without
 * reading the words. Grey alone is too easy to skim past, and colour alone says nothing to a
 * reader who cannot separate these two greys.
 */
export default function TripStateTag({ state }: { state: ActivityState }) {
  const { t } = useTranslation();

  return (
    <Tag
      color={STATE_COLOURS[state]}
      icon={state === 'draft' ? <EditOutlined /> : undefined}
      data-testid="trip-state"
    >
      {t(`trips.stateValues.${state}`)}
    </Tag>
  );
}
