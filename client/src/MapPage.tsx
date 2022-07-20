import React from 'react';

import { Alert, AppBar, Avatar, Box, Button, Container, IconButton, Menu, MenuItem, Toolbar, Tooltip, Typography } from '@mui/material';

import BasicMapComponent from './components/BasicMapComponent';
import BasicNominatimSearch from './components/BasicNominatimSearch';
import SideDrawer from './components/SideDrawer';
import ToggleDrawerButton from './components/ToggleDrawerButton';

import './MapPage.less';

import {
  defaults as OlControlDefaults
} from 'ol/control';
import OlLayerGroup from 'ol/layer/Group';
import OlLayerTile from 'ol/layer/Tile';
import OlMap from 'ol/Map';
import {
  fromLonLat
} from 'ol/proj';
import OlSourceOsm from 'ol/source/OSM';
import OlSourceTileWMS from 'ol/source/TileWMS';
import OlView from 'ol/View';

import XYZ from 'ol/source/XYZ';

import MapContext from '@terrestris/react-geo/dist/Context/MapContext/MapContext';

import Draw, { DrawEvent as OlDrawEvent, Options as OlDrawOptions } from 'ol/interaction/Draw';
import { Feature as OlFeature } from 'ol/Feature';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import Style from 'ol/style/Style';
import Fill from 'ol/style/Fill';
import Stroke from 'ol/style/Stroke';
import CircleStyle from 'ol/style/Circle';
import Modify from 'ol/interaction/Modify';
import Snap from 'ol/interaction/Snap';
import Icon from 'ol/style/Icon';

import FeaturesSideDrawer from './components/FeaturesSideDrawer';
import BasicNominatimSearch2 from './components/ToggleFeaturesDrawerButton';
import ToggleFeaturesDrawerButton from './components/ToggleFeaturesDrawerButton';

import postal from 'postal';

import ReactDOM, { render } from 'react-dom';
import FeatureAPI from './middleware/FeatureAPI';
import WKT from 'ol/format/WKT';

import FeatureTypeAPI from './middleware/FeatureTypeAPI';
import Overlay from 'ol/Overlay';

import {toLonLat} from 'ol/proj';
import {toStringHDMS} from 'ol/coordinate';

