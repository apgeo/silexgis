// SPDX-License-Identifier: AGPL-3.0-or-later

let deliberately = false;

/**
 * Marks this page as leaving on purpose, for good: a sign-out that has already cancelled its
 * renewal key and ended its sign-in cookie. The page's "leave with unsaved work?" questions must
 * let such a move through, because a member who chose to stay would be left on a page that still
 * looks signed in, holding an access token nothing can renew, with the server's session gone.
 */
export function leaveDeliberately(): void {
  deliberately = true;
}

/** Whether the page is being left on purpose, so no "stay?" question may hold it back. */
export function isLeavingDeliberately(): boolean {
  return deliberately;
}
