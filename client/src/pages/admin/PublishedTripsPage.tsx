// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  Alert,
  App,
  Button,
  Empty,
  Flex,
  Input,
  InputNumber,
  Modal,
  Result,
  Select,
  Space,
  Statistic,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import type { TableProps } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { ApiError } from '../../api/client.ts';
import {
  usePublishedLinks,
  useReplaceTripTrackingShare,
  useRevokeEveryPublishedLink,
  useRevokeTripPublishedLinks,
  type PublishedLink,
  type PublishedLinks,
  type PublishedLinkSort,
  type PublishedLinkStatus,
} from '../../api/hooks.ts';
import FreshFollowLink from '../../components/trips/FreshFollowLink.tsx';
import { trackingProblemMessage } from '../../components/trips/trackingProblems.ts';
import PublishedLinkSettingsNote from './PublishedLinkSettingsNote.tsx';
import { PUBLISHED_LINK_STATUSES } from './publishedLinkStatuses.ts';

/**
 * Colour says one thing only: whether the address opens something for whoever holds it. The three
 * that do are coloured; the three that do not are not, with the one that could start answering
 * again by itself set apart from the two that are simply over.
 */
const statusColor: Record<PublishedLinkStatus, string> = {
  followable: 'green',
  inGrace: 'blue',
  inArchive: 'geekblue',
  withheld: 'gold',
  lapsed: 'default',
  revoked: 'default',
};

/** The columns the server can order by, keyed by the column they are offered on. */
const sortByColumn: Record<string, PublishedLinkSort> = {
  status: 'status',
  trip: 'tripTitle',
  tripDate: 'tripDate',
  createdAt: 'createdAt',
  expiresAt: 'expiresAt',
  watchArmedAt: 'watchArmedAt',
};

const DAY_MS = 24 * 60 * 60 * 1000;

/**
 * The longest stretch, in days, the server lets the list be asked about: ten years. The field is
 * held to it so that a number the server would refuse is never sent.
 */
const MAX_ARMED_DAYS = 3650;

/** How long the answer naming a larger number than the list showed stays up, in seconds. */
const MORE_THAN_LISTED_SECONDS = 12;

/**
 * How many links the list counts as still standing: every status but the one that says a link was
 * taken back. <b>The list's figure, not the installation's</b> — the list hides the links of
 * deleted trips, so withdrawing everything can take back more than this.
 */
function standingLinks(counts: PublishedLinks['counts'] | undefined): number {
  return PUBLISHED_LINK_STATUSES.filter((each) => each !== 'revoked').reduce(
    (sum, each) => sum + (counts?.find((count) => count.status === each)?.count ?? 0),
    0,
  );
}

/** What is waiting for somebody to say they mean it. */
type Pending =
  | { kind: 'replace'; link: PublishedLink }
  | { kind: 'trip'; link: PublishedLink }
  | { kind: 'everything' }
  | null;

/**
 * Everything this installation has published, for the people who answer for the installation.
 *
 * <b>Every status on this page is the server's.</b> What a link opens depends on its watch, on a
 * grace window and a retention that are installation settings, and on whether its cave may be
 * published at all — all of which the published pages themselves are served by. A status worked
 * out here from the dates in a row would sooner or later describe a page that answers
 * differently, so the page prints the word it was given and the instant that word was decided
 * at. The settings are printed beside the list, once, so that "just closed" and "in the archive"
 * can be read as periods; they are printed and nothing is computed from them.
 *
 * <b>The three acts each say what goes with them before they are done.</b> Taking links back is
 * not only "the page stops answering": a finished trip whose last link is gone also leaves its
 * cave's public list of past trips, and comes back only by starting its watch again. That is the
 * consequence nobody guesses, so each confirmation spells it out — and withdrawing everything,
 * which cannot be narrowed afterwards, also asks for a word to be typed.
 *
 * <b>A replaced link's address is shown once, in a dialog that a stray click cannot close.</b>
 * The server keeps a hash and nothing else; an address dismissed before it was copied is gone,
 * and the old one already answers nothing.
 *
 * <b>Tracking left running is shown, never closed from here.</b> Nothing closes a trip's tracking
 * but a person, on purpose, so one that somebody forgot runs for ever: its party stays on the
 * published page as still underground, or — once the link has run out — is in no public list at
 * all. The page says when each was started and how long it has run, can be narrowed to those
 * running longer than a number of days the reader chooses, and sends the reader to the trip's own
 * tracking tab, where closing it is the coordinator's act with the coordinator's confirmation.
 *
 * <b>The address line is a mirror, not a setting.</b> It prints the address the server counted
 * this very request under. Somebody who does not recognise it as their own has found a proxy
 * count that is too low — which fails in no other visible way.
 */
