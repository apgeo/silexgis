// SPDX-License-Identifier: AGPL-3.0-or-later
import { Link } from 'react-router-dom';
import { useCave } from '../../api/hooks.ts';

/**
 * One of the caves a trip names.
 *
 * The identity comes from the trip itself — the list the trip's own read hands over, which has
 * already had removed from it every cave this reader may not place. Only the name is asked for
 * here, and it is asked for through the cave's own read, so a cave that has since become
 * unreadable shows as nothing more than the identity the trip carried.
 *
 * Shared between the trip page and the report so both name a cave the same way: two surfaces
 * resolving the same list by two routes is how they come to disagree about what is on it.
 */
export default function TripCaveLink({ caveId }: { caveId: string }) {
  const { data: cave } = useCave(caveId);
  return <Link to={`/caves/${caveId}`}>{cave?.name ?? caveId}</Link>;
}
