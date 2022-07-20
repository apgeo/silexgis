import React from 'react';

import {
  Drawer
} from 'antd';
import {
  DrawerProps
} from 'antd/lib/drawer';

import {
  useTranslation
} from 'react-i18next';

const BasicLayerTree = React.lazy(() => import('../BasicLayerTree'));
import {
  useAppDispatch
} from '../../hooks/useAppDispatch';
import {
  useAppSelector
} from '../../hooks/useAppSelector';
// import {
//   toggleVisibility
// } from '../../store/drawer';

import './index.less';
import { toggleFeaturesVisibility } from '../../store/featuresDrawer';
import FeatureTypeAPI from '../../middleware/FeatureTypeAPI';
import FeatureToolbar from '../FeatureToolbar';

// import { CaveApiFp, CaveApiFactory, CaveApiFetchParamCreator, Configuration as SxgApiConfiguration } from '../../middleware/typescript-fetch-client-generated'; // import { AssetApiFetchParamCreator } from '../../middleware/typescript-fetch-client-generated/api';


export const FeaturesSideDrawer: React.FC<Partial<DrawerProps>> = (props): JSX.Element => {
  const dispatch = useAppDispatch();
  const visible = useAppSelector((state) => state.featuresDrawer.visible);
  const {
    t
  } = useTranslation();

  const toggleDrawer = () => {
    dispatch(toggleFeaturesVisibility());
  };

  // const onFeatureSelection = () => { }

  return (
    <Drawer
      title={t('FeaturesDrawer.title')}
      placement="left"
      onClose={toggleDrawer}
      visible={visible}
      mask={false}
      // drawerStyle={{width: '178px'}}
      // bodyStyle={{padding: '16px'}} // default 24
      bodyStyle={{padding: '2px'}} // default 24
      width={200}

      style={{ position: 'absolute' }}
      getContainer={false}

      {...props}
    >
      <React.Suspense
        fallback={null}
      >
        <FeatureToolbar
          onFeatureSelection={props.onFeatureSelection}
          featureTypes={props.featureTypes}
        />
        {/* features<br/>
        1<br/>
        2<br/>
        3<br/>
        4 */}
      </React.Suspense>
    </Drawer>
  );
};

export default FeaturesSideDrawer;
