// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  Card,
  Col,
  Collapse,
  Form,
  InputNumber,
  Radio,
  Row,
  Select,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  useSurveyModel,
  useSurveyModels,
  type SpeleolocImportOptions,
  type SpeleolocRecording,
} from '../../api/hooks.ts';
import type { SelectorScope } from '../../filters/selection.ts';
import ObjectSelector from '../selector/ObjectSelector.tsx';
import {
  clampCandidateCount,
  MAX_STATION_CANDIDATES,
  MIN_STATION_CANDIDATES,
} from './speleolocSelection.ts';

const TRIP_SCOPES: readonly SelectorScope[] = [
  { id: 'trips', labelKey: 'filters.scopes.trips', world: 'tripLog', where: null },
];

/** A survey model belongs to a cave, so the cave is the only kind of feature worth offering. */
const CAVE_SCOPES: readonly SelectorScope[] = [
  { id: 'caves', labelKey: 'filters.scopes.caves', world: 'feature', where: { field: 'kind', value: 'cave' } },
];

interface Props {
  options: SpeleolocImportOptions;
  onChange: (next: SpeleolocImportOptions) => void;
  /** What the archive holds. Empty until it has been read once, or when it could not be read. */
  recordings: readonly SpeleolocRecording[];
  recordingsLoading: boolean;
  /**
   * Set when the review is of one recording already kept on the server, so there is nothing to
   * choose: the recording is named instead of offered, with the facts the archive chooser would
   * show for it.
   */
  lockedRecording?: {
    title: string;
    startedAt: string;
    endedAt: string | null;
    scans: number;
    documents: number;
  };
}

/**
 * The three choices a recording import makes before any scan means anything: which recording out
 * of the archive, where its positions go, and which survey they are positions in.
 *
 * None of them is guessed. An archive is a copy of a whole phone database and carries every trip
 * that phone ever recorded, so naming one is the first act; a position is a station of a survey
 * model, so nothing can be proposed before a model is named; and putting a history onto a trip is
 * a claim about that trip, so the trip is either named or created here and never inferred from
 * what the recording says.
 *
 * The survey is reached through its cave because that is the read the server publishes — models
 * are listed per cave — and a cave whose exact position this account may not be told lists no
 * models at all, which is what "there is nothing here for you" looks like rather than an error.
 */
