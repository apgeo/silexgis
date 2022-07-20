import * as React from 'react';

import { withStyles, makeStyles } from '@material-ui/core/styles';
import Tab from '@material-ui/core/Tab';
import TabContext from '@material-ui/lab/TabContext';
import TabList from '@material-ui/lab/TabList';
import TabPanel from '@material-ui/lab/TabPanel';
// import CloseIcon from '@mui/icons-material/Close';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
// import Checkbox from '@mui/material/Checkbox';
// import FormControlLabel from '@mui/material/FormControlLabel';
// import FormGroup from '@mui/material/FormGroup';
// import IconButton from '@mui/material/IconButton';
import MenuItem from '@mui/material/MenuItem';
import Modal from '@mui/material/Modal';
import Select, { SelectChangeEvent } from '@mui/material/Select';
// import NativeSelect from '@mui/material/NativeSelect';
import Snackbar from '@mui/material/Snackbar';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Paper from '@mui/material/Paper';

import FeatureAPI from '../../middleware/FeatureAPI';
import NotificationSnackbar from '../NotificationSnackbar';
import Entrance from '../../data/models/Entrance';

import update from 'immutability-helper';
import WKT from 'ol/format/WKT';

import './FeatureEditModal.less';

const boxStyle = {
  position: 'absolute' as 'absolute',
  top: '50%',
  left: '50%',
  transform: 'translate(-50%, -50%)',
  width: 400,
  bgcolor: 'background.paper',
  border: '2px solid #000',
  boxShadow: 24,
  p: 4,
  /* m: 1, */
  width: '125ch',
  '& .MuiTextField-root': {
    m: 1,
    width: '25ch'
  },
  '& .MuiTab-root': {
    'minWidth': '45px'
  }
};

// const styles = {
//   'MuiButtonBase-root': {
//     display: 'block',
//     // 'background-color': 'green'
//   },
//   // 'MuiTab-root': {
//   //   'minWidth': '20px'
//   // }
//   /* '& .MuiButtonBase-root': {
//     display: 'block',
//     // 'background-color': 'green'
//   }
//   */
// };

const styles = theme => ({
  root: {
    width: "100%",
    maxWidth: 360,
    backgroundColor: theme.palette.background.paper,
    display: 'block'
  },
  menuItemRoot: {
    display: 'block',
    "&$menuItemSelected, &$menuItemSelected:focus, &$menuItemSelected:hover": {
      backgroundColor: "red"
    }
  },
  menuItemSelected: {}
});

// const useStyles = makeStyles((theme) => ({
//   root: {
//     // whiteSpace: "unset",
//     // wordBreak: "break-all",
//     display: 'block',
//   }
// }));

class FeatureEditModal extends React.Component {
  // [x: string]: (ev: any) => any;

  // mixins: [LinkedStateMixin];

  // state = {
  //   latitude: 2,
  //   longitude:5,
  //   openModal : false,

  //   tabValue: '1',
  //   // Feature_type_id: 'unknown'
  // };

