// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { LibraryAlbums } from '../../api/hooks.ts';
import {
  albumChooserMessage,
  albumChooserState,
} from '../../pages/photolibrary/browseState.ts';

export interface LibraryAlbumChooserProps {
  /** What the library answered about its albums, or undefined while that is still unknown. */
  albums: LibraryAlbums | undefined;
  isPending: boolean;
  error: unknown;
  /** The album currently narrowing the listing, or null for the whole library. */
  albumId: string | null;
  onChoose: (albumId: string | undefined) => void;
  /**
   * Whether words are on screen. A search is put to the library whole — neither product will take
   * a set of albums to search within — so while one is showing this narrows nothing, and the
   * control says so rather than sitting there looking as though it applies.
   */
  searching: boolean;
}

/**
 * Choosing one of a neighbouring library's own albums, so that a listing of it can be narrowed to
 * an expedition rather than scrolled.
 *
 * <p>
 * A club files by expedition and a foreign album is usually one, which is what makes this the
 * narrowing worth having after time: it is the difference between looking through forty thousand
 * pictures and looking through one trip's four hundred.
 * </p>
 * <p>
 * <b>The four states this control can be in are four different facts, and it says which.</b> A
 * chooser still filling, a library that keeps no albums, a library that did not answer about them,
 * and a list to pick from. The middle two draw the same empty control and send a reader to
 * completely different places — one to make an album, the other to a container that is down — so
 * the sentence is chosen where the choosing can be checked rather than in the middle of this
 * markup.
 * </p>
 * <p>
 * Nothing here narrows anything itself. The album chosen goes to the server, the server puts it to
 * the library in the library's own grammar, and what comes back is whatever the library decided
 * the album is — which is why a count under a narrowed listing counts the album and says so.
 * </p>
 */
export default function LibraryAlbumChooser({
  albums,
  isPending,
  error,
  albumId,
  onChoose,
  searching,
}: LibraryAlbumChooserProps) {
  const { t } = useTranslation();

  const state = albumChooserState({ isPending, error, albums });
  const message = albumChooserMessage(state);

  // Named where the library named it, and "untitled" only where the library said nothing — which is
  // the library declining to name it rather than somebody having called it that.
  const options = (albums?.items ?? []).map((album) => ({
    value: album.albumId,
    label:
      album.photographCount === null
        ? (album.title ?? t('libraryPhotos.albums.untitled'))
        : t('libraryPhotos.albums.withCount', {
            title: album.title ?? t('libraryPhotos.albums.untitled'),
            count: album.photographCount,
          }),
  }));

  return (
    <Flex vertical gap={2}>
      <Select
        allowClear
        showSearch
        optionFilterProp="label"
        // Disabled while there is nothing to choose from, and while a search is on screen: a
        // control that can only fail is worse than no control, and one that silently does nothing
        // is worse than either.
        disabled={state !== 'albums' || searching}
        value={albumId ?? undefined}
        onChange={(value?: string) => onChoose(value || undefined)}
        options={options}
        placeholder={t(message ?? 'libraryPhotos.albums.choose')}
        style={{ minWidth: 260 }}
        data-testid="library-photo-album"
      />

      {/* Said only when an album is actually chosen, because that is when it changes what is on
          screen: the words went to the library whole and the album narrowed nothing. Saying it
          over an empty chooser would be noise. */}
      {searching && albumId !== null && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('libraryPhotos.albums.notWithASearch')}
        </Typography.Text>
      )}

      {/* A short list that does not say it is short is a wrong answer: a reader whose album is
          missing concludes the library has lost it. */}
      {albums?.truncated && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('libraryPhotos.albums.truncated', { count: albums.items.length })}
        </Typography.Text>
      )}
    </Flex>
  );
}
