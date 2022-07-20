import React from 'react';

import AdbIcon from '@mui/icons-material/Adb';
import MenuIcon from '@mui/icons-material/Menu';
import { AppBar, Avatar, Box, Button, Container, IconButton, Menu, MenuItem, Toolbar, Tooltip, Typography } from '@mui/material';

import './App.less';

import {
  BrowserRouter, // as Router,
  Routes,
  Route,
  Link
} from 'react-router-dom';

import CaveTable from './components/CaveTable';
import CaveEditModal from './components/Forms/CaveEditModal';
import MapPage from './MapPage';
import CaveList from './components/Pages/CaveList';
import FeatureEditModal from './components/Forms/FeatureEditModal';

class App extends React.Component /* JSX.Element */ /* React.FC */ {
  constructor(props, context) {
    super(props);
    this.state = {
      isModalOpen: false,

      anchorElUser: false, // HTMLElement
      anchorElNav: false,

      modalCaveEditOpened: false,
      addCaveEntranceCoords: [],
      modalCaveEditMode: 'add',

      modalFeatureEditOpened: false,
      addFeatureEntranceCoords: [],
      modalFeatureEditMode: 'add',
      addFeatureType: undefined,
      modalFeatureOlFeature: undefined,

      DataisLoaded: false,
    };

    this.setAnchorElUser = this.setAnchorElUser.bind(this);
    this.setAnchorElNav = this.setAnchorElNav.bind(this);

    this.handleOpenCaveEditModalStateChange = this.handleOpenCaveEditModalStateChange.bind(this);
    this.handleCloseUserMenu = this.handleCloseUserMenu.bind(this);
    this.handleOpenCaveEditNavMenu = this.handleOpenCaveEditNavMenu.bind(this);
    this.handleCloseNavMenu = this.handleCloseNavMenu.bind(this);

    this.onAddCave = this.onAddCave.bind(this);

    this.handleOpenFeatureEditModalStateChange = this.handleOpenFeatureEditModalStateChange.bind(this);
    this.onAddFeature = this.onAddFeature.bind(this);
    this.openCaveEditModal = this.openCaveEditModal.bind(this);

    this.handleModalSaveCave = this.handleModalSaveCave.bind(this);
    this.handleModalSaveFeature = this.handleModalSaveFeature.bind(this);

    this.mapPageRef = React.createRef();
  }

  setAnchorElNav (event: React.MouseEvent<HTMLElement>)
  {
    if (event)
      this.setState({anchorElNav: event.currentTarget});
  }

  setAnchorElUser (event: React.MouseEvent<HTMLElement>)
  {
    if (event)
      this.setState({anchorElUser: event.currentTarget});
  }

  handleCloseNavMenu () {
    this.setAnchorElNav(null);
  };

  handleOpenCaveEditNavMenu () {
    console.log("handleOpenCaveEditNavMenu()");
    // this.setState({modalCaveEditOpened: true});
    // this.setCEOpen(true);
    this.setState({modalCaveEditOpened: true});
    // caveEditModalRef.current.handleOpen();
  };

  handleCloseUserMenu () {
    this.setAnchorElUser(null);
  };

  handleOpenCaveEditModalStateChange (openState: boolean) {
    console.log("handleOpenStateChange()");
    // this.setCEOpen(openState);
    this.setState({modalCaveEditOpened: openState});
  };

  onAddCave(entranceCoords:[]) {
    console.log("onAddCave()");
    console.log(entranceCoords);

    this.openCaveEditModal('add', entranceCoords);
  }

  openCaveEditModal (addOrEdit: string, entranceCoords?: []) {
    console.log("openCaveEditModal()");

    this.setState({
      modalCaveEditOpened: true,
      addCaveEntranceCoords: [entranceCoords[0], entranceCoords[1]],
      modalCaveEditMode: addOrEdit
    });
  }

  /////////////////////////////
  // add feature

