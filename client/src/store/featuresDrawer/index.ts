import {
  createSlice
} from '@reduxjs/toolkit';

interface FeaturesDrawerState {
  visible: boolean;
}

const initialState: FeaturesDrawerState = {
  visible: !false
};

export const sliceFeatures = createSlice({
  name: 'featuresDrawer',
  initialState,
  reducers: {
    toggleFeaturesVisibility: (state) => {
      state.visible = !state.visible;
    }
  }
});

export const {
  toggleFeaturesVisibility
} = sliceFeatures.actions;

export default sliceFeatures.reducer;
