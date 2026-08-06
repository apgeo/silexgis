// SPDX-License-Identifier: AGPL-3.0-or-later
import type { WorkspaceSelection } from '../stores/workspaceStore.ts';
import { publish, subscribe, type ViewExtent, type ViewKind } from './workspaceBus.ts';

// Keeping the flat map and the 3D scene showing the same thing.
//
// The two views are separate renderers with separate cameras, and neither may reach into the
// other: the whole 3D module exists behind one seam, and a library that drove an OpenLayers map
// from a Cesium camera would put that seam through the flat map as well. So they talk in the terms
// the workspace bus already carries — a selection is a reference, an extent is four degrees — and
// each one applies what it hears with its own camera.
//
// THE LOOP. Two-way binding between two views that disagree about what they are being told is the
// classic way to make a browser tab spin. Everything about the shape below is that problem:
//
//   1. The bus delivers to the publisher's own window synchronously, including to the publisher
//      itself, so a naive "on my move, publish; on an extent, move" re-enters on the same tick
//      before any cross-window hop is involved.
//   2. There is no fixed point to converge on. The flat map fits an extent with padding and a
//      maximum zoom; the scene sizes its box off the ground in the middle of a possibly tilted
//      screen. Feed either one's answer to the other and it comes back different, so an equality
//      check on the value cannot end the exchange — it only slows it down.
//
// It is broken in two places, and only the first is load-bearing:
//
//   * Every extent carries which KIND of view published it, and a view ignores its own kind. That
//     alone terminates the same-window exchange after exactly one hop: map moves, scene follows,
//     the scene's resulting move is not published because it is following.
//   * A view that is following mutes its own publisher until the move it was told to make has
//     settled. This is what stops the follower's camera animation from being read as a fresh user
//     gesture, and it is a plain timer rather than a "wait for the move-ended event" because a
//     move that changes nothing raises no event at all and would leave the mute latched forever.
//
// Filtering by kind rather than by window means two windows both showing the flat map do not sync
// their extents to each other. That is a deliberate limit, not an oversight: two views with the
// same idea of an extent still do not agree on it exactly, and there is nothing in a pair of equal
// views to say which one should give way — so they would drift against each other indefinitely.
// Selection, which has an exact fixed point, is not limited that way: a pick is the same value
// wherever it lands, so it reaches every other view including one of the same kind in another
// window, and the exchange ends because the second delivery finds the view already showing it.
//
// A LATE ARRIVAL. Within one window the last extent anybody announced is remembered, so a view
// opened afterwards starts where the others already are. A view opened in a NEW window has no such
// memory and the bus retains nothing, so it asks — and until somebody answers it stays where its
// own defaults, its URL or its saved view put it.

export type { ViewExtent, ViewKind };

export interface ViewSyncHandlers {
  /** Another view selected something. Already de-duplicated: only changes arrive. */
  onSelection?(selection: WorkspaceSelection | null): void;
  /**
   * What this view is showing as selected right now.
   *
   * The echo check is made against this rather than against what this view was last told, because
   * the two part company: the workspace selection is also written by panels that draw no map and
   * announce nothing — a row clicked in the features table, a feature deleted in the detail panel
   * — and a view remembering only bus traffic would then discard the next arrival of a selection
   * it no longer holds, leaving the window showing something else entirely with no way back to it
   * except picking something different first.
   *
   * A view that does not answer is taken at its word about what it was last told, which is right
   * for one whose selection only ever changes through this bus.
   */
  currentSelection?(): WorkspaceSelection | null;
  /** Another view is showing this box at this map zoom. */
  onExtent?(bounds: ViewExtent, zoom: number): void;
  /**
   * Where this view is looking right now, for answering a view that has just opened elsewhere.
   * Undefined when it is not looking at anything yet.
   */
  currentExtent?(): { bounds: ViewExtent; zoom: number } | undefined;
}

