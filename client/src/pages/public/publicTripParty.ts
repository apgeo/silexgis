// SPDX-License-Identifier: AGPL-3.0-or-later
import type { PublicTripParticipant, PublicTripTeam } from '../../api/hooks.ts';

/** How a published party divides up: underground, back out, or not yet heard of. */
export type PublicTripStanding = 'underground' | 'out' | 'unheard';

/**
 * Where one member of the party stands, as the three states the envelope actually distinguishes.
 *
 * <b>"Neither in nor out" is a state of its own and has to be drawn as one.</b> Somebody nobody
 * has reported yet arrives with both flags false, and a page that folded that into "not
 * underground" would draw a party which has not set off as one that is already out — which on this
 * surface, read by somebody waiting at home, is the difference between "they have not gone in yet"
 * and "they are safely back".
 */
export function standingOf(
  participant: Pick<PublicTripParticipant, 'in' | 'out'>,
): PublicTripStanding {
  if (participant.out) {
    return 'out';
  }
  return participant.in ? 'underground' : 'unheard';
}

/** How many of the party stand in each state — the line that sums a followed page up. */
export function partyStandings(
  participants: readonly Pick<PublicTripParticipant, 'in' | 'out'>[],
): Record<PublicTripStanding, number> {
  const counts: Record<PublicTripStanding, number> = { underground: 0, out: 0, unheard: 0 };
  for (const participant of participants) {
    counts[standingOf(participant)]++;
  }
  return counts;
}

/** One team's row of the page, or the gathering of everybody who is on none. */
export interface PublicTripPartyGroup<T> {
  teamId: string | null;
  /** Null for the group of people on no team; the page names that one in its own language. */
  title: string | null;
  members: T[];
}

/**
 * The party arranged the way it was organised: by team, with everybody on no team gathered at the
 * end.
 *
 * The teams keep the order the envelope sent them in. They are a club's own arrangement of its
 * party, so re-sorting them here by a count or by whoever was reported last would rearrange
 * somebody's trip to suit what the radio happened to say a minute ago.
 *
 * A participant naming a team the envelope did not carry is gathered with the unteamed rather than
 * dropped. Nothing should produce that, and the alternative to handling it is a person quietly
 * missing from the one page their family is watching.
 */
export function partyByTeam<T extends Pick<PublicTripParticipant, 'teamId'>>(
  participants: readonly T[],
  teams: readonly PublicTripTeam[],
): PublicTripPartyGroup<T>[] {
  const known = new Set(teams.map((team) => team.id));
  const groups: PublicTripPartyGroup<T>[] = teams.map((team) => ({
    teamId: team.id,
    title: team.title,
    members: participants.filter((participant) => participant.teamId === team.id),
  }));

  const loose = participants.filter(
    (participant) => participant.teamId === null || !known.has(participant.teamId),
  );
  if (loose.length > 0) {
    groups.push({ teamId: null, title: null, members: loose });
  }

  return groups.filter((group) => group.members.length > 0);
}

/**
 * How long ago something was heard, in the reader's own language.
 *
 * <b>A followed page is read for the gap, not for the clock.</b> Somebody watching from home wants
 * to know that the last word was twenty minutes ago; a timestamp makes them do that subtraction
 * themselves, on a phone, possibly in another time zone, and gets it wrong for them if the party
 * is caving across midnight. So the gap is what is said, and the exact moment is offered beside it
 * for whoever wants it.
 *
 * Rounded down to the unit below, which is the honest direction: "2 hours ago" for something two
 * hours and fifty minutes old would be a page under-reporting a silence.
 */
export function sinceInWords(iso: string, now: number, language: string): string {
  return gapInWords(new Date(iso).getTime(), now, language);
}

/**
 * A reported moment as something that can be compared and worded, or null where there is no
 * moment to be had.
 *
 * <b>There are three ways a moment can be missing and only one of them looks like it.</b> The
 * explicit `null` the server sends for a position nobody reported is the obvious one. A field a
 * server predating it never wrote arrives as `undefined`, which is not `null` and passes every
 * `=== null` guard ever written. And a string that will not parse — a clock nobody set, a value
 * mangled in transit — arrives looking like a moment and is `NaN` the instant anything reads it.
 * All three mean the same thing to a reader, which is that nobody said when, so all three answer
 * the same way here.
 *
 * <b>Why this is a shared function rather than a guard at each site.</b> What `NaN` does next
 * depends entirely on who receives it: `Intl.RelativeTimeFormat` throws — taking a followed page
 * down to its error boundary, so a family watching a trip sees no party at all rather than one
 * undated position — while `toLocaleTimeString` quietly returns the words "Invalid Date" and draws
 * them on a marker beside somebody's name. Neither is a failure a reader could act on, and a rule
 * spelled once cannot be remembered at one site and forgotten at the next.
 */