const setupMap = /* async */() => {

  // https://github.com/terrestris/react-geo-ws/blob/main/gitbook/map-integration/layer-tree.md

  const osmLayer = new OlLayerTile({
    source: new OlSourceOsm()
  });
  osmLayer.set('name', 'OpenStreetMap');

  const osmLayer2 = new OlLayerTile({
    source: new XYZ({url: 'https://tile.thunderforest.com/landscape/{z}/{x}/{y}.png?apikey=034bfd1e41fb4374a12d9ec352f671f7&amp;zmax=18&amp;zmin=0'}),
  });
  osmLayer2.set('name', 'OSM Thunderforest Landscape');

  const osmLayer3 = new OlLayerTile({
    source: new XYZ({url: 'https://tile.thunderforest.com/landscape/{z}/{x}/{y}.png?apikey=034bfd1e41fb4374a12d9ec352f671f7&amp;zmax=18&amp;zmin=0'}),
  });
  osmLayer3.set('name', 'OSM THF OpenCycleMap');

  const osmLayer4 = new OlLayerTile({
    source: new XYZ({url: 'https://tile.thunderforest.com/outdoors/{z}/{x}/{y}.png?apikey=034bfd1e41fb4374a12d9ec352f671f7&amp;zmax=18&amp;zmin=0'}),
  });
  osmLayer4.set('name', 'THF Outdoors');

  const osmLayer5 = new OlLayerTile({
    source: new XYZ({url: 'http://korona.geog.uni-heidelberg.de/tiles/roads/x={x}&y={y}&z={z}&amp;zmax=18&amp;zmin=0'}),
  });
  osmLayer5.set('name', 'MapSurfer OSM Roads');

  const osmLayer6 = new OlLayerTile({
    source: new XYZ({url: 'http://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}&amp;zmax=25&amp;zmin=0'}),
  });
  osmLayer6.set('name', 'ArcGis Topo');

  const osmLayer7 = new OlLayerTile({
    source: new XYZ({url: 'http://ecn.t3.tiles.virtualearth.net/tiles/a{q}.jpeg?g=1&amp;zmax=18&amp;zmin=0'}),
  });
  osmLayer7.set('name', 'Bing');

  const osmLayer8 = new OlLayerTile({
    source: new XYZ({url: 'https://tileserver.4umaps.com/{z}/{x}/{y}.png&amp;zmax=23&amp;zmin=0'}),
  });
  osmLayer8.set('name', 'OSM T4u Maps');

  const osmLayer9 = new OlLayerTile({
    source: new XYZ({url: 'http://c.tile.opentopomap.org/{z}/{x}/{y}.png&amp;zmax=17&amp;zmin=0'}),
  });
  osmLayer9.set('name', 'OSM Open Topo');

  const osmLayer10 = new OlLayerTile({
    source: new XYZ({url: 'http://services.arcgisonline.com/ArcGis/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}.png&amp;zmin=0'}),
  });
  osmLayer10.set('name', 'Esri Tile');

  const osmLayer11 = new OlLayerTile({
    source: new XYZ({url: 'https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}&amp;zmax=28&amp;zmin=0'}),
  });
  osmLayer11.set('name', 'Google_Sattelite');


  /* const temperatureDayLayer = new OlLayerTile({
        opacity: 0.5,
        source: new OlSourceTileWMS({
          url: 'https://neo.gsfc.nasa.gov/wms/wms',
          projection: 'CRS:84',
          params: {
            LAYERS: 'MOD_LSTD_CLIM_M'
          }
        })
      });
      temperatureDayLayer.set('name', 'Average Land Surface Temperature (Day)');

      const temperatureNightLayer = new OlLayerTile({
        opacity: 0.5,
        visible: false,
        source: new OlSourceTileWMS({
          url: 'https://neo.gsfc.nasa.gov/wms/wms',
          projection: 'CRS:84',
          params: {
            LAYERS: 'MOD_LSTN_CLIM_M'
          }
        })
      });
      temperatureNightLayer.set('name', 'Average Land Surface Temperature (Night)');

      const eoLayerGroup = new OlLayerGroup({
        layers: [temperatureDayLayer, temperatureNightLayer]
      });
      eoLayerGroup.set('name', 'NASA Earth Observations');
    */

  const backgroundLayerGroup = new OlLayerGroup({
    layers: [osmLayer /* , osmLayer2, osmLayer3, osmLayer4, osmLayer5, osmLayer6, osmLayer7, osmLayer8, osmLayer9, osmLayer10, osmLayer11*/ ]
  });

  backgroundLayerGroup.set('name', 'Background');

  const backgroundLayerGroup2 = new OlLayerGroup({
    layers: [/* osmLayer,*/ /* osmLayer2*/]
  });
  backgroundLayerGroup2.set('name', 'Background2');

  const center = fromLonLat([0, 40], 'EPSG:3857');

  return new OlMap({
    view: new OlView({
      center: center,
      zoom: 0
    }),
    layers: [backgroundLayerGroup, backgroundLayerGroup2/* , eoLayerGroup*/],
    controls: OlControlDefaults({
      zoom: false
    })
  });
};

class MapPage extends React.Component /* React.FC */ {

