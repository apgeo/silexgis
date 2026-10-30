// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, readJson } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid } from './shared.ts';
import type { LibraryPhotographQuery, LibrarySearchQuery } from './types.ts';
import { queryKeys } from './queryKeys.ts';

/**
 * Which neighbouring photo library a request is about, named the way the server names it.
 *
 * Taken from the generated contract rather than written here as a union of the products that
 * happen to exist today: a third one added on the server would leave a hand-written union
 * type-checking against a value it has never heard of, and failing only at runtime.
 */
export type LibraryPhotoSource = LibraryPhotoProvider['source'];
/**
 * How a neighbouring library answers words: `text` when it matches them against what somebody wrote
 * down about a photograph — a title, a caption, a keyword, a label its own classifier produced —
 * and `meaning` when it turns them into a description of an image and orders what it holds by
 * closeness to that description.
 *
 * Read rather than assumed, because it decides what a person is invited to type: a box reading
 * "describe the picture" over a library that can only look up words is a promise the far side
 * cannot keep, and the empty answer that follows reads as an empty library.
 */
export type LibrarySearchMatching = 'text' | 'meaning';

export type LibraryPhotoProvider = components['schemas']['PhotoLibraryProviderDto'] & {
  search: LibrarySearchMatching;
};
export type LibraryPhotoStatus = Omit<
  components['schemas']['PhotoLibraryStatusDto'],
  'providers' | 'unconfigured'
> & {
  providers: LibraryPhotoProvider[];
  unconfigured: LibraryPhotoProvider[];
  /**
   * The longest run of words the server will put in a request to a library. Read rather than held
   * as a second copy here: this screen both stops its box short of the limit and prints the limit
   * in the sentence explaining a refusal, and two copies of one number is how a sentence goes on
   * stating the old one after the server's has moved.
   */
  maxSearchLength: number;
};
export type LibraryPhotoCollection = components['schemas']['LibraryPhotoFeatureCollection'];

/**
 * What one library said about itself when the server last asked it.
 *
 * A separate answer from the overlay's own load state, and the two are not interchangeable: this
 * one says whether the library is working at all, and the load state says what came back for the
 * rectangle currently on screen. A library can be perfectly healthy and hold nothing here.
 */
export type LibraryPhotoHealth = components['schemas']['PhotoLibraryHealthDto'];

/**
 * How long a reading of a library's health stands, matching the window the server holds one for.
 *
 * Stated once and used for both the freshness and the timer below, so the two cannot drift apart
 * into a client that re-asks faster than the server will ever answer differently, or slower than
 * the operator loop the short window was chosen for: restart a container, look at the line.
 */
const PHOTO_LIBRARY_HEALTH_WINDOW_MS = 30_000;

export interface PhotoLibraryStatusUse {
  /**
   * Whether this caller is watching what the libraries answered when they were last asked, rather
   * than only whether this installation has one. False asks once and lets other callers' timers
   * refresh it.
   */
  watchingHealth?: boolean;
}

/**
 * The photo libraries this account may see, or none.
 *
 * A query rather than an imperative fetch, unlike the map loaders below it: there is no viewport
 * in this question, so it is asked once and answered from cache while the map is panned. An
 * account outside the audience is told it may read nothing and given an empty list — the answer
 * a client needs in order to decide whether to offer the overlay at all, without being told which
 * products this installation runs.
 *
 * It used to be held for five minutes, which was right while the answer was a static one about
 * what an operator had typed into a settings file. The answer now carries what each library said
 * when it was last asked, and the server holds one such reading for half a minute — so anything
 * held here for longer hands an operator a health line older than the server was ever willing to
 * serve, and the short window on the far side buys nothing on the path anybody actually uses.
 * Kept to the same half minute for that reason, and asked again while somebody is looking at it,
 * because what it describes is the state of another container and changes without anything
 * happening in this browser.
 *
 * The timer runs only while there is a library being used to report on: an installation that runs
 * none of these products has nothing to poll for, and neither has one whose libraries an
 * administrator has all stopped — the server asks a stopped library nothing, so re-asking would
 * fetch the same stored decision over and over, and that decision changes only when somebody
 * changes it on a settings screen, which invalidates this answer directly. Coming back to the tab
 * still re-asks, so a brake released elsewhere is picked up as soon as anybody looks. It also stops
 * of its own accord while the tab is in the background, which is the default and is wanted here — a
 * map left open in a tab nobody is looking at should not keep a neighbouring container awake.
 *
 * And it runs only for a caller that is watching the health. Two things read this answer and they
 * want different halves of it: a surface showing whether a library is up is watching something
 * that changes on its own, while a caller asking only whether there is a library to offer at all
 * is asking something that cannot change without a server restart. The second kind mounts for the
 * whole of a session, on every page — so a timer inherited from the first would turn a question
 * asked once into a request twice a minute, for every signed-in account, forever.
 */
