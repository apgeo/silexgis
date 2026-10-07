// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, PlusOutlined, ReloadOutlined, ShareAltOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Checkbox,
  Flex,
  Modal,
  Popconfirm,
  Select,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import {
  useApplyExpeditionSharing,
  useCavingGroups,
  useExpeditionSharing,
  useReapplyExpeditionSharing,
  useUserSearch,
  useWithdrawExpeditionSharing,
  type AccessActionFlag,
  type ExpeditionSharedRule,
  type ExpeditionShareEntry,
  type ExpeditionSharingOutcome,
} from '../../api/hooks.ts';
import { ACTION_ORDER, actionSetLabels, joinActions } from '../../components/permissions/accessDisplay.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

/**
 * The rights a camp may share down to its trips.
 *
 * Not *create*: a rule written onto one trip grants nothing to be created into, and the server
 * refuses it at object reach. Not *exact location*: the cascade refuses that flag outright — it
 * is inert on a trip, and offering it here would suggest a camp can hand out cave positions.
 */
const SHAREABLE: readonly AccessActionFlag[] = ACTION_ORDER.filter(
  (flag) => flag !== 'create' && flag !== 'viewExactLocation',
);

/** How many trips an act skipped: the ones it may name and the ones it only counts. */
const skippedCount = (outcome: ExpeditionSharingOutcome): number =>
  outcome.skippedTrips.length + outcome.skippedTripsNotNamed;

/** A rule being composed, before the act that writes it onto every member trip. */
interface Draft {
  subjectKind: 'user' | 'cavingGroup';
  subjectId: string;
  subjectName: string | null;
  effect: 'allow' | 'deny';
  actions: Set<AccessActionFlag>;
}

interface Props {
  expeditionId: string;
  open: boolean;
  onClose: () => void;
}

/**
 * Sharing a camp with somebody: what it grants on the trips the camp gathers, and the three acts
 * that change it.
 *
 * The rules do not sit on the camp. Sharing writes one object-scoped rule onto each member trip,
 * marked with the camp that wrote it, so the camp's organiser can hand a partner club the camp's
 * whole fortnight in one act and each trip still answers for itself. That shape sets the three
 * things this dialog has to say plainly: a trip that joins later is *not* covered until somebody
 * re-applies the sharing; the rules can be added to but a subject is only ever taken off by
 * withdrawing everything and applying the rest again; and each trip answers for itself — the act
 * reaches the trips whose permissions the organiser may manage and skips the others, so a camp
 * gathering three clubs' trips is shared for the organiser's own at once and for the rest as
 * their owners delegate.
 *
 * What was skipped is said, because a sharing that silently reached nine trips of ten would be
 * read as reaching ten. The server names the skipped trips the organiser may read and only
 * counts the ones they may not — a camp can gather a trip its organiser cannot open — and this
 * dialog shows exactly that and nothing it worked out for itself.
 */