  constructor(props, context) {
    super(props);
    this.state = {
      count: 0,
      open: false,
      setOpen: false,

      tabValue: '1',
      isModalOpen: this.props.isModalOpen, // false,

      notificationOpened: false,
      notificationMessage: '',
      notificationSeverity: '',

      featureData: {
        id: undefined,
        name: '',
        description: '',        
        feature_type_id: this.props.featureType.id,
        coordinates: [], // if point, coordinates for being able to modify it in interface
        geometryWKT: undefined,
        propertiesJSON: undefined
      },

      fieldValues: {}
    };

    console.log('_____________FeatureEditModal constructor ()');

    if (props.isModalOpen) { console.log('isModalOpen = true'); }

    const updateFieldEvent = key => ev => { this.setState({ [key]: ev.target.value }); };

    const updateNestedFieldEvent = key => ev => {

      var [parentProperty, _key] = key.split('.', 2);

      // console.log ("updateNestedFieldEvent()");      console.log ("pp=" + parentProperty);      console.log ("key=" + _key);
      this.setState({
        [parentProperty]: {
          ...this.state[parentProperty],
          [_key]: ev.target.value
        }
      });
      // multiple answers and opinions here https://stackoverflow.com/questions/43040721/how-to-update-nested-state-properties-in-react    |    https://alexsidorenko.com/blog/react-update-nested-state/
      // this.setState({[key]: ev.target.value}); 
    };

    // const updateFieldEvent = key => ev => this.props.onUpdateField(key, ev.target.value);

    // const setTabValue = key => newValue => { this.setState({tabValue: newValue}); };

    this.handleChange = this.handleChange.bind(this);
    this.handleSubmit = this.handleSubmit.bind(this);
    this.handleOpen = this.handleOpen.bind(this);
    this.handleClose = this.handleClose.bind(this);

    // this.handleLatChange = this.handleLatChange.bind(this);

    // this.handleLatitudeChange = updateNestedFieldEvent('featureData.latitude');
    // this.handleLongitudeChange = updateNestedFieldEvent('featureData.longitude');

    this.handleLatitudeChange = this.handleLatitudeChange.bind(this);
    this.handleLongitudeChange = this.handleLongitudeChange.bind(this);

    this.handleNameChange = updateNestedFieldEvent('featureData.name');
    // this.handlexChange = updateNestedFieldEvent('featureData.other_toponyms');

    // this.handleTabChange = (event: React.SyntheticEvent, newValue: string) => {
    //   // setTabValue(newValue);
    // };  

    this.handleTabChange = this.handleTabChange.bind(this);

    this.handleSave = this.handleSave.bind(this);
    this.handleButtonCancel = this.handleButtonCancel.bind(this);

    this.handleNotificationOpenStateChange = this.handleNotificationOpenStateChange.bind(this);
    // this.showModal = this.showModal.bind(this);
    // this.handleShow = this.handleShow.bind(this);

    this.handleFieldChange = this.handleFieldChange.bind(this);
    this.handle_description_Change = updateNestedFieldEvent('featureData.description');

    this.handleKeyPress = this.handleKeyPress.bind(this);

    this.useStyles = this.useStyles.bind(this);
  }
  // const [open, setOpen] = React.useState(false);
  // const handleOpen = () => setOpen(true);
  // const handleClose = () => setOpen(false);

  useStyles ()
  {
    return makeStyles((theme) => ({
      root: {
        // whiteSpace: "unset",
        // wordBreak: "break-all",
        display: 'block',
      }
    }));
  };

  handleKeyPress(event: React.SyntheticEvent) {
    if (event.key === 'Enter') {
      this.handleSave(event);
    }
  }
  
  handleOpen() {
    console.log('handleOpen()');
    this.setState({ open: true });
  }
  // handleClose () { this.setState({open: false }); }

  handleClose() {
    console.log('handleClose()');

    // this.props.onOpenStateChange(false);
    this.setState({ isModalOpen: false });
  }

  // https://reactjs.org/docs/lifting-state-up.html
  handleButtonCancel() {
    console.log('handleButtonCancel()');
    this.props.onOpenStateChange(false, false);
    // this.setState({isModalOpen: false }); 
  }

  // handleOpen  = val => () { this.setState({open: true }); } // arrow

  // showModal () { this.setState({isModalOpen: true }); }
  // handleShow () { this.setState({isModalOpen: true }); }

  handleChange(event) { this.setState({ value: event.target.value }); }
  // const handleChange = (event) => { setState({value: event.target.value}); }; // arrow

  handleSubmit(event) {
    alert('A val was changed: ' + this.state.featureData.latitude);
    event.preventDefault();
  }

  handleTabChange(event: React.SyntheticEvent, newValue: string) {
    // console.log(event);
    // console.log(newValue);

    this.setState({ tabValue: newValue });
  }

  handleFieldChange(event: React.SyntheticEvent, fieldProperties: any/*, newValue: string*/) { // featureType
    console.log(`handleFieldChange()`);
    
    console.log(event);
    console.log(fieldProperties);
    console.log(event.target.value);
    // console.log(newValue);
    
    /*
    // this is bad since it doesn't update state
    var item = this.state.fieldValues[fieldProperties.identifier];

    if (!item) { 
      this.state.fieldValues[fieldProperties.identifier] = {}; 
      item = this.state.fieldValues[fieldProperties.identifier];
    }

    item.identifier = fieldProperties.identifier;
    item.value = event.target.value;
    this.state.fieldValues[fieldProperties.identifier] = item;

    this.setState({ fieldValues: this.state.fieldValues });
    */


    /* // either this (update from 'immutability-helper'), or using bellow
    let newState = update(this.state, {
      fieldValues: {
        [fieldProperties.identifier]: { $set: event.target.value },
       }
     });
    
    this.setState(newState);
    */
      // or updateNestedFieldEvent

      // or this (spread) would work as well
        this.setState({ 
          fieldValues: {
            ...this.state.fieldValues,
            [fieldProperties.identifier]: event.target.value,
        }});

    console.log(this.state.fieldValues);
  }

