// SPDX-License-Identifier: AGPL-3.0-or-later

// What the person did just before the error.
//
// A stack trace says where a program was; it rarely says what was being done, and "what was being
// done" is the whole difference between a report somebody can reproduce and one that gets filed and
// never acted on. So a short trail is kept — the routes visited, the things clicked, the requests
// made and what they answered — and attached to whatever is reported next.
//
// A ring buffer, small on purpose. Nothing here is stored, sent anywhere but the local development
// sink, or kept between reloads; and a trail long enough to need scrolling is one nobody reads.

/** How much history is worth having. Roughly the last half-minute of ordinary use. */
const TRAIL_LENGTH = 25;

/** The longest a single entry may be. Clicked elements can carry a paragraph of text. */
const ENTRY_LENGTH = 120;

const trail: string[] = [];
let installed = false;

function clockTime(): string {
  const now = new Date();
  const pad = (value: number, width = 2) => String(value).padStart(width, '0');
  return `${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}.${pad(now.getMilliseconds(), 3)}`;
}

/** Adds one entry, dropping the oldest when the trail is full. */
export function recordBreadcrumb(kind: 'route' | 'click' | 'api' | 'note', text: string): void {
  trail.push(`${clockTime()} ${kind} ${text}`.slice(0, ENTRY_LENGTH));
  if (trail.length > TRAIL_LENGTH) {
    trail.shift();
  }
}

/** The trail as it stands, oldest first. A copy — a report must not alias the live buffer. */
export function breadcrumbTrail(): string[] {
  return [...trail];
}

/** Empties the trail. For tests; nothing in the application needs it. */
export function clearBreadcrumbs(): void {
  trail.length = 0;
}

/**
 * How an element is described in the trail, in the terms somebody would use to find it again.
 *
 * Accessible name first, then a test handle, then its own text, and a tag name only when an element
 * offers nothing else — which is the order of how useful each is for saying "the thing I clicked".
 * A CSS path is deliberately not used: it is precise about the DOM and says nothing about what was
 * on screen.
 */
function describeElement(element: Element): string {
  const role = element.getAttribute('role') ?? element.tagName.toLowerCase();
  const label =
    element.getAttribute('aria-label') ??
    element.getAttribute('data-testid') ??
    element.getAttribute('title') ??
    element.textContent?.trim().replace(/\s+/g, ' ') ??
    '';
  return label ? `${role} "${label.slice(0, 60)}"` : role;
}

/**
 * Starts recording routes and clicks. Safe to call more than once; the second call does nothing.
 *
 * Navigation is watched by wrapping the history methods rather than from inside the router. Two
 * reasons: the routes are defined as data, so there is no one component every navigation passes
 * through, and an error on the sign-in page has to be reported too — which rules out anything living
 * inside the authenticated layout. It also keeps every part of this diagnostic self-contained, so it
 * can be removed without touching the application.
 */
export function installBreadcrumbs(): void {
  if (installed) {
    return;
  }
  installed = true;

  // Capture phase, so a click is recorded even when a handler stops it from propagating — which the
  // interesting ones often do.
  window.addEventListener(
    'click',
    (event) => {
      try {
        const target = event.target;
        if (target instanceof Element) {
          // The nearest thing that behaves like a control, rather than the exact node under the
          // pointer, which for a button is usually its icon or a bare span of its label.
          const actionable = target.closest('button,a,[role],input,select,textarea,label') ?? target;
          recordBreadcrumb('click', describeElement(actionable));
        }
      } catch {
        // A breadcrumb is never worth an error of its own.
      }
    },
    { capture: true },
  );

  const history = window.history;
  const wrap = (method: 'pushState' | 'replaceState') => {
    const original = history[method].bind(history);
    history[method] = function patched(...args: Parameters<History['pushState']>) {
      const result = original(...args);
      try {
        recordBreadcrumb('route', `${method === 'pushState' ? 'to' : 'replace'} ${location.pathname}`);
      } catch {
        // As above.
      }
      return result;
    };
  };
  wrap('pushState');
  wrap('replaceState');
  window.addEventListener('popstate', () => recordBreadcrumb('route', `back to ${location.pathname}`));

  recordBreadcrumb('route', `opened ${location.pathname}`);
}
