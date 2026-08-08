// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { DeleteOutlined, EyeOutlined, PlusOutlined } from '@ant-design/icons';
import { App, Alert, Button, Checkbox, Flex, Select, Table, Tag, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../../api/client.ts';
import {
  parseAccessActions,
  usePermissionGroupEntries,
  useReplacePermissionGroupEntries,
  type AccessActionFlag,
  type AccessCatalog,
  type AccessCatalogDomain,
  type AccessDomainName,
  type AccessEffect,
  type AccessScopeKind,
  type FeatureKind,
  type PermissionGroup,
  useFeatureTypes,
} from '../../../api/hooks.ts';
import { joinActions } from '../../../components/permissions/accessDisplay.ts';
import RuleAnchorPicker from './RuleAnchorPicker.tsx';

interface DraftRule {
  key: number;
  effect: AccessEffect;
  domain: AccessDomainName;
  actions: Set<AccessActionFlag>;
  scopeKind: AccessScopeKind;
  scopeId: string | null;
  scopeLabel: string | null;
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
  /** "Preview first" affordance jumps to the preview tab. */
  onRequestPreview: () => void;
}

/**
 * The ruleset: rows of effect · domain · actions · scope, edited in place and saved as a
 * full replace. Everything offered — domains, the scopes each accepts, the actions valid
 * inside each scope — comes from the server's catalog, generated from the validator
 * itself, so this editor cannot offer what the server would refuse.
 *
 * A ruleset containing a deny cannot be saved before its effect has been *looked at*:
 * the preview is the only honest answer to what a deny actually costs, and a blind save
 * is exactly the foot-gun the model warns about.
 */
export default function RulesTab({ group, catalog, previewSeen, onRulesChanged, onRequestPreview }: RulesTabProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { token } = theme.useToken();
  const { data: entries, isPending } = usePermissionGroupEntries(group.id);
  const replaceEntries = useReplacePermissionGroupEntries(group.id);
  const { data: featureTypes } = useFeatureTypes();
  const [drafts, setDrafts] = useState<DraftRule[]>([]);
  const nextKey = useRef(0);

  const isFullAdministrators = group.isProtected && group.slug === 'full-administrators';

  useEffect(() => {
    if (entries) {
      setDrafts(entries.map((entry) => ({
        key: nextKey.current++,
        effect: entry.effect,
        domain: entry.domain,
        actions: new Set([...parseAccessActions(entry.actions)]),
        scopeKind: entry.scopeKind,
        scopeId: entry.scopeId ?? null,
        scopeLabel: entry.scopeLabel ?? null,
        featureKind: entry.featureKind ?? null,
        featureTypeId: entry.featureTypeId ?? null,
      })));
    }
  }, [entries]);

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
      scopeLabel: null,
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
      scopeLabel: null,
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
      scopeLabel: null,
      featureKind: null,
      featureTypeId: null,
    }]);
    onRulesChanged();
  };

  const hasDeny = drafts.some((d) => d.effect === 'deny');
  const incomplete = drafts.some((d) =>
    d.actions.size === 0 || ((scopeOf(d.domain, d.scopeKind)?.requiresAnchor ?? false) && !d.scopeId));
  const previewRequired = hasDeny && !previewSeen;

  const onSave = async () => {
    try {
      await replaceEntries.mutateAsync(drafts.map((d) => ({
        effect: d.effect,
        domain: d.domain,
        actions: joinActions(d.actions),
        scopeKind: d.scopeKind,
        scopeId: d.scopeId,
        featureKind: d.featureKind,
        featureTypeId: d.featureTypeId,
      })));
      message.success(t('common.saved'));
    } catch (error) {
      message.error(
        error instanceof ApiError && error.code === 'access_entry.exceeds_own_rights'
          ? t('permissionGroups.exceedsOwnRights')
          : t('common.saveFailed'),
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
                      label={draft.scopeLabel}
                      onChange={(scopeId, scopeLabel) => patch(draft.key, { scopeId, scopeLabel })}
                    />
                  )}
                  {allowsNarrowing(draft.domain, draft.scopeKind) && (
                    <>
                      <Select
                        size="small"
                        allowClear
                        placeholder={t('permissionGroups.narrowKind')}
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
            title: '',
            key: 'remove',
            width: 50,
            render: (_, draft) => (
              <Button
                size="small"
                type="text"
                danger
                icon={<DeleteOutlined />}
                onClick={() => {
                  setDrafts(drafts.filter((d) => d.key !== draft.key));
                  onRulesChanged();
                }}
              />
            ),
          },
        ]}
      />

      <Flex justify="space-between">
        <Button icon={<PlusOutlined />} onClick={addRule}>
          {t('permissionGroups.addRule')}
        </Button>
        <Button
          type="primary"
          onClick={() => void onSave()}
          loading={replaceEntries.isPending}
          disabled={previewRequired || incomplete}
          data-testid="save-rules"
        >
          {t('common.save')}
        </Button>
      </Flex>
    </Flex>
  );
}