/**
 * The selection a view shows and reads back — everything the protocol needs of one.
 *
 * Passed as a pair rather than as a lone callback because answering only half of it is the defect:
 * a view that accepts selections without saying what it is showing cannot have its echoes told
 * apart from a legitimate repeat.
 */
export interface ViewSelectionPort {
  /** What this view is showing as selected. */
  current(): WorkspaceSelection | null;
  /** Another view picked something. */
  set(selection: WorkspaceSelection | null): void;
}

export interface ViewSyncHandle {
  /** Announces what this view is showing — silently dropped while it is following another. */
  publishExtent(bounds: ViewExtent, zoom: number): void;
  /** Announces what this view's viewer just picked. */
  publishSelection(selection: WorkspaceSelection | null): void;
  /**
   * Keeps this view quiet for the usual settling period, for a move it is about to be given that
   * nobody asked it to share.
   *
   * The case this exists for is a saved view being opened. Such a document says where BOTH views
   * stand, and each is put back where it belongs by the code that reads it — so the move that
   * follows is not a viewer looking somewhere new, and announcing it would have one restored view
   * talk the other out of the position the same document had just given it.
   */
  muteUntilSettled(): void;
  detach(): void;
}

export interface ViewSyncOptions {
  /**
   * How long a view stays muted after being told where to look, in milliseconds.
   *
   * Long enough to cover the camera animation both views use plus the settling delay their
   * publishers wait out, so the move a view was told to make is never re-announced as one its
   * viewer made. The cost of it being generous is that a viewer who grabs the map inside that
   * window has that one gesture go unshared, which is invisible next to a pair of views chasing
   * each other.
   */
  followSettleMs?: number;
  /**
   * How long a view has to have been open before it answers another one asking where to look.
   *
   * A view that has only just opened is as likely to be the one that needs placing as the one
   * worth following, and two of them opened together would otherwise each place the other at its
   * own default. Waiting settles that without any notion of seniority: whoever was already there
   * answers, and whoever has just arrived listens.
   */
  joinGraceMs?: number;
}

const DEFAULT_FOLLOW_SETTLE_MS = 1200;
const DEFAULT_JOIN_GRACE_MS = 1000;

/**
 * The last extent anybody announced, so a view opened afterwards starts where the others already
 * are instead of at its own default.
 *
 * Only within one window: a fresh window has no memory of the exchange and opens where its own
 * defaults, its URL or its saved view put it, which is the right answer there.
 */
let lastExtent: { origin: ViewKind; bounds: ViewExtent; zoom: number } | undefined;

