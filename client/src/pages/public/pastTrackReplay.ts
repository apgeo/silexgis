// SPDX-License-Identifier: AGPL-3.0-or-later
import type { PublicPastTrack, PublicTripEnvelope, PublicTripParticipant } from '../../api/hooks.ts';
import { publicPlaceReported } from '../../caveview/publicTrackedCavers.ts';
import { teamStation, type TrackedCaver } from '../../caveview/trackedCavers.ts';
import { replayWindow, type ReplayWindow } from '../../caveview/trackingReplay.ts';
import { instantOf } from './publicTripParty.ts';

/**
 * A past trip of a published cave, wound back to a moment of itself.
 *
 * <b>This is not a second replay, and the shape below is what keeps it from becoming one.</b> What
 * a moment of a past trip is folded into is a <em>published trip envelope</em> — the very answer
 * the live page already draws — so everything downstream runs unchanged: the party list with its
 * four reasons somebody cannot be drawn, the three standings and their counts, the gathering into
 * teams, the four alerts, the fold into markers (`publicTrackedCavers`), the coordinate lookup, the
 * station pictures and the scanned sheets. A second set of honesty rules for the past is exactly
 * what this file exists to not have: the live page and the archive draw the same shape through the
 * same code, and a rule corrected in one is corrected in both by construction.
 *
 * <b>Three facts about the response shape the fold, and each is a place where the obvious reading
 * would be wrong.</b>
 *
 * <b>The standing is the server's, not this file's.</b> Every fix carries `in`/`out` as the standing
 * in force <em>after</em> that report, folded over the whole prefix of the log — including reports
 * that are never emitted, because a coordinator's note moves nothing and is never handed to a
 * stranger. Folding it again from the rows that did arrive would be this file deciding what moves a
 * standing, which is a decision with one home, on the server, in Domain. So the standing at a
 * moment is simply the standing on the last fix at or before it.
 *
 * <b>A report with no place leaves the last place standing rather than clearing it</b> — the same
 * reading the live watch's position column has and the coordinator's replay has. Somebody reported
 * out keeps the station they were last seen at, marked out: taking them off the drawing would read
 * as a caver who vanished rather than one who is safely above ground.
 *
 * <b>A withholding can only ever be said as the weaker claim</b>, and that is not decided here
 * either. A fix carrying no station, no depth and no other-survey flag is either a report whose
 * place was kept from this reader or a report that said somebody went in or came out, and the
 * response carries no report kind to tell them apart — deliberately. Handing the moment over as an
 * envelope means the live page's own rule for that ambiguity is the one that answers it.
 */

/** Which of the party a reader has asked the replay to keep the camera on. */
export interface PastFollow {
  kind: 'caver' | 'team';
  /** A place in the party as a string, or a team id. Null is the group of everybody on no team. */
  id: string | null;
}

/**
 * The stretch of time a past trip is replayed over.
 *
 * <b>Measured by the same function a coordinator's replay is measured by</b>, so the two surfaces
 * cannot come to disagree about where a trip begins and ends. What it is given is this trip's own
 * armed and closed instants and the reports themselves, which widen it — a report relayed out of a
 * cave carries the moment it was <em>said</em>, so one can land outside the watch's own stretch,
 * and a scrubber that could not be dragged to a report is a replay with a report missing from it.
 *
 * <b>The one thing supplied differently is where an unclosed watch ends.</b> A live trip's window
 * ends at the moment the replay was opened, because the party is still moving; nobody on this
 * surface is. A past trip whose watch was switched off rather than closed carries no closing
 * instant at all, and ending its window at the present would hand a reader a rail that is mostly
 * the months since — every report collapsed onto the left-hand end of it. So the fallback given
 * here is the trip's own beginning, which the widening then carries forward to the last report
 * there actually is.
 *
 * <b>A truncated record is treated exactly as such a watch, and that is the whole of the honesty
 * fix for it.</b> One response carries a bounded number of reports, oldest first, and says so when
 * there were more. The trip's closing instant is still known — so a window ending there would be a
 * rail whose back half has no reports on it at all, over which every caver stays pinned at the last
 * station that did arrive, standing underground, growing hours older by the minute. That reads as a
 * party who stopped being heard from, which is a far worse statement than the true one. So the end
 * of the record is the end of the rail, and the strip says in words that the record was cut.
 */
export function pastReplayWindow(track: PublicPastTrack): ReplayWindow | null {
  const armed = instantOf(track.armedAt);
  if (armed === null) {
    return null;
  }
  const fixes = track.participants.flatMap((participant) => participant.track);
  // A record the response could not carry whole ends where the reports end, not where the trip
  // did. Running the rail on to a closing instant the fold has no reports for would pin every
  // caver at their last delivered station, with a standing of "underground" and an age that grows
  // by the hour as the handle moves — a party who stopped being heard from, which is exactly the
  // statement the response's own truncation flag exists to refuse. Handed to the shared measurer
  // as a watch with no closing instant, whose end is then carried forward by the reports there
  // actually are; the banner says in words why the rail stops where it does.
  return replayWindow(
    track.trackTruncated ? { armedAt: track.armedAt, closedAt: null } : track,
    fixes,
    armed,
  );
}

