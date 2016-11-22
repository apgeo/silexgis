  const POINTER_INSPECT_FEATURE = 0;
  const POINTER_MEASURE = 1;
  const POINTER_DRAW_GENERIC = 2;
  const POINTER_NEW_CAVE = 3;
  const POINTER_NEW_FEATURE = 4;
  
  var map;
  var layers = [];
  var map_overlay_geo_comana;
  var user_mouse_interaction_type = POINTER_INSPECT_FEATURE;
  var drawInteraction;//drawControls;
  var drawSource;
  var featureOverlay;
  var features = new ol.Collection();
  var modify;

  var addGeoElementTooltipElement;
  var addGeoElementTooltip;
  var _db_features_layer = undefined;
  var _displayFeatureInfo = undefined;
  var _displayDetailsWindow = undefined;
  var featureTypes = {};//= [];
  var symbols_path = "./assets/feature_symbols/";
  
var geoStyle = {
        'Point': new ol.style.Style({
          image: new ol.style.Circle({
            fill: new ol.style.Fill({
              color: 'rgba(255,255,0,0.4)'
            }),
            radius: 5,
            stroke: new ol.style.Stroke({
              color: '#ff0',
              width: 1
            })
          })
        }),
        'LineString': new ol.style.Style({
          stroke: new ol.style.Stroke({
            color: '#f00',
            width: 3
          })
        }),
        'MultiLineString': new ol.style.Style({
          stroke: new ol.style.Stroke({
            color: '#0f0',
            width: 3
          })
        })
		
		
		,
        'cave': new ol.style.Style({
          stroke: new ol.style.Stroke({
            color: '#8f9',
            width: 9
          })
        })
		
      };
	  
    function initMap() {
		//console.info('init()');
		
       //var epsg4326 =  new ol.proj.Projection("EPSG:4326"); //WGS 1984 projection
       //var googleMercator = new ol.proj.Projection("EPSG:900913");
	   //var epsg3857 =  new ol.proj.Projection("EPSG:3857");

		var scaleLineControl = new ol.control.ScaleLine();	   

var defaultStyle = {
  'Point': [new ol.style.Style({
    image: new ol.style.Circle({
      fill: new ol.style.Fill({
        color: 'rgba(255,255,0,0.5)'
      }),
      radius: 5,
      stroke: new ol.style.Stroke({
        color: '#ff0',
        width: 1
      })
    })
  })],
  'LineString': [new ol.style.Style({
    stroke: new ol.style.Stroke({
      color: '#f00',
      width: 3
    })
  })],
  'Polygon': [new ol.style.Style({
    fill: new ol.style.Fill({
      color: 'rgba(0,255,255,0.5)'
    }),
    stroke: new ol.style.Stroke({
      color: '#0ff',
      width: 1
    })
  })],
  'MultiPoint': [new ol.style.Style({
    image: new ol.style.Circle({
      fill: new ol.style.Fill({
        color: 'rgba(255,0,255,0.5)'
      }),
      radius: 5,
      stroke: new ol.style.Stroke({
        color: '#f0f',
        width: 1
      })
    })
  })],
  'MultiLineString': [new ol.style.Style({
    stroke: new ol.style.Stroke({
      color: '#0f0',
      width: 3
    })
  })],
  'MultiPolygon': [new ol.style.Style({
    fill: new ol.style.Fill({
      color: 'rgba(0,0,255,0.5)'
    }),
    stroke: new ol.style.Stroke({
      color: '#00f',
      width: 1
    })
  })]

};		

var caveStyle = new ol.style.Style({
  image: new ol.style.Icon({
          anchor: [0.5, 0.5],
          size: [32, 32],
          //offset: [52, 0],
          opacity: 0.75,
          scale: 0.5,
          src: 'assets/img/cave.png'
        })
  });		
var pitStyle = new ol.style.Style({
  image: new ol.style.Icon({
          anchor: [0.5, 0.5],
          size: [32, 32],
          //offset: [52, 0],
          opacity: 0.75,
          scale: 0.5,
          src: 'assets/img/pit.png'
        })
  });		
var sinkholeStyle = new ol.style.Style({
  image: new ol.style.Icon({
          anchor: [0.5, 0.5],
          size: [64, 64],
          //offset: [52, 0],
          opacity: 0.75,
          scale: 0.5,
          src: 'assets/img/sinkhole.png'
        })
  });

		var styleFunction = function(feature, resolution) {
  var featureStyleFunction = feature.getStyleFunction();
  if (featureStyleFunction) {
    return featureStyleFunction.call(feature, resolution);
  } else {
    return defaultStyle[feature.getGeometry().getType()];
  }  
};

var dragAndDropInteraction = new ol.interaction.DragAndDrop({
  formatConstructors: [
    ol.format.GPX,
    ol.format.GeoJSON,
    ol.format.IGC,
    ol.format.KML,
    ol.format.TopoJSON,

	ol.format.EsriJSON,
	//ol.format.GeoJSON,
	//ol.format.TopoJSON,	
	//ol.format.IGC,
	ol.format.Polyline,
	ol.format.WKT,
	////- ol.format.GMLBase, // error on GPX load
	//ol.format.GPX,
	//ol.format.KML,
	ol.format.OSMXML,
	//-- ol.format.WFS, // error on GPX load
	//-- ol.format.WMSGetFeatureInfo	// error on GPX load
  ]
});

/////////////////////////
/////////////////////////
	   /*
	   var view = new ol.View({
        // the view's initial state
        //center: ,
        zoom: 6
      });
	  */
        map = new ol.Map({			
        target: 'mapdiv',
        /*layers: [
          new ol.layer.Tile({
            source: new ol.source.MapQuest({layer: 'sat'})
          })
        ],
        view: new ol.View({
          center: ol.proj.fromLonLat([37.41, 8.82]),
          zoom: 4
        })*/   
		
             //div: "mapdiv",
			 //projection: ol.proj.get('EPSG:3857'),
			 //projection: googleMercator,
             //displayProjection: epsg3857,
			 //displayProjection: wgs84,
			 //displayProjection: ol.proj.get('EPSG:3857'),//ol.proj.get('EPSG:4326'),
			 
			 //displayProjection: 'EPSG:4326',
			 //projection: 'EPSG:4326',
			 //displayProjection: new ol.proj.Projection("EPSG:4326"),
			 //projection: new ol.proj.Projection("EPSG:4326"),
			 
			 //center: new OpenLayers.LonLat(45.669245, 25.416870).transform(new ol.proj.Projection('EPSG:4326'), new ol.proj.Projection('EPSG:3857')),
			  interactions: ol.interaction.defaults().extend([dragAndDropInteraction]),
			 zoom: 2,//5,
			  controls: ol.control.defaults({
				attributionOptions: /** @type {olx.control.AttributionOptions} */ ({
				  collapsible: false
				})
			  }).extend([
				scaleLineControl
			  ]),
			  //layers: [],
			  units: ol.control.ScaleLineUnits.METRIC
			  //loadTilesWhileAnimating: true,
       } );	  


	   var db_features_layer = undefined;
	   
var mainLayerFilter = function (layerCandidate)
{
	//return true;
	//return false;	
	//console.log(layerCandidate); //console.log(layerCandidate == db_features_layer);
	return layerCandidate == db_features_layer;
}

 dragAndDropInteraction.on('addfeatures', function(event) {
  var vectorSource = new ol.source.Vector({
    features: event.features,
    projection: event.projection
  });
  map.getLayers().push(new ol.layer.Vector({
    source: vectorSource,
    style: styleFunction
  }));
  var view = map.getView();
  //view.fitExtent(
  //    vectorSource.getExtent(), /** @type {ol.Size} */ (map.getSize()));
  addFeaturesOnDrop(event); // event.features, event.file
});

var displayDetailsWindow = function(pixel) {
  
  var features = [];
  map.forEachFeatureAtPixel(pixel, function(feature, layer) { features.push(feature); }, this, mainLayerFilter, this);
  
  if (features.length > 0)
  {
	var selectedFeature = features[0];
  
	if (selectedFeature.get('elevation') == undefined)
		return;
		
	var popupHtml = "";
	//var coordinates = selectedFeature.getGeometry().getCoordinates();
	
	coord = ol.proj.transform(selectedFeature.getGeometry().getCoordinates(), 'EPSG:3857', 'EPSG:4326');

	popupHtml += "<b>" + selectedFeature.get('gpx_name') + "</b>" + "<br/>" + "<br/>";
	popupHtml += "<i>" + rtrim(coord[1]+"", 8) + ", " + rtrim(coord[0]+"", 8) + "</i>" + "<br/>";
	popupHtml += "Alt: " + selectedFeature.get('elevation') + "m" + "<br/>" + "<br/>";	
	popupHtml += selectedFeature.get('gpx_time') + "<br/>";
	//popupHtml += "tip " + selectedFeature.get('_id_point_type') + "<br/>";
	
	//popupHtml += "<br/>" + "<br/>"+ "<br/>"
	//"<iframe src='test_gps_upload.html'></iframe>";
	//"<form action='./saveGPSFileData.php' method='post' enctype='multipart/form-data'> Select file to upload:    <input type='file' name='file' id='fileToUpload'>    <input type='submit' value='Upload GPS file' name='submit'>	</form>";
	
	//document.getElementById('info').innerHTML = info.join(', ') || '&nbsp';
	featureDetailsPopup.show(selectedFeature.getGeometry().getCoordinates(), popupHtml); // '<div><h2>Coordinates</h2><p>' + prettyCoord + '</p></div>'	
	//console.info(selectedFeature);
  }
}
_displayDetailsWindow = displayDetailsWindow;

var displayFeatureInfo = function(pixel) {
  var features = [];
  map.forEachFeatureAtPixel(pixel, function(feature, layer) { features.push(feature); }, this, mainLayerFilter, this);
  
  if (document.getElementById('info') != null) //--
  if (user_mouse_interaction_type == POINTER_INSPECT_FEATURE || 
		user_mouse_interaction_type == POINTER_NEW_CAVE)
		
  if (features.length > 0) {
    var info = [];
    var i, fc;
    for (i = 0, fc = features.length; i < fc; ++i) {
		if (features[i].get('name'))		
			info.push(features[i].get('name'));
		else
			if (features[i].get('gpx_name'))	
				info.push(features[i].get('gpx_name'));
	  //-- console.info(features[i]);
    }
	//console.info(features[0]);
    document.getElementById('info').innerHTML = info.join(', ') || '&nbsp';
	popup.show(features[0].getGeometry().getCoordinates(), info.join(', ') || '<br/>'); // '<div><h2>Coordinates</h2><p>' + prettyCoord + '</p></div>'
  } else {
    document.getElementById('info').innerHTML = '&nbsp;';
	popup.hide();
  }
};

_displayFeatureInfo = displayFeatureInfo;

map.on('pointermove', function(evt) {
  if (evt.dragging) {
    return;
  }
  var pixel = map.getEventPixel(evt.originalEvent);
  displayFeatureInfo(pixel);
  
  if (user_mouse_interaction_type == POINTER_NEW_CAVE)
  {
		coordinates = ol.proj.transform(evt.coordinate, 'EPSG:3857', 'EPSG:4326');
	    addGeoElementTooltipElement.innerHTML = "Click to add a new cave here <br/> " + (rtrim(coordinates[1]+"", 8) + ", " + rtrim(coordinates[0]+"", 8));
        addGeoElementTooltip.setPosition(evt.coordinate);

        $(addGeoElementTooltipElement).removeClass('hidden');
  }
  
});

      map.on('mouseout', function() {
        $(addGeoElementTooltipElement).addClass('hidden');
      });

map.on('click', function(evt) {
  //displayFeatureInfo(evt.pixel);
  displayDetailsWindow(evt.pixel);
  //console.info(evt);
  
	if (user_mouse_interaction_type == POINTER_NEW_CAVE)
	{	
		var coordinates = undefined;
		
		//var features = db_features_layer.getSource().getFeatures();
		
		var features = [];
		
		var pixel = map.getEventPixel(evt.originalEvent);
		map.forEachFeatureAtPixel(pixel, function(feature, layer) { features.push(feature); }, this, mainLayerFilter, this);
		
		var existingSelectedFeature = features[0];
		
		if (existingSelectedFeature)
			coordinates = [existingSelectedFeature.getProperties().long, existingSelectedFeature.getProperties().lat];
		else
			coordinates = ol.proj.transform(evt.coordinate, 'EPSG:3857', 'EPSG:4326'); // [long, lat]
			
		newCave(cave_id = undefined, coordinates, existingSelectedFeature); // [long, lat]
	}
	else
	if (user_mouse_interaction_type == POINTER_NEW_FEATURE && false)
	{	
		var coordinates = undefined;
		
		//var features = db_features_layer.getSource().getFeatures();
		
		var features = [];
		
		var pixel = map.getEventPixel(evt.originalEvent);
		map.forEachFeatureAtPixel(pixel, function(feature, layer) { features.push(feature); }, this, mainLayerFilter, this);
		
		var existingSelectedFeature = features[0];
		
		if (existingSelectedFeature)
			coordinates = [existingSelectedFeature.getProperties().long, existingSelectedFeature.getProperties().lat];
		else
			coordinates = ol.proj.transform(evt.coordinate, 'EPSG:3857', 'EPSG:4326'); // [long, lat]
				
		newFeature(cave_id = undefined, coordinates, existingSelectedFeature, selected_new_feature_type); // [long, lat]
	}
});

map.on('doubleclick', function(evt) {
  //displayFeatureInfo(evt.pixel);
  console.info(evt);
});

var 				map_layer_osm = new ol.layer.Tile({
				  source: new ol.source.OSM()
				})
//map.addLayer(map_layer_osm);
	   
	   var map_attribution_arcgis = new ol.Attribution({
  html: 'Tiles &copy; <a href="http://services.arcgisonline.com/ArcGIS/' +
      'rest/services/World_Topo_Map/MapServer">ArcGIS</a>'
});

	     var map_layer_arcgis = new ol.layer.Tile({
      source: new ol.source.XYZ({
        attributions: [map_attribution_arcgis],
        url: 'http://server.arcgisonline.com/ArcGIS/rest/services/' +
            'World_Topo_Map/MapServer/tile/{z}/{y}/{x}'
      })
    });
	//map.addLayer(map_layer_arcgis);
	

	   map_layer_osm.setVisible(false);
	   map_layer_arcgis.setVisible(true);
	   	  
		  
		  
	   startLon = 25.36640167236328;//25.416870;
	   startLat = 45.89311462575596;//45.669245;
	   // 25.416870, 45.669245
	   
	   //var position       = new OpenLayers.LonLat(startLon, startLat).transform(fromProjection, toProjection);
       var zoom           = 12;
	   //var zoom           =2;
	   //var zoom = map.getZoomForResolution(76.43702827453613);
	   //var position       = new OpenLayers.LonLat(25.416870, 45.669245).transform(new ol.proj.Projection('EPSG:4326'), new ol.proj.Projection('EPSG:3857'));
	   
	   //var position       = new OpenLayers.LonLat(startLon, startLat);//.transform(map.displayProjection, map.baseLayer.projection);
	   
	   var coord = [startLon, startLat];
	   
	   var position = ol.proj.transform(coord, "EPSG:4326", 'EPSG:3857'); // map.getProjectionObject()
	   // var position = ol.proj.fromLonLat(coord).transform(new ol.proj.Projection("EPSG:4326"), new ol.proj.Projection('EPSG:3857')); // map.getProjectionObject()
	   //document.getElementById("header").innerHTML = "displayProjection = " + map.displayProjection + "  baseLayerProjection = " + map.baseLayer.projection;
	   console.info("displayProjection = " + map.displayProjection + "  baseLayerProjection = ?" /*+ map.baseLayer.projection*/);
	   //map.setDisplayProjection(new ol.proj.Projection('EPSG:3857'));
	   
	   //map.setCenter(position, zoom);
	   map.setView(new ol.View({
		center: position,
		zoom: zoom
	  }));
	   

	function initEditing()
	{
		//alert("init");
		console.info('initEditing()');
		
	  var features = new ol.Collection();
	  
	  var featureOverlaySource;
	  
      var featureOverlay = new ol.layer.Vector({
        source: featureOverlaySource = new ol.source.Vector({features: features}),
        style: new ol.style.Style({
          fill: new ol.style.Fill({
            color: 'rgba(255, 255, 255, 0.2)'
          }),
          stroke: new ol.style.Stroke({
            color: '#ffcc00',
            width: 2
          }),
          image: new ol.style.Circle({
            radius: 7,
            fill: new ol.style.Fill({
              color: '#ffcc33'
            })
          })
        })
      });
      
	  featureOverlaySource.on('addfeature', function(evt){
    var feature = evt.feature;
    var coords = feature.getGeometry().getCoordinates();
	console.info(feature);
	console.info(coords);
});

	  featureOverlay.setMap(map);

      var modify = new ol.interaction.Modify({
        features: features,
        // the SHIFT key must be pressed to delete vertices, so
        // that new vertices can be drawn at the same position
        // of existing vertices
        deleteCondition: function(event) {
          return ol.events.condition.shiftKeyOnly(event) &&
              ol.events.condition.singleClick(event);
        }
      });
      map.addInteraction(modify);

      var draw; // global so we can remove it later
      function addInteraction() {
        draw = new ol.interaction.Draw({
          features: features,
          type: /** @type {ol.geom.GeometryType} */ (typeSelect.value)
        });
        map.addInteraction(draw);
      }

      var typeSelect = document.getElementById('geometry_type');


      /**
       * Let user change the geometry type.
       * @param {Event} e Change event.
       */
      typeSelect.onchange = function(e) {
        map.removeInteraction(draw);
        addInteraction();
      };

	  
	  /////////////
	  var iconFeature = new ol.Feature({
  geometry: new ol.geom.Point(ol.proj.transform([25.416870, 45.669245], "EPSG:4326", 'EPSG:3857')),
  name: 'marc',
  population: 4000,
  rainfall: 500
});


/** @type {olx.style.IconOptions} */ 

var vectorSource = new ol.source.Vector({
  features: [iconFeature]
});

var vectorLayer = new ol.layer.Vector({
  source: vectorSource
});

map.addLayer(vectorLayer);
console.info("vectorLayer");
console.info(vectorLayer);
	  /////////////
	  
      addInteraction();
	  
	//}
	}
	
	function initMeasurement()
	{	
	var wgs84Sphere = new ol.Sphere(6378137);

      // var raster = new ol.layer.Tile({
        // source: new ol.source.MapQuest({layer: 'sat'})
      // });

      var source = new ol.source.Vector();

      var vector = new ol.layer.Vector({
        source: source,
        style: new ol.style.Style({
          fill: new ol.style.Fill({
            color: 'rgba(255, 255, 255, 0.2)'
          }),
          stroke: new ol.style.Stroke({
            color: '#ffcc33',
            width: 2
          }),
          image: new ol.style.Circle({
            radius: 7,
            fill: new ol.style.Fill({
              color: '#ffcc33'
            })
          })
        })
      });


      /**
       * Currently drawn feature.
       * @type {ol.Feature}
       */
      var sketch;


      /**
       * The help tooltip element.
       * @type {Element}
       */
      var helpTooltipElement;


      /**
       * Overlay to show the help messages.
       * @type {ol.Overlay}
       */
      var helpTooltip;


      /**
       * The measure tooltip element.
       * @type {Element}
       */
      var measureTooltipElement;


      /**
       * Overlay to show the measurement.
       * @type {ol.Overlay}
       */
      var measureTooltip;


      /**
       * Message to show when the user is drawing a polygon.
       * @type {string}
       */
      var continuePolygonMsg = 'Click to continue the polygon';


      /**
       * Message to show when the user is drawing a line.
       * @type {string}
       */
      var continueLineMsg = 'Click to continue the line';


      /**
       * Handle pointer move.
       * @param {ol.MapBrowserEvent} evt The event.
       */
      var pointerMoveHandler = function(evt) {
        if (evt.dragging || (user_mouse_interaction_type != POINTER_MEASURE)) {
          return;
        }
		console.info("user_mouse_interaction_type " +  user_mouse_interaction_type);
        /** @type {string} */
        var helpMsg = '';

        if (sketch) {
          var geom = (sketch.getGeometry());
          if (geom instanceof ol.geom.Polygon) {
            helpMsg = continuePolygonMsg;
          } else if (geom instanceof ol.geom.LineString) {
            helpMsg = continueLineMsg;
          }
        }

        helpTooltipElement.innerHTML = helpMsg;
        helpTooltip.setPosition(evt.coordinate);

        $(helpTooltipElement).removeClass('hidden');
      };


      // var map = new ol.Map({
        // layers: [raster, vector],
        // target: 'map',
        // view: new ol.View({
          // center: [-11000000, 4600000],
          // zoom: 15
        // })
      // });
	map.addLayer(vector);
	  
      map.on('pointermove', pointerMoveHandler);

      $(map.getViewport()).on('mouseout', function() {
        $(helpTooltipElement).addClass('hidden');
      });

      var typeSelect = document.getElementById('measure_type');
      var geodesicCheckbox = document.getElementById('geodesic');

      var draw; // global so we can remove it later


      /**
       * Format length output.
       * @param {ol.geom.LineString} line The line.
       * @return {string} The formatted length.
       */
      var formatLength = function(line) {
        var length;
        if (geodesicCheckbox.checked) {
          var coordinates = line.getCoordinates();
          length = 0;
          var sourceProj = map.getView().getProjection();
          for (var i = 0, ii = coordinates.length - 1; i < ii; ++i) {
            var c1 = ol.proj.transform(coordinates[i], sourceProj, 'EPSG:4326');
            var c2 = ol.proj.transform(coordinates[i + 1], sourceProj, 'EPSG:4326');
			
			length += wgs84Sphere.haversineDistance([c1[1], c1[0]], [c2[1], c2[0]]); //-- ? modified long lat for openlayers coordinate format but not tested
            //length += wgs84Sphere.haversineDistance(c1, c2); // initial solution
          }
        } else {
          length = Math.round(line.getLength() * 100) / 100;
        }
        var output;
        if (length > 100) {
          output = (Math.round(length / 1000 * 100) / 100) +
              ' ' + 'km';
        } else {
          output = (Math.round(length * 100) / 100) +
              ' ' + 'm';
        }
        return output;
      };


      /**
       * Format length output.
       * @param {ol.geom.Polygon} polygon The polygon.
       * @return {string} Formatted area.
       */
      var formatArea = function(polygon) {
        var area;
        if (geodesicCheckbox.checked) {
          var sourceProj = map.getView().getProjection();
          var geom = /** @type {ol.geom.Polygon} */(polygon.clone().transform(
              sourceProj, 'EPSG:4326'));
          var coordinates = geom.getLinearRing(0).getCoordinates();
          area = Math.abs(wgs84Sphere.geodesicArea(coordinates));
        } else {
          area = polygon.getArea();
        }
        var output;
        if (area > 10000) {
          output = (Math.round(area / 1000000 * 100) / 100) +
              ' ' + 'km<sup>2</sup>';
        } else {
          output = (Math.round(area * 100) / 100) +
              ' ' + 'm<sup>2</sup>';
        }
        return output;
      };

      function addInteraction() 
	  {									  
        var type = (typeSelect.value == 'area' ? 'Polygon' : 'LineString');
        draw = new ol.interaction.Draw({
          source: source,
          type: /** @type {ol.geom.GeometryType} */ (type),
          style: new ol.style.Style({
            fill: new ol.style.Fill({
              color: 'rgba(255, 255, 255, 0.2)'
            }),
            stroke: new ol.style.Stroke({
              color: 'rgba(0, 0, 0, 0.5)',
              lineDash: [10, 10],
              width: 2
            }),
            image: new ol.style.Circle({
              radius: 5,
              stroke: new ol.style.Stroke({
                color: 'rgba(0, 0, 0, 0.7)'
              }),
              fill: new ol.style.Fill({
                color: 'rgba(255, 255, 255, 0.2)'
              })
            })
          })
        });
        map.addInteraction(draw);

        createMeasureTooltip();
        createHelpTooltip();

        var listener;
        draw.on('drawstart',
            function(evt) {
              // set sketch
			  
              sketch = evt.feature;

              /** @type {ol.Coordinate|undefined} */
              var tooltipCoord = evt.coordinate;

              listener = sketch.getGeometry().on('change', function(evt) {
                var geom = evt.target;
                var output;
                if (geom instanceof ol.geom.Polygon) {
                  output = formatArea(geom);
                  tooltipCoord = geom.getInteriorPoint().getCoordinates();
                } else if (geom instanceof ol.geom.LineString) {
                  output = formatLength(geom);
                  tooltipCoord = geom.getLastCoordinate();
                }
                measureTooltipElement.innerHTML = output;
                measureTooltip.setPosition(tooltipCoord);
              });
            }, this);

        draw.on('drawend',
            function() {
              measureTooltipElement.className = 'spg_tooltip spg_tooltip-static';
              measureTooltip.setOffset([0, -7]);
              // unset sketch
              sketch = null;
              // unset tooltip so that a new one can be created
              measureTooltipElement = null;
              createMeasureTooltip();
              ol.Observable.unByKey(listener);
            }, this);
      }


      /**
       * Creates a new help tooltip
       */
      function createHelpTooltip() {
        if (helpTooltipElement) {
          helpTooltipElement.parentNode.removeChild(helpTooltipElement);
        }
        helpTooltipElement = document.createElement('div');
        helpTooltipElement.className = 'spg_tooltip hidden';
        helpTooltip = new ol.Overlay({
          element: helpTooltipElement,
          offset: [15, 0],
          positioning: 'center-left'
        });
        map.addOverlay(helpTooltip);
      }


      /**
       * Creates a new measure tooltip
       */
      function createMeasureTooltip() {
        if (measureTooltipElement) {
          measureTooltipElement.parentNode.removeChild(measureTooltipElement);
        }
        measureTooltipElement = document.createElement('div');
        measureTooltipElement.className = 'spg_tooltip tooltip-measure';
        measureTooltip = new ol.Overlay({
          element: measureTooltipElement,
          offset: [0, -15],
          positioning: 'bottom-center'
        });
        map.addOverlay(measureTooltip);
      }


      /**
       * Let user change the geometry type.
       */
      typeSelect.onchange = function() {
        map.removeInteraction(draw);
        addInteraction();
      };

		$("#measurementCheckBox").change(
			function() {		
				user_mouse_interaction_type = $(this).is(":checked") ? POINTER_MEASURE : user_mouse_interaction_type;
				
				map.removeInteraction(draw);
				
				if (user_mouse_interaction_type == POINTER_MEASURE)
					addInteraction();

			}
			);	
	
	  }	
		// end measurement
		//////////////////
	  
	  
	function addFeaturesOnDrop(event)
	{
		var features = event.features;
		
		var points = [];
		var tracks = []; // MultiLineString
		var trackSegments = []; // LineString
		var unknownFeatures = [];
		
		for (var index=0; index < features.length; index++)
		{
			if (features[index].getGeometry().getType() == "Point")
				points.push(features[index]);
			else
			if (features[index].getGeometry().getType() == "LineString")
				trackSegments.push(features[index]);
			else
			if (features[index].getGeometry().getType() == "MultiLineString")
				tracks.push(features[index]);
			else
			{
				unknownFeatures.push(features[index]);
				console.log("unknownFeatures were added");
			}
		}

		var dataDescriptionText = "";
		
		if (points.length > 0)
			dataDescriptionText += points.length + " puncte, ";
		if (trackSegments.length > 0)
			dataDescriptionText += trackSegments.length + " segmente, ";
		if (tracks.length > 0)
			dataDescriptionText += tracks.length + " trackuri, ";
		if (unknownFeatures.length > 0)
			dataDescriptionText += unknownFeatures.length + " obiecte necunoscute, ";
		
		if (tracks.length > 0)
			showGeometry(tracks[0]);
		else
			if (trackSegments.length > 0)
				showGeometry(trackSegments[0]);
				else
					if (points.length > 0)
						showGeometry(points[0]);
		
		var q_res = confirm(String.format("Datele au fost afisate pe harta ({0}).\r\nDoriti sa le adaugati si in baza de date?", dataDescriptionText));
		
		if (q_res)
		{
			console.info(features);					
			saveGPSFileData(event.file);
		}
	}
	
	function showGeometry(feature)
	{	
		map.getView().fit(feature.getGeometry().getExtent(), map.getSize());
		var centerOfGeom = map.getView().getCenter();
		
		//flyToCoordinates(coordinates);
	}

	/*
	// http://gis.stackexchange.com/questions/132480/get-center-of-geometry-in-openlayers-3
	function getCenterOfExtent(Extent){
var X = Extent[0] + (Extent[2]-Extent[0])/2;
var Y = Extent[1] + (Extent[3]-Extent[1])/2;
return [X, Y];
}
*/
	function saveGPSFileData(fileObject)
	{
    var file_data = fileObject;
    var form_data = new FormData();                  
    form_data.append('file', file_data);
	//console.info(form_data);
	//console.info(file_data);
    //alert(form_data);
    $.ajax({
                url: 'saveGPSFileData.php', // point to server-side PHP script 
                dataType: 'text',  // what to expect back from the PHP script, if anything
                cache: false,
                contentType: false,
                processData: false,
                data: form_data,                         
                type: 'post',
                success: function(php_script_response){
					if (php_script_response.indexOf("201")) // if (php_script_response == "201")
						;//alert("saved");
					else
						alert(php_script_response); // display response from the PHP script, if any
                }
     });	
	}
	
      //addInteraction();

	  
	//////////////////////////////////////////////
	// layers control
	/*function switchLayer()
	 {
		console.info('switchLayer()');
	  var checkedLayer = $('#layerswitcher input[name=layer]:checked').val();
	  for (i = 0, ii = layers.length; i < ii; ++i) layers[i].setVisible(i==checkedLayer);
	 }
*/

	var localLayer = new ol.layer.Tile({
  //extent: [2033814, 6414547, 2037302, 6420952],
  preload: Infinity,
  visible: true,
  source: new ol.source.TileWMS(({
    url: 'http://localhost:8090/geoserver/gwc/service/wms',
    params: {'LAYERS': 'WORKSPACE:nurc', 'TILED': true, 'VERSION': '1.3.0',
      'FORMAT': 'image/png8', 'WIDTH': 256, 'HEIGHT': 256, 'CRS': 'EPSG:3857'},
    serverType: 'geoserver'
    }))
  });

	//var layers = [];
	map_layer_mapquest_sat = new ol.layer.Tile({ source: new ol.source.MapQuest({layer: 'sat'}) }, visible = false);
	map_layer_mapquest_hyb = new ol.layer.Group({ layers: [ new ol.layer.Tile({ source: new ol.source.MapQuest({layer: 'sat'}) }), new ol.layer.Tile({ source: new ol.source.MapQuest({layer: 'hyb'}) }) ] });
	map_layer_mapquest_osm = new ol.layer.Tile({ source: new ol.source.MapQuest({layer: 'osm'}) });
	map_layer_osm = new ol.layer.Tile({ source: new ol.source.OSM() });

	var map_attribution_arcgis = new ol.Attribution({html: 'Tiles &copy; <a href="http://services.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer">ArcGIS</a>'});
	var map_layer_arcgis = new ol.layer.Tile({      source: new ol.source.XYZ({attributions: [map_attribution_arcgis], url: 'http://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}'})});

	layers[0] = map_layer_osm;
	layers[1] = map_layer_mapquest_sat;
	layers[2] = map_layer_mapquest_hyb;
	layers[3] = map_layer_mapquest_osm;
	layers[4] = map_layer_arcgis;

var styles = [
  'Road',
  'Aerial',
  'AerialWithLabels',
  //'collinsBart',
  //'ordnanceSurvey'
];

var bing_layers = [];
var index, z;
for (index = 0, z = styles.length; index < z; ++index) {
  bing_layers.push(new ol.layer.Tile({
    visible: false,
    preload: Infinity,
    source: new ol.source.BingMaps({
      key: 'AuCJtTLV9OazNjczomNJr_v1-b5dbgEUDS22yowbkbmeDQi_KInhfv_Yabqy-MOI',
      imagerySet: styles[index]
      // use maxZoom 19 to see stretched tiles instead of the BingMaps
      // "no photos at this zoom level" tiles
      // maxZoom: 19
    })
  }));
}

	layers[5] = bing_layers[0];
	layers[6] = bing_layers[1];
	layers[7] = bing_layers[2];
	layers[8] = localLayer;
	
	//map.addLayer(localLayer);
	
	//layers[8] = bing_layers[3];
	//layers[9] = bing_layers[4];
	
	
	//var imageExtent = [0, 0, 7000, 8000];
	
	      
		  var extent = [25.247375, 45.832892, 25.5004861, 46.0000083];
		  //var extent = [-111110, -111110, -1114460, 1112945];      
		  
	  var projection = new ol.proj.Projection({
        code: 'xkcd-image',
        units: 'pixels',
        extent: extent
      });

	      map_overlay_geo_comana = //new ol.layer.Group({ layers: [
		  new ol.layer.Image({
			//extent: [-13884991, 2870341, -7455066, 6338219],
          // source: new ol.source.ImageWMS({
            // //url: './persani_comana_geologica_cropped_rectified.tif',
			// url: './persani_comana_geologica_cropped_rectified.jpg',
            // params: {'LAYERS': 'topp:states'},
            // serverType: 'geoserver'
          // })
		  
				//extent: extent,
		     source: new ol.source.ImageStatic({
			 //opacity: 0.5,
				//url: './persani_comana_geologica_cropped_rectified.jpg',
				url: './assets/layer_images/persani_comana_geologica_cropped_rectified_mercator.jpg',
				imageSize: [4460, 2945],
              //crossOrigin: '',			  
              projection: ol.proj.get('EPSG:4326'),//projection,//"epsg:900913", // ol.proj.get('EPSG:3857')
			  //"espg:4326" wgs84; "epsg:900913" mercator  , // epsg4326,
              imageExtent: extent,
            })
		  		          
             //extent: extent,
          // source: new ol.source.ImageWMS({
            // url: './persani_comana_geologica_cropped_rectified.jpg',
            // params: {},
            // //serverType: 'geoserver'
        //})
        })
		//]
		//})
		;
		
	// center: ol.proj.transform(
              //ol.extent.getCenter(imageExtent), 'EPSG:27700', 'EPSG:3857'),
			  
	//layers[8] = map_overlay_geo_comana;
	//map.addLayer(map_overlay_geo_comana);
	 //map.addOverlay(map_overlay_geo_comana);

	 
	 /*var geoJsonProc = new ol.format.GeoJSON.writeFeatures(features4326, {
  dataProjection: 'EPSG:3857',
  featureProjection: 'EPSG:4326'
});
*/
		///////////////////////////
		// database geojson layer with points
		
	/*	var geojsonObject = {
    'type': 'FeatureCollection',
    'features': [
        {
            'type': 'Feature',
            'geometry': {
                'coordinates': [ 25.314217, 45.915094 ], // [ 45.915094, 25.314217 ], 
                'type': 'Point'
            },
        }
    ]
	};*/
	var db_features_source = new ol.source.Vector({
      url: 'data/getDBGeoData.php',
      format: new ol.format.GeoJSON({
         defaultDataProjection : 'EPSG:4326'//,
		 //ignoreExtraDims: true		 		
		})
		
		//features: (new ol.format.GeoJSON()).readFeatures(geojsonObject)
		//features: new ol.format.GeoJSON().readFeatures(geojsonObject,{ featureProjection: 'EPSG:3857' })
	});

   /*var*/ db_features_layer = new ol.layer.Vector({
   source: db_features_source ,
   name: 'database features',
   style: _feature_styleFunction
   //projection : 'EPSG:4326' //'EPSG:3857', // 'EPSG:4326'
   //style: style_1()
           //style: caveStyle
		   /*function(feature) {
          return geoStyle[feature.getGeometry().getType()];
        }*/

	});
	
	_db_features_layer = db_features_layer;
	
	var key = db_features_source.on('change', function(event) {
        if (db_features_source.getState() == 'ready') {
			db_features_source.unByKey(key); //-- ??
		  
		 //var defaultStyle = geoStyle[feature.getGeometry().getType()];
		 
		 
	   	//db_features_layer.getSource().forEachFeature(function(feature){

		         // Note we use 'getFeatures()' because with forEachFeature we
                // can not modify feature's geometry or will get a
                // 'cannot update extent while reading' error.
		var features = db_features_layer.getSource().getFeatures();
		
		for(var index=0; index < features.length; index++) {
				
		var feature = features[index];
		
		//var defaultStyle = geoStyle[feature.getGeometry().getType()];
        //console.log(feature.getProperties());

/*		var _point_type = feature.get('_id_point_type');
		
		var point_type = feature.get('point_type');
		
		if (point_type == "feature")
		{
			var feature_type_id = feature.get('feature_type_id');
			
			var featureType = featureTypes[feature_type_id];
			
			if (featureType)
				feature.setStyle(featureType.style);
			else
				console.log("error: no feature type existent for " + feature_type_id);
		}
		else
		if (point_type == "cave_entrance")
		{
			feature.setStyle(caveStyle);
		}
*/		
		/*
		switch(_point_type) {
    case 0:
        feature.setStyle(defaultStyle);
        break;
    case 3:
        feature.setStyle(caveStyle);
        break;
	case 4:
		feature.setStyle(pitStyle);
		break;
	case 5:
		feature.setStyle(sinkholeStyle);
		break;
    default:        
		feature.setStyle(defaultStyle);
	};
     */   
    }//});
          
        }
      });

	for (index = 0; index < layers.length; index++)
	{		
		layers[index].setVisible(index == 0);
			
		map.addLayer(layers[index]);
	}
	map.addLayer(map_overlay_geo_comana);
	
	map.addLayer(db_features_layer);
	/*
	map.addLayer(map_layer_osm);
	map.addLayer(map_layer_mapquest_sat);
	map.addLayer(map_layer_mapquest_hyb);
	map.addLayer(map_layer_mapquest_osm);
	map.addLayer(map_layer_arcgis);
	(/

	switchLayer();
	$(function() { switchLayer() } );
	$("#layerswitcher input[name=layer]").change(function() { switchLayer() } );
	*/
	
	/////////////////////////////////////////////
	
$("#slider-id").slider({
    value: 100,
    slide: function(e, ui) {
        map_overlay_geo_comana.setOpacity(ui.value / 100);
    }
});	

$("#hgPersaniCentruCheckBox").change(
    function() {
		//alert($(this).is(":checked"));
          map_overlay_geo_comana.setVisible($(this).is(":checked"));
		  //map_overlay_geo_comana.setOpacity(50 / 100);
    }
);

// $("#measurementCheckBox").change(
    // function() {		
		// user_mouse_interaction_type = $(this).is(":checked") ? 1 : 0;
    // }
// );

map_overlay_geo_comana.setVisible(false);

$("#hgPersaniCentruCheckBox").prop('checked', map_overlay_geo_comana.getVisible());
//$("#hgPersaniCentruCheckBox").prop('checked', map_overlay_geo_comana.getVisible());

//////////////////
// pop up feature

var popup = new ol.Overlay.Popup();
map.addOverlay(popup);

var featureDetailsPopup = new ol.Overlay.Popup();
map.addOverlay(featureDetailsPopup);

map.on('singleclick', function(evt) {
    //var prettyCoord = ol.coordinate.toStringHDMS(ol.proj.transform(evt.coordinate, 'EPSG:3857', 'EPSG:4326'), 2);
    //popup.show(evt.coordinate, '<div><h2>Coordinates</h2><p>' + prettyCoord + '</p></div>');
});


				/*var */drawSource = new ol.source.Vector();
				var pointLayer = new ol.layer.Vector(
				{
				  source: drawSource,
  style: new ol.style.Style({
    fill: new ol.style.Fill({
      color: 'rgba(255, 255, 255, 0.2)'
    }),
    stroke: new ol.style.Stroke({
      color: '#ffcc33',
      width: 2
    }),
    image: new ol.style.Circle({
      radius: 7,
      fill: new ol.style.Fill({
        color: '#ffcc33'
      })
    })
  })
				});
                /*var lineLayer = new ol.layer.Vector("Line Layer");
                var polygonLayer = new ol.layer.Vector("Polygon Layer");
                var boxLayer = new ol.layer.Vector("Box layer");
				*/
                map.addLayer(pointLayer); // map.addLayer([/*wmsLayer, */pointLayer/*, lineLayer, polygonLayer, boxLayer*/]);
/*
featureOverlay = new ol.FeatureOverlay({
  style: new ol.style.Style({
    fill: new ol.style.Fill({
      color: 'rgba(255, 255, 255, 0.2)'
    }),
    stroke: new ol.style.Stroke({
      color: '#ffcc33',
      width: 2
    }),
    image: new ol.style.Circle({
      radius: 7,
      fill: new ol.style.Fill({
        color: '#ffcc33'
      })
    })
  })
});
				
featureOverlay.setMap(map);
	/*			
				var modify = new ol.interaction.Modify({
  features: featureOverlay.getFeatures(),
  // the SHIFT key must be pressed to delete vertices, so
  // that new vertices can be drawn at the same position
  // of existing vertices
  deleteCondition: function(event) {
    return ol.events.condition.shiftKeyOnly(event) &&
        ol.events.condition.single3(event);
  }
});
map.addInteraction(modify);
*/
				//map.addLayer(vectorLayer);
                //map.addControl(new OpenLayers.Control.LayerSwitcher());
                //map.addControl(new OpenLayers.Control.MousePosition());

                /*drawControls = {
                    point: new OpenLayers.Control.DrawFeature(pointLayer,
                        OpenLayers.Handler.Point),
                    line: new OpenLayers.Control.DrawFeature(lineLayer,
                        OpenLayers.Handler.Path),
                    polygon: new OpenLayers.Control.DrawFeature(polygonLayer,
                        OpenLayers.Handler.Polygon),
                    box: new OpenLayers.Control.DrawFeature(boxLayer,
                        OpenLayers.Handler.RegularPolygon, {
                            handlerOptions: {
                                sides: 4,
                                irregular: true
                            }
                        }
                    )
                };
*/
                /*for(var key in drawControls) {
                    map.addControl(drawControls[key]);
                }*/

                //map.setCenter(new OpenLayers.LonLat(0, 0), 3);

                //document.getElementById('noneToggle').checked = true;				

	var mousePositionControl = new ol.control.MousePosition({
        coordinateFormat: ol.coordinate.createStringXY(4),
        projection: 'EPSG:4326',
        // comment the following two lines to have the mouse position
        // be placed within the map.
        
		/*className: 'custom-mouse-position',
        target: document.getElementById('mouse-position'),
		*/
        undefinedHTML: '&nbsp;'
      });
	
	map.getControls().extend([mousePositionControl]);
	
	window.onresize = function()
	{
		setTimeout( function() 
		{ 
			console.log("map.updateSize();");
			map.updateSize();
			}, 500);
	}
    
	initMeasurement();
	initSearchControl();
	loadGeoFiles();
	
	var _lat = parseFloat(getUrlParameter('lat'));
	var _long = parseFloat(getUrlParameter('long'));
	var point_id = parseFloat(getUrlParameter('point_id'));
	
	if (_lat)	
		if (_long)
	{
		parametrizedCenter = ol.proj.transform([_long, _lat], 'EPSG:4326', 'EPSG:3857');
		map.getView().setCenter(parametrizedCenter);

	if (point_id)
	setTimeout(function() {			
			gotoMapElement(point_id);
		}, 2500);
		
		// gotoMapElement(point_id);
	}
}	
	///////////////////////////
	// end initMap
	///////////////////////////
	
	var draw_feature_type;
	
	// Drawing type ('Point', 'LineString', 'Polygon', 'MultiPoint', 'MultiLineString', 'MultiPolygon' or 'Circle').
	function selectDrawFeature(featureType)
	{
		user_mouse_interaction_type = POINTER_DRAW_GENERIC;
		draw_feature_type = featureType;
		
		map.removeInteraction(drawInteraction);
		map.removeInteraction(modify);
		
		addFeatureInteraction(featureType);
  
		//toggleControl(featureType);
	}
	
	function addFeatureInteraction(drawFeatureType) {

		  if (drawFeatureType !== 'None') {

	//	var features = new ol.Collection();
	
      modify = new ol.interaction.Modify({
        features: features,//selectInteraction.getFeatures()
		//source: drawSource
		deleteCondition: function(event) {
    return ol.events.condition.shiftKeyOnly(event) &&
        ol.events.condition.singleClick(event);
		}
      });	  

	map.addInteraction(modify);
	//map.getInteractions().extend([selectInteraction, modify]);	  		  
		  
		  
			drawInt = new ol.interaction.Draw({
			  source: drawSource,
			  //features: featureOverlay.getFeatures(),
			  features: features,
			  type: /** @type {ol.geom.GeometryType} */ drawFeatureType,
			  //condition: ol.events.condition.singleClick,
			  //freehandCondition: ol.events.condition.noModifierKeys
			});
			map.addInteraction(drawInt);
			
			drawInteraction = drawInt;			
  }
}
	            function toggleControl(element) {
                for(key in drawControls) {
                    var control = drawControls[key];
                    if(element.value == key && element.checked) {
                        control.activate();
                    } else {
                        control.deactivate();
                    }
                }
            }
			
	function loadGeoFiles()
	{
	
	var geoFiles = [];
	
	geoFiles.forEach(
	function(filePath) 
	{
	var vector = new ol.layer.Vector({
        source: new ol.source.Vector({
          url: 'geofiles/' + filePath,
          format: new ol.format.GPX()
        }),
        style: function(feature) {
          return geoStyle[feature.getGeometry().getType()];
        }
      });
	  
	  map.addLayer(vector);
	});
	}
	
	function switchLayer()
	 {	 
		console.info('switchLayer()');
		//console.info(map.getLayers());
	  var checkedLayer = $('#layerswitcher input[name=layer]:checked').val();
	  //console.info('checkedLayer = ' + checkedLayer);
	  
	  //if (checkedLayer == 99)
	  //alert("");
	  
	  var _map_layers = layers;
	  for (index = 0; index < _map_layers.length; index++)
		_map_layers[index].setVisible(index == checkedLayer);
	 }

	 
	//initEditing();
	
	if (!String.format) {
  String.format = function(format) {
    var args = Array.prototype.slice.call(arguments, 1);
    return format.replace(/{(\d+)}/g, function(match, number) { 
      return typeof args[number] != 'undefined'
        ? args[number] 
        : match
      ;
    });
  };
}

