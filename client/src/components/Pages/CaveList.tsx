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


// import LinkedStateMixin from 'react-addons-linked-state-mixin';
// import linkState from 'react-link-state';

// var LinkedStateMixin = require('react-addons-linked-state-mixin');
// var LinkedStateMixin = require('react-addons-linked-state-mixin'); // ES5 with npm
// var LinkedStateMixin = require('react-link-state'); // ES5 with npm

// import { useLink } from 'valuelink';
// import Link, { LinkedComponent } from 'valuelink';

// import { makeStyles } from '@material-ui/core/styles';
// import AppBar from '@material-ui/core/AppBar';

import { Space, Table, Tag } from 'antd';
import type { ColumnsType, TableProps } from 'antd/lib/table';

import CaveAPI from '../../middleware/CaveAPI';
import NotificationSnackbar from '../NotificationSnackbar';

import CaveEditModal from '../Forms/CaveEditModal';

// import Tab from '@mui/material/Tab';
// import TabContext from '@mui/lab/TabContext';
// import TabList from '@mui/lab/TabList';
// import TabPanel from '@mui/lab/TabPanel';

// import CaveAPI from './middleware/CaveAPI';

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
  '& .MuiTextField-root': { m: 1, width: '25ch' },
};

const styles = {
  'MuiButtonBase-root': {
    display: 'block',
    // 'background-color': 'green'
  },
  /* '& .MuiButtonBase-root': {
    display: 'block',
    // 'background-color': 'green'
  }
  */
};

// ///////////////////////
// datatable

// https://ant.design/components/table/

interface DataType {
  key: string;
  id: number;
  name: string;
  type_id: number;
  tags: string[];
}

class CaveList extends React.Component {
  // [x: string]: (ev: any) => any;

  // mixins: [LinkedStateMixin];

  // state = {
  //   modalCaveEditOpened: false
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

      modalCaveEditCaveId: undefined,
      modalCaveEditMode: 'add',
      modalCaveEditOpened: false,
      // modalCaveEditWasSaved: false,

      notificationOpened: false,
      notificationMessage: '',
      notificationSeverity: '',

