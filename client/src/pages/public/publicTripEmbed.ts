// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The conversation between a published trip's viewer and the page that frames it.
 *
 * <b>The iframe is the boundary, and it stays the boundary.</b> A club's website wants its own
 * prose to drive the cave viewer — "the sump at Sala Mare" as a link that flies the camera there —
 * and the ways to do that are a cross-origin script reaching into this application, or two
 * documents passing each other messages. The second is the one that keeps a boundary: nothing of
 * this application's origin is readable from the host page, no header opens anything to anybody,
 * and the whole surface is what is declared below.
 *
 * <b>Origins are checked in both directions, and neither direction trusts the message.</b> Going
 * in, the embedded page answers only its own framer (`event.source === window.parent`) and replies
 * to `event.origin` rather than to `'*'`. Coming out, the host script compares `event.origin`
 * against the origin it was told to talk to, and posts to that origin and no other. Who may frame
 * the page at all is not decided here — it is decided by `frame-ancestors` at the web server, so
 * the list of sites that may hold this conversation is an operator's setting and not a string in a
 * bundle anybody can read.
 *
 * Everything in one file because it is one protocol: the page that receives these messages and the
 * script that sends them must agree exactly, and the script is handed out as text for somebody to
 * paste into a content editor — so it is built from the same constants rather than written twice.
 */

/** Marks a message as belonging to this conversation and not to some other script on the page. */
export const EMBED_CHANNEL = 'silexgis-trip-embed';

/**
 * The protocol's version, carried on every message.
 *
 * It exists because the two halves are deployed years apart: the snippet is pasted into a website
 * once and sits there while the application it points at is upgraded underneath it. A message from
 * a version this page does not know is ignored rather than guessed at.
 */
export const EMBED_PROTOCOL = 1;

/** What a hyperlink in the surrounding prose can ask the viewer to show. */
export type EmbedFocusKind = 'station' | 'survey' | 'caver';

export interface EmbedFocusMessage {
  silexgis: typeof EMBED_CHANNEL;
  v: typeof EMBED_PROTOCOL;
  type: 'focus';
  target: { kind: EmbedFocusKind; ref: string };
}

/**
 * The host page saying hello, which is also how the embedded page learns who is framing it.
 *
 * It has to be said in this direction, and that is a property of the boundary rather than an
 * awkwardness of it: a page may only post a message to an origin it names, and the embedded page
 * does not know its framer's origin until the framer speaks. Guessing one from the referrer, or
 * posting to `'*'`, would both be this application volunteering the party to whoever happened to
 * be listening.
 */
export interface EmbedHelloMessage {
  silexgis: typeof EMBED_CHANNEL;
  v: typeof EMBED_PROTOCOL;
  type: 'hello';
}

/**
 * The answer to a hello: who the party is, so the article's prose can be written against it.
 *
 * <b>Said again whenever it stops being true.</b> A greeting arrives while the envelope is still
 * in flight — the frame finishes loading long before a request made inside it comes back — so the
 * first of these usually carries nothing, and a host page that only ever heard that one would
 * believe the trip has no party. Every later change to what the viewer is showing, including each
 * minute's poll moving somebody, is announced the same way.
 */
export interface EmbedReadyMessage {
  silexgis: typeof EMBED_CHANNEL;
  v: typeof EMBED_PROTOCOL;
  type: 'ready';
  /**
   * Whether the party below is what is being shown, or merely what is known so far.
   *
   * The distinction a host page cannot otherwise make: an empty party and a party that has not
   * arrived yet are both `[]`, and a page that greys out links from this message would grey out
   * every one of them forever on the strength of the first announcement.
   */
  loaded: boolean;
  /**
   * The party exactly as the embedded page is showing it — the place in the party, the name it is
   * drawn under, the station it is drawn at when there is one, and, when there is not, which kind
   * of nothing that is.
   *
   * Nothing else, and in particular nothing the page itself was not given: this is the same
   * envelope a follower already holds, handed to the document framing it.
   *
   * <b>`station` alone is not enough, and there are three ways for it to be null.</b> Nobody has
   * reported where that person is. Or somebody has, and it was measured in a different survey of
   * the cave than the one in this frame — which happens whenever a watch is re-pointed at a
   * corrected survey while the party is underground — and that is `onOtherSurvey`. Or somebody has,
   * naming a station of this very survey, and the drawing in the frame turns out to hold no node of
   * that name: a survey re-exported with its stations renamed does that to every place reported
   * before it, under the same model id, with nobody having touched the watch. That is
   * `notOnDrawing`, it is known only once this browser has parsed the file, and no server could
   * have said it.
   *
   * Handed over as one shape, an article that greys out a link on `station === null` would tell its
   * readers that nobody knows where a person underground is, at the moment when somebody does. So
   * an article is expected to read the two flags, and each of them wants different words — "on a
   * different survey of the cave" and "at a station this drawing does not contain".
   *
   * <b>The station of such a place is deliberately not handed over</b>, in both cases and for one
   * reason: a name that is not a point of this drawing has no honest use in prose printed beside
   * it. It would be interpolated into "X is at Y" and linked, and the link would fly nowhere. The
   * name is not lost — the frame's own list of people says it, marked, which is where a reader can
   * see the drawing that cannot show it in the same glance.
   */
  party: {
    ordinal: number;
    name: string;
    station: string | null;
    onOtherSurvey: boolean;
    notOnDrawing: boolean;
  }[];
}

