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
  /**
   * Whether this installation publishes the party's real names — the server's answer, passed down
   * rather than asked for again, because the page around this panel has already read it.
   */
  publishesRealNames: boolean;
  /**
   * Whether a link opens the page at this moment — the server's own answer, from the trip's read.
   *
   * <b>Not the same question as "is there an unrevoked row", which is all this panel can see.</b>
   * A publication also ends when the watch closes (the ordinary way nearly every real one ends),
   * when the grace after that closing runs out, and when the trip's cave stops being publishable.
   * None of those is a fact about a row, all of them are facts about the trip, and one of them is
   * measured by an installation setting this panel is never sent. So the answer is read once,
   * where the rule lives, and passed in.
   */
  published: boolean;
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
 * <b>A publication ends on its own, and revoking is how it ends early.</b> Taking a link back is
 * always available and is immediate; what it cannot do is reach the copy of the token already
 * sitting in a club's article, which is indexed and archived and outlives anybody remembering it is
 * there. So the server ends a publication without being asked — when the watch closes, and in any
 * case when the link's own window runs out — and both of those are said here, beside the address,
 * because whoever pastes it into a website is the one person who needs to know the page will stop.
 * The remaining half — a cave that gains protection after the fact — is the server's too, decided
 * again on every read and needing nobody to remember anything. Which is why the list below says,
 * per link, whether it is opening anything <em>now</em> rather than only when it was made and when
 * it runs out: a publication that ends without being ended leaves its rows exactly where they were.
 *
 * <b>Whether the page will name people is said here, in words, before the button is pressed and
 * again when the address appears.</b> The people named are not the person pressing the button —
 * they are the rest of the club — so a link that puts their names in front of the internet must
 * not be minted by somebody who was never told it would. Which of the two sentences is true is an
 * installation's setting rather than a property of this trip, so it is read from the server and
 * never guessed here.
 */