function initLayout()
{
  //$(".simple").splitter();
  $('body').layout({ applyDefaultStyles: true,
    /*onopen_start: function () {
		
        // STOP the pane from opening
        //return false; // false = Cancel
    },*/	
	onresize: function () {	
		/*setTimeout( function() { console.log("map.updateSize();"); map.updateSize(); }, 500);*/
		
		console.log("onresize");
		map.updateSize();
        // STOP the pane from opening
        //return false; // false = Cancel
    }
});

var stateResetSettings = {
		/*north__size:		"auto"
	,	north__initClosed:	false
	,	north__initHidden:	false
	,	south__size:		"auto"
	,	south__initClosed:	false
	,	south__initHidden:	false
	,	west__size:			200
	,	west__initClosed:	false
	,	west__initHidden:	false
	,	east__size:			300
	,	east__initClosed:	false
	,	east__initHidden:	false
	*/
			south__initClosed:	true
	,	south__initHidden:	false
	//,	east__initClosed:	true
	//,	east__initHidden:	false

	};

$('body').layout().loadState( stateResetSettings )



}

function initDrawObjects()
{
	//var drawObjects = ["Sinkhole"];
	
	$.ajax({
		type: "GET",
		url: "data/getFeatureTypes.php", //"/webservices/PodcastService.asmx/CreateMarkers",
		// The key needs to match your method's input parameter (case-sensitive).
		//data: JSON.stringify(data), // JSON.stringify({ Markers: markers })
		contentType: "application/json; charset=utf-8",
		dataType: "json",
		success: function(data) { 			
			//console.log(data);
			//featureTypes = {}; // for key/value indexed object
			
			for (var property in data) {
				if (data.hasOwnProperty(property)) 
				{					
					//featureTypes.push(data[property]);
					featureTypes[data[property].Id] = data[property];
					featureType = featureTypes[data[property].Id];
					
					var feature_type_id = data[property].Id;
					var feature_type_name = data[property].Name;
					var feature_type_symbol_path = data[property].SymbolPath;					
					
					
					if (feature_type_symbol_path && feature_type_symbol_path != "null")
						feature_type_symbol_path = symbols_path + feature_type_symbol_path;
					else
						feature_type_symbol_path = "";
					
					var control = $("<button onclick='enableDrawNewFeature(" + feature_type_id + ");' style='background-color:transparent; border-color:transparent;' ><img src='" + feature_type_symbol_path + "' height='16'/>" + feature_type_name + "</button><br/>");
					// var control = $("<button onclick='console.log(\"" + feature_type_name + "\");' style='background-image: url(" + feature_type_symbol_path + ");background-repeat: no-repeat;background-position: left;padding-left: 16px;' ><img height='16'>" + feature_type_name + "</button><br/>");
					control.appendTo($("#drawControlBox"));
					

					var symbol_path = undefined;
					
					if (feature_type_symbol_path)
						symbol_path = feature_type_symbol_path;					
						
					var featureStyle = undefined;
					
					if (feature_type_symbol_path)					
					featureStyle = new ol.style.Style({
					  image: new ol.style.Icon({
							  //anchor: [0.5, 0.5],
							  size: [64, 64],
							  //offset: [52, 0],
							  opacity: 0.75,
							  scale: 0.5,
							  src: symbol_path
							})

							,
							text: new ol.style.Text({
            font: '12px Verdana',
            text: "not defined",
            fill: new ol.style.Fill({color: 'black'}),
            stroke: new ol.style.Stroke({color: 'white', width: 0.5}),
			offsetX: 28,
			offsetY: 18
        })
					  });
					else
						featureStyle = new ol.style.Style({
							  fill: new ol.style.Fill({
								color: 'rgba(255, 255, 255, 0.2)'
							  }),
							  stroke: new ol.style.Stroke({
								color: '#ffcc00',
								width: 2
							  }),
							  image: new ol.style.Circle({
								radius: 7,
								fill: new ol.style.Fill({
								  color: '#ffcc33'
								})
							  })
							  ,
							text: new ol.style.Text({
            font: '12px Verdana',
            text: "not defined",//feature.get('ARESTA'),
            fill: new ol.style.Fill({color: 'black'}),
            stroke: new ol.style.Stroke({color: 'white', width: 0.5}),
			offsetX: 5,
			offsetY: 5
        })
							});

					featureType.style = featureStyle;
				}
			}			
			
		},
		//failure: function(errMsg) {
			//$onFailure(errMsg);
			//},
		error:  function(jqXHR, textStatus, errorThrown )
		{
			//onFailure(textStatus); //-- show error code returned
			console.log(errorThrown);
			console.log("Error loading feature types: " + textStatus + " " + errorThrown);
			//alert(errMsg);
		}
	});
}
/*Object.prototype.getName = function() { 
   var funcNameRegex = /function (.{1,})\(/;
   var results = (funcNameRegex).exec((this).constructor.toString());
   return (results && results.length > 1) ? results[1] : "";
};*/

