// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { LeftOutlined, RightOutlined } from '@ant-design/icons';
import { Alert, Button, Card, Flex, Segmented, Skeleton, Space, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { usePhotoLibraries, usePhotoLibraryPhotographs } from '../../api/hooks.ts';
import {
  browseState,
  countLine,
  emptyMessage,
  pagingOf,
  problemOf,
  searchWording,
  stepPage,
} from '../../pages/photolibrary/browseState.ts';
import LibraryPhotoDrawer from './LibraryPhotoDrawer.tsx';
import LibraryPhotoGrid from './LibraryPhotoGrid.tsx';

/** Pictures per page. The same number the library's own page and this application's gallery show. */
const PageSize = 60;

export interface TripLibraryPhotoPanelProps {
  tripId: string;
}

/**
 * The photographs a neighbouring library holds from the days this trip was out.
 *
 * <p>
 * <b>The window comes from the trip, on the server.</b> This panel names a trip and nothing else —
 * it sends no dates, and there is no shape of this component that could. The server reads the
 * trip's own start and end, decides whether this account may read that trip at all, and turns the
 * days into the question the library is asked. A window a browser chose would make this a general
 * date filter wearing a trip's name, and the provenance is the entire point of the panel.
 * </p>
 * <p>
 * <b>Nothing here is filed, bound or written.</b> Opening this asks a library a question and draws
 * the answer. No photograph becomes the trip's, nothing is copied, and closing the tab leaves both
 * sides exactly as they were. Saying a photograph belongs to a trip is a later feature and a
 * different decision.
 * </p>
 * <p>
 * <b>The window is a little wider than the trip, and the panel says so.</b> A trip records calendar
 * days with no time zone; a camera stamps instants. The two frames can be displaced by hours, so
 * the server reaches a day past each end rather than dropping the end of a long push and reporting
 * a smaller number with nothing saying the number is small. Every tile carries its own date, so the
 * few extra ones are visible for what they are.
 * </p>
 * <p>
 * The grid, the picture addresses, the drawer and every sentence about what an empty or failed
 * answer means are the browsing page's own. A second copy of any of them is how one surface comes
 * to describe a stopped container as an empty library while the other does not.
 * </p>
 */
export default function TripLibraryPhotoPanel({ tripId }: TripLibraryPhotoPanelProps) {
  const { t, i18n } = useTranslation();
  const { data: status, isPending: askingStatus } = usePhotoLibraries();

  const [page, setPage] = useState(1);
  const [source, setSource] = useState<string | undefined>(undefined);
  const [open, setOpen] = useState<string | null>(null);

  // Only the libraries this installation is actually using, as the library's own page decides it.
  // One that has been stopped is left out of the chooser and out of everything below it, because
  // the route behind it refuses and a control that can only fail is worse than no control.
  const usable = useMemo(
    () => (status?.providers ?? []).filter((library) => !library.suspended),
    [status],
  );

  const chosen = usable.find((library) => library.source === source)?.source ?? usable[0]?.source;

  const query = useMemo(() => ({ page, pageSize: PageSize, tripId }), [page, tripId]);
  const { data, isPending, error } = usePhotoLibraryPhotographs(chosen, query);

  const state = browseState({ isPending, error, page: data });
  const paging = pagingOf(data, page);

  if (askingStatus) {
    return <Skeleton active paragraph={{ rows: 3 }} />;
  }

  // Not for this account, so there is nothing to explain. Unlike the library's own page — which
  // somebody reached by an address and is owed a sentence — this panel is one section of a page
  // about something else, and an account with no right to a neighbouring library has no errand
  // that begins here.
  if (!status || !status.mayRead) {
    return null;
  }

  // An installation that runs no photo library is a supported installation and this panel is not
  // part of it: a card on every trip page explaining the absence of a product nobody deployed is
  // furniture. A library that exists and has been stopped is a different matter — somebody decided
  // that, and the reason the panel is empty is worth one line.
  if (status.providers.length === 0) {
    return null;
  }

  if (usable.length === 0 || !chosen) {
    return (
      <Card size="small" title={t('libraryPhotos.trip.title')} style={{ marginBottom: 16 }}>
        <Alert type="info" showIcon message={t('libraryPhotos.health.suspended')} />
      </Card>
    );
  }

  const library = usable.find((entry) => entry.source === chosen);

  // Whether the answer on screen was narrowed to a trip's days, read off the request that was
  // actually sent rather than written as a literal beside each sentence that depends on it. It is
  // always true here — this panel has a trip or it is not drawn — and that is exactly why it is
  // worth deriving: the two sentences below are the whole honesty of the surface, and a literal
  // that stopped agreeing with the request would be silently wrong in the worst direction.
  // "Showing 12 of 12 photographs the library holds" over a listing narrowed to one weekend tells
  // a reader their club owns twelve photographs when it owns forty thousand.
  const narrowedToTheTrip = query.tripId !== undefined;
  const counted = data ? countLine(data, narrowedToTheTrip) : null;

  // The sentence a failure gets, decided by the state. The two refusals this panel can meet which
  // the library's page cannot — a trip that is not this reader's to see, and a trip whose dates
  // cannot make a window — are named there rather than falling through to "the library did not
  // answer", which would send somebody to a container that is working perfectly.
  //
  // The wording is the listing's: no words are put to any library here, so the search-only
  // sentences are unreachable, and the length is the server's own number rather than a copy.
  const problem = problemOf(state, false, searchWording('text'), status.maxSearchLength);

  return (
    <Card
      size="small"
      title={t('libraryPhotos.trip.title')}
      style={{ marginBottom: 16 }}
      data-testid="trip-library-photos"
    >
      {/* Said at the top rather than left to be noticed. A reader counting the tiles against their
          own memory of the weekend is owed both halves: these came from a library this application
          does not own, and the days asked about reach one past each end of the trip. */}
      <Typography.Paragraph type="secondary" style={{ maxWidth: 720 }}>
        {t('libraryPhotos.trip.explains')}
      </Typography.Paragraph>

      {/* Only when there is a choice to make. Two of these products can be connected at once and
          they are separate installations with separate contents. */}
      {usable.length > 1 && (
        <Segmented
          value={chosen}
          onChange={(value) => {
            setSource(String(value));
            setPage(1);
            setOpen(null);
          }}
          options={usable.map((entry) => ({ value: entry.source, label: entry.name }))}
          style={{ marginBottom: 12 }}
          data-testid="trip-library-photo-source"
        />
      )}

      {/* The library's own state, which is a different question from what came back for these
          days. A library can be perfectly healthy and hold nothing from that weekend. */}
      {library?.health && !library.health.picturesAvailable && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          message={t('libraryPhotos.health.picturesStopped')}
        />
      )}

      {problem && (
        <Alert
          type={problem.kind}
          showIcon
          style={{ marginBottom: 12 }}
          message={t(problem.key, problem.values)}
          data-testid="trip-library-photo-problem"
        />
      )}

      {state === 'loading' && <Skeleton active paragraph={{ rows: 4 }} />}

      {(state === 'photographs' || state === 'empty' || state === 'endOfList') && data && (
        <>
          <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 8 }}>
            {/* What the number is a count of, and it is not the library: it counts what the
                library holds from these days. Counted only where there is something to count —
                an empty grid says what it is inside itself. */}
            <Typography.Text type="secondary">
              {counted && t(counted.key, counted.values)}
            </Typography.Text>

            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('libraryPhotos.browse.readAt', {
                when: new Date(data.readAt).toLocaleTimeString(i18n.resolvedLanguage),
              })}
            </Typography.Text>
          </Flex>

          <LibraryPhotoGrid
            source={chosen}
            photographs={data.items}
            pictureUrlTemplate={data.pictureUrlTemplate}
            onOpen={setOpen}
            // "Nothing was taken then" and "this library holds nothing" are different facts, and
            // only the first one is about the trip. Kept apart where the keeping apart is checked.
            emptyText={t(emptyMessage(state, data, searchWording('text'), narrowedToTheTrip))}
          />

          {(paging.hasPrevious || paging.hasNext) && (
            <Flex justify="center" style={{ marginTop: 16 }}>
              <Space>
                <Button
                  icon={<LeftOutlined />}
                  disabled={!paging.hasPrevious}
                  onClick={() => setPage((current) => stepPage(current, -1, paging))}
                >
                  {t('libraryPhotos.browse.previous')}
                </Button>
                <Typography.Text type="secondary">
                  {t('libraryPhotos.browse.page', { page: data.page })}
                </Typography.Text>
                <Button
                  icon={<RightOutlined />}
                  iconPosition="end"
                  disabled={!paging.hasNext}
                  onClick={() => setPage((current) => stepPage(current, 1, paging))}
                >
                  {t('libraryPhotos.browse.next')}
                </Button>
              </Space>
            </Flex>
          )}
        </>
      )}

      <LibraryPhotoDrawer source={chosen} photographId={open} onClose={() => setOpen(null)} />
    </Card>
  );
}
