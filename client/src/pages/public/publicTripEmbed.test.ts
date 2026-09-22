// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import {
  EMBED_CHANNEL,
  EMBED_FOCUS_KINDS,
  EMBED_PROTOCOL,
  buildEmbedSnippet,
  embedFrameId,
  parseEmbedInbound,
  publicTripEmbedPath,
  publicTripPath,
} from './publicTripEmbed.ts';

const TOKEN = 'abcDEF-123_xyz';
const TOKEN_A = 'AAAAaaaa1111-token-a';
const TOKEN_B = 'BBBBbbbb2222-token-b';

function snippet(overrides: Partial<Parameters<typeof buildEmbedSnippet>[0]> = {}) {
  return buildEmbedSnippet({
    origin: 'https://caves.example.org',
    token: TOKEN,
    title: 'Peștera Demo Mare',
    ...overrides,
  });
}

describe('the block a website is handed', () => {
  it('sizes the frame from its width, and never in fixed pixels', () => {
    // The failure this guards against: a fixed-height iframe in a mobile article, which is either
    // a letterbox or a scroll trap at 360px and cannot be right at both widths.
    const html = snippet();

    expect(html).toContain('aspect-ratio:4 / 3');
    expect(html).toContain('width:100%');
    expect(html).toContain('height:100%');
    // A floor and a ceiling are bounds on a derived height and are fine; a height stated in pixels
    // is the defect, and so are the width/height attributes an editor's embed dialog produces.
    expect(html).toContain('min-height:260px');
    expect(html).not.toMatch(/(?<!min-)(?<!max-)height:\s*\d+px/);
    expect(html).not.toMatch(/width="\d+"/);
    expect(html).not.toMatch(/height="\d+"/);
  });

  it('points the frame at the embed address of this installation', () => {
    expect(snippet()).toContain(
      `src="https://caves.example.org/shared/trips/${TOKEN}/embed"`,
    );
  });

  it('tells the relay which origin it is allowed to talk to', () => {
    // The one place the host script learns where to post, and the one thing it compares an
    // incoming message's origin against. Without it the relay would have to post to '*'.
    expect(snippet()).toContain('data-silexgis-embed="https://caves.example.org"');
  });

  it('trims a trailing slash off the installation address rather than doubling one', () => {
    const html = snippet({ origin: 'https://caves.example.org/' });

    expect(html).toContain('src="https://caves.example.org/shared/trips/');
    expect(html).not.toContain('example.org//shared');
  });

  it('escapes the caption, because a trip is named by somebody who can type a quote', () => {
    const html = snippet({ title: 'Ana\'s "big" trip <2026>' });

    expect(html).toContain('title="Ana\'s &quot;big&quot; trip &lt;2026&gt;"');
  });

  it('explains the link attributes where the person pasting it will be looking', () => {
    const html = snippet();

    expect(html).toContain('data-silexgis-station=');
    expect(html).toContain('data-silexgis-survey=');
    expect(html).toContain('data-silexgis-caver=');
    expect(html).toContain('data-silexgis-target=');
  });

  it('carries its own relay rather than fetching one, and never posts to a wildcard origin', () => {
    const html = snippet();

    expect(html).toContain('<script>');
    // Nothing to host, nothing to keep in step, nothing for a host page's own policy to refuse.
    expect(html).not.toMatch(/<script[^>]+src=/);
    expect(html).not.toContain("postMessage(message, '*')");
    expect(html).not.toContain(", '*')");
  });

  it('names the channel and version the embedded page checks for', () => {
    const html = snippet();

    expect(html).toContain(`var CHANNEL = '${EMBED_CHANNEL}';`);
    expect(html).toContain(`var VERSION = ${EMBED_PROTOCOL};`);
  });

  it('checks the origin of what comes back as well as where it posts', () => {
    const html = snippet();

    expect(html).toContain("getAttribute('data-silexgis-embed') === event.origin");
    expect(html).toContain('contentWindow === event.source');
  });
});

/**
 * A club's article with blocks pasted into it, driven as a browser would drive it.
 *
 * The relay is handed out as text and runs on somebody else's page, so the only way to know what
 * it does is to run it on one. A document of its own per page rather than the test file's own:
 * these scripts attach listeners to the document they are given, and a page that inherited the
 * previous test's listeners would answer clicks nobody in this test made.
 */