export default function TrackingSharePanel({
  tripLogId,
  tripTitle,
  canEdit,
  publishesRealNames,
  published,
}: Props) {
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
  const [minted, setMinted] = useState<{
    id: string;
    token: string;
    /** When this address stops working, which is part of what was just handed out. */
    expiresAt: string;
  } | null>(null);

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
      setMinted({ id: created.id, token: created.token, expiresAt: created.expiresAt });
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

  // A link that was taken back, or one that has run out, is no longer a link at all and is not
  // listed. Lapsing is decided from the same instant for every row, so the list cannot disagree
  // with itself halfway down.
  const asOf = Date.now();
  const lapsed = (share: { expiresAt: string }) => Date.parse(share.expiresAt) <= asOf;
  const standing = (shares.data ?? []).filter(
    (share) => share.revokedAt === null && !lapsed(share),
  );

  // <b>Whether a standing link actually opens the page is the server's answer, and this is the one
  // account an administrator has of what is published.</b> A row that read "Live" after the watch
  // closed — which is how nearly every real publication ends, not an edge case — would be this
  // surface telling somebody the trip is published, with a date two weeks out, while every follower
  // gets a 404. It cannot be worked out from the row: the watch's state, the grace after it closes
  // and the cave's own refusal are all facts about the trip, and `published` is the read that
  // already asks them through the rule itself.
  //
  // One boolean is enough and is exact, because the trip-wide half is shared by every row: while
  // some link opens the page, "unrevoked and inside its window" is the whole of what remains to ask
  // of a row; and when none does, none of these opens one either.
  //
  // A dormant row is still drawn, with its button. A closed watch can be armed again and a
  // protection can be lifted, so such a link is not dead — it is exactly the one an administrator
  // may want to take back before it starts answering again, and a hidden row cannot be revoked.
  //
  // Two whole calls rather than one over a chosen key: the check that every key the code asks for
  // exists reads them out of the source text, and a key assembled at the call is one it cannot see.
  const status = published
    ? t('trips.tracking.publish.statusActive')
    : t('trips.tracking.publish.statusDormant');

  // Anything but an explicit "no" is worded as the disclosing case. A server that did not answer
  // the question — an older build, a read that has not landed — leaves an administrator warned
  // about names that may not appear, which costs them a moment; the other way round costs somebody
  // else their name on a public page.
  const namesShown = publishesRealNames !== false;

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

      {/* That a link ends by itself, said before anybody presses the button rather than discovered
          when a page stops answering. It is also the answer to the objection this panel used to
          invite — "what happens to the block I pasted into our website last spring" — and the
          honest answer is that it stops, which is the point. */}
      <Typography.Paragraph type="secondary" data-testid="trip-tracking-publish-ends">
        {t('trips.tracking.publish.ends')}
      </Typography.Paragraph>

      {/* Not `type="secondary"` like the paragraph above it: this one is the disclosure, and it is
          the sentence somebody has to have read before they hand the address to a club's website. */}
      <Typography.Paragraph data-testid="trip-tracking-publish-names">
        {/* Two whole calls rather than one call over a chosen key: the check that every key the
            code asks for exists reads them out of the source text, and a key assembled at the
            call is a key that check cannot see. */}
        {namesShown
          ? t('trips.tracking.publish.namesShown')
          : t('trips.tracking.publish.namesHidden')}{' '}
        {/* Beside the disclosure rather than anywhere else, because it is the other half of it: the
            people this names are not the person reading, and they are now told at the moment it
            happens. Whoever is about to publish should know that before they do it, not be
            surprised by a colleague's reply. */}
        <span data-testid="trip-tracking-publish-tells-party">
          {t('trips.tracking.publish.tellsParty')}
        </span>
      </Typography.Paragraph>

      {minted !== null && (
        <Alert
          type="success"
          showIcon
          title={t('trips.tracking.publish.mintedTitle')}
          description={
            <Flex vertical gap={8} style={{ marginTop: 4 }}>
              <Typography.Text strong>{t('trips.tracking.publish.tokenOnce')}</Typography.Text>
              {/* Said again beside the address itself. The paragraph above is read before anybody
                  decides; this is what is on screen at the moment there is something to paste. */}
              <Typography.Text data-testid="trip-tracking-publish-minted-names">
                {namesShown
                  ? t('trips.tracking.publish.mintedNamesShown')
                  : t('trips.tracking.publish.mintedNamesHidden')}
              </Typography.Text>
              {/* Said at the moment there is an address to paste, because the date is part of what
                  is being handed over: somebody putting this into an article has to be able to
                  write "this link works until …" beside it, and this response is the only place
                  that answer exists. Worded as an outer bound rather than a promise — closing the
                  watch ends the page sooner, which is the ordinary way a publication ends. */}
              <Typography.Text data-testid="trip-tracking-publish-minted-expires">
                {t('trips.tracking.publish.mintedExpires', { when: when(minted.expiresAt) })}
              </Typography.Text>

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
        ) : standing.length === 0 ? (
          <Typography.Text type="secondary">{t('trips.tracking.publish.none')}</Typography.Text>
        ) : (
          <Flex vertical gap={8}>
            {standing.map((share) => (
              <Flex
                key={share.id}
                gap={8}
                wrap
                align="center"
                justify="space-between"
                data-testid={`trip-tracking-publish-share-${share.id}`}
              >
                <Flex gap={8} align="center" wrap style={{ minWidth: 0 }}>
                  <Tag
                    color={published ? 'blue' : 'default'}
                    data-testid={`trip-tracking-publish-status-${share.id}`}
                  >
                    {status}
                  </Tag>
                  <Typography.Text type="secondary">
                    {t('trips.tracking.publish.createdAt', { when: when(share.createdAt) })}
                  </Typography.Text>
                  {/* When it ends, on the row rather than only on the mint: the address itself is
                      shown once and never again, so this list is the only place an administrator
                      can come back to and find out how long the trip stays published.

                      And when nothing is open, the same date said the other way round. "Works
                      until the 28th" is false the moment the watch closes, and it is false in the
                      direction that matters — somebody reading it believes a page is answering. The
                      date is still worth printing, because it is when this link stops being one
                      that could start working again. */}
                  <Typography.Text
                    type="secondary"
                    data-testid={`trip-tracking-publish-expires-${share.id}`}
                  >
                    {published
                      ? t('trips.tracking.publish.expiresAt', { when: when(share.expiresAt) })
                      : t('trips.tracking.publish.dormantUntil', { when: when(share.expiresAt) })}
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
