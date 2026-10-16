// SPDX-License-Identifier: AGPL-3.0-or-later
import { lazy, Suspense, useState } from 'react';
import { Alert, Modal, Spin, Typography } from 'antd';
import Button from '../../Button.tsx';
import { useTranslation } from 'react-i18next';
import ErrorBoundary from '../../ErrorBoundary.tsx';
import type { TrackingMovieDialogProps } from './TrackingMovieDialog.tsx';

const TrackingMovieDialog = lazy(() => import('./TrackingMovieDialog.tsx'));

/**
 * The words a browser uses when a page asks for a part of the application that is no longer there:
 * the installation was updated after the page was opened, and the page still names the files of the
 * version it was opened on. Chromium, Firefox and Safari each word it their own way.
 */
const STALE_PAGE = /dynamically imported module|Importing a module script failed|Unable to preload CSS/i;

/**
 * The movie dialog, fetched the first time somebody opens it.
 *
 * The dialog brings the encoders, the recorder and a second viewer's host with it — none of which a
 * page that merely offers the button needs — so the pages carry only this. Once opened it stays
 * mounted, closed, so it can play its closing animation; its body goes with it on every close.
 *
 * <b>Whatever happens, pressing the button shows a dialog.</b> While its code arrives the dialog
 * says it is opening; if it cannot open it says so, with the reason, in the dialog — and the trip
 * page behind it stays as it was. Before this, the code arriving showed nothing at all, and a
 * failure either did the same or took the whole page down with it: a reader who pressed the
 * button saw nothing happen and could not tell a slow server from a broken one.
 */
export default function LazyTrackingMovieDialog(props: TrackingMovieDialogProps) {
  const open = props.surveyModelId !== null;
  const [wanted, setWanted] = useState(false);
  // Counts the openings, so that a failure belongs to the opening it happened in: the next press of
  // the button tries again rather than showing the last attempt's error.
  const [openings, setOpenings] = useState(0);
  const [wasOpen, setWasOpen] = useState(false);
  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) {
      setOpenings((count) => count + 1);
    }
  }
  if (open && !wanted) {
    setWanted(true);
  }
  if (!wanted && !open) {
    return null;
  }
  return (
    <ErrorBoundary
      key={openings}
      fallback={(detail) => <MovieDialogFailed open={open} detail={detail} onClose={props.onClose} />}
    >
      <Suspense fallback={<MovieDialogOpening open={open} onClose={props.onClose} />}>
        <TrackingMovieDialog {...props} />
      </Suspense>
    </ErrorBoundary>
  );
}

function MovieDialogOpening({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  return (
    <Modal open={open} title={t('caveview.movie.title')} onCancel={onClose} footer={null} data-testid="movie-dialog-opening">
      <Spin description={t('caveview.movie.opening')}>
        <div style={{ minHeight: 120 }} />
      </Spin>
    </Modal>
  );
}

function MovieDialogFailed({ open, detail, onClose }: { open: boolean; detail: string; onClose: () => void }) {
  const { t } = useTranslation();
  const stale = STALE_PAGE.test(detail);
  return (
    <Modal
      open={open}
      title={t('caveview.movie.title')}
      onCancel={onClose}
      data-testid="movie-dialog-failed"
      footer={[
        <Button key="close" onClick={onClose}>
          {t('common.close')}
        </Button>,
        <Button key="reload" type="primary" onClick={() => window.location.reload()}>
          {t('errorBoundary.reload')}
        </Button>,
      ]}
    >
      <Alert
        type="error"
        showIcon
        title={t('caveview.movie.openFailed')}
        description={
          stale ? (
            t('caveview.movie.openFailedStale')
          ) : (
            // The message itself: of no use to most readers, and the first thing asked for when one
            // of them tells whoever runs the installation that it does not open.
            <Typography.Text code>{detail}</Typography.Text>
          )
        }
      />
    </Modal>
  );
}
