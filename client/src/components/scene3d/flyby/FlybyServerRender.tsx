// SPDX-License-Identifier: AGPL-3.0-or-later
import { CloudServerOutlined, DownloadOutlined, StopOutlined } from '@ant-design/icons';
import { Alert, Progress, Radio, Select, Space, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { downloadFile } from '../../../api/download.ts';
import Button from '../../Button.tsx';
import {
  FLYBY_RENDER_CONTAINERS,
  flybyRenderFileUrl,
  flybyRenderIsActive,
  useCancelFlybyRender,
  useFlybyRenderAvailability,
  useFlybyRenders,
  type FlybyRender,
  type FlybyRenderContainer,
  type FlybyRenderPlan,
} from '../../../api/hooks/flybyRenders.ts';

/** The phases a render node reports, each with words of its own; anything else reads as "working". */
const PHASES = new Set(['claimed', 'downloading', 'checking', 'rendering', 'composing', 'collectingOutputs', 'uploading', 'done']);

/** The roles of a result's files in the order the panel offers them: the film first. */
const ROLE_ORDER = ['movie', 'still', 'subtitles', 'log', 'status'];

function clock(seconds: number): string {
  const whole = Math.max(0, Math.round(seconds));
  const h = Math.floor(whole / 3600);
  const m = Math.floor((whole % 3600) / 60);
  const s = whole % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
}

export interface FlybyServerRenderProps {
  /** Whether a render may be asked for now: a flyby to render, and nothing else being made. */
  canStart: boolean;
  /** Whether the bundle for a render is being made and sent. */
  starting: boolean;
  /** Makes the bundle and queues its render. */
  onStart: (plan: FlybyRenderPlan, container: FlybyRenderContainer) => void;
}

/**
 * "Render on the server", beside the bundle's download: the same bundle, rendered by a render node
 * instead of on the reader's own machine, and the reader's latest render — how far it got, a way to
 * stop it, and its film to download.
 *
 * Shown only while a node has called lately: offered with none, a render would sit in the queue
 * with nothing to say why. A render already going stays shown either way, so it can be followed and
 * stopped.
 */
export default function FlybyServerRender({ canStart, starting, onStart }: FlybyServerRenderProps) {
  const { t, i18n } = useTranslation();
  const [plan, setPlan] = useState<FlybyRenderPlan>('draft');
  const [container, setContainer] = useState<FlybyRenderContainer>('mp4');
  const availability = useFlybyRenderAvailability();
  const renders = useFlybyRenders();
  const latest = renders.data?.[0];
  const available = availability.data?.available === true;
  if (!available && (latest === undefined || !flybyRenderIsActive(latest))) {
    return null;
  }
  const going = latest !== undefined && flybyRenderIsActive(latest);

  return (
    <section className="scene3d-flyby-movie-server" data-testid="flyby-render-server" aria-label={t('flyby.render.title')}>
      <Typography.Text strong>{t('flyby.render.title')}</Typography.Text>
      <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
        {t('flyby.render.help')}
      </Typography.Text>
      {available && (
        <Space wrap>
          <Radio.Group
            value={plan}
            onChange={(event) => setPlan(event.target.value as FlybyRenderPlan)}
            optionType="button"
            size="small"
            aria-label={t('flyby.render.plan')}
            data-testid="flyby-render-plan"
            disabled={starting}
          >
            <Radio.Button value="draft">{t('flyby.render.planDraft')}</Radio.Button>
            <Radio.Button value="full">{t('flyby.render.planFull')}</Radio.Button>
          </Radio.Group>
          <Select
            size="small"
            value={container}
            onChange={setContainer}
            aria-label={t('flyby.render.container')}
            data-testid="flyby-render-container"
            disabled={starting}
            options={FLYBY_RENDER_CONTAINERS.map((value) => ({ value, label: value.toUpperCase() }))}
          />
          <Button
            icon={<CloudServerOutlined />}
            loading={starting}
            disabled={!canStart || going}
            onClick={() => onStart(plan, container)}
            data-testid="flyby-render-start"
          >
            {t('flyby.render.start')}
          </Button>
        </Space>
      )}
      {latest !== undefined && <RenderState render={latest} language={i18n.language} />}
    </section>
  );
}

function RenderState({ render, language }: { render: FlybyRender; language: string }) {
  const { t } = useTranslation();
  const cancel = useCancelFlybyRender();
  const stop = (
    <Button
      size="small"
      danger
      icon={<StopOutlined />}
      loading={cancel.isPending}
      disabled={render.cancelRequested}
      onClick={() => cancel.mutate(render.id)}
      data-testid="flyby-render-cancel"
    >
      {render.cancelRequested ? t('flyby.render.stopping') : t('flyby.render.stop')}
    </Button>
  );

  if (render.status === 'queued') {
    return (
      <div data-testid="flyby-render-state" data-status="queued" role="status">
        <Typography.Text>{t('flyby.render.queued', { name: render.name })}</Typography.Text> {stop}
      </div>
    );
  }

  if (render.status === 'running') {
    const total = render.total ?? 0;
    const done = render.done ?? 0;
    const phase = render.phase && PHASES.has(render.phase) ? render.phase : 'working';
    return (
      <div data-testid="flyby-render-state" data-status="running" role="status">
        <Progress percent={total > 0 ? Math.floor((done / total) * 100) : 0} size="small" status="active" />
        <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
          {t(`flyby.render.phase.${phase}`)}
          {total > 0 ? ` · ${t('flyby.render.frames', { done, total })}` : ''}
          {render.secondsLeft != null ? ` · ${t('flyby.render.timeLeft', { time: clock(render.secondsLeft) })}` : ''}
        </Typography.Text>{' '}
        {stop}
      </div>
    );
  }

  if (render.status === 'succeeded') {
    const files = [...render.files].sort((a, b) => ROLE_ORDER.indexOf(a.role) - ROLE_ORDER.indexOf(b.role));
    return (
      <Alert
        type="success"
        showIcon
        data-testid="flyby-render-state"
        data-status="succeeded"
        title={t('flyby.render.succeeded', { name: render.name })}
        description={
          <Space orientation="vertical" size="small">
            <Space wrap>
              {files.map((file) => (
                <Button
                  key={file.name}
                  size="small"
                  type={file.role === 'movie' ? 'primary' : 'default'}
                  icon={<DownloadOutlined />}
                  onClick={() => void downloadFile(flybyRenderFileUrl(render.id, file.name))}
                  data-testid={`flyby-render-file-${file.role}`}
                >
                  {t(`flyby.render.file.${file.role}`, { defaultValue: file.name })}
                </Button>
              ))}
            </Space>
            {render.expiresAt && (
              <Typography.Text type="secondary" className="scene3d-flyby-movie-help">
                {t('flyby.render.kept', { date: new Date(render.expiresAt).toLocaleDateString(language) })}
              </Typography.Text>
            )}
          </Space>
        }
      />
    );
  }

  return render.status === 'cancelled' ? (
    <Alert type="info" showIcon data-testid="flyby-render-state" data-status="cancelled" title={t('flyby.render.cancelled')} />
  ) : (
    <Alert
      type="error"
      showIcon
      data-testid="flyby-render-state"
      data-status="failed"
      title={t('flyby.render.failed')}
      description={render.error ?? undefined}
    />
  );
}