export function usePhotoLibraries({ watchingHealth = true }: PhotoLibraryStatusUse = {}) {
  return useQuery({
    queryKey: queryKeys.photoLibraryStatus,
    // Named as the shape the server sends. This answer says how each library answers words, which
    // a screen reads to word its search box, and the contract types this client is built against
    // are read from a running API rather than from the source — so until they are read again the
    // field is described here instead.
    queryFn: () =>
      unwrap(api.GET('/api/v1/photo-libraries/status')) as Promise<LibraryPhotoStatus>,
    staleTime: PHOTO_LIBRARY_HEALTH_WINDOW_MS,
    refetchInterval: (query) =>
      watchingHealth && (query.state.data?.providers ?? []).some((library) => !library.suspended)
        ? PHOTO_LIBRARY_HEALTH_WINDOW_MS
        : false,
    refetchOnWindowFocus: watchingHealth,
  });
}

/**
 * Imperative fetch used by the photo-library overlays (not a hook), one library per call.
 *
 * Imperative for the reason every other map loader here is: a bbox that changes with every pan is
 * an unbounded cache key, and the loader's own sequence guard is cheaper than fighting a query
 * cache for last-write-wins.
 *
 * The library is a path segment rather than a filter, because it selects which foreign
 * installation is called. One request per library, never one for both: they are separate
 * installations with separate uptime, and a joined request would be as slow as the slower of them
 * and as broken as the more broken one.
 */
export async function fetchLibraryPhotoFeatures(
  source: LibraryPhotoSource,
  bbox: string,
  signal?: AbortSignal,
): Promise<LibraryPhotoCollection> {
  return unwrap(
    api.GET('/api/v1/photo-libraries/{source}/map', {
      params: { path: { source }, query: { bbox } },
      signal,
    }),
  );
}

/**
 * Asks the server to re-open one library's picture delivery and to forget what it last heard
 * about that library's health.
 *
 * The status answer is invalidated rather than patched, because the point of pressing this is to
 * find out what the library says now: a button that reopened the pictures and left the health
 * line describing the state before the fix would look like a button that does nothing.
 *
 * Invalidated whether the call succeeded or not, and that is the case it matters in. A recheck
 * fails precisely when the library is still not working — which is when an operator has just
 * tried something and is watching this panel to find out whether it took. The server forgets its
 * held reading on both paths too, so re-asking here is what turns that into a line on the screen.
 */
export function useRecheckPhotoLibrary() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (source: LibraryPhotoSource) =>
      // unwrapVoid, not unwrap: this route answers 204 with no body, and unwrap treats an absent
      // body as a failure. Through unwrap the button reported "the library still did not answer"
      // every time the recheck succeeded — the one message that must never be wrong, on the one
      // control an operator presses when they already suspect something is broken.
      unwrapVoid(
        api.POST('/api/v1/photo-libraries/{source}/recheck', { params: { path: { source } } }),
      ),
    onSettled: () =>
      void queryClient.invalidateQueries({ queryKey: queryKeys.photoLibraryStatus }),
  });
}

/**
 * What one photograph in a neighbouring library is to become in this installation's own registry.
 *
 * Note what is not in it: a coordinate. The caller names a photograph and a rectangle to look for
 * it in, and the server reads the position from the library that holds it — a body able to carry a
 * latitude would be a way of putting an object anywhere at all while it looked as though a camera
 * had measured it, and the provenance is the whole point of creating one this way.
 */