var _feature_styleFunction = function(feature, resolution) {
    /*
	var geometries = feature.getGeometry().getGeometries();
    var point = geometries[0];
    var line = geometries[1];

    var iconStyle = new ol.style.Style({
      geometry: point,
      image: ...,
      text: ...
    });

    var lineStyle = new ol.style.Style({
      geometry: line,
      stroke: ...
    });
*/
    //return [iconStyle, lineStyle];
	
		var _point_type = feature.get('_id_point_type');
		
		var point_type = feature.get('point_type');
		
		var selectedStyle = undefined;
		
		if (point_type == "feature")
		{
			var feature_type_id = feature.get('feature_type_id');
			
			var featureType = featureTypes[feature_type_id];
			
			if (featureType)
			{
				//feature.setStyle(featureType.style);
				selectedStyle = featureType.style;				
				featureType.style.getText().setText(feature.get('name'));				
			}
			else
				console.error("no feature type existent for " + feature_type_id);
		}
		else
		if (point_type == "cave_entrance")
		{
			selectedStyle = caveStyle;
			//feature.setStyle(caveStyle);
		}
		
		return [selectedStyle];
};

$(document).ready(function() {
  // Handler for .ready() called.
	initDrawObjects();
	
	initMap();  
	initLayout();
	initNewCaveForm(); //-- might be deffered until new cave form is open
	

  
  map.updateSize();
  });


      function createGeoElementTooltip() {
        if (addGeoElementTooltipElement) {
          addGeoElementTooltipElement.parentNode.removeChild(addGeoElementTooltipElement);
        }
        addGeoElementTooltipElement = document.createElement('div');
        addGeoElementTooltipElement.className = 'spg_tooltip hidden';
        addGeoElementTooltip = new ol.Overlay({
          element: addGeoElementTooltipElement,
          offset: [15, 0],
          positioning: 'center-left'
        });
        map.addOverlay(addGeoElementTooltip);
      }


