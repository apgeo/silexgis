// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { DeleteOutlined, EyeOutlined, PlusOutlined } from '@ant-design/icons';
import { App, Alert, Checkbox, Flex, Select, Table, Tag, theme } from 'antd';
import Button from '../../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { isConcurrencyConflict } from '../../../api/client.ts';
import { versionReadWith } from '../../../api/readingVersion.ts';
import {
  parseAccessActions,
  usePermissionGroupEntries,
  useReplacePermissionGroupEntries,
  type AccessActionFlag,
  type AccessCatalog,
  type AccessCatalogDomain,
  type AccessEntry,
  type AccessDomainName,
  type AccessEffect,
  type AccessScopeKind,
  type FeatureKind,
  type PermissionGroup,
  useGenericFeatureTypes,
} from '../../../api/hooks.ts';
import { joinActions } from '../../../components/permissions/accessDisplay.ts';
import ReviewRightWarning from '../../../components/permissions/ReviewRightWarning.tsx';
import { grantsUploadReview } from '../../../components/permissions/reviewRight.ts';
import RuleAnchorPicker from './RuleAnchorPicker.tsx';
import { accessRuleProblem } from '../../../components/permissions/accessRuleProblem.ts';
import HiddenTitle from '../../../components/a11y/HiddenTitle.tsx';

interface DraftRule {
  key: number;
  effect: AccessEffect;
  domain: AccessDomainName;
  actions: Set<AccessActionFlag>;
  scopeKind: AccessScopeKind;
  scopeId: string | null;
  featureKind: FeatureKind | null;
  featureTypeId: number | null;
}

interface RulesTabProps {
  group: PermissionGroup;
  catalog: AccessCatalog;
  /** Whether a preview has been looked at since the rules last changed. */
  previewSeen: boolean;
  /** Any edit invalidates a previously seen preview. */
  onRulesChanged: () => void;
  /** Told whenever the drafts start or stop holding edits not yet saved. */
  onDirtyChange?: (dirty: boolean) => void;
  /** "Preview first" affordance jumps to the preview tab. */
  onRequestPreview: () => void;
}

/**
 * The ruleset: rows of effect · domain · actions · scope, edited in place and saved as a
 * full replace. Everything offered — domains, the scopes each accepts, the actions valid
 * inside each scope — comes from the server's catalog, generated from the validator
 * itself, so this editor cannot offer what the server would refuse.
 *
 * A ruleset containing a deny cannot be saved before the preview has been opened. The preview
 * answers from the rules already saved, so it shows whom the group reaches and what they hold
 * today — not what the unsaved deny would take away — and the warning says exactly that, with a
 * reminder after the save to look again once the deny is live.
 *
 * Saving replaces the whole ruleset, so another administrator's save made while these drafts were
 * open would be undone without a word. The rules the drafts were built from are therefore
 * remembered: a read that brings different ones (a background refetch, or the fresh read made just
 * before every save) stops the save until the administrator either loads the stored rules or says
 * to keep their own. The save also carries the version of the rules the drafts were built from,
 * so the server refuses it when a save landed between that last read and this one.
 */
