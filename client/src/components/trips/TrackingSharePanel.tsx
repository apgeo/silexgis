// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CopyOutlined, GlobalOutlined } from '@ant-design/icons';
import { Alert, App, Button, Card, Flex, Input, Popconfirm, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useMintTripTrackingShare,
  useRevokeTripTrackingShare,
  useTripTrackingShares,
} from '../../api/hooks.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { buildEmbedSnippet, publicTripPath } from '../../pages/public/publicTripEmbed.ts';
import { trackingProblemMessage } from './trackingProblems.ts';

interface Props {
  tripLogId: string;
  /** The trip's own name, which is what the frame is captioned with on somebody else's website. */
  tripTitle: string;
  canEdit: boolean;
}

/**
 * Publishing the trip: the link somebody hands out, and the block a club pastes into its website.
 *
 * <b>The address and the snippet are offered in the same breath as the mint, and that is forced.</b>
 * The token exists in exactly one response — the server keeps a hash of it and nothing else, so it
 * cannot be shown again and the list below is deliberately token-free. Anywhere else in this
 * application, "copy the embed code" would be a button on a row; here a row holds no token, so a
 * button on one could only mint a second link. So both are shown at the moment there is something
 * to show, with the warning that this is the only moment, and both are still on screen while
 * whoever pressed the button pastes them somewhere.
 *
 * <b>Revoking is the way a publication ends</b>, and it is always available: a link is a capability
 * and taking it back is the only thing that ends one, since the read has no caller to re-check.
 * The other half — a cave that gains protection after the fact — is the server's, decided again on
 * every read and needing nobody to remember anything.
 */
