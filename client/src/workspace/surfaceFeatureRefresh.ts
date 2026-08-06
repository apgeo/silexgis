// SPDX-License-Identifier: AGPL-3.0-or-later

// "The surface features changed" — announced once, heard by every view that draws them.
//
// The overlays that draw features do not read them through the query cache: they ask for the box
// the viewer is looking at and keep what came back, which means a write has no cached key anyone
// can invalidate. Each view has to refetch its own box instead.
//
// Announcing rather than calling is what makes that safe to extend. A write path (an edit, a
// delete, a restore of an earlier version) knows a feature changed and nothing else; it must not
// also have to know which views exist, and a write made from a page with no map open should reach
// nobody rather than reach into a view that is not mounted. Each view registers what it does about
// the news while it is on screen, and stops when it goes away.

const listeners = new Set<() => void>();

/** Registers a view's refetch, and returns an unsubscribe function. */
export function onSurfaceFeaturesChanged(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** Announces a write to the surface features; every mounted view refetches what it is showing. */
export function surfaceFeaturesChanged(): void {
  for (const listener of [...listeners]) {
    listener();
  }
}