function hostPage(html: string) {
  const doc = document.implementation.createHTMLDocument('a club’s article');
  doc.body.innerHTML = html;

  const sent: { frame: string; message: Record<string, unknown>; origin: string }[] = [];
  for (const frame of Array.from(doc.querySelectorAll('iframe'))) {
    // A frame in a document with no browsing context has no contentWindow, and what the relay
    // posted to which frame is the whole of what is being measured.
    Object.defineProperty(frame, 'contentWindow', {
      configurable: true,
      value: {
        postMessage: (message: Record<string, unknown>, origin: string) =>
          sent.push({ frame: frame.id, message, origin }),
      },
    });
  }

  // The relay's own `window`: it keeps its registry on this and listens for messages on it.
  const win = { addEventListener: () => {} } as unknown as Window;
  for (const script of Array.from(doc.querySelectorAll('script'))) {
    // The snippet is pasted as text and executed by a browser; running it any other way here
    // would be testing something this project does not hand out.
    new Function('window', 'document', script.textContent ?? '')(win, doc);
  }

  return {
    document: doc,
    sent,
    focuses: () => sent.filter((posted) => posted.message.type === 'focus'),
    click(selector: string) {
      doc
        .querySelector(selector)!
        .dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    },
  };
}

const idA = embedFrameId(TOKEN_A);
const idB = embedFrameId(TOKEN_B);

/** Two trips written up in one post, which is the ordinary shape of a club's trip report page. */
function twoEmbeds() {
  return hostPage(
    `${snippet({ token: TOKEN_A, title: 'Trip A' })}
     <p>
       <a id="link-a" href="#cave" data-silexgis-station="A.1" data-silexgis-target="${idA}">in cave A</a>
       <a id="link-b" href="#cave" data-silexgis-station="B.1" data-silexgis-target="${idB}">in cave B</a>
       <a id="link-none" href="#cave" data-silexgis-station="A.1">somewhere</a>
     </p>
     ${snippet({ token: TOKEN_B, title: 'Trip B' })}`,
  );
}

describe('an article that frames more than one trip', () => {
  it('gives each viewer a name of its own, and tells the editor what it is', () => {
    // A fixed name is what makes the documented remedy inoperative: both frames answer to it,
    // getElementById hands back the first, and a link about the second cave drives the first.
    expect(idA).not.toBe(idB);
    expect(snippet({ token: TOKEN_A })).toContain(`id="${idA}"`);
    expect(snippet({ token: TOKEN_A })).toContain(`data-silexgis-target="${idA}"`);

    const page = twoEmbeds();
    expect(page.document.querySelectorAll(`#${idA}`)).toHaveLength(1);
    expect(page.document.querySelectorAll(`#${idB}`)).toHaveLength(1);
  });

  it('sends a link’s ask to the viewer it names, once, and to no other', () => {
    // Twice-over wrong before: the second block's copy of the relay installed a second listener on
    // the document, so one click posted twice — and both went to the first frame.
    const page = twoEmbeds();
    page.click('#link-b');

    expect(page.focuses()).toHaveLength(1);
    expect(page.focuses()[0].frame).toBe(idB);
    expect(page.focuses()[0].message).toMatchObject({
      silexgis: EMBED_CHANNEL,
      v: EMBED_PROTOCOL,
      type: 'focus',
      target: { kind: 'station', ref: 'B.1' },
    });
    expect(page.focuses()[0].origin).toBe('https://caves.example.org');
  });

  it('greets each viewer once, however many blocks were pasted', () => {
    const page = twoEmbeds();
    const greetings = page.sent.filter((posted) => posted.message.type === 'hello');

    expect(greetings.map((posted) => posted.frame).sort()).toEqual([idA, idB].sort());
  });

  it('leaves a link that names no viewer alone rather than guessing which cave it meant', () => {
    // The failure being refused: prose about one cave flying the camera to a station in another
    // cave's model, which is a confident statement about where a place is, and wrong.
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const page = twoEmbeds();
    page.click('#link-none');

    expect(page.focuses()).toHaveLength(0);
    // Said out loud, because a link that does nothing is the other way to waste somebody's day.
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('data-silexgis-target'));
    warn.mockRestore();
  });

  it('still lets a lone viewer be driven by links that name nothing', () => {
    // The common case, and the reason naming is not simply made compulsory: one trip, one block,
    // prose written without an attribute anybody has to look up.
    const page = hostPage(
      `${snippet({ token: TOKEN_A })}
       <a id="link-none" href="#cave" data-silexgis-station="A.1">the sump</a>`,
    );
    page.click('#link-none');

    expect(page.focuses()).toHaveLength(1);
    expect(page.focuses()[0].frame).toBe(idA);
  });

  it('refuses a target that names something which is not one of these viewers', () => {
    const page = hostPage(
      `${snippet({ token: TOKEN_A })}
       <div id="not-a-frame"></div>
       <a id="link-none" href="#cave" data-silexgis-station="A.1" data-silexgis-target="not-a-frame">the sump</a>`,
    );
    page.click('#link-none');

    expect(page.focuses()).toHaveLength(0);
  });
});

