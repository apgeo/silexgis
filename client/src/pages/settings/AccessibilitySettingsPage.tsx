// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { App, Button, Card, Flex, InputNumber, Select, Switch, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useUiPreferences, useUpdateUiPreferences } from '../../api/hooks.ts';
import { useLanguageChoice } from '../../i18n/languageChoice.ts';
import {
  DEFAULT_APPEARANCE,
  useUiPrefsStore,
  type Appearance,
  type DensityPref,
  type LandingPage,
  type ThemePref,
} from '../../stores/uiPrefsStore.ts';

/** The shape this page stores server-side; the server keeps it opaque. */
interface StoredPreferences {
  appearance?: Partial<Appearance>;
}

/**
 * Appearance and accessibility.
 *
 * Two cards, because the split is real and worth showing: what follows the person to every
 * machine they sign in on, and what belongs to this browser. Reduced motion is a need, not a
 * per-device taste, so it travels; a centerline budget tuned for a workstation would be wrong
 * on a phone, so it does not.
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
  const centerlineDetailZoom = useUiPrefsStore((s) => s.centerlineDetailZoom);
  const centerlineMaxPaths = useUiPrefsStore((s) => s.centerlineMaxPaths);
  const setCenterlineLimits = useUiPrefsStore((s) => s.setCenterlineLimits);

  const { data: stored } = useUiPreferences();
  const save = useUpdateUiPreferences();
  const hydrated = useRef(false);

  // Adopt the stored choice once, when this browser has none of its own to go on — otherwise a
  // fresh machine would ignore what the account already asked for.
  useEffect(() => {
    if (hydrated.current || !stored) {
      return;
    }
    hydrated.current = true;
    const remote = (stored.preferences as StoredPreferences | undefined)?.appearance;
    if (remote) {
      setAppearance(remote);
    }
  }, [stored, setAppearance]);

  const update = (patch: Partial<Appearance>) => {
    const next = { ...appearance, ...patch };
    setAppearance(patch);
    save.mutate({ appearance: next }, { onError: () => message.error(t('common.saveFailed')) });
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

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>
              {t('settings.accessibility.centerlineDetailZoom')}
            </Typography.Text>
            <InputNumber
              min={1}
              max={22}
              aria-label={t('settings.accessibility.centerlineDetailZoom')}
              value={centerlineDetailZoom}
              onChange={(value) =>
                setCenterlineLimits({ detailZoom: value ?? undefined, maxPaths: centerlineMaxPaths })
              }
            />
            <Typography.Text style={{ minWidth: 160 }}>
              {t('settings.accessibility.centerlineMaxPaths')}
            </Typography.Text>
            <InputNumber
              min={100}
              max={200000}
              step={1000}
              aria-label={t('settings.accessibility.centerlineMaxPaths')}
              value={centerlineMaxPaths}
              onChange={(value) =>
                setCenterlineLimits({ detailZoom: centerlineDetailZoom, maxPaths: value ?? undefined })
              }
            />
          </Flex>

          <Flex>
            <Button
              onClick={() => {
                setCenterlineLimits({ detailZoom: undefined, maxPaths: undefined });
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
