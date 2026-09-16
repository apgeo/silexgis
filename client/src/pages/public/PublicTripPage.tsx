// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState, type CSSProperties, type ReactNode } from 'react';
import { EyeInvisibleOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import { Alert, Card, Flex, Result, Spin, Tag, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { usePublicTrip, type PublicTripParticipant } from '../../api/hooks.ts';
import CaveViewPanel from '../../components/caveview/CaveViewPanel.tsx';
import { envelopeCrsLookup, publicTrackedCavers } from '../../caveview/publicTrackedCavers.ts';
import { unnamedViewerFileName } from '../../caveview/viewerFileName.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import { partyByTeam, partyStandings, sinceInWords, standingOf } from './publicTripParty.ts';
import './PublicTripPage.css';

/**
 * A tracked trip as somebody without an account follows it.
 *
 * <b>Nothing on this page belongs to the application around it.</b> No navigation, no sign-in
 * offer, no link of any kind — a visitor here was handed one address by somebody they know, and
 * every other address in this installation refuses them. A page that showed them the way in would
 * be showing them a door that is locked, and a page that carried the workspace's chrome would
 * imply they are inside something they are not.
 *
 * <b>Designed at 360px.</b> A live-trip link is opened on a phone, usually by somebody who is not
 * a caver and is checking whether the party is out yet; the desk layout is what the widths above
 * 760px get, and it is the exception here rather than the starting point.
 *
 * <b>Every unusable token is one page.</b> Malformed, unknown, revoked, a cave somebody has since
 * protected, a trip that is gone: the server answers all of them identically, on purpose, because
 * telling them apart is telling a stranger which tokens exist. So there is one failure state here
 * and nothing inspects the refusal to find out which it was.
 *
 * <b>The three states of a party are drawn as three.</b> Underground, out, and nobody has said
 * anything yet — because the person reading this is waiting for the second one, and drawing the
 * third as "not underground" would tell them the party is back before it has set off.
 */
export default function PublicTripPage() {
  const { t, i18n } = useTranslation();
  const { token } = useParams<{ token: string }>();
  const narrow = useIsMobile();
  const { token: antdToken } = theme.useToken();
  const { data, isPending, error } = usePublicTrip(token);

  /**
   * The delivery URL the viewer was given, kept as it was.
   *
   * The URL is signed and expires in about ten minutes, and the only way to get a fresh one is to
   * read the envelope again — which this page does every minute while the party is underground. So
   * a page that simply passed the latest URL through would hand the viewer a different address on
   * every poll, and the viewer reloads on the address: the model would be downloaded and parsed
   * again every minute, and the camera would be thrown back to the view the model opens at, for
   * the whole time somebody sat watching it.
   *
   * Pinning the first one costs nothing, because the viewer fetches exactly once. Whether that
   * address still works an hour later is of no interest to a model that is already in the browser.
   * A first load that fails is recovered by reloading the page, which mints a fresh URL — the same
   * recovery every expired delivery URL in this application has.
   */
  const [pinnedModelUrl, setPinnedModelUrl] = useState<string | null>(null);
  const model = data?.model ?? null;
  useEffect(() => setPinnedModelUrl(null), [token]);
  useEffect(() => {
    if (model !== null) {
      setPinnedModelUrl((current) => current ?? model.modelUrl);
    }
  }, [model]);

  // The tab a link opens in says which trip it is. Worth doing here and nowhere else in this
  // application: everything else is opened from inside a workspace whose tab is already named,
  // and this is a single page somebody keeps open beside a dozen others while they wait.
  useEffect(() => {
    if (data !== undefined) {
      document.title = data.title;
    }
  }, [data]);

  const crsLookup = useMemo(() => envelopeCrsLookup(model), [model]);

  const cavers = useMemo(
    () =>
      data === undefined
        ? []
        : publicTrackedCavers(data, (ordinal) => t('publicTrip.caverOrdinal', { ordinal })),
    [data, t],
  );

  // Antd's tokens reach the stylesheet as custom properties on the page's own root, so the
  // rules below stay readable and the colours still come from the one place they are decided.
  const palette = {
    '--silexgis-public-bg': antdToken.colorBgContainer,
    '--silexgis-public-border': antdToken.colorBorderSecondary,
  } as CSSProperties;

  if (isPending) {
    return (
      <Flex align="center" justify="center" style={{ minHeight: '100dvh' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  // Only when there is nothing to show at all.
  //
  // The distinction this page has to keep is between "this link opens nothing" and "this phone
  // could not ask just now". A follower on a train loses signal for a few seconds, the minute's
  // poll fails, and a page that took the refusal as the answer would replace the party a family
  // is watching with a statement that the link may never have existed — for a minute, until the
  // next poll, and on evidence that says no such thing. The envelope already in hand is still
  // the last true word about where everybody was; it stays on screen, under a notice saying it
  // has stopped being refreshed.
  if (data === undefined) {
    return (
      <Flex align="center" justify="center" style={{ minHeight: '100dvh', padding: 16 }}>
        <Card style={{ maxWidth: 520, width: '100%' }}>
          <Result
            icon={<QuestionCircleOutlined />}
            status="warning"
            title={t('publicTrip.notFoundTitle')}
            subTitle={t('publicTrip.notFoundBody')}
            data-testid="public-trip-not-found"
          />
        </Card>
      </Flex>
    );
  }

  const counts = partyStandings(data.participants);
  const groups = partyByTeam(data.participants, data.teams);
  const now = Date.now();

  const when = (value: string | null) =>
    value === null ? '—' : new Date(value).toLocaleString(i18n.language);

  /** Where somebody was last reported, said as strongly as this page is actually told it. */
  const place = (participant: PublicTripParticipant): ReactNode => {
    if (participant.stationName !== null && participant.stationName.length > 0) {
      return participant.stationName;
    }
    if (participant.depthM !== null) {
      return t('trips.metres', { value: participant.depthM });
    }
    // The absence that can only be a withholding cannot be identified on this surface — the
    // envelope carries no report kind — so the weaker of the two phrasings is the only one used
    // here. Over-claiming would tell a stranger something is being kept from them at the moment
    // a party has merely not set off.
    if (participant.lastRecordedAt !== null && data.positionsWithheld) {
      return (
        <Tag icon={<EyeInvisibleOutlined />} data-testid="public-trip-position-withheld">
          {t('publicTrip.positionMaybeWithheld')}
        </Tag>
      );
    }
    return t('publicTrip.positionUnreported');
  };

  const standingTag = (participant: PublicTripParticipant) => {
    const standing = standingOf(participant);
    return (
      <Tag
        color={standing === 'underground' ? 'blue' : standing === 'out' ? 'green' : 'default'}
        data-testid={`public-trip-standing-${standing}`}
      >
        {t(`publicTrip.standing.${standing}`)}
      </Tag>
    );
  };

  const fact = (label: string, value: ReactNode) => (
    <div key={label}>
      <Typography.Text type="secondary" className="public-trip-fact-label">
        {label}
      </Typography.Text>
      <span className="public-trip-fact-value">{value}</span>
    </div>
  );

  const dates =
    data.tripDateEnd === null || data.tripDateEnd === data.tripDate
      ? formatDate(data.tripDate, i18n.language)
      : `${formatDate(data.tripDate, i18n.language)} – ${formatDate(data.tripDateEnd, i18n.language)}`;

  return (
    <div className="public-trip" style={palette} data-testid="public-trip">
      <header className="public-trip-head">
        <h1 className="public-trip-title" data-testid="public-trip-title">
          {data.title}
        </h1>
        <div className="public-trip-subtitle">
          <Typography.Text type="secondary">{dates}</Typography.Text>
          <Tag
            color={data.state === 'armed' ? 'blue' : 'default'}
            data-testid={`public-trip-state-${data.state}`}
          >
            {t(`publicTrip.state.${data.state}`)}
          </Tag>
        </div>
      </header>

      <main className="public-trip-body">
        <div className="public-trip-standing" data-testid="public-trip-counts">
          {(['underground', 'out', 'unheard'] as const).map((standing) => (
            <div className="public-trip-standing-cell" key={standing}>
              <span className="public-trip-standing-count" data-testid={`public-trip-count-${standing}`}>
                {counts[standing]}
              </span>
              <Typography.Text type="secondary" className="public-trip-standing-label">
                {t(`publicTrip.standing.${standing}`)}
              </Typography.Text>
            </div>
          ))}
        </div>

        {error != null && (
          <Alert
            type="warning"
            showIcon
            title={t('publicTrip.staleTitle')}
            description={t('publicTrip.staleBody')}
            data-testid="public-trip-stale"
          />
        )}

        {data.state !== 'armed' && (
          <Alert
            type="info"
            showIcon
            title={t('publicTrip.closedTitle')}
            description={t('publicTrip.closedBody')}
            data-testid="public-trip-closed"
          />
        )}

        {data.positionsWithheld && (
          <Alert
            type="info"
            showIcon
            title={t('publicTrip.withheldTitle')}
            description={t('publicTrip.withheldBody')}
            data-testid="public-trip-withheld"
          />
        )}

        {model !== null && pinnedModelUrl !== null && (
          <div className="public-trip-model">
            <CaveViewPanel
              fileUrl={pinnedModelUrl}
              fileName={unnamedViewerFileName(model.format)}
              // The same share of the screen the signed-in panel reserves, and for the same
              // reason: the viewer takes every gesture that begins inside it, so it must never
              // be the only thing under a thumb. dvh because a phone's address bar collapses.
              height={`min(${narrow ? 300 : 440}px, 60dvh)`}
              trackedCavers={cavers}
              crsLookup={crsLookup}
              toolbar
              // No `stationMedia`, and it is deliberate rather than an omission. A station's
              // pictures are read from the model's links, and that route takes an account — the
              // visitor holding this one token is refused it, as they are refused every other
              // address here. So the hook the signed-in surfaces share must not be reached for
              // from this page: it would fire a request that answers 401 where no console is being
              // watched, and then draw precisely what a cave with no pictures draws, which is a
              // gap nobody would ever see reported. The published envelope is the only thing this
              // page can read and it carries no pictures today; the day it does, the map is built
              // from it beside the other derivation rather than inside this file.
            />
          </div>
        )}

        <section data-testid="public-trip-party">
          {groups.map((group) => (
            <div key={group.teamId ?? 'no-team'}>
              <h2 className="public-trip-team-title">
                {group.title ?? t('publicTrip.noTeam')}
              </h2>
              <div className="public-trip-people">
                {group.members.map((participant) => (
                  <div
                    className="public-trip-person"
                    key={participant.ordinal}
                    data-testid={`public-trip-caver-${participant.ordinal}`}
                  >
                    <div className="public-trip-person-head">
                      <span className="public-trip-person-name">
                        {participant.label ??
                          t('publicTrip.caverOrdinal', { ordinal: participant.ordinal })}
                      </span>
                      {standingTag(participant)}
                    </div>
                    <div className="public-trip-person-facts">
                      {fact(t('publicTrip.columnPosition'), place(participant))}
                      {fact(
                        t('publicTrip.columnLastHeard'),
                        participant.lastRecordedAt === null ? (
                          '—'
                        ) : (
                          <Typography.Text title={when(participant.lastRecordedAt)}>
                            {sinceInWords(participant.lastRecordedAt, now, i18n.language)}
                          </Typography.Text>
                        ),
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </section>
      </main>

      <footer className="public-trip-foot">
        <Typography.Text type="secondary">{t('app.name')}</Typography.Text>
      </footer>
    </div>
  );
}

/** A calendar date, in the reader's language, without inventing a time of day for it. */
function formatDate(value: string, language: string): string {
  const parts = value.split('-').map(Number);
  const date = new Date(Date.UTC(parts[0], (parts[1] ?? 1) - 1, parts[2] ?? 1));
  return date.toLocaleDateString(language, { timeZone: 'UTC', dateStyle: 'medium' });
}