describe('an article that drives the cave’s past', () => {
  /** One viewer and a paragraph of prose about a trip that is over. */
  function pastArticle() {
    return hostPage(
      `${snippet({ token: TOKEN_A, title: 'Trip A' })}
       <p>
         <a id="link-trip" href="#cave" data-silexgis-trip="trip-1">the 2019 push</a>
         <a id="link-team" href="#cave" data-silexgis-team="team-a" data-silexgis-trip="trip-1">the survey team</a>
         <a id="link-moment" href="#cave" data-silexgis-caver="2" data-silexgis-trip="trip-1"
            data-silexgis-moment="2019-07-06T13:40:00Z">where Ana was at half one</a>
         <a id="link-live" href="#cave" data-silexgis-trip="live">back to this trip</a>
       </p>`,
    );
  }

  it('sends a past trip as its own target', () => {
    const page = pastArticle();
    page.click('#link-trip');
    expect(page.focuses().at(-1)?.message.target).toEqual({ kind: 'trip', ref: 'trip-1' });
  });

  it('sends a team as the target and the trip as the context it is read in', () => {
    const page = pastArticle();
    page.click('#link-team');
    const posted = page.focuses().at(-1)?.message;
    // The narrower thing is what the link is about; the trip is where to look for it.
    expect(posted?.target).toEqual({ kind: 'team', ref: 'team-a' });
    expect(posted?.trip).toBe('trip-1');
  });

  it('carries a moment beside a person, so one press does both', () => {
    const page = pastArticle();
    page.click('#link-moment');
    const posted = page.focuses().at(-1)?.message;
    expect(posted?.target).toEqual({ kind: 'caver', ref: '2' });
    expect(posted?.trip).toBe('trip-1');
    expect(posted?.at).toBe('2019-07-06T13:40:00Z');
  });

  it('writes an article’s own way back out of the past', () => {
    const page = pastArticle();
    page.click('#link-live');
    expect(page.focuses().at(-1)?.message.target).toEqual({ kind: 'trip', ref: 'live' });
  });

  it('leaves a link naming none of these attributes entirely alone', () => {
    const page = hostPage(
      `${snippet({ token: TOKEN_A, title: 'Trip A' })}
       <p><a id="link-plain" href="/somewhere">an ordinary link</a></p>`,
    );
    page.click('#link-plain');
    expect(page.focuses()).toHaveLength(0);
  });

  it('keeps the protocol at one, because raising it would kill every snippet already pasted', () => {
    // Both halves compare `v` for equality. A relay pasted into a club's website years ago would
    // stop being answered at all — no greeting, no party, every station link dead — and nothing on
    // either side would say why. The vocabulary grows instead, in the one direction that is safe:
    // the snippet is generated by the installation it points at, so a relay is never newer than the
    // page, and an older relay simply never utters the new words.
    expect(EMBED_PROTOCOL).toBe(1);
    expect(snippet()).toContain('var VERSION = 1;');
  });

  it('keeps the attributes the click handler looks for and the kinds it sends as one list', () => {
    // A kind added to the parser and forgotten in the relay's selector is a link that silently
    // behaves as an ordinary link; the relay builds its selector from its own list, and this is
    // what holds that list against the page's.
    const html = snippet();
    for (const kind of EMBED_FOCUS_KINDS) {
      expect(html).toContain(`'${kind}'`);
    }
  });
});

