/* eslint-disable no-console */
/* eslint-disable indent */
/* eslint-disable no-trailing-spaces */
/* eslint-disable max-len */
/* eslint-disable react/jsx-max-props-per-line */
/* eslint-disable object-curly-newline */
import * as React from 'react';

import { withStyles } from '@material-ui/core/styles';
import Tab from '@material-ui/core/Tab';
import TabContext from '@material-ui/lab/TabContext';
import TabList from '@material-ui/lab/TabList';
import TabPanel from '@material-ui/lab/TabPanel';
import CloseIcon from '@mui/icons-material/Close';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormGroup from '@mui/material/FormGroup';
import IconButton from '@mui/material/IconButton';
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

import CaveAPI from '../../middleware/CaveAPI';
import NotificationSnackbar from '../NotificationSnackbar';
import Entrance from '../../data/models/Entrance';


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

const styles = {
  'MuiButtonBase-root': {
    display: 'block',
    // 'background-color': 'green'
  },
  // 'MuiTab-root': {
  //   'minWidth': '20px'
  // }
  /* '& .MuiButtonBase-root': {
    display: 'block',
    // 'background-color': 'green'
  }
  */
};

class CaveEditModal extends React.Component {
  // [x: string]: (ev: any) => any;

  // mixins: [LinkedStateMixin];

  // state = {
  //   latitude: 2,
  //   longitude:5,
  //   openModal : false,

  //   tabValue: '1',
  //   // cave_type_id: 'unknown'
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

