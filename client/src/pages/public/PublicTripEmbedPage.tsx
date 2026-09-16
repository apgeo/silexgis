// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import { Flex, Spin, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { usePublicTrip } from '../../api/hooks.ts';
import CaveViewPanel, {
  type CaveViewFocusRequest,
} from '../../components/caveview/CaveViewPanel.tsx';
import { envelopeCrsLookup, publicTrackedCavers } from '../../caveview/publicTrackedCavers.ts';
import { usePublishedStationMedia } from '../../caveview/useStationMedia.ts';
import { unnamedViewerFileName } from '../../caveview/viewerFileName.ts';
import { usePinnedModelUrl } from './pinnedModelUrl.ts';
import {
  EMBED_CHANNEL,
  EMBED_PROTOCOL,
  parseEmbedInbound,
  type EmbedOutboundMessage,
  type EmbedReadyMessage,
} from './publicTripEmbed.ts';
import './PublicTripPage.css';

/**
 * The same published trip as a viewer and nothing else, for an iframe on somebody's website.
 *
 * <b>No chrome at all, and that is the difference from the page next door.</b> A title, a party
 * list and a footer belong to the article this sits inside — a club writes those itself, in its own
 * words and its own layout — so what is offered here is the drawing, the party on it, and the
 * viewer's own controls. It fills whatever box the snippet gave it.
 *
 * <b>The iframe is the boundary and nothing widens it.</b> The host page cannot read anything of
 * this document; what it can do is post a message asking for a place to be shown, and this answers
 * only its own framer and only ever to that framer's origin. Which sites may frame this at all is
 * an operator's setting enforced as `frame-ancestors` by the web server — so the allow-list is
 * where an operator can see and change it, not compiled into a bundle.
 *
 * <b>A caver is a place too.</b> Prose that says "caver 3 is at the sump" can link the number, and
 * this resolves it to wherever that place in the party was last reported — which is the only way a
 * link in an article can stay correct while the party moves.
 */
