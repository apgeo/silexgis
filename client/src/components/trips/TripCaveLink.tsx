// SPDX-License-Identifier: AGPL-3.0-or-later
import { Link } from 'react-router-dom';
import { useCave } from '../../api/hooks.ts';

/**
 * One of the caves a trip names.
 *
 * The identity comes from the trip itself — the list the trip's own read hands over, which has
 * already had removed from it every cave this reader may not open and every cave they may not
 * place. So every identifier reaching this component resolves, and how many caves were taken out
 * is said once beside the list rather than implied by a row that will not resolve.
 *
 * The name is still asked for through the cave's own read, because the trip does not carry names
 * — and that read is the only thing that can be refused here. A cave that becomes unreadable
 * between the two reads is the one case left, and it is a race rather than a rule.
 *
 * Shared between the trip page and the report so both name a cave the same way: two surfaces
 * resolving the same list by two routes is how they come to disagree about what is on it.
 */
export default function TripCaveLink({ caveId }: { caveId: string }) {
  const { data: cave } = useCave(caveId);
  return <Link to={`/caves/${caveId}`}>{cave?.name ?? caveId}</Link>;
}