function _set_user_mouse_interaction_type(interaction_type)
{
	user_mouse_interaction_type = interaction_type;
}

var selected_new_feature_type;

function enableDrawNewFeature(feature_type_id)
{
	selected_new_feature_type = feature_type_id;
	
	var featureType = featureTypes[feature_type_id].Type;
	createGeoElementTooltip();
	addGeoElemInteraction(featureType);
	_set_user_mouse_interaction_type(POINTER_NEW_FEATURE);
	//selectDrawFeature('cave');	
}

function enableDrawNewCave()
{
	selected_new_feature_type = undefined;
	
	createGeoElementTooltip();
	addGeoElemInteraction("point");
	_set_user_mouse_interaction_type(POINTER_NEW_CAVE);
	//selectDrawFeature('cave');
	
}

function UserException(message) {
   this.message = message;
   this.name = "UserException";
}

var drawGeoElemInteraction;

      function addGeoElemInteraction(featureType) 
	  {
		
        var type = "Point";//(typeSelect.value == 'area' ? 'Polygon' : 'LineString');
		
		if (featureType == "point") type = "Point";
		else
		if (featureType == "linestring") type = "LineString";
		else
		if (featureType == "polygon") type = "Polygon";
		else
			throw new UserException("not supported drawing type");
		
		map.removeInteraction(drawGeoElemInteraction);
        
		var source = new ol.source.Vector();
		
/*       var vector = new ol.layer.Vector({
        source: source,
        style: new ol.style.Style({
          fill: new ol.style.Fill({
            color: 'rgba(255, 255, 255, 0.2)'
          }),
          stroke: new ol.style.Stroke({
            color: '#ffcc33',
            width: 2
          }),
          image: new ol.style.Circle({
            radius: 7,
            fill: new ol.style.Fill({
              color: '#ffcc33'
            })
          })
        })
      });
 */
 var _draw_int_image;
 
 if (selected_new_feature_type)
			_draw_int_image = new ol.style.Circle({
              radius: 5,
              stroke: new ol.style.Stroke({
                color: 'rgba(0, 0, 0, 0.7)'
              }),
              fill: new ol.style.Fill({
                color: 'rgba(255, 255, 255, 0.2)'
              })
            });
else
		_draw_int_image = new ol.style.Icon({
          anchor: [1, 1], // anchor: [0.5, 0.5],
          size: [32, 32],
          //offset: [52, 0],
          opacity: 0.75,
          scale: 0.4,//0.5,
          src: 'assets/img/cave.png'
		  //,xxx: '55'
        });
					
        drawGeoElemInteraction = new ol.interaction.Draw({
          source: source,
          type: /** @type {ol.geom.GeometryType} */ (type),
          style: new ol.style.Style({
            fill: new ol.style.Fill({
              color: 'rgba(255, 255, 255, 0.2)'
            }),
            stroke: new ol.style.Stroke({
              color: 'rgba(0, 0, 0, 0.5)',
              lineDash: [10, 10],
              width: 2
            }),
            /*image: new ol.style.Circle({
              radius: 5,
              stroke: new ol.style.Stroke({
                color: 'rgba(0, 0, 0, 0.7)'
              }),
              fill: new ol.style.Fill({
                color: 'rgba(255, 255, 255, 0.2)'
              })
			}),*/
			    image: _draw_int_image
          }),
		  
		  /*drawend: function (event)
		  {
				console.log(event);
		  }*/
        });
		
		        drawGeoElemInteraction.on('drawend',
            function(event) {
				if (user_mouse_interaction_type != POINTER_NEW_FEATURE)
				{
					event.preventDefault();
					return;
				}
				
				console.log(event);
				saveFeature(event.feature);
            }, this);

        
		map.addInteraction(drawGeoElemInteraction);
	}