  onAddFeature(featureType: any, entranceCoords:[], olFeature: any) {
    console.log("onAddFeature()");
    console.log(entranceCoords);

    this.openFeatureEditModal ('add', featureType, entranceCoords, olFeature);
  }

  openFeatureEditModal (addOrEdit: string, featureType: any, entranceCoords?: [], olFeature?: any) {
    // if (typeof entranceCoords !== 'undefined') { }
    console.log("openFeatureEditModal()");

    this.setState({
      modalFeatureEditOpened: true,
      addFeatureEntranceCoords: [entranceCoords[0], entranceCoords[1]],
      modalFeatureEditMode: addOrEdit,
      addFeatureType: featureType,
      modalFeatureOlFeature: olFeature
    });
  }

  handleOpenFeatureEditModalStateChange (openState: boolean) {
    console.log("handleOpenStateChange()");
    this.setState({modalFeatureEditOpened: openState});
  };

  // end add feature
  //////////////////

  handleModalSaveCave (caveData: any) {
    console.log("handleModalSaveCave()");

    console.log("caveData > id =" + caveData.id);

    this.mapPageRef.current.reloadFeatures(); // bad react design (using ref), to be changed... :)
    // this.setState({modalFeatureEditOpened: openState});
  };

  handleModalSaveFeature (featureData: any) {
    console.log("handleModalSaveFeature()");

    console.log("featureData > id =" + featureData.id);

    this.mapPageRef.current.reloadFeatures(); // bad react design (using ref), to be changed... :)
    // this.setState({modalFeatureEditOpened: openState});
  };