export function attachViewSync(
  origin: ViewKind,
  handlers: ViewSyncHandlers,
  options: ViewSyncOptions = {},
): ViewSyncHandle {
  const followSettleMs = options.followSettleMs ?? DEFAULT_FOLLOW_SETTLE_MS;
  const joinGraceMs = options.joinGraceMs ?? DEFAULT_JOIN_GRACE_MS;
  let following = false;
  let followTimer: number | undefined;
  // What this view was last told is selected, for a view that does not say what it is showing.
  // Compared by value, because both views build a fresh object for every pick and the two sides of
  // an exchange are never the same object.
  let knownSelection: WorkspaceSelection | null = null;
  // Whether this view has been open long enough to answer another one asking where to look.
  let established = false;
  const establishTimer = window.setTimeout(() => {
    established = true;
  }, joinGraceMs);
  // True only for the instant this view's own question is going out. The bus hands an event to the
  // publishing window's own subscribers synchronously, inside the publish call, so a flag set
  // around that call is enough to tell this view's question apart from another view's — and no
  // window may answer its own, or a window that opened alone would place itself at its own
  // default and announce it as an answer.
  let askingWhereToLook = false;

  const believedSelection = () =>
    handlers.currentSelection ? handlers.currentSelection() : knownSelection;

  /** Says where this view is looking, without asking whether it is allowed to speak. */
  const sendExtent = (bounds: ViewExtent, zoom: number) => {
    lastExtent = { origin, bounds, zoom };
    publish({ kind: 'extent', origin, bounds, zoom });
  };

  const mute = () => {
    following = true;
    window.clearTimeout(followTimer);
    followTimer = window.setTimeout(() => {
      following = false;
    }, followSettleMs);
  };

  const follow = (bounds: ViewExtent, zoom: number) => {
    mute();
    handlers.onExtent?.(bounds, zoom);
  };

  const unsubscribe = subscribe((event) => {
    if (event.kind === 'extent') {
      if (event.origin !== origin) {
        follow(event.bounds, event.zoom);
      }
      return;
    }
    if (event.kind === 'view-hello') {
      if (askingWhereToLook || !established) {
        return;
      }
      const here = handlers.currentExtent?.();
      if (here) {
        // Answered even while this view is following another: the mute exists to stop a view
        // re-announcing a move it was told to make, not to stop it answering a direct question,
        // and where it stands is a true answer however it came to be standing there.
        sendExtent(here.bounds, here.zoom);
      }
      return;
    }
    if (event.kind === 'selection') {
      // The publisher's own echo is dropped here too, by value: it set what this view is showing
      // before announcing it, so by the time its own subscriber runs the comparison already holds.
      // That is what makes the same check serve for a repeat arriving from another window.
      if (sameSelection(event.selection, believedSelection())) {
        return;
      }
      knownSelection = event.selection;
      handlers.onSelection?.(event.selection);
    }
  });

  // A view that arrives late joins the conversation where it stands rather than where it started:
  // from this window's memory if there is any, and otherwise by asking the other windows.
  //
  // Deferred by a turn rather than run here, and not for tidiness: this function has not returned
  // yet, so a handler that touched the handle it is about to return would find nothing there. The
  // caller is left holding a working handle before anything can call back into it.
  let detached = false;
  const replay = lastExtent && lastExtent.origin !== origin ? lastExtent : undefined;
  queueMicrotask(() => {
    if (detached) {
      return;
    }
    if (replay) {
      follow(replay.bounds, replay.zoom);
      return;
    }
    askingWhereToLook = true;
    publish({ kind: 'view-hello', origin });
    askingWhereToLook = false;
  });

  return {
    publishExtent(bounds, zoom) {
      if (following) {
        return;
      }
      sendExtent(bounds, zoom);
    },
    publishSelection(selection) {
      knownSelection = selection;
      publish({ kind: 'selection', origin, selection });
    },
    muteUntilSettled: mute,
    detach() {
      detached = true;
      window.clearTimeout(followTimer);
      window.clearTimeout(establishTimer);
      unsubscribe();
    },
  };
}

/**
 * Whether two selections mean the same thing.
 *
 * By value rather than by reference on purpose: each view builds its own object for every pick, so
 * reference equality would report every echo as a change and every change as worth re-rendering
 * the detail panel for.
 */
export function sameSelection(
  left: WorkspaceSelection | null,
  right: WorkspaceSelection | null,
): boolean {
  if (left === right) {
    return true;
  }
  if (!left || !right || left.kind !== right.kind) {
    return false;
  }
  switch (left.kind) {
    case 'entrance':
      return (
        right.kind === 'entrance' &&
        left.entranceId === right.entranceId &&
        left.caveId === right.caveId
      );
    case 'feature':
      return right.kind === 'feature' && left.featureId === right.featureId;
    case 'cave':
      return right.kind === 'cave' && left.caveId === right.caveId;
    case 'cluster':
      return (
        right.kind === 'cluster' &&
        left.lon === right.lon &&
        left.lat === right.lat &&
        left.count === right.count &&
        left.zoom === right.zoom
      );
  }
}

/** Forgets the remembered extent. For tests, which must not inherit one another's view. */
export function resetViewSyncMemory(): void {
  lastExtent = undefined;
}