export default function PublishedTripsPage() {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [status, setStatus] = useState<PublishedLinkStatus | undefined>();
  const [armedLongerThanDays, setArmedLongerThanDays] = useState<number | undefined>();
  const [sort, setSort] = useState<PublishedLinkSort | undefined>();
  const [descending, setDescending] = useState<boolean | undefined>();
  const [pending, setPending] = useState<Pending>(null);
  const [typed, setTyped] = useState('');
  const [replaced, setReplaced] = useState<{
    token: string;
    expiresAt: string;
    tripTitle: string;
    protectedCaveWithinSurveyBounds: boolean;
  } | null>(null);

  const links = usePublishedLinks({
    page,
    pageSize,
    status,
    armedLongerThanDays,
    sort,
    descending,
  });
  const replace = useReplaceTripTrackingShare();
  const revokeTrip = useRevokeTripPublishedLinks();
  const revokeEverything = useRevokeEveryPublishedLink();

  const language = i18n.resolvedLanguage;
  const moment = (value: string) => new Date(value).toLocaleString(language);
  // A day as the club wrote it down, with no hour to shift it across midnight in another zone.
  const day = (value: string) => new Date(`${value}T00:00:00`).toLocaleDateString(language);

  const word = t('publishedTrips.everything.word');
  const wordTyped = typed.trim().toLocaleUpperCase(language) === word.toLocaleUpperCase(language);

  const dismiss = () => {
    setPending(null);
    setTyped('');
  };

  const confirmReplace = async (link: PublishedLink) => {
    try {
      const fresh = await replace.mutateAsync({ tripLogId: link.tripLogId, shareId: link.id });
      setReplaced({
        token: fresh.token,
        expiresAt: fresh.expiresAt,
        tripTitle: link.tripTitle,
        protectedCaveWithinSurveyBounds: fresh.protectedCaveWithinSurveyBounds,
      });
    } catch (error) {
      message.error(trackingProblemMessage(error, t));
    } finally {
      dismiss();
    }
  };

  const confirmTrip = async (link: PublishedLink) => {
    try {
      const done = await revokeTrip.mutateAsync({ tripLogId: link.tripLogId });
      message.success(t('publishedTrips.trip.done', { links: done.revokedLinks }));
    } catch {
      message.error(t('publishedTrips.failed'));
    } finally {
      dismiss();
    }
  };

  const confirmEverything = async () => {
    // What the list showed when the act was confirmed, kept before the answer redraws it.
    const listed = standingLinks(links.data?.counts);
    try {
      const done = await revokeEverything.mutateAsync();
      if (done.revokedLinks > listed) {
        // More was taken back than the list showed, and the list is where an administrator would
        // look to check the figure. The act also withdraws the links of deleted trips, which the
        // list leaves out, so the answer says both numbers and why they may differ — and stays up
        // long enough to be read, being three figures and a sentence.
        message.success(
          t('publishedTrips.everything.doneMoreThanListed', {
            links: done.revokedLinks,
            trips: done.trips,
            listed,
          }),
          MORE_THAN_LISTED_SECONDS,
        );
      } else {
        message.success(
          t('publishedTrips.everything.done', { links: done.revokedLinks, trips: done.trips }),
        );
      }
    } catch {
      message.error(t('publishedTrips.failed'));
    } finally {
      dismiss();
    }
  };

  const heading = (
    <>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {t('publishedTrips.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary">{t('publishedTrips.intro')}</Typography.Paragraph>
    </>
  );

  // Refused is the server's word, not this page's guess from a list of groups: the list crosses
  // every trip's own access rules, and who may read it is decided where it is served. Said as its
  // own state rather than as a failure, because nothing failed and trying again changes nothing.
  if (links.error instanceof ApiError && links.error.status === 403) {
    return (
      <div style={{ padding: 24 }} data-testid="published-trips-refused">
        {heading}
        <Result
          status="403"
          title={t('publishedTrips.refused.title')}
          subTitle={t('publishedTrips.refused.text')}
        />
      </div>
    );
  }

  if (links.error != null && links.data === undefined) {
    return (
      <div style={{ padding: 24 }}>
        {heading}
        <Alert
          type="error"
          showIcon
          title={t('publishedTrips.unavailable')}
          action={<Button onClick={() => void links.refetch()}>{t('publishedTrips.refresh')}</Button>}
          data-testid="published-trips-unavailable"
        />
      </div>
    );
  }

  const data = links.data;
  const countOf = (wanted: PublishedLinkStatus) =>
    data?.counts.find((each) => each.status === wanted)?.count ?? 0;
  const standing = standingLinks(data?.counts);
  const anyWarned = (data?.items ?? []).some((item) => item.protectedCaveWithinSurveyBounds);
  // Whole days between two instants the server gave: when the tracking was started, and the one
  // moment every status in this answer was decided at. This browser's clock is not consulted.
  const daysRunning = (armedAt: string) =>
    data === undefined
      ? 0
      : Math.max(0, Math.floor((Date.parse(data.asOf) - Date.parse(armedAt)) / DAY_MS));
  const longRunningLabel = t('publishedTrips.longRunning.label');

  const onTableChange: TableProps<PublishedLink>['onChange'] = (pagination, _filters, sorter) => {
    setPage(pagination.current ?? 1);
    setPageSize(pagination.pageSize ?? 25);
    const single = Array.isArray(sorter) ? sorter[0] : sorter;
    const named = single?.order ? sortByColumn[String(single.columnKey)] : undefined;
    setSort(named);
    setDescending(named === undefined ? undefined : single?.order === 'descend');
  };

  return (
    <div style={{ padding: 24 }} data-testid="published-trips">
      {heading}

      <Flex gap={32} wrap style={{ marginBottom: 16 }} data-testid="published-trips-counts">
        {PUBLISHED_LINK_STATUSES.map((each) => (
          <Statistic
            key={each}
            title={t(`publishedTrips.statuses.${each}`)}
            value={countOf(each)}
            loading={data === undefined}
            data-testid={`published-trips-count-${each}`}
          />
        ))}
      </Flex>

      {data !== undefined && (
        <Typography.Paragraph type="secondary" data-testid="published-trips-notes">
          {t('publishedTrips.asOf', { when: moment(data.asOf) })}{' '}
          {/* Two whole calls rather than one over a chosen key: the check that every key the code
              asks for exists reads them out of the source text. */}
          {data.publishesRealNames
            ? t('publishedTrips.namesShown')
            : t('publishedTrips.namesHidden')}
          {!data.archiveEnabled && <> {t('publishedTrips.archiveOff')}</>}
        </Typography.Paragraph>
      )}

      {data !== undefined && (
        <PublishedLinkSettingsNote settings={data.settings} archiveEnabled={data.archiveEnabled} />
      )}

      {data !== undefined && (
        <Typography.Paragraph type="secondary" data-testid="published-trips-seen-from">
          {data.seenFrom === null ? (
            t('publishedTrips.seenFromUnknown')
          ) : (
            <>
              {t('publishedTrips.seenFrom')}{' '}
              <Typography.Text code data-testid="published-trips-seen-from-address">
                {data.seenFrom}
              </Typography.Text>{' '}
              {t('publishedTrips.seenFromExplain')}
            </>
          )}
        </Typography.Paragraph>
      )}

      {/* The explanation in plain sight rather than only behind the tag: the tag says "look", and
          what it can and cannot have seen is the whole of what makes it safe to act on. */}
      {anyWarned && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title={t('publishedTrips.bounds.title')}
          description={t('publishedTrips.bounds.explain')}
          data-testid="published-trips-bounds-explain"
        />
      )}

      <Flex gap={8} align="center" wrap justify="space-between" style={{ marginBottom: 12 }}>
        <Space wrap>
          <Select<PublishedLinkStatus | undefined>
            allowClear
            style={{ minWidth: 220 }}
            placeholder={t('publishedTrips.anyStatus')}
            value={status}
            onChange={(value) => {
              setStatus(value);
              setPage(1);
            }}
            options={PUBLISHED_LINK_STATUSES.map((value) => ({
              value,
              label: t(`publishedTrips.statuses.${value}`),
            }))}
            data-testid="published-trips-status-filter"
          />
          <Space size={4}>
            <Typography.Text>{longRunningLabel}</Typography.Text>
            <InputNumber
              min={0}
              max={MAX_ARMED_DAYS}
              precision={0}
              style={{ width: 96 }}
              placeholder={t('publishedTrips.longRunning.any')}
              aria-label={longRunningLabel}
              value={armedLongerThanDays ?? null}
              onChange={(value) => {
                // Emptied is "no such question". A number the server would refuse is not sent:
                // the field holds it back to its bounds when it is left.
                if (value === null) setArmedLongerThanDays(undefined);
                else if (
                  Number.isInteger(value) &&
                  value >= 0 &&
                  value <= MAX_ARMED_DAYS
                )
                  setArmedLongerThanDays(value);
                else return;
                setPage(1);
              }}
              data-testid="published-trips-long-running-filter"
            />
          </Space>
          <Button onClick={() => void links.refetch()} loading={links.isFetching && !!data}>
            {t('publishedTrips.refresh')}
          </Button>
        </Space>
        <Button
          danger
          // Nothing standing in the list means nothing a reader could open that this act would
          // take back; offered anyway it would mostly be a confirmation of the application's one
          // irreversible act that then does nothing. The list leaves out the links of deleted
          // trips, which the act does withdraw, so with only those standing the act is not on
          // offer here: they answer nobody while their trip is deleted.
          disabled={standing === 0}
          onClick={() => setPending({ kind: 'everything' })}
          data-testid="published-trips-revoke-everything"
        >
          {t('publishedTrips.actions.unpublishEverything')}
        </Button>
      </Flex>

      <Table<PublishedLink>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="small"
        loading={links.isPending}
        dataSource={data?.items}
        onChange={onTableChange}
        locale={{
          emptyText: (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={
                armedLongerThanDays !== undefined
                  ? status === undefined
                    ? t('publishedTrips.emptyLongRunning')
                    : t('publishedTrips.emptyBoth')
                  : status === undefined
                    ? t('publishedTrips.empty')
                    : t('publishedTrips.emptyFiltered')
              }
            />
          ),
        }}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
        }}
        columns={[
          {
            title: t('publishedTrips.columns.status'),
            key: 'status',
            sorter: true,
            render: (_: unknown, link) => (
              <Space size={4} wrap>
                <Tooltip title={t(`publishedTrips.statusHelp.${link.status}`)}>
                  <Tag
                    color={statusColor[link.status]}
                    data-testid={`published-trips-status-${link.id}`}
                  >
                    {t(`publishedTrips.statuses.${link.status}`)}
                  </Tag>
                </Tooltip>
                {link.protectedCaveWithinSurveyBounds && (
                  <Tooltip title={t('publishedTrips.bounds.explain')}>
                    <Tag color="orange" data-testid={`published-trips-bounds-${link.id}`}>
                      {t('publishedTrips.bounds.tag')}
                    </Tag>
                  </Tooltip>
                )}
              </Space>
            ),
          },
          {
            title: t('publishedTrips.columns.trip'),
            key: 'trip',
            sorter: true,
            render: (_: unknown, link) => (
              <Link to={`/trip-logs/${link.tripLogId}`}>{link.tripTitle}</Link>
            ),
          },
          {
            title: t('publishedTrips.columns.tripDate'),
            key: 'tripDate',
            sorter: true,
            render: (_: unknown, link) =>
              link.tripDateEnd && link.tripDateEnd !== link.tripDate
                ? `${day(link.tripDate)} – ${day(link.tripDateEnd)}`
                : day(link.tripDate),
          },
          {
            title: t('publishedTrips.columns.cave'),
            key: 'cave',
            render: (_: unknown, link) =>
              link.cave === null ? (
                <Typography.Text type="secondary">{t('publishedTrips.noCave')}</Typography.Text>
              ) : (
                <Link to={`/caves/${link.cave.id}`}>
                  {link.cave.name ?? t('publishedTrips.unnamedCave')}
                </Link>
              ),
          },
          {
            // The name an account may be shown under, never its address.
            title: t('publishedTrips.columns.publishedBy'),
            key: 'publishedBy',
            render: (_: unknown, link) =>
              link.createdByLabel ?? (
                <Typography.Text type="secondary">
                  {t('publishedTrips.unknownPublisher')}
                </Typography.Text>
              ),
          },
          {
            title: t('publishedTrips.columns.createdAt'),
            key: 'createdAt',
            sorter: true,
            render: (_: unknown, link) => moment(link.createdAt),
          },
          {
            title: t('publishedTrips.columns.expiresAt'),
            key: 'expiresAt',
            sorter: true,
            render: (_: unknown, link) =>
              link.revokedAt === null
                ? moment(link.expiresAt)
                : t('publishedTrips.revokedAt', { when: moment(link.revokedAt) }),
          },
          {
            title: (
              <Tooltip title={t('publishedTrips.columns.watchArmedAtHelp')}>
                <span>{t('publishedTrips.columns.watchArmedAt')}</span>
              </Tooltip>
            ),
            key: 'watchArmedAt',
            sorter: true,
            render: (_: unknown, link) => {
              if (link.watchArmedAt === null) {
                return (
                  <Typography.Text type="secondary">
                    {t('publishedTrips.watch.unknown')}
                  </Typography.Text>
                );
              }
              if (link.watchState !== 'armed') {
                // Over: when it was started is history, and there is nothing left to close.
                return (
                  <Flex vertical>
                    <Typography.Text type="secondary">{moment(link.watchArmedAt)}</Typography.Text>
                    {link.watchClosedAt !== null && (
                      <Typography.Text type="secondary">
                        {t('publishedTrips.watch.closed', { when: moment(link.watchClosedAt) })}
                      </Typography.Text>
                    )}
                  </Flex>
                );
              }
              return (
                <Flex vertical data-testid={`published-trips-running-${link.id}`}>
                  <span>{moment(link.watchArmedAt)}</span>
                  <span>
                    {t('publishedTrips.watch.running', { days: daysRunning(link.watchArmedAt) })}
                  </span>
                  {/* Running with a link that ran out: followed by nobody and in no list of past
                      trips either, which is exactly the tracking nobody notices was left on. */}
                  {link.status === 'lapsed' && (
                    <Typography.Text type="warning">
                      {t('publishedTrips.watch.runningLapsed')}
                    </Typography.Text>
                  )}
                  {/* Closing is done where the coordinator does it, with what that asks. */}
                  <Link
                    to={`/trip-logs/${link.tripLogId}?tab=tracking`}
                    data-testid={`published-trips-watch-${link.id}`}
                  >
                    {t('publishedTrips.watch.open')}
                  </Link>
                </Flex>
              );
            },
          },
          {
            title: (
              <Tooltip title={t('publishedTrips.columns.handleHelp')}>
                <span>{t('publishedTrips.columns.handle')}</span>
              </Tooltip>
            ),
            key: 'handle',
            render: (_: unknown, link) => <Typography.Text code>{link.handle}</Typography.Text>,
          },
          {
            // No heading: the only things in this column are buttons that name themselves.
            title: '',
            key: 'action',
            render: (_: unknown, link) =>
              // A link that was taken back has nothing left to exchange or withdraw. Every other
              // status keeps both, the ones that open nothing included: a lapsed or withheld link
              // can start answering again when its watch or its cave changes, and that is exactly
              // the one somebody may want gone before it does.
              link.revokedAt === null ? (
                <Space size={4} wrap>
                  <Button
                    size="small"
                    onClick={() => setPending({ kind: 'replace', link })}
                    data-testid={`published-trips-replace-${link.id}`}
                  >
                    {t('publishedTrips.actions.replace')}
                  </Button>
                  <Button
                    size="small"
                    danger
                    onClick={() => setPending({ kind: 'trip', link })}
                    data-testid={`published-trips-revoke-trip-${link.id}`}
                  >
                    {t('publishedTrips.actions.unpublishTrip')}
                  </Button>
                </Space>
              ) : null,
          },
        ]}
      />

      <Modal
        open={pending?.kind === 'replace'}
        title={t('publishedTrips.replace.title')}
        okText={t('publishedTrips.actions.replace')}
        cancelText={t('common.cancel')}
        confirmLoading={replace.isPending}
        onCancel={dismiss}
        onOk={() => pending?.kind === 'replace' && void confirmReplace(pending.link)}
        destroyOnHidden
        data-testid="published-trips-replace-confirm"
      >
        <Typography.Paragraph strong>
          {pending?.kind === 'replace' ? pending.link.tripTitle : ''}
        </Typography.Paragraph>
        <Typography.Paragraph>{t('publishedTrips.replace.stops')}</Typography.Paragraph>
        <Typography.Paragraph style={{ marginBottom: 0 }}>
          {t('publishedTrips.replace.keeps')}
        </Typography.Paragraph>
      </Modal>

      <Modal
        open={pending?.kind === 'trip'}
        title={t('publishedTrips.trip.title')}
        okText={t('publishedTrips.actions.unpublishTrip')}
        okButtonProps={{ danger: true }}
        cancelText={t('common.cancel')}
        confirmLoading={revokeTrip.isPending}
        onCancel={dismiss}
        onOk={() => pending?.kind === 'trip' && void confirmTrip(pending.link)}
        destroyOnHidden
        data-testid="published-trips-revoke-trip-confirm"
      >
        <Typography.Paragraph strong>
          {pending?.kind === 'trip' ? pending.link.tripTitle : ''}
        </Typography.Paragraph>
        <Typography.Paragraph>{t('publishedTrips.trip.stops')}</Typography.Paragraph>
        <Typography.Paragraph>{t('publishedTrips.trip.leavesHistory')}</Typography.Paragraph>
        <Typography.Paragraph strong style={{ marginBottom: 0 }}>
          {t('publishedTrips.irreversible')}
        </Typography.Paragraph>
      </Modal>

      <Modal
        open={pending?.kind === 'everything'}
        title={t('publishedTrips.everything.title')}
        okText={t('publishedTrips.actions.unpublishEverything')}
        // Dead until the word is there. The server asks for a word of its own as well; this one
        // is for the person, in their language, and is what makes the press a decision.
        okButtonProps={{ danger: true, disabled: !wordTyped }}
        cancelText={t('common.cancel')}
        confirmLoading={revokeEverything.isPending}
        onCancel={dismiss}
        onOk={() => wordTyped && void confirmEverything()}
        destroyOnHidden
        data-testid="published-trips-revoke-everything-confirm"
      >
        <Typography.Paragraph>
          {t('publishedTrips.everything.stops', { links: standing })}
        </Typography.Paragraph>
        {/* The figure above is the list's, and the act reaches further than the list: said here,
            before the press, so that a larger number in the answer is not a surprise. */}
        <Typography.Paragraph data-testid="published-trips-revoke-everything-deleted">
          {t('publishedTrips.everything.deletedToo')}
        </Typography.Paragraph>
        <Typography.Paragraph>{t('publishedTrips.everything.leavesHistory')}</Typography.Paragraph>
        <Typography.Paragraph strong>{t('publishedTrips.irreversible')}</Typography.Paragraph>
        <Typography.Paragraph>
          {t('publishedTrips.everything.typeWord', { word })}
        </Typography.Paragraph>
        <Input
          value={typed}
          onChange={(event) => setTyped(event.target.value)}
          autoComplete="off"
          aria-label={t('publishedTrips.everything.typeWord', { word })}
          data-testid="published-trips-revoke-everything-word"
        />
      </Modal>

      <Modal
        open={replaced !== null}
        title={t('publishedTrips.replaced.title')}
        // One way out, and it says what the reader is asserting by taking it. No X, no Escape and
        // no click beside the dialog: the address behind it cannot be shown again.
        closable={false}
        keyboard={false}
        mask={{ closable: false }}
        footer={
          <Button
            type="primary"
            onClick={() => setReplaced(null)}
            data-testid="published-trips-replaced-done"
          >
            {t('publishedTrips.replaced.done')}
          </Button>
        }
        destroyOnHidden
        data-testid="published-trips-replaced"
      >
        {replaced !== null && (
          <Flex vertical gap={8}>
            <Typography.Text strong>{replaced.tripTitle}</Typography.Text>
            <Typography.Text strong>{t('publishedTrips.replaced.once')}</Typography.Text>
            <Typography.Text>
              {t('publishedTrips.replaced.expires', { when: moment(replaced.expiresAt) })}
            </Typography.Text>
            {replaced.protectedCaveWithinSurveyBounds && (
              <Alert
                type="warning"
                showIcon
                title={t('publishedTrips.bounds.title')}
                description={t('publishedTrips.bounds.explain')}
              />
            )}
            <FreshFollowLink token={replaced.token} testId="published-trips-replaced" />
          </Flex>
        )}
      </Modal>
    </div>
  );
}