/**
 * Every moment a report was made on this trip, oldest first and without repeats.
 *
 * Offered so a reader can step from one report to the next instead of hunting for them with a
 * handle — the same service the coordinator's replay gives with its note arrows, over the thing
 * this surface actually has. A published track carries no notes by design; what it carries is
 * reports, and those are what somebody scrubbing a trip is looking for.
 */
export function pastReportMoments(track: PublicPastTrack): number[] {
  const moments = new Set<number>();
  for (const participant of track.participants) {
    for (const fix of participant.track) {
      const at = instantOf(fix.recordedAt);
      if (at !== null) {
        moments.add(at);
      }
    }
  }
  return [...moments].sort((left, right) => left - right);
}

/** The report before a moment, for a reader stepping back. Strictly before. */
export function momentBefore(moments: readonly number[], at: number): number | null {
  let found: number | null = null;
  for (const moment of moments) {
    if (moment >= at) {
      break;
    }
    found = moment;
  }
  return found;
}

/** The next report after a moment, for a reader stepping forward. Strictly after. */
export function momentAfter(moments: readonly number[], at: number): number | null {
  return moments.find((moment) => moment > at) ?? null;
}

/**
 * One member of a past party as they stood at an instant, in the shape the live envelope uses.
 *
 * Everybody the trip's roster names is folded, in the order it names them, including people no
 * report mentions yet: a list that grew and shrank as the replay played would be a different
 * surface from the one it is pretending to be, and the live page lists them all too.
 */
function participantAt(
  participant: PublicPastTrack['participants'][number],
  at: number,
): PublicTripParticipant {
  let stationName: string | null = null;
  let depthM: number | null = null;
  let positionOnOtherModel = false;
  let positionRecordedAt: string | null = null;
  let lastRecordedAt: string | null = null;
  let teamId: string | null = null;
  let inside = false;
  let out = false;

  for (const fix of participant.track) {
    const when = instantOf(fix.recordedAt);
    // A fix whose instant will not parse is left out rather than placed somewhere: there is no
    // moment to put it at, and putting it anywhere moves somebody at a time nothing says they
    // moved. The same rule the coordinator's replay applies to its own log.
    if (when === null || when > at) {
      continue;
    }
    lastRecordedAt = fix.recordedAt;
    // The team a report carried at the time, and nothing else — never a team the roster holds now.
    // Somebody moved between teams half way through is shown in the team they were in at the
    // moment on the clock, which is the anachronism this whole surface exists to remove.
    if (fix.teamId !== null) {
      teamId = fix.teamId;
    }
    inside = fix.in;
    out = fix.out;

    // Whether this row claims a place at all, asked through the one function both public surfaces
    // ask it through. A row claiming none leaves the last place standing.
    if (publicPlaceReported(fix) !== null) {
      stationName = fix.stationName;
      depthM = fix.depthM;
      positionOnOtherModel = fix.positionOnOtherModel;
      positionRecordedAt = fix.recordedAt;
    }
  }

  return {
    ordinal: participant.ordinal,
    label: participant.label,
    teamId,
    stationName,
    depthM,
    lastRecordedAt,
    positionRecordedAt,
    positionOnOtherModel,
    in: inside,
    out,
  };
}

/**
 * A past trip at one instant, as the envelope every published surface already knows how to draw.
 *
 * <b>What the shape costs, said out loud: the moment somebody went in.</b> The envelope has no
 * field for it — the live page could never have one, because a follower is not sent the log it
 * would come off — so a card on this surface says "—" for an entry time that the track below it
 * plainly holds. That is the price of one shape for both surfaces, and it is the right way round:
 * the alternative is a second participant shape whose honesty rules drift from the first, in
 * exchange for one line on one card.
 *
 * <b>`state` is `closed` and not a guess.</b> A trip readable as past is by definition not being
 * followed — the server refuses an armed watch outright — so the one thing this envelope must never
 * say is `armed`, which is what makes a page poll and what makes it print "underground now" over a
 * party who came out last winter. A watch switched off rather than closed carries no closing
 * instant, and that absence travels through untouched: `closedAt` is null and nothing invents one.
 */
export function pastEnvelopeAt(track: PublicPastTrack, at: number): PublicTripEnvelope {
  return {
    title: track.title,
    tripDate: track.tripDate,
    tripDateEnd: track.tripDateEnd,
    state: 'closed',
    armedAt: track.armedAt,
    closedAt: track.closedAt,
    positionsWithheld: track.positionsWithheld,
    model: track.model,
    teams: track.teams,
    participants: Number.isFinite(at)
      ? track.participants.map((participant) => participantAt(participant, at))
      // An instant that is not a number names no moment, and a fold at one would draw a party at a
      // time nothing says anything about. The roster is still named, with nothing reported of
      // anybody — which is the honest reading of "we do not know which moment this is".
      : track.participants.map((participant) => participantAt(participant, Number.NEGATIVE_INFINITY)),
  };
}

