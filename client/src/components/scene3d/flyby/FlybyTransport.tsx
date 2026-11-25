// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  CaretRightOutlined,
  FullscreenOutlined,
  PauseOutlined,
  RetweetOutlined,
  StopOutlined,
  VideoCameraOutlined,
} from '@ant-design/icons';
import { Progress, Slider, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../../Button.tsx';
import { flybyClock } from './flybyClock.ts';
import type { FlybyController } from './useFlyby.ts';

export interface FlybyTransportProps {
  flyby: FlybyController;
  /** Plays the flyby with everything but the scene taken off the screen. */
  onCleanScreen(): void;
  /** Opens the dialog the flyby is written to a movie file from. */
  onMakeMovie(): void;
}

/**
 * The bar under the scene a flyby is driven from: play and pause, stop, where it has got to, and
 * whether it repeats.
 *
 * The position can be dragged both ways at any time, playing or not — a flyby's moment depends on
 * nothing but its time — and is only held while the flyby is still being prepared, when there is no
 * path yet to be anywhere on.
 */
export default function FlybyTransport({ flyby, onCleanScreen, onMakeMovie }: FlybyTransportProps) {
  const { t } = useTranslation();
  const { playerState, preparing } = flyby;
  const playing = playerState.status === 'playing';
  const prepared = flyby.length > 0 && playerState.status !== 'idle';

  return (
    <div className="scene3d-flyby-transport" data-testid="flyby-transport" role="group" aria-label={t('flyby.transportTitle')}>
      {playing ? (
        <Button size="small" icon={<PauseOutlined />} onClick={flyby.pause} aria-label={t('flyby.pause')} data-testid="flyby-pause" />
      ) : (
        <Button
          size="small"
          type="primary"
          icon={<CaretRightOutlined />}
          loading={preparing !== null}
          disabled={!flyby.document}
          onClick={() => void flyby.play()}
          aria-label={t('flyby.play')}
          data-testid="flyby-play"
        />
      )}
      <Button
        size="small"
        icon={<StopOutlined />}
        disabled={!flyby.active}
        onClick={flyby.stop}
        aria-label={t('flyby.stop')}
        data-testid="flyby-stop"
      />
      {preparing !== null ? (
        <span className="scene3d-flyby-preparing" data-testid="flyby-status" role="status">
          <Typography.Text>
            {preparing === true || preparing.total === 0
              ? t('flyby.preparing')
              : t(preparing.step === 'subjects' ? 'flyby.preparingSubjects' : 'flyby.preparingRoute', {
                  done: preparing.done,
                  total: preparing.total,
                })}
          </Typography.Text>
          {preparing !== true && preparing.total > 0 && (
            <Progress percent={Math.round((preparing.done / preparing.total) * 100)} size="small" showInfo={false} />
          )}
        </span>
      ) : (
        <>
          <div className="scene3d-flyby-position" data-testid="flyby-position">
            <Slider
              min={0}
              max={Math.max(flyby.length, 0.1)}
              step={0.1}
              value={prepared ? playerState.t : 0}
              disabled={!prepared}
              onChange={flyby.seek}
              tooltip={{ formatter: (value) => flybyClock(value ?? 0) }}
              ariaLabelForHandle={t('flyby.position')}
            />
          </div>
          <Typography.Text className="scene3d-flyby-time" data-testid="flyby-time" data-status={playerState.status}>
            {flybyClock(prepared ? playerState.t : 0)} / {flybyClock(flyby.length)}
          </Typography.Text>
        </>
      )}
      <Tooltip title={t('flyby.loopHint')}>
        <Button
          size="small"
          type={flyby.loop ? 'primary' : 'default'}
          icon={<RetweetOutlined />}
          aria-pressed={flyby.loop}
          onClick={() => flyby.setLoop(!flyby.loop)}
          aria-label={t('flyby.loop')}
          data-testid="flyby-loop"
        />
      </Tooltip>
      <Tooltip title={t('flyby.cleanScreenHint')}>
        <Button
          size="small"
          icon={<FullscreenOutlined />}
          disabled={!flyby.document || preparing !== null}
          onClick={onCleanScreen}
          aria-label={t('flyby.cleanScreen')}
          data-testid="flyby-clean"
        />
      </Tooltip>
      <Tooltip title={t('flyby.movie.openHint')}>
        <Button
          size="small"
          icon={<VideoCameraOutlined />}
          disabled={!flyby.document || preparing !== null}
          onClick={onMakeMovie}
          aria-label={t('flyby.movie.open')}
          data-testid="flyby-movie"
        />
      </Tooltip>
    </div>
  );
}