  constructor(props, context) {
    
    super(props);
    
    this.state = {
      tabValue: '1',
      isModalOpen: false,

      anchorElUser: false, // HTMLElement
      anchorElNav: false,
      modalCaveEditOpened: false,

      // items: [],
      dataisLoaded: false,// ,
      // mapPromise: any

      map: null,
      // mapPromise: null,
      drawDigitizeLayer: null, // OlVectorLayer<OlVectorSource<OlGeometry>>
      selectedFeatureType: undefined,

      areFeaturesLoaded: false,
      featureTypeList: {}, // [],

      afterRenderFlag: 0,
    };
    
    console.log("MapPage.constructor()");
    
    this.handleOnDrawEnd = this.handleOnDrawEnd.bind(this);
    this.handleShowDebugInfo = this.handleShowDebugInfo.bind(this);

    this.handleDrawEnd_AddCave = this.handleDrawEnd_AddCave.bind(this);

    this.runAfterRender = this.runAfterRender.bind(this);

    this.handlerTest1 = this.handlerTest1.bind(this);

    // let _this = this;

    // var subscription = postal.subscribe({
    //   channel: "mainmap",
    //   topic: "feature.draw",
    //   callback: this.featureDrawPCallback.bind(this)
      
    //   // function(data, envelope) {
    //   //   // `data` is the data published by the publisher.
    //   //   // `envelope` is a wrapper around the data & contains
    //   //   // metadata about the message like the channel, topic,
    //   //   // timestamp and any other data which might have been
    //   //   // added by the sender.
    //   //   console.log ("feature.draw callback");
    //   //   console.log (data);
    //   //   console.log (envelope);

    //   //   _this.startDraw();
    //   // }
    // });

    // this.initMap();
  }

  // featureDrawPCallback (data, envelope) {
  //   // `data` is the data published by the publisher.
  //   // `envelope` is a wrapper around the data & contains
  //   // metadata about the message like the channel, topic,
  //   // timestamp and any other data which might have been
  //   // added by the sender.
  //   console.log ("feature.draw callback");
  //   console.log (data);
  //   console.log (envelope);

  //   console.log('state featureDrawPCallback');
  //   console.log(this.state);

  //   // this.startDraw();
  // }

  initMap () {    
    this.initInteractions();
    
    this.reloadFeatures();

    this.initOverlays();
  }

  reloadFeatures () {
    this.loadFeatures()
      .then((res) => {
        console.log("loadFeatures() then in initMap");
        console.log(res);

        this.mapSource.clear(true); // fast=true - Skip dispatching of removefeature events.
        this.retrieveFeatures(); //-- maybe better load in parallel both (for performance) and use them when both available
      },
      (error) => {
      
      }
      );
    // this.retrieveFeatures();
  }

  initOverlays () {
    //-- ugly workaround, to improve
    this.setState({ afterRenderFlag: 1 }, () => {
      this.setState({
        afterRenderFlag: 2,          
      }, () => { setTimeout(
        () => { this._initOverlays()},
        2000
      );
      });
    });
  }

  _initOverlays () {
    console.log("initOverlays ()");
    let map = this.state.map;

    const container = document.getElementById('popup');
    const content = document.getElementById('popup-content');
    const closer = document.getElementById('popup-closer');

    // const container = ReactDOM.findDOMNode(this.refs['popup']);
    // const content = ReactDOM.findDOMNode(this.refs['popup-content']);
    // const closer = ReactDOM.findDOMNode(this.refs['popup-closer']);

    if (container == null) // is null or undefined
    {
      console.log("");
      console.log("");
      console.log("container not initialized");
      console.log("");
      return;
    }
    else
      console.log("container initialized");
    
    // var $this = $(ReactDOM.findDOMNode(this));

    const overlay = new Overlay({
      element: container,
      autoPan: {
        animation: {
          duration: 250,
        },
      },
    });

    closer.onclick = function () {
      overlay.setPosition(undefined);
      closer.blur();
      return false;
    };

    map.addOverlay(overlay);
    var _this = this;
    // singleclick
    map.on('pointermove', function (evt) {
      const coordinate = evt.coordinate;

      // https://openlayers.org/en/latest/apidoc/module-ol_Map-Map.html#forEachFeatureAtPixel
      let featureDetRes = map.forEachFeatureAtPixel(evt.pixel, function (f, layer) {
        const hdms = toStringHDMS(toLonLat(coordinate));
        // console.log(f);

        const featureProperties = f.get('p');
        // console.log(featureProperties);

        if (featureProperties)
        {
          content.innerHTML = 
          '<span>' + _this.state.featureTypeList[featureProperties.feature_type_id].name + '</span>' + '<br/>' +
          '<span>' + featureProperties.name + '</span>' + '<br/>' +
          '<code>' + hdms + '</code>';
          
          overlay.setPosition(coordinate);
    
          return true;
        }
        else
          return false;
      });
    
      if (!featureDetRes)
        overlay.setPosition(undefined);
      // const hdms = toStringHDMS(toLonLat(coordinate));
    
      // content.innerHTML = '<p>Coordinates:</p><code>' + hdms + '</code>';
      // overlay.setPosition(coordinate);
    });

  }

