import React from 'react';

import {
  faTools
} from '@fortawesome/free-solid-svg-icons';
import {
  FontAwesomeIcon
} from '@fortawesome/react-fontawesome';

import SimpleButton, {
  SimpleButtonProps
} from '@terrestris/react-geo/dist/Button/SimpleButton/SimpleButton';

import {
  useAppDispatch
} from '../../hooks/useAppDispatch';
import {
  toggleFeaturesVisibility
} from '../../store/featuresDrawer';

import './index.less';

export const ToggleFeaturesDrawerButton: React.FC<Partial<SimpleButtonProps>> = (
  props
): JSX.Element => {
  const dispatch = useAppDispatch();

  const toggleFeaturesDrawer = () => {
    dispatch(toggleFeaturesVisibility());
  };

  return (
    <SimpleButton
      className="toggle-features-drawer-button"
      onClick={toggleFeaturesDrawer}
      icon={
        <FontAwesomeIcon
          icon={faTools}
        />
      }
    />
  );
};

export default ToggleFeaturesDrawerButton;
