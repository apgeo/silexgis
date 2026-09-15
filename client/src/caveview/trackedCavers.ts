// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TrackingState } from '../api/hooks.ts';

/**
 * Folding a trip's watch into the people a survey model can draw.
 *
 * Nothing here imports the viewer, so all of it is arithmetic over plain objects a test can drive
 * without a WebGL context — the same reason the anchor arithmetic next door is arranged this way.
 *
 * Two rules are carried over from the tracking table unchanged, because they are the same rules
 * and a second spelling of them is a second thing to get wrong:
 *
 * **A withheld position is said to have been withheld.** Station names are location data and are
 * kept from a reader without the right to place the cave; they arrive as absences, exactly as they
 * do for somebody nobody has reported yet. Drawing both as "nothing known" would tell a rescue
 * co-ordinator that nobody knows where a caver is, when what is true is that *they* are not being
 * told. No marker is invented for a position that was withheld — there is nowhere to put one —
 * but the person is still listed, and listed as withheld.
 *
 * **How strongly that is said follows the last report.** A report whose kind always carries a
 * place, arriving without one, can only be a withholding. Going in, coming out and a radio note
 * carry no place at all, so an absence after one of those is the ordinary early-trip state and is
 * said as the weaker thing.
 */

/** Where one person was last reported, as far as this reader is being told. */
export type TrackedCaverPosition =
  /** A station of the model on screen, which is where the marker goes. */
  | { kind: 'station'; station: string }
  /** A depth below the entrance, which no station names — so it is said, not drawn. */
  | { kind: 'depth'; depthM: number }
  /** A position exists and this reader may not be told it. `certain` is the stronger claim. */
  | { kind: 'withheld'; certain: boolean }
  /** Nobody has reported a place for this person. */
  | { kind: 'unreported' };

/** One person on the watch, ready to be drawn over the model. */
export interface TrackedCaver {
  caverId: string;
  /** How the person reads on screen — the trip's roster is what knows this, not the watch. */
  name: string;
  /**
   * Which team this person was on, or null for nobody's team.
   *
   * Carried beside the title rather than derived from it, because a title is not an identity: two
   * teams of one trip may be called the same thing, and a team with no title at all is still a
   * team. Anything that gathers the party into its teams keys on this; the title is only ever the
   * word printed over the gathering.
   */
  teamId: string | null;
  teamTitle: string | null;
  position: TrackedCaverPosition;
  /** ISO instant of the latest report of any kind, or null when there has been none. */
  lastRecordedAt: string | null;
  /**
   * ISO instant of the report that placed this person, or null where that is not known.
   *
   * <b>Not the same thing as `lastRecordedAt`, and the difference is the point.</b> A position
   * survives reports that carry no position: going in, coming out and a radio note all say that
   * something happened without saying where, so somebody's latest report and the report their
   * station came from are routinely not the same report. `lastRecordedAt` is the first; this is the
   * second, and anything comparing two people's *positions* has to compare these.
   *
   * Null means the source could not say. The folded watch carries only the kind of the latest
   * report, so a caver whose latest is a note has a station of unknown age — known only to be no
   * newer than `lastRecordedAt`. A replay reads the very report that placed somebody and always
   * knows; a published trip carries no kinds at all and never does.
   */
  positionAt: string | null;
  /** When this person went in, as whoever mounts the panel records it. */
  enteredAt: string | null;
  /** Reported out. Their last position is where they were, not where they are. */
  out: boolean;
}

/** What the roster knows about somebody the watch only knows by id. */
export interface TrackedCaverIdentity {
  name: string;
  enteredAt?: string | null;
}

/**
 * The people of one watch, placed against one survey model.
 *
 * <b>Nothing is returned when the watch resolves positions against another model.</b> A station
 * path means whatever the model it was measured in says it means, so `sala-mare.4` of one cave
 * names a place in a different cave with the same survey names — and a marker drawn from it would
 * be a confident statement about where somebody is, made from a name that happens to collide. A
 * panel showing a different model than the watch names therefore shows no watch at all.
 *
 * @param identify what the trip's own roster says about a caver id: the watch carries ids, and
 *   nothing on it knows what anybody is called.
 * @param surveyModelId the model the panel is showing, or undefined when it does not know.
 */