export default function TrackingSharePanel({ tripLogId, tripTitle, canEdit }: Props) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  // Every control here is pressed, and how big a thing has to be to be pressed depends on what is
  // pressing it — not on how much room there is across.
  const coarse = useCoarsePointer();
  const shares = useTripTrackingShares(tripLogId, canEdit);
  const mint = useMintTripTrackingShare();
  const revoke = useRevokeTripTrackingShare();
  /**
   * The link this browser just minted: its token, and which share it belongs to.
   *
   * The token is held for this page's life only, because nothing can produce it again. The id is
   * held beside it because the panel invites a rotation — mint a fresh link, then take the old one
   * back — and taking *a* link back must not clear the one on screen unless it is that one. There
   * is no recovering from getting that wrong: the server stores a hash, so a token cleared before
   * it was pasted anywhere is gone, and the administrator has to mint a third.
   */
  const [minted, setMinted] = useState<{ id: string; token: string } | null>(null);

  if (!canEdit) {
    return null;
  }

  const controlSize: 'large' | 'small' = coarse ? 'large' : 'small';
  const confirmSizes = {
    okButtonProps: { size: controlSize },
    cancelButtonProps: { size: controlSize },
  };

  const when = (value: string) => new Date(value).toLocaleString(i18n.language);
  const followUrl =
    minted === null ? '' : `${window.location.origin}${publicTripPath(minted.token)}`;
  const snippet =
    minted === null
      ? ''
      : buildEmbedSnippet({
          origin: window.location.origin,
          token: minted.token,
          title: tripTitle,
        });

  /**
   * Puts text on the clipboard and says whether it went.
   *
   * The modern clipboard call is refused outside a secure context and by a browser that has not
   * been asked — an installation reached over plain HTTP on a club's local network is exactly that
   * case — and a copy button that silently does nothing is worse than one that says it could not.
   * The text stays selected in the field either way, so there is always a way to take it by hand.
   */
  const copy = async (text: string, notice: string) => {
    try {
      await navigator.clipboard.writeText(text);
      message.success(notice);
    } catch {
      message.warning(t('trips.tracking.publish.copyFailed'));
    }
  };

  const onMint = async () => {
    try {
      const created = await mint.mutateAsync({ tripLogId });
      setMinted({ id: created.id, token: created.token });
    } catch (error) {
      message.error(trackingProblemMessage(error, t));
    }
  };

  const onRevoke = async (shareId: string) => {
    try {
      await revoke.mutateAsync({ tripLogId, shareId });
      // Cleared only when the link taken back is the one on screen: that address now answers
      // nothing and must not be left to be pasted somewhere. Any other link being revoked says
      // nothing about this one — and clearing it regardless would destroy the only copy there
      // will ever be of a token minted a moment ago, which is exactly what rotating a link does.
      setMinted((current) => (current !== null && current.id === shareId ? null : current));
      message.success(t('trips.tracking.publish.revoked'));
    } catch (error) {
      message.error(trackingProblemMessage(error, t));
    }
  };

  const live = (shares.data ?? []).filter((share) => share.revokedAt === null);

  return (
    <Card
      size="small"
      title={t('trips.tracking.publish.title')}
      style={{ marginBottom: 16 }}
      data-testid="trip-tracking-publish"
      extra={
        <Button
          type="primary"
          size={controlSize}
          icon={<GlobalOutlined />}
          loading={mint.isPending}
          onClick={() => void onMint()}
          data-testid="trip-tracking-publish-mint"
        >
          {t('trips.tracking.publish.mint')}
        </Button>
      }
    >
      <Typography.Paragraph type="secondary" style={{ marginTop: 0 }}>
        {t('trips.tracking.publish.explain')}
      </Typography.Paragraph>

      {minted !== null && (
        <Alert
          type="success"
          showIcon
          title={t('trips.tracking.publish.mintedTitle')}
          description={
            <Flex vertical gap={8} style={{ marginTop: 4 }}>
              <Typography.Text strong>{t('trips.tracking.publish.tokenOnce')}</Typography.Text>

              <div>
                <Typography.Text type="secondary">
                  {t('trips.tracking.publish.linkLabel')}
                </Typography.Text>
                {/* Compact rather than an addon, so at 360px the address and its button stack
                    instead of squeezing the address into a strip four characters wide. */}
                <Flex gap={8} wrap style={{ marginTop: 4 }}>
                  <Input
                    readOnly
                    value={followUrl}
                    size={controlSize}
                    onFocus={(event) => event.target.select()}
                    style={{ flex: '1 1 220px', minWidth: 0 }}
                    data-testid="trip-tracking-publish-link"
                  />
                  <Button
                    size={controlSize}
                    icon={<CopyOutlined />}
                    onClick={() => void copy(followUrl, t('trips.tracking.publish.linkCopied'))}
                    data-testid="trip-tracking-publish-copy-link"
                  >
                    {t('trips.tracking.publish.copy')}
                  </Button>
                </Flex>
              </div>

              <div>
                <Typography.Text type="secondary">
                  {t('trips.tracking.publish.embedLabel')}
                </Typography.Text>
                <Typography.Paragraph type="secondary" style={{ margin: '2px 0 4px' }}>
                  {t('trips.tracking.publish.embedHelp')}
                </Typography.Paragraph>
                <Input.TextArea
                  readOnly
                  value={snippet}
                  rows={4}
                  onFocus={(event) => event.target.select()}
                  style={{ fontFamily: 'monospace', fontSize: 12 }}
                  data-testid="trip-tracking-publish-snippet"
                />
                <Button
                  size={controlSize}
                  icon={<CopyOutlined />}
                  style={{ marginTop: 8 }}
                  onClick={() => void copy(snippet, t('trips.tracking.publish.embedCopied'))}
                  data-testid="trip-tracking-publish-copy-embed"
                >
                  {t('trips.tracking.publish.copyEmbed')}
                </Button>
              </div>
            </Flex>
          }
          data-testid="trip-tracking-publish-minted"
        />
      )}

      <div style={{ marginTop: 12 }} data-testid="trip-tracking-publish-list">
        {shares.error != null ? (
          <Alert type="error" showIcon title={t('trips.tracking.publish.listUnavailable')} />
        ) : live.length === 0 ? (
          <Typography.Text type="secondary">{t('trips.tracking.publish.none')}</Typography.Text>
        ) : (
          <Flex vertical gap={8}>
            {live.map((share) => (
              <Flex
                key={share.id}
                gap={8}
                wrap
                align="center"
                justify="space-between"
                data-testid={`trip-tracking-publish-share-${share.id}`}
              >
                <Flex gap={8} align="center" wrap style={{ minWidth: 0 }}>
                  <Tag color="blue">{t('trips.tracking.publish.statusActive')}</Tag>
                  <Typography.Text type="secondary">
                    {t('trips.tracking.publish.createdAt', { when: when(share.createdAt) })}
                  </Typography.Text>
                </Flex>
                <Popconfirm
                  title={t('trips.tracking.publish.revokeConfirm')}
                  onConfirm={() => void onRevoke(share.id)}
                  {...confirmSizes}
                >
                  <Button
                    size={controlSize}
                    danger
                    loading={revoke.isPending}
                    data-testid={`trip-tracking-publish-revoke-${share.id}`}
                  >
                    {t('trips.tracking.publish.revoke')}
                  </Button>
                </Popconfirm>
              </Flex>
            ))}
          </Flex>
        )}
      </div>
    </Card>
  );
}
