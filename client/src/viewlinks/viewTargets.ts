// SPDX-License-Identifier: AGPL-3.0-or-later
import { publish, subscribe } from '../workspace/workspaceBus.ts';
import type { ResourceRef, ViewControlDescriptor, ViewControlKind } from './resourceRef.ts';

/**
 * The views that are on screen and can be asked to show something.
 *
 * <b>Why this is not the camera registry.</b> There is already a place a view registers itself —
 * `workspace/viewCamera.ts` — and it is a *stack*: the newest registration is the one a command
 * reaches, and the rest wait underneath. That is right for what it does, which is "zoom the view
 * the viewer is looking at to this feature": there is one such view, and sending the command to
 * all of them would move a map somebody left framed on something else.
 *
 * Following a hyperlink is the opposite case. The reader is a panel beside several views at once
 * and the whole point is that all of them answer — the map pans, the scene flies, the survey
 * viewer picks out the station — and that each can be muted on its own. So this is a *set*, and
 * a set with addresses, because a reader may also say "only that one".
 *
 * Both exist and neither replaces the other: a view typically registers with both, for the two
 * different questions.
 *
 * <b>Views answer for themselves.</b> A control says whether it can show a reference before it
 * is asked to, and the answer is the control's own — the map knows a survey station means
 * nothing to it, and the model viewer knows it holds a different cave's model. Nothing here
 * decides on their behalf from the reference's type, which would put a table of what each view
 * understands in a third place that has to be edited whenever any of them learns something.
 */

/** A view that can be asked to show a resource, for as long as it is mounted. */
export interface ViewControl {
  /** Unique within this window; the address is this plus the window's id. */
  id: string;
  kind: ViewControlKind;
  /** i18n key naming it in a menu. */
  labelKey: string;
  /**
   * Whether this control could show that reference *now* — the right cave loaded, a geometry it
   * can reach, an anchor kind it understands. A control that answers false is listed as unable
   * rather than hidden: a reader who expected the map to move needs to know it could not, not
   * to be left wondering whether they missed it.
   */
  canReveal(ref: ResourceRef): boolean;
  /** Show it, in this view's own idiom: pan, fly, select, highlight. */
  reveal(ref: ResourceRef): void;
}

/**
 * This window's identity on the bus. Random per window rather than derived from anything: two
 * pop-outs opened from the same page have the same everything else.
 */
const windowId = crypto.randomUUID();

const controls = new Map<string, ViewControl>();
const rosterListeners = new Set<() => void>();

/** Controls in other windows, by window id, as of the last roll call each answered. */
const remote = new Map<string, ViewControlDescriptor[]>();

let busAttached = false;

export function addressOf(control: { id: string }): string {
  return `${windowId}:${control.id}`;
}

function descriptorFor(control: ViewControl): ViewControlDescriptor {
  return { address: addressOf(control), kind: control.kind, labelKey: control.labelKey };
}

function announce(): void {
  publish({ kind: 'controls-here', windowId, controls: [...controls.values()].map(descriptorFor) });
}

function rosterChanged(): void {
  for (const listener of rosterListeners) {
    listener();
  }
}

/**
 * Starts listening for reveals and roll calls. Called by every entry point into this module
 * rather than at import time, so a window that never mounts a view control and never reads a
 * roster does not join the conversation.
 */
function attach(): void {
  if (busAttached) {
    return;
  }

  busAttached = true;

  subscribe((event) => {
    if (event.kind === 'reveal') {
      for (const control of controls.values()) {
        if (event.to && !event.to.includes(addressOf(control))) {
          continue;
        }

        // Asked again here rather than trusted from the sender. The roster the reader chose
        // from was gathered before the click, and a view can stop being able to show something
        // between the two — the model viewer is loading another cave, the map has been closed.
        if (control.canReveal(event.ref)) {
          control.reveal(event.ref);
        }
      }

      return;
    }

    if (event.kind === 'controls-roll-call') {
      announce();
      return;
    }

    if (event.kind === 'controls-here') {
      if (event.windowId === windowId) {
        // Our own announcement, delivered back to us. The local controls are known exactly;
        // reading them from a message would be the same list, one round trip later.
        return;
      }

      remote.set(event.windowId, [...event.controls]);
      rosterChanged();
      return;
    }

    if (event.kind === 'controls-gone' && remote.delete(event.windowId)) {
      rosterChanged();
    }
  });

  // A window that is going away says so, which is what keeps a closed pop-out from sitting in
  // somebody's menu until the next roll call. `pagehide` rather than `unload`: it is the event
  // that still fires when a page is put into the back/forward cache, and the one browsers have
  // not been steadily deprecating.
  window.addEventListener('pagehide', () => {
    publish({ kind: 'controls-gone', windowId });
  });
}