export default function PublicTripEmbedPage() {
  const { t } = useTranslation();
  const { token } = useParams<{ token: string }>();
  const { token: antdToken } = theme.useToken();
  // The refusal is not read separately here: a first read that failed leaves nothing to frame and
  // is the same empty box as a trip with no drawing, and a later one that failed leaves what is
  // already on screen alone.
  const { data, isPending } = usePublicTrip(token);
  const [focusRequest, setFocusRequest] = useState<CaveViewFocusRequest | undefined>();

  // Held still while it is the same survey and replaced when it is not, by the one rule the
  // followed page uses — a re-signed address must not re-parse the model and throw the camera
  // back to its opening view every minute, and a survey swapped mid-trip must not leave this
  // drawing the old geometry under the new survey's station names.
  const model = data?.model ?? null;
  const pinnedModelUrl = usePinnedModelUrl(model?.modelUrl, token);

  const cavers = useMemo(
    () =>
      data === undefined
        ? []
        : publicTrackedCavers(data, (ordinal) => t('publicTrip.caverOrdinal', { ordinal })),
    [data, t],
  );

  const crsLookup = useMemo(() => envelopeCrsLookup(model), [model]);

  // Kept fresh across re-reads rather than pinned like the model URL, and kept as one object while
  // it is the same photographs — both for the reason the page next door gives at length. The case
  // is sharper here: an embed sits inside an article about a trip that finished months ago, opened
  // by a reader who scrolls to the drawing when they get to it, so its picture URLs are the ones
  // most likely to be spent long after they were minted.
  const stationMedia = usePublishedStationMedia(model?.pictures);

  /**
   * The party as the framing document is told it: a place, a name, a station or nothing, and which
   * kind of nothing it is.
   *
   * The second half is not a nicety. A station is absent for two unrelated reasons — nobody has
   * reported a place, or a place was reported on a different survey than the one in this frame and
   * cannot honestly be drawn on it — and the article around this viewer writes its prose against
   * what it is told. Told only "no station", it says nobody knows where somebody underground is.
   */
  const party = useMemo(
    () =>
      cavers.map((caver) => ({
        ordinal: Number(caver.caverId),
        name: caver.name,
        station: caver.position.kind === 'station' ? caver.position.station : null,
        onOtherSurvey: caver.position.kind === 'otherModel',
      })),
    [cavers],
  );
  const loaded = data !== undefined;

  /**
   * The framer's origin, once it has said hello, and what it was last told.
   *
   * Both are held rather than derived because the conversation outlives any one render: a greeting
   * arrives while the envelope is still in flight, so the useful answer is the one sent *after*
   * it lands, and there is no second greeting to hang that on — the host script says hello once
   * per frame and then listens.
   */
  const framerOrigin = useRef<string | null>(null);
  const announced = useRef<string | null>(null);

  const announce = useCallback(
    (origin: string) => {
      announced.current = JSON.stringify({ loaded, party });
      // Never `'*'`: the party, and which stations it is standing at, goes to the document that
      // framed this page and to no other listener that happens to be in the chain.
      window.parent.postMessage(
        {
          silexgis: EMBED_CHANNEL,
          v: EMBED_PROTOCOL,
          type: 'ready',
          loaded,
          party,
        } satisfies EmbedReadyMessage,
        origin,
      );
    },
    [loaded, party],
  );

  /**
   * Says the party again whenever it stops being what the framer was told.
   *
   * The whole reason the announcement is not a one-off. The envelope lands after the frame has
   * loaded and been greeted, and afterwards each minute's poll can move somebody or bring them
   * out; a host page that greys out or labels its links from this message has to hear about that,
   * and it has no way to ask. An unchanged party is not re-announced, so a poll that found nothing
   * new costs the page nothing.
   */
  useEffect(() => {
    const origin = framerOrigin.current;
    if (origin !== null && announced.current !== JSON.stringify({ loaded, party })) {
      announce(origin);
    }
  }, [announce, loaded, party]);

  /** Where a place named by the host page is in this model, or null when it is nowhere. */
  const stationOfCaver = useCallback(
    (ref: string) => {
      const ordinal = Number.parseInt(ref, 10);
      const participant = data?.participants.find((person) => person.ordinal === ordinal);
      return participant?.stationName ?? null;
    },
    [data],
  );

  useEffect(() => {
    // Nothing to talk to. A viewer opened directly in a tab is a legitimate thing to do — it is
    // how somebody checks a snippet before pasting it — and it simply has no conversation.
    if (window.parent === window) {
      return;
    }
    const parent = window.parent;

    const reply = (origin: string, message: EmbedOutboundMessage) => {
      // Never `'*'`: the party, and which stations it is standing at, goes to the document that
      // framed this page and to no other listener that happens to be in the chain.
      parent.postMessage(message, origin);
    };

    const onMessage = (event: MessageEvent) => {
      // The framer, and only the framer. A page nested deeper, an opener, a worker — none of them
      // are who this page is having a conversation with, and `event.origin` alone would not tell
      // them apart from the one that is.
      if (event.source !== parent) {
        return;
      }
      const inbound = parseEmbedInbound(event.data);
      if (inbound === null) {
        return;
      }
      if (inbound.type === 'hello') {
        // Remembered, because this is the only moment the framer's origin is ever stated and
        // every later announcement has to be addressed to it.
        framerOrigin.current = event.origin;
        announce(event.origin);
        return;
      }

      const { kind, ref } = inbound.target;
      const settled = (found: boolean) =>
        reply(event.origin, {
          silexgis: EMBED_CHANNEL,
          v: EMBED_PROTOCOL,
          type: 'focused',
          target: { kind, ref },
          found,
        });

      const station = kind === 'caver' ? stationOfCaver(ref) : ref;
      if (station === null) {
        // A place in the party that nobody has placed. Answered rather than ignored, so an
        // article can grey a link out instead of offering one that does nothing.
        settled(false);
        return;
      }
      // A fresh object every time, which is what asks the panel to move — pressing the same link
      // twice has to fly the camera back, and it would not if the request compared equal.
      setFocusRequest({
        kind: kind === 'survey' ? 'survey' : 'station',
        ref: station,
        onSettled: (found: boolean) => settled(found),
      });
    };

    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [announce, stationOfCaver]);

  const palette = {
    '--silexgis-public-bg': antdToken.colorBgContainer,
    '--silexgis-public-border': antdToken.colorBorderSecondary,
  } as CSSProperties;

  const failure = (message: string) => (
    <div className="public-trip-embed" style={palette} data-testid="public-trip-embed-failure">
      <div className="public-trip-embed-failure">
        <Typography.Text type="secondary">{message}</Typography.Text>
      </div>
    </div>
  );

  if (isPending) {
    return (
      <div className="public-trip-embed" style={palette}>
        <Flex align="center" justify="center" style={{ height: '100%' }}>
          <Spin size="large" />
        </Flex>
      </div>
    );
  }

  // Only when there is nothing to show. A poll that failed while an envelope is already in hand —
  // a phone that went through a tunnel — leaves the drawing and the party exactly where they were
  // rather than replacing somebody's website with an assertion that the link never existed.
  if (data === undefined) {
    return failure(t('publicTrip.notFoundTitle'));
  }

  if (model === null || pinnedModelUrl === null) {
    // The trip is published and there is no drawing to put in a frame. Said in words rather than
    // left as an empty box, because an empty box on somebody's website reads as a broken embed.
    return failure(t('publicTrip.embedNoModel'));
  }

  return (
    <div className="public-trip-embed" style={palette} data-testid="public-trip-embed">
      <CaveViewPanel
        fileUrl={pinnedModelUrl}
        fileName={unnamedViewerFileName(model.format)}
        // The one mount where the whole height is right: this document IS the frame, its size was
        // chosen by whoever pasted the snippet, and the page that scrolls is theirs.
        height="100%"
        trackedCavers={cavers}
        crsLookup={crsLookup}
        focusRequest={focusRequest}
        toolbar
        // From the envelope, exactly as on the page next door and through the same derivation. The
        // links a station's pictures are otherwise read from answer only to an account, and this
        // document is served to a stranger on somebody else's website — so what is drawn here is
        // what the server decided may be published, and this file decides nothing further.
        stationMedia={stationMedia}
      />
    </div>
  );
}
