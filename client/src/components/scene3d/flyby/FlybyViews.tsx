// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { AimOutlined, ArrowDownOutlined, ArrowUpOutlined, CameraOutlined, DeleteOutlined } from '@ant-design/icons';
import { InputNumber, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { FLYBY_SECONDS_RANGE } from '../../../flyby/flybyDocument.ts';
import type { FlybyCapturedView } from '../../../flyby/flybyPresets.ts';
import Button from '../../Button.tsx';

export interface FlybyViewsProps {
  views: FlybyCapturedView[];
  setViews(views: FlybyCapturedView[]): void;
  /** The scene's camera as a view, or undefined when the middle of the picture shows no ground. */
  capture(): FlybyCapturedView | undefined;
  busy: boolean;
}

/**
 * The views a flyby flies through, captured from the scene's camera one at a time: the person
 * moves the scene to a picture they want and presses Add this view. Each view after the first has
 * the seconds the flight into it takes; any view can be taken again from the camera as it is now,
 * moved up or down the list, or taken out.
 */
export default function FlybyViews({ views, setViews, capture, busy }: FlybyViewsProps) {
  const { t } = useTranslation();
  const [noGround, setNoGround] = useState(false);
  /** The camera now, or nothing and a sentence saying why. */
  const take = (): FlybyCapturedView | undefined => {
    const view = capture();
    setNoGround(!view);
    return view;
  };
  const replace = (index: number, view: FlybyCapturedView) =>
    setViews(views.map((existing, at) => (at === index ? view : existing)));
  const move = (index: number, by: number) => {
    const next = [...views];
    const [moved] = next.splice(index, 1);
    next.splice(index + by, 0, moved);
    setViews(next);
  };

  return (
    <div className="scene3d-flyby-group" data-testid="flyby-views">
      <Typography.Text type="secondary">{t('flyby.views.hint')}</Typography.Text>
      <Button
        size="small"
        icon={<CameraOutlined />}
        disabled={busy}
        onClick={() => {
          const view = take();
          if (view) {
            setViews([...views, view]);
          }
        }}
        data-testid="flyby-view-add"
      >
        {t('flyby.views.add')}
      </Button>
      {noGround && (
        <Typography.Text type="danger" data-testid="flyby-view-no-ground">
          {t('flyby.views.noGround')}
        </Typography.Text>
      )}
      {views.length > 0 && (
        <ol className="scene3d-flyby-changes" data-testid="flyby-view-list">
          {views.map((view, index) => {
            const name = t('flyby.views.name', { number: index + 1 });
            return (
              <li key={index} data-testid={`flyby-view-${index}`}>
                <span className="scene3d-flyby-change-what">
                  {name}
                  <Typography.Text type="secondary">
                    {' · '}
                    {t(view.underground ? 'flyby.views.underground' : 'flyby.views.distance', { metres: Math.round(view.distance) })}
                  </Typography.Text>
                </span>
                <div className="scene3d-flyby-row">
                  {index > 0 && (
                    <InputNumber
                      size="small"
                      min={FLYBY_SECONDS_RANGE.min}
                      max={FLYBY_SECONDS_RANGE.max}
                      step={1}
                      value={view.seconds}
                      onChange={(seconds) => seconds !== null && replace(index, { ...view, seconds })}
                      disabled={busy}
                      suffix={t('flyby.secondsUnit')}
                      aria-label={t('flyby.views.seconds', { view: name })}
                      data-testid={`flyby-view-${index}-seconds`}
                    />
                  )}
                  <Button
                    size="small"
                    type="text"
                    icon={<AimOutlined />}
                    disabled={busy}
                    aria-label={t('flyby.views.recapture', { view: name })}
                    onClick={() => {
                      const taken = take();
                      if (taken) {
                        replace(index, { ...taken, seconds: view.seconds });
                      }
                    }}
                    data-testid={`flyby-view-${index}-recapture`}
                  />
                  <Button
                    size="small"
                    type="text"
                    icon={<ArrowUpOutlined />}
                    disabled={busy || index === 0}
                    aria-label={t('flyby.views.up', { view: name })}
                    onClick={() => move(index, -1)}
                    data-testid={`flyby-view-${index}-up`}
                  />
                  <Button
                    size="small"
                    type="text"
                    icon={<ArrowDownOutlined />}
                    disabled={busy || index === views.length - 1}
                    aria-label={t('flyby.views.down', { view: name })}
                    onClick={() => move(index, 1)}
                    data-testid={`flyby-view-${index}-down`}
                  />
                  <Button
                    size="small"
                    type="text"
                    danger
                    icon={<DeleteOutlined />}
                    disabled={busy}
                    aria-label={t('flyby.views.remove', { view: name })}
                    onClick={() => setViews(views.filter((_, at) => at !== index))}
                    data-testid={`flyby-view-${index}-remove`}
                  />
                </div>
              </li>
            );
          })}
        </ol>
      )}
    </div>
  );
}