/**
 * Registers a view for as long as it is mounted, and returns a detach function.
 *
 * Detaching twice is harmless — React runs an effect's cleanup and setup twice in development to
 * surface exactly this class of bug, and a control that removed somebody else's registration on
 * its second cleanup would leave a mounted view unreachable for the rest of the session.
 */
export function registerViewControl(control: ViewControl): () => void {
  attach();
  controls.set(control.id, control);
  rosterChanged();
  announce();

  let released = false;
  return () => {
    if (released) {
      return;
    }

    released = true;
    controls.delete(control.id);
    rosterChanged();
    announce();
  };
}

/** The controls in this window, in registration order. */
export function localControls(): ViewControlDescriptor[] {
  return [...controls.values()].map((control) => ({ ...descriptorFor(control), local: true }));
}

/**
 * Every control anybody has announced, this window's first.
 *
 * Local entries are exact. Remote ones are as of the last roll call, which
 * {@link refreshRoster} is for — call it when a menu that lists them is about to open.
 */
export function allControls(): ViewControlDescriptor[] {
  return [
    ...localControls(),
    ...[...remote.values()].flat().map((descriptor) => ({ ...descriptor, local: false })),
  ];
}

/**
 * Asks every other window to say what it has, and resolves once they have had time to answer.
 *
 * The wait is a fixed short pause rather than a count of expected replies, because there is no
 * way to know how many windows are open — that is the very thing being asked. It is short
 * enough to sit inside the gesture that opens a menu and long enough for a same-machine
 * `BroadcastChannel` round trip, which is a task-queue hop rather than a network one.
 */
export async function refreshRoster(): Promise<ViewControlDescriptor[]> {
  attach();
  publish({ kind: 'controls-roll-call' });
  await new Promise((resolve) => setTimeout(resolve, ROLL_CALL_MS));
  return allControls();
}

/** How long a roll call waits for answers. */
export const ROLL_CALL_MS = 120;

export function subscribeToRoster(listener: () => void): () => void {
  attach();
  rosterListeners.add(listener);
  return () => rosterListeners.delete(listener);
}

/**
 * Asks views to show a reference.
 *
 * `to` names the controls that should act; leaving it out means every control in every window
 * that can. Published on the bus even when the only controls are in this window, because the bus
 * delivers to its own window synchronously — so there is one path, and a pop-out opened later
 * behaves the same as one that was open all along.
 */
export function reveal(ref: ResourceRef, to?: readonly string[]): void {
  attach();
  publish({ kind: 'reveal', ref, to });
}

/**
 * Which of the given controls could show this reference, as far as this window can tell.
 *
 * Only local controls can be asked — `canReveal` is a function, and functions do not cross a
 * `BroadcastChannel`. A control in another window is therefore listed as available and asked
 * when the reveal arrives, where it answers for itself. That asymmetry is deliberate: the
 * alternative is a request/response round trip per remote control every time a hover card opens,
 * to grey out a menu item.
 */
export function canRevealHere(ref: ResourceRef): Set<string> {
  const able = new Set<string>();
  for (const control of controls.values()) {
    if (control.canReveal(ref)) {
      able.add(addressOf(control));
    }
  }

  return able;
}

/** Test seam: forgets every registration and every remembered window. */
export function resetViewControlsForTests(): void {
  controls.clear();
  remote.clear();
  rosterListeners.clear();
}
