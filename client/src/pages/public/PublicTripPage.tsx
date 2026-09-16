// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState, type CSSProperties, type ReactNode } from 'react';
import { EyeInvisibleOutlined, QuestionCircleOutlined, WarningOutlined } from '@ant-design/icons';
import { Alert, Card, Flex, Result, Spin, Tag, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { usePublicTrip, type PublicTripParticipant } from '../../api/hooks.ts';
import CaveViewPanel from '../../components/caveview/CaveViewPanel.tsx';
import { noStationsMissing } from '../../caveview/placedOnModel.ts';
import { envelopeCrsLookup, publicTrackedCavers } from '../../caveview/publicTrackedCavers.ts';
import { usePublishedStationMedia } from '../../caveview/useStationMedia.ts';
import { unnamedViewerFileName } from '../../caveview/viewerFileName.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import { usePinnedModelUrl } from './pinnedModelUrl.ts';
import {
  partyByTeam,
  partyStandings,
  positionAgeInWords,
  sinceInWords,
  standingOf,
} from './publicTripParty.ts';
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

  // The address the viewer is given: held still while it is the same survey, replaced when the
  // survey itself changes. Both halves matter and the reasoning for each lives with the rule,
  // beside the page that shares it.
  const model = data?.model ?? null;
  const pinnedModelUrl = usePinnedModelUrl(model?.modelUrl, token);

  // The tab a link opens in says which trip it is. Worth doing here and nowhere else in this
  // application: everything else is opened from inside a workspace whose tab is already named,
  // and this is a single page somebody keeps open beside a dozen others while they wait.
  useEffect(() => {
    if (data !== undefined) {
      document.title = data.title;
    }
  }, [data]);

  const crsLookup = useMemo(() => envelopeCrsLookup(model), [model]);

  /**
   * The pictures the envelope carried, shaped for the viewer.
   *
   * <b>Deliberately not pinned the way the model URL above is.</b> The model is pinned because the
   * viewer re-downloads and re-parses it when its address changes, and the camera goes back to the
   * opening view with it — a minute's poll must not do that to somebody watching. A picture is
   * fetched only when a finger lands on the station holding it, which is what makes pinning them
   * the wrong way round: the model URL is spent the instant it arrives and never again, while a
   * picture URL may not be spent for half an hour, by which time a signature good for ten minutes
   * has expired. A pinned set would be a page of broken thumbnails on somebody's website.
   *
   * <b>So they are kept fresh, and kept fresh without disturbing anybody.</b> The re-read that
   * re-signs them is arranged in the query itself, which goes on asking — more slowly — while a
   * published page carries pictures; and what the fresh signatures land on is the map the viewer is
   * already holding rather than a new one, so a strip standing open under somebody's thumb is not
   * closed by a poll they did not make. Both halves live in one place for the two pages that need
   * them.
   */
  const stationMedia = usePublishedStationMedia(model?.pictures);

  const cavers = useMemo(
    () =>
      data === undefined
        ? []
        : publicTrackedCavers(data, (ordinal) => t('publicTrip.caverOrdinal', { ordinal })),
    [data, t],
  );

  /**
   * The stations the drawing on this page turns out not to hold.
   *
   * <b>Read from the viewer and from nowhere else, and it is the one thing on this page the server
   * cannot answer.</b> Everything else here was decided before the envelope was sent: which places
   * may be shown at all, which were measured in another survey. Whether the file this browser
   * parsed contains a station of the name it was given is known only after it is parsed, in this
   * browser, by the viewer that parsed it. Empty until a drawing is loaded, so a phone that has not
   * downloaded the model yet claims nothing about it.
   *
   * Station paths, as the viewer answers them: a name either is one of the drawing's nodes or it is
   * not, and two followers' places that share a name share the answer.
   */
  const [unplacedStations, setUnplacedStations] = useState<ReadonlySet<string>>(noStationsMissing);
  /**
   * Whether this page was given a station and the drawing above holds no node of that name.
   *
   * Two questions in order, exactly as the coordinator's own table asks them. A place measured in
   * another survey is not drawn here at all, so it can never also be missing from a drawing it was
   * never going to be on — and a follower told both things about one person is being told two
   * different stories at once.
   */
  const offModel = (participant: PublicTripParticipant) =>
    !participant.positionOnOtherModel
    && participant.stationName !== null
    && participant.stationName.length > 0
    && unplacedStations.has(participant.stationName);

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

  /**
   * Where somebody was last reported, said as strongly as this page is actually told it, and the
   * moment that placed them there where there is one.
   *
   * <b>Only the branches that draw a place carry a moment.</b> A position nobody reported and one
   * this page may not be told arrive the same way — with no moment at all — and neither may gain
   * an age from anything written later, because there is no age in scope where no place was drawn.
   * The one repair that must never be made is filling that gap from the last word, which would
   * date a station from a radio note made hours after it.
   */
  const positionOf = (
    participant: PublicTripParticipant,
  ): { shown: ReactNode; placedAt: string | null } => {
    // Read before the two absences below, because both of them would be wrong about it and one of
    // them would be the worst sentence this page can produce.
    //
    // <b>Somebody has reported where this person is.</b> The place was measured in a different
    // survey of the cave than the one drawn here — a watch re-pointed at a corrected survey while
    // the party is underground leaves every earlier report naming the survey it was made in — so
    // the server sends the station and the depth as absences, deliberately, and raises this bit to
    // say which kind of absence it is. Without reading it the page falls through to "No position
    // reported" and tells the family of somebody underground that nobody knows where they are,
    // which is false. The drawing beside it already says "on another survey"; this is the same
    // page's other half, and it is the half read on a phone.
    if (participant.positionOnOtherModel) {
      return {
        shown: (
          <Tag icon={<QuestionCircleOutlined />} data-testid="public-trip-position-other-model">
            {t('publicTrip.positionOtherModel')}
          </Tag>
        ),
        // No moment either, and for the reason the server sends none: an hour beside a place this
        // page cannot show would date something a reader can only read as the place beside it.
        placedAt: null,
      };
    }
    // A station this page was given, and the drawing above holds no station of that name.
    //
    // <b>The name stays and is marked, rather than being replaced by a tag.</b> A family reading
    // this is being told where somebody was reported, and that is still true — it is the drawing
    // that cannot show it, because the survey has been re-exported with its stations renamed since
    // the report was made. Dropping the name would take away the one thing this page is for;
    // leaving it unmarked beside a drawing showing nobody would let a reader search that drawing
    // for a dot that was never going to be there.
    if (offModel(participant)) {
      return {
        shown: (
          <>
            {participant.stationName}
            <Tag
              icon={<WarningOutlined />}
              color="warning"
              className="public-trip-position-mark"
              data-testid={`public-trip-position-not-on-model-${participant.ordinal}`}
            >
              {t('publicTrip.positionNotOnModel')}
            </Tag>
          </>
        ),
        placedAt: participant.positionRecordedAt,
      };
    }
    if (participant.stationName !== null && participant.stationName.length > 0) {
      return { shown: participant.stationName, placedAt: participant.positionRecordedAt };
    }
    if (participant.depthM !== null) {
      return {
        shown: t('trips.metres', { value: participant.depthM }),
        placedAt: participant.positionRecordedAt,
      };
    }
    // The absence that can only be a withholding cannot be identified on this surface — the
    // envelope carries no report kind — so the weaker of the two phrasings is the only one used
    // here. Over-claiming would tell a stranger something is being kept from them at the moment
    // a party has merely not set off.
    if (participant.lastRecordedAt !== null && data.positionsWithheld) {
      return {
        shown: (
          <Tag icon={<EyeInvisibleOutlined />} data-testid="public-trip-position-withheld">
            {t('publicTrip.positionMaybeWithheld')}
          </Tag>
        ),
        placedAt: null,
      };
    }
    return { shown: t('publicTrip.positionUnreported'), placedAt: null };
  };

  /**
   * The place, with how long ago it was reported under it.
   *
   * <b>Two ages on this card and they must not read as one.</b> A family follows this page to know
   * whether the party has moved, and "last heard" answers something else entirely — a radio check
   * saying everyone is fine moves it and moves nobody. So the position keeps its own age, inside
   * its own fact, worded rather than bare: a number under "Last reported at" and another under
   * "Last heard" would be two figures on a phone screen that somebody worried is reading quickly.
   */
  const positionFact = (participant: PublicTripParticipant): ReactNode => {
    const { shown, placedAt } = positionOf(participant);
    const since = positionAgeInWords(placedAt, now, i18n.language);
    return (
      <>
        {shown}
        {since !== null && (
          <Typography.Text
            type="secondary"
            className="public-trip-position-age"
            title={when(placedAt)}
            data-testid={`public-trip-position-age-${participant.ordinal}`}
          >
            {t('publicTrip.positionSince', { since })}
          </Typography.Text>
        )}
      </>
    );
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

        {/* Said once for the page as well as once per person, because the tag beside a name is a
            label and this is the explanation of it — and the reader is somebody waiting for a
            party to come out, who needs to know that a place they cannot see is not a place
            nobody knows.

            `title`, like its three siblings above. This one passed `message` under a comment saying
            the library's alert had no `title` prop and that the other three were therefore losing
            their headings to a browser tooltip. That was true of the version it was written
            against and is not true of the one installed: `title` is the prop and `message` is the
            deprecated spelling of it, which this alert was announcing on the console of every
            phone that opened a trip with a place on another survey. */}
        {data.participants.some((participant) => participant.positionOnOtherModel) && (
          <Alert
            type="info"
            showIcon
            title={t('publicTrip.otherModelTitle')}
            description={t('publicTrip.otherModelBody')}
            data-testid="public-trip-other-model"
          />
        )}

        {/* And the one the server could not have warned about, because it is not a fact about the
            report at all: the drawing itself has no station of the name that was reported. Said
            once for the page as well as beside each name, for the same reason its neighbour above
            is — the mark beside a name is a label, and a reader waiting for a party to come out
            needs to be told that a place the drawing cannot show is not a place nobody knows. */}
        {data.participants.some(offModel) && (
          <Alert
            type="warning"
            showIcon
            title={t('publicTrip.notOnModelTitle')}
            description={t('publicTrip.notOnModelBody')}
            data-testid="public-trip-not-on-model"
          />
        )}

        {model !== null && pinnedModelUrl !== null && (
          <div className="public-trip-model">
            <CaveViewPanel
              fileUrl={pinnedModelUrl}
              fileName={unnamedViewerFileName(model.format)}
              // Which stations the viewer could not place a marker at, so the list of people
              // below this one says it in words. The list is the half read on a phone.
              onUnplacedStationsChange={setUnplacedStations}
              // The same share of the screen the signed-in panel reserves, and for the same
              // reason: the viewer takes every gesture that begins inside it, so it must never
              // be the only thing under a thumb. dvh because a phone's address bar collapses.
              height={`min(${narrow ? 300 : 440}px, 60dvh)`}
              trackedCavers={cavers}
              crsLookup={crsLookup}
              toolbar
              // The pictures come from the envelope and from nothing else. The hook the signed-in
              // surfaces share reads the model's links, and that route takes an account — the
              // visitor holding this one token is refused it, as they are refused every other
              // address here — so reaching for it would fire a request that answers 401 where no
              // console is being watched, and then draw exactly what a cave with no pictures
              // draws. What the server sends is already decided: only photographs somebody
              // published, with URLs that reach a rendering and never an upload. Nothing is
              // filtered on the way through, because nothing here could be trusted to.
              stationMedia={stationMedia}
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
                      {fact(t('publicTrip.columnPosition'), positionFact(participant))}
                      {/* The other moment, kept as its own fact under its own label: this one
                          moves when anybody says anything at all about somebody, including a note
                          that says nothing about where they are. */}
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