  mapSource: undefined;

  initInteractions () {

    const source = new VectorSource();    
    const vector = new VectorLayer({
      source: source,
      style: new Style({
        fill: new Fill({
          color: 'rgba(255, 255, 255, 0.2)',
        }),
        stroke: new Stroke({
          color: '#ffcc33',
          width: 2,
        }),
        image: new CircleStyle({
          radius: 7,
          fill: new Fill({
            color: '#ffcc33',
          }),
        }),
      }),
    });

    this.mapSource = source;


    let map = this.state.map;

    const modify = new Modify({source: source});
    map.addInteraction(modify);

    map.addLayer(vector);

    // let draw, snap; // global so we can remove them later    
    const interactionType = 'Point'; // 'Point' | 'LineString' | 'Polygon' | 'Circle'
    
    function addDrawInteraction(featureType) {
      console.log('addDrawInteraction()');
      console.log(featureType);
  
      let addIntStyle = new Style({
        image: new Icon({
          color: '#8959A8',
          crossOrigin: 'anonymous',
          // For Internet Explorer 11
          imgSize: [64, 64], //-- maybe should be dynamic depending on image size
          // src: 'feature_symbols/' + featureType.symbol_path,
          src: 'feature_symbols/' + featureType.symbol_path,
        }),      
      });
  
      // let draw: Draw; this.draw = draw;
      this.draw = new Draw({
        source: source,
        type: interactionType,
        style: addIntStyle,        
      });

      this.draw.on('drawend', (e) => {
        // e.type=='drawend'|'' e.feature, e.target is typeof Draw
        console.log('drawend');
        console.log(e);

        // console.log((event.feature as OlFeature));
        let entranceCoords = this.transformProjectionTo4326(e.feature.getGeometry()?.clone()).getCoordinates();
        
        this.addFeature(this.state.selectedFeatureType, entranceCoords, e.feature);
      });
     
      // draw.addEventListener(); // .addEventListener('featureDrawed', function(){

      map.addInteraction(this.draw);
      this.snap = new Snap({ source: source });
      map.addInteraction(this.snap);

      this.setState({ selectedFeatureType: featureType });
    }

    this.addDrawInteraction = addDrawInteraction;

    console.log('state x initMap();');
    console.log(this.state);
  }

  draw: Draw = undefined;
  snap: Snap = undefined; // global so we can remove them later
  addDrawInteraction: undefined;
  // _this: undefined;

  startDraw (featureType) {
    console.log('startDraw()');
    console.log(featureType);

    let map = this.state.map;
    
    if (this.draw)
      map.removeInteraction(this.draw);

    if (this.snap)
      map.removeInteraction(this.snap);
    
    this.addDrawInteraction(featureType);
  }

  // onDrawEnd?: (event: OlDrawEvent) => void;
  handleOnDrawEnd (event) {
    console.log("handleOnDrawEnd()");
    console.log(event);
    // event.type == 'drawend'
    // event.feature
  }

  handleShowDebugInfo () {
    console.log("handleShowDebugInfo()");
    
    console.log("this.state.drawDigitizeLayer = ");
    console.log(this.state.drawDigitizeLayer);
  }

  // onDrawEnd?: (event: OlDrawEvent) => void;
  handleDrawEnd_AddCave (event: OlDrawEvent) {
    // event.preventDefault();

    console.log("handleDrawEnd_AddCave()");
    console.log(event);
    console.log(event.feature);

    // let feature = event.feature as OlFeature;
    // event.feature.getGeometry() == Point
    // event.feature.getGeometry().getCoordinates() = []; ex [-1526767.5826087445, 11067777.494375937] [long, lat]
    console.log((event.feature as OlFeature));
    console.log(this.transformProjectionTo4326(event.feature.getGeometry()?.clone()));

    let entranceCoords = this.transformProjectionTo4326(event.feature.getGeometry()?.clone()).getCoordinates();
  
    this.addCave(entranceCoords);
    // event.type == 'drawend'
    // event.feature
  }
  