  handleSave(event) {
    console.log('handleSave()');
    // event.preventDefault();
    let featureType = this.props.featureType;
    console.log(this.state.featureData);

    const format = new WKT();

    const geometryWKT = format.writeFeature(this.props.olFeature, {
      dataProjection: 'EPSG:4326',
      featureProjection: 'EPSG:3857',
      decimals: 7
    });

    this.state.featureData.geometryWKT = geometryWKT;

    // new FeatureAPI().Save();
    if (this.state.featureData.id)
    {
      FeatureAPI.Save(this.state.featureData)
      .then(res => res.json())
      .then(
        (result) => {
          // console.log("fetch then");
          // console.log(result);

          if (result.success)
          {
            this.showNotification('Feature data was saved', 'success');
            // this.props.wasSaved = true;
            this.props.onSaveFeature(result.data);
            this.props.onOpenStateChange(false, true);
            //this.handleButtonCancel();
            //this.handleClose();
          }
          else
            this.showNotification('Error saving feature data:  ' + result.message + '"', 'error');
        },
        // Note: it's important to handle errors here instead of a catch() block so that we don't swallow exceptions from actual bugs in components.
        (error) => {
          this.showNotification('Error saving feature data (server failure error)', 'error');
          // this.setState({
          //   isLoaded: true,
          //   error
          // });
        }
      )
      .catch((error) => {
        this.showNotification('Error saving feature data (prob. error in client code)', 'error');
      });
    }
    else
    {
      FeatureAPI.Add(this.state.featureData)
        .then(res => res.json())
        .then(
          (result) => {
            // console.log("fetch then");
            // console.log(result);

            if (result.success)
            {
              this.showNotification('Feature data was saved', 'success');
              // this.props.wasSaved = true;
              this.props.onSaveFeature(result.data);
              this.props.onOpenStateChange(false, true);
              //this.handleButtonCancel();
              //this.handleClose();
            }
            else
              this.showNotification('Error saving feature data:  ' + result.message + '"', 'error');
          },
          // Note: it's important to handle errors here instead of a catch() block so that we don't swallow exceptions from actual bugs in components.
          (error) => {
            this.showNotification('Error saving feature data (server failure error)', 'error');
            // this.setState({
            //   isLoaded: true,
            //   error
            // });
          }
        )
        .catch((error) => {
          this.showNotification('Error saving feature data (prob. error in client code)', 'error');
        });
    }
  }

  showNotification(message, notificationAlertType) {
    this.setState({
      notificationOpened: true,
      notificationMessage: message,
      notificationSeverity: notificationAlertType
    });
  }

  handleNotificationOpenStateChange(openState: boolean) {
    // console.log("handleOpenStateChange()");

    this.setState({ notificationOpened: openState });
  };

  handleLatitudeChange(event) {     
    let coordinates = this.state.featureData.coordinates;
    coordinates[1] = event.target.value;

    this.setState({ 
      featureData: 
      {
        ...this.state.featureData,
        coordinates: coordinates
      }
      }); 
  }

  handleLongitudeChange(event) { 
    let coordinates = this.state.featureData.coordinates;
    coordinates[0] = event.target.value;

    this.setState({ 
      featureData: 
      {
        ...this.state.featureData,
        coordinates: coordinates
      }
      }); 
  }
  // handleLatChange(event) { this.setState({latitude: event.target.value }); }

  // handleSelectChange (event: SelectChangeEvent) {    
  //   this.setState({rock_type: event.target.value as string});
  // };

