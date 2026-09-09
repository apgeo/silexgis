// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { LeftOutlined, RightOutlined } from '@ant-design/icons';
import { Alert, Button, Flex, Input, Segmented, Skeleton, Space, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import {
  usePhotoLibraries,
  usePhotoLibraryPhotographs,
  usePhotoLibrarySearch,
} from '../../api/hooks.ts';
import LibraryPhotoDrawer from '../../components/photolibrary/LibraryPhotoDrawer.tsx';
import LibraryPhotoGrid from '../../components/photolibrary/LibraryPhotoGrid.tsx';
import {
  browseState,
  countLine,
  emptyMessage,
  pagingOf,
  problemOf,
  searchWording,
  stepPage,
} from './browseState.ts';

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

  // Only the libraries this installation is actually using. One it has stopped using is left out
  // of the chooser and out of everything downstream of it — including the search box, which is
  // offered per library — because the routes behind them refuse, and a control that can only fail
  // is worse than no control.
  const libraries = useMemo(
    () => (status?.providers ?? []).filter((library) => !library.suspended),
    [status],
  );

  // Whether anything was stopped, which is why the page may be empty. A different fact from an
  // installation that was never given a library, and the two send a reader to different places.
  const anySuspended = (status?.providers ?? []).some((library) => library.suspended);
  const named = params.get('source');
  const source = libraries.find((library) => library.source === named)?.source
    ?? libraries[0]?.source;
  const words = params.get('q') ?? '';

  // Two questions, two routes, and never both at once. Asking a library what it holds and asking
  // what it makes of a sentence are different questions with differently shaped answers — one of
  // the two products replies to the second with an ordering of its whole library rather than with
  // a narrowed list — so the screen asks one of them and says which one it is showing.
  const searching = words.trim().length > 0;

  const listing = useMemo(() => ({ page, pageSize: PageSize }), [page]);
  const search = useMemo(() => ({ q: words.trim(), page, pageSize: PageSize }), [page, words]);

  // The listing is not asked for while a search is on screen, and the search is not asked for
  // without words: each hook is given the library only when its own question is the one being put.
  const listed = usePhotoLibraryPhotographs(searching ? undefined : source, listing);
  const found = usePhotoLibrarySearch(source, search);
  const { data, isPending, error } = searching ? found : listed;

  const state = browseState({ isPending, error, page: data });
  const counted = data ? countLine(data) : null;
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
  // one — so this says what is true rather than showing an empty grid. An answer that never
  // arrived lands here too, and deliberately: with nothing said about which libraries exist there
  // is no library to draw and nothing further down has a number it could trust.
  //
  // Two sentences rather than one, because there are two reasons to be standing here and they are
  // not the same errand: nobody connected a library, or somebody stopped the one that is there.
  // Telling a reader to connect a library they already have is the wrong advice.
  if (!status || libraries.length === 0 || !source) {
    return (
      <Alert
        type="info"
        showIcon
        message={t(anySuspended ? 'libraryPhotos.health.suspended' : 'libraryPhotos.notConfigured')}
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

  // What this library does with words decides what the box invites, what its answer is called, and
  // what an empty or failed answer from it means. Read from what the server published about the
  // product rather than guessed at from its name.
  const wording = searchWording(library?.search ?? 'text');

  // The one sentence a failure gets, decided by the state and by which question was being put. The
  // length in it is the server's own rather than a second copy kept here: this box both stops short
  // of the limit and prints it in the sentence explaining the refusal, and two copies of one number
  // is how a screen goes on stating the old one after the server's has moved.
  const problem = problemOf(state, searching, wording, status.maxSearchLength);

  // The words as they were actually put to the library, which one of the two products reduces on
  // the way out. Compared against what is in the address rather than against what is in the box,
  // because the box may have been typed into again since this answer was asked for.
  const reduced = data && 'searched' in data && data.searched !== words.trim()
    ? data.searched
    : null;

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

        {/* Worded by what the library will actually do with the words, which the server publishes
            for each product. Both can be searched and they answer differently: one looks the words
            up in what somebody wrote down, the other compares them to the pictures themselves. A
            box inviting a description of a photograph over a library that can only look words up
            is a promise the far side cannot keep, and the nothing that comes back reads as an
            empty library. */}
        <Input.Search
          allowClear
          maxLength={status.maxSearchLength}
          placeholder={t(wording.placeholder)}
          defaultValue={words}
          onSearch={(value) => setFilter('q', value || undefined)}
          style={{ width: 280 }}
          data-testid="library-photo-search"
        />
      </Flex>

      {/* What kind of question was just asked, said where the answer to it is. Shown once there is
          an answer to explain: somebody who searched for a thing they remember seeing and got
          nothing is owed the difference between "this library holds nothing like that" and "this
          library was never looking at the pictures". */}
      {searching && (
        <Typography.Paragraph type="secondary" style={{ maxWidth: 720 }}>
          {t(wording.explains)}
        </Typography.Paragraph>
      )}

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

      {/* One sentence, chosen where the choosing can be checked. Several different things draw an
          empty screen — a right nobody has, a credential the library refused, an answer this build
          could not read, a question this application declined to put at all, and a library that is
          actually stopped — and a surface that renders them as one is why somebody spends an
          afternoon on a container that was working. */}
      {problem && (
        <Alert
          type={problem.kind}
          showIcon
          style={{ marginBottom: 12 }}
          message={t(problem.key, problem.values)}
          data-testid="library-photo-problem"
        />
      )}

      {/* What was actually asked, when it is not what was typed. One of the two products reads a
          colon as naming one of its own fields, so the separators come out before the words are
          sent — which is what stops a search box from becoming a way of asking where a photograph
          was taken. A reader who knows that product's own grammar would otherwise see this
          application disagree with it and have nothing anywhere to explain why. */}
      {reduced !== null && reduced !== '' && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          message={t('libraryPhotos.search.reduced', { words: reduced })}
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
              {counted && t(counted.key, counted.values)}
            </Typography.Text>

            {/* Only where a library was actually read. A search whose words reduced to nothing this
                product could search for was put to nobody, and a time stamped for a reading that
                never happened is a false statement on the one line a reader would use to decide
                whether the far side was reached at all. */}
            {data.readAt !== null && (
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {t('libraryPhotos.browse.readAt', {
                  when: new Date(data.readAt).toLocaleTimeString(i18n.resolvedLanguage),
                })}
              </Typography.Text>
            )}
          </Flex>

          <LibraryPhotoGrid
            source={source}
            photographs={data.items}
            pictureUrlTemplate={data.pictureUrlTemplate}
            onOpen={setOpen}
            // The ways of arriving at an empty grid, kept apart where the keeping apart can be
            // checked: a step past the end of a listing that holds plenty, a library that holds
            // nothing at all, a search that matched nothing, an ordering that ranked nothing, and
            // words that were reduced to nothing anybody could be asked about.
            emptyText={t(emptyMessage(state, data, wording))}
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