/** The answer to one focus: whether the model turned out to hold what the prose named. */
export interface EmbedFocusedMessage {
  silexgis: typeof EMBED_CHANNEL;
  v: typeof EMBED_PROTOCOL;
  type: 'focused';
  target: { kind: EmbedFocusKind; ref: string };
  found: boolean;
}

export type EmbedOutboundMessage = EmbedReadyMessage | EmbedFocusedMessage;

/** What the embedded page will act on: a greeting, or a place to show. */
export type EmbedInbound = { type: 'hello' } | { type: 'focus'; target: EmbedFocusMessage['target'] };

/**
 * Reads something actionable out of whatever arrived on a `message` event, or null.
 *
 * Everything is checked, including the things that "cannot" be wrong: the data on a message event
 * is authored by another document and arrives as `unknown`, so a page that destructured it would
 * be a page any script anywhere could make throw by posting a number at it. The reference is
 * length-capped for the same reason — nothing downstream of here has any use for a megabyte of
 * station name.
 */
export function parseEmbedInbound(data: unknown): EmbedInbound | null {
  if (typeof data !== 'object' || data === null) {
    return null;
  }
  // Read as loosely as it actually arrives. Describing it as one of the declared message types
  // would be this file asserting something about a value another document wrote, which is exactly
  // what the checks below exist to avoid asserting.
  const message = data as { silexgis?: unknown; v?: unknown; type?: unknown; target?: unknown };
  if (message.silexgis !== EMBED_CHANNEL || message.v !== EMBED_PROTOCOL) {
    return null;
  }
  if (message.type === 'hello') {
    return { type: 'hello' };
  }
  if (message.type !== 'focus') {
    return null;
  }
  const target: unknown = message.target;
  if (typeof target !== 'object' || target === null) {
    return null;
  }
  const { kind, ref } = target as { kind?: unknown; ref?: unknown };
  if (kind !== 'station' && kind !== 'survey' && kind !== 'caver') {
    return null;
  }
  if (typeof ref !== 'string' || ref.length === 0 || ref.length > 400) {
    return null;
  }
  return { type: 'focus', target: { kind, ref } };
}

/**
 * The script a host page runs, as source text.
 *
 * Handed out inside the paste-in snippet rather than served from this application, and that is a
 * decision rather than an economy. A snippet that pulled a script off this installation would be a
 * second request the host page has to be allowed to make, a second thing that can be blocked by a
 * content-security policy somebody else wrote, and a second version to keep in step with the page
 * it talks to. Inline, what was pasted is what runs, and it keeps working while this application
 * is upgraded under it.
 *
 * Written in the old dialect on purpose — `var`, no arrow functions, no optional chaining. It is
 * pasted into websites this project does not control and is never transpiled by anything.
 */
