// SPDX-License-Identifier: AGPL-3.0-or-later

// What a presentation draws over the application: a pointer (an automated browser has none a
// screenshot can see), the ring a click leaves, captions, title cards and a fade to black.
//
// Every part is set from the presentation's clock on each step rather than animated by the
// browser, so a movie made one frame at a time shows exactly what one played in real time does.

const POINTER_SVG =
  '<svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 28 28">' +
  '<path d="M4 2 L4 22 L9.5 17 L13 25 L16.5 23.5 L13 15.8 L20.5 15.8 Z" fill="#111" stroke="#fff" ' +
  'stroke-width="1.8" stroke-linejoin="round"/></svg>';

function element(tag: string, style: Partial<CSSStyleDeclaration>): HTMLElement {
  const node = document.createElement(tag);
  Object.assign(node.style, style);
  return node;
}

// Text is sized in parts of the window's height, so a draft and a full-size movie are laid out alike.
export class PresenterOverlay {
  private readonly root: HTMLElement;
  private readonly pointer: HTMLElement;
  private readonly ripple: HTMLElement;
  private readonly caption: HTMLElement;
  private readonly title: HTMLElement;
  private readonly titleText: HTMLElement;
  private readonly subtitleText: HTMLElement;
  private readonly fade: HTMLElement;

  constructor() {
    this.root = element('div', {
      position: 'fixed',
      inset: '0',
      pointerEvents: 'none',
      zIndex: '2147483647',
      fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif',
    });
    this.root.dataset.presenter = 'overlay';
    this.fade = element('div', { position: 'absolute', inset: '0', background: '#000', opacity: '0' });
    this.title = element('div', {
      position: 'absolute',
      inset: '0',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      gap: '18px',
      background: 'radial-gradient(ellipse at center, rgba(10,40,40,0.92), rgba(0,0,0,0.96))',
      color: '#fff',
      opacity: '0',
      textAlign: 'center',
    });
    this.titleText = element('div', { fontSize: '6vh', fontWeight: '600', letterSpacing: '0.01em' });
    this.subtitleText = element('div', { fontSize: '2.6vh', opacity: '0.8' });
    this.title.append(this.titleText, this.subtitleText);
    this.caption = element('div', {
      position: 'absolute',
      left: '50%',
      bottom: '7%',
      transform: 'translateX(-50%)',
      maxWidth: '70%',
      padding: '1.3vh 2.6vh',
      borderRadius: '1.1vh',
      background: 'rgba(0,0,0,0.68)',
      color: '#fff',
      fontSize: '2.8vh',
      lineHeight: '1.3',
      textAlign: 'center',
      opacity: '0',
    });
    this.ripple = element('div', {
      position: 'absolute',
      width: '44px',
      height: '44px',
      marginLeft: '-22px',
      marginTop: '-22px',
      borderRadius: '50%',
      border: '3px solid rgba(255,196,0,0.95)',
      opacity: '0',
    });
    this.pointer = element('div', { position: 'absolute', width: '28px', height: '28px', opacity: '0' });
    this.pointer.innerHTML = POINTER_SVG;
    this.root.append(this.caption, this.ripple, this.pointer, this.title, this.fade);
    document.body.append(this.root);
  }

  setPointer(x: number, y: number, opacity: number): void {
    // The arrow's tip is drawn at (4, 2) of its box.
    this.pointer.style.transform = `translate(${x - 4}px, ${y - 2}px)`;
    this.pointer.style.opacity = String(opacity);
  }

  /** `age` is seconds since the click; the ring grows and fades over half a second. */
  setRipple(x: number, y: number, age: number | undefined): void {
    if (age === undefined || age < 0 || age > 0.5) {
      this.ripple.style.opacity = '0';
      return;
    }
    const p = age / 0.5;
    this.ripple.style.left = `${x}px`;
    this.ripple.style.top = `${y}px`;
    this.ripple.style.transform = `scale(${0.4 + p * 1.1})`;
    this.ripple.style.opacity = String(1 - p);
  }

  setCaption(text: string | undefined, opacity: number): void {
    if (text !== undefined && this.caption.textContent !== text) {
      this.caption.textContent = text;
    }
    this.caption.style.opacity = String(text === undefined ? 0 : opacity);
  }

  setTitle(text: string | undefined, subtitle: string | undefined, opacity: number): void {
    if (text !== undefined) {
      this.titleText.textContent = text;
      this.subtitleText.textContent = subtitle ?? '';
    }
    this.title.style.opacity = String(text === undefined ? 0 : opacity);
  }

  setFade(opacity: number): void {
    this.fade.style.opacity = String(opacity);
  }

  destroy(): void {
    this.root.remove();
  }
}
