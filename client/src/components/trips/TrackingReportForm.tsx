// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { AimOutlined, LogoutOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  ConfigProvider,
  Flex,
  Form,
  Input,
  InputNumber,
  Select,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import {
  TRACKING_EVENT_KINDS,
  useResolveTrackingDepth,
  type TrackingDepthCandidate,
  type TrackingTeam,
  type TripPositionEventKind,
} from '../../api/hooks.ts';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import List from '../List.tsx';
import TrackingWhenField from './TrackingWhenField.tsx';
import { useTrackingPanelTheme } from './trackingControlSizes.ts';
import { trackingProblemMessage } from './trackingProblems.ts';
import {
  trackingStationRules,
  useTrackingReport,
  type TrackingReportValues,
} from './trackingReport.ts';

interface ReportForm extends TrackingReportValues {
  kind: TripPositionEventKind;
}

interface Props {
  tripLogId: string;
  /** Whether the watch is armed. Reports land on an armed watch and on no other. */
  armed: boolean;
  /** The people this report is about — the table's selection, in the order it holds them. */
  caverIds: readonly string[];
  teams: readonly TrackingTeam[];
  /** Called once a report has landed, so the selection that produced it can be let go. */
  onRecorded: () => void;
}

/**
 * Recording what somebody underground just said.
 *
 * One request for however many people the report is about, and that is the point of the selection
 * rather than a saving of round trips: a party that reached a station reached it together, at one
 * moment, and a request per person would put that moment on the log several times over with
 * nothing saying they were the same report.
 *
 * A depth is offered a preview before it is recorded. The server turns a depth into the nearest
 * station under the trip's filter and datum, and "nearest" is a decision somebody should watch
 * being made — a filter set a week ago, a datum on a station somebody renamed, and the position
 * that lands on the log is a place the party is not. The preview costs one call and makes that
 * visible while it can still be changed. Each station it offers can then be taken as the answer,
 * which is the difference between a preview and a lookup: the whole reason to ask which station a
 * depth means is that you may want to report that station rather than the depth.
 *
 * The form is drawn only while the watch is armed. The server refuses reports otherwise, and
 * relying on that refusal would mean offering somebody a form that cannot work at the moment they
 * most need one — the wording here says which act is missing instead.
 *
 * What a report actually *is* — which fields travel, how a moment is written, which refusal is a
 * warning — is not decided here. This card and the dialog opened by pressing a station on the model
 * are two ways to the same act, and that act has one home.
 */