const RELAY_SOURCE = `(function () {
  var CHANNEL = '${EMBED_CHANNEL}';
  var VERSION = ${EMBED_PROTOCOL};
  var KINDS = ['station', 'survey', 'caver'];
  /* Where the first copy of this script on the page leaves itself for the next copy to find. */
  var KEY = '__silexgisTripEmbedRelay';

  function frames() {
    return document.querySelectorAll('iframe[data-silexgis-embed]');
  }

  function send(frame, message) {
    if (frame.contentWindow) {
      frame.contentWindow.postMessage(message, frame.getAttribute('data-silexgis-embed'));
    }
  }

  function hello(frame) {
    send(frame, { silexgis: CHANNEL, v: VERSION, type: 'hello' });
  }

  /* Somebody has to speak first, and it has to be this side: a page can only post to an origin it
     names, and the viewer does not know who framed it until it is told. */
  function greet() {
    var all = frames();
    for (var i = 0; i < all.length; i++) {
      var frame = all[i];
      /* Each frame is greeted once. A second block pasted into the same article runs this again,
         and what it is running it for is the frame that arrived with it. */
      if (frame.getAttribute('data-silexgis-greeted')) continue;
      frame.setAttribute('data-silexgis-greeted', '1');
      hello(frame);
      /* Said again when the frame finishes loading, because a hello sent before the document
         inside it exists reaches nothing at all. */
      frame.addEventListener('load', function () {
        hello(this);
      });
    }
  }

  /* One conversation per page, however many blocks were pasted into it.

     An article about two trips carries two of these blocks, and each block brings its own copy of
     this script. A second copy that installed its own listeners would post every click twice and
     announce every frame twice — and since the listeners are on the document rather than on a
     frame, there is nothing about the second copy that makes its listeners belong to the second
     viewer. So the first copy to run owns the listeners for the whole document, and every later
     copy only asks it to greet the frames that arrived with it. */
  var running = window[KEY];
  if (running && running.v === VERSION) {
    running.greet();
    return;
  }
  window[KEY] = { v: VERSION, greet: greet };

  /* Which viewer a link drives: the one it names, else the only one on the page.

     With several viewers and no name there is no right answer, and the wrong one is not a
     harmless miss — it flies the camera to a station in a different cave's model and presents
     that as where the prose said. So the link is left to behave as an ordinary link, and the
     reason is said out loud where whoever pasted the block will find it. */
  function frameFor(link) {
    var named = link.getAttribute('data-silexgis-target');
    if (named) {
      var found = document.getElementById(named);
      var embedded = found && found.tagName === 'IFRAME' && found.getAttribute('data-silexgis-embed');
      return embedded ? found : null;
    }
    var all = frames();
    if (all.length === 1) return all[0];
    if (all.length > 1 && typeof console !== 'undefined' && console.warn) {
      console.warn(
        'SilexGIS: this page holds ' + all.length + ' trip viewers, so a link must name the one ' +
        'it drives with data-silexgis-target="<the iframe id>".'
      );
    }
    return null;
  }

  function kindOf(link) {
    for (var i = 0; i < KINDS.length; i++) {
      if (link.hasAttribute('data-silexgis-' + KINDS[i])) return KINDS[i];
    }
    return null;
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', greet);
  } else {
    greet();
  }

  document.addEventListener('click', function (event) {
    var from = event.target;
    if (!from || !from.closest) return;
    var link = from.closest('[data-silexgis-station],[data-silexgis-survey],[data-silexgis-caver]');
    if (!link) return;
    var kind = kindOf(link);
    var frame = frameFor(link);
    if (!kind || !frame || !frame.contentWindow) return;

    event.preventDefault();
    send(frame, {
      silexgis: CHANNEL,
      v: VERSION,
      type: 'focus',
      target: { kind: kind, ref: link.getAttribute('data-silexgis-' + kind) }
    });
    /* On a phone the prose and the viewer are rarely on screen together, so a link that moved a
       camera nobody can see would look like a link that did nothing. */
    if (frame.scrollIntoView) frame.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  });

  window.addEventListener('message', function (event) {
    var data = event.data;
    if (!data || data.silexgis !== CHANNEL || data.v !== VERSION) return;
    /* The origin is checked against the one this page was told to talk to, and the window against
       the frame that claims to have sent it. Either alone is not enough. */
    var mine = false;
    var all = frames();
    for (var i = 0; i < all.length; i++) {
      if (all[i].contentWindow === event.source && all[i].getAttribute('data-silexgis-embed') === event.origin) {
        mine = true;
      }
    }
    if (!mine) return;
    document.dispatchEvent(new CustomEvent('silexgis:' + data.type, { detail: data }));
  });
})();`;

export interface EmbedSnippetOptions {
  /** Where this installation answers — scheme and host, no trailing slash. */
  origin: string;
  /** The follow token. It is the whole of the capability, so the snippet carries it in the clear. */
  token: string;
  /** What the frame is called for a reader using a screen reader. */
  title: string;
  /**
   * The shape the box holds, as a CSS `aspect-ratio`.
   *
   * The box is what makes this responsive, and it has to be: an iframe given a height in pixels
   * keeps that height in a column 360px wide, which is the single commonest way an embed ends up
   * unusable on a phone. Here the width is whatever the article gives it and the height follows,
   * so the frame is correct at every width without the host page measuring anything.
   */
  aspectRatio?: string;
}

