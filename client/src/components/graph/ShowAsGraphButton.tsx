// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApartmentOutlined } from '@ant-design/icons';
import type { ButtonProps } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import Button from '../Button.tsx';
import { graphRoute } from './graphRoute.ts';

interface Props {
  /** The item's kind in the wire spelling the graph and the link register share, such as `feature`. */
  type: string;
  id: string;
  size?: ButtonProps['size'];
  buttonType?: ButtonProps['type'];
}

/** Opens the relations graph with this item as the starting point. */
export default function ShowAsGraphButton({ type, id, size = 'small', buttonType = 'link' }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  return (
    <Button
      size={size}
      type={buttonType}
      icon={<ApartmentOutlined />}
      onClick={() => void navigate(graphRoute(type, id))}
      data-testid="show-as-graph"
    >
      {t('graph.showAsGraph')}
    </Button>
  );
}
