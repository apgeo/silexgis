// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { fireEvent } from '@testing-library/dom';
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useTranslation } from 'react-i18next';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import i18n from '../../i18n';
import { CHOICE_KEY } from '../../i18n/languageStorage.ts';
import PublicLanguageButton from './PublicLanguageButton.tsx';
import { PUBLIC_CHOICE_KEY } from './publicLanguageStorage.ts';
import { usePublicLanguage } from './usePublicLanguage.ts';

/** A published trip's page, reduced to the two things this rule is about. */
function Published() {
  const { t } = useTranslation();
  const control = usePublicLanguage();
  const { search } = useLocation();
  return (
    <>
      <p data-testid="said">{t('publicTrip.retry')}</p>
      <p data-testid="address">{search}</p>
      <PublicLanguageButton control={control} />
    </>
  );
}

function open(address: string) {
  return render(
    <MemoryRouter initialEntries={[address]}>
      <Routes>
        <Route path="/shared/trips/:token" element={<Published />} />
        <Route path="/shared/trips/:token/embed" element={<Published />} />
      </Routes>
    </MemoryRouter>,
  );
}

const IN_ENGLISH = 'Try again';
const IN_ROMANIAN = 'Încearcă din nou';

beforeEach(async () => {
  window.localStorage.clear();
  // What a visitor's browser holds on arrival: no choice, and so the language the application
  // opens in.
  await i18n.changeLanguage('ro');
});

afterEach(async () => {
  cleanup();
  window.localStorage.clear();
  // The rest of the suite reads English; this file must not leave it otherwise.
  await i18n.changeLanguage('en');
});

