// SPDX-License-Identifier: AGPL-3.0-or-later
import type { FlybyDocument } from './flybyDocument.ts';

// Walls large enough to be asked about, asked about once for a whole flyby.
//
// A cave whose walls are a real cost to download — on a metered connection, tens of megabytes —
// is not downloaded until the viewer says yes. A flyby that visits such caves would otherwise stop
// to ask in the middle of its flight, or fly past walls that never come. So before it plays, the
// caves of its route whose walls are waiting for a yes are gathered into one question, with how
// many and how big, and the answer is given for all of them at once. A no is kept: the flyby
// plays without those walls, and nothing about them is asked again while it does.

/** Walls the view is waiting for a yes about. */
export interface FlybyWallsPending {
  caveId: string;
  /** What downloading them costs, as the server measured the file. */
  bytes: number;
}

/** What the view says about walls that wait for a yes; a test supplies a plain object. */
export interface FlybyWallsAsk {
  /** Of these caves, those whose walls the view would draw and is waiting for a yes about. */
  pending(caveIds: readonly string[]): FlybyWallsPending[];
  /** The viewer said yes for these caves' walls. */
  agree(caveIds: readonly string[]): void;
}

/** The one question asked for a flyby's route. */
export interface FlybyWallsQuestion {
  caveIds: string[];
  bytes: number;
}

/** The caves a flyby visits — its saved filters' written out — by id, each once, in the order first visited. */
export function flybyRouteCaves(document: Pick<FlybyDocument, 'subjects'>): string[] {
  return [...new Set(document.subjects.flatMap((subject) => (subject.kind === 'cave' ? [subject.caveId] : [])))];
}

/** The question to ask before a flyby plays, or undefined when no walls on its route wait for a yes. */
export function flybyWallsQuestion(document: Pick<FlybyDocument, 'subjects'>, walls: FlybyWallsAsk): FlybyWallsQuestion | undefined {
  const pending = walls.pending(flybyRouteCaves(document));
  if (pending.length === 0) {
    return undefined;
  }
  return {
    caveIds: pending.map((walled) => walled.caveId),
    bytes: pending.reduce((total, walled) => total + walled.bytes, 0),
  };
}