export function trackedCaversFrom(
  tracking: TrackingState,
  identify: (caverId: string) => TrackedCaverIdentity,
  surveyModelId: string | undefined,
): TrackedCaver[] {
  if (
    surveyModelId === undefined
    || tracking.surveyModelId === null
    || tracking.surveyModelId !== surveyModelId
  ) {
    return [];
  }

  const teamTitles = new Map(tracking.teams.map((team) => [team.id, team.title]));

  return tracking.participants.map((participant) => {
    const identity = identify(participant.caverId);
    return {
      caverId: participant.caverId,
      name: identity.name,
      teamId: participant.teamId,
      teamTitle:
        participant.teamId === null ? null : (teamTitles.get(participant.teamId) ?? null),
      position: positionOf(participant, tracking.positionsWithheld),
      lastRecordedAt: participant.lastRecordedAt,
      // Known only where the latest report is the one that carried the place. The watch folds a
      // position and a time out of different reports — the place from the last report that named
      // one, the time from the last report of any kind — so the two agree only when the latest
      // report was itself a position. After a note or a "come out", the station is older than the
      // time beside it by an amount the watch does not carry, and saying so is what stops the
      // comparison next door reading it as fresh.
      positionAt: positionCarryingKind(participant.lastKind) ? participant.lastRecordedAt : null,
      enteredAt: identity.enteredAt ?? null,
      out: participant.out,
    };
  });
}

/** The two report kinds that always name a place. The rest say something happened, not where. */
export function positionCarryingKind(kind: TrackingState['participants'][number]['lastKind']) {
  return kind === 'atStation' || kind === 'atDepth';
}

function positionOf(
  participant: TrackingState['participants'][number],
  positionsWithheld: boolean,
): TrackedCaverPosition {
  if (participant.stationName !== null && participant.stationName.length > 0) {
    return { kind: 'station', station: participant.stationName };
  }
  // A depth is a position and is not a station: it is somewhere on a line the model does not
  // draw, so it is reported in words rather than placed at a station it might not be at.
  if (participant.depthM !== null) {
    return { kind: 'depth', depthM: participant.depthM };
  }
  if (participant.lastRecordedAt === null || !positionsWithheld) {
    return { kind: 'unreported' };
  }
  return { kind: 'withheld', certain: positionCarryingKind(participant.lastKind) };
}

/** One team of the party, or the gathering of everybody on none. */
export interface TrackedCaverTeam {
  /** Null for the group of people on no team; whoever draws it names that one. */
  teamId: string | null;
  /** Null for the unteamed group, and also for a team the watch carried no title for. */
  title: string | null;
  members: TrackedCaver[];
}

/**
 * The party gathered into the teams it was organised in, with everybody on no team at the end.
 *
 * <b>Keyed on the team's id, never on its title.</b> Two teams of one trip may be called the same
 * thing and a team may be called nothing at all, so a gathering by title would silently merge the
 * first pair and lose the second — and the thing being gathered here is what a reader then presses
 * to be shown where that team is.
 *
 * Teams keep the order the watch lists their members in, which is the order the trip's own roster
 * is in. They are a club's arrangement of its party, so re-ordering them by size or by whoever was
 * reported last would rearrange somebody's trip to suit what the radio happened to say a minute ago.
 */
export function trackedCaverTeams(cavers: readonly TrackedCaver[]): TrackedCaverTeam[] {
  const teams = new Map<string, TrackedCaverTeam>();
  const loose: TrackedCaver[] = [];

  for (const caver of cavers) {
    if (caver.teamId === null) {
      loose.push(caver);
      continue;
    }
    const team = teams.get(caver.teamId);
    if (team === undefined) {
      teams.set(caver.teamId, {
        teamId: caver.teamId,
        title: caver.teamTitle,
        members: [caver],
      });
    } else {
      team.members.push(caver);
    }
  }

  const groups = [...teams.values()];
  if (loose.length > 0) {
    groups.push({ teamId: null, title: null, members: loose });
  }
  return groups;
}

/**
 * The name of the one team all of these people are on, or null where there is no such name.
 *
 * <b>Written for the heading over a group of people standing at one station.</b> A heading is a
 * claim about who the names under it are, so it is only printed where the claim is true of every
 * one of them: null is answered for a mixture of teams, for anybody on no team, and for a team the
 * watch carried no title for — in each of those cases the names alone are the whole of what is
 * known, and a word over them would say more than that.
 *
 * <b>It says whose people these are, never that the team is complete.</b> The lines under the
 * heading are the claim about who is at the station, and a member whose position was withheld or
 * who has not been placed is not among them — so a team of five, three of whom are here, is headed
 * by its name over three names. Inventing the missing two, or suppressing the heading because they
 * are missing, would each say something nobody reported.
 */
export function sharedTeamTitle(members: readonly TrackedCaver[]): string | null {
  const first = members[0];
  if (first === undefined || first.teamId === null) {
    return null;
  }
  if (members.some((member) => member.teamId !== first.teamId)) {
    return null;
  }
  const title = first.teamTitle;
  return title === null || title.length === 0 ? null : title;
}

