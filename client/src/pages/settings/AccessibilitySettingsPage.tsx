// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, Card, Flex, Select, Switch, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useSaveAccountPreferences } from '../../api/hooks/accountPrefs.ts';
import { useLanguageChoice } from '../../i18n/languageChoice.ts';
import { COORDINATE_FORMATS, formatCoordinate, type CoordinateFormat } from '../../geo/coordinateFormat.ts';
import {
  DEFAULT_APPEARANCE,
  useUiPrefsStore,
  type Appearance,
  type DensityPref,
  type LandingPage,
  type ThemePref,
} from '../../stores/uiPrefsStore.ts';

/**
 * The position each format is shown with in the list, so the choice is made by what it looks like:
 * the summit of Moldoveanu, a public trig point, rather than anything from the registry.
 */
const COORDINATE_EXAMPLE: [number, number] = [24.73617, 45.59944];

/**
 * Appearance and accessibility.
 *
 * Two cards, because the split is real and worth showing: what follows the person to every
 * machine they sign in on, and what belongs to this browser. Reduced motion is a need, not a
 * per-device taste, so it travels; map controls hidden to make room on a phone would be wrong
 * on a workstation, so that does not.
 *
 * Each control writes the local copy immediately and saves in the background — a theme switch
 * that waited for a round trip before repainting would feel broken.
 */
export default function AccessibilitySettingsPage() {
  const { t } = useTranslation();
  const { language, choose } = useLanguageChoice();
  const { message } = App.useApp();
  const appearance = useUiPrefsStore((s) => s.appearance);
  const setAppearance = useUiPrefsStore((s) => s.setAppearance);
  const landingPage = useUiPrefsStore((s) => s.landingPage);
  const setLandingPage = useUiPrefsStore((s) => s.setLandingPage);
  const mapChromeHidden = useUiPrefsStore((s) => s.mapChromeHidden);
  const setMapChromeHidden = useUiPrefsStore((s) => s.setMapChromeHidden);
  const coordinateFormat = useUiPrefsStore((s) => s.coordinateFormat);
  const setCoordinateFormat = useUiPrefsStore((s) => s.setCoordinateFormat);
  const overviewMap = useUiPrefsStore((s) => s.overviewMap);
  const setOverviewMap = useUiPrefsStore((s) => s.setOverviewMap);

  // What the account has stored was taken at sign-in; a change here is written through the one
  // writer of the account's preferences — at once, so a failure can be said here — which merges it
  // into the stored document rather than replacing the document with this page's part of it.
  const save = useSaveAccountPreferences();
  const saveNow = () => save.mutate(undefined, { onError: () => message.error(t('common.saveFailed')) });

  const update = (patch: Partial<Appearance>) => {
    setAppearance(patch);
    saveNow();
  };

  return (
    <Flex vertical gap={16}>
      <Card size="small" title={t('settings.accessibility.heading')}>
        <Flex vertical gap={16}>
          <Typography.Text type="secondary">{t('settings.accessibility.intro')}</Typography.Text>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>{t('settings.accessibility.theme')}</Typography.Text>
            <Select
              style={{ width: 200 }}
              value={appearance.theme}
              onChange={(theme: ThemePref) => update({ theme })}
              aria-label={t('settings.accessibility.theme')}
              options={(['system', 'light', 'dark'] as const).map((value) => ({
                value,
                label: t(`settings.accessibility.themeValues.${value}`),
              }))}
            />
          </Flex>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>{t('settings.accessibility.density')}</Typography.Text>
            <Select
              style={{ width: 200 }}
              value={appearance.density}
              onChange={(density: DensityPref) => update({ density })}
              aria-label={t('settings.accessibility.density')}
              options={(['comfortable', 'compact'] as const).map((value) => ({
                value,
                label: t(`settings.accessibility.densityValues.${value}`),
              }))}
            />
          </Flex>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>
              {t('settings.accessibility.reduceMotion')}
            </Typography.Text>
            <Switch
              checked={appearance.reduceMotion}
              onChange={(reduceMotion) => update({ reduceMotion })}
              aria-label={t('settings.accessibility.reduceMotion')}
            />
            <Typography.Text type="secondary">{t('settings.accessibility.reduceMotionHint')}</Typography.Text>
          </Flex>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>{t('settings.accessibility.coordinateFormat')}</Typography.Text>
            <Select
              style={{ width: 280 }}
              value={coordinateFormat}
              onChange={(format: CoordinateFormat) => {
                setCoordinateFormat(format);
                saveNow();
              }}
              aria-label={t('settings.accessibility.coordinateFormat')}
              data-testid="coordinate-format"
              options={COORDINATE_FORMATS.map((value) => ({
                value,
                label: `${t(`settings.accessibility.coordinateFormatValues.${value}`)} — ${formatCoordinate(
                  COORDINATE_EXAMPLE[0],
                  COORDINATE_EXAMPLE[1],
                  value,
                )}`,
              }))}
            />
          </Flex>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>{t('settings.accessibility.overviewMap')}</Typography.Text>
            <Switch
              checked={overviewMap}
              onChange={(open) => {
                setOverviewMap(open);
                saveNow();
              }}
              aria-label={t('settings.accessibility.overviewMap')}
            />
          </Flex>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>{t('common.language')}</Typography.Text>
            <Select
              style={{ width: 200 }}
              value={language}
              onChange={choose}
              aria-label={t('common.language')}
              options={[
                { value: 'en', label: 'English' },
                { value: 'ro', label: 'Română' },
              ]}
            />
          </Flex>
        </Flex>
      </Card>

      <Card size="small" title={t('settings.accessibility.deviceOnly')}>
        <Flex vertical gap={16}>
          <Typography.Text type="secondary">{t('settings.accessibility.deviceOnlyIntro')}</Typography.Text>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>{t('settings.accessibility.landingPage')}</Typography.Text>
            <Select
              style={{ width: 200 }}
              value={landingPage}
              onChange={(page: LandingPage) => setLandingPage(page)}
              aria-label={t('settings.accessibility.landingPage')}
              options={(['map', 'dashboard'] as const).map((value) => ({
                value,
                label: t(`settings.accessibility.landingValues.${value}`),
              }))}
            />
          </Flex>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>{t('settings.accessibility.mapChrome')}</Typography.Text>
            <Switch
              checked={mapChromeHidden}
              onChange={setMapChromeHidden}
              aria-label={t('settings.accessibility.mapChrome')}
            />
          </Flex>

          <Flex>
            {/* Puts back what this page shows and nothing else. The rendering budgets kept for
                this browser are set on the advanced page and have their own way back there; a
                button here that emptied them would do it where nobody is looking. */}
            <Button
              onClick={() => {
                setMapChromeHidden(false);
                setLandingPage('map');
                update(DEFAULT_APPEARANCE);
                message.success(t('settings.accessibility.resetDone'));
              }}
            >
              {t('settings.accessibility.reset')}
            </Button>
          </Flex>
        </Flex>
      </Card>
    </Flex>
  );
}