export default function SpeleolocImportOptionsPanel({
  options,
  onChange,
  recordings,
  recordingsLoading,
  lockedRecording,
}: Props) {
  const { t, i18n } = useTranslation();
  const cavingGroups = useCavingGroups();

  // The cave is not one of the stored choices — the model is — so a resumed review learns its cave
  // from the model it already names rather than asking the reviewer to find it again.
  const chosenModel = useSurveyModel(options.surveyModelId ?? undefined);
  const [caveId, setCaveId] = useState<string | undefined>(undefined);
  const effectiveCaveId = caveId ?? chosenModel.data?.caveId;
  const models = useSurveyModels(effectiveCaveId);

  const set = <K extends keyof SpeleolocImportOptions>(key: K, value: SpeleolocImportOptions[K]) =>
    onChange({ ...options, [key]: value });

  const when = (value: string | null) =>
    value ? new Date(value).toLocaleString(i18n.language) : '—';

  const recordingOptions = recordings.map((recording) => ({
    value: recording.id,
    label: `${recording.title} · ${when(recording.startedAt)} · ${t('speleolocImport.scanCount', { count: recording.pointCount })}`,
  }));

  const chosenRecording = recordings.find((recording) => recording.id === options.tripUuid);

  return (
    <Card size="small" styles={{ body: { paddingBlock: 8 } }} data-testid="speleoloc-import-options">
      <Collapse
        ghost
        defaultActiveKey={['recording', 'destination', 'survey']}
        items={[
          {
            key: 'recording',
            label: t('speleolocImport.optionsRecording'),
            children: (
              <Row gutter={[16, 8]}>
                {lockedRecording ? (
                  <Col xs={24}>
                    <div data-testid="speleoloc-import-recording-locked">
                      <Typography.Text strong>{lockedRecording.title}</Typography.Text>
                      <br />
                      <Typography.Text type="secondary">
                        {t('speleolocImport.recordingFacts', {
                          started: when(lockedRecording.startedAt),
                          ended: when(lockedRecording.endedAt),
                          scans: lockedRecording.scans,
                          documents: lockedRecording.documents,
                        })}
                      </Typography.Text>
                    </div>
                  </Col>
                ) : (
                  <>
                    <Col xs={24}>
                      <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
                        {t('speleolocImport.recordingHint')}
                      </Typography.Paragraph>
                    </Col>
                    <Col xs={24}>
                      <Form.Item label={t('speleolocImport.recording')} style={{ marginBottom: 8 }}>
                        <Select
                          allowClear
                          showSearch
                          optionFilterProp="label"
                          loading={recordingsLoading}
                          placeholder={t('speleolocImport.recordingPlaceholder')}
                          value={options.tripUuid ?? undefined}
                          onChange={(value?: string) => set('tripUuid', value ?? null)}
                          data-testid="speleoloc-import-recording"
                          options={recordingOptions}
                        />
                      </Form.Item>
                    </Col>
                    {chosenRecording && (
                      <Col xs={24}>
                        <div data-testid="speleoloc-import-recording-facts">
                          {chosenRecording.caveTitle && <Tag>{chosenRecording.caveTitle}</Tag>}
                          <Typography.Text type="secondary">
                            {t('speleolocImport.recordingFacts', {
                              started: when(chosenRecording.startedAt),
                              ended: when(chosenRecording.endedAt),
                              scans: chosenRecording.pointCount,
                              documents: chosenRecording.documentCount,
                            })}
                          </Typography.Text>
                        </div>
                      </Col>
                    )}
                  </>
                )}
              </Row>
            ),
          },
          {
            key: 'destination',
            label: t('speleolocImport.optionsDestination'),
            children: (
              <Row gutter={[16, 8]}>
                <Col xs={24}>
                  <Radio.Group
                    value={options.createTrip ? 'new' : 'existing'}
                    onChange={(e) =>
                      // Never both: the server refuses a request naming a trip and asking for one,
                      // so choosing one side clears the other here rather than at the button.
                      onChange({
                        ...options,
                        createTrip: e.target.value === 'new',
                        tripLogId: e.target.value === 'new' ? null : options.tripLogId,
                      })
                    }
                    data-testid="speleoloc-import-destination"
                    options={[
                      { value: 'existing', label: t('speleolocImport.destinationExisting') },
                      { value: 'new', label: t('speleolocImport.destinationNew') },
                    ]}
                  />
                </Col>
                {!options.createTrip && (
                  <Col xs={24}>
                    <Form.Item label={t('speleolocImport.trip')} style={{ marginBottom: 8 }}>
                      {/* Opened empty, it lists the most recent trips first: the trip a recording
                          belongs to is nearly always one just walked, and this list is no more than
                          the trip register already shows the same account. A stored destination is
                          named by the filter model's resolve, so a review resumed a week later
                          still shows its trip rather than an identifier. Whether this account may
                          put a history onto the trip is the server's decision on import. */}
                      <ObjectSelector
                        scopes={TRIP_SCOPES}
                        presentation="dropdown"
                        allowClear
                        browseOnEmpty
                        pageSize={10}
                        sorts={['occurred']}
                        placeholder={t('speleolocImport.tripPlaceholder')}
                        aria-label={t('speleolocImport.trip')}
                        value={options.tripLogId ?? null}
                        onChange={(value) => set('tripLogId', typeof value === 'string' ? value : null)}
                        data-testid="speleoloc-import-trip"
                      />
                    </Form.Item>
                    <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
                      {t('speleolocImport.tripHint')}
                    </Typography.Paragraph>
                  </Col>
                )}
                {options.createTrip && (
                  <>
                    <Col xs={24}>
                      <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
                        {t('speleolocImport.createTripHint')}
                      </Typography.Paragraph>
                    </Col>
                    <Col xs={24} md={12}>
                      <Form.Item label={t('speleolocImport.visibility')} style={{ marginBottom: 8 }}>
                        <Select
                          value={options.visibility ?? 'private'}
                          onChange={(value) => set('visibility', value)}
                          data-testid="speleoloc-import-visibility"
                          options={(
                            ['private', 'cavingGroup', 'authenticated', 'public'] as const
                          ).map((value) => ({ value, label: t(`caves.visibilityValues.${value}`) }))}
                        />
                      </Form.Item>
                    </Col>
                    <Col xs={24} md={12}>
                      <Form.Item label={t('speleolocImport.cavingGroup')} style={{ marginBottom: 8 }}>
                        <Select
                          allowClear
                          showSearch
                          optionFilterProp="label"
                          loading={cavingGroups.isLoading}
                          value={options.cavingGroupId ?? undefined}
                          onChange={(value?: string) => set('cavingGroupId', value ?? null)}
                          data-testid="speleoloc-import-caving-group"
                          options={(cavingGroups.data ?? []).map((group) => ({
                            value: group.id,
                            label: group.name,
                          }))}
                        />
                      </Form.Item>
                    </Col>
                  </>
                )}
              </Row>
            ),
          },
          {
            key: 'survey',
            label: t('speleolocImport.optionsSurvey'),
            children: (
              <Row gutter={[16, 8]}>
                <Col xs={24}>
                  <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
                    {t('speleolocImport.surveyHint')}
                  </Typography.Paragraph>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('speleolocImport.cave')} style={{ marginBottom: 8 }}>
                    {/* A resumed review's cave comes from the model it names and is named by the
                        filter model's resolve, so it shows without being searched for again. */}
                    <ObjectSelector
                      scopes={CAVE_SCOPES}
                      presentation="dropdown"
                      allowClear
                      pageSize={10}
                      sorts={[]}
                      placeholder={t('speleolocImport.cavePlaceholder')}
                      aria-label={t('speleolocImport.cave')}
                      value={effectiveCaveId ?? null}
                      onChange={(value) => {
                        setCaveId(typeof value === 'string' ? value : undefined);
                        // A model belongs to one cave, so a model chosen under the old one is not
                        // a model of the new one — left standing it would be a choice the reviewer
                        // can no longer see, deciding where every scan lands.
                        set('surveyModelId', null);
                      }}
                      data-testid="speleoloc-import-cave"
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('speleolocImport.surveyModel')} style={{ marginBottom: 8 }}>
                    {/* Stays a plain list of the chosen cave's models rather than a search over
                        the survey-model filter world: that world deliberately has no "models of
                        cave X" field, so it cannot narrow to one cave, and the server's per-cave
                        listing is the read that also withholds the models of a cave whose exact
                        position this account may not be told. */}
                    <Select
                      allowClear
                      showSearch
                      optionFilterProp="label"
                      disabled={!effectiveCaveId}
                      loading={models.isFetching}
                      placeholder={t('speleolocImport.surveyModelPlaceholder')}
                      value={options.surveyModelId ?? undefined}
                      onChange={(value?: string) => set('surveyModelId', value ?? null)}
                      data-testid="speleoloc-import-survey-model"
                      options={(models.data ?? []).map((model) => ({
                        value: model.id,
                        label: model.name,
                      }))}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    label={t('speleolocImport.candidateCount')}
                    tooltip={t('speleolocImport.candidateCountHint')}
                    style={{ marginBottom: 8 }}
                  >
                    {/* Never fewer than two. One offer per scan reads as a settled answer even
                        where the survey has two branches at that depth, and narrowing the list is
                        then a way to make every tie disappear without being told that is what it
                        did. */}
                    <InputNumber
                      min={MIN_STATION_CANDIDATES}
                      max={MAX_STATION_CANDIDATES}
                      style={{ width: 120 }}
                      value={clampCandidateCount(options.candidateCount)}
                      onChange={(value) => set('candidateCount', clampCandidateCount(value))}
                      data-testid="speleoloc-import-candidate-count"
                    />
                  </Form.Item>
                </Col>
              </Row>
            ),
          },
        ]}
      />
    </Card>
  );
}