export default function ExpeditionSharingModal({ expeditionId, open, onClose }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: sharing, isError } = useExpeditionSharing(expeditionId, open);
  const apply = useApplyExpeditionSharing(expeditionId);
  const reapply = useReapplyExpeditionSharing(expeditionId);
  const withdraw = useWithdrawExpeditionSharing(expeditionId);
  const { data: cavingGroups } = useCavingGroups();

  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [subjectKind, setSubjectKind] = useState<'user' | 'cavingGroup'>('cavingGroup');
  const [subjectId, setSubjectId] = useState<string>();
  const [effect, setEffect] = useState<'allow' | 'deny'>('allow');
  const [userQuery, setUserQuery] = useState('');
  const debouncedUserQuery = useDebouncedValue(userQuery);
  const { data: users } = useUserSearch(debouncedUserQuery);

  // What the last apply or re-apply in this dialog did — which trips it skipped. Held here and
  // not in the cache: it is the answer to an act, not a state of the camp, and reading the camp's
  // sharing again does not give it back.
  const [outcome, setOutcome] = useState<ExpeditionSharingOutcome | null>(null);

  const memberTrips = sharing?.memberTrips ?? 0;
  const rules = sharing?.rules ?? [];

  const addDraft = () => {
    if (!subjectId || drafts.some((d) => d.subjectId === subjectId && d.subjectKind === subjectKind && d.effect === effect)) {
      return;
    }
    const name = subjectKind === 'cavingGroup'
      ? cavingGroups?.find((x) => x.id === subjectId)?.name ?? null
      : users?.find((x) => x.id === subjectId)?.label ?? null;
    setDrafts([...drafts, {
      subjectKind,
      subjectId,
      subjectName: name,
      effect,
      actions: new Set<AccessActionFlag>(['read']),
    }]);
    setSubjectId(undefined);
    setUserQuery('');
  };

  const toggleAction = (index: number, flag: AccessActionFlag, checked: boolean) => {
    setDrafts(drafts.map((draft, i) => {
      if (i !== index) {
        return draft;
      }
      const next = new Set(draft.actions);
      if (checked) {
        next.add(flag);
      } else {
        next.delete(flag);
      }
      return { ...draft, actions: next };
    }));
  };

  /**
   * The server's refusals, in words that say what to do about them. The incomplete refusal is
   * the sharing that reached no trip at all; it carries a count and nothing else, by design: a
   * member trip the caller cannot see must not be disclosed by the refusal that mentions it.
   */
  const refusal = (error: unknown): string => {
    if (!(error instanceof ApiError)) {
      return t('common.saveFailed');
    }
    switch (error.code) {
      case 'access_cascade.incomplete': {
        const count = error.problem?.refusedTripCount;
        return t('expeditions.sharing.incomplete', { count: typeof count === 'number' ? count : 0 });
      }
      case 'expedition_sharing.no_trips':
        return t('expeditions.sharing.noTrips');
      case 'expedition_sharing.nothing_shared':
        return t('expeditions.sharing.nothingShared');
      default:
        return t('common.saveFailed');
    }
  };

  /**
   * Says what an act did. "Shared" only when every trip took it; otherwise how many did, as a
   * warning rather than a success, with the trips it skipped listed in the dialog itself — a
   * toast is gone in three seconds and the list is what somebody has to act on.
   */
  const announce = (done: ExpeditionSharingOutcome, act: 'applied' | 'reapplied') => {
    setOutcome(done);
    const skipped = skippedCount(done);
    if (skipped === 0) {
      message.success(t(`expeditions.sharing.${act}`));
    } else {
      message.warning(
        t('expeditions.sharing.inPart', { shared: done.sharedTrips, total: done.sharedTrips + skipped }),
      );
    }
  };

  const onApply = async () => {
    const entries: ExpeditionShareEntry[] = drafts
      .filter((d) => d.actions.size > 0)
      .map((d) => ({
        subjectKind: d.subjectKind,
        subjectId: d.subjectId,
        effect: d.effect,
        actions: joinActions(d.actions),
      }));
    if (entries.length === 0) {
      return;
    }
    try {
      announce(await apply.mutateAsync(entries), 'applied');
      setDrafts([]);
    } catch (error) {
      setOutcome(null);
      message.error(refusal(error));
    }
  };

  const onReapply = async () => {
    try {
      announce(await reapply.mutateAsync(), 'reapplied');
    } catch (error) {
      setOutcome(null);
      message.error(refusal(error));
    }
  };

  const onWithdraw = async () => {
    try {
      await withdraw.mutateAsync();
      // Nothing of the camp's is on any trip now, so what an earlier act skipped is no longer
      // a statement about anything.
      setOutcome(null);
      message.success(t('expeditions.sharing.withdrawn'));
    } catch (error) {
      message.error(refusal(error));
    }
  };

  const close = () => {
    setOutcome(null);
    onClose();
  };

  const busy = apply.isPending || reapply.isPending || withdraw.isPending;
  // Fewer trips carrying a rule than the camp has means trips the sharing does not reach —
  // joined since it was last applied, or skipped when it was — the one state this dialog exists
  // to make visible.
  const behind = rules.some((rule) => rule.trips < memberTrips);
  const skipped = outcome ? skippedCount(outcome) : 0;

  return (
    <Modal
      title={t('expeditions.sharing.title')}
      open={open}
      onCancel={close}
      footer={null}
      width={860}
      destroyOnHidden
    >
      {isError ? (
        <Tag color="warning">{t('expeditions.sharing.notAllowed')}</Tag>
      ) : (
        <>
          <Typography.Paragraph type="secondary">{t('expeditions.sharing.intro')}</Typography.Paragraph>
          <Typography.Paragraph data-testid="expedition-sharing-trips">
            {t('expeditions.sharing.memberTrips', { count: memberTrips })}
          </Typography.Paragraph>

          {behind && skipped === 0 && (
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 12 }}
              title={t('expeditions.sharing.behindHint')}
              data-testid="expedition-sharing-behind"
            />
          )}

          {outcome && skipped > 0 && (
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 12 }}
              title={t('expeditions.sharing.skippedTitle', {
                shared: outcome.sharedTrips,
                skipped,
              })}
              description={
                <>
                  {outcome.skippedTrips.length > 0 && (
                    <ul style={{ margin: 0, paddingInlineStart: 20 }}>
                      {outcome.skippedTrips.map((trip) => (
                        <li key={trip.id} data-testid="expedition-sharing-skipped-trip">
                          <Link to={`/trip-logs/${trip.id}`}>{trip.title}</Link>
                          {' — '}
                          {t(`expeditions.sharing.skippedReason.${trip.reason}`)}
                        </li>
                      ))}
                    </ul>
                  )}
                  {outcome.skippedTripsNotNamed > 0 && (
                    <Typography.Paragraph
                      style={{ marginTop: 8, marginBottom: 0 }}
                      data-testid="expedition-sharing-skipped-unnamed"
                    >
                      {t('expeditions.sharing.skippedNotNamed', {
                        count: outcome.skippedTripsNotNamed,
                      })}
                    </Typography.Paragraph>
                  )}
                  <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
                    {t('expeditions.sharing.skippedHint')}
                  </Typography.Paragraph>
                </>
              }
              data-testid="expedition-sharing-skipped"
            />
          )}

          <Table<ExpeditionSharedRule>
            scroll={{ x: 'max-content' }}
            rowKey={(rule) => `${rule.subjectKind}:${rule.subjectId}:${rule.effect}:${rule.actions}`}
            size="small"
            pagination={false}
            dataSource={rules}
            locale={{ emptyText: t('expeditions.sharing.empty') }}
            data-testid="expedition-sharing-rules"
            columns={[
              {
                title: t('permissions.subject'),
                key: 'subject',
                render: (_, rule) => (
                  <>
                    <Tag>{t(`permissions.${rule.subjectKind}`)}</Tag>
                    {rule.subjectName ?? rule.subjectId}
                  </>
                ),
              },
              {
                title: t('access.effect'),
                key: 'effect',
                width: 90,
                render: (_, rule) => (
                  <Tag color={rule.effect === 'deny' ? 'red' : 'green'}>
                    {t(`access.effects.${rule.effect}`)}
                  </Tag>
                ),
              },
              {
                title: t('expeditions.sharing.actions'),
                key: 'actions',
                render: (_, rule) => actionSetLabels(t, rule.actions).join(', '),
              },
              {
                title: t('expeditions.sharing.coverage'),
                key: 'coverage',
                width: 160,
                render: (_, rule) => (
                  <span data-testid="expedition-sharing-coverage">
                    {t('expeditions.sharing.covered', { trips: rule.trips, total: memberTrips })}
                    {rule.trips < memberTrips && (
                      <Tag color="warning" style={{ marginInlineStart: 8 }}>
                        {t('expeditions.sharing.behind')}
                      </Tag>
                    )}
                  </span>
                ),
              },
            ]}
          />

          <Flex gap={8} wrap style={{ marginTop: 12, marginBottom: 12 }}>
            {/* Re-applying is the act that covers trips which joined since: automatic coverage
                would be a grant nobody performed. Withdrawing takes back exactly what the camp
                wrote, and leaves a rule of the same shape authored on a trip's own tab alone. */}
            <Button
              icon={<ReloadOutlined />}
              loading={reapply.isPending}
              disabled={busy || rules.length === 0}
              onClick={() => void onReapply()}
              data-testid="expedition-sharing-reapply"
            >
              {t('expeditions.sharing.reapply')}
            </Button>
            <Popconfirm
              title={t('expeditions.sharing.withdrawConfirm')}
              onConfirm={() => void onWithdraw()}
              okButtonProps={{ danger: true }}
            >
              <Button
                danger
                icon={<DeleteOutlined />}
                loading={withdraw.isPending}
                disabled={busy || rules.length === 0}
                data-testid="expedition-sharing-withdraw"
              >
                {t('expeditions.sharing.withdraw')}
              </Button>
            </Popconfirm>
          </Flex>

          <Typography.Title level={5} style={{ marginTop: 16 }}>
            {t('expeditions.sharing.addTitle')}
          </Typography.Title>
          <Flex gap={8} style={{ marginBottom: 12 }} wrap>
            <Select
              value={subjectKind}
              style={{ width: 130 }}
              onChange={(kind: 'user' | 'cavingGroup') => {
                setSubjectKind(kind);
                setSubjectId(undefined);
              }}
              options={[
                { value: 'cavingGroup', label: t('permissions.cavingGroup') },
                { value: 'user', label: t('permissions.user') },
              ]}
            />
            {subjectKind === 'cavingGroup' ? (
              <Select
                style={{ flex: 1, minWidth: 180 }}
                showSearch
                optionFilterProp="label"
                placeholder={t('permissions.pickCavingGroup')}
                value={subjectId}
                onChange={setSubjectId}
                options={cavingGroups?.map((group) => ({ value: group.id, label: group.name }))}
              />
            ) : (
              <Select
                style={{ flex: 1, minWidth: 180 }}
                showSearch
                filterOption={false}
                placeholder={t('permissions.pickUser')}
                value={subjectId}
                onSearch={setUserQuery}
                onChange={setSubjectId}
                options={users?.map((user) => ({
                  value: user.id,
                  // The address is only present when the person shares it; the label always is.
                  label: user.email ? `${user.label} (${user.email})` : user.label,
                }))}
                notFoundContent={null}
              />
            )}
            <Select
              value={effect}
              style={{ width: 110 }}
              onChange={setEffect}
              options={[
                { value: 'allow', label: t('access.effects.allow') },
                { value: 'deny', label: <Tag color="red" style={{ marginInlineEnd: 0 }}>{t('access.effects.deny')}</Tag> },
              ]}
            />
            <Button
              icon={<PlusOutlined />}
              onClick={addDraft}
              disabled={!subjectId}
              data-testid="expedition-sharing-add"
            >
              {t('permissions.addRule')}
            </Button>
          </Flex>

          {drafts.length > 0 && (
            <>
              <Table<Draft>
                scroll={{ x: 'max-content' }}
                rowKey={(draft) => `${draft.subjectKind}:${draft.subjectId}:${draft.effect}`}
                size="small"
                pagination={false}
                dataSource={drafts}
                data-testid="expedition-sharing-drafts"
                columns={[
                  {
                    title: t('permissions.subject'),
                    key: 'subject',
                    render: (_, draft) => (
                      <>
                        <Tag>{t(`permissions.${draft.subjectKind}`)}</Tag>
                        {draft.subjectName ?? draft.subjectId}
                      </>
                    ),
                  },
                  {
                    title: t('access.effect'),
                    key: 'effect',
                    width: 90,
                    render: (_, draft) => (
                      <Tag color={draft.effect === 'deny' ? 'red' : 'green'}>
                        {t(`access.effects.${draft.effect}`)}
                      </Tag>
                    ),
                  },
                  ...SHAREABLE.map((flag) => ({
                    title: t(`access.actions.${flag}`),
                    key: flag,
                    width: 84,
                    align: 'center' as const,
                    render: (_: unknown, draft: Draft, index: number) => (
                      <Checkbox
                        checked={draft.actions.has(flag)}
                        onChange={(e) => toggleAction(index, flag, e.target.checked)}
                      />
                    ),
                  })),
                  {
                    title: '',
                    key: 'remove',
                    width: 50,
                    render: (_, _draft, index) => (
                      <Button
                        size="small"
                        type="text"
                        danger
                        icon={<DeleteOutlined />}
                        onClick={() => setDrafts(drafts.filter((_, i) => i !== index))}
                      />
                    ),
                  },
                ]}
              />
              <Flex gap={8} align="center" style={{ marginTop: 12 }}>
                <Button
                  type="primary"
                  icon={<ShareAltOutlined />}
                  loading={apply.isPending}
                  disabled={busy}
                  onClick={() => void onApply()}
                  data-testid="expedition-sharing-apply"
                >
                  {t('expeditions.sharing.apply')}
                </Button>
                <Typography.Text type="secondary">{t('expeditions.sharing.applyHint')}</Typography.Text>
              </Flex>
            </>
          )}
        </>
      )}
    </Modal>
  );
}