/**
 * The same people, whoever is still underground first.
 *
 * <b>Written for the block of names one collapsed marker draws.</b> That marker stands for
 * everybody whose last reported station is this one, and somebody reported out is among them —
 * their last position is where they were, not where they are, which is the whole reason their
 * marker is drawn in the muted colour when it is drawn alone. Collapsed, that colour is gone: one
 * dot in one colour, over a list of names. Saying it on each line is what puts it back, and
 * putting the marked lines together is what makes the answer readable rather than findable: the
 * names above the break are the party still at the station, and a reader deciding whether to call
 * somebody out reads the shape of the block instead of checking five lines one at a time.
 *
 * <b>A stable partition, which is why this is not a re-ordering.</b> Inside each half the watch's
 * own order survives untouched, and that is the trip's roster order — a club's arrangement of its
 * own party, not something the last radio report gets to rearrange. The one thing that moves a
 * name is that person coming out, which is exactly the change somebody watching this is watching
 * for.
 */
export function undergroundFirst(members: readonly TrackedCaver[]): TrackedCaver[] {
  return [...members.filter((member) => !member.out), ...members.filter((member) => member.out)];
}

/** An instant that can be compared, or null where the string was absent or unreadable. */
function instantOf(value: string | null): number | null {
  if (value === null) {
    return null;
  }
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Whoever of these spoke last, ties keeping the earlier member — which is roster order. */
function newestOf(
  members: readonly TrackedCaver[],
  timeOf: (member: TrackedCaver) => number | null,
): TrackedCaver {
  let found = members[0];
  let newest = timeOf(found) ?? Number.NEGATIVE_INFINITY;
  for (const member of members.slice(1)) {
    const at = timeOf(member) ?? Number.NEGATIVE_INFINITY;
    if (at > newest) {
      found = member;
      newest = at;
    }
  }
  return found;
}

/**
 * Where a team was last reported to be, or null when none of it has been placed.
 *
 * <b>No team position is stored anywhere, and this is a derivation rather than a record.</b> The
 * watch folds reports per person; a team is a label those reports carry, so "where the team is" has
 * to be read back out of its members. What is taken is the station of whichever member was placed
 * most recently — a team underground moves together and its position is whatever it last said over
 * the radio, so the newest word is the team's word.
 *
 * Members with no station are passed over rather than counted as disagreement: somebody whose
 * position was withheld, or who has reported a depth, says nothing about where the team is
 * standing, and a team of five with one placed member is still a team somebody can be shown.
 *
 * Two things decide "most recently", in this order, and both of them are here because the obvious
 * reading of the watch gets the answer wrong on a surface a rescue co-ordinator reads.
 *
 * <b>Whoever is still underground speaks for the team.</b> Somebody reported out keeps the station
 * they were last seen at — the whole watch is built that way, and the model deliberately draws their
 * marker in the muted colour on the grounds that it is where they were, not where they are. Their
 * report of coming out is also, necessarily, the newest report anybody on that team has made. So a
 * team of four that reached the far end of a cave and then sent one person out would have been shown
 * standing at the pitch head near the entrance, named by the one member who is already on the
 * surface. A team every one of whose members is out still answers — the place they came from is the
 * last thing known about them, and is what a search would start from — so this is an order of
 * preference and not a filter.
 *
 * <b>And the time compared is the time of the position, not of the latest report.</b> The folded
 * watch carries one time per person, taken from their last report of any kind, while their station
 * comes from the last report that named one. A radio note moves the first and not the second, so
 * comparing those times would let somebody whose only recent word was "we are fine" define where the
 * team is standing, against a colleague's genuinely newer station. Where nobody on the team carries
 * a positioned time — a published trip carries no report kinds, so none of its members ever does —
 * the latest report of any kind is the best that can be had and is used rather than giving up.
 */
export function teamStation(members: readonly TrackedCaver[]): string | null {
  const placed = members.filter((member) => member.position.kind === 'station');
  if (placed.length === 0) {
    return null;
  }

  const underground = placed.filter((member) => !member.out);
  const speaking = underground.length > 0 ? underground : placed;

  const dated = speaking.filter((member) => instantOf(member.positionAt) !== null);
  const chosen =
    dated.length > 0
      ? newestOf(dated, (member) => instantOf(member.positionAt))
      : newestOf(speaking, (member) => instantOf(member.lastRecordedAt));

  // Narrowing the filter above is what a type predicate would buy, and the position is read once
  // here instead: the filter is the only place the rule lives either way.
  return chosen.position.kind === 'station' ? chosen.position.station : null;
}
