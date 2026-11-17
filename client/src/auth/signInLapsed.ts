// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';

interface SignInLapsedState {
  /** Whether a request was refused because the sign-in is over and could not be renewed. */
  lapsed: boolean;
  /** The person chose to stay on the page for now; a later refusal raises the notice again. */
  dismiss(): void;
}

/**
 * Whether this tab's sign-in has ended, as a request the server refused found out.
 *
 * Raised by the transport and turned into a notice by the signed-in shell. The transport does not
 * send the person to sign in by itself: signing in again is a full redirect, and a refusal can come
 * from a background refetch or from a save whose form is the only copy of what was typed. Leaving is
 * the person's choice, made from the notice once they have kept what they need.
 */
export const useSignInLapsed = create<SignInLapsedState>()((set) => ({
  lapsed: false,
  dismiss: () => set({ lapsed: false }),
}));

/** Records that the sign-in is over. Called from outside React, by the transport. */
export function signInLapsed(): void {
  if (!useSignInLapsed.getState().lapsed) useSignInLapsed.setState({ lapsed: true });
}