function saveFeature(feature)
{
	
		//var coordinates = undefined;
		
		//var features = db_features_layer.getSource().getFeatures();
		
		//var features = [];
		
		//var pixel = map.getEventPixel(evt.originalEvent);
		//map.forEachFeatureAtPixel(pixel, function(feature, layer) { features.push(feature); }, this, mainLayerFilter, this);
		
		var existingSelectedFeature = feature;
		
		//if (existingSelectedFeature)
			
		coordinates = existingSelectedFeature.getGeometry().getCoordinates();
			// coordinates = [existingSelectedFeature.getProperties().long, existingSelectedFeature.getProperties().lat];
		//else
		//	coordinates = ol.proj.transform(evt.coordinate, 'EPSG:3857', 'EPSG:4326'); // [long, lat]
				
		newFeature(cave_id = undefined, coordinates, existingSelectedFeature, selected_new_feature_type); // [long, lat]
	
}

function getFeatureGeoJsonString(feature)
{
	var geoJsonProc = new ol.format.GeoJSON({
	  //defaultDataProjection: 'EPSG:3857',
	  //geometryName: 'geomx'
	});

	var featureGeoJsonString = geoJsonProc.writeFeature(feature, {featureProjection:'EPSG:3857', dataProjection: 'EPSG:4326'});

	return featureGeoJsonString;
}

