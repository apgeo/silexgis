// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { AimOutlined, LogoutOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  DatePicker,
  Flex,
  Form,
  Input,
  InputNumber,
  Select,
  Typography,
} from 'antd';
import type { Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { isConcurrencyConflict } from '../../api/client.ts';
import {
  TRACKING_EVENT_KINDS,
  useRecordTrackingEvents,
  useResolveTrackingDepth,
  type TrackingDepthCandidate,
  type TrackingTeam,
  type TripPositionEventKind,
} from '../../api/hooks.ts';
import List from '../List.tsx';
import { trackingProblemMessage } from './trackingProblems.ts';

interface ReportForm {
  kind: TripPositionEventKind;
  stationName?: string;
  depthM?: number | null;
  teamId?: string | null;
  note?: string;
  recordedAt?: Dayjs | null;
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
 * visible while it can still be changed.
 *
 * The form is drawn only while the watch is armed. The server refuses reports otherwise, and
 * relying on that refusal would mean offering somebody a form that cannot work at the moment they
 * most need one — the wording here says which act is missing instead.
 */
export default function TrackingReportForm({
  tripLogId,
  armed,
  caverIds,
  teams,
  onRecorded,
}: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<ReportForm>();
  const kind = Form.useWatch('kind', form) ?? 'entered';
  const record = useRecordTrackingEvents();
  const resolve = useResolveTrackingDepth();
  const [candidates, setCandidates] = useState<TrackingDepthCandidate[] | null>(null);

  if (!armed) {
    return (
      <Alert
        type="info"
        showIcon
        message={t('trips.tracking.notArmedTitle')}
        description={t('trips.tracking.notArmedBody')}
        style={{ marginBottom: 16 }}
        data-testid="trip-tracking-not-armed"
      />
    );
  }

  const failed = (error: unknown) => {
    if (isConcurrencyConflict(error)) {
      message.warning(trackingProblemMessage(error, t));
      return;
    }
    message.error(trackingProblemMessage(error, t));
  };

  const send = async (body: Parameters<typeof record.mutateAsync>[0]) => {
    try {
      const created = await record.mutateAsync(body);
      message.success(t('trips.tracking.recorded', { count: created.length }));
      form.resetFields(['stationName', 'depthM', 'note', 'recordedAt']);
      setCandidates(null);
      onRecorded();
    } catch (error) {
      failed(error);
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

    const note = values.note?.trim();
    await send({
      tripLogId,
      caverIds: [...caverIds],
      kind: values.kind,
      // A station name belongs to a station report and a depth to a depth report; the server
      // refuses a request carrying the other one rather than quietly ignoring it.
      stationName: values.kind === 'atStation' ? (values.stationName ?? '').trim() : null,
      depthM: values.kind === 'atDepth' ? (values.depthM ?? null) : null,
      teamId: values.teamId ?? null,
      note: note ? note : null,
      recordedAt: values.recordedAt ? values.recordedAt.toISOString() : null,
    });
  };

  // Saying somebody is out is the one report that is worth its own control: it is the commonest
  // thing anybody records, it carries no position, and asking for it through the picker is three
  // actions at the moment a party is walking out.
  const onMarkOut = () =>
    send({ tripLogId, caverIds: [...caverIds], kind: 'exited', teamId: form.getFieldValue('teamId') ?? null });

  const onPreviewDepth = async () => {
    const depthM = form.getFieldValue('depthM') as number | null | undefined;
    if (depthM === null || depthM === undefined) {
      return;
    }
    try {
      setCandidates(await resolve.mutateAsync({ tripLogId, depthM }));
    } catch (error) {
      setCandidates(null);
      failed(error);
    }
  };

  const nobody = caverIds.length === 0;

  return (
    <Card size="small" title={t('trips.tracking.report')} style={{ marginBottom: 16 }}>
      <Form<ReportForm>
        form={form}
        layout="vertical"
        requiredMark={false}
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
            rules={[{ required: true, message: t('trips.tracking.reportStationRequired') }]}
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
                        <List.Item>
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
        <Form.Item
          name="recordedAt"
          label={t('trips.tracking.reportAt')}
          extra={t('trips.tracking.reportAtHelp')}
        >
          <DatePicker showTime style={{ width: '100%' }} data-testid="trip-tracking-recorded-at" />
        </Form.Item>
      </Form>

      {nobody && (
        <Alert
          type="warning"
          showIcon
          message={t('trips.tracking.selectNobody')}
          style={{ marginBottom: 12 }}
          data-testid="trip-tracking-nobody"
        />
      )}

      <Flex gap="small" wrap>
        <Button
          type="primary"
          disabled={nobody}
          loading={record.isPending}
          onClick={() => void onRecord()}
          data-testid="trip-tracking-record"
        >
          {t('trips.tracking.recordFor', { count: caverIds.length })}
        </Button>
        <Button
          icon={<LogoutOutlined />}
          disabled={nobody}
          loading={record.isPending}
          onClick={() => void onMarkOut()}
          data-testid="trip-tracking-mark-out"
        >
          {t('trips.tracking.markOut', { count: caverIds.length })}
        </Button>
      </Flex>
    </Card>
  );
}