  // https://reactjs.org/docs/state-and-lifecycle.html
  componentDidMount () {
    console.log('_____________FeatureEditModal componentDidMount ()');
    console.log('modalFeatureEditMode=' + this.props.mode);

    if (this.props.mode == 'add')
    {
      // console.log('mode=' + this.props.mode);
    }
    else
    if (this.props.mode == 'edit')
    {
      let featureId = this.props.featureId;

      FeatureAPI.GetById(featureId).then(res => res.json())
      .then(
        (res) => {
          console.log('get response data ok');

          console.log(res.data);

          if (res.success == false)
            alert("Error: " + res.message);
          else
            this.setState({ featureData: res.data });
          // Promise.resolve(result);
        },
        (error) => {
          console.log('Error (er): ' + error);
        }
      )
      .catch((error) => {
        alert('Error saving feature data: ' + error);
      });

      console.log('feature data =');
      // console.log(featureData);
    }
    else
      console.warn('unknown modalFeatureEditMode=' + this.props.mode);
  }

  componentWillUnmount() {
    console.log('_____________FeatureEditModal componentWillUnmount ()');
  }

  render () {
    const { /* latitude, */ tabValue } = this.state;
    // $users = this.$at( 'users' ),

    // latitude = this.state.featureData.latitude;

    // const state$ = this.state$();
    // { latitude } = this.state$();

    // const [tabValue] = this.state;

    // this.state.isModalOpen = this.props.isModalOpen;

    // if (this.props.isModalOpen) {
    //   // this.state.isModalOpen = this.props.isModalOpen;
    //   // this.props.isModalOpen = false;
    // }

    // console.log("this.state.isModalOpen = " + this.state.isModalOpen);
    // console.log("this.props.isModalOpen = " + this.props.isModalOpen);
    
    console.log('_________________FeatureEditModal render()');

    const getLatitude = () => {
      try {         
        return this.state.featureData.coordinates[1]; 
      } 
      catch (e) {
        return -2;
      } 
    }

    const getLongitude = () => {
      try {         
        return this.state.featureData.coordinates[0]; 
      } 
      catch (e) {
        return -2;
      } 
    }

    const getFieldValue = (f) => {
      return this.state.fieldValues[f.identifier];            
    }

    let featureType = this.props.featureType;

    // for test    
    featureType.properties = {
      fields: [
        {
          identifier: 'radius',
          title: 'Radius',
          type: 'decimal',
          description: '',
          required: false
        },
        {
          identifier: 'depth',
          title: 'Depth',
          type: 'decimal',
          description: '',
          required: false
        },
        {
          identifier: 'test_string',
          title: 'Test string',
          type: 'string',
          description: '',
          required: false
        },
        {
          identifier: 'test_selection',
          title: 'Test select',
          type: 'selection',
          selectValues: ['x1', 'x2', 'zzzzzzzz', 'xxxxxxxx'],
          defaultValue: 'x2',
          description: '',
          required: false
        },
      ]
    };

    // const classes = useStyles();
    const { classes } = this.props;

    let keyIndex: number = 0;

    if (this.state.featureData) {
    return (
      <div>
        {/* <Button onClick={this.handleOpen}>Add feature</Button> */}
        {/* open={this.props.isModalOpen} */}
        {/* show={this.state.isModalOpen}  */}
        {/* onHide={this.handleClose} */}
        <Modal
          open={this.props.isModalOpen}

          onClose={this.handleClose}
          aria-labelledby="feature-edit-modal-title"
          aria-describedby="feature-edit-modal-description"
        >
          {
            this.state.featureData &&
            <span>

          <Box sx={boxStyle} component="form" autoComplete="off" >
            <TabContext value={tabValue + ''}>
              <Box sx={{
                borderBottom: 1,
                borderColor: 'divider'
              }}
              >
                <TabList onChange={this.handleTabChange} aria-label="xx">
                  <Tab label="General" value="1" />
                  <Tab label="Media" value="2" />                  
                </TabList>
              </Box>
              <TabPanel value="1">
              
              <div>
                  {/* InputLabelProps={{ shrink: true, }}  */}
                  <TextField id="name" label="Name" variant="outlined" size="small" value={this.state.featureData.name} onChange={this.handleNameChange} style={{ width: '80%' }} onKeyPress={this.handleKeyPress} />
              </div>

              {
                featureType.properties.fields.map((f) => //(
                {
                  if (f.type == 'decimal')
                  {
                     return <div>
                       <TextField 
                         key={(keyIndex++)}
                         id={f.identifier} 
                         label={f.title} // internationalization                         
                         variant="outlined" 
                         type="number" 
                         InputLabelProps={{ shrink: true, }} 
                         size="small" 
                         value={getFieldValue(f)} 
                         onChange={ (event) => this.handleFieldChange(event, f) } 
                       />
                       {/* <span>{getFieldValue(f)}</span> for testing state update */}
                     </div>;
                  }
                  else if (f.type == 'string')
                  {
                    return <div>
                      <TextField 
                        key={(keyIndex++)}
                        id={f.identifier} 
                        label={f.title}                         
                        variant="outlined" 
                        type="text" 
                        InputLabelProps={{ shrink: true, }} 
                        size="small" 
                        value={getFieldValue(f)} 
                        onChange={ (event) => this.handleFieldChange(event, f) } 
                      />
                      {/* <span>{getFieldValue(f)}</span> for testing state update */}
                    </div>;                     
                  }
                  else if (f.type == 'selection')
                  {
                    return <div>
                      <Select 
                        key={(keyIndex++)}
                        labelId={f.identifier}
                        id={f.identifier}
                        value={this.state.featureData.feature_type_id}
                        label="Feature type"
                        onChange={ (event) => this.handleFieldChange(event, f) }
                        style={{ display: 'block' }}
                        // classes={{ root: this.useStyles() }}
                        inputProps={{selectmenuitem: '1'}}
                      >
                        { 
                          f.selectValues.map((sv) => {
                            // classes={{ root: this.useStyles().root }}
                            return <MenuItem key={sv} value={sv} inputProps={{selectmenuitem: '1'}} 
                            classes={{
                              root: classes.menuItemRoot }}
                            >{sv}</MenuItem>
                          })
                        }
                      </Select>
  
                      {/* <span>{getFieldValue(f)}</span> for testing state update */}
                    </div>;                     
                  }                  
                //)
                })
              }

                {/* https://mui.com/material-ui/react-text-field/#inputs */}
                <div>
                  {/* <input id="latitude" label="Latitude" variant="outlined" type="number" value={ latitude } /> */}
                  {/* <Typography id="label_lat_long" variant="h4" component="h4">(Main) entrance</Typography> */}
                  { 
                   featureType.type == 'point' &&
                   <span>
                    <span>{'Point:'}</span>
                    <TextField key={(keyIndex++)} id="latitude" label="Latitude" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={getLatitude()} onChange={this.handleLatitudeChange} />
                    {/* valueLink={this.linkState('latitude')}  */}

                    {/* <TextField id="latitude" label="Latitude" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.featureData.latitude} onChange={this.handleLatChange} /> */}
                    <TextField key={(keyIndex++)} id="longitude" label="Longitude" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={getLongitude()} onChange={this.handleLongitudeChange} />
                   </span>
                  }

                  <span>{this.state.featureData.latitude}x{this.state.featureData.longitude}</span>
                </div>

                <div>
                  {/* InputLabelProps={{ shrink: true, }}  */}
                  <TextField key={(keyIndex++)} id="description" label="Description" variant="outlined" size="small" value={this.state.featureData.description} onChange={this.handle_description_Change} style={{ width: '80%' }} multiline rows={4} />
                </div>

              </TabPanel>
              <TabPanel value="2">
              </TabPanel>

            </TabContext>

            <Button variant="contained" onClick={this.handleSave}>Save</Button>
            <Button variant="outlined" onClick={this.handleButtonCancel}>Cancel</Button>

            {/* <span>{this.state.notificationOpened ? "opened" : "closed"}</span> */}
            {this.state.notificationOpened &&
              <NotificationSnackbar
                isOpen={this.state.notificationOpened}
                message={this.state.notificationMessage}
                onOpenStateChange={this.handleNotificationOpenStateChange}
                alertSeverity={this.state.notificationSeverity}
              />
            }

          </Box>

            </span>
        }

        </Modal>
      </div>
    );
      }
      else
      {
    return (
      <div>loading</div>
        )
      }

  }
}

export default withStyles(styles)(FeatureEditModal);
// export default withStyles(styles)(NavigationBar);