function newFeature(feature_id = undefined, coordinates, existingSelectedFeature, new_feature_type)
{
	openNewFeatureForm(feature_id, coordinates, existingSelectedFeature, new_feature_type);	
	
	if (feature_id == undefined)
		$('#featureModal').modal();
}

function openNewFeatureForm(feature_id, coordinates, existingSelectedFeature, new_feature_type)
{
	editMode = false;
	
	if (feature_id)
		editMode = true;
	
	var featureType = featureTypes[new_feature_type];
	//$('#saveCave').off('click');
	$('#featureForm').off('submit');
	
	//$("#caveForm").find("input, input[type=text], textarea").val("");
	$('#feature_id').val("");
	//$('#feature_coords_lon').val("");
	//$('#feature_coords_lat').val("");
	$('#feature_string').val(getFeatureGeoJsonString(existingSelectedFeature));
	
	$('#feature_type_id').val(featureType.Id);
	$('#feature_existing_point_id').val("");	
	
	
	$("#featureForm")[0].reset();
	
	
	selFeatureProps = undefined;
	
	if (existingSelectedFeature)
	{
		selFeatureProps = existingSelectedFeature.getProperties();
		$('#feature_existing_point_id').val(selFeatureProps.id);	 
	}
	
	if (coordinates)
	{
		$('#feature_coords_lat').val(coordinates[1]);
		$('#feature_coords_lon').val(coordinates[0]);
		$('#feature_coords_label').text(rtrim(coordinates[1]+"", 8) + ",  " + rtrim(coordinates[0]+"", 8) + ((selFeatureProps != undefined) ? (" : " + selFeatureProps.gpx_name) : ""));		
	}	
		
	if (editMode)
		$('#featureModalTitleLabel').text("Edit feature");
	else
	{			
		$('#featureModalTitleLabel').text("New " + featureType.Name);		
		$('#feature_type_id').val(new_feature_type);
		
		
		feature_type_symbol_path = featureType.SymbolPath;
		
		if (feature_type_symbol_path && feature_type_symbol_path != "null")
		{
			feature_type_symbol_path = symbols_path + feature_type_symbol_path;
			$('#featureModalTitleLabel').prepend("<img src='" + feature_type_symbol_path + "' height='24'/>");
		}
	}
	
	/*	
	$('#saveCave').on('click', function(event) {
		//event.preventDefault(); // To prevent following the link (optional)		
		//onSaveCave(this);
		//$(this).submit();
	});
	*/
	$('#featureForm').on('submit', function(e) {
          event.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postFeature.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#featureModal').modal('toggle'); 
				reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/ 
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
		
	//fillCaveEntries();
	
	if (feature_id)
	{
		$.getJSON("data/getFeature.php?cave_id=" + cave_id, function( data ) {
			
			$('#featureModal').modal();
			
			$('#feature_id').val(data.Id);
			$('#feature_name').val(data.Name);				
			$('#feature_description').val(data.Description);
			$('#feature_type_id').val(data.Feature_type_id);
			
			//_caveFormServerData = data;
			//$('#featureModal').modal();
		});		
	}
}

function newCave(cave_id = undefined, coordinates, existingSelectedFeature)
{
	openNewCaveForm(cave_id, coordinates, existingSelectedFeature);	
	
	if (cave_id == undefined)
		$('#caveModal').modal();
}


//var _caveFormServerData;

function initNewCaveForm()
{
	fillCaveEntries();
}

function openNewCaveForm(cave_id, coordinates, existingSelectedFeature)
{
	editMode = false;
	
	if (cave_id)
		editMode = true;
		
	//$('#saveCave').off('click');
	$('#caveForm').off('submit');
	
	//$("#caveForm").find("input, input[type=text], textarea").val("");
	$('#cave_id').val("");
	$('#cave_coords_lon').val("");
	$('#cave_coords_lat').val("");
	$('#entrance_existing_point_id').val("");
	
	$("#caveForm")[0].reset();
	
	
	selFeatureProps = undefined;
	
	if (existingSelectedFeature)
	{
		selFeatureProps = existingSelectedFeature.getProperties();
		$('#entrance_existing_point_id').val(selFeatureProps.id);	 
	}
	
	if (coordinates)
	{
		$('#cave_coords_lat').val(coordinates[1]);
		$('#cave_coords_lon').val(coordinates[0]);
		$('#cave_coords_label').text(rtrim(coordinates[1]+"", 8) + ",  " + rtrim(coordinates[0]+"", 8) + ((selFeatureProps != undefined) ? (" : " + selFeatureProps.gpx_name) : ""));		
	}	
	
	if (editMode)
		$('#caveModalTitleLabel').text("Edit cave");
	else
		$('#caveModalTitleLabel').text("New cave 1");		
	
	/*	
	$('#saveCave').on('click', function(event) {
		//event.preventDefault(); // To prevent following the link (optional)		
		//onSaveCave(this);
		//$(this).submit();
	});
	*/
	$('#caveForm').on('submit', function(e) {
          event.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postCave.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#caveModal').modal('toggle'); 
				reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/ 
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
		
	//fillCaveEntries();
	
	if (cave_id)
	{
		$.getJSON("data/getCave.php?cave_id=" + cave_id, function( data ) {
			
			$('#caveModal').modal();
			
			$('#cave_id').val(data.Id);
			$('#cave_name').val(data.Name);				
			$('#cave_description').val(data.Description);
			$('#cave_type').val(data.Typeid);
			$('#cave_identifier').val(data.Locationidentifier);

			$('#cave_type').selectpicker('refresh');
			
			//_caveFormServerData = data;
			//$('#caveModal').modal();
		});		
	}
}


function postDataAsync(_url, data, onSuccess, onFailure)
{
	$.ajax({
		type: "POST",
		url: _url, //"/webservices/PodcastService.asmx/CreateMarkers",
		// The key needs to match your method's input parameter (case-sensitive).
		data: JSON.stringify(data), // JSON.stringify({ Markers: markers })
		contentType: "application/json; charset=utf-8",
		dataType: "json",
		success: function(data) { 
			onSuccess(data); 
			//alert(data); 
		},
		//failure: function(errMsg) {
			//$onFailure(errMsg);
			//},
		error:  function(jqXHR, textStatus, errorThrown )
		{
			onFailure(textStatus); //-- show error code returned
			//alert(errMsg);
		}
});
}

function reloadMapFeatures()
{
	_db_features_layer.getSource().changed();
}

function fillCaveEntries()
{
 
	$.getJSON("data/getCaveTypes.php", function( data ) {
	var items = [];
	
	$('#cave_type').find('option').remove();
	
	$.each( data, function( key, val ) {
		//items.push( "<li id='" + key + "'>" + val + "</li>" );
		//$('#cave_type').append('<li id="' + val.Id + '" type_name="' + val.Id + '" ><a href="#">' + val.Name + '</a></li>'); // $('#cave_type').append('<li id="' + val.Id + '" type_name="' + val.Name + '" ><a href="#">' + val.Name + '</a></li>');
		$('#cave_type').append('<option value="' + val.Id + '" >' + val.Name + '</option>');		
	});
	
	$('#cave_type').selectpicker('refresh');
	
	//console.log("_caveFormServerData.Typeid = " + _caveFormServerData.Typeid);		
	});
}

var searchPickerResponseReceivedTime;

function initSearchControl()
{
	// $('#search_control').on( "change", function(evt) {console.log("on change> " + $(this).val() );} );
	/*
	
	$('#search_control').on( "input", function(event) 
	{
		event.preventDefault();
		
		console.log("> " + $(this).val() );
		
		var search_text = $(this).val().trim();
		
		if (search_text)
		{
        //var formData = $(this).serializeObject();
				
		var serializedFormData = { text: search_text };  		
		var searchSelectPicker = $(this);
		
		postDataAsync("data/getSearchFeatures.php", serializedFormData,
			function(data)
			{ 
				searchSelectPicker.find('option').remove();

				$.each(data, function( key, val ) {
					//items.push( "<li id='" + key + "'>" + val + "</li>" );
					//$('#cave_type').append('<li id="' + val.Id + '" type_name="' + val.Id + '" ><a href="#">' + val.Name + '</a></li>'); // $('#cave_type').append('<li id="' + val.Id + '" type_name="' + val.Name + '" ><a href="#">' + val.Name + '</a></li>');
					searchSelectPicker.append('<option value="' + val.id + '" >' + val.name + ' - ' + val.res_type + '</option>');
					console.log('<option value="' + val.id + '" >' + val.name + ' - ' + val.res_type + '</option>');
				});
				
				searchSelectPicker.selectpicker('refresh');

				//console.log('close');
				//$('#caveModal').modal('toggle'); 
		
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		); // { cave: formData }
		}
	});
	*/
	
	// doc: https://github.com/truckingsim/Ajax-Bootstrap-Select
	var options = {
        ajax          : {
            url     : 'data/getSearchFeatures.php',
            type    : 'POST',
            dataType: 'json',
            // Use "{{{q}}}" as a placeholder and Ajax Bootstrap Select will
            // automatically replace it with the value of the search query.
            data    : {
                q: '{{{q}}}'
            }
        },
        locale        : {
            emptyTitle: 'Search...', // Click to search elements on the map
			currentlySelected: 'Last selected element'
        },
        log           : 2,
        preprocessData: function (data) {
            var index, len = data.length, array = [];
            if (len) {
                for (index = 0; index < len; index++) {
                    array.push($.extend(true, data[index], {
                        text : data[index].name,
                        value: data[index].point_db_id, //data[index].id,
                        data : {
                            subtext: data[index].res_type,
							icon: 'glyphicon-home search_glyph_color'
                        }
                    }));
                }
            }
            // You must always return a valid array when processing data. The
            // data argument passed is a clone and cannot be modified directly.
            return array;
        },
		processData: function (data) {
			searchPickerResponseReceivedTime = new Date(); // .timeStamp
			console.log(data);
		},
		//cache = false,
		//preserveSelected = false
    };

    $('#search_control').selectpicker().filter('.with-ajax').ajaxSelectPicker(options);
    //$('select').trigger('change');//$('#search_control').trigger('change');

	/*
////////////////////////	
	var target = document.getElementById('search_control');
 
// create an observer instance
var observer = new MutationObserver(function(mutations) {
  mutations.forEach(function(mutation) {
    //console.log(mutation);
	
	for(var index=0; index < mutation.addedNodes.length; index++)
		//if (features[index].getProperties().id == elementDbId)
		{
			//console.log(mutation.addedNodes[index].nodeName);
			//console.log(features[index]);
			
			$(mutation.addedNodes[index]).on("click", 
		function (evt) {  
		//evt.preventDefault();
		
		console.log("click xx> " + $(this).val());		
	});
		}

  });    
});
 
// configuration of the observer:
var config = { //attributes: true, 
childList: true 
//, characterData: true
 };
 
// pass in the target node, as well as the observer options
observer.observe(target, config);
 
// later, you can stop observing
//observer.disconnect();
//////////////////////

	// $('#search_control').parent().bind('DOMNodeInserted', function(evt) { 
	// console.log("html modified");
	// console.log(evt);
	// });

	$('#search_control').parent().find('li').on("click", 
		function (evt) {  
		evt.preventDefault();
		
		console.log("x> " + $(this).val());
		console.log("x> " + $(this).val());
	});
	*/
	
	$('#search_control').on( "change", function(evt) 
	{
		// workaround to avoid a bug (in the picker library or here?) that triggers change on loading a search set of elements from server
		
		//if ((evt.timeStamp - searchPickerResponseReceivedTime) < 1250)
		//	return;
		
		//return;
		//event.preventDefault();
		
		//var selectedText = $(this).find("option:selected").text();
		var selectedValue = $(this).find("option:selected").val();
		
		gotoMapElement($(this).val());
		
		console.log("on change> " + $(this).val() );		
		
		//$(this).val("");
		//$(this).text("");
		//return !false;
	});
	
	//	$('#search_control').on('changed.bs.select', function (event) {
	//		console.log("on x> " + $(this).val() );		
	//});
    
    
	/*on('changed.bs.select', function (e, clickedIndex, newValue, oldValue) {
		var selected = $(e.currentTarget).val();
	});
	*/
	
	
}

	function gotoMapElement(elementDbId)
	{
		console.log("gotoMapElement " + elementDbId);		
		//db_features_layer.getSource().forEachFeature(function(feature){		
		//var features = db_features_layer.getSource().getFeatures();
		
		var features = _db_features_layer.getSource().getFeatures();
		
		for(var index=0; index < features.length; index++)
		if (features[index].getProperties().id == elementDbId)
		{
			//console.log("found");
			//console.log(features[index]);
			
			//ol.proj.transform(features[index].getGeometry().getCoordinates(), 'EPSG:3857', 'EPSG:4326');
			
			flyTo(features[index]);
		}
	}
	
	function flyTo(feature)
	{	
		var coordinates = [feature.getProperties().long, feature.getProperties().lat];
		flyToCoordinates(ol.proj.fromLonLat(coordinates));
	}
	
	//var featureToShowCoordinates = undefined;
	
	function flyToCoordinates(coordinates)
	{		
		var view = map.getView()
		
        var duration = 2000;
        var start = +new Date();
        var pan = ol.animation.pan({
          duration: duration,
          source: /** @type {ol.Coordinate} */ (view.getCenter()),
          start: start
        });
        var bounce = ol.animation.bounce({
          duration: duration,
          resolution: 4 * view.getResolution(),
          start: start
        });
        map.beforeRender(pan
		//, bounce
		);
        view.setCenter(coordinates);
		
		console.log("fly to coordinates");
		console.log(coordinates);		
		
		var _c = coordinates;
		//workaround for displaying information on the selected point
		setTimeout(function() {
			//console.log(coordinates);
			console.log("coordinates: " + coordinates);
			console.log("pixel: " + map.getPixelFromCoordinate(coordinates));
			_displayDetailsWindow(map.getPixelFromCoordinate(coordinates));
			//_displayFeatureInfo(map.getPixelFromCoordinate(coordinates));			
			//featureToShowCoordinates = coordinates;
		}, 2000);
		/*
		map.on("postrender", function (x) {		
			_displayFeatureInfo(map.getPixelFromCoordinate(featureToShowCoordinates));			
			featureToShowCoordinates = undefined;
		});*/
		
		/*var pan = ol.animation.pan({
			duration: 2000,
			source: (view.getCenter()) // @type {ol.Coordinate}
		});
		map.beforeRender(pan);
		view.setCenter(coordinates);
		*/
	}

var mapLayerSelectorShown = true;

function toggleMapLayerSelector()
{
	mapLayerSelectorShown = !mapLayerSelectorShown;
	
	if (mapLayerSelectorShown)
		$("#layerswitcher").show();	
	else
		$("#layerswitcher").hide();	
}

function toggleFullScreen(mapContainer) {
    if ($(mapContainer).hasClass("normal")) {
        $(mapContainer).addClass("fullscreen").removeClass("normal");
    } else {
        $(mapContainer).addClass("normal").removeClass("fullscreen");
    }
    map.updateSize();
}
	
    $.fn.serializeObject = function() {
        var o = {};
        var a = this.serializeArray();
        $.each(a, function() {
            if (o[this.name]) {
                if (!o[this.name].push) {
                    o[this.name] = [o[this.name]];
                }
                o[this.name].push(this.value || '');
            } else {
                o[this.name] = this.value || '';
            }
        });
        return o;
    };

	function rtrim(str, maxLength) {
		return str.substr(0, maxLength);
	}

var getUrlParameter = function getUrlParameter(sParam) {
    var sPageURL = decodeURIComponent(window.location.search.substring(1)),
        sURLVariables = sPageURL.split('&'),
        sParameterName,
        i;

    for (i = 0; i < sURLVariables.length; i++) {
        sParameterName = sURLVariables[i].split('=');

        if (sParameterName[0] === sParam) {
            return sParameterName[1] === undefined ? true : sParameterName[1];
        }
    }
};

var typeOf = function(obj){
    return ({}).toString.call(obj)
        .match(/\s([a-zA-Z]+)/)[1].toLowerCase();
};
function cloneObject(obj){
    var type = typeOf(obj);
    if (type == 'object' || type == 'array') {
        if (obj.clone) {
            return obj.clone();
        }
        var clone = type == 'array' ? [] : {};
        for (var key in obj) {
            clone[key] = cloneObject(obj[key]);
        }
        return clone;
    }
    return obj;
}