export default function RulesTab({
  group,
  catalog,
  previewSeen,
  onRulesChanged,
  onDirtyChange,
  onRequestPreview,
}: RulesTabProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { token } = theme.useToken();
  const { data: entries, isPending, isError, refetch } = usePermissionGroupEntries(group.id);
  const replaceEntries = useReplacePermissionGroupEntries(group.id);
  const { data: featureTypes } = useGenericFeatureTypes();
  const [drafts, setDrafts] = useState<DraftRule[]>([]);
  const nextKey = useRef(0);
  // Saving replaces the group's whole ruleset, so nothing may be edited or saved before the
  // stored rules are known: a save over the empty starting list would wipe them.
  const loaded = entries !== undefined;
  // Whether the drafts hold edits not yet saved. A read of the stored rules that arrives
  // behind an edit (a focus refetch, another administrator's save) must not discard it.
  const [dirty, setDirty] = useState(false);
  const dirtyRef = useRef(false);
  const markDirty = (value: boolean) => {
    dirtyRef.current = value;
    setDirty(value);
    onDirtyChange?.(value);
  };

  // The stored rules the drafts were built from, as text, and whether a later read differed.
  const baselineRef = useRef<string | null>(null);
  // The version of that reading, which the save is held to: the server refuses a save made from
  // a ruleset another administrator has saved over since, however the two reads lined up here.
  const baselineVersionRef = useRef<string | undefined>(undefined);
  const [storedMoved, setStoredMoved] = useState(false);

  const isFullAdministrators = group.isProtected && group.slug === 'full-administrators';

  const fillFrom = (stored: AccessEntry[]) => {
    baselineRef.current = JSON.stringify(stored);
    baselineVersionRef.current = versionReadWith(stored);
    setStoredMoved(false);
    setDrafts(stored.map((entry) => ({
      key: nextKey.current++,
      effect: entry.effect,
      domain: entry.domain,
      actions: new Set([...parseAccessActions(entry.actions)]),
      scopeKind: entry.scopeKind,
      scopeId: entry.scopeId ?? null,
      featureKind: entry.featureKind ?? null,
      featureTypeId: entry.featureTypeId ?? null,
    })));
  };

  useEffect(() => {
    if (!entries) {
      return;
    }
    if (!dirtyRef.current) {
      fillFrom(entries);
    } else if (JSON.stringify(entries) !== baselineRef.current) {
      setStoredMoved(true);
    }
  }, [entries]);

  // Drops the drafts for the rules as they are stored now.
  const loadStored = () => {
    if (entries) {
      markDirty(false);
      fillFrom(entries);
      onRulesChanged();
    }
  };

  // The administrator has seen that the stored rules moved and chooses to replace them anyway.
  const keepMine = () => {
    baselineRef.current = JSON.stringify(entries);
    baselineVersionRef.current = versionReadWith(entries);
    setStoredMoved(false);
  };

  const domainOf = (name: AccessDomainName): AccessCatalogDomain =>
    catalog.domains.find((d) => d.domain === name)!;
  const scopeOf = (domain: AccessDomainName, scope: AccessScopeKind) =>
    domainOf(domain).scopes.find((s) => s.scopeKind === scope);

  const validActions = (domain: AccessDomainName, scope: AccessScopeKind): AccessActionFlag[] =>
    (scopeOf(domain, scope)?.actions ?? []).flatMap((a) => [...parseAccessActions(a)]);

  // Kind/type narrowing only has a faithful flat evaluation at the unanchored scopes;
  // the server rejects it anywhere else, so the control disappears rather than misleads.
  const allowsNarrowing = (domain: AccessDomainName, scope: AccessScopeKind) =>
    domainOf(domain).supportsKindNarrowing && (scope === 'all' || scope === 'own');

  const patch = (key: number, change: Partial<DraftRule>) => {
    setDrafts(drafts.map((d) => (d.key === key ? { ...d, ...change } : d)));
    markDirty(true);
    onRulesChanged();
  };

  const changeDomain = (draft: DraftRule, domain: AccessDomainName) => {
    const scopes = domainOf(domain).scopes;
    const scopeKind = scopes.some((s) => s.scopeKind === draft.scopeKind)
      ? draft.scopeKind
      : scopes[0].scopeKind;
    const valid = new Set(validActions(domain, scopeKind));
    patch(draft.key, {
      domain,
      scopeKind,
      scopeId: null,
      featureKind: null,
      featureTypeId: null,
      actions: new Set([...draft.actions].filter((a) => valid.has(a))),
    });
  };

  const changeScope = (draft: DraftRule, scopeKind: AccessScopeKind) => {
    const valid = new Set(validActions(draft.domain, scopeKind));
    patch(draft.key, {
      scopeKind,
      scopeId: null,
      actions: new Set([...draft.actions].filter((a) => valid.has(a))),
      ...(allowsNarrowing(draft.domain, scopeKind) ? {} : { featureKind: null, featureTypeId: null }),
    });
  };

  const addRule = () => {
    const domain = catalog.domains[0];
    setDrafts([...drafts, {
      key: nextKey.current++,
      effect: 'allow',
      domain: domain.domain,
      actions: new Set<AccessActionFlag>(['read']),
      scopeKind: domain.scopes[0].scopeKind,
      scopeId: null,
      featureKind: null,
      featureTypeId: null,
    }]);
    markDirty(true);
    onRulesChanged();
  };

  const hasDeny = drafts.some((d) => d.effect === 'deny');
  const incomplete = drafts.some((d) =>
    d.actions.size === 0 || ((scopeOf(d.domain, d.scopeKind)?.requiresAnchor ?? false) && !d.scopeId));
  const previewRequired = hasDeny && !previewSeen;

  const onSave = async () => {
    // A last look at what is stored, as close to the save as the client can get without a version:
    // a ruleset that moved since the drafts were built would be overwritten by this full replace.
    const fresh = await refetch();
    if (fresh.data && JSON.stringify(fresh.data) !== baselineRef.current) {
      setStoredMoved(true);
      return;
    }
    const savedWithDeny = hasDeny;
    try {
      await replaceEntries.mutateAsync({
        entries: drafts.map((d) => ({
          effect: d.effect,
          domain: d.domain,
          actions: joinActions(d.actions),
          scopeKind: d.scopeKind,
          scopeId: d.scopeId,
          featureKind: d.featureKind,
          featureTypeId: d.featureTypeId,
        })),
        ifMatch: baselineVersionRef.current,
      });
      // The save invalidates the stored rules; the read that follows refills the drafts.
      markDirty(false);
      message.success(t(savedWithDeny ? 'permissionGroups.savedWithDeny' : 'common.saved'), savedWithDeny ? 8 : undefined);
    } catch (error) {
      // Saved over since the drafts were built: the application's conflict warning says so, and
      // the read that follows brings the stored rules, which holds the save until the
      // administrator loads them or keeps their own.
      if (isConcurrencyConflict(error)) {
        void refetch();
        return;
      }
      message.error(
        accessRuleProblem(error, t),
      );
    }
  };

  if (isFullAdministrators) {
    return <Alert type="info" showIcon title={t('permissionGroups.fullAdminNoRules')} />;
  }

  return (
    <Flex vertical gap={12}>
      {previewRequired && (
        <Alert
          type="warning"
          showIcon
          title={t('permissionGroups.previewRequired')}
          action={
            <Button size="small" icon={<EyeOutlined />} onClick={onRequestPreview}>
              {t('permissionGroups.openPreview')}
            </Button>
          }
        />
      )}

      {storedMoved && (
        <Alert
          type="warning"
          showIcon
          data-testid="rules-stored-moved"
          title={t('permissionGroups.storedRulesMoved')}
          action={
            <Flex vertical gap={4}>
              <Button size="small" onClick={loadStored}>
                {t('permissionGroups.loadStoredRules')}
              </Button>
              <Button size="small" danger onClick={keepMine}>
                {t('permissionGroups.keepMyRules')}
              </Button>
            </Flex>
          }
        />
      )}

      {isError && (
        <Alert
          type="error"
          showIcon
          title={t('permissionGroups.rulesLoadFailed')}
          action={
            <Button size="small" onClick={() => void refetch()}>
              {t('common.retry')}
            </Button>
          }
        />
      )}

      {grantsUploadReview(drafts) && <ReviewRightWarning />}

      <Table<DraftRule>
        scroll={{ x: 'max-content' }}
        rowKey="key"
        size="small"
        pagination={false}
        loading={isPending}
        dataSource={drafts}
        // A deny row must be impossible to overlook in a screen of allows.
        onRow={(draft) => (draft.effect === 'deny' ? { style: { background: token.colorErrorBg } } : {})}
        columns={[
          {
            title: t('access.effect'),
            key: 'effect',
            width: 110,
            render: (_, draft) => (
              <Select
                size="small"
                aria-label={t('access.effect')}
                value={draft.effect}
                style={{ width: 100 }}
                onChange={(effect: AccessEffect) => patch(draft.key, { effect })}
                options={[
                  { value: 'allow', label: t('access.effects.allow') },
                  {
                    value: 'deny',
                    label: <Tag color="red" style={{ marginInlineEnd: 0 }}>{t('access.effects.deny')}</Tag>,
                  },
                ]}
              />
            ),
          },
          {
            title: t('access.domain'),
            key: 'domain',
            width: 190,
            render: (_, draft) => (
              <Select
                size="small"
                showSearch
                optionFilterProp="label"
                aria-label={t('access.domain')}
                value={draft.domain}
                style={{ width: 180 }}
                onChange={(domain: AccessDomainName) => changeDomain(draft, domain)}
                options={catalog.domains.map((d) => ({
                  value: d.domain,
                  label: t(`access.domains.${d.name}`),
                }))}
              />
            ),
          },
          {
            title: t('access.actionsColumn'),
            key: 'actions',
            render: (_, draft) => (
              <Flex wrap gap={4}>
                {validActions(draft.domain, draft.scopeKind).map((flag) => (
                  <Checkbox
                    key={flag}
                    checked={draft.actions.has(flag)}
                    onChange={(e) => {
                      const next = new Set(draft.actions);
                      if (e.target.checked) {
                        next.add(flag);
                      } else {
                        next.delete(flag);
                      }
                      patch(draft.key, { actions: next });
                    }}
                  >
                    {t(`access.actions.${flag}`)}
                  </Checkbox>
                ))}
              </Flex>
            ),
          },
          {
            title: t('access.scope'),
            key: 'scope',
            width: 420,
            render: (_, draft) => {
              const scopes = domainOf(draft.domain).scopes;
              const needsAnchor = scopeOf(draft.domain, draft.scopeKind)?.requiresAnchor ?? false;
              return (
                <Flex gap={6} wrap align="center">
                  <Select
                    size="small"
                    aria-label={t('access.scope')}
                    value={draft.scopeKind}
                    style={{ width: 170 }}
                    onChange={(scope: AccessScopeKind) => changeScope(draft, scope)}
                    options={scopes.map((s) => ({
                      value: s.scopeKind,
                      label: t(`access.scopes.${s.scopeKind}`),
                    }))}
                  />
                  {needsAnchor && (
                    <RuleAnchorPicker
                      domain={draft.domain}
                      scopeKind={draft.scopeKind}
                      catalog={catalog}
                      value={draft.scopeId}
                      onChange={(scopeId) => patch(draft.key, { scopeId })}
                    />
                  )}
                  {allowsNarrowing(draft.domain, draft.scopeKind) && (
                    <>
                      <Select
                        size="small"
                        allowClear
                        placeholder={t('permissionGroups.narrowKind')}
                        aria-label={t('permissionGroups.narrowKind')}
                        value={draft.featureKind ?? undefined}
                        style={{ width: 150 }}
                        // Kind AND type on one rule has no faithful flat form — one or the other.
                        disabled={draft.featureTypeId !== null}
                        onChange={(kind?: FeatureKind) => patch(draft.key, { featureKind: kind ?? null })}
                        options={(['generic', 'cave', 'caveEntrance', 'centerline'] as const).map((kind) => ({
                          value: kind,
                          label: t(`featureKinds.${kind}`),
                        }))}
                      />
                      <Select
                        size="small"
                        allowClear
                        showSearch
                        optionFilterProp="label"
                        placeholder={t('permissionGroups.narrowType')}
                        aria-label={t('permissionGroups.narrowType')}
                        value={draft.featureTypeId ?? undefined}
                        style={{ width: 170 }}
                        disabled={draft.featureKind !== null}
                        onChange={(id?: number) => patch(draft.key, { featureTypeId: id ?? null })}
                        options={featureTypes?.map((type) => ({ value: type.id, label: type.name }))}
                      />
                    </>
                  )}
                </Flex>
              );
            },
          },
          {
            title: <HiddenTitle>{t('a11y.actionsColumn')}</HiddenTitle>,
            key: 'remove',
            width: 50,
            render: (_, draft) => (
              <Button
                size="small"
                type="text"
                danger
                icon={<DeleteOutlined />}
                aria-label={t('iconButtons.delete')}
                onClick={() => {
                  setDrafts(drafts.filter((d) => d.key !== draft.key));
                  markDirty(true);
                  onRulesChanged();
                }}
              />
            ),
          },
        ]}
      />

      <Flex justify="space-between">
        <Button icon={<PlusOutlined />} onClick={addRule} disabled={!loaded}>
          {t('permissionGroups.addRule')}
        </Button>
        <Button
          type="primary"
          onClick={() => void onSave()}
          loading={replaceEntries.isPending}
          disabled={!loaded || !dirty || previewRequired || incomplete || storedMoved}
          data-testid="save-rules"
        >
          {t('common.save')}
        </Button>
      </Flex>
    </Flex>
  );
}
