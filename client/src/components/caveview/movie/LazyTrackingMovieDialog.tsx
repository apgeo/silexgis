// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense, useState } from 'react';
import type { TrackingMovieDialogProps } from './TrackingMovieDialog.tsx';

const TrackingMovieDialog = lazy(() => import('./TrackingMovieDialog.tsx'));

/**
 * The movie dialog, fetched the first time somebody opens it.
 *
 * The dialog brings the encoders, the recorder and a second viewer's host with it — none of which a
 * page that merely offers the button needs — so the pages carry only this. Once opened it stays
 * mounted, closed, so it can play its closing animation; its body goes with it on every close.
 */
export default function LazyTrackingMovieDialog(props: TrackingMovieDialogProps) {
  const [wanted, setWanted] = useState(false);
  if (props.surveyModelId !== null && !wanted) {
    setWanted(true);
  }
  if (!wanted && props.surveyModelId === null) {
    return null;
  }
  return (
    <Suspense fallback={null}>
      <TrackingMovieDialog {...props} />
    </Suspense>
  );
}