/**
 * The station the camera should be on to keep a followed team or caver in view, or null.
 *
 * <b>Null is an answer and is not a failure.</b> The person or team being followed can be unplaced
 * at the moment on the clock for every reason the surface already knows about — nobody had reported
 * them yet, the place was withheld, the place was measured on another survey — and each of those is
 * a moment where there is nowhere honest to point a camera. A follower that guessed would fly to
 * wherever somebody was an hour later and present it as where they are now.
 *
 * A team is answered through the same derivation a coordinator's list uses for "where is this
 * team", so the two surfaces pick the same member to speak for it: whoever is still underground, by
 * the moment their <em>position</em> was reported rather than the moment they last said anything.
 */
export function followedStation(
  cavers: readonly TrackedCaver[],
  follow: PastFollow | null,
): string | null {
  if (follow === null) {
    return null;
  }
  if (follow.kind === 'caver') {
    const caver = cavers.find((member) => member.caverId === follow.id);
    return caver !== undefined && caver.position.kind === 'station' ? caver.position.station : null;
  }
  const members = cavers.filter((member) => member.teamId === follow.id);
  return members.length === 0 ? null : teamStation(members);
}

/**
 * What a followed team or caver is called, for the line that says whom the replay is keeping up
 * with — or null when the follow names nobody this trip holds.
 *
 * <b>Read off the trip's own roster and teams rather than off the moment on the clock.</b> Both
 * would be defensible and only one is usable: at the start of a replay nobody has been reported
 * into any team yet, so a name folded from the moment would be absent for the first part of every
 * trip — and the reader would be told nothing was being followed at exactly the moment they pressed
 * a link asking for it. Who is being followed is a standing answer about the trip; whether they can
 * be <em>drawn</em> yet is the moment's question, and {@link followedStation} is the one that asks
 * it.
 *
 * Null rather than made-up words: a link in somebody's article can name a team of another trip, or
 * a place in the party beyond the end of this one's roster, and answering that with a name would be
 * this page inventing a person. Null also for a team the trip carried no title for — there is
 * nothing to call it, and the group of everybody on no team is a different thing with its own word.
 */
export function followedName(
  track: PublicPastTrack,
  follow: PastFollow | null,
  names: { unteamed: string; unnamed: (ordinal: number) => string },
): string | null {
  if (follow === null) {
    return null;
  }
  if (follow.kind === 'caver') {
    const participant = track.participants.find(
      (member) => String(member.ordinal) === follow.id,
    );
    return participant === undefined
      ? null
      : (participant.label ?? names.unnamed(participant.ordinal));
  }
  if (follow.id === null) {
    return names.unteamed;
  }
  const title = track.teams.find((team) => team.id === follow.id)?.title ?? null;
  return title === null || title.length === 0 ? null : title;
}

/**
 * The first moment the followed team or caver was somewhere a drawing could show, or null.
 *
 * <b>Where a replay opens when a link asked to follow somebody and named no moment.</b> A trip's
 * own beginning is the right opening for a replay of the whole party — it is the moment that needs
 * no explanation — and it is the wrong one for "show me the survey team": at the armed instant
 * nobody has been reported into any team yet, so the reader presses a link and watches an empty
 * rail until they think to drag it. Opening where the followed party first appears is what the link
 * plainly meant, and the reader can still wind back to the start.
 *
 * <b>A caver's team at a moment is the last team any report of theirs named</b>, which is what the
 * fold says too — a report claiming a place and naming no team belongs to whatever team that person
 * was in at the time, not to none.
 */
export function firstPlacedMoment(
  track: PublicPastTrack,
  follow: PastFollow | null,
): number | null {
  if (follow === null) {
    return null;
  }
  let earliest: number | null = null;
  for (const participant of track.participants) {
    if (follow.kind === 'caver' && String(participant.ordinal) !== follow.id) {
      continue;
    }
    let teamId: string | null = null;
    for (const fix of participant.track) {
      if (fix.teamId !== null) {
        teamId = fix.teamId;
      }
      if (follow.kind === 'team' && teamId !== follow.id) {
        continue;
      }
      const place = publicPlaceReported(fix);
      // Only a place a drawing could show: a report measured on another survey, or one whose place
      // was withheld, is not a moment at which this follow has anywhere to point.
      if (place === null || place.kind === 'otherModel') {
        continue;
      }
      const when = instantOf(fix.recordedAt);
      if (when !== null && (earliest === null || when < earliest)) {
        earliest = when;
      }
      break;
    }
  }
  return earliest;
}