      caveData: {
        entrances: [
          new Entrance({
          // new Entrance {
            coordinates: this.props.addCaveEntranceCoords,
            title: 'entrance title'
          })
        ],
        // Array<Entrance>,
        // latitude: this.props.addCaveEntranceCoords ? this.props.addCaveEntranceCoords[1] : 0,
        // longitude: this.props.addCaveEntranceCoords ? this.props.addCaveEntranceCoords[0] : 0,        
        id: undefined,
        name: '',
        other_toponyms: '',
        identification_code: '',
        cave_type_id: 0, // 'unknown',
        description: '',
        rock_type: '',
        rock_age: '',
        cave_age: '',
        region: '',
        hydrographic_basin: '',
        valley: '',
        tributary_river: '',
        closest_address: '',
        land_registry_number: '',
        depth: null,
        positive_denivelation: null,
        negative_denivelation: null,
        surveyed_length: null,
        extension_3d: null,
        plane_extension: null,
        area: null,
        volume: null,
        ramification_index: null,
        discovery_date: '',
        discoverer: '',
        is_show_cave: '',
        show_cave_length: '',
        protection_class: ''
      }
    };

    console.log('_____________CaveEditModal constructor ()');

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

    // this.handleLatitudeChange = updateNestedFieldEvent('caveData.latitude');
    // this.handleLongitudeChange = updateNestedFieldEvent('caveData.longitude');

    this.handleLatitudeChange = this.handleLatitudeChange.bind(this);
    this.handleLongitudeChange = this.handleLongitudeChange.bind(this);

    this.handleNameChange = updateNestedFieldEvent('caveData.name');
    this.handleOtherToponymsChange = updateNestedFieldEvent('caveData.other_toponyms');
    this.handle_identification_code_Change = updateNestedFieldEvent('caveData.identification_code');
    this.handle_cave_type_id_Change = updateNestedFieldEvent('caveData.cave_type_id');
    this.handle_description_Change = updateNestedFieldEvent('caveData.description');

    this.handle_rock_type_Change = updateNestedFieldEvent('caveData.rock_type');
    this.handle_rock_age_Change = updateNestedFieldEvent('caveData.rock_age');
    this.handle_cave_age_Change = updateNestedFieldEvent('caveData.cave_age');
    this.handle_region_Change = updateNestedFieldEvent('caveData.region');
    this.handle_hydrographic_basin_Change = updateNestedFieldEvent('caveData.hydrographic_basin');
    this.handle_valley_Change = updateNestedFieldEvent('caveData.valley');
    this.handle_tributary_river_Change = updateNestedFieldEvent('caveData.tributary_river');
    this.handle_closest_address_Change = updateNestedFieldEvent('caveData.closest_address');
    this.handle_land_registry_number_Change = updateNestedFieldEvent('caveData.land_registry_number');

    this.handle_positive_denivelation_Change = updateNestedFieldEvent('caveData.positive_denivelation');
    this.handle_negative_denivelation_Change = updateNestedFieldEvent('caveData.negative_denivelation');
    this.handle_surveyed_length_Change = updateNestedFieldEvent('caveData.surveyed_length');
    this.handle_extension_3d_Change = updateNestedFieldEvent('caveData.extension_3d');
    this.handle_depth_Change = updateNestedFieldEvent('caveData.depth');
    this.handle_area_Change = updateNestedFieldEvent('caveData.area');
    this.handle_volume_Change = updateNestedFieldEvent('caveData.volume');
    this.handle_ramification_index_Change = updateNestedFieldEvent('caveData.ramification_index');

    this.handle_discovery_date_Change = updateNestedFieldEvent('caveData.discovery_date');
    this.handle_discoverer_Change = updateNestedFieldEvent('caveData.discoverer');
    this.handle_is_show_cave_Change = updateNestedFieldEvent('caveData.is_show_cave');
    this.handle_ramification_show_cave_length_Change = updateNestedFieldEvent('caveData.ramification_show_cave_length');
    this.handle_protection_class_Change = updateNestedFieldEvent('caveData.protection_class');

    // this.handleTabChange = (event: React.SyntheticEvent, newValue: string) => {
    //   // setTabValue(newValue);
    // };  

    this.handleTabChange = this.handleTabChange.bind(this);

    this.handleSave = this.handleSave.bind(this);
    this.handleButtonCancel = this.handleButtonCancel.bind(this);

    this.handleNotificationOpenStateChange = this.handleNotificationOpenStateChange.bind(this);

    this.handleKeyPress = this.handleKeyPress.bind(this);
    // this.showModal = this.showModal.bind(this);
    // this.handleShow = this.handleShow.bind(this);
  }
  // const [open, setOpen] = React.useState(false);
  // const handleOpen = () => setOpen(true);
  // const handleClose = () => setOpen(false);

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
    alert('A val was changed: ' + this.state.caveData.latitude);
    event.preventDefault();
  }

  handleTabChange(event: React.SyntheticEvent, newValue: string) {
    // console.log(event);
    // console.log(newValue);

    this.setState({ tabValue: newValue });
  }

  handleKeyPress(event: React.SyntheticEvent) {
    if (event.key === 'Enter') {
      this.handleSave(event);
    }
  }

  handleSave(event) {
    console.log('handleSave()');
    // event.preventDefault();

    // new CaveAPI().Save();
    if (this.state.caveData.id)
    {
      CaveAPI.Save(this.state.caveData)
      .then(res => res.json())
      .then(
        (result) => {
          // console.log("fetch then");
          // console.log(result);

          if (result.success)
          {
            this.showNotification('Cave data was saved', 'success');
            // this.props.wasSaved = true;
            this.props.onSaveCave(result.data);

            this.props.onOpenStateChange(false, true);            
            //this.handleButtonCancel();
            //this.handleClose();
          }
          else
            this.showNotification('Error saving cave data:  ' + result.message + '"', 'error');
        },
        // Note: it's important to handle errors here instead of a catch() block so that we don't swallow exceptions from actual bugs in components.
        (error) => {
          this.showNotification('Error saving cave data (server failure error)', 'error');
          // this.setState({
          //   isLoaded: true,
          //   error
          // });
        }
      )
      .catch((error) => {
        this.showNotification('Error saving cave data (prob. error in client code)', 'error');
      });
    }
    else
    {
      CaveAPI.Add(this.state.caveData)
        .then(res => res.json())
        .then(
          (result) => {
            // console.log("fetch then");
            // console.log(result);

            if (result.success)
            {
              this.showNotification('Cave data was saved', 'success');
              // this.props.wasSaved = true;
              this.props.onSaveCave(result.data);

              this.props.onOpenStateChange(false, true);
              //this.handleButtonCancel();
              //this.handleClose();
            }
            else
              this.showNotification('Error saving cave data:  ' + result.message + '"', 'error');
          },
          // Note: it's important to handle errors here instead of a catch() block so that we don't swallow exceptions from actual bugs in components.
          (error) => {
            this.showNotification('Error saving cave data (server failure error)', 'error');
            // this.setState({
            //   isLoaded: true,
            //   error
            // });
          }
        )
        .catch((error) => {
          this.showNotification('Error saving cave data (prob. error in client code)', 'error');
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
    let entrances = this.state.caveData.entrances;
    entrances[0].coordinates[1] = event.target.value;

    this.setState({ 
      caveData: 
      {
        ...this.state.caveData,
        entrances:  entrances
      }
      }); 
  }

  handleLongitudeChange(event) { 
    let entrances = this.state.caveData.entrances;
    entrances[0].coordinates[0] = event.target.value;

    this.setState({ 
      caveData: {
        ...this.state.caveData,
        entrances:  entrances
      }});
  }
  // handleLatChange(event) { this.setState({latitude: event.target.value }); }

  // handleSelectChange (event: SelectChangeEvent) {    
  //   this.setState({rock_type: event.target.value as string});
  // };

  // https://reactjs.org/docs/state-and-lifecycle.html
  componentDidMount () {
    console.log('_____________CaveEditModal componentDidMount ()');
    console.log('modalCaveEditMode=' + this.props.mode);

    if (this.props.mode == 'add')
    {
      // console.log('mode=' + this.props.mode);
    }
    else
    if (this.props.mode == 'edit')
    {
      let caveId = this.props.caveId;

      CaveAPI.GetById(caveId).then(res => res.json())
      .then(
        (res) => {
          console.log('get response data ok');

          console.log(res.data);

          if (res.success == false)
            alert("Error: " + res.message);
          else
            this.setState({ caveData: res.data });
          // Promise.resolve(result);
        },
        (error) => {
          console.log('Error (er): ' + error);
        }
      )
      .catch((error) => {
        alert('Error saving cave data: ' + error);
      });

      console.log('cave data =');
      // console.log(caveData);
    }
    else
      console.warn('unknown modalCaveEditMode=' + this.props.mode);
  }

  componentWillUnmount() {
    console.log('_____________CaveEditModal componentWillUnmount ()');
  }

  render () {
    const { /* latitude, */ tabValue } = this.state;
    // $users = this.$at( 'users' ),

    // latitude = this.state.caveData.latitude;

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
    
    console.log('_________________CaveEditModal render()');

    const getLatitude = () => {
      try {         
        return this.state.caveData.entrances[0].coordinates[1]; 
      } 
      catch (e) {
        return -2;
      } 
    }

    const getLongitude = () => {
      try {         
        return this.state.caveData.entrances[0].coordinates[0]; 
      } 
      catch (e) {
        return -2;
      } 
    }

    if (this.state.caveData) {
    return (
      <div>
        {/* <Button onClick={this.handleOpen}>Add cave</Button> */}
        {/* open={this.props.isModalOpen} */}
        {/* show={this.state.isModalOpen}  */}
        {/* onHide={this.handleClose} */}
        <Modal
          open={this.props.isModalOpen}

          onClose={this.handleClose}
          aria-labelledby="cave-edit-modal-title"
          aria-describedby="cave-edit-modal-description"
        >
          {
            this.state.caveData &&
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
                  <Tab label="Geology" value="2" />
                  <Tab label="Location" value="3" />
                  <Tab label="Topometry" value="4" />
                  <Tab label="Other" value="5" />
                  <Tab label="Media" value="6" />
                  <Tab label="Entrances" value="7" />
                </TabList>
              </Box>
              <TabPanel value="1">

                {/* https://mui.com/material-ui/react-text-field/#inputs */}
                <div>
                  {/* <input id="latitude" label="Latitude" variant="outlined" type="number" value={ latitude } /> */}
                  {/* <Typography id="label_lat_long" variant="h4" component="h4">(Main) entrance</Typography> */}
                  <span>{'(Main) entrance:'}</span>
                  <TextField id="latitude" label="Latitude" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={getLatitude()} onChange={this.handleLatitudeChange} />
                  {/* valueLink={this.linkState('latitude')}  */}

                  {/* <TextField id="latitude" label="Latitude" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.caveData.latitude} onChange={this.handleLatChange} /> */}
                  <TextField id="longitude" label="Longitude" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={getLongitude()} onChange={this.handleLongitudeChange} />

                  <span>{this.state.caveData.latitude}x{this.state.caveData.longitude}</span>
                </div>

                <div>
                  {/* InputLabelProps={{ shrink: true, }}  */}
                  <TextField id="name" label="Name" variant="outlined" size="small" value={this.state.caveData.name} onChange={this.handleNameChange} style={{ width: '80%' }} onKeyPress={this.handleKeyPress} />
                </div>

                <div>
                  {/* InputLabelProps={{ shrink: true, }}  */}
                  <TextField id="other_toponyms" label="Other toponyms" variant="outlined" size="small" value={this.state.caveData.other_toponyms} onChange={this.handleOtherToponymsChange} style={{ width: '80%' }} />
                </div>

                <div>
                  {/* InputLabelProps={{ shrink: true, }}  */}
                  <TextField id="identification_code" label="Identification code" variant="outlined" size="small" value={this.state.caveData.identification_code} onChange={this.handle_identification_code_Change} style={{ width: '80%' }} />
                </div>

                {/* alternative component: NativeSelect */}
                <Select
                  labelId="cave_type_id"
                  id="cave_type_id"
                  value={this.state.caveData.cave_type_id}
                  label="Cave type"
                  onChange={this.handle_cave_type_id_Change}
                >
                  <MenuItem key={1} value={1}>t1</MenuItem><br />
                  <MenuItem key={2} value={2}>t2</MenuItem><br />
                  <MenuItem key={3} value={3}>t3</MenuItem>
                </Select>

                {/* <div>              
              <TextField id="cave_type_id" label="Cave type" variant="outlined" size="small" value={this.state.caveData.other_toponyms} onChange={this.handle_other_toponyms_Change} style={{width: '80%'}} />
            </div> */}

                <div>
                  {/* InputLabelProps={{ shrink: true, }}  */}
                  <TextField id="description" label="Description" variant="outlined" size="small" value={this.state.caveData.description} onChange={this.handle_description_Change} style={{ width: '80%' }} multiline rows={4} />
                </div>

                {/* <TextField id="cave-name" label="Name" variant="outlined"size="small" /> */}
                              
              </TabPanel>
              <TabPanel value="2">
                {/* alternative component: NativeSelect */}
                <Select
                  labelId="rock_type"
                  id="rock_type"
                  value={this.state.caveData.rock_type}
                  label="Rock type"
                  onChange={this.handle_rock_type_Change}

                  sx={{ display: 'block' }}
                >
                  <MenuItem value={'unknown'}>unknown</MenuItem> <br />
                  <MenuItem value={'limestone'}>limestone</MenuItem> <br />
                  <MenuItem value={'basalt'}>basalt</MenuItem> <br />
                  <MenuItem value={'gypsum'}>gypsum</MenuItem> <br />
                  <MenuItem value={'dolomite'}>dolomite</MenuItem> <br />
                  <MenuItem value={'crystalline_schist'}>crystalline_schist</MenuItem> <br />
                  <MenuItem value={'ice'}>ice</MenuItem> <br />
                  <MenuItem value={'lava'}>lava</MenuItem> <br />
                  <MenuItem value={'chalk'}>chalk</MenuItem> <br />
                  <MenuItem value={'salt'}>salt</MenuItem> <br />
                  <MenuItem value={'other'}>other</MenuItem> <br />
                </Select>
                <span>{this.state.caveData.rock_type}{this.state.caveData.cave_type_id}</span>

                <div>
                  <TextField id="rock_age" label="Rock age" variant="outlined" size="small" value={this.state.caveData.rock_age} onChange={this.handle_rock_age_Change} style={{ width: '80%' }} />
                </div>

                <div>
                  <TextField id="cave_age" label="Cave age" variant="outlined" size="small" value={this.state.caveData.cave_age} onChange={this.handle_cave_age_Change} style={{ width: '80%' }} />
                </div>

              </TabPanel>
              <TabPanel value="3">

                <div>
                  <TextField id="region" label="Region" variant="outlined" size="small" value={this.state.caveData.region} onChange={this.handle_region_Change} style={{ width: '80%' }} />
                </div>

                <div>
                  <TextField id="hydrographic_basin" label="Hydrographic basin" variant="outlined" size="small" value={this.state.caveData.hydrographic_basin} onChange={this.handle_hydrographic_basin_Change} style={{ width: '80%' }} />
                </div>

                <div>
                  <TextField id="valley" label="Valley" variant="outlined" size="small" value={this.state.caveData.valley} onChange={this.handle_valley_Change} style={{ width: '80%' }} />
                </div>

                <div>
                  <TextField id="tributary_river" label="Tributary river" variant="outlined" size="small" value={this.state.caveData.tributary_river} onChange={this.handle_tributary_river_Change} style={{ width: '80%' }} />
                </div>

                <div>
                  <TextField id="closest_address" label="Closest address" variant="outlined" size="small" value={this.state.caveData.closest_address} onChange={this.handle_closest_address_Change} style={{ width: '80%' }} />
                </div>

                <div>
                  <TextField id="land_registry_number" label="Land registry number" variant="outlined" size="small" value={this.state.caveData.land_registry_number} onChange={this.handle_land_registry_number_Change} style={{ width: '80%' }} />
                </div>

              </TabPanel>

              <TabPanel value="4">

                <div>
                  <TextField id="depth" label="Depth" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.caveData.depth} onChange={this.handle_depth_Change} />
                </div>

                <div>
                  <TextField id="positive_denivelation" label="Positive denivelation" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.caveData.positive_denivelation} onChange={this.handle_positive_denivelation_Change} />
                </div>

                <div>
                  <TextField id="negative_denivelation" label="Negative denivelation" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.caveData.negative_denivelation} onChange={this.handle_negative_denivelation_Change} />
                </div>

                <div>
                  <TextField id="surveyed_length" label="Surveyed length" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.caveData.surveyed_length} onChange={this.handle_surveyed_length_Change} />
                </div>

                {/* potential_depth */}
                <div>
                  <TextField id="extension_3d" label="3D extension" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.caveData.extension_3d} onChange={this.handle_extension_3d_Change} />
                </div>

                <div>
                  <TextField id="plane_extension" label="Plane extension" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.caveData.plane_extension} onChange={this.handle_depth_Change} />
                </div>

                <div>
                  <TextField id="area" label="Area" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.caveData.area} onChange={this.handle_area_Change} />
                </div>

                <div>
                  <TextField id="volume" label="Volume" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.caveData.volume} onChange={this.handle_volume_Change} />
                </div>

                <div>
                  <TextField id="ramification_index" label="Ramification index" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.caveData.ramification_index} onChange={this.handle_ramification_index_Change} />
                </div>

              </TabPanel>
              <TabPanel value="5">

                <div>
                  <TextField id="discovery_date" label="Discovery date" variant="outlined" size="small" value={this.state.caveData.discovery_date} onChange={this.handle_discovery_date_Change} style={{ width: '80%' }} />
                </div>

                <div>
                  <TextField id="discoverer" label="Discoverer" variant="outlined" size="small" value={this.state.caveData.discoverer} onChange={this.handle_discoverer_Change} style={{ width: '80%' }} />
                </div>

                <div>
                  <FormControlLabel control={<Checkbox id="is_show_cave" onChange={this.handle_is_show_cave_Change} />} label="Show cave" />
                </div>

                <div>
                  <TextField id="show_cave_length" label="Show cave length" variant="outlined" type="number" InputLabelProps={{ shrink: true, }} size="small" value={this.state.caveData.show_cave_length} onChange={this.handle_ramification_show_cave_length_Change} />
                </div>

                <div>
                  <TextField id="protection_class" label="Protection class" variant="outlined" size="small" value={this.state.caveData.protection_class} onChange={this.handle_protection_class_Change} style={{ width: '80%' }} />
                </div>

              </TabPanel>
              <TabPanel value="6">

              </TabPanel>

              {/* entrances */}
              <TabPanel value="7">
                <TableContainer component={Paper}>
                  <Table sx={{ minWidth: 650 }} aria-label="simple table">
                    <TableHead>
                      <TableRow>
                        <TableCell>Title</TableCell>
                        <TableCell align="right">Latitude</TableCell>
                        <TableCell align="right">Longitude</TableCell>
                        {/* <TableCell align="right">Carbs&nbsp;(g)</TableCell>
                        <TableCell align="right">Protein&nbsp;(g)</TableCell> */}
                      </TableRow>
                    </TableHead>
                    <TableBody>                    
                      {/* key={en.title + `_enkey-${Math.random()}`} */}
                      { this.state.caveData && this.state.caveData.entrances && 
                        this.state.caveData.entrances.map((en) => (
                          en.coordinates &&
                        <TableRow
                          key={'entrance_title_tr' + `_key_` + en.coordinates[0]}
                          sx={{ '&:last-child td, &:last-child th': { border: 0 } }}
                        >
                          <TableCell component="th" scope="row">
                            {/* id={'entrance_title' + `_id-${Math.random()}`} */}
                            <TextField key={'entrance_title_tf' + `_key_` + en.coordinates[0]} label="Title" variant="outlined" size="small" value={en.title} 
                              onChange={(event) => {
                                  // event.preventDefault();
                                  let entrances = this.state.caveData.entrances;
                                  
                                  let index = entrances.indexOf(en);                                  
                                  entrances[index].title = event.target.value;
                              
                                  this.setState({ 
                                    caveData: {
                                      ...this.state.caveData,
                                      entrances: entrances
                                    }});                              
                            }} 
                            style={{ width: '80%' }} />
                            {/* <div>key={'entrance_title' + `_key_` + en.coordinates[0]}</div>
                            <div>id={'entrance_title' + `_id-${Math.random()}`}</div> */}
                            {/* {en.title} */}
                          </TableCell>
                          <TableCell align="right">{en.coordinates ? en.coordinates[1] : 'x'}</TableCell>
                          <TableCell align="right">{en.coordinates ? en.coordinates[0] : 'x'}</TableCell>
                          {/* <TableCell align="right">{row.carbs}</TableCell>
                          <TableCell align="right">{row.protein}</TableCell> */}
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>

                  <Button variant="contained" onClick={
                    () => {
                      console.log(this.state.caveData);
                      console.log(this.state.caveData.entrances);
                      console.log(this.state.caveData.cave_type_id);
                    }
                  }>test</Button>

                </TableContainer>
              </TabPanel>

            </TabContext>
            {/*
          __general
          other_toponyms
          identification_code
          cave_type_id
          description

          __geology
          rock_type
          rock_age
          cave_age

          __location
          region
          hydrographic_basin
          valley
          tributary_river
          closest_address
          land_registry_number

          __topometry
          depth
          positive_depth
          negative_depth
          potential_depth
          surveyed_length
          real_extension / extension_3d
          projected_extension / plane_extension
          area
          volume
          ramification_index

          __other
          discovery_date
          discoverer
          exploration_status
          is_show_cave
          show_cave_length
          protection_class

          __entrances
          */}

            {/* <Typography id="modal-modal-title" variant="h6" component="h2">
            pestera
            </Typography>
            <Typography id="modal-modal-description" sx={{ mt: 2 }}>
            Peștera ȘȚÎĂ:;îăpțș.
            </Typography> */}
            {/* <span>_{this.state.isModalOpen ? 'x' : '0'}_</span> */}
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

export default withStyles(styles)(CaveEditModal);
// export default withStyles(styles)(NavigationBar);