export default function TrackingReportForm({
  tripLogId,
  armed,
  caverIds,
  teams,
  onRecorded,
}: Props) {
  const { t } = useTranslation();
  // The depth preview's own refusal. A report's refusals are worded where a report is sent.
  const { message } = App.useApp();
  // Every control here is pressed, and how big it has to be follows the pointer and not the width:
  // a phone in landscape has a desk's room across and still no pixel precision.
  const coarse = useCoarsePointer();
  const [form] = Form.useForm<ReportForm>();
  const kind = Form.useWatch('kind', form) ?? 'entered';
  const report = useTrackingReport();
  const resolve = useResolveTrackingDepth();
  const [candidates, setCandidates] = useState<TrackingDepthCandidate[] | null>(null);
  const panelTheme = useTrackingPanelTheme(coarse);

  if (!armed) {
    return (
      <Alert
        type="info"
        showIcon
        title={t('trips.tracking.notArmedTitle')}
        description={t('trips.tracking.notArmedBody')}
        style={{ marginBottom: 16 }}
        data-testid="trip-tracking-not-armed"
      />
    );
  }

  const send = async (values: TrackingReportValues) => {
    if (await report.send(tripLogId, caverIds, values)) {
      form.resetFields(['stationName', 'depthM', 'note', 'recordedAt']);
      setCandidates(null);
      onRecorded();
    }
  };

  const onRecord = async () => {
    let values: ReportForm;
    try {
      values = await form.validateFields();
    } catch {
      // The form is already showing why. Swallowed rather than rethrown so a refused validation
      // does not surface as an unhandled rejection in the browser, which is watched for.
      return;
    }
    await send(values);
  };

  // Saying somebody is out is the one report that is worth its own control: it is the commonest
  // thing anybody records, it carries no position, and asking for it through the picker is three
  // actions at the moment a party is walking out.
  const onMarkOut = () =>
    send({ kind: 'exited', teamId: form.getFieldValue('teamId') ?? null });

  const onPreviewDepth = async () => {
    const depthM = form.getFieldValue('depthM') as number | null | undefined;
    if (depthM === null || depthM === undefined) {
      return;
    }
    try {
      setCandidates(await resolve.mutateAsync({ tripLogId, depthM }));
    } catch (error) {
      setCandidates(null);
      message.error(trackingProblemMessage(error, t));
    }
  };

  /**
   * Taking one of the offered stations as the answer.
   *
   * The report changes kind as well as value, and it has to: the candidates only exist under a
   * depth report, and what is being said once a station has been chosen is "they are at this
   * station", which is a different claim about the world from "they are this far down". Recording
   * the depth instead would leave the server to resolve it a second time, against a filter that
   * could have moved in between — so the station somebody looked at and the station that lands on
   * the log would be two answers to one question.
   */
  const onChooseCandidate = (candidate: TrackingDepthCandidate) => {
    form.setFieldsValue({ kind: 'atStation', stationName: candidate.stationName });
    setCandidates(null);
  };

  const nobody = caverIds.length === 0;
  /**
   * How big everything on this card is drawn. `large` is where the forty pixels come from — the
   * component library builds it out of `controlHeightLG`, the touch target the rest of this
   * application uses — and asking by size rather than by height means the padding, line height
   * and icon inside each control are built for the size the control believes it is.
   */
  const controlSize: 'large' | 'middle' = coarse ? 'large' : 'middle';

  return (
    <Card size="small" title={t('trips.tracking.report')} style={{ marginBottom: 16 }}>
      {/* Around the form rather than around each chooser: a `Form.Item` hands its value and its
          change handler to the single element it is given, so anything put between the two takes
          them instead of the control. */}
      <ConfigProvider theme={panelTheme}>
        <Form<ReportForm>
          form={form}
          layout="vertical"
          requiredMark={false}
          size={controlSize}
          initialValues={{ kind: 'entered' as TripPositionEventKind }}
        >
          <Form.Item name="kind" label={t('trips.tracking.reportKind')}>
            <Select
              data-testid="trip-tracking-kind"
              onChange={() => setCandidates(null)}
              options={TRACKING_EVENT_KINDS.map((value) => ({
                value,
                label: t(`trips.tracking.kinds.${value}`),
              }))}
            />
          </Form.Item>

          {kind === 'atStation' && (
            <Form.Item
              name="stationName"
              label={t('trips.tracking.reportStation')}
              extra={t('trips.tracking.reportStationHelp')}
              rules={trackingStationRules(t)}
            >
              <Input data-testid="trip-tracking-station" />
            </Form.Item>
          )}

          {kind === 'atDepth' && (
            <>
              <Form.Item
                name="depthM"
                label={t('trips.tracking.reportDepth')}
                extra={t('trips.tracking.reportDepthHelp')}
                rules={[{ required: true, message: t('trips.tracking.reportDepthRequired') }]}
              >
                <InputNumber
                  style={{ width: '100%' }}
                  onChange={() => setCandidates(null)}
                  data-testid="trip-tracking-depth"
                />
              </Form.Item>
              <Flex gap="small" wrap style={{ marginBottom: 12 }}>
                <Button
                  size={controlSize}
                  icon={<AimOutlined />}
                  onClick={() => void onPreviewDepth()}
                  loading={resolve.isPending}
                  data-testid="trip-tracking-depth-preview"
                >
                  {t('trips.tracking.depthPreview')}
                </Button>
              </Flex>
              {candidates !== null && (
                <div style={{ marginBottom: 12 }} data-testid="trip-tracking-depth-candidates">
                  {candidates.length === 0 ? (
                    <Typography.Text type="secondary">
                      {t('trips.tracking.depthCandidatesNone')}
                    </Typography.Text>
                  ) : (
                    <>
                      <Typography.Text type="secondary">
                        {t('trips.tracking.depthCandidates')}
                      </Typography.Text>
                      <List
                        size="small"
                        dataSource={candidates}
                        renderItem={(candidate) => (
                          <List.Item
                            actions={[
                              <Button
                                key="choose"
                                size={controlSize}
                                onClick={() => onChooseCandidate(candidate)}
                                data-testid={`trip-tracking-depth-choose-${candidate.stationName}`}
                              >
                                {t('trips.tracking.depthCandidateChoose')}
                              </Button>,
                            ]}
                          >
                            <List.Item.Meta
                              title={candidate.stationName}
                              description={`${t('trips.tracking.depthCandidate', {
                                depth: candidate.depthM,
                                delta: candidate.deltaM,
                              })}${candidate.surveyName ? ` · ${candidate.surveyName}` : ''}`}
                            />
                          </List.Item>
                        )}
                      />
                    </>
                  )}
                </div>
              )}
            </>
          )}

          {teams.length > 0 && (
            <Form.Item name="teamId" label={t('trips.tracking.reportTeam')}>
              <Select
                allowClear
                placeholder={t('trips.tracking.reportTeamNone')}
                data-testid="trip-tracking-team"
                options={teams.map((team) => ({ value: team.id, label: team.title }))}
              />
            </Form.Item>
          )}

          <Form.Item name="note" label={t('trips.tracking.reportNote')}>
            <Input.TextArea rows={2} data-testid="trip-tracking-note" />
          </Form.Item>

          {/* Left empty the server stamps the report with its own clock, which is what a report made
              as it happens wants. It is filled in for the other case — word relayed out of the cave
              some time after it was said — and a time in the future is refused rather than stored. */}
          <TrackingWhenField size={controlSize} coarse={coarse} idPrefix="trip-tracking" />
        </Form>
      </ConfigProvider>

      {nobody && (
        <Alert
          type="warning"
          showIcon
          title={t('trips.tracking.selectNobody')}
          style={{ marginBottom: 12 }}
          data-testid="trip-tracking-nobody"
        />
      )}

      <Flex gap="small" wrap>
        <Button
          type="primary"
          size={controlSize}
          disabled={nobody}
          loading={report.isPending}
          onClick={() => void onRecord()}
          data-testid="trip-tracking-record"
        >
          {t('trips.tracking.recordFor', { count: caverIds.length })}
        </Button>
        <Button
          size={controlSize}
          icon={<LogoutOutlined />}
          disabled={nobody}
          loading={report.isPending}
          onClick={() => void onMarkOut()}
          data-testid="trip-tracking-mark-out"
        >
          {t('trips.tracking.markOut', { count: caverIds.length })}
        </Button>
      </Flex>
    </Card>
  );
}
