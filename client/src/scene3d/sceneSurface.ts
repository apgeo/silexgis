// SPDX-License-Identifier: AGPL-3.0-or-later

// The one element in this window that a 3D scene is ever drawn into.
//
// A browser keeps only a handful of 3D drawing contexts alive and silently drops the oldest when a
// new one is asked for, so a window must never hold two scenes. The scene module already refuses
// to build a second one — but it refuses by throwing, and a route and a workspace panel that are
// briefly mounted at the same time (which is what a route change is) would turn that refusal into
// an error box on somebody's screen. Refusal is the wrong shape: what is wanted is that the second
// mount *joins* the first.
//
// So the element itself is the singleton, not just the scene inside it. There is one of it per
// window, it is created here, and a view that wants to show the scene does not build a container —
// it lends the scene one of its own boxes and this module moves the surface into it. The scene
// module is then always handed the same element and its "already attached elsewhere" check can
// never fire, which is the difference between "two scenes are unlikely" and "two scenes cannot be
// asked for".
//
// Claims are a stack rather than a single slot, for the same reason the view-camera registry
// checks identity before clearing: during a route change the arriving view claims before the
// leaving one lets go, and a plain last-write-wins would leave the surface homeless the moment the
// leaving view's cleanup ran. Whoever is on top of the stack has it; when they let go it goes back
// to whoever is under them.
//
// Deliberately engine-free, so it can be imported statically by anything: it creates a `div` and
// moves it, and knows nothing about what draws inside.

/** Told when this claim gains or loses the surface, so a view can say why its box is empty. */
type HeldListener = (held: boolean) => void;

interface SurfaceClaim {
  slot: HTMLElement;
  onHeldChanged?: HeldListener;
}

let surface: HTMLDivElement | undefined;
const claims: SurfaceClaim[] = [];

/**
 * The element the scene draws into, created on first use.
 *
 * It starts outside the document and returns there whenever no view is showing it. A detached
 * drawing surface has no size, which every renderer already has to cope with (a hidden tab is the
 * same situation).
 *
 * What this buys is that two views which overlap in time share one scene rather than one of them
 * being refused — it does NOT keep the scene alive across a gap. When the last view lets go, the
 * scene itself is torn down and the next view builds a new one, which costs a fresh graphics
 * context per visit. That is the deliberate trade: at most one context is ever alive, which is the
 * property that matters, since a browser keeps only a handful and silently drops the oldest.
 */
export function sceneSurfaceElement(): HTMLDivElement {
  if (!surface) {
    surface = document.createElement('div');
    surface.className = 'scene3d-surface';
    surface.dataset.testid = 'scene3d-surface';
  }
  return surface;
}

/**
 * Puts the scene surface inside `slot` and keeps it there until the returned function is called.
 *
 * A claim made while another one holds the surface takes it — the newest mount is the one the
 * viewer is looking at — and the displaced claim is told, so it can show something honest rather
 * than an unexplained empty rectangle.
 */
export function claimSceneSurface(slot: HTMLElement, onHeldChanged?: HeldListener): () => void {
  const claim: SurfaceClaim = { slot, onHeldChanged };
  const displaced = claims.at(-1);
  claims.push(claim);
  displaced?.onHeldChanged?.(false);
  attachTo(claim);

  let released = false;
  return () => {
    if (released) {
      return;
    }
    released = true;
    const index = claims.indexOf(claim);
    if (index === -1) {
      return;
    }
    const wasHolding = index === claims.length - 1;
    claims.splice(index, 1);
    if (!wasHolding) {
      return; // Somebody else has it; taking it off them now would blank their panel.
    }
    const next = claims.at(-1);
    if (next) {
      attachTo(next);
    } else {
      surface?.remove();
    }
  };
}

/** How many views are currently showing the scene. Exists so a test can state the invariant. */
export function sceneSurfaceClaimCount(): number {
  return claims.length;
}

function attachTo(claim: SurfaceClaim): void {
  const element = sceneSurfaceElement();
  if (element.parentElement !== claim.slot) {
    claim.slot.append(element);
  }
  claim.onHeldChanged?.(true);
}