export type LibraryPhotoFeatureRequest = components['schemas']['PhotoLibraryFeatureRequest'];
export type LibraryPhotoFeatureCreated = components['schemas']['PhotoLibraryFeatureCreatedDto'];

/**
 * An object already in the registry near where a photograph was taken.
 *
 * Information rather than a refusal: the answer comes back beside an object that was created, and
 * it exists because forty photographs of one entrance would otherwise quietly become forty caves.
 * An empty list is not a promise that nothing is there — the server searches only what this caller
 * may both read and place exactly.
 */
export type LibraryPhotoNearbyFeature = components['schemas']['PhotoLibraryNearbyFeatureDto'];

/**
 * Creates a cave, an entrance, or a feature of another kind at the position one photograph in a
 * neighbouring library records.
 *
 * The map overlays are refreshed by the caller rather than here: they are OpenLayers sources loaded
 * imperatively per viewport, not query-cache entries, so what has to happen after this is a reload
 * of the extent on screen and not an invalidation.
 */
export function useCreateFeatureFromLibraryPhoto() {
  return useMutation({
    mutationFn: (input: {
      source: LibraryPhotoSource;
      reference: string;
      bbox: string;
      body: LibraryPhotoFeatureRequest;
    }) =>
      unwrap(
        api.POST('/api/v1/photo-libraries/{source}/photographs/{reference}/feature', {
          params: {
            path: { source: input.source, reference: input.reference },
            query: { bbox: input.bbox },
          },
          body: input.body,
        }),
      ),
  });
}

/**
 * One photograph a neighbouring library holds, as a list of it reports one.
 *
 * There is no latitude and no longitude here, and there is no shape of this record that has them.
 * A list of a neighbouring library is a way of looking through pictures rather than a second map;
 * where each was taken is the map's question, and the map is the surface that answers it.
 */
export type LibraryPhotograph = components['schemas']['LibraryPhotographDto'];

/** One page of a neighbouring library, and the few things a grid needs to explain itself. */
export type LibraryPhotographPage = components['schemas']['LibraryPhotographPageDto'];

/** One photograph in full, as far as the library that holds it will say. */
export type LibraryPhotographDetail = components['schemas']['LibraryPhotographDetailDto'];

/** The address of one page of one library. Built here so the two hooks below cannot disagree. */
function photographsUrl(source: LibraryPhotoSource, query: LibraryPhotographQuery): string {
  const search = new URLSearchParams({
    page: String(query.page),
    pageSize: String(query.pageSize),
  });
  if (query.tripId !== undefined) {
    search.set('tripId', query.tripId);
  }
  if (query.albumId !== undefined) {
    search.set('albumId', query.albumId);
  }
  return `/api/v1/photo-libraries/${encodeURIComponent(source)}/photographs?${search.toString()}`;
}

/**
 * One page of a neighbouring library's photographs.
 *
 * A query rather than an imperative fetch, unlike the map loaders: a page is a thing somebody is
 * looking at rather than a viewport that changes with every pan, so paging back and forth is
 * answered from the cache instead of asking a neighbouring container again.
 *
 * `keepPreviousData` so turning a page keeps the grid on screen while the next one arrives. The
 * alternative is a page that empties and refills, which reads as a library that briefly held
 * nothing — and telling "nothing here" apart from "not yet" is most of what this screen owes its
 * reader.
 */
