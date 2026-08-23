// SPDX-License-Identifier: AGPL-3.0-or-later
import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import type { TripLogInfo } from '../../api/hooks.ts';

/**
 * How much of the list a trip works through has been settled, as one badge: ticked of total.
 *
 * Advisory, and drawn as advisory. It is a reading for the party rather than a state the trip is
 * in: nothing is refused because a line is unsettled, and a trip with nothing settled is on this
 * listing, and visible to exactly the same people, as one fully settled. Green only when every
 * line is confirmed, because "some of it" and "all of it" are the two answers worth telling apart
 * at a glance.
 *
 * Nothing is drawn where the server sent no figure. A trip whose purpose names no list and one
 * whose list this reader may not see are the same absence on purpose — a list answers to its own
 * audience, and a zero here would say a list exists.
 */
export default function TripReadinessTag({
  readiness,
}: {
  readiness: TripLogInfo['checklistReadiness'];
}) {
  const { t } = useTranslation();
  if (!readiness) {
    return null;
  }

  const settled = readiness.ticked >= readiness.total;
  return (
    // The advisory reading rides on the badge itself rather than on a hover component: this is
    // drawn once per row of a listing, and a listing of fifty is not the place for fifty
    // portalled popups. The tab that draws the list in full says the same thing in words.
    <Tag
      color={settled ? 'green' : 'default'}
      title={t('trips.checklistAdvisory')}
      data-testid="trip-readiness"
    >
      {t('trips.checklistReadiness', { ticked: readiness.ticked, total: readiness.total })}
    </Tag>
  );
}