  render () {
    const pages = [
      {
        title: 'Map',
        link: '/map'
      },
      {
        title: 'Caves',
        link: '/caves'
      },
    ];

    const settings = ['Profile', 'Account', 'Dashboard', 'Logout'];

    return (
      <div className="App">
        <BrowserRouter>
          <AppBar position="static" color="primary">

            <Container maxWidth="xl">
              <Toolbar disableGutters>
                <AdbIcon sx={{ display: { xs: 'none', md: 'flex' }, mr: 1 }} />
                <Typography
                  variant="h6"
                  noWrap
                  component="a"
                  href="/"
                  sx={{
                    mr: 2,
                    display: { xs: 'none', md: 'flex' },
                    fontFamily: 'monospace',
                    fontWeight: 700,
                    letterSpacing: '.3rem',
                    color: 'inherit',
                    textDecoration: 'none',
                  }}
                >SilexGIS</Typography>

                <Box sx={{ flexGrow: 1, display: { xs: 'flex', md: 'none' } }}>
                  <IconButton
                    size="large"
                    aria-label="account of current user"
                    aria-controls="menu-appbar"
                    aria-haspopup="true"
                    onClick={this.handleOpenNavMenu}
                    color="inherit"
                  >
                    <MenuIcon />
                  </IconButton>
                  <Menu
                    id="menu-appbar"
                    anchorEl={this.state.anchorElNav}
                    anchorOrigin={{
                      vertical: 'bottom',
                      horizontal: 'left',
                    }}
                    keepMounted
                    transformOrigin={{
                      vertical: 'top',
                      horizontal: 'left',
                    }}
                    open={Boolean(this.state.anchorElNav)}
                    onClose={this.handleCloseNavMenu}
                    sx={{
                      display: { xs: 'block', md: 'none' },
                    }}
                  >
                    {/* handleOpen={this.handleOpenCaveEditNavMenu} */}
                    {pages.map((page) => (
                      // <MenuItem key={page.title} onClick={this.handleCloseNavMenu}>
                      <MenuItem key={page.title} component={Link} to={page.link} >
                        <Typography textAlign="center">{page.title}</Typography>
                      </MenuItem>
                    ))}
                  </Menu>
                </Box>
                <AdbIcon sx={{ display: { xs: 'flex', md: 'none' }, mr: 1 }} />
                <Typography
                  variant="h5"
                  noWrap
                  component="a"
                  href=""
                  sx={{
                    mr: 2,
                    display: { xs: 'flex', md: 'none' },
                    flexGrow: 1,
                    fontFamily: 'monospace',
                    fontWeight: 700,
                    letterSpacing: '.3rem',
                    color: 'inherit',
                    textDecoration: 'none',
                  }}
                >SilexGIS</Typography>

                <Box sx={{ flexGrow: 1, display: { xs: 'none', md: 'flex' } }}>
                  {pages.map((page) => (
                    // onClick={this.handleCloseNavMenu}
                    <Button
                      key={page.title}
                      sx={{ my: 2, color: 'white', display: 'block' }}
                    >
                      <Link to={page.link} >
                        {page.title}
                      </Link>
                    </Button>
                  ))}
                </Box>

                <Box sx={{ flexGrow: 0 }}>
                  <Tooltip title="Open settings">
                    <IconButton onClick={this.handleOpenUserMenu} sx={{ p: 0 }}>
                      <Avatar alt="Remy Sharp" src="/static/images/avatar/2.jpg" />
                    </IconButton>
                  </Tooltip>
                  <Menu
                    sx={{ mt: '45px' }}
                    id="menu-appbar"
                    anchorEl={this.state.anchorElUser}
                    anchorOrigin={{
                      vertical: 'top',
                      horizontal: 'right',
                    }}
                    keepMounted
                    transformOrigin={{
                      vertical: 'top',
                      horizontal: 'right',
                    }}
                    open={Boolean(this.state.anchorElUser)}
                    onClose={this.handleCloseUserMenu}
                  >
                    {settings.map((setting) => (
                      <MenuItem key={setting} onClick={this.handleCloseUserMenu}>
                        <Typography textAlign="center">{setting}</Typography>
                      </MenuItem>
                    ))}
                  </Menu>
                </Box>

              </Toolbar>
            </Container>

          </AppBar>

          {this.state.modalCaveEditOpened &&
        <CaveEditModal
          isModalOpen={this.state.modalCaveEditOpened}
          onOpenStateChange={this.handleOpenCaveEditModalStateChange}
          addCaveEntranceCoords={this.state.addCaveEntranceCoords}
          mode={this.state.modalCaveEditMode}
          onSaveCave={this.handleModalSaveCave}
        />
          }

          {this.state.modalFeatureEditOpened &&
        <FeatureEditModal
          isModalOpen={this.state.modalFeatureEditOpened}
          onOpenStateChange={this.handleOpenFeatureEditModalStateChange}
          addFeatureEntranceCoords={this.state.addFeatureCoords}
          featureType={this.state.addFeatureType}
          mode={this.state.modalFeatureEditMode}
          olFeature={this.state.modalFeatureOlFeature}
          onSaveFeature={this.handleModalSaveFeature}
        />
          }

          <Routes>
            <Route
              path="*"
              element={ <MapPage /> } />
            <Route exact path="/"
              element={ <MapPage onAddCave={this.onAddCave} onAddFeature={this.onAddFeature} ref={this.mapPageRef} /> }>
            </Route>
            <Route path="/map"
              element={ <MapPage onAddCave={this.onAddCave} onAddFeature={this.onAddFeature} ref={this.mapPageRef} /> }>
            </Route>
            <Route path="/caves" element={<CaveList />} >
              <Route path="/caves/details" element={<div>details </div>} />
            </Route>
          </Routes>
        </BrowserRouter>
      </div>
    );
  }

  componentDidMount() {
    fetch('http://localhost/silexgis/server/public/api/caves')
      .then((res) => res.json())
      .then((json) => {
        this.setState({
          items: json,
          DataisLoaded: true,
          modalCaveEditOpened: false
        });
      })
  }

}
export default App;

function appBarLabel(label: string) {
  return (
    <Toolbar>
      <IconButton edge="start" color="inherit" aria-label="menu" sx={{ mr: 2 }}>
        <MenuIcon />
      </IconButton>
      <Typography variant="h6" noWrap component="div" sx={{ flexGrow: 1 }}>
        {label}
      </Typography>
    </Toolbar>
  );

}