export function usePhotoLibraryPhotographs(
  source: LibraryPhotoSource | undefined,
  query: LibraryPhotographQuery,
) {
  return useQuery({
    queryKey: queryKeys.libraryPhotographs(source ?? '', query),
    queryFn: () => readJson<LibraryPhotographPage>(photographsUrl(source!, query)),
    enabled: source !== undefined,
    // Turning a page keeps the grid on screen while the next one arrives, so the page does not
    // empty and refill — which reads as a library that briefly held nothing, and telling "nothing
    // here" apart from "not yet" is most of what this screen owes its reader.
    //
    // Only within one library and one trip, though. One library's photographs drawn under
    // another's name would ask the wrong library about anything opened from them; and one trip's
    // photographs drawn under another trip's heading is the one claim this whole surface exists to
    // make honestly, so it must not be made by a grid that has not caught up yet.
    placeholderData: (
      previous?: LibraryPhotographPage,
      previousQuery?: { queryKey: readonly unknown[] },
    ) => {
      const asked = previousQuery?.queryKey[3] as LibraryPhotographQuery | undefined;
      return previous?.source === source
        && asked?.tripId === query.tripId
        && asked?.albumId === query.albumId
        ? previous
        : undefined;
    },
    // The far side is a separate product somebody else is filing pictures into, so a page held for
    // long enough to feel instant is also a page that stops being what the library holds. Half a
    // minute is the same window this application already holds a library's health for.
    staleTime: 30_000,
    // Not on coming back to the tab, unlike almost everything else here, and the reason is the
    // picture addresses rather than the photographs. Each answer carries a freshly minted,
    // short-lived credential in every tile's address, so an answer asked for again is sixty
    // addresses the browser has never seen and cannot revalidate — a page of derivatives fetched
    // afresh through this application and out of the neighbouring container, for a screen nobody
    // has touched.
    refetchOnWindowFocus: false,
    // Not retried here. The refusal a library that did not answer produces has already been
    // through this application's own attempts at the far side, so three more rounds with a
    // second's, two seconds' and four seconds' wait between them add nothing but the seven seconds
    // a reader spends before the sentence written for exactly this case appears.
    retry: false,
  });
}

/**
 * One album a neighbouring library keeps.
 *
 * <p>
 * Every field but the identifier may be null, and null means <b>the library did not say</b> rather
 * than empty: the two products describe an album differently, and what only one of them answers is
 * marked absent instead of being filled in from somewhere adjacent. The count is the library's own
 * number where the product publishes one — a fact about the library and not about whoever is
 * looking, since one credential belongs to the whole installation — and zero is a number, not an
 * absence.
 * </p>
 * <p>
 * There is no coordinate here, and there is no shape of this record that has one.
 * </p>
 * <p>
 * Written out here rather than read from the generated contract types, which have not been read
 * from a running API since this route was added. It is the server's record field for field, and it
 * becomes the generated one the next time the contract is read.
 * </p>
 */
export interface LibraryAlbum {
  albumId: string;
  title: string | null;
  photographCount: number | null;
  from: string | null;
  to: string | null;
}

/** The albums one library keeps, and the one thing a chooser needs to explain itself. */
export interface LibraryAlbums {
  source: string;
  libraryName: string;
  items: LibraryAlbum[];
  /** True when the library keeps more albums than this installation offers, so this is a prefix. */
  truncated: boolean;
  readAt: string;
}

/**
 * The albums one neighbouring library keeps, for narrowing a listing to one.
 *
 * <p>
 * Asked once per library rather than once per view: a chooser's contents are a fact about the
 * library, so paging, choosing and clearing all read the one answer instead of asking a
 * neighbouring container again. Held a little longer than a page of photographs for the same
 * reason — albums change far more slowly than the pictures in them.
 * </p>
 * <p>
 * A library that did not answer fails rather than arriving empty, so the screen can tell "this
 * library keeps no albums" from "nobody asked it anything successfully". Those two draw the same
 * chooser and send a reader to entirely different places.
 * </p>
 */
export function usePhotoLibraryAlbums(source: LibraryPhotoSource | undefined) {
  return useQuery({
    queryKey: queryKeys.libraryAlbums(source ?? ''),
    queryFn: () =>
      readJson<LibraryAlbums>(
        `/api/v1/photo-libraries/${encodeURIComponent(source!)}/albums`,
      ),
    enabled: source !== undefined,
    staleTime: 300_000,
    // No picture addresses on this answer, so unlike the listing there is nothing here that a
    // refetch would make the browser fetch again. It is still not asked for on coming back to the
    // tab, because a chooser somebody has not touched has not become wrong.
    refetchOnWindowFocus: false,
    // Not retried, for the reason the listing is not: a library that did not answer has already
    // been asked as often as this application is willing, and the seconds spent on three more
    // rounds are seconds before the sentence written for exactly this case appears.
    retry: false,
  });
}

