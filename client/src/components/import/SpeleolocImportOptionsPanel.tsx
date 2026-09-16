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
  useCaves,
  useCavingGroups,
  useSurveyModel,
  useSurveyModels,
  useTripLog,
  useTripLogs,
  type SpeleolocImportOptions,
  type SpeleolocRecording,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import {
  clampCandidateCount,
  MAX_STATION_CANDIDATES,
  MIN_STATION_CANDIDATES,
} from './speleolocSelection.ts';

interface Props {
  options: SpeleolocImportOptions;
  onChange: (next: SpeleolocImportOptions) => void;
  /** What the archive holds. Empty until it has been read once, or when it could not be read. */
  recordings: readonly SpeleolocRecording[];
  recordingsLoading: boolean;
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
}: Props) {
  const { t, i18n } = useTranslation();
  const cavingGroups = useCavingGroups();

  const [tripSearch, setTripSearch] = useState('');
  const [caveSearch, setCaveSearch] = useState('');
  const debouncedTripSearch = useDebouncedValue(tripSearch);
  const debouncedCaveSearch = useDebouncedValue(caveSearch);

  const trips = useTripLogs({
    search: debouncedTripSearch || undefined,
    page: 1,
    pageSize: 25,
    sort: '-date',
  });
  // The trip a stored review already names, asked for by name. Without it a review resumed
  // tomorrow shows its own destination as a bare identifier, because a searched page of
  // twenty-five recent trips need not contain the one that was chosen a week ago.
  const chosenTrip = useTripLog(options.tripLogId ?? undefined);

  // The cave is not one of the stored choices — the model is — so a resumed review learns its cave
  // from the model it already names rather than asking the reviewer to find it again.
  const chosenModel = useSurveyModel(options.surveyModelId ?? undefined);
  const [caveId, setCaveId] = useState<string | undefined>(undefined);
  const effectiveCaveId = caveId ?? chosenModel.data?.caveId;
  const caves = useCaves({ search: debouncedCaveSearch || undefined, page: 1, pageSize: 25 });
  const models = useSurveyModels(effectiveCaveId);

  const set = <K extends keyof SpeleolocImportOptions>(key: K, value: SpeleolocImportOptions[K]) =>
    onChange({ ...options, [key]: value });

  const when = (value: string | null) =>
    value ? new Date(value).toLocaleString(i18n.language) : '—';

  const recordingOptions = recordings.map((recording) => ({
    value: recording.id,
    label: `${recording.title} · ${when(recording.startedAt)} · ${t('speleolocImport.scanCount', { count: recording.pointCount })}`,
  }));

  const tripOptions = [
    ...(trips.data?.items ?? []).map((trip) => ({
      value: trip.id,
      label: `${trip.tripDate} · ${trip.title}`,
    })),
    // The stored destination, when the searched page did not happen to carry it.
    ...(chosenTrip.data && !(trips.data?.items ?? []).some((trip) => trip.id === chosenTrip.data.id)
      ? [{ value: chosenTrip.data.id, label: `${chosenTrip.data.tripDate} · ${chosenTrip.data.title}` }]
      : []),
  ];

  const caveOptions = [
    ...(caves.data?.items ?? []).map((cave) => ({ value: cave.id, label: cave.name })),
    // Same as the trip above: the cave a resumed review's model belongs to, which a searched page
    // of twenty-five need not contain.
    ...(chosenModel.data
      && !(caves.data?.items ?? []).some((cave) => cave.id === chosenModel.data.caveId)
      ? [{ value: chosenModel.data.caveId, label: t('speleolocImport.chosenCave') }]
      : []),
  ];

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
                      <Select
                        allowClear
                        showSearch
                        // Searched on the server, so the box is not filtered twice against a page
                        // it has already narrowed.
                        filterOption={false}
                        loading={trips.isFetching}
                        placeholder={t('speleolocImport.tripPlaceholder')}
                        value={options.tripLogId ?? undefined}
                        onSearch={setTripSearch}
                        onChange={(value?: string) => set('tripLogId', value ?? null)}
                        data-testid="speleoloc-import-trip"
                        options={tripOptions}
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
                    <Select
                      allowClear
                      showSearch
                      filterOption={false}
                      loading={caves.isFetching}
                      placeholder={t('speleolocImport.cavePlaceholder')}
                      value={effectiveCaveId}
                      onSearch={setCaveSearch}
                      onChange={(value?: string) => {
                        setCaveId(value);
                        // A model belongs to one cave, so a model chosen under the old one is not
                        // a model of the new one — left standing it would be a choice the reviewer
                        // can no longer see, deciding where every scan lands.
                        set('surveyModelId', null);
                      }}
                      data-testid="speleoloc-import-cave"
                      options={caveOptions}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('speleolocImport.surveyModel')} style={{ marginBottom: 8 }}>
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