  // transformCoordinatesProjectionToEPSG4326
  // expects coordinates with 'EPSG:3857' projection as input
  transformProjectionTo4326(geometry: []): [] {
    // src = 'EPSG:3857';
    // dest = 'EPSG:4326';
      
    return geometry.transform('EPSG:3857', 'EPSG:4326');
    // return feature.getGeometry().transform(src, dest);
  }

  //-- add feature properties as param? properties depends on the specific feature, given by context other than featureType
  addFeature(featureType: any, entranceCoords:[], olFeature: any) {
    if (featureType.name == 'cave' || featureType.name == 'aven')
    {
      let caveType = featureType.name;
      this.addCave(entranceCoords, caveType);

      return;
    }

    this.props.onAddFeature(featureType, entranceCoords, olFeature);
  }

  // caveType == 'cave' | 'aven'
  addCave(entranceCoords:[], caveType: string) {
    this.props.onAddCave(entranceCoords);
  }

  retrieveFeatures () {
    FeatureAPI.GetAll().then(res => res.json())
      .then(
        (res) => {
          // console.log('get features ok');
          // console.log(res);
          this.putFeaturesOnMap(res.data);

          this.setState({ 
            areFeaturesLoaded: true,
            featureList: res.data 
          }, 
          () => {
            // this.putFeaturesOnMap(res.data);
          });
                    
          // Promise.resolve(result);
        },
        (error) => {
        }
      );

  }

  iconStyles: any = {};

  putFeaturesOnMap(dbFeatures) {

    console.log ('putFeaturesOnMap()');

    const format = new WKT();

    let olFeatures = [];

    for (let index = 0; index < dbFeatures.length; index++)
    {
      const olFeature = format.readFeature(dbFeatures[index].geometry_wkt, {
        dataProjection: 'EPSG:4326',
        featureProjection: 'EPSG:3857',
      });

      olFeature.set('p', dbFeatures[index], true); // vs setProperties
      //-- should put only a subset of feature properties - lower memory footprint (or return only a subset from server)

      olFeature.set('name', dbFeatures[index].name);

      //////////////

      let fType = this.state.featureTypeList[dbFeatures[index].feature_type_id];

      if (fType == null)
      {
        console.error(`feature undefined for feature_type_id=${dbFeatures[index].feature_type_id}`);
        // return;
      }

      if (this.iconStyles[fType.id])
        olFeature.setStyle(this.iconStyles[fType.id]);
      else
      {
        const iconStyle = new Style({
          image: new Icon({
            anchor: [0.5, 46],
            anchorXUnits: 'fraction',
            anchorYUnits: 'pixels',

            // color: '#8959A8',
            crossOrigin: 'anonymous',
            // For Internet Explorer 11
            imgSize: [64, 64], //-- maybe should be dynamic depending on image size
            scale: 0.5,
            // src: 'feature_symbols/' + featureType.symbol_path,
            src: 'feature_symbols/' + fType.symbol_path,
          }),
        // ...
        });

        olFeature.setStyle(iconStyle);

        this.iconStyles[fType.id] = iconStyle;
      }

      olFeatures.push(olFeature);

      //////////////
    }

    this.mapSource.addFeatures(olFeatures);
  }

  handlerTest1() {
    console.log("handlerTest1()");
      
    this.initOverlays();
  }
  // setAnchorElNav (event: React.MouseEvent<HTMLElement>)
  // {
  //   this.setState({anchorElNav: event.currentTarget});
  // }
  
  // setAnchorElUser (event: React.MouseEvent<HTMLElement>)
  // {
  //   this.setState({anchorElUser: event.currentTarget});
  // }

  runAfterRender () {
    console.log("runAfterRender()");
    // this.initOverlays();
  }

