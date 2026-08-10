// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Where a picture lands when somebody drags it, worked out in one place.
 *
 * <p>
 * The server is told "put this one after that one" rather than a list of positions, because two
 * people arranging the same album at once must not overwrite each other's whole ordering — one
 * move is one move, and the second arrives as a move rather than as a stale snapshot of every
 * position. So the whole of the client's job is turning a drop into that one sentence.
 * </p>
 * <p>
 * The rule is: the dragged picture takes the index the target occupies now. That reads correctly
 * in both directions, which the naive "insert before the target" does not — dragging something
 * rightward onto a tile and having it land to that tile's left looks like the drag missed.
 * </p>
 */

/** Where a picture goes: after the named one, or first when null. */
export interface Placement {
  afterId: string | null;
}

/**
 * The placement for dropping <paramref name="movedId" /> onto <paramref name="targetId" />, or
 * null when the drop changes nothing (onto itself, or onto something not in the album).
 */
export function placementForDrop(
  order: readonly string[],
  movedId: string,
  targetId: string,
): Placement | null {
  if (movedId === targetId) {
    return null;
  }

  const targetIndex = order.indexOf(targetId);
  if (targetIndex < 0 || !order.includes(movedId)) {
    return null;
  }

  // Read from the order with the dragged picture already taken out, since that is the list the
  // insertion happens into — reading it from the original would name the picture being moved.
  const rest = order.filter((id) => id !== movedId);
  return { afterId: rest[targetIndex - 1] ?? null };
}

/**
 * The placement for nudging a picture one step with the keyboard.
 *
 * Dragging is a mouse gesture and an album of two hundred has to be arrangeable without one, so
 * every tile also moves by a step. A step is the same operation as dropping onto the neighbour,
 * which is why it is expressed as one rather than as a second ordering rule.
 */
export function placementForStep(
  order: readonly string[],
  movedId: string,
  direction: -1 | 1,
): Placement | null {
  const index = order.indexOf(movedId);
  if (index < 0) {
    return null;
  }

  const neighbour = order[index + direction];
  return neighbour === undefined ? null : placementForDrop(order, movedId, neighbour);
}

/**
 * The order after a placement is applied.
 *
 * Used to redraw before the server answers. A grid that waited for the round trip would show the
 * picture snapping back to where it was for as long as the request takes, which reads as the drag
 * having failed.
 */
export function reorder(
  order: readonly string[],
  movedId: string,
  { afterId }: Placement,
): string[] {
  const rest = order.filter((id) => id !== movedId);
  const at = afterId === null ? 0 : rest.indexOf(afterId) + 1;
  return [...rest.slice(0, at), movedId, ...rest.slice(at)];
}
