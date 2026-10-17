// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, Alert, Card, Flex, Select, Typography, type GetRef } from 'antd';
import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'react-router-dom';
import { useCapabilities, useCreateDoor } from '../../api/hooks/access.ts';
import { useSaveAccountPreferences } from '../../api/hooks/accountPrefs.ts';
import {
  SAVED_FILTER_AUDIENCE_ANCHOR,
  audienceFromKey,
  audienceKey,
  effectiveSavedFilterAudience,
} from '../../filters/savedFilterAudience.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

/**
 * Who a saved filter is shared with when the member does not choose otherwise at the save.
 *
 * Private until they pick something else here. The choice follows the account, not the browser,
 * and is only a starting point: every place a filter is saved shows the audience it will get and
 * lets it be changed, and the server stores whatever audience it is sent — it applies no default
 * of its own beyond private.
 *
 * The caving groups offered are the member's own groups a saved filter may be bound to, as the
 * server decides it for a create (saved filters share the saved map views' permission area) — not
 * every club in the installation, which the directory of caving groups would list.
 */
export default function SavedFilterSettingsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { hash } = useLocation();
  const stored = useUiPrefsStore((s) => s.savedFilterAudience);
  const setStored = useUiPrefsStore((s) => s.setSavedFilterAudience);
  const capabilities = useCapabilities();
  const { cavingGroups } = useCreateDoor('mapViews');
  const groups = capabilities.data ? cavingGroups : undefined;
  const effective = effectiveSavedFilterAudience(stored, groups);
  const groupGone = stored.visibility === 'cavingGroup' && effective.visibility !== 'cavingGroup';

  const save = useSaveAccountPreferences();
  const selectRef = useRef<GetRef<typeof Select>>(null);
  const sectionRef = useRef<HTMLDivElement>(null);

  // Reached by a "change your default" link beside a save dialog: bring the choice into view and
  // put the keyboard on it, so the person lands where the link said rather than at the page top.
  useEffect(() => {
    if (hash !== `#${SAVED_FILTER_AUDIENCE_ANCHOR}`) {
      return;
    }
    sectionRef.current?.scrollIntoView?.({ block: 'start' });
    selectRef.current?.focus();
  }, [hash]);

  const groupName = (id: string) => groups?.find((group) => group.id === id)?.name ?? t('savedFilters.someGroup');

  const options = [
    { value: audienceKey({ visibility: 'private' }), label: t('savedFilters.private') },
    ...(groups ?? []).map((group) => ({
      value: audienceKey({ visibility: 'cavingGroup', cavingGroupId: group.id }),
      label: t('savedFilters.sharedWith', { name: group.name }),
    })),
    // While the member's groups are on their way, the stored club is still the value: give it a
    // label rather than letting the control show its raw key.
    ...(groups === undefined && effective.visibility === 'cavingGroup'
      ? [{ value: audienceKey(effective), label: t('savedFilters.someGroup') }]
      : []),
    { value: audienceKey({ visibility: 'authenticated' }), label: t('savedFilters.authenticated') },
  ];

  const explanation =
    effective.visibility === 'cavingGroup'
      ? t('savedFilters.explain.cavingGroup', { name: groupName(effective.cavingGroupId) })
      : t(`savedFilters.explain.${effective.visibility}`);

  const choose = (key: string) => {
    setStored(audienceFromKey(key));
    save.mutate(undefined, { onError: () => message.error(t('common.saveFailed')) });
  };

  return (
    <Flex vertical gap={16} ref={sectionRef} id={SAVED_FILTER_AUDIENCE_ANCHOR}>
      <Card
        size="small"
        title={t('settings.filters.heading')}
        extra={<Link to="/saved-filters">{t('settings.filters.manage')}</Link>}
      >
        <Flex vertical gap={16}>
          <Typography.Text type="secondary">{t('settings.filters.intro')}</Typography.Text>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }} id="saved-filter-default-audience-label">
              {t('settings.filters.defaultAudience')}
            </Typography.Text>
            <Select
              ref={selectRef}
              style={{ width: 280 }}
              value={audienceKey(effective)}
              onChange={choose}
              loading={capabilities.isLoading}
              aria-label={t('settings.filters.defaultAudience')}
              aria-describedby="saved-filter-default-audience-explanation"
              data-testid="saved-filter-default-audience"
              options={options}
            />
          </Flex>

          <Typography.Text id="saved-filter-default-audience-explanation" data-testid="saved-filter-default-audience-explanation">
            {explanation}
          </Typography.Text>

          {groupGone && <Alert type="warning" showIcon title={t('settings.filters.groupGone')} />}
        </Flex>
      </Card>
    </Flex>
  );
}