export function instantOf(value: string | null | undefined): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/**
 * How long ago an instant already in hand was — a moment the page measured itself, such as when
 * its last read arrived, rather than one a report carried as text. The same rounding as every
 * other gap here, by calling it.
 */
export function ageInWords(instant: number, now: number, language: string): string {
  return gapInWords(instant, now, language);
}

/** The gap between an instant and now, in the reader's language — the one rounding rule. */
function gapInWords(instant: number, now: number, language: string): string {
  const seconds = Math.round((instant - now) / 1000);
  const format = new Intl.RelativeTimeFormat(language, { numeric: 'auto' });
  const magnitude = Math.abs(seconds);
  if (magnitude < 60) {
    return format.format(Math.trunc(seconds), 'second');
  }
  if (magnitude < 3600) {
    return format.format(Math.trunc(seconds / 60), 'minute');
  }
  if (magnitude < 86_400) {
    return format.format(Math.trunc(seconds / 3600), 'hour');
  }
  return format.format(Math.trunc(seconds / 86_400), 'day');
}

/**
 * How long ago the report that actually placed somebody was made, or null where nothing placed
 * them.
 *
 * <b>Two different moments live on a participant and this is the second one.</b> The last word
 * about somebody — a radio note, an exit, "no answer from Maria" — moves `lastRecordedAt` and
 * says nothing about where they are. The station beside it came from the last report that claimed
 * a place, and it can be hours older: a party reported at p.g.7 at noon and radioing "all fine" at
 * four has a position four hours old and a last word eight minutes old. Dating the station with
 * the second of those was the defect this exists to close, so this function reads the position's
 * own moment and there is deliberately nothing else it can read.
 *
 * <b>Silence stays silence, and the signature is how.</b> A position that was never reported and
 * one this reader may not be told apart arrive identically — as no moment at all — and neither may
 * acquire an age: every set of words for it ("just now", "0 minutes ago", a date in 1970) is a
 * report nobody made. What is taken here is therefore the moment itself rather than a participant,
 * so that a caller whose position resolution produced no place has nothing to hand over. A
 * withheld position cannot be dated by mistake, because the only branch that carries a moment is
 * the branch that drew a place.
 *
 * <b>And all three spellings of "no moment" are refused, not just the one the types admit to.</b>
 * The generated client declares this field required, so an absent one is `undefined` rather than
 * `null` and a guard written against `null` alone lets it through — into a formatter that throws,
 * out of render, and down to the error boundary: a followed page showing a family no party at all
 * because one position had no time on it. See {@link instantOf}, which is where the three are
 * named and where the only copy of that rule lives.
 *
 * The gap is worded by the same rule as every other gap on these two pages — called, never
 * copied — so a coordinator and a family reading the same position round it the same way.
 */
export function positionAgeInWords(
  positionRecordedAt: string | null | undefined,
  now: number,
  language: string,
): string | null {
  const at = instantOf(positionRecordedAt);
  return at === null ? null : gapInWords(at, now, language);
}

/**
 * A calendar date, in the reader's language, without inventing a time of day for it.
 *
 * <b>Read as UTC on purpose.</b> A trip date is a calendar day and carries no hour; handing
 * `new Date('2026-09-14')` to a formatter in a timezone west of Greenwich prints the 13th, which is
 * a page telling a club its trip was a day earlier than it was.
 */
export function formatTripDate(value: string, language: string): string {
  const parts = value.split('-').map(Number);
  const date = new Date(Date.UTC(parts[0], (parts[1] ?? 1) - 1, parts[2] ?? 1));
  return date.toLocaleDateString(language, { timeZone: 'UTC', dateStyle: 'medium' });
}

/**
 * When a trip was, as one or two dates — the line under a title, and the line on a row of the
 * archive's picker, said the same way in both places.
 *
 * A single date where the trip began and ended on one day, or where nothing recorded an end: a
 * range whose two halves are the same reads as a mistake, and repeating one date twice says less
 * than printing it once.
 */
export function tripDateRange(
  tripDate: string,
  tripDateEnd: string | null,
  language: string,
): string {
  return tripDateEnd === null || tripDateEnd === tripDate
    ? formatTripDate(tripDate, language)
    : `${formatTripDate(tripDate, language)} – ${formatTripDate(tripDateEnd, language)}`;
}

/**
 * The hour of a moment, and its date too when that is not today.
 *
 * <b>Today is the reader's own day, not the server's.</b> A page read at home is read on a phone's
 * clock, and "08:40" with no date is a claim about that phone's today. Yesterday's 23:50 read at
 * ten past midnight therefore carries its date, because without one it would be read as an hour
 * that has not come yet.
 */