describe('the two addresses a published trip has', () => {
  it('separates the followed page from the frame', () => {
    expect(publicTripPath(TOKEN)).toBe(`/shared/trips/${TOKEN}`);
    expect(publicTripEmbedPath(TOKEN)).toBe(`/shared/trips/${TOKEN}/embed`);
  });

  it('escapes a token that would otherwise change the address it is put in', () => {
    expect(publicTripPath('a/../b')).toBe('/shared/trips/a%2F..%2Fb');
  });
});

describe('what the embedded page will act on', () => {
  const focus = {
    silexgis: EMBED_CHANNEL,
    v: EMBED_PROTOCOL,
    type: 'focus',
    target: { kind: 'station', ref: 'p.g.7' },
  };

  it('reads a greeting and a focus', () => {
    expect(parseEmbedInbound({ silexgis: EMBED_CHANNEL, v: EMBED_PROTOCOL, type: 'hello' })).toEqual({
      type: 'hello',
    });
    expect(parseEmbedInbound(focus)).toEqual({
      type: 'focus',
      target: { kind: 'station', ref: 'p.g.7' },
      // The two members an older relay never sets. Read as absences rather than left off, so every
      // caller of this reads one shape — an article pasted years ago goes on driving the camera
      // exactly as it did, and the page it drives has no second branch for it.
      trip: null,
      at: null,
    });
  });

  it('ignores anything that is not this conversation', () => {
    // A page listening on `message` hears from every script in the frame tree and from whatever a
    // browser extension posts. None of it may reach the viewer, and none of it may make this throw.
    expect(parseEmbedInbound(null)).toBeNull();
    expect(parseEmbedInbound(42)).toBeNull();
    expect(parseEmbedInbound('hello')).toBeNull();
    expect(parseEmbedInbound({})).toBeNull();
    expect(parseEmbedInbound({ ...focus, silexgis: 'something-else' })).toBeNull();
    expect(parseEmbedInbound({ ...focus, v: EMBED_PROTOCOL + 1 })).toBeNull();
    expect(parseEmbedInbound({ ...focus, type: 'evaluate' })).toBeNull();
  });

  it('reads a past trip, a team and a moment off one link', () => {
    // One sentence in an article — "the survey team at the sump, on the 2019 trip" — has to be one
    // press, so the trip and the moment travel beside the target rather than as three links.
    expect(
      parseEmbedInbound({
        ...focus,
        target: { kind: 'team', ref: 'team-a' },
        trip: 'trip-1',
        at: '2019-07-06T13:40:00Z',
      }),
    ).toEqual({
      type: 'focus',
      target: { kind: 'team', ref: 'team-a' },
      trip: 'trip-1',
      at: '2019-07-06T13:40:00Z',
    });
  });

  it('reads a context of the wrong shape as an absent one rather than throwing', () => {
    // These arrive from another document as `unknown`. A page that destructured them would be a
    // page any script anywhere could make throw by posting a number at it.
    const read = parseEmbedInbound({ ...focus, trip: 42, at: { when: 'now' } });
    expect(read).toEqual({ type: 'focus', target: { kind: 'station', ref: 'p.g.7' }, trip: null, at: null });
    const overlong = parseEmbedInbound({ ...focus, trip: 'x'.repeat(401) });
    expect(overlong?.type === 'focus' ? overlong.trip : 'not a focus').toBeNull();
  });

  it('refuses a focus whose target is not one of the things prose can name', () => {
    expect(parseEmbedInbound({ ...focus, target: null })).toBeNull();
    expect(parseEmbedInbound({ ...focus, target: { kind: 'file', ref: '/etc/passwd' } })).toBeNull();
    expect(parseEmbedInbound({ ...focus, target: { kind: 'tripLog', ref: 'x' } })).toBeNull();
    expect(parseEmbedInbound({ ...focus, target: { kind: 'station', ref: '' } })).toBeNull();
    expect(parseEmbedInbound({ ...focus, target: { kind: 'station', ref: 7 } })).toBeNull();
    expect(
      parseEmbedInbound({ ...focus, target: { kind: 'station', ref: 'x'.repeat(401) } }),
    ).toBeNull();
  });
});