  render () {
    try {
      console.log("MapPage.render()");
      
      return (
        <div className="MapPage">
          {this.state.map &&
          <MapContext.Provider value={this.state.map}>

            { this.state.areFeaturesLoaded && 
              // <div className='site-drawer-render-in-current-wrapper'>
              <span>
                <BasicMapComponent />
                <FeaturesSideDrawer
                  featureTypes={this.state.featureTypeList}
                />
                {/* onFeatureSelection={this.onFeatureSelection}  */}
              </span>
              // </div>
            }
            
            { this.state.areFeaturesLoaded && (this.state.afterRenderFlag >= 2) && 
            
              <div id="popup" ref="popup" className="ol-popup" >
                <a href="#" id="popup-closer" ref="popup-closer" className="ol-popup-closer" ></a>
                <div id="popup-content" ref="popup-content" ></div>
              </div>
            }
            {/* <div onLoad = {this.runAfterRender} ></div> */}

            <BasicNominatimSearch />
            
            <ToggleDrawerButton />          
            <SideDrawer />

            {/* <BasicNominatimSearch2 /> */}
            <ToggleFeaturesDrawerButton />

          </MapContext.Provider>
          }
          {/* <BasicNominatimSearch />
        <ToggleDrawerButton />
        <SideDrawer /> */}
      
        </div>      
      );
    } 
    catch (error) {
    // eslint-disable-next-line no-console
      console.error(error);

      render(
        <React.StrictMode>
          <Alert
            className="error-boundary"
            message="Error while loading the application"
            description="An unexpected error occured while loading the application. Please try to reload the page."
            type="error"
            showIcon
          />
        </React.StrictMode>,
        document.getElementById('app')
      );
    };

  // };
  // return renderMP();
  }

  componentDidMount() {
    const getMapObject = async () => {      
      const map = await setupMap();
      this.setState({map: map, tabValue: 2}, function () {   
    
        console.log('before this.initMap();');
        console.log('state before this.initMap();');
        console.log(this.state);
        this.initMap();
     
      });
      // console.log("smo");

      var subscription = postal.subscribe({
        channel: "mainmap",
        topic: "feature.draw",
        callback: // this.featureDrawPCallback.bind(this)
        // function(data, envelope) {
      ((data, envelope) => {
        // `data` is the data published by the publisher.
        // `envelope` is a wrapper around the data & contains
        // metadata about the message like the channel, topic,
        // timestamp and any other data which might have been
        // added by the sender.
        console.log ("feature.draw callback"); console.log (data); console.log (envelope);
        this.startDraw(data.feature);
      }).bind(this)
      });
    };

    getMapObject();
  }

  private loadFeatures () : Promise<any> {
  // from https://stackoverflow.com/a/18837750
    function imageExists(image_url: string){

      var http = new XMLHttpRequest();

      http.open('HEAD', image_url, false);
      http.send();

      return http.status !== 404;
    }

    const promiseFT = new Promise((resolve, reject) => {
      FeatureTypeAPI.GetAll()
        .then(res => res.json())
        .then(
          (res) => {
            console.log('get response data ok');

            console.log(res);

            let featureTypes = {}; // need custom indexing based on database id

            for (let index = 0; index < res.data.length; index++)
            {
              let ft = res.data[index];

              ft.key = ft.id; // key needed for react list unique identifier

              let symbolImageNotFound = false;

              if ((ft.symbol_path == null) // this checks both null and undefined
                || (symbolImageNotFound = !imageExists('feature_symbols/' + ft.symbol_path)))
              {
                if (symbolImageNotFound)
                  console.warn(`symbol resource not found for for feature id=${ft.id} title='${ft.name} symbol_path='${ft.symbol_path}''`);

                ft.symbol_path = 'generic_feature.png';
              }

              featureTypes[res.data[index].id] = res.data[index];
            }

            this.setState({
              areFeaturesLoaded: true,
              featureTypeList: featureTypes // res.data
            });

            resolve(featureTypes); // res
          // Promise.resolve(result);
          },
          (error) => {
            reject(error);
          }
        );
    }
    );

    return promiseFT;
  }

}
export default MapPage;