describe('the language a published trip opens in', () => {
  it('is the one the installation opens in when the address names none', () => {
    open('/shared/trips/tok');

    expect(screen.getByTestId('said')).toHaveTextContent(IN_ROMANIAN);
    expect(document.documentElement.lang).toBe('ro');
  });

  it('is the one the address names, on the page and in the frame', async () => {
    open('/shared/trips/tok?lang=en');
    expect(screen.getByTestId('said')).toHaveTextContent(IN_ENGLISH);
    expect(document.documentElement.lang).toBe('en');
    cleanup();

    await i18n.changeLanguage('ro');
    open('/shared/trips/tok/embed?lang=en');
    expect(screen.getByTestId('said')).toHaveTextContent(IN_ENGLISH);
  });

  it('does not record a language an address named as this browser’s choice', () => {
    // The author of the link chose it. Written down, it would decide whether a member's account
    // language is adopted the next time they sign in on this machine.
    open('/shared/trips/tok?lang=en');

    expect(screen.getByTestId('said')).toHaveTextContent(IN_ENGLISH);
    expect(window.localStorage.getItem(CHOICE_KEY)).toBeNull();
  });

  it('lets the address win over a choice made here before, and leaves that choice standing', () => {
    window.localStorage.setItem(CHOICE_KEY, 'ro');

    open('/shared/trips/tok?lang=en');

    expect(screen.getByTestId('said')).toHaveTextContent(IN_ENGLISH);
    expect(window.localStorage.getItem(CHOICE_KEY)).toBe('ro');
  });

  it('ignores a language it does not speak, and one that is not a language at all', () => {
    for (const address of [
      '/shared/trips/tok?lang=de',
      '/shared/trips/tok?lang=',
      '/shared/trips/tok?lang=english',
      '/shared/trips/tok?lang=en-GB',
    ]) {
      open(address);
      expect(screen.getByTestId('said')).toHaveTextContent(IN_ROMANIAN);
      expect(document.documentElement.lang).toBe('ro');
      cleanup();
    }
  });

  it('reads the name however an editor happened to type it', () => {
    open('/shared/trips/tok?lang=EN');

    expect(screen.getByTestId('said')).toHaveTextContent(IN_ENGLISH);
  });

  it('is asked of the address on a published trip and on no other screen', () => {
    // Measured on the application's own source: the one reader of `lang` is this hook, and the
    // screens that call it are the published trip's page and its frame. A third caller — the
    // signed-in layout, say — is a decision about every address in the application, and has to be
    // made here on purpose.
    const sourceRoot = join(process.cwd(), 'src');
    const callers = readdirSync(sourceRoot, { recursive: true, encoding: 'utf8' })
      .filter((file) => /\.tsx?$/.test(file) && !/\.test\.tsx?$/.test(file))
      .filter((file) => !file.endsWith('usePublicLanguage.ts'))
      .filter((file) => /\busePublicLanguage\s*\(/.test(readFileSync(join(sourceRoot, file), 'utf8')))
      .map((file) => relative(sourceRoot, join(sourceRoot, file)).split(sep).join('/'))
      .sort();

    expect(callers).toEqual([
      'pages/public/PublicTripEmbedPage.tsx',
      'pages/public/PublicTripPage.tsx',
    ]);
    // And the application's own settling of its language is told nothing about addresses.
    expect(i18n.options.detection?.order).not.toContain('querystring');
  });

  it('is the one a reader chose on a published page before, when the address names none', () => {
    window.localStorage.setItem(PUBLIC_CHOICE_KEY, 'en');

    open('/shared/trips/tok');

    expect(screen.getByTestId('said')).toHaveTextContent(IN_ENGLISH);
    expect(document.documentElement.lang).toBe('en');
  });

  it('lets the address win over a press made on a published page before, and leaves it standing', () => {
    window.localStorage.setItem(PUBLIC_CHOICE_KEY, 'en');

    open('/shared/trips/tok?lang=ro');

    expect(screen.getByTestId('said')).toHaveTextContent(IN_ROMANIAN);
    expect(window.localStorage.getItem(PUBLIC_CHOICE_KEY)).toBe('en');
  });

  it('reads nothing into a remembered value that is not one of its languages', () => {
    window.localStorage.setItem(PUBLIC_CHOICE_KEY, 'de');

    open('/shared/trips/tok');

    expect(screen.getByTestId('said')).toHaveTextContent(IN_ROMANIAN);
  });
});

describe('the button that changes a published trip’s language', () => {
  it('is a button named in the language it leads to, and says which language that is', () => {
    const { container } = open('/shared/trips/tok');

    const button = screen.getByRole('button', { name: 'Show this page in English' });
    expect(button).toHaveTextContent('English');
    expect(button).toHaveAttribute('lang', 'en');
    expect(container.querySelectorAll('a')).toHaveLength(0);
  });

  it('changes the page, the document and the address, and is remembered for published pages', () => {
    open('/shared/trips/tok?past=abc&lang=ro');

    fireEvent.click(screen.getByTestId('public-trip-language'));

    expect(screen.getByTestId('said')).toHaveTextContent(IN_ENGLISH);
    expect(document.documentElement.lang).toBe('en');
    // The rest of the address is the reader's place on the page and is left alone.
    expect(screen.getByTestId('address')).toHaveTextContent('?past=abc&lang=en');
    expect(window.localStorage.getItem(PUBLIC_CHOICE_KEY)).toBe('en');
    // And now it leads back.
    expect(screen.getByRole('button', { name: 'Afișează pagina în română' })).toHaveAttribute(
      'lang',
      'ro',
    );
  });

  it('leaves the signed-in application’s own record of a choice exactly as it found it', () => {
    // That record decides what the application opens in and whether an account's stored language
    // may be adopted at sign-in, and its one writer also saves to the account. A page read without
    // an account cannot, so it must not write there — neither where nothing was chosen…
    open('/shared/trips/tok');
    fireEvent.click(screen.getByTestId('public-trip-language'));
    expect(screen.getByTestId('said')).toHaveTextContent(IN_ENGLISH);
    expect(window.localStorage.getItem(CHOICE_KEY)).toBeNull();
    cleanup();

    // …nor over a choice somebody did make there.
    window.localStorage.setItem(CHOICE_KEY, 'ro');
    open('/shared/trips/tok?lang=ro');
    fireEvent.click(screen.getByTestId('public-trip-language'));
    expect(window.localStorage.getItem(CHOICE_KEY)).toBe('ro');
  });

  it('is not undone by an address that still named the language the page opened in', () => {
    open('/shared/trips/tok?lang=en');
    fireEvent.click(screen.getByTestId('public-trip-language'));
    expect(screen.getByTestId('said')).toHaveTextContent(IN_ROMANIAN);

    fireEvent.click(screen.getByTestId('public-trip-language'));
    expect(screen.getByTestId('said')).toHaveTextContent(IN_ENGLISH);
    expect(screen.getByTestId('address')).toHaveTextContent('?lang=en');
  });
});