/**
 * One page of what a neighbouring library made of a set of words.
 *
 * <p>
 * <b>There is no total here, and there is not meant to be one.</b> Neither of the products behind
 * this can say how many photographs match a sentence: one ranks everything it holds by how close
 * each picture is to what the words describe, so there is no set of matches to count, and the other
 * counts only the page it has just sent. A number in this position would be invented, and nothing
 * on a screen distinguishes an invented number from a counted one. What can honestly be shown is
 * how many came back and whether the library says there are more.
 * </p>
 * <p>
 * Written out here rather than read from the generated contract types. It is the server's record
 * field for field, and it becomes the generated one the next time the contract is read from a
 * running API.
 * </p>
 */
export type LibraryPhotographSearchPage = components['schemas']['LibraryPhotographSearchPageDto'];

/** The address of one page of one search. */
function searchUrl(source: LibraryPhotoSource, query: LibrarySearchQuery): string {
  const search = new URLSearchParams({
    q: query.q,
    page: String(query.page),
    pageSize: String(query.pageSize),
  });
  return `/api/v1/photo-libraries/${encodeURIComponent(source)}/search?${search.toString()}`;
}

/**
 * What one neighbouring library makes of a set of words.
 *
 * A separate hook from the listing because it is a separate question with a differently shaped
 * answer, and keeping them apart is what lets a screen say which of the two it is showing. The
 * words go to the far side untouched by anything here: what a sentence means is the library's
 * decision, and a guess at its grammar made in a browser would be a second, wrong copy of it.
 */
export function usePhotoLibrarySearch(
  source: LibraryPhotoSource | undefined,
  query: LibrarySearchQuery,
) {
  return useQuery({
    queryKey: queryKeys.librarySearch(source ?? '', query),
    queryFn: () => readJson<LibraryPhotographSearchPage>(searchUrl(source!, query)),
    enabled: source !== undefined && query.q.length > 0,
    // The previous answer is kept on screen only while a page of the same search is being turned.
    // Not across a change of words, and not across a change of library: an answer to one question
    // drawn under another question's words is the one thing this screen must never show, and it is
    // exactly what a grid that does not empty between searches would show.
    placeholderData: (previous?: LibraryPhotographSearchPage, previousQuery?: { queryKey: readonly unknown[] }) => {
      const asked = previousQuery?.queryKey[3] as LibrarySearchQuery | undefined;
      return previous?.source === source && asked?.q === query.q ? previous : undefined;
    },
    staleTime: 30_000,
    // The same two as the listing, for the same two reasons: every answer carries a freshly minted,
    // short-lived credential in each picture's address, so asking again is a page of derivatives
    // fetched afresh out of a neighbouring container for a screen nobody has touched — and a
    // library that did not answer has already been asked as often as this application is willing.
    refetchOnWindowFocus: false,
    retry: false,
  });
}

/**
 * Everything one library will say about one photograph.
 *
 * Asked by the photograph's own identifier, which is not the string its picture is fetched with:
 * on one of the two products that one is a hash of the picture's contents and names no photograph
 * at all.
 */
export function usePhotoLibraryPhotograph(
  source: LibraryPhotoSource | undefined,
  photographId: string | null,
) {
  return useQuery({
    queryKey: queryKeys.libraryPhotograph(source ?? '', photographId ?? ''),
    queryFn: () =>
      readJson<LibraryPhotographDetail>(
        `/api/v1/photo-libraries/${encodeURIComponent(source!)}/photographs/${encodeURIComponent(photographId!)}`,
      ),
    enabled: source !== undefined && photographId !== null,
    staleTime: 30_000,
    // The same two as the listing, for the same two reasons: the picture address in this answer
    // carries a credential that is new every time, and a library that did not answer has already
    // been asked as often as this application is willing to ask.
    refetchOnWindowFocus: false,
    retry: false,
  });
}