      caveList: []
    };

    console.log('_____________CaveList constructor ()');

    if (props.isModalOpen) { console.log('isModalOpen = true'); }

    const updateFieldEvent = key => ev => { this.setState({ [key]: ev.target.value }); };

    const updateNestedFieldEvent = key => ev => {

      var [parentProperty, _key] = key.split('.', 2);

      // console.log ("updateNestedFieldEvent()");      console.log ("pp=" + parentProperty);      console.log ("key=" + _key);
      this.setState({ [parentProperty]: { ...this.state[parentProperty], [_key]: ev.target.value } });
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

    this.handleTabChange = this.handleTabChange.bind(this);

    this.handleSave = this.handleSave.bind(this);
    this.handleButtonCancel = this.handleButtonCancel.bind(this);

    this.handleNotificationOpenStateChange = this.handleNotificationOpenStateChange.bind(this);

    this.onTablePropsChange = this.onTablePropsChange.bind(this);
    // this.showModal = this.showModal.bind(this);
    // this.handleShow = this.handleShow.bind(this);

    this.handleAddCave = this.handleAddCave.bind(this);

    this.handleCEMOpenStateChange = this.handleCEMOpenStateChange.bind(this);
    this.handleOpenCaveEditNavMenu = this.handleOpenCaveEditNavMenu.bind(this);

    this.handleCaveTitleClick = this.handleCaveTitleClick.bind(this);
    this.handleCaveDelete = this.handleCaveDelete.bind(this);
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
    this.props.onOpenStateChange(false);
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

  handleSave(event) {
    console.log('handleSave()');
    // event.preventDefault();

    // new CaveAPI().Save();
    CaveAPI.Save(this.state.caveData)
      .then(res => res.json())
      .then(
        (result) => {
          // console.log("fetch then");
          // console.log(result);

          if (result.success)
            this.showNotification('Cave data was saved', 'success');
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

  handleAddCave() {
    console.log('handleOpen()');
    //this.setCEOpen(true);
    this.openCaveEditModal('add', undefined);
  }

  setCEOpen(val) {
    console.log("setCEOpen(" + val + ")");
    this.setState({ modalCaveEditOpened: val });
  }

  handleOpenCaveEditNavMenu() {
    console.log("handleOpenCaveEditNavMenu()");
    // this.setState({modalCaveEditOpened: true});
    this.setCEOpen(true);
    // caveEditModalRef.current.handleOpen();
  };

  showNotification(message, notificationAlertType) {
    this.setState({
      notificationOpened: true,
      notificationMessage: message,
      notificationSeverity: notificationAlertType
    });
  }

  handleNotificationOpenStateChange(openState: boolean) {
    // console.log("handleNotificationOpenStateChange()");

    this.setState({ notificationOpened: openState });
  };

  handleCEMOpenStateChange(openState: boolean, wasSaved?: boolean) {
    console.log("handleCEMOpenStateChange()");
    this.setState({ modalCaveEditOpened: openState });

    // console.log("wasSaved " + wasSaved); // this.state.modalCaveEditWasSaved

    if (!openState && wasSaved) // this.state.modalCaveEditWasSaved
    {
      console.log("saved, refresh");
      this.loadData();
    }
  };

  handleCaveTitleClick(event, caveId?: number) {
    console.log('handleCaveTitleClick()');
    // console.log(event);

    if (caveId == undefined)
      alert("caveId == undefined")
    else
      this.openCaveEditModal('edit', caveId);
  };

  handleCaveDelete(event, caveId: number, caveName: string) {
    console.log(`handleCaveDelete(id=${caveId}, name='${caveName}')`);
    // console.log(event);

    if (caveId == undefined)
      alert("caveId == undefined");
    else {
      if (confirm("Are you sure you want to delete cave '" + caveName + "' ?") == true) {
        {
          this.deleteCave(caveId);
          // this.openCaveEditModal('edit', caveId);
        }
      }
    };
  }

  deleteCave(caveId: number) {
    CaveAPI.Delete(caveId)
      .then(res => res.json())
      .then(
        (result) => {
          // console.log("fetch then");
          // console.log(result);

          if (result.success)
          {
            this.showNotification('Cave was deleted', 'success');
            this.loadData();
          }
          else
            this.showNotification('Error deleting cave:  ' + result.message + '"', 'error');
        },
        // Note: it's important to handle errors here instead of a catch() block so that we don't swallow exceptions from actual bugs in components.
        (error) => {
          this.showNotification('Error deleting cave (server failure error)', 'error');
        }
      )
      .catch((error) => {
        this.showNotification('Error saving cave data (prob. error in client code)', 'error');
      });
  }

  openCaveEditModal(addOrEdit: string, caveId?: number) {
    this.setState({
      modalCaveEditOpened: true,
      // addCaveEntranceCoords: [entranceCoords[0], entranceCoords[1]],
      modalCaveEditMode: addOrEdit,
      modalCaveEditCaveId: caveId
    });
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

  // handleLatChange(event) { this.setState({latitude: event.target.value }); }

  // handleSelectChange (event: SelectChangeEvent) {    
  //   this.setState({rock_type: event.target.value as string});
  // };

  // const onChange: TableProps<DataType>['onChange'] = (pagination, filters, sorter, extra) => {
  //   console.log('params', pagination, filters, sorter, extra);
  // };
  onTablePropsChange(pagination, filters, sorter, extra) {
    console.log('onTablePropsChange() params', pagination, filters, sorter, extra);
  };

  // https://reactjs.org/docs/state-and-lifecycle.html
  componentDidMount() {
    console.log('_____________CaveList componentDidMount ()');

    this.loadData();

    console.log('cave data =');
    // console.log(caveData);
  }

  private loadData() {
    CaveAPI.GetAll().then(res => res.json())
      .then(
        (res) => {
          console.log('get response data ok');

          console.log(res);

          for (let index = 0; index < res.data.length; index++)
            res.data[index].key = res.data[index].id;

          this.setState({ caveList: res.data });
          // Promise.resolve(result);
        },
        (error) => {
        }
      );
  }

  componentWillUnmount() {
    console.log('_____________CaveList componentWillUnmount ()');
  }

  render() {
    // const { /* latitude, */ tabValue } = this.state;
    // $users = this.$at( 'users' ),

    // latitude = this.state.caveData.latitude;

    // const state$ = this.state$();
    // { latitude } = this.state$();

    // const [tabValue] = this.state;

    // this.state.isModalOpen = this.props.isModalOpen;

    // if (this.props.isModalOpen)
    // {
    //   // this.state.isModalOpen = this.props.isModalOpen;
    //   // this.props.isModalOpen = false;
    // }

    // console.log("this.state.isModalOpen = " + this.state.isModalOpen);
    // console.log("this.props.isModalOpen = " + this.props.isModalOpen);

    console.log('_________________CaveList render()');

    const columns: ColumnsType<DataType> = [
      {
        title: 'Id',
        dataIndex: 'id',
        key: 'id',
        filterMode: 'tree',
        filterSearch: true,
        // defaultFilteredValue: true,
        // onFilter: (value: string, record) => record.name.includes(value),    
        sorter: (a, b) => a.id - b.id,
        defaultSortOrder: 'descend' // 'ascend' | 'descend'
      },
      {
        title: 'Name',
        dataIndex: 'name',
        key: 'name', // id?
        render: (text, data) => <a onClick={event => this.handleCaveTitleClick(event, data.id)} >{text}</a>,
        //filterMode: 'tree',
        filterSearch: true,
        // onFilter: (value: string, record) => record.name.includes(value),
        // sorter: (a, b) => a.age - b.age,
        sorter: (a, b) => b.name.localeCompare(a.name), // a.localeCompare(b, 'en') 
        // https://dev.to/maciekgrzybek/ultimate-guide-to-sorting-in-javascript-and-typescript-4al9    

        // sorter: (a, b) => a.name.length - b.name.length,
        // sorter: {
        //   compare: (a, b) => a.chinese - b.chinese,
        //   multiple: 3,
        // },
      },
      {
        title: 'Type',
        dataIndex: 'type_id',
        key: 'type_id',
        filterMode: 'tree',
        filterSearch: true,
        // onFilter: (value: string, record) => record.name.includes(value),    
        sorter: (a, b) => a.type_id - b.type_id,
      },
      {
        title: 'Tags',
        key: 'tags',
        dataIndex: 'tags',
        render: (_, { tags }) => (
          <>
            {
              // tags.map(tag => {
              //   let color = tag.length > 5 ? 'geekblue' : 'green';
              //   if (tag === 'loser') {
              //     color = 'volcano';
              //   }
              //   return (
              //     <Tag color={color} key={tag}>
              //       {tag.toUpperCase()}
              //     </Tag>
              //   );
              // })
            }
          </>
        ),
      },
      {
        title: 'Action',
        key: 'action',
        render: (_, record) => (
          <Space size="middle">
            {/* <a>Invite {record.name}</a> */}
            <a onClick={event => this.handleCaveDelete(event, record.id, record.name)}>Delete</a>
          </Space>
        ),
      },
    ];

    // const data: DataType[] = [
    //   {
    //     key: '1',
    //     name: 'John Brown',
    //     age: 32,
    //     address: 'New York No. 1 Lake Park',
    //     tags: ['nice', 'developer'],
    //   },
    //   ...
    // ];

    // //////////////////////////

    return (
      <div>        
        <Button variant="contained" onClick={this.handleAddCave}>Add cave</Button>

        <Table 
        columns={columns} 
        dataSource={this.state.caveList} 
        onChange={this.onTablePropsChange} 
        pagination={{ 
          position: ['topRight', 'bottomRight'], 
          // pageSize: 15
          defaultPageSize: 15, 
          // defaultCurrent: 5,
        }}
        size='small'
        bordered={true}

        // size='small|middle|large'

        // pagination={{  // something wrong with sorting when setting these values
        //   defaultPageSize: 10, 
        //   defaultCurrent: 5,
        // }}

        />

        {this.state.modalCaveEditOpened &&
          <CaveEditModal
            isModalOpen={this.state.modalCaveEditOpened}
            onOpenStateChange={this.handleCEMOpenStateChange}
            caveId={this.state.modalCaveEditCaveId}
            mode={this.state.modalCaveEditMode}
          // wasSaved={this.state.modalCaveEditWasSaved}
          />
        }

        {this.state.notificationOpened &&
          <NotificationSnackbar
            isOpen={this.state.notificationOpened}
            message={this.state.notificationMessage}
            onOpenStateChange={this.handleNotificationOpenStateChange}
            alertSeverity={this.state.notificationSeverity}
          />
        }
      </div>
    );
  }
}

export default withStyles(styles)(CaveList);
