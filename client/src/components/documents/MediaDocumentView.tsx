// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Empty, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FileInfo } from '../../api/hooks.ts';
import DownloadDocument from './DownloadDocument.tsx';

/**
 * A recording played where the browser can play it, and named honestly where it cannot.
 *
 * Nothing is converted. A recording is served as it was stored, and the browser either knows
 * the codec or does not — so an interview in a format this machine understands plays, and one
 * in a format it does not says so and offers the file. Converting would mean shipping a
 * transcoder, which is a large piece of software with real licensing care attached, to solve a
 * problem most uploads do not have.
 *
 * The bytes come from the ordinary delivery route, which answers partial requests, so the
 * player seeks and buffers the way it would over any other file rather than pulling a whole
 * cave-passage video down before the first frame.
 *
 * The delivery URL is renewed periodically because the one that came with the file lapses. A
 * player that followed every renewal would restart whatever was playing, so the address is
 * frozen the moment playback begins and tracks the renewals until then. A recording long
 * enough to outlive a frozen address ends where any other failure ends: a sentence saying so
 * and the download, rather than silence.
 */
export default function MediaDocumentView({ file }: { file: FileInfo }) {
  const { t } = useTranslation();
  const [pinned, setPinned] = useState<string | null>(null);
  const src = pinned ?? file.contentUrl;
  // Remembered as the address that failed rather than as a flag, so a recording that would not
  // start under a lapsed link is tried again under the renewed one. Once playback has begun the
  // address is frozen, so a failure there stays put — which is the honest outcome, since silently
  // starting the recording over from the beginning is not recovering it.
  const [undeliverable, setUndeliverable] = useState<string | null>(null);

  if (undeliverable === src) {
    return (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description={
          <Flex vertical gap={8} align="center">
            <Typography.Text type="secondary">{t('documents.viewer.mediaFailed')}</Typography.Text>
            <DownloadDocument file={file} />
          </Flex>
        }
      />
    );
  }

  const shared = {
    src,
    controls: true,
    preload: 'metadata' as const,
    'aria-label': file.originalName,
    onPlay: () => setPinned(src),
    onError: () => setUndeliverable(src),
  };

  return (
    <Flex vertical gap={8} style={{ width: '100%', minWidth: 0 }}>
      {file.kind === 'video' ? (
        <video
          {...shared}
          // Playing where it sits rather than taking over the screen, which is what a phone
          // does by default and is the wrong thing for a clip beside the notes about it.
          playsInline
          style={{ width: '100%', maxWidth: '100%', maxHeight: '70vh' }}
        />
      ) : (
        <audio {...shared} style={{ width: '100%' }} />
      )}
    </Flex>
  );
}
