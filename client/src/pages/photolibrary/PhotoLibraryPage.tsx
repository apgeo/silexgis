// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { LeftOutlined, RightOutlined } from '@ant-design/icons';
import { Alert, Button, Flex, Input, Segmented, Skeleton, Space, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { usePhotoLibraries, usePhotoLibraryPhotographs } from '../../api/hooks.ts';
import LibraryPhotoDrawer from '../../components/photolibrary/LibraryPhotoDrawer.tsx';
import LibraryPhotoGrid from '../../components/photolibrary/LibraryPhotoGrid.tsx';
import { browseState, pagingOf, stepPage } from './browseState.ts';

/** Pictures per page. The same number this installation's own gallery shows. */
const PageSize = 60;

/**
 * Looking through a photo library this installation does not own.
 *
 * <p>
 * A page rather than a map panel, because it is not a map. <b>There is no latitude here, no
 * rectangle, no coordinate-derived sort and nothing to pan</b> — this is a way of looking through
 * pictures, and where each was taken is a question the map answers. That is a property of how the
 * surface is built rather than something it remembers to hide, which is also why it can show what
 * a map cannot: most of a caving club's library has no position at all, and every one of those
 * pictures is here.
 * </p>
 * <p>
 * The library and the words are read from the address, so a filtered view is a link somebody can
 * send. The page number is not: it is a position in somebody's own reading of a list whose
 * contents change on the other side of a socket, and a link promising the fourth page of a
 * neighbouring library promises something nobody here controls.
 * </p>
 */
export default function PhotoLibraryPage() {
  const { t, i18n } = useTranslation();
  const [params, setParams] = useSearchParams();
  const { data: status, isPending: askingStatus } = usePhotoLibraries();

  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<string | null>(null);

  const libraries = useMemo(() => status?.providers ?? [], [status]);
  const named = params.get('source');
  const source = libraries.find((library) => library.source === named)?.source
    ?? libraries[0]?.source;
  const words = params.get('q') ?? '';

  const query = useMemo(
    () => ({ page, pageSize: PageSize, q: words || undefined }),
    [page, words],
  );

  const { data, isPending, error } = usePhotoLibraryPhotographs(source, query);
  const state = browseState({ isPending, error, page: data });
  // The page being asked for, not the page in hand: while one is being turned they differ, and the
  // controls follow the question rather than the answer that is still on screen.
  const paging = pagingOf(data, page);

  // An address naming a product this installation does not run draws the library it does run, and
  // says so by correcting itself. Left alone, the address and the screen would disagree for as long
  // as the reader had the page open — and the address is the half that gets copied and sent on, so
  // a link saying one archive would go on delivering another with nothing anywhere admitting it.
  useEffect(() => {
    if (named === null || source === undefined || named === source) {
      return;
    }

    const corrected = new URLSearchParams(params);
    corrected.set('source', source);
    setParams(corrected, { replace: true });
  }, [named, source, params, setParams]);

  if (askingStatus) {
    return <Spin style={{ display: 'block', marginTop: '20vh' }} />;
  }

  // Told no by the server rather than by a rail that does not offer the page, because the rail is
  // not a permission and a reader who arrived by an address deserves a sentence.
  if (status && !status.mayRead) {
    return (
      <Alert
        type="error"
        showIcon
        message={t('libraryPhotos.refusals.notAllowed')}
        style={{ margin: 16 }}
      />
    );
  }

  // An installation that runs none of these products is a supported installation, not a broken
  // one — so this says what is true rather than showing an empty grid.
  if (libraries.length === 0 || !source) {
    return (
      <Alert
        type="info"
        showIcon
        message={t('libraryPhotos.notConfigured')}
        style={{ margin: 16 }}
      />
    );
  }

  const setFilter = (key: string, value: string | undefined) => {
    const next = new URLSearchParams(params);
    if (value === undefined || value === '') {
      next.delete(key);
    } else {
      next.set(key, value);
    }

    setParams(next, { replace: true });
    setPage(1);
    setOpen(null);
  };

  const library = libraries.find((entry) => entry.source === source);

  return (
    <div style={{ padding: 24, height: '100%', overflow: 'auto' }}>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {t('libraryPhotos.browse.title')}
      </Typography.Title>

      {/* Said once, at the top, rather than left to be noticed. A reader who expects a map here
          and finds none is owed the reason, and the reason is that placing a photograph is a
          different act on a different surface. */}
      <Typography.Paragraph type="secondary" style={{ maxWidth: 720 }}>
        {t('libraryPhotos.browse.noPositionShown')}
      </Typography.Paragraph>

      <Flex wrap gap={8} align="center" style={{ marginBottom: 16 }}>
        {/* Only when there is a choice to make. Two of these products can be connected at once and
            they are separate installations with separate contents; one of them alone is not a
            choice and a control offering it would be furniture. */}
        {libraries.length > 1 && (
          <Segmented
            value={source}
            onChange={(value) => setFilter('source', String(value))}
            options={libraries.map((entry) => ({ value: entry.source, label: entry.name }))}
            data-testid="library-photo-source"
          />
        )}

        {/* Offered only where the library can answer it. One of the two products matches text over
            what a photograph says about itself and the other has no such question at all, and a
            box that quietly did nothing would be worse than no box: the server refuses the words
            rather than dropping them, so this control and that refusal agree. */}
        {data?.textSearchSupported === false ? (
          <Typography.Text type="secondary">
            {t('libraryPhotos.browse.searchUnsupported', { library: data.libraryName })}
          </Typography.Text>
        ) : (
          <Input.Search
            allowClear
            placeholder={t('libraryPhotos.browse.search')}
            defaultValue={words}
            onSearch={(value) => setFilter('q', value || undefined)}
            style={{ width: 260 }}
            data-testid="library-photo-search"
          />
        )}
      </Flex>

      {/* The library's own state, which is a different question from what came back for this page.
          A library can be perfectly healthy and hold nothing matching. */}
      {library?.health && !library.health.picturesAvailable && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          message={t('libraryPhotos.health.picturesStopped')}
        />
      )}

      {data?.pageSizeCapped && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          message={t('libraryPhotos.browse.pageCapped', { count: data.pageSize })}
        />
      )}

      {state === 'refused' && (
        <Alert type="error" showIcon message={t('libraryPhotos.refusals.notAllowed')} />
      )}

      {/* Words this application would not put to this library — carried in the address from a
          search of the other one, most often. Not a failure of anything: the box above is still
          there to clear, and saying "the library did not answer" would send a reader to look at a
          container that is working perfectly. */}
      {state === 'searchUnsupported' && (
        <Alert
          type="info"
          showIcon
          message={t('libraryPhotos.browse.searchUnsupported', {
            library: library?.name ?? source,
          })}
        />
      )}

      {/* Three answers, never one. A blank page because the library holds nothing matching, a blank
          page because the library did not answer, and a page still filling are different facts,
          and a surface that renders all three as emptiness is why somebody spends an afternoon
          debugging a library that was working. */}
      {state === 'silent' && (
        <Alert
          type="warning"
          showIcon
          message={t('libraryPhotos.browse.silent')}
          data-testid="library-photo-silent"
        />
      )}

      {state === 'loading' && <Skeleton active paragraph={{ rows: 8 }} />}

      {(state === 'photographs' || state === 'empty' || state === 'endOfList') && data && (
        <>
          <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 8 }}>
            <Typography.Text type="secondary">
              {/* Two sentences, because there are two answers and only one of them is a number.
                  One product states how many it holds and the other publishes no way to ask, and a
                  page that showed the count of what is on screen where the size of the library
                  belongs would be telling a reader the club has sixty photographs. Counted only
                  where there is something to count: a page with nothing on it says what it is
                  below, and "showing 0" over it would be a second, worse way of saying it. */}
              {paging.shown === 0
                ? null
                : paging.total === null
                  ? t('libraryPhotos.browse.showingUnknownTotal', { count: paging.shown })
                  : t('libraryPhotos.browse.showingOf', {
                      shown: paging.shown,
                      total: paging.total,
                    })}
            </Typography.Text>

            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('libraryPhotos.browse.readAt', {
                when: new Date(data.readAt).toLocaleTimeString(i18n.resolvedLanguage),
              })}
            </Typography.Text>
          </Flex>

          <LibraryPhotoGrid
            source={source}
            photographs={data.items}
            pictureUrlTemplate={data.pictureUrlTemplate}
            onOpen={setOpen}
            emptyText={
              // The two ways of arriving at an empty grid, kept apart. One is a library holding
              // nothing that matches; the other is a step past the end of a listing that holds
              // plenty — and one of the two products offers that step every time its library
              // happens to hold an exact multiple of a page.
              state === 'endOfList'
                ? t('libraryPhotos.browse.pastEnd')
                : t('libraryPhotos.browse.empty')
            }
          />

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
        </>
      )}

      <LibraryPhotoDrawer source={source} photographId={open} onClose={() => setOpen(null)} />
    </div>
  );
}
