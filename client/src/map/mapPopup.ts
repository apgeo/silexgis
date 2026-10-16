// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Marks a balloon opened over the map as something to read, and says what it is.
 *
 * The map's own element is an application region, because it takes the arrow keys and +/− for
 * itself; a screen reader inside such a region stops reading and hands every key to the page. A
 * balloon is drawn inside the map's element — that is where the map puts what it overlays — but
 * it holds text and links rather than a surface to steer, so it declares itself a document, which
 * puts the reader back into reading mode while inside it. The label is set each time the balloon
 * opens, so it follows the reader's language and names the thing it is about.
 */
export function describeMapPopup(element: HTMLElement, label: string): void {
  element.setAttribute('role', 'document');
  element.setAttribute('aria-label', label);
}