export function clockInWords(instant: number, now: number, language: string): string {
  return new Intl.DateTimeFormat(
    language,
    sameLocalDay(instant, now) ? { timeStyle: 'short' } : { dateStyle: 'medium', timeStyle: 'short' },
  ).format(new Date(instant));
}

/** Whether two instants fall on one calendar day where the reader is. */
function sameLocalDay(one: number, other: number): boolean {
  const a = new Date(one);
  const b = new Date(other);
  return (
    a.getFullYear() === b.getFullYear()
    && a.getMonth() === b.getMonth()
    && a.getDate() === b.getDate()
  );
}

/**
 * A reported moment as a reader should be given it: how long ago while that goes on being true,
 * and the hour itself once it would not.
 *
 * <b>A gap is a promise that the page is keeping up.</b> "Twenty minutes ago" is worth having
 * while a trip is being followed and the page is redrawn as time passes. Once the watch is closed,
 * or the link has stopped answering, nothing on the page will change again — and a gap printed
 * then is true for a minute and wrong for the rest of the night, or grows without end under a
 * party who came out hours ago and makes a finished trip read as a lengthening silence. So a
 * settled page says when, in the hour and the date, which stays true however long it is left open.
 *
 * Null for a moment that is not one, by the one rule every moment on these pages is read by — see
 * {@link instantOf}.
 */
export function momentOrAge(
  value: string | null | undefined,
  now: number,
  language: string,
  settled: boolean,
): string | null {
  const at = instantOf(value);
  if (at === null) {
    return null;
  }
  return settled ? clockInWords(at, now, language) : gapInWords(at, now, language);
}

/**
 * A length of time in the reader's language — "3 hr 10 min" — or null where there is none to say.
 *
 * Rounded down to the minute, the direction every gap on these pages is rounded in. Written by the
 * browser's own unit formatting rather than from translated words, because a count of hours needs
 * a different word at one, at two and at twenty in Romanian and this application keeps no plural
 * forms.
 *
 * Null when the end is before the start: a watch re-started after the moment in hand, or a clock
 * that disagrees with the server's, describes no length of time, and "-4 min" under a trip's title
 * would be read as something being wrong with the trip.
 */
export function durationInWords(from: number, to: number, language: string): string | null {
  if (!Number.isFinite(from) || !Number.isFinite(to) || to < from) {
    return null;
  }
  const minutes = Math.floor((to - from) / 60_000);
  const unit = (value: number, name: 'day' | 'hour' | 'minute') =>
    new Intl.NumberFormat(language, { style: 'unit', unit: name, unitDisplay: 'short' }).format(value);
  if (minutes < 60) {
    return unit(minutes, 'minute');
  }
  if (minutes < 24 * 60) {
    const rest = minutes % 60;
    const hours = unit(Math.floor(minutes / 60), 'hour');
    return rest === 0 ? hours : `${hours} ${unit(rest, 'minute')}`;
  }
  const hours = Math.floor(minutes / 60) % 24;
  const days = unit(Math.floor(minutes / (24 * 60)), 'day');
  return hours === 0 ? days : `${days} ${unit(hours, 'hour')}`;
}

/**
 * The moment a trip began to be followed, or null where the trip says none.
 *
 * <b>It is when the watch was started and nothing else.</b> Not when anybody went underground: a
 * watch is started by a person, often in the car park, sometimes an hour after the party went in,
 * and starting it again replaces the moment. Every line worded from this has to be true of that —
 * "followed since", never "underground since" — on the one page that must not say more than it
 * was told.
 *
 * The one place the instant is chosen, so a trip that one day carries a better one changes here
 * and nowhere else.
 */
export function watchStartedAt(trip: { armedAt: string | null | undefined }): number | null {
  return instantOf(trip.armedAt);
}

/**
 * From when to when a finished trip was followed, as one range — "8:40 AM – 2:10 PM", with the
 * date where it is not today — or null where the trip does not say both ends.
 *
 * The browser words the range, because only it knows which halves two moments share in the
 * reader's language: one date said once for a trip within a day, both for one across midnight.
 */
export function followedSpanInWords(
  trip: { armedAt: string | null | undefined; closedAt: string | null | undefined },
  now: number,
  language: string,
): string | null {
  const from = watchStartedAt(trip);
  const to = instantOf(trip.closedAt);
  if (from === null || to === null || to < from) {
    return null;
  }
  return new Intl.DateTimeFormat(
    language,
    sameLocalDay(from, now) && sameLocalDay(to, now)
      ? { timeStyle: 'short' }
      : { dateStyle: 'medium', timeStyle: 'short' },
  ).formatRange(new Date(from), new Date(to));
}
