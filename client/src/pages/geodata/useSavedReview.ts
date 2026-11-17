// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useRef, useState } from 'react';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

/** How long the reviewer has to pause before the review is saved. */
const SAVE_PAUSE_MS = 800;

/**
 * The life of a review the server keeps — applied once when it arrives, saved as the reviewer
 * works, started afresh after a confirmation — shared by every import workspace so a fix to it
 * lands in all of them at once.
 *
 * - The saved review is applied once and the browser then owns it: applying a refetch over the
 *   top would undo whatever the reviewer changed while the request was in flight.
 * - The autosave waits for a pause, so ticking through twenty rows sends one request.
 * - Only state built on the latest application is saved. Anything older is the blank the page
 *   started with, and saving it would write an empty review over the one just read. The count of
 *   applications is kept in a ref as well as in state because the ref moves in the same effect
 *   that applies the review, before the state copy that goes into the saved value has re-rendered.
 * - `restart` hands the page back to the server's copy: the next session read is applied again,
 *   which is what a confirmation needs once it has spent the review.
 *
 * `loaded` is state, not a ref, so whatever it enables (the preview) follows it on the render it
 * changes rather than on some later one.
 */
export function useSavedReview<Saved, Body>({
  saved,
  apply,
  body,
  save,
}: {
  /** The server's copy of the review, once read. */
  saved: Saved | undefined;
  /** Puts the server's copy into the page's state. */
  apply: (saved: Saved) => void;
  /** What the page would save now. */
  body: Body;
  /** Sends a save. */
  save: (body: Body) => void;
}): { loaded: boolean; hydration: number; restart: () => void } {
  const loadedRef = useRef(false);
  const [loaded, setLoaded] = useState(false);
  const hydrationRef = useRef(0);
  const [hydration, setHydration] = useState(0);

  useEffect(() => {
    if (saved && !loadedRef.current) {
      loadedRef.current = true;
      hydrationRef.current += 1;
      setHydration(hydrationRef.current);
      setLoaded(true);
      apply(saved);
    }
    // apply is the page's setter bundle; only a new copy from the server is a reason to run.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [saved]);

  const pending = useDebouncedValue(JSON.stringify({ hydration, body }), SAVE_PAUSE_MS);
  useEffect(() => {
    if (!loadedRef.current) {
      return;
    }
    const { hydration: builtOn, body: sent } = JSON.parse(pending) as { hydration: number; body: Body };
    if (builtOn !== hydrationRef.current) {
      return;
    }
    save(sent);
    // save wraps a stable mutation object; including it would resend on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pending]);

  const restart = useCallback(() => {
    loadedRef.current = false;
    setLoaded(false);
  }, []);

  return { loaded, hydration, restart };
}