/** Everything of an embed that has to be identical in the page and in the snippet that frames it. */
export const EMBED_BOX = {
  aspectRatio: '4 / 3',
  /** Below this the viewer's own toolbar and the party list have nowhere to go. */
  minHeightPx: 260,
  /** A tall phone would otherwise hand the whole screen to a frame the reader cannot scroll past. */
  maxHeight: '80vh',
} as const;

/** The address of a published trip's page, for a follower. */
export function publicTripPath(token: string): string {
  return `/shared/trips/${encodeURIComponent(token)}`;
}

/** The address of the same trip as a chrome-less viewer, for an iframe. */
export function publicTripEmbedPath(token: string): string {
  return `${publicTripPath(token)}/embed`;
}

/**
 * What the frame is called on the page that pastes it — one name per published trip.
 *
 * <b>Derived from the token because two blocks land in one article.</b> A club writes about two
 * trips in the same post, pastes both blocks, and a link beside the second one has to be able to
 * say which viewer it drives. A fixed name would make both frames answer to it, `getElementById`
 * would hand back the first, and prose about one cave would move the camera in the other's model —
 * so the name has to differ per trip, and the only thing the snippet holds that differs per trip
 * is the token.
 *
 * A prefix of it, not the whole thing: the id is written out by hand in the article's links, and
 * forty-three characters of base64 is not something anybody types twice. It discloses nothing —
 * the whole token is already in the `src` two lines below, in the same page, in the clear.
 *
 * Two frames of the *same* trip on one page do share a name. That is left alone deliberately: both
 * show the same model of the same cave, so a link reaching the first says nothing untrue.
 */
export function embedFrameId(token: string): string {
  // Ids go into HTML and into CSS selectors, so anything outside the token alphabet is dropped
  // rather than escaped. Real tokens are base64url and survive this untouched.
  const slug = token.replace(/[^A-Za-z0-9_-]/g, '').slice(0, 12);
  return `silexgis-trip${slug === '' ? '' : `-${slug}`}`;
}

function escapeAttribute(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

/**
 * The block somebody pastes into their website's editor.
 *
 * Self-contained by construction: no stylesheet to add, no script to host, nothing to configure on
 * the other end. What it produces is a frame that is correct at 360px and at 1600px for the same
 * reason — its height is derived from its width — and a small relay that lets the article's own
 * hyperlinks drive the viewer inside it.
 *
 * The comment at the top is part of the deliverable, not decoration. Whoever pastes this is an
 * editor rather than a developer, and the one thing they have to know to use the link attributes
 * is written where they will be looking.
 */
export function buildEmbedSnippet({
  origin,
  token,
  title,
  aspectRatio = EMBED_BOX.aspectRatio,
}: EmbedSnippetOptions): string {
  const src = escapeAttribute(`${origin.replace(/\/+$/, '')}${publicTripEmbedPath(token)}`);
  const home = escapeAttribute(origin.replace(/\/+$/, ''));
  const frameId = escapeAttribute(embedFrameId(token));
  const box =
    `position:relative;width:100%;aspect-ratio:${aspectRatio};` +
    `min-height:${EMBED_BOX.minHeightPx}px;max-height:${EMBED_BOX.maxHeight}`;

  return `<!-- SilexGIS live trip. This viewer is called ${frameId}.
     Links anywhere in this page can drive it:
       <a href="#cave" data-silexgis-station="sala-mare.4" data-silexgis-target="${frameId}">the sump</a>
       <a href="#cave" data-silexgis-survey="galeria-nord" data-silexgis-target="${frameId}">the north gallery</a>
       <a href="#cave" data-silexgis-caver="3" data-silexgis-target="${frameId}">caver 3</a>
     With only this one viewer on the page, data-silexgis-target may be left off. With more than
     one — two trips in the same article — every link must name the viewer it drives, or it is
     left alone rather than sent to the wrong cave. -->
<div style="${box}">
  <iframe
    id="${frameId}"
    src="${src}"
    title="${escapeAttribute(title)}"
    loading="lazy"
    allowfullscreen
    referrerpolicy="strict-origin-when-cross-origin"
    style="position:absolute;inset:0;width:100%;height:100%;border:0"
    data-silexgis-embed="${home}"></iframe>
</div>
<script>${RELAY_SOURCE}</script>`;
}
