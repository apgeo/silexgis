// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Flex, Select, Statistic, Tag, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import type {
  TripImportFeatureMatch,
  TripImportOptions,
  TripImportPersonMatch,
  TripImportProposals,
} from '../../api/hooks.ts';

interface Props {
  proposals: TripImportProposals;
  options: TripImportOptions;
  onChange: (next: TripImportOptions) => void;
}

/**
 * The names a switch cannot deal with, split the two ways they are undecided for.
 *
 * The uncreatable ones are read off `mayCreate`, which the server states per name, and never
 * off `willCreate`. The two look alike on the setting the screen opens in and mean opposite
 * things: with the create switch off nothing will be created, so `willCreate` is false for every
 * name that missed — including the ordinary full names a single switch would create. Deriving
 * the list from it put those names in a red list headed by a count that did not include them,
 * so the count and the list under it contradicted each other on the default setting.
 */
function needingADecision(people: readonly TripImportPersonMatch[]) {
  return {
    uncreatable: people.filter((p) => p.state === 'unmatched' && !p.mayCreate),
  };
}

/**
 * What confirming this review would add, and what it would leave undecided.
 *
 * Every figure is rendered at zero as well as above it. A count that disappears when it is
 * nothing reads, to somebody scanning the screen, exactly like a count that was never there —
 * and "no new cavers" is a different statement from "nobody looked".
 *
 * The two decision figures are the reason this block exists at all. A name matching more than
 * one caver, and a name matching none that cannot be created — a bare initial, a lone given
 * name — both end with the person left off the trip they went on, and both are silent unless
 * something says so before the button is pressed. So the warning is a standing alert above the
 * table rather than a tooltip or a disabled button: a reviewer must be unable to confirm a
 * sheet believing it is fine when it is not.
 *
 * And a name that needs a decision is settled here rather than only reported here. A screen that
 * states a decision is required while offering no way to make it is the same failure the counts
 * exist to prevent, one step further along: the reviewer is told, agrees, and still ends with
 * the person missing from every trip they went on. The choice is offered among exactly the
 * records the name matched, which is also the only set a choice is honoured from.
 */
export default function TripImportSummary({ proposals, options, onChange }: Props) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const caverChoices = options.caverChoices ?? {};
  const featureChoices = options.featureChoices ?? {};

  const { uncreatable } = needingADecision(proposals.people);
  const places = [...proposals.caves, ...proposals.areas];
  const undecidedPeople = proposals.ambiguousPersonCount + proposals.uncreatablePersonCount;

  // A name the reviewer has settled is no longer ambiguous, so the server reports it as matched
  // and it would drop straight out of this list — taking the control that settled it, and any
  // way of seeing or changing the answer, with it. Anything carrying a choice therefore stays.
  const toSettle = <T extends { source: string; state: string }>(
    matches: readonly T[],
    chosen: Record<string, string>,
  ) => matches.filter((m) => m.state === 'ambiguous' || chosen[m.source] !== undefined);

  const peopleToSettle = toSettle(proposals.people, caverChoices);
  const placesToSettle = toSettle(places, featureChoices);

  const choose = (
    key: 'caverChoices' | 'featureChoices',
    source: string,
    id: string | undefined,
  ) => {
    const next = { ...(options[key] ?? {}) };
    if (id === undefined) {
      delete next[source];
    } else {
      next[source] = id;
    }
    onChange({ ...options, [key]: next });
  };

  /** One undecided name beside the records it answered to, as a choice between them. */
  const settler = (
    match: TripImportPersonMatch | TripImportFeatureMatch,
    key: 'caverChoices' | 'featureChoices',
    chosen: Record<string, string>,
  ) => (
    <Flex key={match.source} gap={8} align="center" wrap style={{ marginBottom: 6 }}>
      <Tag color={match.state === 'ambiguous' ? 'gold' : 'green'}>{match.source}</Tag>
      <Select
        allowClear
        size="small"
        style={{ minWidth: 240 }}
        placeholder={t('tripImport.chooseWhich', { count: match.candidates.length })}
        value={chosen[match.source] ?? undefined}
        onChange={(value?: string) => choose(key, match.source, value)}
        data-testid={`trip-import-choose-${key}-${match.source}`}
        options={match.candidates.map((candidate) => ({
          value: candidate.id,
          label: candidate.name,
        }))}
      />
    </Flex>
  );

  return (
    <Card size="small" title={t('tripImport.summaryTitle')} data-testid="trip-import-summary">
      <Flex gap={24} wrap style={{ marginBottom: 8 }}>
        <Statistic title={t('tripImport.newTripTypes')} value={proposals.newTripTypeCount} />
        <Statistic title={t('tripImport.newCavers')} value={proposals.newCaverCount} />
        <Statistic title={t('tripImport.newCaves')} value={proposals.newCaveCount} />
        <Statistic title={t('tripImport.newAreas')} value={proposals.newAreaCount} />
      </Flex>
      <Flex gap={24} wrap>
        <Statistic
          title={t('tripImport.peopleNeedingADecision')}
          value={undecidedPeople}
          styles={undecidedPeople > 0 ? { content: { color: token.colorError } } : undefined}
        />
        <Statistic
          title={t('tripImport.placesNeedingADecision')}
          value={proposals.ambiguousPlaceCount}
          styles={
            proposals.ambiguousPlaceCount > 0 ? { content: { color: token.colorError } } : undefined
          }
        />
      </Flex>

      {(undecidedPeople > 0 || peopleToSettle.length > 0) && (
        <Alert
          type="warning"
          showIcon
          style={{ marginTop: 12 }}
          data-testid="trip-import-people-warning"
          title={t('tripImport.peopleWarningTitle', {
            ambiguous: proposals.ambiguousPersonCount,
            uncreatable: proposals.uncreatablePersonCount,
          })}
          description={
            <>
              <Typography.Paragraph style={{ marginBottom: 8 }}>
                {t('tripImport.peopleWarningBody')}
              </Typography.Paragraph>
              {peopleToSettle.length > 0 && (
                <div data-testid="trip-import-people-choices">
                  {peopleToSettle
                    .slice(0, 20)
                    .map((person) => settler(person, 'caverChoices', caverChoices))}
                </div>
              )}
              {/* The names no choice can settle, because nothing answered to them and nobody can
                  be made from them either. Listed apart from the ones above precisely because
                  there is nothing to pick: run together, the two read as one problem with one
                  remedy, and the remedy does not exist for these. */}
              {uncreatable.length > 0 && (
                <div data-testid="trip-import-people-uncreatable">
                  {uncreatable.slice(0, 20).map((person) => (
                    <Tag key={person.source} color="red">
                      {person.source}
                    </Tag>
                  ))}
                </div>
              )}
              {/* Counted against the list actually shortened, so the total a reviewer can
                  reconstruct from "twenty shown and N more" is the number of names there are. */}
              {peopleToSettle.length > 20 && (
                <Typography.Text type="secondary">
                  {t('tripImport.andMoreNames', { count: peopleToSettle.length - 20 })}
                </Typography.Text>
              )}
            </>
          }
        />
      )}

      {(proposals.ambiguousPlaceCount > 0 || placesToSettle.length > 0) && (
        <Alert
          type="warning"
          showIcon
          style={{ marginTop: 12 }}
          data-testid="trip-import-places-warning"
          title={t('tripImport.placesWarningTitle', { count: proposals.ambiguousPlaceCount })}
          description={
            <div data-testid="trip-import-place-choices">
              {placesToSettle
                .slice(0, 20)
                .map((place) => settler(place, 'featureChoices', featureChoices))}
            </div>
          }
        />
      )}
    </Card>
  );
}
