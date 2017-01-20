  const POINTER_INSPECT_FEATURE = 0;
  const POINTER_MEASURE = 1;
  const POINTER_DRAW_GENERIC = 2;
  const POINTER_NEW_CAVE = 3;
  const POINTER_NEW_FEATURE = 4;
  const POINTER_NEW_PICTURE = 5;
  const POINTER_DEFINE_CAVE_FEATURES = 6;

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
  var _db_features_source = undefined;
  var _displayFeatureInfo = undefined;
  var _displayDetailsWindow = undefined;
  var featureTypes = {};//= [];
  var symbols_path = "./assets/feature_symbols/";
  //var _loadFeaturesFunc = undefined;
  var map_views = {};//= [];
  var default_map_view = undefined;
  var default_map_view_id = undefined;
  var undo_drawGeoElemInteraction = false;
  
  var document_onkeyup = undefined;
  
  var _picturesLayer;
  var _geo_file_layer;
  
  var current_point_pixel;
  
  var _geofiles = undefined;
  var _team_members = undefined;
  
  var _pictureThumbnailLightSlider;
  var _map_pictures;
  
  var _current_coordinate;
  
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
            }),
										text: new ol.style.Text({
            font: '12px Verdana',
            text: "not defined",//feature.get('ARESTA'),
            fill: new ol.style.Fill({color: 'black'}),
            stroke: new ol.style.Stroke({color: 'white', width: 0.5}),
			offsetX: 5,
			offsetY: 5
        })
          })
        }),
        'LineString': new ol.style.Style({
          stroke: new ol.style.Stroke({
            color: '#f00',
            width: 3
          }),
							text: new ol.style.Text({
            font: '12px Verdana',
            text: "not defined",//feature.get('ARESTA'),
            fill: new ol.style.Fill({color: 'black'}),
            stroke: new ol.style.Stroke({color: 'white', width: 0.5}),
			offsetX: 5,
			offsetY: 5
        })
        }),
        'MultiLineString': new ol.style.Style({
          stroke: new ol.style.Stroke({
            color: '#0f0',
            width: 3
          }), 
        }),
		

  'Polygon': new ol.style.Style({
    fill: new ol.style.Fill({
      color: 'rgba(0,0,255,0.2)'
	  //color: 'red'
    }),
    stroke: new ol.style.Stroke({
      color: '#00f',
      width: 2,//1
    }),
	text: new ol.style.Text({
            font: '14px Verdana',
            text: "not defined",//feature.get('ARESTA'),
            fill: new ol.style.Fill({color: 'black', width: 2.5}),
			//fill: new ol.style.Fill({color: 'black'}),
            stroke: new ol.style.Stroke({color: 'red', width: 1.5}),
			// stroke: new ol.style.Stroke({color: 'white', width: 0.5}),
			offsetX: 5,
			offsetY: 5
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

var _caveStyle = new ol.style.Style({
  image: new ol.style.Icon({
          anchor: [0.5, 0.5],
          size: [32, 32],
          //offset: [52, 0],
          opacity: 0.75,
          scale: 0.5,
          src: 'assets/img/cave.png'
        })
  });		
	  
    function initMap() {
		//console.info('init()');
		
       //var epsg4326 =  new ol.proj.Projection("EPSG:4326"); //WGS 1984 projection
       //var googleMercator = new ol.proj.Projection("EPSG:900913");
	   //var epsg3857 =  new ol.proj.Projection("EPSG:3857");

		var scaleLineControl = new ol.control.ScaleLine() 
		{
			units: 'metric' //ol.control.ScaleLine.Units
		};	   

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
    }),
										text: new ol.style.Text({
            font: '12px Verdana',
            text: "not defined",//feature.get('ARESTA'),
            fill: new ol.style.Fill({color: 'black'}),
            stroke: new ol.style.Stroke({color: 'white', width: 0.5}),
			offsetX: 5,
			offsetY: 5
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
	  var keyboardPan = new ol.interaction.KeyboardPan();
	  var keyboardZoom = new ol.interaction.KeyboardZoom();
	  
		var switcher = new ol.control.LayerSwitcher(
		{	
			target:$(".layerSwitcher").get(0), 
			show_progress:true,
			extent: true,
			trash: true,
			oninfo: function (l) { alert(l.get("title")); }
		});

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
			 interactions: ol.interaction.defaults().extend([dragAndDropInteraction, new ol.interaction.DragRotateAndZoom(), keyboardPan, keyboardZoom, /*switcher*/]),			  
			 zoom: 2,//5,
			  controls: ol.control.defaults({
				attributionOptions: /** @type {olx.control.AttributionOptions} */ ({
				  collapsible: false
				})
			  }).extend([
				scaleLineControl
			  ]),
			  //layers: [],
			  //units: ol.control.ScaleLineUnits.METRIC,
			  //loadTilesWhileAnimating: true,
			  keyboardEventTarget: document,
			  //noModifierKeys //-- problem: https://github.com/openlayers/ol3/issues/917
       } );	  


	   var db_features_layer = undefined;
	   
 dragAndDropInteraction.on('addfeatures', function(event) {
  var vectorSource = new ol.source.Vector({
    features: event.features,
    projection: event.projection
  });
  map.getLayers().push(new ol.layer.Vector({
    source: vectorSource,
    style: styleFunction,
	name: 'features',
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
	
	
	var cave_id = selectedFeature.get('cave_id');
	var cave_entrance_id = "";
	
	var cave_control_html = "";
	
	var ce_html_edit_id = "";
	var ce_html_details_id = "";
	var cave_html_edit_id = "";
	var cave_html_details_id = "";
	
	if (cave_entrance_id !== undefined)
	{		
		cave_entrance_id = selectedFeature.get('cave_entrance_id');		

		ce_html_edit_id = "entrance_edit_" + cave_entrance_id;
		ce_html_details_id = "entrance_details_" + cave_entrance_id;
		cave_html_edit_id = "cave_edit_" + cave_entrance_id;
		cave_html_details_id = "cave_details_" + cave_entrance_id;
		
		//cave_entrance_id = selectedFeature.get('cave_entrance_id');
		//ce_html_id = "entrance_" + cave_entrance_id;		
		
		var cave_entrance_edit_html = "<a href='#' id='" + ce_html_edit_id + " 'click='editCave(" + cave_entrance_id + ");' >" + _t().map_popup.edit_cave_entrance + "</a><br/>";
		var cave_entrance_details_html = "<a href='#' id='" + ce_html_details_id + " 'click='openCaveDetailsForm(" + cave_entrance_id + ")' >" + _t().map_popup.edit_cave_entrance + "</a><br/><br/>";

		var cave_edit_html = "<a href='#' id='" + cave_html_edit_id + "' click='editCave(" + cave_entrance_id + ");' >" + _t().map_popup.edit_cave + "</a><br/>";
		var cave_details_html = "<a href='#' id='" + cave_html_details_id + "' click='openCaveDetailsForm(" + cave_id + ");' >" + _t().map_popup.cave_details + "</a>";
		
		cave_control_html = cave_entrance_edit_html + cave_entrance_details_html + cave_edit_html + cave_details_html;
	}
	
	
		
	var popupHtml = "";
	//var coordinates = selectedFeature.getGeometry().getCoordinates();
	
	coord = ol.proj.transform(selectedFeature.getGeometry().getCoordinates(), 'EPSG:3857', 'EPSG:4326');

	popupHtml += "<b>" + selectedFeature.get('gpx_name') + "</b>" + "<br/>" + "<br/>";
	popupHtml += "<i>" + rtrim(coord[1]+"", 8) + ", " + rtrim(coord[0]+"", 8) + "</i>" + "<br/>";
	popupHtml += "<i>" + rtrim(selectedFeature.getGeometry().getCoordinates()[1]+"", 8) + ", " + rtrim(selectedFeature.getGeometry().getCoordinates()[0]+"", 8) + "</i>" + "<br/>";
	popupHtml += "Alt: " + selectedFeature.get('elevation') + "m" + "<br/>" + "<br/>";	
	popupHtml += selectedFeature.get('gpx_time') + "<br/>" + cave_control_html;
	//popupHtml += "tip " + selectedFeature.get('_id_point_type') + "<br/>";
	
	//popupHtml += "<br/>" + "<br/>"+ "<br/>"
	//"<iframe src='test_gps_upload.html'></iframe>";
	//"<form action='./saveGPSFileData.php' method='post' enctype='multipart/form-data'> Select file to upload:    <input type='file' name='file' id='fileToUpload'>    <input type='submit' value='Upload GPS file' name='submit'>	</form>";
	
	//document.getElementById('info').innerHTML = info.join(', ') || '&nbsp';
	featureDetailsPopup.show(selectedFeature.getGeometry().getCoordinates(), popupHtml); // '<div><h2>Coordinates</h2><p>' + prettyCoord + '</p></div>'	
	//console.info(selectedFeature);
	
	if (cave_entrance_id !== undefined)
	{		
		$('#' + ce_html_edit_id).on('click', function(event) {
			newCave(cave_id, selectedFeature.getGeometry().getCoordinates(), selectedFeature);
		});
	
		$('#' + ce_html_details_id).on('click', function(event) {
			//newCave(cave_id, selectedFeature.getGeometry().getCoordinates(), selectedFeature);
		});
	}
	
	if (cave_id !== undefined)	
	{
		$('#' + cave_html_edit_id).on('click', function(event) {
			newCave(cave_id, selectedFeature.getGeometry().getCoordinates(), selectedFeature);
		});

	
		$('#' + cave_html_details_id).on('click', function(event) {
			openCaveDetailsForm(cave_id);
		});
	}
  }
}
_displayDetailsWindow = displayDetailsWindow;

var displayFeatureInfo = function(pixel) {
  var features = [];
  map.forEachFeatureAtPixel(pixel, function(feature, layer) { features.push(feature); }, this, mainLayerFilter, this);
  
  if (document.getElementById('info') != null) //--
  if (//user_mouse_interaction_type.inCollection(POINTER_INSPECT_FEATURE, POINTER_INSPECT_FEATURE, POINTER_NEW_CAVE, POINTER_NEW_FEATURE, POINTER_NEW_PICTURE)
  
	  user_mouse_interaction_type == POINTER_INSPECT_FEATURE || 
	  user_mouse_interaction_type == POINTER_NEW_CAVE ||
	  user_mouse_interaction_type == POINTER_NEW_FEATURE ||
	  user_mouse_interaction_type == POINTER_NEW_PICTURE
	  
		)
		
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

function compareCoordinates(coord1, coord2){
    var
        lon1 = Math.round(coord1[0]),
        lon2 = Math.round(coord2[0]),
        lat1 = Math.round(coord1[1]),
        lat2 = Math.round(coord2[1]);

    var
        percent_lon = Math.abs(lon1 / lon2 - 1).toFixed(4),
        percent_lat = Math.abs(lat1 / lat2 - 1).toFixed(4);
        percent = (Number(percent_lon) + Number(percent_lat) / 2).toFixed(4);

    return percent;
}

function between(number, min, max){
    if(number >= min && number <= max) return true;
    else return false;
}

var is_geo_file_layer_changed = false;

setInterval(function()
{
	if (is_geo_file_layer_changed)
	{
		is_geo_file_layer_changed = false;
		_geo_file_layer.changed();
	}
	//console.log("timer");
}, 800);

map.on('pointermove', function(evt) {
  if (evt.dragging) {
    return;
  }
  
  var pixel = map.getEventPixel(evt.originalEvent);
  
  current_point_pixel = pixel;
  
  _current_coordinate = evt.coordinate;
  
  //displayFeatureInfo(pixel);
  
  var hoveredFeatures = [];
  
  /*var feature = */map.forEachFeatureAtPixel(pixel, function(feature, layer) {
			if (layer == caveFeaturesLayer)
			{
				hoveredFeatures.push(feature);
				is_geo_file_layer_changed = true;
			}
          //return feature;
        });
	
	hoverFeatureOverlay.getSource().clear();
	hoverFeatureOverlay.getSource().addFeatures(hoveredFeatures);
	//var _hoveredFeaturesToAdd = [];
	
	/*hoveredFeatures.forEach(function (hf)
	{		
		if (hoverFeatureOverlay.getSource().getFeatures().indexOf(hf) < 0)
			_hoveredFeaturesToAdd.push(hf);			
	});*/
	
	
	/*
	var toRemoveFeatures = highlightedFeatures.slice();
	
	hoveredFeatures.forEach(function (hf)
	{
	if (highlightedFeatures[hf] !== undefined)
	{
    }
	else
	{
		hoverFeatureOverlay.getSource().addFeature(hf);         
		highlightedFeatures.push(hf);
        //highlightedFeatures[hf] = hf;//highlight = feature;
    }
	
	delete toRemoveFeatures[hf];
	});
	
	toRemoveFeatures.forEach(function (rf)
	{
		if (hoverFeatureOverlay.getSource().getFeatures().indexOf(rf) >= 0)
		{
			hoverFeatureOverlay.getSource().removeFeature(rf);
			delete highlightedFeatures[rf];
		}
	});
	*/
	
	
/*	if (feature !== highlight) {
          if (highlight) {
            hoverFeatureOverlay.getSource().removeFeature(highlight);
          }
          if (feature) {
            hoverFeatureOverlay.getSource().addFeature(feature);
          }
          highlight = feature;
        }*/
  /*var isHit = map.hasFeatureAtPixel(pixel);
  
	if (isHit)
	{
        var pointer_coord = map.getEventCoordinate(evt.originalEvent);
        var closest = caveFeaturesDrawSource.getClosestFeatureToCoordinate(pointer_coord);

        if (closest)
		{
            var geometry = closest.getGeometry();
            var closest_coord = geometry.getClosestPoint(pointer_coord);
            
            var coefficient = compareCoordinates(pointer_coord, closest_coord);
            console.info('closest: ' + closest.getId(), ' coeff: ' + coefficient);
            
            if (between(coefficient, 0, 0.01))
			{
                //hoverFeatureOverlay.addFeature(closest);                
				//_geo_file_layer.changed();
				console.log("> _geo_file_layer.changed();");
            }
			else 
			{
                hoverFeatureOverlay.removeFeature(closest);
                hoverFeatureOverlay.getFeatures().clear();
                hoverInteraction.getFeatures().clear();
            }
        }
    }*/
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
	
	if (user_mouse_interaction_type == POINTER_INSPECT_FEATURE)
	{
		var clickedFeature = map.forEachFeatureAtPixel(evt.pixel, function(feature, layer) { return feature; });
		// var clickedFeature = map.forEachFeatureAtPixel(evt.pixel, function(feature, layer) { return feature; }, this, mainLayerFilter, this);
		
		if (clickedFeature)
		{
			var url = clickedFeature.getProperties().url;
			var description = clickedFeature.getProperties().description;
			var pictureName = clickedFeature.getProperties().file_path;
			
			if (url)
			{
				var url = '.' + url;
				
				$("#thumbPictureHolder").attr("href", url);
				$("#thumbPictureHolder").attr("data-footer", description);
				$("#thumbPictureHolder").attr("data-title", pictureName);
								
				$("#thumbPictureHolder").ekkoLightbox(
				//{ remote: url }
				);
			}
		}
	}
	else
	if (user_mouse_interaction_type == POINTER_NEW_CAVE && false)
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
			coordinates = evt.coordinate; // OL coord format 'EPSG:3857'
		 // coordinates = ol.proj.transform(evt.coordinate, 'EPSG:3857', 'EPSG:4326'); // [long, lat]
			
		newCave(cave_id = undefined, coordinates, existingSelectedFeature); // [long, lat]
	}
	else
	if (user_mouse_interaction_type == POINTER_NEW_FEATURE ) // && false
	{	
		return;
		var coordinates = undefined;
		
		//var features = db_features_layer.getSource().getFeatures();
		
		var features = [];
		
		var pixel = map.getEventPixel(evt.originalEvent);
		map.forEachFeatureAtPixel(pixel, function(feature, layer) { features.push(feature); }, this, mainLayerFilter, this);
		
		var existingSelectedFeature = features[0];
		
		if (existingSelectedFeature)
			coordinates = [existingSelectedFeature.getProperties().long, existingSelectedFeature.getProperties().lat];
		else
			coordinates = evt.coordinate; // OL coord format 'EPSG:3857'
			// coordinates = ol.proj.transform(evt.coordinate, 'EPSG:3857', 'EPSG:4326'); // [long, lat]
				
		newFeature(cave_id = undefined, coordinates, existingSelectedFeature, selected_new_feature_type); // [long, lat]
	}
	else
	if (user_mouse_interaction_type == POINTER_NEW_PICTURE)
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
			coordinates = evt.coordinate; // OL coord format 'EPSG:3857'
			//coordinates = ol.proj.transform(evt.coordinate, 'EPSG:3857', 'EPSG:4326'); // [long, lat]
				
		newPicture(cave_id = undefined, coordinates, existingSelectedFeature); // [long, lat]
	}
});

map.on('doubleclick', function(evt) {
  //displayFeatureInfo(evt.pixel);
  console.info(evt);
});

var 				map_layer_osm = new ol.layer.Tile({
				  source: new ol.source.OSM(),
				  name: 'OpenStreetMap',
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
            'World_Topo_Map/MapServer/tile/{z}/{y}/{x}',
			name: 'ArcGIS Topo',
		crossOrigin: 'anonymous', // to prevent error "Tainted canvases may not be exported."
      })
    });
	//map.addLayer(map_layer_arcgis);
	

	   map_layer_osm.setVisible(false);
	   map_layer_arcgis.setVisible(true);
	   	  
		  
		  
	   //startLon = 25.36640167236328;//25.416870;
	   //startLat = 45.89311462575596;//45.669245;
	   
	   startLon = 23.8366;//25.416870;
	   startLat = 45.2598;//45.669245;
	   
	   // 25.416870, 45.669245
	   
	   //var position       = new OpenLayers.LonLat(startLon, startLat).transform(fromProjection, toProjection);
       var zoom           = 11;//12;
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
	  
	  var featureOverlaySource;i
	  
      var featureOverlay = new ol.layer.Vector({
        source: featureOverlaySource = new ol.source.Vector({
			features: features,
			name: 'feature overlay',			
			}),
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
		//var undo = false;
		
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
  source: vectorSource,
name: 'vector layer',  
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
			name: 'measurements layer',
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
		/*
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
        return output;*/
		
		// http://turfjs.org/docs/#distance

		//if (line.getCoordinates)
		
		//-- turf.distance is wrong when getting off world map bounds when zoom is too low?		
		//-- native wgs84Sphere.haversineDistance might be better since it is ol native ? ( var wgs84Sphere = new ol.Sphere(6378137); )
		{
			var length = 0;
			
			var coordinates = line.getCoordinates();
						
			var sourceProj = map.getView().getProjection();
			for (var index = 0, c_length = coordinates.length - 1; index < c_length; ++index) 
			{
				if (geodesicCheckbox.checked)
				{		
					var sourceProjection = map.getView().getProjection();
					
					var p0 = turf.point(ol.proj.transform(line.getCoordinates()[index], sourceProjection, 'EPSG:4326'));
					var p1 = turf.point(ol.proj.transform(line.getCoordinates()[index + 1], sourceProjection, 'EPSG:4326'));

					length += turf.distance(p0, p1) * 1000; // deafault: kilometers
				}
				else 
				{
					//-- incorrect ?
					length = Math.round(line.getLength()); // length += Math.round(line.getLength() * 100) / 100;
				}
			}
			
			var output;
			
			if (length > 100) 
				output = (Math.round(length / 1000 * 100) / 100) + ' ' + 'km';
			else 
				output = (Math.round(length) * 100 / 100) + ' ' + 'm';
			
			return output;
		}
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
	initMeasurement();	  
	
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
			dataDescriptionText += points.length + " points, ";
		if (trackSegments.length > 0)
			dataDescriptionText += trackSegments.length + " segments, ";
		if (tracks.length > 0)
			dataDescriptionText += tracks.length + " trackuri, ";
		if (unknownFeatures.length > 0)
			dataDescriptionText += unknownFeatures.length + " unknown objects, ";
		
		if (tracks.length > 0)
			showGeometry(tracks[0]);
		else
			if (trackSegments.length > 0)
				showGeometry(trackSegments[0]);
				else
					if (points.length > 0)
						showGeometry(points[0]);
		
		var q_res = confirm(String.format("Geodata is now displayed on the map ({0}).\r\nDo you want to add them in the database?", dataDescriptionText));
		
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
                url: 'data/saveGPSFileData.php', // point to server-side PHP script 
                dataType: 'text',  // what to expect back from the PHP script, if anything
                cache: false,
                contentType: false,
                processData: false,
                data: form_data,                         
                type: 'post',
                success: function(php_script_response){
					if (php_script_response.indexOf("201") >= 0) // if (php_script_response == "201")
					{
						showNotification("Data file was saved.");
						;//alert("saved");
					}
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
    })),
	name: 'Local server',
  });

	//var layers = [];
	
	//map_layer_mapquest_sat = new ol.layer.Tile({ source: new ol.source.MapQuest({layer: 'sat'}) }, visible = false);
	//map_layer_mapquest_hyb = new ol.layer.Group({ layers: [ new ol.layer.Tile({ source: new ol.source.MapQuest({layer: 'sat'}) }), new ol.layer.Tile({ source: new ol.source.MapQuest({layer: 'hyb'}) }) ] });
	//map_layer_mapquest_osm = new ol.layer.Tile({ source: new ol.source.MapQuest({layer: 'osm'}) });
	
	map_layer_osm = new ol.layer.Tile({ source: new ol.source.OSM({ 
											crossOrigin: 'anonymous' // to prevent error "Tainted canvases may not be exported."
											}), 
										name: 'OpenStreetMap', 
									 });

	var map_attribution_arcgis = new ol.Attribution({html: 'Tiles &copy; <a href="http://services.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer">ArcGIS</a>'});
	var map_layer_arcgis = new ol.layer.Tile({      
		source: new ol.source.XYZ({
				attributions: [map_attribution_arcgis], 
				url: 'http://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}',
				crossOrigin: 'anonymous' // to prevent error "Tainted canvases may not be exported."
				}),
		name: 'ArcGIS topo',
		});

	//layers[0] = map_layer_osm;
	//layers[1] = map_layer_mapquest_sat;
	//layers[2] = map_layer_mapquest_hyb;
	//layers[3] = map_layer_mapquest_osm;
	//layers[1] = map_layer_arcgis;

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
    }),
	name: 'Bing ' + styles[index],
  }));
}

	//layers[2] = bing_layers[0];
	//layers[3] = bing_layers[1];
	//layers[4] = bing_layers[2];
	//layers[5] = localLayer;
	
	//var layers = [map_layer_osm, map_layer_arcgis, bing_layers[0], bing_layers[1], bing_layers[2]];
	
	var gi_layer = new ol.layer.Tile({
            title: 'Global Imagery',
            source: new ol.source.TileWMS({
              url: 'http://demo.opengeo.org/geoserver/wms',
              params: {LAYERS: 'nasa:bluemarble', VERSION: '1.1.1'}
            })
          });
		  
	var blg_layer = new ol.layer.Tile({
          extent: [-13884991, 2870341, -7455066, 6338219],
          source: new ol.source.TileWMS({
            url: 'http://demo.boundlessgeo.com/geoserver/wms',
            params: {'LAYERS': 'topp:states', 'TILED': true},
            serverType: 'geoserver'
          })
        });
		
	
  
    var mapbox_layer = new ol.layer.Tile({
      source: new ol.source.XYZ({
        url: 'https://api.mapbox.com/styles/v1/mapbox/streets-v9/tiles/256/{z}/{x}/{y}?access_token=<your access token here>'
      })
    });
  	
	layers = new Array(map_layer_osm, map_layer_arcgis, bing_layers[0], bing_layers[1], bing_layers[2], gi_layer, blg_layer, mapbox_layer);
	
	
	//map.addLayer(localLayer);
	
	//layers[8] = bing_layers[3];
	//layers[9] = bing_layers[4];
	
	
	//var imageExtent = [0, 0, 7000, 8000];
	
	      
		  var extent = [24.247375, 44.832892, 24.5004861, 45.0000083];
		  // var extent = [25.247375, 45.832892, 25.5004861, 46.0000083];
		  //var extent = [-111110, -111110, -1114460, 1112945];      
		  
	  var projection = new ol.proj.Projection({
        code: 'xkcd-image',
        units: 'pixels',
        //extent: extent
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
		  name: 'geologic test',
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
			  name: 'geologic test'
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
	
/*	
	for (index = 0; index < layers.length; index++)
	{		
		layers[index].setVisible(index == 0);
			
		map.addLayer(layers[index]);
	}
*/	
	for (index = 0; index < layers.length; index++)
	{		
		layers[index].setVisible(index == 0);
			
		map.addLayer(layers[index]);
	}
		
	//loadFeaturesFunc = loadFeatures;
	//loadFeatures();
	
	map.addLayer(map_overlay_geo_comana);
	
	//map.addLayer(db_features_layer);
	
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

map_overlay_geo_comana.setVisible(!false);

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
				  name: 'drawing',				  
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
    
	//initThumbnailLoading();
	
	
	
$(document).delegate('*[data-toggle="lightbox"]', 'click', function(event) {
    event.preventDefault();
    $(this).ekkoLightbox();
});

			document.addEventListener('keyup', function(event) 
			{ //-- unbind on enddraw
			//map.addEventListener('keyup', function() {			
				//event.preventDefault();
				
			  console.log("eventListener('keyup' global");
			  
			  if (event.keyCode === 27) {
				console.log("esc pressed");
				
				if (drawGeoElemInteraction)
				{
				{
					map.removeInteraction(drawGeoElemInteraction);
					user_mouse_interaction_type = POINTER_INSPECT_FEATURE;
					console.log("");
				}
				}
				
				if (caveFeatureDrawInteraction)
				{
					map.removeInteraction(caveFeatureDrawInteraction);
					user_mouse_interaction_type = POINTER_INSPECT_FEATURE;
				}
			  }
			  else
			  if (event.keyCode === 187) // 61
			  {
				console.log("= pressed");
				map.getView().setZoom(map.getView().getZoom() + 1);
			  }
			});
			
			//testInitLineSelection(_db_features_layer);
		
/*		
		var hoverInteraction = new ol.interaction.Select({
			condition: ol.events.condition.pointerMove,
			layers: [caveFeaturesLayer, _geo_file_layer]
		});
		
		hoverInteraction.on('select', function(evt)
		{
			if (evt.selected.length > 0)
				console.info('selected: ' + evt.selected[0].getId());
		});

		hoverFeatureOverlay = new ol.Overlay({
			map: map,
			style: _test_hoverGeometryStyle
		});
		
		map.addInteraction(hoverInteraction);*/
}
	///////////////////////////
	// end initMap
	///////////////////////////

	function loadFeatures()
	{
		if (_db_features_layer)
			map.removeLayer(_db_features_layer);
		
		var db_features_source = new ol.source.Vector({
		  url: 'data/getDBGeoData.php' +'?type=surface_feature',
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
		
		_db_features_source = db_features_source;
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
		  
		  map.addLayer(db_features_layer);
	}

function testInitLineSelection(target_layer)
{
	/////////////////
	// test selection
			
var selectEuropa = new ol.style.Style({
          stroke: new ol.style.Stroke({
            color: '#f201f9',
            width: 8
        })
      });			

var selectInteraction = new ol.interaction.Select({
        layers: function(layer) {
		console.log("selectInteraction function(layer)");
          return true;
		  //layer.get('selectable') == true;
        },
        style: [/*selectFrancePoints, */selectEuropa]
      });
map.getInteractions().extend([selectInteraction]);

target_layer.set('selectable', true);
}

	var mainLayerFilter = function (layerCandidate)
	{
		//return true;
		//return false;	
		//console.log(layerCandidate); //console.log(layerCandidate == db_features_layer);
		return layerCandidate == _db_features_layer ||
				layerCandidate == _geo_file_layer; // for points loaded from files stored on the server, not from db
	}
	
	function get_selected_feature(evt)
	{
		var coordinates = undefined;
		
		//var features = db_features_layer.getSource().getFeatures();
		
		var features = [];
		
		//var pixel = map.getEventPixel(evt.originalEvent);
		map.forEachFeatureAtPixel(current_point_pixel, function(feature, layer) { features.push(feature); }, this, mainLayerFilter, this);
		
		var existingSelectedFeature = features[0];
		
		if (existingSelectedFeature)
			coordinates = [existingSelectedFeature.getProperties().long, existingSelectedFeature.getProperties().lat];
		//else
		//	coordinates = evt.coordinate; // OL coord format 'EPSG:3857'
		 // coordinates = ol.proj.transform(evt.coordinate, 'EPSG:3857', 'EPSG:4326'); // [long, lat]
		 
		 /*var result =
		 {
			feature: existingSelectedFeature,
			coordinates: coordinates
		 };
		*/
		return existingSelectedFeature;
	}
	
var picture_cache = {};

    function pictureStyle(feature) {
      // cache styles by photo url
      var url = feature.get('url');
	  var thumbUrl = feature.get('thumbUrl');
      if (!picture_cache[url]) {
        // use the icon style and scale the image to 10% so its not too large
        picture_cache[url] = new ol.style.Style({
          image: new ol.style.Icon({
            scale: 0.5, // 0.10,
            src: './' + thumbUrl//url
          }) // class="img-thumbnail"
        });
      }
      return [picture_cache[url]];
    }

function initThumbnailLoading()
{
	if (_picturesLayer)
		map.removeLayer(_picturesLayer);
	
	var pictureSource = new ol.source.Vector();

    // a cache for the style objects, always good practice
	
    var picturesLayer = new ol.layer.Vector({
      source: pictureSource,
		name: 'pictures',	  
      style: pictureStyle
    });

	map.addLayer(picturesLayer);

	_picturesLayer = picturesLayer;
	
    function successHandler(data) {
      var transform = ol.proj.getTransform('EPSG:4326', 'EPSG:3857');
      data.features.forEach(function(item) {
        var feature = new ol.Feature(); // var feature = new ol.Feature(item);
        feature.set('url', item.properties.url);
		feature.set('thumbUrl', item.properties.thumbUrl);
        var coordinate = transform([parseFloat(item.geometry.coordinates[1]), parseFloat(item.geometry.coordinates[0])]);
        var geometry = new ol.geom.Point(coordinate);
        feature.setGeometry(geometry);
        pictureSource.addFeature(feature);
      });
    }

	var extent = map.getView().calculateExtent(map.getSize());
	var projection = map.getView().getProjection();
	var bbox = ol.proj.transformExtent(extent, projection, "EPSG:4326");
	
    $.ajax({
      url: 'data/getPictures.php?bbox=' + bbox[0] + ',' + bbox[1] + ',' + bbox[2] + ',' + bbox[3],
      dataType: 'json', // dataType: 'jsonp',
      //jsonpCallback: 'jsonFlickrFeed',
      success: successHandler,
		error:  function(jqXHR, textStatus, errorThrown )
		{
			//onFailure(textStatus); //-- show error code returned
			console.error(errorThrown);
			console.error("Error loading feature types: " + textStatus + " " + errorThrown);
			//alert(errMsg);
		}
	  
    });
}
	
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
			  condition: ol.events.condition.singleClick,
			  //freehandCondition: ol.events.condition.noModifierKeys
			  //freehandCondition: ol.events.condition.always,
			  //condition: ol.events.condition.never,
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
		/*
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
		
			//var drawObjects = ["Sinkhole"];
	*/
	var gpxFormat = new ol.format.GPX({
		readExtensions: function(x) {
			  return x;
			},
		extractStyles: false, //-- probably GPX doesn't have styles ? or it has some icons ?
		extractAttributes: !false			
		});
		
	var kmlFormat = new ol.format.KML({
		readExtensions: function(x) {
			  return x;
			},
		extractStyles: false,
		extractAttributes: !false
	});
		
	var gpxFeatures;

	var geoFileLayer = new ol.layer.Vector({
		source: new ol.source.Vector({
		    //features: ,
			//projection: new ol.proj.Projection({ code: "ESPG:4326"})
		}),
		name: 'geo files',
		//style: function(feature) {
		//	  return geoStyle[feature.getGeometry().getType()];
		//}
		//style: geoStyle['LineString'] // defaultStyle
		style: caveGalleryLinesStyleFunction
		});
	
	map.addLayer(geoFileLayer);
	
	_geo_file_layer = geoFileLayer;
	
	$.ajax({
		type: "GET",
		url: "data/getGeoFiles.php", //"/webservices/PodcastService.asmx/CreateMarkers",
		// The key needs to match your method's input parameter (case-sensitive).
		//data: JSON.stringify(data), // JSON.stringify({ Markers: markers })
		contentType: "application/json; charset=utf-8",
		dataType: "json",
		success: function(data) { 			
			//console.log(data);			
			_geofiles = {}; // for key/value indexed object
			
			for (var property in data) {
				if (data.hasOwnProperty(property)) 
				{					
					//featureTypes.push(data[property]);
					_geofiles[data[property].Id] = data[property];
					geofile = _geofiles[data[property].Id];
					
					var geofile_id = data[property].Id;
					var geofile_name = data[property].FileName;
					var geofile_type = data[property].Type;
					var gf_file_url = './uploads/' + geofile_name;
					
					// from http://gis.stackexchange.com/questions/175592/read-gpx-file-from-desktop-in-openlayers-3 | http://gis.stackexchange.com/a/175637
					
					//var reader = new FileReader();
					//reader.readAsText(gf_file_path, "UTF-8");
					//reader.onload = function (evt) {
					
					var geoFormat = undefined;
					
					if (geofile_type == "GPX")
						geoFormat = gpxFormat;
					else
					if (geofile_type == "KML")
						geoFormat = kmlFormat;
					else
					{
						//console.error("unsupported geo format: " + geofile_type);
						// console.warn("unsupported geo format: " + geofile_type);
						console.info("unsupported geo format: " + geofile_type + " url: " + gf_file_url);
						geoFormat = undefined;
						return;
					}
						
					$.ajax({  
						url: gf_file_url,
						dataType: "text",  
						async: false,
						success: function(data)
						{
							gpxFeatures = geoFormat.readFeatures(data, { dataProjection:'EPSG:4326', featureProjection:'EPSG:3857' });
							geoFileLayer.getSource().addFeatures(gpxFeatures);
							
							//testInitLineSelection(geoFileLayer);
						},
		error:  function(jqXHR, textStatus, errorThrown )
		{
			//onFailure(textStatus); //-- show error code returned
			//console.warn(errorThrown);
			console.warn("Error loading geo file: " + gf_file_url + " " + textStatus + " " + errorThrown);
			//alert(errMsg);
		}
						
					});  											
				}
				}

		},
		//failure: function(errMsg) {
			//$onFailure(errMsg);
			//},
		error:  function(jqXHR, textStatus, errorThrown )
		{
			//onFailure(textStatus); //-- show error code returned
			console.error(errorThrown);
			console.error("Error loading geo file list: " + textStatus + " " + errorThrown);
			//alert(errMsg);
		}
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
		//_map_layers.indexOf(index).setVisible(index == checkedLayer);
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
	*/
		east__size:			"auto"
	,	east__initClosed:	false
	,	east__initHidden:	false
	
	,	south__initClosed:	true
	,	south__initHidden:	false
	,	south__size:		"auto"
	,	west__initClosed:	false
	,	west__initHidden:	false

	//,	east__initClosed:	true
	//,	east__initHidden:	false

	};

$('body').layout().loadState(stateResetSettings);



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
			
			var drawFeaturesTreeData = [];
			
			var groupElements = {};
			
			for (var property in data) {
				if (data.hasOwnProperty(property)) 
				{
					//featureTypes.push(data[property]);
					featureTypes[data[property].Id] = data[property];
					featureType = featureTypes[data[property].Id];
					
					var feature_type_id = data[property].Id;
					var feature_type_name = data[property].Name;
					var feature_type_symbol_path = data[property].SymbolPath;
					var feature_group_type = data[property].GroupType;
						
					if (feature_type_symbol_path && feature_type_symbol_path != "null")
						feature_type_symbol_path = symbols_path + feature_type_symbol_path;
					else
						feature_type_symbol_path = symbols_path + "generic_feature.png";
					
					var localized_feature_type_name = eval("_t().feature_types." + feature_type_name.replace(" ", "_"));
					
					var index = 0;
					var groupSubmenuItem = undefined;
					//var groupElement = undefined;
					
					if ((groupSubmenuItem = groupElements[feature_group_type]) === undefined)
					{
						var localized_feature_group_type_name = eval("_t().feature_types." + (feature_group_type + "s_group_label").replace(" ", "_"));
						
						var _groupSubmenuItem =	{
							id: feature_group_type,
							text: localized_feature_group_type_name,
							//text: feature_group_type,
							type: "root",
							children: [],
							state : {
								opened : true,
								//selected : true
							},							
							icon: "glyphicon glyphicon-plus",
							li_attr: {},
							a_attr: {}
						};
						
						groupSubmenuItem = _groupSubmenuItem;
						groupElements[feature_group_type] = _groupSubmenuItem;
						
						drawFeaturesTreeData.push(groupSubmenuItem);
					}

					groupSubmenuItem.children.push({
							//children: true,
							id: feature_type_name,
							text: localized_feature_type_name,
							//text: feature_type_name,							
							//type: "root",
							icon: feature_type_symbol_path,
							feature_type_id: feature_type_id
						});

					/*if ((groupElement = groupElements[feature_group_type]) === undefined)
					{						
						var geControl = $("<li>" + feature_group_type + "<ul id='sbm_" + feature_group_type + "'></ul></li>");
						geControl.appendTo($("#drawFeaturesTreeControl_root")); // geControl.appendTo($("#drawControlBox"));						
						groupElements[feature_group_type] = geControl;
					}
					//else
					//	groupElement = groupElements[index];
					
					var control = $("<li id='menu_" + feature_type_id + "' icon='" + feature_type_symbol_path + "'><button onclick='enableDrawNewFeature(" + feature_type_id + ");' style='background-color:transparent; border-color:transparent;' ><img src='" + feature_type_symbol_path + "' height='16'/>" + feature_type_name + "</button></li>");
					// var control = $("<button onclick='enableDrawNewFeature(" + feature_type_id + ");' style='background-color:transparent; border-color:transparent;' ><img src='" + feature_type_symbol_path + "' height='16'/>" + feature_type_name + "</button>");
					// var control = $("<button onclick='console.log(\"" + feature_type_name + "\");' style='background-image: url(" + feature_type_symbol_path + ");background-repeat: no-repeat;background-position: left;padding-left: 16px;' ><img height='16'>" + feature_type_name + "</button><br/>");
					control.appendTo($("#" + "sbm_" + feature_group_type));
					*/

					var symbol_path = undefined;
					
					if (feature_type_symbol_path)
						symbol_path = feature_type_symbol_path;					
					//else
					//	symbol_path = symbols_path + "generic_feature.png";
						
					var featureStyle = undefined;
					
					if (feature_type_symbol_path)
					{
					if (featureType.Type == "point")
					featureStyle = new ol.style.Style({
						image: new ol.style.Icon({
							  anchor: [0.25, 0.25],
							  // anchor: [0.5, 0.5],
							  size: [64, 64],
							  //offset: [52, 0],
							  opacity: 0.75,
							  scale: 0.5,
							  src: symbol_path
							}),
							          /*image: new ol.style.Circle({
            fill: new ol.style.Fill({
              color: 'rgba(255,255,0,0.4)'
            }),
			           radius: 5,
            stroke: new ol.style.Stroke({
              color: '#ff0',
              width: 1
            })
			
			}),*/

						text: new ol.style.Text({
							font: '12px Verdana',
							text: "not defined",
							fill: new ol.style.Fill({color: 'black'}),
							stroke: new ol.style.Stroke({color: 'white', width: 0.5}),
							offsetX: 8,
							offsetY: 8,
							textAlign: 'left'// 'left', 'right', 'center', 'end' or 'start'. Default is 'start'.
							})
						//ol.style.Circle
					  });
					  else
						if (featureType.Type == "linestring")
							featureStyle = geoStyle['LineString'];
					  else					  
						if (featureType.Type == "polygon")
							featureStyle = geoStyle["Polygon"];
					  else
						console.log("unsupoorted feature type");
					}
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
			
			initDrawFeaturesTreeControl(drawFeaturesTreeData);
		},
		//failure: function(errMsg) {
			//$onFailure(errMsg);
			//},
		error:  function(jqXHR, textStatus, errorThrown )
		{
			//onFailure(textStatus); //-- show error code returned
			console.error(errorThrown);
			console.error("Error loading feature types: " + textStatus + " " + errorThrown);
			//alert(errMsg);
		}
	});
}

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
		
		var ftId = feature.get('feature_type_id');
		
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
			//selectedStyle = _caveStyle;
			var featureType = featureTypes[3];
			selectedStyle = featureType.style;
			
			selectedStyle = _caveStyle;
			//feature.setStyle(caveStyle);			
		}
		else
		//-- workaround, it should be detected cleaner
		if ((ftId && (featureTypes[ftId]["GroupType"] == "cave_feature")) ||
			(feature.get("geoobject_type") == "cave_feature") // set for recognising feature type in the map interface for not posted features.		
		)
		{
			var feature_type_id = feature.get('feature_type_id');
			
			var featureType = featureTypes[feature_type_id];
			
			if (featureType)
			{
				//feature.setStyle(featureType.style);
				selectedStyle = featureType.style;
				
				var text = undefined;
				
				if (feature.get('cave_feature_name'))
					text = feature.get('cave_feature_name');
				else if (feature.get('feature_name'))
					text = feature.get('feature_name');
				else if (feature.get('name'))
					text = feature.get('name');
				
				featureType.style.getText().setText(text);		
			}
			else
				console.error("no feature type existent for " + feature_type_id);			
		}
		else
			console.error("point type = " + point_type);
		
		return [selectedStyle];
};

var map_center_set_by_url = false;

/*$(document).load(function() {
	localize_static_html();
});
*/
$(document).ready(function() {
  // Handler for .ready() called.  
	//initDrawObjects();

	var _lat = parseFloat(getUrlParameter('lat'));
	var _long = parseFloat(getUrlParameter('lon'));
	var point_id = parseFloat(getUrlParameter('point_id'));
	
	localize_static_html();
	document.getElementsByTagName("html")[0].style.visibility = "visible";	
	
	initMap();
	initLayout();
	initNewCaveForm(); //-- might be deffered until new cave form is open	
	initNewFeatureForm();
	
	initCaveDetailsForm();
	initNewCaveEntranceForm(); //-- deffer init only for the form loading moment?
	initTripReportForm();
	
	initUploadControls();
	initCaveDetailsUploadControl();
	initPicturesUploadControl();
	
	initFeatureSearchControl();
	initTripFeatureSearchControl();

	initGeocoderSearch();
	initMapLayerSwitcherControl();
	initMapPermalinkControl();
	initCaveFilesTable();
	initContextMenu();
	//initDrawFeaturesTreeControl();
	initCaveFeaturesHighlighting();
	
	initCaveFeaturesCheckBox();
	
	initDrawObjects();
	//loadFeatures();
	
	setTimeout( function() 
	{ 
		initPictureThumbLayer();
	}, 1000);
	
	init_export_map_as_image();
	
	setTimeout( function() 
	{ 
		//initThumbnailLoading();			
	}, 500);

	setTimeout( function()
	{ 
		initViews(set_center = true);
	}, 1500);
		
	initSearchControl();		

	loadCaveFeatures();
	setTimeout(function() 
	{			
		//loadCaveFeatures(); 
	}, 1000);
	
	setTimeout(function() 
	{			
		loadGeoFiles(); 
		loadFeatures();
		setInitialCaveLayerSettings();
	}, 2500);
	//setTimeout(function() {			initFeatureClusteringLayer(); }, 2500);
	
/*	var _lat = parseFloat(getUrlParameter('lat'));
	var _long = parseFloat(getUrlParameter('lon'));
	var point_id = parseFloat(getUrlParameter('point_id'));
*/	

//-- multiple calls to setView up to this point make initial map loading to flicker and change several times quickly
	if (_lat && _long)		
		//if (_long)
	{
		map_center_set_by_url = true;
		
		parametrizedCenter = ol.proj.transform([_long, _lat], 'EPSG:4326', 'EPSG:3857');
		map.getView().setCenter(parametrizedCenter);

		if (point_id)
		{
			setTimeout(function() {			
				gotoMapElement(point_id);
			}, 2500);
		}
		// gotoMapElement(point_id);
	}	
/*	else
		if (default_map_view_id)
		{
			//parametrizedCenter = ol.proj.transform([_long, _lat], 'EPSG:4326', 'EPSG:3857');
			//map.getView().setCenter(parametrizedCenter);
		
			showView(default_map_view_id)
		}
	*/
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
	addGeoElemInteraction(featureType, featureTypes[feature_type_id]);
	_set_user_mouse_interaction_type(POINTER_NEW_FEATURE);
	//selectDrawFeature('cave');	
}

function enableDrawNewCave()
{
	selected_new_feature_type = undefined;
	_set_user_mouse_interaction_type(POINTER_NEW_CAVE);
	
	createGeoElementTooltip();
	addGeoElemInteraction("point");
	
	//selectDrawFeature('cave');
	
}

function enableDrawNewPicture()
{
	selected_new_feature_type = undefined;
	_set_user_mouse_interaction_type(POINTER_NEW_PICTURE);
	
	createGeoElementTooltip();
	addGeoElemInteraction("point");
	
	//selectDrawFeature('cave');
	
}

function UserException(message) {
   this.message = message;
   this.name = "UserException";
}

var drawGeoElemInteraction;
var _draw_source;

//-- should send only 2'nd parameter which is the object. Now property is duplicated in first parameter
      function addGeoElemInteraction(featureType, featureTypeObject = undefined) 
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
        
		if (/*caveFeaturesDrawSource && */(featureTypeObject["GroupType"] == "cave_feature"))
		{
			enableCaveFeatureEditing();			
			_draw_source = caveFeaturesDrawSource;
		}
		else
		{
			var source = new ol.source.Vector();		
			_draw_source = source;
		}
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
 
 var pointer_icon_url = getInteractionPointerIconUrl(user_mouse_interaction_type);
 
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
	if (pointer_icon_url)
		_draw_int_image = new ol.style.Icon({
          anchor: [1, 1], // anchor: [0.5, 0.5],
          size: [64, 64], //size: [32, 32],
          //offset: [52, 0],
          opacity: 0.75,
          scale: 0.4,//0.4,//0.5,
          src: pointer_icon_url
		  // src: 'assets/img/cave.png'
		  //,xxx: '55'
        });
	else
			_draw_int_image = new ol.style.Circle({
              radius: 5,
              stroke: new ol.style.Stroke({
                color: 'rgba(0, 0, 0, 0.7)'
              }),
              fill: new ol.style.Fill({
                color: 'rgba(255, 255, 255, 0.2)'
              })
            });
					
		//var undo = false;		
		
        drawGeoElemInteraction = new ol.interaction.Draw({
          source: _draw_source,
          type: /** @type {ol.geom.GeometryType} */ (type),
			  //condition: ol.events.condition.singleClick,
			  //freehandCondition: ol.events.condition.noModifierKeys
		  freehandCondition: ol.events.condition.always,
			  //condition: ol.events.condition.never,		  
          style: new ol.style.Style({
            fill: new ol.style.Fill({
              color: 'rgba(255, 255, 255, 0.2)'
            }),
            stroke: new ol.style.Stroke({
              color: 'rgba(0, 0, 0, 0.5)',
              //lineDash: [10, 10],
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
		  /*
		  geometryFunction: function(coords, geom) {
			if (!geom) {
			  geom = new ol.geom.LineString(null);
			}
			if (undo_drawGeoElemInteraction) {
			  if (coords.length > 1) {
				//coords.pop();
				var coordinates = geom.getCoordinates();
				var diff = coords.length - coordinates.length;
				if (diff > 1) {
					coords.splice(coords.length - diff, diff - 1);
				}				
			  }
			  undo_drawGeoElemInteraction = false;
			}
			geom.setCoordinates(coords);
			return geom;
		  }
		  */
        });
			//var document_onkeyup = undefined;
		  // when a new feature has been drawn...
		  drawGeoElemInteraction.on('drawstart', function(evt) {
			var feature = evt.feature;
			var geom = feature.getGeometry();
			document.addEventListener('keyup', document_onkeyup = function() { //-- unbind on enddraw
			//map.addEventListener('keyup', function() {			
			  //-- double keyup listener (one local here and one global might overlap
			  console.log("document.addEventListener('keyup' local drawstart");
			  
			  if (event.keyCode === 27) {
				console.log("esc pressed");
			  /*
				if (geom.getType() === "LineString") {
				  var coords = geom.getCoordinates();
				  var len = coords.length;
				  console.log("undo");
				  if (len > 1) {
					//drawGeoElemInteraction.removeLastPoint();
					//geom.setCoordinates(geom.getCoordinates().slice(0, len - 1));					
				  }
				  
				  
				}
				*/
					//drawGeoElemInteraction.setActive(false);
				if (drawGeoElemInteraction)
				{
				if (geom.getType() === "LineString" ||
					geom.getType() === "MultiLineString"
				)
				{
					drawGeoElemInteraction.removeLastPoint();
					console.log("drawGeoElemInteraction.removeLastPoint()");
				}
				else
					console.log("geom.getType = " + geom.getType());
				
				//undo_drawGeoElemInteraction = true;
				
				if (geom.getCoordinates().length == 0)
				{
					map.removeInteraction(drawGeoElemInteraction);
					user_mouse_interaction_type = POINTER_INSPECT_FEATURE;
					console.log("geom.getCoordinates().length == 0");
				}
				}
			  }
			});
		});
		        drawGeoElemInteraction.on('drawend',
            function(event) {
				//event.preventDefault();
				
				if (document_onkeyup)
				{
					document.removeEventListener('keyup', document_onkeyup);
					//user_mouse_interaction_type = POINTER_INSPECT_FEATURE;
					document_onkeyup = undefined; // multiple?
					console.log("document.removeEventListener");
				}
			
				// only features for now for this event because they have event.feature associated
				if (user_mouse_interaction_type != POINTER_NEW_FEATURE)
				{
					event.preventDefault();
					return;
				}

				var sfr = get_selected_feature(event);				
				
				var feature = event.feature;
				
				if (sfr)
					feature = sfr;
				
				//-- ?
				//caveFeaturesDrawSource = undefined;
				//console.log(event);
				saveFeature(feature, event);
            }, this);

        
		map.addInteraction(drawGeoElemInteraction);
	}

const pointer_icons_base_url = 'assets/img/';

function getInteractionPointerIconUrl(interactionType)
{
	var file_name = undefined;
	
	if (interactionType == POINTER_NEW_CAVE)
		file_name = 'cave.png';
	else
	if (interactionType == POINTER_NEW_PICTURE)
		file_name = 'add_image.png';
	else
	if (interactionType == POINTER_NEW_FEATURE)
		file_name = 'flag.png';
	else
	if (interactionType == POINTER_INSPECT_FEATURE)
		file_name = undefined;
	else
		file_name = undefined;
	
	if (file_name)
		return pointer_icons_base_url + file_name;
	else
		return undefined;
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
		
		if (selected_new_feature_type == 3)
			newCave(undefined, coordinates, existingSelectedFeature);
		else
		if (selected_new_feature_type == 4)
			newCaveEntrance(undefined, coordinates, existingSelectedFeature);
		else
		if (featureTypes[selected_new_feature_type]["GroupType"] == "cave_feature")
			newCaveFeature(undefined, coordinates, existingSelectedFeature, selected_new_feature_type); // [long, lat];
		else
			newFeature(undefined, coordinates, existingSelectedFeature, selected_new_feature_type); // [long, lat]

			
}

/**
 * Gets GeoJson representation for the feature.
 * * @param {ol.Feature} feature
 * not to be confused with the Feature database type which describes a surface feature
 */
function getFeatureGeoJsonString(feature)
{
	if (feature === undefined)
	{
		throw "feature parameter is undefined";
	}
	
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

// expecting coordinates type [] 'EPSG:3857'
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
	
	//-- workaround to avoid selecting an existing multipoint feature on adding a new onerror	
	
	/* ??
	var added_point = new ol.Feature({
		//name: "point_feature_xx",
		geometry: new ol.geom.Point(_current_coordinate)
	});
	
	_draw_source.addFeature(added_point);
	
	existingSelectedFeature = added_point;
	*/
	$('#feature_string').val(getFeatureGeoJsonString(existingSelectedFeature));
	//console.log($('#feature_string').val());
	
	existingSelectedFeature.set("geoobject_type", "surface_feature"); // set for recognising feature type in the map interface for not posted features.
	
	$('#feature_type_id').val(featureType.Id);
	$('#feature_existing_point_id').val("");	
	
	
	$("#featureForm")[0].reset();
	
	
	selFeatureProps = undefined;
	
	if (existingSelectedFeature)
	{
		selFeatureProps = existingSelectedFeature.getProperties();
		$('#feature_existing_point_id').val(selFeatureProps.id);
	}
	
	var coord_type = typeof coordinates;
	var is_point_feature = !(coord_type == 'object' || coord_type == 'array');
	
	if (coordinates && is_point_feature)
	{
		//espg coordinates
		
		var coordinates_espg4326 = ol.proj.transform(coordinates, 'EPSG:3857', 'EPSG:4326');
		
		$('#feature_coords_lat').val(coordinates_espg4326[1]);
		$('#feature_coords_lon').val(coordinates_espg4326[0]);
		$('#feature_coords_label').text(rtrim(coordinates_espg4326[1]+"", 8) + ",  " + rtrim(coordinates_espg4326[0]+"", 8) + ((selFeatureProps != undefined) ? (" : " + selFeatureProps.gpx_name) : ""));		
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
          e.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postFeature.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#featureModal').modal('toggle'); 
				
				showNotification("Feature <b>" + formData.feature_name + "</b> was saved.");
				reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/ 
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
		
	//fillCaveEntries();
	
	if (feature_id)
	{
		$.getJSON("data/getFeature.php?feature_id=" + feature_id, function( data ) {
			
			$('#featureModal').modal();
			
			$('#feature_id').val(data.Id);
			$('#feature_name').val(data.Name);				
			$('#feature_description').val(data.Description);
			$('#feature_type_id').val(data.FeatureTypeId);
			
			//_caveFormServerData = data;
			//$('#featureModal').modal();
		});		
	}	
}

////////////////////////
// new picture

function newPicture(picture_id = undefined, coordinates, existingSelectedPicture)
{
	openNewPictureForm(picture_id, coordinates, existingSelectedPicture);	
	
	if (picture_id == undefined)
		$('#pictureModal').modal();
}

function openNewPictureForm(picture_id, coordinates, existingSelectedFeature)
{
	editMode = false;
	
	if (picture_id)
		editMode = true;
	
	//var featureType = featureTypes[new_feature_type];
	//$('#saveCave').off('click');
	$('#pictureForm').off('submit');
	
	//$("#caveForm").find("input, input[type=text], textarea").val("");
	$('#picture_id').val("");
	//$('#feature_coords_lon').val("");
	//$('#feature_coords_lat').val("");
	//$('#point_string').val(getFeatureGeoJsonString(existingSelectedFeature));
	
	//$('#feature_type_id').val(featureType.Id);
	$('#picture_existing_point_id').val("");
	
	
	$("#pictureForm")[0].reset();
	
	
	selFeatureProps = undefined;
	
	if (existingSelectedFeature)
	{
		selFeatureProps = existingSelectedFeature.getProperties();
		$('#picture_existing_point_id').val(selFeatureProps.id);
	}
	
	if (coordinates)
	{
		var coordinates_espg4326 = ol.proj.transform(coordinates, 'EPSG:3857', 'EPSG:4326');
		
		$('#point_coords_lat').val(coordinates_espg4326[1]);
		$('#point_coords_lon').val(coordinates_espg4326[0]);
		$('#picture_coords_label').text(rtrim(coordinates_espg4326[1]+"", 8) + ",  " + rtrim(coordinates_espg4326[0]+"", 8) + ((selFeatureProps != undefined) ? (" : " + selFeatureProps.gpx_name) : ""));		
	}	
		
	if (editMode)
		$('#pictureModalTitleLabel').text("Edit picture");
	else
	{			
		$('#pictureModalTitleLabel').text("New picture");
		//$('#picture_type_id').val(new_feature_type);
		
		
		/*feature_type_symbol_path = featureType.SymbolPath;
		
		if (feature_type_symbol_path && feature_type_symbol_path != "null")
		{
			feature_type_symbol_path = symbols_path + feature_type_symbol_path;
			$('#featureModalTitleLabel').prepend("<img src='" + feature_type_symbol_path + "' height='24'/>");
		}
		*/
	}
	
	/*	
	$('#saveCave').on('click', function(e) {
		//e.preventDefault(); // To prevent following the link (optional)		
		//onSaveCave(this);
		//$(this).submit();
	});
	*/
	$('#pictureForm').on('submit', function(e) {
			e.preventDefault();

			var formInputRegularData = $(this).serializeObject();
			//var formData = new FormData($(this));
			formData = new FormData($(this));
			formData.append( 'form_data', JSON.stringify(formInputRegularData));
			formData.append( 'file', $( '#file' )[0].files[0] );
			
			/*formData.append('picture_coords_lat', $('#picture_coords_lat').val());
			formData.append('picture_coords_lon', $('#picture_coords_lon').val());
			formData.append('picture_id', $('#picture_id').val());
			formData.append('picture_existing_point_id', $('#picture_existing_point_id').val());
			*/
			
		  //var serializedFormData = JSON.stringify(formData);
		  
		  $.ajax({
  url: 'data/postPicture.php',
  data: formData,
  processData: false,
  contentType: false,
  type: 'POST',
  success: function(php_script_response){    
					if (php_script_response.indexOf("201") >= 0) // if (php_script_response == "201")
						{
							console.log('close picture form');
							$('#pictureModal').modal('toggle');
							
							showNotification("Picture <b>" + formData.picture_name +"</b> was added.");
							
							reloadMapFeatures();
						}
					else
						alert(php_script_response); // display response from the PHP script, if any
  }
});
});
/*
		  postDataAsync("data/postPicture.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#pictureModal').modal('toggle');
				reloadMapFeatures();				
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
*/		
	//fillCaveEntries();
	
	if (picture_id)
	{
		$.getJSON("data/getPicture.php?picture_id=" + picture_id, function( data ) {
			
			$('#pictureModal').modal();
			
			$('#picture_id').val(data.Id);
			$('#picture_name').val(data.Name);				
			$('#picture_description').val(data.Description);
			$('#picture_type_id').val(data.Feature_type_id);

			$('#point_coords_lat').val("");
			$('#point_coords_lon').val("");
			$('#point_string').val("");
			//$('#picture_existing_point_id').val("");
			//_caveFormServerData = data;
			//$('#featureModal').modal();
		});
	}
}

// end new picture
///////////////////////

function newCave(cave_id = undefined, coordinates, existingSelectedFeature)
{
	openNewCaveForm(cave_id, coordinates, existingSelectedFeature);	
	
	if (cave_id == undefined)
		$('#caveModal').modal();
}


//var _caveFormServerData;

//////////////////////
//  New cave form

function initNewCaveForm()
{
	fillCaveTypeEntries();
}

function initNewFeatureForm()
{
	$('#featureModal').on('shown.bs.modal', function () {
		$('#feature_name').focus();
	})
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
	
	$('#ce_feature_string').val(getFeatureGeoJsonString(existingSelectedFeature));
	// $('#ce_feature_string').val(getFeatureGeoJsonString(coordinates));
	
	$("#caveForm")[0].reset();
	
	
	selFeatureProps = undefined;
	
	if (existingSelectedFeature)
	{
		selFeatureProps = existingSelectedFeature.getProperties();
		$('#entrance_existing_point_id').val(selFeatureProps.id);	 
	}
	
	if (coordinates)
	{
		var coordinates_espg4326 = ol.proj.transform(coordinates, 'EPSG:3857', 'EPSG:4326');
		
		$('#cave_coords_lat').val(coordinates_espg4326[1]);
		$('#cave_coords_lon').val(coordinates_espg4326[0]);
		
		var feature_name = selFeatureProps.name;		
		
		if (selFeatureProps.gpx_name) 
			feature_name = selFeatureProps.gpx_name;
		
		$('#cave_coords_label').text(rtrim(coordinates_espg4326[1]+"", 8) + ",  " + rtrim(coordinates_espg4326[0]+"", 8) + ((selFeatureProps != undefined) ? (" : " + feature_name) : ""));
		
		$('#cf_name').val(feature_name);
	}
	
	if (editMode)
		$('#caveModalTitleLabel').text("Edit cave '" +selFeatureProps['cave_name']  +"'");
	else
		$('#caveModalTitleLabel').text("New cave");
	
	/*	
	$('#saveCave').on('click', function(e) {
		//e.preventDefault(); // To prevent following the link (optional)		
		//onSaveCave(this);
		//$(this).submit();
	});
	*/
	$('#caveForm').on('submit', function(e) {
	
		  //$('#cf_rock_type_id').val(-2);		
        e.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  formData.cf_rock_type_id = -2;
		  
		  
		  postDataAsync("data/postCave.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#caveModal').modal('toggle'); 
				last_added_cave_id = undefined; //-- need to return the cave_id from postCave.php or to load load the last added cave in fillCavePicker()
				
				showNotification("Cave <b>" + formData.cf_name + "</b> was saved.");
				reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/				
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
		
	//fillCaveEntries();
	
	if (cave_id)
	{
		$.getJSON("data/getCave.php?cave_id=" + cave_id, function( data ) {
			
			$('#caveModal').modal();
			
			$('#cave_id').val(data.Id);
			$('#cf_name').val(data.Name);
			$('#cf_other_toponyms').val(data.Othertoponyms);
			$('#cf_identification_code').val(data.Identificationcode);
			$('#cf_description').val(data.Description);
			$('#cf_rock_age').val(data.Rockage);
			$('#cf_region').val(data.Region);
			$('#cf_hydrographic_basin').val(data.Hydrographicbasin);
			$('#cf_valley').val(data.Valley);
			$('#cf_tributary_river').val(data.Tributaryriver);
			$('#cf_closest_address').val(data.Closestaddress);
			$('#cf_land_registry_number').val(data.Landregistrynumber);
			$('#cf_is_show_cave').val(data.Isshowcave);
			$('#cf_show_cave_length').val(data.Showcavelength);
			$('#cf_website').val(data.Website);
			$('#cf_depth').val(data.Depth);
			$('#cf_positive_depth').val(data.Positivedepth);
			$('#cf_negative_depth').val(data.Negativedepth);
			$('#cf_surveyed_length').val(data.Surveyedlength);
			$('#cf_straight_length').val(data.Straightlength);
			$('#cf_volume').val(data.Volume);
			$('#cf_ramification_index').val(data.Ramificationindex);
			$('#cf_discovery_date').val(data.Discoverydate);
			$('#cf_discoverer').val(data.Discoverer);
			$('#cf_real_extension').val(data.Realextension);
			$('#cf_projected_extension').val(data.Projectedextension);
			$('#cf_exploration_status').val(data.Explorationstatus);
			$('#cf_protection_class').val(data.Protectionclass);
			$('#cf_potential_depth').val(data.Potentialdepth);
			
			$('#cf_cave_type').val(data.Typeid);
			$('#cf_rock_type_id').val(data.Rocktypeid);
			$('#cf_cave_age').val(data.Caveage);
			//$('#').val(data.);
			
			$('#cf_cave_type').selectpicker('refresh');
			$('#cf_cave_age').selectpicker('refresh');
			$('#cf_rock_type_id').selectpicker('refresh');
			
			//_caveFormServerData = data;
			//$('#caveModal').modal();
		});		
	}
}

// end New cave form
//////////////////////////

//////////////////////////
// Cave details form

function initCaveDetailsForm()
{
	//fillCaveEntries();
}

function openCaveDetailsForm(cave_id)
{
	editMode = false;
	
	if (cave_id)
		editMode = true;
		
	//$('#saveCave').off('click');
	$('#caveDetailsForm').off('submit');
	
	//$("#caveForm").find("input, input[type=text], textarea").val("");
	$('#cave_id').val("");
	$('#cave_coords_lon').val("");
	$('#cave_coords_lat').val("");
	//$('#entrance_existing_point_id').val("");
		
	// $('#ce_feature_string').val(getFeatureGeoJsonString(``));
	
	//$("#caveDetailsForm")[0].reset();
	
	
	selFeatureProps = undefined;
	
	/*
	if (existingSelectedFeature)
	{
		selFeatureProps = existingSelectedFeature.getProperties();
		$('#entrance_existing_point_id').val(selFeatureProps.id);	 
	}
	*/
	/*
	$('#caveForm').on('submit', function(e) {
          e.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postCave.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#caveModal').modal('toggle'); 
				reloadMapFeatures();
				//-- $("caveModal").modal('hide');
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
	*/
	//fillCaveEntries();	
	$('#caveDetailsAddFilesButton').off('click');
	$('#caveDetailsAddFilesButton').on('click', function(event) {
		openUploadFilesForm(cave_id, "cave");
	});

	//-- cave form?
	$('#caveDetailsAddFilesButton').on('hidden.bs.modal', function () {
		refreshCaveFilesTable(cave_id);
	});
	
	if (cave_id)
	{
		$.getJSON("data/getCave.php?cave_id=" + cave_id, function( data ) {
			
			$('#caveDetailsModal').modal();
			
			$('#cd_cave_id').val(data.Id);
			$('#cd_cave_name').val(data.Name);				
			$('#cd_cave_description').val(data.Description);
			$('#cd_cave_type').val(data.Typeid);
			$('#cd_cave_identifier').val(data.Locationidentifier);

			$('#upload_file_cave_id').val(data.Id);
			
			$('#cave_type').selectpicker('refresh');
			
			
			//_caveFormServerData = data;
			//$('#caveModal').modal();
		});		
	}
	
	
	refreshCaveFilesTable(cave_id);
}
	
function refreshCaveFilesTable(cave_id)
{
	var form_data = JSON.stringify( { cave_id: cave_id } );
	
    $.ajax({
                url: 'data/getFiles.php', // point to server-side PHP script 
                dataType: 'json', // dataType: 'jsonp', //dataType: 'text',  
                cache: false,
                contentType: false,
                processData: false,
                data: form_data,
                type: 'post',
                success: function(data){
							var dataSet = [];

							var file_directory = 'data/uploader/files/';
							
							data.forEach(function(item) {
								var file_url = file_directory + item.file_name;
								var file_name_html = "<a href='" + file_url + "' target='_blank' >" + item.file_name + "</a>";
								dataSet.push([file_name_html, item.size, item.add_time, item.object_type]);
							});							
							  
							/*$('#cave_files_table').DataTable({
								//"ajax": '../ajax/data/arrays.txt'
								data: dataSet,
							});*/
							$('#cave_files_table').DataTable().clear();
							$('#cave_files_table').DataTable()
								.rows.add(dataSet)
								.draw();
                },
				error:  function(jqXHR, textStatus, errorThrown )
				{
					//onFailure(textStatus); //-- show error code returned
					console.error(errorThrown);
					console.error("Error loading feature types: " + textStatus + " " + errorThrown);
					//alert(errMsg);
				}				
     });	

/*	$('#cave_files_table').DataTable( {
        "ajax": '../ajax/data/arrays.txt'
    } );*/

}


function initCaveDetailsUploadControl()
{
	/*$('#fileupload').fileupload({
		//formData: {example: 'test'}
		url: 'speogis/data/uploader'
	}).on('fileuploadsubmit', function (e, data) {
		data.formData = data.context.find(':input').serializeArray();
	});
	
	$('#fileupload').bind('fileuploadsubmit', function (e, data) {
		// The example input, doesn't have to be part of the upload form:
		var input = $('#input');
		data.formData = {example: input.val()};
		if (!data.formData.example) {
		  data.context.find('button').prop('disabled', false);
		  input.focus();
		  return false;
		}
	});
	*/
    //'use strict';

    // Initialize the jQuery File Upload widget:
    /*$('#fileupload').fileupload({
        // Uncomment the following to send cross-domain cookies:
        //xhrFields: {withCredentials: true},
        url: 'data/uploader/'
		//url: 'data/uploader'
    });
	*/
	
	 /*$('.fileupload').fileupload({
        // Uncomment the following to send cross-domain cookies:
        //xhrFields: {withCredentials: true},
        //url: 'data/uploader/'
		//url: 'data/uploader'
    });*/


	/*
    // Enable iframe cross-domain access via redirect option:
    $('#fileupload').fileupload(
        'option',
        'redirect',
        window.location.href.replace(
            /\/[^\/]*$/,
            '/cors/result.html?%s'
        )
    );
	*/
	
	/*
    if (window.location.hostname === 'blueimp.github.io') {
        // Demo settings:
        $('#fileupload').fileupload('option', {
            url: 'speogis/data/uploader',
            // Enable image resizing, except for Android and Opera,
            // which actually support image resizing, but fail to
            // send Blob objects via XHR requests:
            disableImageResize: /Android(?!.*Chrome)|Opera/
                .test(window.navigator.userAgent),
            maxFileSize: 999000,
            acceptFileTypes: /(\.|\/)(gif|jpe?g|png)$/i
        });
        // Upload server status check for browsers with CORS support:
        if ($.support.cors) {
            $.ajax({
                url: '//jquery-file-upload.appspot.com/',
                type: 'HEAD'
            }).fail(function () {
                $('<div class="alert alert-danger"/>')
                    .text('Upload server currently unavailable - ' +
                            new Date())
                    .appendTo('#fileupload');
            });
        }
    } else 
	*/
	{
        // Load existing files:

		
        $('.fileupload').addClass('fileupload-processing');		
        $.ajax({
            // Uncomment the following to send cross-domain cookies:
            //xhrFields: {withCredentials: true},
            url: $('#fileupload_cave').fileupload('option', 'url'),
            dataType: 'json',
            context: $('#fileupload_cave')[0]
        }).always(function () {
            $(this).removeClass('fileupload-processing');
        }).done(function (result) {
            $(this).fileupload('option', 'done')
                .call(this, $.Event('done'), {result: result});
        });
    }	
	
	// $('#fileupload').fileupload('destroy');
	
	$('#fileupload_cave').on('submit', function(e) {
          e.preventDefault();
		  $('#fileupload_cave').modal('toggle'); 
	});
	
}
// end Cave details form
////////////////////////////


////////////////////////////
// begin Cave entrance form

var last_added_cave_id = undefined;

function newCaveEntrance(cave_entrance_id = undefined, coordinates, existingSelectedFeature)
{
	openNewCaveEntranceForm(cave_entrance_id, coordinates, existingSelectedFeature);
	
	if (cave_entrance_id == undefined)
		$('#caveEntranceModal').modal();
}

function openNewCaveEntranceForm(cave_entrance_id, coordinates, existingSelectedFeature)
{
	editMode = false;
	
	if (cave_entrance_id)
		editMode = true;

	fillCavePicker(); //- must wait for list of caves to load (async operation) to open new cave entrance form
	
	//$('#saveCave').off('click');
	$('#caveEntranceForm').off('submit');
	
	//$("#caveForm").find("input, input[type=text], textarea").val("");
	$('#cave_entrance_id').val("");
	$('#cave_entrance_coords_lon').val("");
	$('#cave_entrance_coords_lat').val("");
	$('#cave_entrance_existing_point_id').val("");
	$('#cave_entrance_name').val("");
	$('#cave_entrance_description').val("");
			
	// $('#ce_feature_string').val(getFeatureGeoJsonString(coordinates));
	
	$("#caveEntranceForm")[0].reset();
	
	if (existingSelectedFeature) //-- ?
		$('#cave_entrance_feature_string').val(getFeatureGeoJsonString(existingSelectedFeature));
	
	if (last_added_cave_id !== undefined)
	{
		$('#cave_entrance_cave_id').val(last_added_cave_id);
		$('#cave_entrance_cave_id').selectpicker('refresh');
	}

	selFeatureProps = undefined;
	
	if (existingSelectedFeature)
	{
		selFeatureProps = existingSelectedFeature.getProperties();
		$('#cave_entrance_existing_point_id').val(selFeatureProps.id);	 
	}
	
	if (coordinates)
	{
		var coordinates_espg4326 = ol.proj.transform(coordinates, 'EPSG:3857', 'EPSG:4326');
		
		$('#cave_entrance_coords_lat').val(coordinates_espg4326[1]);
		$('#cave_entrance_coords_lon').val(coordinates_espg4326[0]);
		$('#cave_entrance_coords_label').text(rtrim(coordinates_espg4326[1]+"", 8) + ",  " + rtrim(coordinates_espg4326[0]+"", 8) + ((selFeatureProps != undefined) ? (" : " + selFeatureProps.gpx_name) : ""));
	}	
	
	if (editMode)
		;//$('#caveEntranceModalTitleLabel').text("Edit cave entrance '" + selFeatureProps['cave_entrance_name'] +"'");
	else
		$('#caveEntranceModalTitleLabel').text("New cave entrance");

	/*	
	$('#saveCave').on('click', function(e) {
		//e.preventDefault(); // To prevent following the link (optional)		
		//onSaveCave(this);
		//$(this).submit();
	});
	*/
	$('#caveEntranceForm').on('submit', function(e) {
          e.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postCaveEntrance.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#caveEntranceModal').modal('toggle'); 
				
				showNotification("Cave entrance <b>" + formData.cave_entrance_name + "</b> was saved.");
				
				reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/ 
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
		
	//fillCaveEntries();
	
	if (editMode) // cave_entrance_id
	{
		$.getJSON("data/getCaveEntrance.php?cave_entrance_id=" + cave_entrance_id, function( data ) {
			
			$('#caveEntranceModal').modal();
			
			$('#cave_entrance_id').val(data.Id);
			$('#cave_entrance_name').val(data.Name);				
			$('#cave_entrance_description').val(data.Description);
			$('#cave_entrance_type').val(data.Typeid);
			$('#cave_entrance_cave_id').val(data.Caveid);

			$('#cave_entrance_type').selectpicker('refresh');
			$('#cave_entrance_cave_id').selectpicker('refresh');
			
			$('#caveEntranceModalTitleLabel').text("Edit cave entrance '" + data.Name +"'");				
				
			
			//_caveFormServerData = data;
			//$('#caveModal').modal();
		});		
	}
}

function fillCaveEntranceTypeEntries()
{
 
	$.getJSON("data/getCaveEntranceTypes.php", function( data ) {
	var items = [];
	
	$('#cave_entrance_type').find('option').remove();
	
	$.each( data, function( key, val ) {
		//items.push( "<li id='" + key + "'>" + val + "</li>" );
		//$('#cave_type').append('<li id="' + val.Id + '" type_name="' + val.Id + '" ><a href="#">' + val.Name + '</a></li>'); // $('#cave_type').append('<li id="' + val.Id + '" type_name="' + val.Name + '" ><a href="#">' + val.Name + '</a></li>');
		$('#cave_entrance_type').append('<option value="' + val.Id + '" >' + val.Name + '</option>');
	});
	
	$('#cave_entrance_type').selectpicker('refresh');
	
	//console.log("_caveFormServerData.Typeid = " + _caveFormServerData.Typeid);		
	});
}

function initNewCaveEntranceForm()
{
	fillCaveEntranceTypeEntries();
	//fillCavePicker();
	
/*	$('#cf_rock_age').spinedit({
		minimum: -10000,
		maximum: 10000,
		step: 100,
		value: 0,
		numberOfDecimals: 0
	});
*/	
	
	/*
	$('#cf_rock_age').TouchSpin({
                min: 0,
                max: 100,
                step: 0.1,
                decimals: 2,
                boostat: 5,
                maxboostedstep: 10,
                postfix: 'mil. years'
            });
			*/
}

function fillCavePicker()
{ 
	$.getJSON("data/getCaves.php", function( data ) {
	var items = [];
	
	$('#cave_entrance_cave_id').find('option').remove();
	
	$.each( data, function( key, val ) {
		//items.push( "<li id='" + key + "'>" + val + "</li>" );
		//$('#cave_type').append('<li id="' + val.Id + '" type_name="' + val.Id + '" ><a href="#">' + val.Name + '</a></li>'); // $('#cave_type').append('<li id="' + val.Id + '" type_name="' + val.Name + '" ><a href="#">' + val.Name + '</a></li>');
		$('#cave_entrance_cave_id').append('<option value="' + val.Id + '" >' + val.Name + '</option>');
		
		last_added_cave_id = val.Id; //-- workaround to get last added cave, but must wait for list of caves to load to open new cave entrance form
	});
	
	$('#cave_entrance_cave_id').selectpicker('refresh');
	
	//console.log("_caveFormServerData.Typeid = " + _caveFormServerData.Typeid);		
	});
}

// end Cave entrance form
////////////////////////////

function postDataAsync(_url, data, onSuccess, onFailure)
{
	$.ajax({
		type: "POST",
		url: /*"http://localhost/speogis/" + */_url, //"/webservices/PodcastService.asmx/CreateMarkers",
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
	//-- better solution might be to reload all layers, or find a way to reload map?
	/*_geo_file_layer.changed();
	_db_features_layer.changed();
	caveFeaturesLayer.changed();*/
	//? _picturesLayer.changed();
	
	//return;
	//--
	setTimeout(function()
	{
	console.log("reloadMapFeatures()");
	/*
	_db_features_layer.getSource().changed();
		
	map.getLayers().forEach(function (layer) {
		if (!(layer instanceof ol.layer.Group) && layer.getSource())
		{
			var source = layer.getSource();
			
			layer.changed();
			
			if (layer instanceof ol.layer.Vector)
			{
				//layer.refresh({force:true});
				//layer.redraw();
			}
			
			// source.clear();
			//source.updateParams({"time": Date.now()});
			
			/*var params = source.getParams();
				params.t = new Date().getMilliseconds();
				source.updateParams(params);
		}
		
	map.updateSize(); // setSize
	map.render();
	map.renderSync();
	//map.dispatchChangeEvent();
	//vectorSource.dispatchChangeEvent();
	});
	*/
	/*
	$("#mapdiv").empty();
	$("#mapdiv").off();
	$("#mapdiv").unbind();
	
	initMap();
	*/
	/*_geo_file_layer.changed();
	_db_features_layer.changed();
	caveFeaturesLayer.changed();
	*/
	loadFeatures(); //-- should implement better feature reloading: currently the entire layer gets reinstantiated and the new layer is added to map; flicker appears
	loadCaveFeatures();
	initThumbnailLoading();
	}, 500);
}

function fillCaveTypeEntries()
{
 
	$.getJSON("data/getCaveTypes.php", function( data ) {
	var items = [];
	
	$('#cf_cave_type').find('option').remove();
	
	$.each( data, function( key, val ) {
		//items.push( "<li id='" + key + "'>" + val + "</li>" );
		//$('#cave_type').append('<li id="' + val.Id + '" type_name="' + val.Id + '" ><a href="#">' + val.Name + '</a></li>'); // $('#cave_type').append('<li id="' + val.Id + '" type_name="' + val.Name + '" ><a href="#">' + val.Name + '</a></li>');
		$('#cf_cave_type').append('<option value="' + val.Id + '" >' + val.Name + '</option>');		
	});
	
	$('#cf_cave_type').selectpicker('refresh');
	
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
			//console.log(data);
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
		
		//gotoMapElement($(this).val());
		//gotoDbElement
		throw "not implemented";
		
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
	
	$('#tripreport_place').selectpicker().filter('.with-ajax').ajaxSelectPicker(options);
	
	$('#tripreport_place').on( "change", function(evt) 
	{
		// workaround to avoid a bug (in the picker library or here?) that triggers change on loading a search set of elements from server
		
		//if ((evt.timeStamp - searchPickerResponseReceivedTime) < 1250)
		//	return;
		
		//return;
		//event.preventDefault();
		
		//var selectedText = $(this).find("option:selected").text();
		var selectedValue = $(this).find("option:selected").val();
		
		console.log("on change> " + $(this).val() );		
	});
	
}

	function gotoMapElement(elementDbId)
	{
		console.log("gotoMapElement " + elementDbId);		
		//db_features_layer.getSource().forEachFeature(function(feature){		
		//var features = db_features_layer.getSource().getFeatures();
		
		var features = _db_features_layer.getSource().getFeatures();
		
		var found = false;
		for(var index=0; index < features.length; index++)
		//if (features[index].getProperties().id == elementDbId)
		if (features[index].getProperties().point_id == elementDbId)
		{
			//console.log("found");
			//console.log(features[index]);
			
			//ol.proj.transform(features[index].getGeometry().getCoordinates(), 'EPSG:3857', 'EPSG:4326');
			
			flyTo(features[index]);
			
			found = true;
			break;
		}
		
		if (!found)
			throw "the element was not found in the list of features";
	}

	function gotoDbElement(element)
	{
		console.log("gotoMapElement " + element.name);
		//db_features_layer.getSource().forEachFeature(function(feature){		
		//var features = db_features_layer.getSource().getFeatures();
		
		//var features = _db_features_layer.getSource().getFeatures();

		var coordinates_espg3857 = ol.proj.transform([element.c_lat, element.c_lon], 'EPSG:4326', 'EPSG:3857');
		
		//-- better cave feature/polygon detection
		if (element.c_lat === undefined)
		{
			caveFeaturesDrawSource.forEachFeature(function (f) 
			{ 
					if (f.get("point_id") == element.point_db_id)
					{
						flyToCoordinates(undefined, f.getGeometry().getExtent(), {padding: [50, 50, 50, 50]});
						map.getView().setZoom(map.getView().getZoom() - 1);
					}
			});
		}
		else
			flyToCoordinates(coordinates_espg3857);
		
		//todo: store feature identifier, after loading features at that poitn, select it on the map (tooltip)
	}
	
	function flyTo(feature)
	{	
		//-- better get corrdinates from db data?
		//var coordinates = [feature.getProperties().long, feature.getProperties().lat];
		// flyToCoordinates(ol.proj.fromLonLat(coordinates));
		
		var feature_geometry = feature.getGeometry();
		var coordinates;
		
		if (feature_geometry == undefined)
		{	
			var coordinates_espg4326 = [feature.getProperties().long_from_point, feature.getProperties().lat_from_point];
			coordinates = ol.proj.transform(coordinates_espg4326, 'EPSG:4326', 'EPSG:3857');
		}
		else
			coordinates = feature.getGeometry().getCoordinates();		
		
		flyToCoordinates(coordinates);		
	}
	
	//var featureToShowCoordinates = undefined;
	
	function flyToCoordinates(coordinates, extent = undefined)
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
		, bounce
		);
		
		if (extent)
			map.getView().fit(extent, map.getSize());
		else
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

	function initFeatureSearchControl()
	{
	/*
var substringMatcher = function(strs) {
  return function findMatches(q, cb) {
    var matches, substringRegex;

    // an array that will be populated with substring matches
    matches = [];

    // regex used to determine if a string contains the substring `q`
    substrRegex = new RegExp(q, 'i');

    // iterate through the pool of strings and for any string that
    // contains the substring `q`, add it to the `matches` array
    $.each(strs, function(i, str) {
      if (substrRegex.test(str)) {
        matches.push(str);
      }
    });

    cb(matches);
  };
};
*/
  var engine, remoteHost, template, empty;
  
  remoteHost = '';
  template = Handlebars.compile($("#result-template").html());
  empty = Handlebars.compile($("#empty-template").html());
  
var featuresDataSource = new Bloodhound({
  datumTokenizer: Bloodhound.tokenizers.obj.whitespace('name'),  
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  //datumTokenizer: Bloodhound.tokenizers.obj.whitespace('name', 'name'),  
  
  identify: function(obj) { console.log("identify" + obj); return obj.id; },
  dupDetector: function(a, b) { return a.id === b.id; },
  prefetch: 'data/getSearchFeatures.php',
  remote: {
    url: remoteHost + 'data/getSearchFeatures.php?q=%QUERY',
    wildcard: '%QUERY'
  },
  transform: function(data)
  {
	console.log('p: ' + data);
  }
});

featuresDataSource.initialize();

// ensure default users are read on initialization
  //engine.get('1090217586', '58502284', '10273252', '24477185')
/*
  function engineWithDefaults(q, sync, async) {
    if (q === '') {
      sync(engine.get('1090217586', '58502284', '10273252', '24477185'));
      async([]);
    }

    else {
      engine.search(q, sync, async);
    }
  }
*/
$('#searchFeatureControl').typeahead({
 //$('#searchFeatureControl .typeahead').typeahead({
  hint: $('.Typeahead-hint'),
  menu: $('.Typeahead-menu'),
  //hint: true,
  highlight: true,
  minLength: 1,
  classNames: {
      open: 'is-open',
      empty: 'is-empty',
      cursor: 'is-active',
      suggestion: 'Typeahead-suggestion',
      selectable: 'Typeahead-selectable'
    },
  //display: 'name',  
  //limit: 9
  //suggestion: Handlebars.compile('<div><strong>{{value}}</strong> – {{year}}</div>')
  /*
  filter: function (parsedResponse) {
            // parsedResponse is the array returned from your backend
            console.log(parsedResponse);

            // do whatever processing you need here
            return parsedResponse;
        }
		*/
},
{
  name: 'features',
  source: featuresDataSource, //substringMatcher(states)
  displayKey: 'name',
    templates: {
      suggestion: template,
      empty: empty,
	  //header: '<h3 class="league-name">Teams</h3>'
    },
})
/*.on('typeahead:asyncrequest', function() {
    $('.Typeahead-spinner').show();
  })
  .on('typeahead:asynccancel typeahead:asyncreceive', function() {
    $('.Typeahead-spinner').hide();
  })*/;	

$('#searchFeatureControl').bind('typeahead:select', function(ev, suggestion) {
  console.log('Selection: ');
  console.log(suggestion);
  gotoDbElement(suggestion); // id
  // gotoMapElement(suggestion.point_db_id); // id
});
	}


function editCave(cave_id)
{
	//newCave(cave_id, undefined, undefined);
}

///////////////////////////////////////
// map view functionality

function addView()
{
	var view_name = prompt("View name:", "");
    
    if (view_name != null) 
	{        	
	var coordinates_espg4326 = ol.proj.transform(map.getView().getCenter(), 'EPSG:3857', 'EPSG:4326');
    
	var checked_layer_index = $('#layerswitcher input[name=layer]:checked').val();
	
	var view_data = {
		mapview_name: view_name,
		center_geometry_coordinates: coordinates_espg4326, // JSON.stringify(coordinates_espg4326)
		properties: { 
						zoom: map.getView().getZoom(),
						layer_index: checked_layer_index,
						map_configuration: getMapLayerConfiguration()
					},		
	};
	
    $.ajax({
                url: 'data/postMapView.php', // point to server-side PHP script 
                dataType: 'text',  // what to expect back from the PHP script, if anything
				
				data: JSON.stringify(view_data), // JSON.stringify({ Markers: markers })
				contentType: "application/json; charset=utf-8",
				//dataType: "json",
				
                cache: false,
                contentType: false,
                processData: false,
                //data: view_data,
                type: 'post',
                success: function(php_script_response){
					if (php_script_response.indexOf("201") >= 0) // if (php_script_response == "201")
					{
						showNotification("Map view <b>" + view_name + "</b> was added.", { from: "bottom", align: "right" });
						refreshViewList();//alert("saved");
					}
					else
						alert(php_script_response); // display response from the PHP script, if any
                }
     });			 	 
	 }
}

function setDefaultView(map_view_id)
{
	var view_data = {
		mapview_id: map_view_id
	};
	
	var view_name = map_views[map_view_id].mapview_name;
	
    $.ajax({
                url: 'data/setDefaultMapView.php', // point to server-side PHP script 
                dataType: 'text',  // what to expect back from the PHP script, if anything
				
				data: JSON.stringify(view_data), // JSON.stringify({ Markers: markers })
				contentType: "application/json; charset=utf-8",
				//dataType: "json",
				
                cache: false,
                contentType: false,
                processData: false,
                //data: view_data,
                type: 'post',
                success: function(php_script_response){
					if (php_script_response.indexOf("201") >= 0) // if (php_script_response == "201")
					{
						showNotification("Map view <b>" + view_name + "</b> is now the default map view which will get loaded when you start the application.", { from: "bottom", align: "right" });
						refreshViewList();//alert("saved");
					}
					else
						alert(php_script_response); // display response from the PHP script, if any
                }
     });
}

function deleteViews(map_view_id, delete_all = false)
{
	var question_text = "";
	
	if (delete_all)
		question_text = "Doriti sa stergeti toate punctele de vizualizare?";
	else
		question_text = "Doriti sa stergeti punctul de vizualizare?";
		
		
	var q_res = confirm(String.format(question_text));
		
	if (q_res)
	{

	
	var view_delete_data = {
		map_view_id: map_view_id,
		delete_all: delete_all
	};
	
	var view_name = map_views[map_view_id].mapview_name;
	
    $.ajax({
                url: 'data/deleteMapViews.php', // point to server-side PHP script 
                dataType: 'text',  // what to expect back from the PHP script, if anything
				
				data: JSON.stringify(view_delete_data), // JSON.stringify({ Markers: markers })
				contentType: "application/json; charset=utf-8",
				//dataType: "json",
				
                cache: false,
                contentType: false,
                processData: false,
                //data: view_data,
                type: 'post',
                success: function(php_script_response){					
					if (php_script_response.indexOf("201") >= 0) // if (php_script_response == "201")
					{
						showNotification("Map view <b>" + view_name + "</b> was deleted.", { from: "bottom", align: "right" });
						refreshViewList();//alert("saved");
					}
					else
						alert(php_script_response); // display response from the PHP script, if any
                }
     });			 	 	 
	 }
}

function initViews(set_center = undefined)
{
	$("#mapViewsControlBox").empty();
	
	$.ajax({
		type: "GET",
		url: "data/getMapViews.php", //"/webservices/PodcastService.asmx/CreateMarkers",
		// The key needs to match your method's input parameter (case-sensitive).
		//data: JSON.stringify(data), // JSON.stringify({ Markers: markers })
		contentType: "application/json; charset=utf-8",
		dataType: "json",
		success: function(data) {
			//console.log(data);
			//featureTypes = {}; // for key/value indexed object
			var html_content = "";
			
			html_content += "";
			
			for (var property in data) {
				if (data.hasOwnProperty(property)) 
				{
					//featureTypes.push(data[property]);
					map_views[data[property].id] = data[property];					
					
					var map_view_id = data[property].id;
					var map_view_name = data[property].mapview_name;
					var map_view_center_geometry_lat = data[property].center_geometry_coordinates[0];
					var map_view_center_geometry_lon = data[property].center_geometry_coordinates[1];
					var map_view_zoom_level = data[property].properties.zoom;
					var map_view_is_default = data[property].is_default;
					
					var home_icon_name = (map_view_is_default == true) ? "house.png" : "house_faded.png";
					
					if (map_view_is_default == true)
					{
						default_map_view = data[property];
						default_map_view_id = map_view_id;
						
						if (set_center && !map_center_set_by_url)
						{									
							showView(default_map_view_id);
						}
					}
					
					var control = ("<span onclick='showView(" + map_view_id + ");' class='map_view_item' ><img src='" + pointer_icons_base_url + "map.png' height='18'/>&nbsp;&nbsp;" + map_view_name + "</span>" +
					 "<div class='map_view_item_operations' ><span onclick='setDefaultView(" + map_view_id + ");' class='map_view_item' ><img src='" + pointer_icons_base_url + home_icon_name + "' height='16'/></span>" +
					 " <span onclick='deleteViews(" + map_view_id + ");' class='map_view_item' ><img src='" + pointer_icons_base_url + "round-delete-button.png' height='16'/></span></div>" + "<br/>");

/*
					var control = ("<button onclick='showView(" + map_view_id + ");' style='background-color:transparent; border-color:transparent;' ><img src='" + pointer_icons_base_url + "map.png' height='24'/>" + map_view_name + "</button>" +
					 " <button onclick='setDefaultView(" + map_view_id + ");' style='background-color:transparent; border-color:transparent;' ><img src='" + pointer_icons_base_url + home_icon_name + "' height='16'/></button>" +
					 " <button onclick='deleteViews(" + map_view_id + ");' style='background-color:transparent; border-color:transparent;' ><img src='" + pointer_icons_base_url + "round-delete-button.png' height='16'/></button> " + "<br/>");
*/					 
					html_content += control;
					//control.appendTo($("#mapViewsControlBox"));
				}
			}

			html_content += ""; // <br/>
			
			var add_map_view_control = ("<button onclick='addView();' style='background-color:transparent; border-color:transparent;' >Add</button>"); // Add map view
			html_content += add_map_view_control;
			
			var delete_all_control = ("<button onclick='deleteViews(undefined, true);' style='background-color:transparent; border-color:transparent;' >Delete all</button><br/>"); // Delete all map views
			html_content += delete_all_control;			
			
			html_content += ""; // <br/>
			//$("#mapViewsControlBox").append($(html_content));
			$("<div>" + html_content + "</div>").appendTo($("#mapViewsControlBox"));
		},
		//failure: function(errMsg) {
			//$onFailure(errMsg);
			//},
		error:  function(jqXHR, textStatus, errorThrown )
		{
			//onFailure(textStatus); //-- show error code returned
			console.error(errorThrown);
			console.error("Error loading feature types: " + textStatus + " " + errorThrown);
			//alert(errMsg);
		}
	});
}

function showView(map_view_id)
{
	console.log("show view " + map_view_id);
	
	var map_view = map_views[map_view_id];
	
	
	//var map_view_name = data[property].Name;
	var map_view_center_geometry_lat = map_view.center_geometry_coordinates[0];
	var map_view_center_geometry_lon = map_view.center_geometry_coordinates[1];
	var map_view_zoom_level = map_view.properties.zoom;
	var map_view_layer_index = map_view.properties.layer_index;

	var coordinates_espg3857 = ol.proj.transform([map_view_center_geometry_lon, map_view_center_geometry_lat], 'EPSG:4326', 'EPSG:3857');
		
	flyToCoordinates(coordinates_espg3857);
	
	//-- are the following also set in setMapLayerConfiguration?
	//var _map_layers = layers;
	//for (index = 0; index < _map_layers.length; index++)
	//	_map_layers[index].setVisible(index == map_view_layer_index);
	
	setMapLayerConfiguration(map_view.properties.map_configuration);
		
	map.getView().setZoom(map_view_zoom_level);
	
	setInitialCaveLayerSettings();
}

function refreshViewList()
{	
	initViews();
}

// end map view functionality
/////////////////////////////

function initGeocoderSearch()
{
	// https://github.com/jonataswalker/ol3-geocoder
	var geocoder = new Geocoder('nominatim', {
	  provider: 'photon', //'osm', //'google' //'photon', //'mapquest',
	  key: '__some_key__',
	  lang: 'en', //en-US, fr-FR
	  placeholder: 'Search for ...',
	  limit: 8+10,
	  keepOpen: true
	});
	map.addControl(geocoder);

	geocoder.on('addresschosen', function(evt){
	  var feature = evt.feature,
		  coord = evt.coordinate,
		  address = evt.address;

	  content.innerHTML = '<p>'+ address.formatted +'</p>';
	  overlay.setPosition(coord);
	});
	
/**
  * Popup
  **/
  var container = document.getElementById('search_popup'),
      content = document.getElementById('search-popup-content'),
      closer = document.getElementById('search-popup-closer'),
      overlay = new ol.Overlay({
        element: container,
        offset: [0, -40]
      });
  closer.onclick = function() {
    overlay.setPosition(undefined);
    closer.blur();
    return false;
  };
  map.addOverlay(overlay);	
}


function initCaveFilesTable()
{
	//$(document).ready(function() { //});
	$('#').DataTable({
		//"ajax": '../ajax/data/arrays.txt'
	});	
}

function initMapLayerSwitcherControl()
{
	// Add a layer switcher outside the map
	var external_switcher = new ol.control.LayerSwitcher(
		{	
			target:$(".layerSwitcher").get(0),
			show_progress:true,
			extent: true,
			trash: true,			
			oninfo: function (layer) { alert(layer.get("name")); }
		});
		
	map.addControl(external_switcher);
	
	// Insert mapbox layer in layer switcher
	
	if ($("#opb").prop("checked")) $('body').addClass('hideOpacity');
	if ($("#dils").prop("checked")) displayInLayerSwitcher(true);
	
}

	function displayInLayerSwitcher(b)
	{	
		//mapbox.set('displayInLayerSwitcher', b);
	}

//-- on layer reordering the configuration saving breaks because the layers are identified by index and the order changes
	
function getMapLayerConfiguration()
{
	var layers = map.getLayers();
	var layer_property_list = [];
	
	var index = 0;
	//for (index = 0; index < layers.length; index++)
	layers.forEach(function (layer) {
            //if (id == layer.get('id'))
			{
	{	
		var layer_properties = {
			layer_index: index, //-- this must be a more reliable identifier which doesn't change with the reordering
			visible: layer.getVisible(),			
			opacity: layer.getOpacity()
		};
		
		layer_property_list.push(layer_properties);
		
		index++;
	}
					
            }            
        });	
	
	return layer_property_list;	
}

function setMapLayerConfiguration(configuration)
{
	var layers = map.getLayers();
	//var layer_property_list = [];
	var layer_list = configuration;
	
	for (index = 0; index < layer_list.length; index++)
	{
		if (layer_list[index])
		{
			var _layer_index = layer_list[index].layer_index;
			
			if (layers.item(_layer_index))
			{
				// filter out helper layers so their functionality and state is not influenced by map view selecting
				if (layers == caveFeaturesLayer)
					continue;
					
				if (layers != caveFeaturesLayer)
				{
				layers.item(_layer_index).setVisible(layer_list[index].visible),			
				layers.item(_layer_index).setOpacity(layer_list[index].opacity)
				}
			}
		}
	}	
}

function initMapPermalinkControl()
{
		// Control
		var ctrl = new ol.control.Permalink(
			{	onclick: function(url) 
				{	
					document.location = "mailto:?subject=subject&body=" + encodeURIComponent(url);
				}
			});
		
		map.addControl(ctrl);

		// Handle user parameter
		var userParam = ctrl.getUrlParams();
		$("#user").val(decodeURIComponent (userParam['user'] || ""))
				.on ('change', function()
				{	userParam['user'] = encodeURIComponent (this.value);
					// Refresh url
					ctrl.changed();
				});	
}

function initCaveFilesTable()
{
	$('#cave_files_table').DataTable( {
        //"ajax": '../ajax/data/arrays.txt'
    } );
}


///////////////////////
// begin context menu
///////////////////////

function initContextMenu()
{
    elastic = function(t) {
      return Math.pow(2, -10 * t) * Math.sin((t - 0.075) * (2 * Math.PI) / 0.3) + 1;
    };
	
    center = function(obj){
      var pan = ol.animation.pan({
        duration: 1000,
        easing: elastic,
        source: map.getView().getCenter()
      });
      map.beforeRender(pan);
      map.getView().setCenter(obj.coordinate);
    };

	marker = function(obj){
      var coord4326 = ol.proj.transform(
            obj.coordinate, 'EPSG:3857', 'EPSG:4326'),
        template = 'Coordinate is ({x} | {y})',
        iconStyle = new ol.style.Style({
          image: new ol.style.Icon({
            scale: .6,
            src: url_marker
          }),
          text: new ol.style.Text({
            offsetY: 25,
            text: ol.coordinate.format(coord4326, template, 2),
            font: '15px Open Sans,sans-serif',
            fill: new ol.style.Fill({ color: '#111' }),
            stroke: new ol.style.Stroke({
              color: '#eee', width: 2
            })
          })
        }),	
        feature = new ol.Feature({
          type: 'removable',
          geometry: new ol.geom.Point(obj.coordinate)
        });
      
      feature.setStyle(iconStyle);
      vectorLayer.getSource().addFeature(feature);
    };
	
	
	var contextmenu = new ContextMenu({
	  width: 170,
	  default_items: true, //default_items are (for now) Zoom In/Zoom Out
	  items: [
		{
		  text: 'Center map here',
		  classname: 'some-style-class', // add some CSS rules
		  callback: center // `center` is your callback function
		},
		{
		  text: 'Add a Marker',
		  classname: 'some-style-class', // you can add this icon with a CSS class
										 // instead of `icon` property (see next line)
		  //icon: 'img/marker.png',  // this can be relative or absolute
		  callback: marker
		},
		'-' // this is a separator
	  ]
	});
	
	map.addControl(contextmenu);
	
	var restore = false;
	
	contextmenu.on('open', function(evt)
	{
		var feature = map.forEachFeatureAtPixel(evt.pixel, function(ft, l){
			return ft;
		});
	  
		var cave_items = [
		  //'-', // this is a separator
		  {
			text: 'Cave details',
			//icon: '',
			callback: function (data)
			{
				var cave_id = feature.getProperties().cave_id;
				openCaveDetailsForm(cave_id);
			}
		  },
		  {
			text: 'Edit cave entrance details',
			//icon: '',
			callback: function (data)
			{
				var cave_entrance_id = feature.getProperties().cave_entrance_id; //feature.getProperties().cave_id;
				newCaveEntrance(cave_entrance_id);
			}
		  }		  
		];
		
		var cave_feature_items = [
		  //'-', // this is a separator
		  {
			text: 'Edit cave feature',
			//icon: '',
			callback: function (data)
			{
				var feature_id = feature.getProperties().id;
				openNewCaveFeatureForm(feature_id, undefined, feature, undefined);
			}
		  },
		  {
			text: 'Delete cave feature',
			//icon: '',
			callback: function (data)
			{
				//var feature_id = feature.getProperties().feature_id; //feature.getProperties().cave_id;
				deleteFeature(feature);
			}
		  }
		  
		];		

		//contextmenu.extend(add_later);	  
	
	if (feature) {
		contextmenu.clear();
		
		if (getFeatureType(feature) == "cave_entrance")
		{
			contextmenu.extend(cave_items);
		//contextmenu.push(cave_item);
		
		/*removeMarkerItem.data = {
		  marker: feature
		};
		contextmenu.push(removeMarkerItem);
		*/
		}
		else if (getFeatureType(feature) == "cave_feature")
		{
			contextmenu.extend(cave_feature_items);
		}
		restore = true;
	  }
	  else if (restore) {
		contextmenu.clear();
		//contextmenu.extend(contextmenu_items);
		contextmenu.extend(contextmenu.getDefaultItems());
		restore = false;
	  }	  
	});
}

///////////////////////
// end context menu
///////////////////////

function getFeatureType(feature)
{
	return feature.getProperties().geoobject_type;
}

function showNotification(message, placement = undefined)
{
	// http://bootstrap-notify.remabledesigns.com/#documentation
	
	var default_placement = { from: "top", align: "right" };
	
	if (placement == undefined)
		placement = default_placement;
	
	var notify = $.notify({
		// options
		message: message
	},{
		// settings
		type: 'info',
		animate: {
			enter: 'animated fadeInDown',
			exit: 'animated fadeOutUp'
		},
		delay: 5000,
		timer: 1000,
		url_target: '_blank',
		placement: placement,
	});
}

function openUploadFilesForm(geoobject_id, geoobject_type) // cave_id, cave_entrance_id, feature_id
{			
	
		
	$('#upload_files_cave_id').val("");
	
	//$('#uploadFilesForm').off('submit');
	//$("#uploadFilesForm")[0].reset();
	
	
	$('#uploadFilesModalTitleLabel').text("Edit cave '" + "" +"'");
	
	$('#fileupload_target_type').val("cave");
/*	
	$('#uploadFilesForm').on('submit', function(e) {
          e.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postCave.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#caveModal').modal('toggle'); 
				last_added_cave_id = undefined; //-- need to return the cave_id from postCave.php or to load load the last added cave in fillCavePicker()
				
				showNotification("Cave <b>" + formData.cave_name + "</b> was saved.");
				reloadMapFeatures();
				//$("caveModal").modal('hide');
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
*/		
	//fillCaveEntries();
	
	if (geoobject_id)
	{
		$('#uploadFilesModal').modal();
		/*$.getJSON("data/getCave.php?cave_id=" + cave_id, function( data ) {
			
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
		*/
	}
}

function addTripFiles()
{
	var trip_log_id = $('#trip_log_id').val();
	openUploadTripFilesForm(trip_log_id);
}

function openUploadTripFilesForm(trip_log_id) // cave_id, cave_entrance_id, feature_id
{			
	$('#upload_files_cave_id').val("");	
	
	$('#uploadFilesModalTitleLabel').text("Edit '" + "" +"'");
	
	$('#fileupload_target_type').val("trip_report");
	$('#fileupload_target_object_id').val(trip_log_id);
/*	
	$('#uploadFilesForm').on('submit', function(e) {
          e.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postCave.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#caveModal').modal('toggle'); 
				last_added_cave_id = undefined; //-- need to return the cave_id from postCave.php or to load load the last added cave in fillCavePicker()
				
				showNotification("Cave <b>" + formData.cave_name + "</b> was saved.");
				reloadMapFeatures();
				//$("caveModal").modal('hide');
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
*/		
	//fillCaveEntries();
	
	if (trip_log_id)
	{
		$('#uploadFilesModal').modal();
		/*$.getJSON("data/getCave.php?cave_id=" + cave_id, function( data ) {
			
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
		*/
	}
}

function openUploadPicturesForm(geoobject_id, geoobject_type) // cave_id, cave_entrance_id, feature_id
{				
	$('#upload_xx_cave_id').val("");
	
	//$('#uploadFilesForm').off('submit');
	//$("#uploadFilesForm")[0].reset();
	
	if (geoobject_id)	
		$('#uploadPicturesModalTitleLabel').text("Edit '" + "" +"'");

/*	
	$('#uploadFilesForm').on('submit', function(e) {
          e.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postCave.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#caveModal').modal('toggle'); 
				last_added_cave_id = undefined; //-- need to return the cave_id from postCave.php or to load load the last added cave in fillCavePicker()
				
				showNotification("Cave <b>" + formData.cave_name + "</b> was saved.");
				reloadMapFeatures();
				//$("caveModal").modal('hide');
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
*/		
	//fillCaveEntries();
	
	//if (geoobject_id)
	{
		$('#uploadPicturesModal').modal();
		/*$.getJSON("data/getCave.php?cave_id=" + cave_id, function( data ) {
			
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
		*/
	}
}

function addPictures()
{
	openUploadPicturesForm(undefined, undefined);
}

function initPicturesUploadControl()
{
	/*$('#fileupload').fileupload({
		//formData: {example: 'test'}
		url: 'speogis/data/uploader'
	}).on('fileuploadsubmit', function (e, data) {
		data.formData = data.context.find(':input').serializeArray();
	});
	
	$('#fileupload').bind('fileuploadsubmit', function (e, data) {
		// The example input, doesn't have to be part of the upload form:
		var input = $('#input');
		data.formData = {example: input.val()};
		if (!data.formData.example) {
		  data.context.find('button').prop('disabled', false);
		  input.focus();
		  return false;
		}
	});
	*/
    //'use strict';

    // Initialize the jQuery File Upload widget:
  
  /*$('#pictureUploader').fileupload({
        // Uncomment the following to send cross-domain cookies:
        //xhrFields: {withCredentials: true},
        url: 'data/uploader/picture_uploader.php'
		//url: 'data/uploader'
    });
	*/
	
	/*
    // Enable iframe cross-domain access via redirect option:
    $('#fileupload').fileupload(
        'option',
        'redirect',
        window.location.href.replace(
            /\/[^\/]*$/,
            '/cors/result.html?%s'
        )
    );
	*/
	
	/*
    if (window.location.hostname === 'blueimp.github.io') {
        // Demo settings:
        $('#fileupload').fileupload('option', {
            url: 'speogis/data/uploader',
            // Enable image resizing, except for Android and Opera,
            // which actually support image resizing, but fail to
            // send Blob objects via XHR requests:
            disableImageResize: /Android(?!.*Chrome)|Opera/
                .test(window.navigator.userAgent),
            maxFileSize: 999000,
            acceptFileTypes: /(\.|\/)(gif|jpe?g|png)$/i
        });
        // Upload server status check for browsers with CORS support:
        if ($.support.cors) {
            $.ajax({
                url: '//jquery-file-upload.appspot.com/',
                type: 'HEAD'
            }).fail(function () {
                $('<div class="alert alert-danger"/>')
                    .text('Upload server currently unavailable - ' +
                            new Date())
                    .appendTo('#fileupload');
            });
        }
    } else 
	*/
	{
        // Load existing files:
		console.log('#pictureUploader');
		console.log($('#pictureUploader').fileupload('option', 'url'));
        //..$('#pictureUploader').addClass('fileupload-processing');
        $.ajax({
            // Uncomment the following to send cross-domain cookies:
            //xhrFields: {withCredentials: true},
            url: $('#pictureUploader').fileupload('option', 'url'),
            dataType: 'json',
            context: $('#pictureUploader')[0]
        }).always(function () {
            $(this).removeClass('fileupload-processing');
        }).done(function (result) {
            $(this).fileupload('option', 'done')
                .call(this, $.Event('done'), {result: result});
        });
    }	
	
	// $('#fileupload').fileupload('destroy');
}

///////////////////////
//  begin localization
//var selected_language = 'en'; // 'ro';
var selected_language = 'ro';

function _t()
{
	try
	{
		return localizedText[selected_language];
	}
	catch (ex)
	{
		return "_not_found_";
	}
}

function localize_static_html()
{
	//var regex = "/(<([^>]+)>)/ig";
	//var regex = "(?<=\{)(.*?)(?=\})";
	//var regex = /{(.*)}/
	
	var regex = /\*{\s*[\w\.]+\s*}\*/g
	// var regex = /{{\s*[\w\.]+\s*}}/g
	
	//var regex = /^^\s*[\w\.]+\s*^^/g
	//body = "<p>test</p>"

	// See more at: http://www.jsmantras.com/blog/String-Methods-search-match-and-replace#sthash.MPZVQvuX.dpuf
	function replacer(match, p1, p2, p3, offset, string)
	{ // p1 is nondigits, p2 digits, and p3 non-alphanumerics 
	
		res = match.match(/[\w\.]+/)[0];
		//console.log(res);
		
		var localized_text = eval("_t()." + res);
			
		if (localized_text == undefined)
			return "*not found*";
		//m_res[0].replace(regex, localized_text);
		//document.body.innerHTML.replace(m_res[0], "");
	
		return localized_text;
		//return [p1, p2, p3].join(' - '); 
	}; 
	
	//var m_res = document.body.innerHTML.match(regex);
	//var m_res = document.body.innerHTML.replace(regex, replacer);
	document.body.innerHTML = document.body.innerHTML.replace(regex, replacer);
	
/*	if (m_res)
	m_res.map(function(x) { 
		// return x.match(/[\w\.]+/)[0]; 
		res = x.match(/[\w\.]+/)[0];
		console.log(res);
		var localized_text = eval("_t()." + res);
		m_res[0].replace(regex, localized_text);
		document.body.innerHTML.replace(m_res[0], "");
		// return res; 
		});
	*/
	
	/*if (m_res != null)
	{
		var res = m_res[0];
		console.log(res);
		//res2 = res.replace('{', '').replace('}', '');
		console.log(eval("_t()." + res));
		m_res[0].replace(regex, '$1');
		
	}
	
	document.body.innerHTML = document.body.innerHTML.replace(regex, "");
	*/
}

//  end localization
///////////////////////

function init_export_map_as_image()
{
	var exportPNGElement = document.getElementById('export_map');
	
	if ('download' in exportPNGElement) 
	{
		exportPNGElement.addEventListener('click', function(e) {
			
			map.once('postcompose', function(event) {
			  var canvas = event.context.canvas;
			  
			  var export_data_url = canvas.toDataURL('image/png');
			  exportPNGElement.href = export_data_url;
			});
			
			map.renderSync();
		
		}, false);
	}
	else {
		var info = document.getElementById('no-download');

		info.style.display = '';
	}


/*
	map.once('postcompose', function(event) {
      var canvas = event.context.canvas;
      //exportPNGElement.href = 
	  var export_data_url = canvas.toDataURL('image/png');
	  //console.log(export_url);
	  
    });
    map.renderSync();
	*/
}

var exportMap = function () {
  canvas = document.getElementsByTagName('canvas')[0];
  canvas.toBlob(function (blob) {
    saveAs(blob, 'map.png');
  })
}

function initFeatureClusteringLayer()
{
var clusterSource = new ol.source.Cluster({
  distance: 40,
  source: _db_features_source
});

_db_features_source.forEachFeature(function(feature){
//console.log(feature, feature instanceof );
});

var styleCache = {};
var clusters = new ol.layer.Vector({
  source: clusterSource,
  style: function(feature, resolution) {
    var size = feature.get('features').length;
    var style = styleCache[size];
    if (!style) {
      style = [new ol.style.Style({
        image: new ol.style.Circle({
          radius: 10,
          stroke: new ol.style.Stroke({
            color: '#fff'
          }),
          fill: new ol.style.Fill({
            color: '#3399CC'
          })
        }),
        text: new ol.style.Text({
          text: size.toString(),
          fill: new ol.style.Fill({
            color: '#fff'
          })
        })
      })];
      styleCache[size] = style;
    }
    return style;
  }
});

map.addLayer(clusters);
}

function initUploadControls()
{
	 $('.fileupload').fileupload({
        // Uncomment the following to send cross-domain cookies:
        //xhrFields: {withCredentials: true},
        //url: 'data/uploader/'
		//url: 'data/uploader'
    });
}

//////////////////////////
// Begin Trip Report form

function addTripReport()
{
	openTripReportForm(undefined);
	
	//if (feature_id == undefined)
	//$('#tripReportModal').modal();
}

function openTripReportForm(trip_log_id) // cave_id, cave_entrance_id, feature_id
{			
	//$('#upload_xx_cave_id').val("");
	
	//$('#uploadFilesForm').off('submit');
	//$("#uploadFilesForm")[0].reset();
	
	if (trip_log_id)
		$('#tripReportModalTitleLabel').text("Edit '" + "" +"'");

	if (trip_log_id)
	{
		$.getJSON("data/getTripReport.php?trip_log_id=" + trip_log_id, function( data ) {
			
			//$('#tripReportModal').modal();
			
			$('#trip_log_id').val(data.Id);
			$('#tripreport_start_time').val(data.Tripstarttime);
			$('#tripreport_end_time').val(data.Tripendtime);				
			$('#tripreport_details').val(data.Details);
			$('#tripreport_place').val(data.Targetzone);
			//$('#tripreport_members').val(data.Members);
			
			//$('#tripreport_members').tagsinput('items');
			
			data.Members.forEach(function(team_member) {
				$('#tripreport_members').tagsinput('add', _team_members[team_member]);
			});
			
			$('#tripreport_members').tagsinput('refresh');
			//$('#').val(data.Feature_type_id);
									
			$('#tripReportModal').modal();
		});
	}
	else
	{
		$('#trip_log_id').val("");
		$('#tripreport_start_time').val("");
		$('#tripreport_end_time').val("");				
		$('#tripreport_details').val("");
		$('#tripreport_place').val("");
		$('#tripreport_members').val("");
		$('#tripreport_members').tagsinput('removeAll');
		
          var formData = $('#tripReportForm').serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  formData.temporary = 1;
		  
		  postDataAsync("data/postTripReport.php", formData, 
			function(response) 
			{ 
		$('#trip_log_id').val(response.Id);
		// $('#tripreport_start_time').val("");
		// $('#tripreport_end_time').val("");				
		// $('#tripreport_details').val("");
		// $('#tripreport_members').val("");
		// $('#tripreport_members').tagsinput('removeAll');
		
		$('#tripReportModal').modal();
			
				//console.log('close');
				//$('#tripReportModal').modal('toggle'); 
				
				showNotification("temp Trip report");
				//reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/ 
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
	
		// $.getJSON("data/getTripReport.php?trip_log_id=" + trip_log_id, function( data ) {				

		// // $('#trip_log_id').val("");
		// // $('#tripreport_start_time').val("");
		// // $('#tripreport_end_time').val("");				
		// // $('#tripreport_details').val("");
		// // $('#tripreport_members').val("");
		// // $('#tripreport_members').tagsinput('removeAll');
		
		// // $('#tripReportModal').modal();
		// });
		
	}
	
	if (trip_log_id)
	{
		refreshTripReportFilesTable(trip_log_id);
	
		/*$('#uploadFilesModal').on('hidden.bs.modal', function () {
			//-- this is not executed, maybe ovewritten by the other event
			refreshTripReportFilesTable(trip_log_id);
		});
		*/
	}
	
	$('#trip_report_files_table').DataTable().clear();
	$('#trip_report_files_table').DataTable().draw();	
}

function initTripReportForm()
{
	$('#trip_report_files_table').DataTable( {
        //"ajax": '../ajax/data/arrays.txt'
    } );

	$('#addFilesToTripReport').on('click', function(event) {
		event.preventDefault(); // To prevent following the link (optional)		
		addTripFiles();
		
		//onSaveCave(this);
		//$(this).submit();
	});
	
	$('#tripReportForm').on('submit', function(e) {
          e.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postTripReport.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#tripReportModal').modal('toggle'); 
				
				showNotification("Trip report <b>" + "" + "</b> was saved.");
				//reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/ 
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
		
	//fillCaveEntries();	
	
	$('#tripreport_start_time').datetimepicker({
		//defaultDate: "11/1/2013",
        /*disabledDates: [
							moment("12/25/2013"),
							new Date(2013, 11 - 1, 21),
							"11/22/2013 00:53"
						]
						*/
	});	

	$('#tripreport_end_time').datetimepicker({
		//defaultDate: "11/1/2013",
        /*disabledDates: [
							moment("12/25/2013"),
							new Date(2013, 11 - 1, 21),
							"11/22/2013 00:53"
						]*/
	});	

/*var tm2 = new Bloodhound({
  datumTokenizer: Bloodhound.tokenizers.obj.whitespace('name'),
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  prefetch: {
    url: 'data/getTeamMembers.php',
    filter: function(list) {
      return $.map(list, function(cityname) {
        return { name: cityname }; });
    }
  }
});
tm2.initialize();
*/
$.getJSON('data/getTeamMembers.php', function( data ) 
{	
var items = [];
_team_members = [];

$.each( data, function( key, val ) {
		
		//val.short_name = val.FirstName + " " + val.LastName;
		val.short_name = val.FirstName;
		
		if (val.LastName && val.LastName.length > 0)
			val.short_name += " " + val.LastName.substr(0, 1) + ".";
			
		items.push(val);
		_team_members[val.Id] = val;
	});
	
	
var teamMembers = new Bloodhound({
  datumTokenizer: function (item) 
  {
	//return [item.FirstName + " " + item.LastName];
	return [item.FirstName, item.LastName, item.FirstName + " " + item.LastName, item.short_name];
  }, //Bloodhound.tokenizers.obj.whitespace('FirstName'),
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  //queryTokenizer: Bloodhound.tokenizers.obj.whitespace('FirstName'),
  //prefetch: 'data/getTeamMembers.php',
  
  // prefetch: {
    // url: 'data/getTeamMembers.php',
    // /*filter: function(list) {
      // return $.map(list, function(cityname) {
        // return { name: cityname }; });
    // }*/
	// },
  identify: function(obj) { console.log("identify" + obj); return obj.Id; },
  dupDetector: function(a, b) { return a.Id === b.Id; },
  //prefetch: 'data/getSearchFeatures.php',
  /*remote: {
    url: 'data/getTeamMembers.php',
    //wildcard: '%QUERY'
  },*/
  transform: function(data)
  {
	console.log('p: ' + data);
  },
  local: items
  	/*local: function(query) {
		var items = [];
      $.getJSON('data/getTeamMembers.php', function( data ) {
	  
	$.each( data, function( key, val ) {
		items.push(val);
	});
	
	
	});
	return items;
    }
	*/
});

teamMembers.initialize();

/**
 * Typeahead
 */
/*var elt = $('.example_typeahead > > input');
elt.tagsinput({
  typeaheadjs: {
    name: 'citynames',
    displayKey: 'name',
    valueKey: 'name',
    source: tm2.ttAdapter()
  }
});
*/

/**
 * Objects as tags
 */
elt = $('#tripreport_members');
elt.tagsinput({
  itemValue: 'Id',
  //itemText: 'FirstName',
  itemText: 'short_name',
  typeaheadjs: {
/*
  hint: $('.Typeahead-hint'),
  menu: $('.Typeahead-menu'),
  //hint: true,
  highlight: true,
  minLength: 1,
  classNames: {
      open: 'is-open',
      empty: 'is-empty',
      cursor: 'is-active',
      suggestion: 'Typeahead-suggestion',
      selectable: 'Typeahead-selectable'
    },
*/  
    name: 'teamMembers',
    //displayKey: 'FirstName',
	displayKey: 'short_name',
	confirmKeys: [13, 44, 188],
    source: teamMembers.ttAdapter(),
	//limit: 9
    /*source: function(query) {
      return $.getJSON('data/getTeamMembers.php');
    }*/	
  }
});

//elt.tagsinput('add', { "Id": 21 , "FirstName": "Amsterdam" });
//elt.tagsinput('add', { "Id": 32 , "FirstName": "xx" });

// elt.tagsinput('add', { "value": 1 , "text": "Amsterdam"   , "continent": "Europe"    });
// elt.tagsinput('add', { "value": 2 , "text": "xx"   , "continent": "zz"    });

	
	// $('#membersTagsControl').tagsinput({
	/*
	$('#tripreport_members').tagsinput({
	  itemValue: 'Id',
	  itemText: 'FirstName',
	  typeahead: {
		source: function(query) {
		  return $.getJSON('data/getTeamMembers.php');
		}
	  }
	});	
	
var cities = new Bloodhound({
  datumTokenizer: Bloodhound.tokenizers.obj.whitespace('text'),
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  prefetch: 'assets/cities.json'
});
cities.initialize();

var elt = $('input');
elt.tagsinput({
  itemValue: 'value',
  itemText: 'text',
  typeaheadjs: {
    name: 'cities',
    displayKey: 'text',
    source: cities.ttAdapter()
  }
});
elt.tagsinput('add', { "value": 1 , "text": "Amsterdam"   , "continent": "Europe"    });
elt.tagsinput('add', { "value": 4 , "text": "Washington"  , "continent": "America"   });
elt.tagsinput('add', { "value": 7 , "text": "Sydney"      , "continent": "Australia" });
elt.tagsinput('add', { "value": 10, "text": "Beijing"     , "continent": "Asia"      });
elt.tagsinput('add', { "value": 13, "text": "Cairo"       , "continent": "Africa"    });	
*/

// HACK: overrule hardcoded display inline-block of typeahead.js
	$(".twitter-typeahead").css('display', 'inline');
	
    });
	
	$('.modal-child').on('show.bs.modal', function () {
        var modalParent = $(this).attr('data-modal-parent');
        //$(modalParent).css('opacity', 0);
		$(modalParent).hide();
    });
	
    $('.modal-child').on('hidden.bs.modal', function () {
        var modalParent = $(this).attr('data-modal-parent');
        //$(modalParent).css('opacity', 1);
		$(modalParent).show();
		
		var trip_log_id = $('#trip_log_id').val();
		refreshTripReportFilesTable(trip_log_id);
    });	
}

function openAddFilesToTripReportForm()
{
	openUploadFilesForm(undefined, undefined);
}

function refreshTripReportFilesTable(trip_log_id)
{
	var form_data = JSON.stringify( { trip_log_id: trip_log_id } );
	
    $.ajax({
                url: 'data/getTripReportFiles.php?trip_log_id=' + trip_log_id, // point to server-side PHP script 
                dataType: 'json', // dataType: 'jsonp', //dataType: 'text',  
                cache: false,
                contentType: false,
                processData: false,
                data: form_data,
                type: 'post',
                success: function(data){
							var dataSet = [];

							var file_directory = 'data/uploader/files/';
							
							data.forEach(function(item) {
								var file_url = file_directory + item.file_name;
								var file_name_html = "<a href='" + file_url + "' target='_blank' >" + item.file_name + "</a>";
								dataSet.push([file_name_html, item.size, item.add_time, 0/*item.object_type*/]);
							});
							  
							/*$('#cave_files_table').DataTable({
								//"ajax": '../ajax/data/arrays.txt'
								data: dataSet,
							});*/
							$('#trip_report_files_table').DataTable().clear();
							$('#trip_report_files_table').DataTable()
								.rows.add(dataSet)
								.draw();
                },
				error:  function(jqXHR, textStatus, errorThrown )
				{
					//onFailure(textStatus); //-- show error code returned
					console.warn(errorThrown);
					console.warn("Error loading trip report files: " + textStatus + " " + errorThrown);
					//console.error(errorThrown);
					//console.error("Error loading trip report files: " + textStatus + " " + errorThrown);
					//alert(errMsg);
				}				
     });	
}
// End Trip Report form
///////////////////////

function initTripFeatureSearchControl()
{
  var engine, remoteHost, template, empty;
  
  remoteHost = '';
  template = Handlebars.compile($("#result-template").html());
  empty = Handlebars.compile($("#empty-template").html());
  
var featuresDataSource = new Bloodhound({
  datumTokenizer: Bloodhound.tokenizers.obj.whitespace('name'),  
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  //datumTokenizer: Bloodhound.tokenizers.obj.whitespace('name', 'name'),  
  
  identify: function(obj) { console.log("identify" + obj); return obj.id; },
  dupDetector: function(a, b) { return a.id === b.id; },
  prefetch: 'data/getSearchFeatures.php',
  remote: {
    url: remoteHost + 'data/getSearchFeatures.php?q=%QUERY',
    wildcard: '%QUERY'
  },
  transform: function(data)
  {
	console.log('p: ' + data);
  }
});

featuresDataSource.initialize();

$('#tripreport_place').typeahead({
 //$('#searchFeatureControl .typeahead').typeahead({
  hint: $('.Typeahead-hint_2'),
  menu: $('.Typeahead-menu_2'),
  //hint: true,
  highlight: true,
  minLength: 1,
  classNames: {
      open: 'is-open',
      empty: 'is-empty',
      cursor: 'is-active',
      suggestion: 'Typeahead-suggestion',
      selectable: 'Typeahead-selectable'
    },
  //display: 'name',  
  //limit: 9
  //suggestion: Handlebars.compile('<div><strong>{{value}}</strong> – {{year}}</div>')
  /*
  filter: function (parsedResponse) {
            // parsedResponse is the array returned from your backend
            console.log(parsedResponse);

            // do whatever processing you need here
            return parsedResponse;
        }
		*/
},
{
  name: 'features',
  source: featuresDataSource, //substringMatcher(states)
  displayKey: 'name',
    templates: {
      suggestion: template,
      empty: empty,
	  //header: '<h3 class="league-name">Teams</h3>'
    },
})
/*.on('typeahead:asyncrequest', function() {
    $('.Typeahead-spinner').show();
  })
  .on('typeahead:asynccancel typeahead:asyncreceive', function() {
    $('.Typeahead-spinner').hide();
  })*/;	

$('#tripreport_place').bind('typeahead:select', function(ev, suggestion) {
  //console.log('Selection: ');
  console.log(suggestion);
  
  //gotoDbElement(suggestion); // id
  // gotoMapElement(suggestion.point_db_id); // id
});
	}
	
function initPictureThumbLayer()
{
	initPictureThumbnailLightSlider();
	// Style
	var styleCache={};
	function getFeatureStyle (feature, resolution, sel)
	{	
		var features = feature.get("features");
			
		if (features)
		{
		var f = features[0];
		var nb = feature.get("features").length;
		var th = f.get("thumbnail");
		var k = th.replace(/(.*)\/(.*)\?(.*)/,"$2")+(nb>1?"_0":"_1")+(sel?"_1":"");
		var style = styleCache[k];
		if (!style)
		{	styleCache[k] = style = new ol.style.Style
			({	image: new ol.style.Photo (
				{	src: th,
					radius: 20,
					crop: true,
					kind: (nb>1) ? "folio":"square",
					shadow: true,
					onload: function() { vector.changed(); },
					stroke: new ol.style.Stroke(
					{	width: sel ? 5 : 3,
						color: sel ? 'red' : '#fff'
					})
				})
			});
		}
		if (nb>1)
		{	var count = new ol.style.Style(
				{	image: new ol.style.RegularShape(
					{	points: 12,
						radius: 9,
						fill: new ol.style.Fill({
								color: '#fff'
							})
					}),
					text: new ol.style.Text(
					{	text: nb.toString(),
						font: 'bold 11px helvetica,sans-serif',
						offsetX: 20,
						offsetY: -20,
                        fill: new ol.style.Fill({
                            color: '#000'
                        })
					})
				});
			var p = count.getImage().getAnchor();
			p[0]-=20;
			p[1]+=20;
			return [ style, count ];
		}
		else return [style];
	}
	//-- else return ?
	}
	console.log("_map_pictures = [];");
	_map_pictures = [];
	// DBPedia layer source
	var vectorSource = new ol.source.DBPedia(
	{	// Tile strategy load at zoom 14
        //strategy: ol.loadingstrategy.tile(ol.tilegrid.createXYZ({ minZoom: 14, maxZoom: 14, tileSize:512  })),
		// Bbox strategy : reload at each move
		strategy: ol.loadingstrategy.bbox,
		//maxResolution: 10, // > zoom 14
		// Language
		lang:"en",
		onPicturesLoad: function (pictures) {
			//$.each(pictures, function(key, value) {
			//console.log("onPicturesLoad clearSlides();");
			clearOutOfViewSlides();
			
			pictures.forEach(function(feature) {
				//console.log("pictures.forEach(function(feature) {");
				//console.log(feature);
				var image_id = feature.getProperties()["image_id"]; 
				_map_pictures[image_id] = feature.getProperties();
				// feature.getProperties()["thumbnail"]
				
				addSlide(image_id);
			});
			//console.log(pictures);			
			_pictureThumbnailLightSlider.refresh();
			//console.log("_pictureThumbnailLightSlider.refresh();");
			
			setTimeout( function() 
			{ 
				$('body').layout().resizeAll(); //--
			}, 1500);
			
		}
	});

	// Force thumbnail to non optional
	/*vectorSource.querySubject = function ()
	{	return "?subject rdfs:label ?label. "
			+ "?subject dbpedia-owl:thumbnail ?thumbnail."
			+ "OPTIONAL {?subject dbpedia-owl:abstract ?abstract} . "
			+ "OPTIONAL {?subject rdf:type ?type}";
	}
*/
	// Force layer reload on resolution change 
	map.getView().on('change:resolution', function(evt)
	{	//vectorSource.clear();
		if (map.getView().getZoom() < 14) $(".options").first().text("Zoom to load data...");
		else $(".options").first().text("");
	});

	var clusterSource = new ol.source.Cluster({
		distance: 60,
		source: vectorSource
	});

	var vector = new ol.layer.AnimatedCluster(
	{	name: 'Pictures layer',
		source: clusterSource,
		// Limit resolution to avoid large area request
		maxResolution: 40, // > zoom 12
		// y ordering
		renderOrder: ol.ordering.yOrdering(),
		// default dbPedia style function
		style: getFeatureStyle
	});

	map.addLayer(vector);

	// Control Select 
	var select = new ol.interaction.Select(
		{	// select dbPedia style function
			style: function(f,res){return getFeatureStyle(f,res,true);}
        })
	map.addInteraction(select);
	
	// On selected
	select.getFeatures().on(['add','remove'], function(e)
	{	var info = $("#select").html("");
		if (e.type=="add") 
		{	
			var features = e.element.get("features");
			
			if (features)
			{
				var el = features[0];
				
				/*$("<h2>").text(el.get("label")).appendTo(info);
				
				if (el.get("thumbnail")) 
					$("<img>").attr('src',el.get("thumbnail")).appendTo(info);
					
				$("<p>").text(el.get("abstract")).appendTo(info);
				*/
				if (el.get("url"))
					showPicture(el.get("url"), "", "");
			}
		}
	});
	
}

function showPicture(url, title, footer)
{
			//var url = clickedFeature.getProperties().url;
			//var description = clickedFeature.getProperties().description;
			//var pictureName = clickedFeature.getProperties().file_path;

				//var url = '.' + url;
				
				$("#thumbPictureHolder").attr("href", url);
				$("#thumbPictureHolder").attr("data-footer", "");
				$("#thumbPictureHolder").attr("data-title", "");
								
				$("#thumbPictureHolder").ekkoLightbox(
				//{ remote: url }
				);	
}

function initPictureThumbnailLightSlider()
{
	// http://sachinchoolur.github.io/lightslider/
	// http://sachinchoolur.github.io/lightslider/examples.html
	
	var slider = $('#mapPicturesLightSlider').lightSlider({
		//gallery: true,
		item: 5,//8,
		loop: true,
		//slideMargin: 0,
		thumbItem: 20,
		//verticalHeight:50,
		autoWidth:false,
		slideMargin: 10,
		//controls: true,
		thumbMargin: 10,
	});
	
	_pictureThumbnailLightSlider = slider;

	slider.refresh();
}

function addSlide(image_id) // url
{ 
	//if (_map_pictures[image_id])
	{
	var image_thumb_url = _map_pictures[image_id]["thumbnail"];
	//var image_id = _map_pictures[image_id]["image_id"];
	//var image_url = _map_pictures[image_id]["url"];
	
	// Class 'lslide' needs to be added with new slide item
	// var newEl = "<li class='lslide'> <a href='javascript:void(0)'><img src='" + url + "' /></a> </li>";
	var newEl = "<li class='lslide'> <a onclick=\"selectThumbPicture(" + image_id + ")\"><img src='" + image_thumb_url + "' /></a> </li>";
	_pictureThumbnailLightSlider.prepend(newEl);
	}
}

//-- this might get triggered multiple times
function clearOutOfViewSlides()
{
	//_map_pictures = _map_pictures.filter(pic => ol.extent.containsExtent(map.getView().calculateExtent(map.getSize()), pic.geometry.getExtent()));
	
	//-- _pictureThumbnailLightSlider should have a remove slide by identifier/index function to remove only not in view slides-pictures insted of removing all and readding the remaining ones
	_pictureThumbnailLightSlider.empty();
	
	_map_pictures.forEach(function(pic, index, object){
		if (!ol.extent.containsExtent(map.getView().calculateExtent(map.getSize()), pic.geometry.getExtent()))
		{
			//_map_pictures.
			object.slice(index, 1);
			//console.log("object.splice " + index);
		}
		else
		{
			//console.log("addSlide " + pic.image_id);
			addSlide(pic.image_id);			
		}
	});	
}

function selectThumbPicture(image_id)
{
	//_map_pictures[image_id] = 
	
	showPicture(_map_pictures[image_id]["url"], "", "");
}

var caveGalleryLinesStyleFunction = function(feature) {
	 var styles = [
          // linestring
          new ol.style.Style({
            stroke: new ol.style.Stroke({
              color: '#888888',
              width: 5
            })
          })
        ];
		var index = 0;
		
	 var geometry = feature.getGeometry();
	 //~~ console.log(geometry.getType());
	 
	 //if (geometry.getType() == "LineString") // MultiLineString
	 if (geometry.getType() == "MultiLineString")
	 geometry.getLineStrings().forEach(function(ls)
		{			
			//console.log(ls);
		//ls = geometry;
	//if (false)
	 ls.forEachSegment(function(start, end) {
          //var dx = end[0] - start[0];
          //var dy = end[1] - start[1];
          //var rotation = Math.atan2(dy, dx);
		  
		  var isInside = false;
		  
		  //var selectedCaveFeatures = caveFeaturesDrawSource.getFeatures();
		  var selectedCaveFeatures = hoverFeatureOverlay.getSource().getFeatures();
		  
		  if (selectedCaveFeatures.length > 0) // if (selectedCaveFeatures.getArray().length > 0)
		  {
			var startPoint = turf.point(start);
			var endPoint = turf.point(end);
		  	
			selectedCaveFeatures.forEach(function (feature)
			{
				//-- should test features if they have associated geometries which may not be the case if in the database there is no spatial row associated (point/line/polygon)
				if (feature.getGeometry().getType() == "Polygon")
				{
					var poly = turf.polygon(feature.getGeometry().getCoordinates());
					//[[[-81, 41],  [-81, 47],  [-72, 47],  [-72, 41],  [-81, 41]]] 			
					isInside |= turf.inside(startPoint, poly) || turf.inside(endPoint, poly);
				}
				else
					; //-- must be handled
			});			
		  }
		
          // arrows
          styles.push(new ol.style.Style({
            //geometry: new ol.geom.Point(end),
			geometry: new ol.geom.LineString([start, end]),
            /*image: new ol.style.Icon({
              src: 'http://openlayers.org/en/v3.20.1/examples/data/arrow.png',
              anchor: [0.75, 0.5],
              rotateWithView: true,
              rotation: -rotation
            }),*/
			stroke: new ol.style.Stroke({
				//color: isInside ? "red" : "darkbrown", // getRandomColor()
				color: isInside ? "#5599FF" : "#991122", // getRandomColor()
				width: isInside ? 5 : 2,
			}),
			/*text: new ol.style.Text({
				text: "" + (index++),
				scale: 1.3,
				fill: new ol.style.Fill({
				color: '#000000'
				}),
			})
			*/
          }));
	});
	}
	);
	
		return styles;
	}
	
	function getRandomColor() {
    var letters = '0123456789ABCDEF';
    var color = '#';
    for (var i = 0; i < 6; i++ ) {
        color += letters[Math.floor(Math.random() * 16)];
    }
	
    return color;
}

function initCaveFeaturesEditor()
{
	var selectEuropa = new ol.style.Style({
          stroke: new ol.style.Stroke({
            color: '#f29109',
            width: 8
        })
      });			
	
	var selectInteraction = new ol.interaction.Select({
		condition: ol.events.condition.mouseMove,
		//condition: ol.events.condition.click
        layers: function(layer) {
			console.log("selectInteraction function(layer)");
          //return true;
		  return layer.get('selectable') == true;
        },
        style: [/*selectFrancePoints, */selectEuropa],		
      });
_selectInteraction = selectInteraction;

map.getInteractions().extend([selectInteraction]);

target_layer.set('selectable', true);

selectInteraction.on('select', 
function (features)
		{
			// _selectInteraction.getFeatures().forEach(function (feature, index, ar) { console.log(feature); console.log(feature.getGeometry().getCoordinates());})
			features.selected.forEach(function (feature, index, ar) 
				{
					//console.log(feature); 
					//console.log(feature.getGeometry().getCoordinates());
					//showParts(feature);
				});
			
			//console.log(features);
		}/*, this(*/);
}

var caveFeatureDrawInteraction = undefined;
var caveFeatureDrawType = undefined;
var selectedCaveFeatures = new ol.Collection();
//var selectedCaveFeatureSource = undefined;
var caveFeaturesDrawSource = undefined;
var caveFeaturesLayer = undefined;

function enableCaveFeatureEditing()
{
		user_mouse_interaction_type = POINTER_DRAW_GENERIC;
		draw_feature_type = "Polygon";
		caveFeatureDrawType = draw_feature_type;
		
		map.removeInteraction(drawInteraction);
		map.removeInteraction(modify);

		/*if (caveFeaturesDrawSource == undefined)
		{
			caveFeaturesDrawSource = new ol.source.Vector();
			caveFeaturesLayer = new ol.layer.Vector(
			{
				source: caveFeaturesDrawSource,
				name: 'cave features drawing',				  
				style: _feature_styleFunction
			});
		}
		
		map.addLayer(caveFeaturesLayer);
		*/		
		startCaveFeaturesEditing();
		/*
		//addFeatureInteraction(featureType);	
      drawModifyInt = new ol.interaction.Modify({
        features: selectedCaveFeatures,//selectInteraction.getFeatures()
		//caveFeaturesDrawSource.getFeatures()
		source: caveFeaturesDrawSource,
		deleteCondition: function(event) {
    return ol.events.condition.shiftKeyOnly(event) &&
        ol.events.condition.singleClick(event);
		}
      });	  

	map.addInteraction(drawModifyInt);
	//map.getInteractions().extend([selectInteraction, modify]);	  		  		  
		  
			drawInt = new ol.interaction.Draw({
				features: selectedCaveFeatures,
				source: caveFeaturesDrawSource,
			  
			  //features: featureOverlay.getFeatures(),			  
			  type: caveFeatureDrawType, // @type {ol.geom.GeometryType}
			  //condition: ol.events.condition.singleClick,
			  //freehandCondition: ol.events.condition.noModifierKeys,
			  //freehandCondition: ol.events.condition.always,
			  //condition: ol.events.condition.never,
			  drawend: function ()
			  {				
				console.log("draw end");
			  }
			});
		
		function onDrawEnd() {
				console.log("drawend");
				_geo_file_layer.changed();
				
				// features.forEach(function (feature) 				{ 					feature.changed();				});
				// ol.Observable.unByKey(listener);
            };
			
        drawInt.on('drawend', onDrawEnd
            , this);
		
		drawModifyInt.on('drawend', onDrawEnd
            , this);
			
				map.addInteraction(drawInt);
			
		caveFeatureDrawInteraction = drawInt;		
		*/
		$("#caveFeaturesEditingCheckBox").prop('checked', caveFeaturesLayer.getVisible());
}


function newCaveFeature(feature_id = undefined, coordinates, existingSelectedFeature, new_feature_type)
{
	openNewCaveFeatureForm(feature_id, coordinates, existingSelectedFeature, new_feature_type);
	
	if (feature_id == undefined)
		$('#caveFeatureModal').modal();
}

// expecting coordinates type [] 'EPSG:3857'
function openNewCaveFeatureForm(feature_id, coordinates, existingSelectedFeature, new_feature_type)
{
	editMode = false;
	
	if (feature_id)
		editMode = true;
	
	
	//$('#saveCave').off('click');		
	$('#caveFeatureForm').off('submit');
	
	//$("#caveForm").find("input, input[type=text], textarea").val("");
	$('#cave_feature_id').val("");
	//$('#cave_feature_coords_lon').val("");
	//$('#cave_feature_coords_lat').val("");
	
	//-- workaround to avoid selecting an existing multipoint feature on adding a new onerror	
	
	/* ??
	var added_point = new ol.Feature({
		//name: "point_feature_xx",
		geometry: new ol.geom.Point(_current_coordinate)
	});
	
	_draw_source.addFeature(added_point);
	
	existingSelectedFeature = added_point;
	*/
	$('#cave_feature_string').val(getFeatureGeoJsonString(existingSelectedFeature));
	
	//existingSelectedFeature.set('feature_type_id');
	
	var featureType = undefined;
	
	if (new_feature_type)
		featureType = featureTypes[new_feature_type];
	else
		featureType = featureTypes[existingSelectedFeature.getProperties().feature_type_id];
	
	existingSelectedFeature.set('feature_type_id', featureType.Id);
	existingSelectedFeature.set("geoobject_type", "cave_feature"); // set for recognising feature type in the map interface for not posted features.
	//	existingSelectedFeature.setProperties({'feature_type_id': featureType});
	//console.log($('#feature_string').val());
	
	$("#caveFeatureForm")[0].reset();
	
	$('#cave_feature_type_id').val(featureType.Id);
	$('#cave_feature_existing_point_id').val("");
	
	
	selFeatureProps = undefined;
	
	if (existingSelectedFeature)
	{
		selFeatureProps = existingSelectedFeature.getProperties();
		$('#cave_feature_existing_point_id').val(selFeatureProps.id);
	}
	
	var coord_type = typeof coordinates;
	var is_point_feature = !(coord_type == 'object' || coord_type == 'array');
	
	if (coordinates && is_point_feature)
	{
		//espg coordinates
		
		var coordinates_espg4326 = ol.proj.transform(coordinates, 'EPSG:3857', 'EPSG:4326');
		
		$('#cave_feature_coords_lat').val(coordinates_espg4326[1]);
		$('#cave_feature_coords_lon').val(coordinates_espg4326[0]);
		$('#cave_feature_coords_label').text(rtrim(coordinates_espg4326[1]+"", 8) + ",  " + rtrim(coordinates_espg4326[0]+"", 8) + ((selFeatureProps != undefined) ? (" : " + selFeatureProps.gpx_name) : ""));		
	}	
		
	if (editMode)
		$('#caveFeatureModalTitleLabel').text("Edit cave feature");
	else
	{
		$('#caveFeatureModalTitleLabel').text("New " + featureType.Name);		
		$('#cave_feature_type_id').val(new_feature_type);
		
		
		feature_type_symbol_path = featureType.SymbolPath;
		
		if (feature_type_symbol_path && feature_type_symbol_path != "null")
		{
			feature_type_symbol_path = symbols_path + feature_type_symbol_path;
			$('#caveFeatureModalTitleLabel').prepend("<img src='" + feature_type_symbol_path + "' height='24'/>");
		}
	}
	
	/*	
	$('#saveCave').on('click', function(e) {
		//e.preventDefault(); // To prevent following the link (optional)		
		//onSaveCave(this);
		//$(this).submit();
	});
	*/
	$('#caveFeatureForm').on('submit', function(e) {
          e.preventDefault();

          //var formData = $(this).serializeObject();
		  
		var formData = 
		{
			feature_description: $("#cave_feature_description").val(),
			feature_existing_point_id: $("#cave_feature_existing_point_id").val(),
			feature_id: $("#cave_feature_id").val(),
			feature_name: $("#cave_feature_name").val(),
			feature_string: $("#cave_feature_string").val(),
			feature_type_id: $("#cave_feature_type_id").val(),
			//feature_group_type: "cave_feature"
			//feature_coords_label
			//feature_coords_lon
			//feature_coords_lat
		}
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postFeature.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#caveFeatureModal').modal('toggle'); 
				
				showNotification("Cave feature <b>" + formData.feature_name + "</b> was saved.");
				reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/ 
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
		
	//fillCaveEntries();
	
	if (feature_id)
	{
		$.getJSON("data/getFeature.php?feature_id=" + feature_id, function( data ) {
			
			$('#caveFeatureModal').modal();
			
			$('#cave_feature_id').val(data.Id);
			$('#cave_feature_name').val(data.Name);				
			$('#cave_feature_description').val(data.Description);
			$('#cave_feature_type_id').val(data.FeatureTypeId);
			
			//_caveFormServerData = data;
			//$('#featureModal').modal();
		});
	}	
}

	//-- the call to getDBGeoData.php should be made in loadFeatures() as well and filtered per each layer afterwards rather than a separate call
	function loadCaveFeatures()
	{
		//if (selectedCaveFeatureSource)
		//	selectedCaveFeatureSource = undefined;

		if (caveFeaturesDrawSource)
			caveFeaturesDrawSource = undefined;
			
		if (caveFeaturesLayer)
		{
			map.removeLayer(caveFeaturesLayer);
			caveFeaturesLayer = undefined;
		}
		
		//if (caveFeaturesDrawSource == undefined)
		
		var caveFeatureSource = new ol.source.Vector({
		  url: 'data/getDBGeoData.php'+'?type=cave_feature',
		  name: 'cave features drawing',
		  format: new ol.format.GeoJSON({
			 defaultDataProjection : 'EPSG:4326'//,
			 //ignoreExtraDims: true		 		
			}),
			style: _feature_styleFunction
			//features: (new ol.format.GeoJSON()).readFeatures(geojsonObject)
			//features: new ol.format.GeoJSON().readFeatures(geojsonObject,{ featureProjection: 'EPSG:3857' })
		});

		//-- cave selection not implemented, so it will get all features from all caves in the current view
		//selectedCaveFeatureSource = caveFeatureSource;
		caveFeaturesDrawSource = caveFeatureSource;
		
		caveFeaturesLayer = new ol.layer.Vector(
			{
				source: caveFeaturesDrawSource,
				name: 'cave features drawing',
				style: _feature_styleFunction,
				//visible: false
			});
	
	  setInitialCaveLayerSettings();
	  
	  map.addLayer(caveFeaturesLayer);
	
	  //map.addLayer(caveFeaturesLayer, false);
	  //caveFeaturesLayer.setVisible(false);	  	  
}

function setInitialCaveLayerSettings()
{
	  caveFeaturesLayer.setVisible(true);
	  caveFeaturesLayer.setOpacity(0.14);
}

function endCaveFeatureEditing()
{
	caveFeaturesLayer.setVisible(false);
	caveFeaturesLayer.setOpacity(0.1);

	//$('#drawFeaturesTreeControl').jstree('open_node', 'cave_feature');
	$('#drawFeaturesTreeControl').jstree('show_node', 'surface_feature');
}

function startCaveFeaturesEditing()
{
	console.log("startCaveFeaturesEditing()");
	
	caveFeaturesLayer.setVisible(true);
	caveFeaturesLayer.setOpacity(0.9);

	$('#drawFeaturesTreeControl').jstree('close_node', 'surface_feature');	
	$('#drawFeaturesTreeControl').jstree('open_node', 'cave_feature');
}

function initCaveFeaturesCheckBox()
{
		$("#caveFeaturesEditingCheckBox").change(
			function() {		
				if ($(this).is(":checked"))
					//startCaveFeaturesEditing();
					enableCaveFeatureEditing();
				else
					endCaveFeatureEditing();
			}
		);	
}


function initDrawFeaturesTreeControl(drawFeaturesTreeData)
{
	/*
		https://www.jstree.com/
		https://www.jstree.com/docs/config/
		https://www.jstree.com/docs/html/
		https://www.jstree.com/docs/json/
		https://www.jstree.com/docs/events/
		https://www.jstree.com/docs/interaction/
		https://www.jstree.com/demo/
		https://www.jstree.com/api/
		https://www.jstree.com/plugins/
	*/
	var drawFeaturesTreeControl = $('#drawFeaturesTreeControl');
	
	drawFeaturesTreeControl.jstree({
		core: {
			data : drawFeaturesTreeData,
			//stripes : false
			},
		plugins : [ "search", /*"state",*/ "wholerow" ]
	});
	
	drawFeaturesTreeControl.jstree('hide_stripes');
	/*.on("create_node", function (node, parent, position) {
      console.log("node " + create_node);
    });*/
	
	drawFeaturesTreeControl.on("changed.jstree", function (e, data) {
		if (data.node.original.feature_type_id)
			enableDrawNewFeature(data.node.original.feature_type_id);
	  //console.log(data.selected);
    });	
	
    // 8 interact with the tree - either way is OK
    /*$('button').on('click', function () {
      $('#jstree').jstree(true).select_node('child_node_1');
      $('#jstree').jstree('select_node', 'child_node_1');
      $.jstree.reference('#jstree').select_node('child_node_1');
    });*/
}

function _test_hoverGeometryStyle(feature)
{
    var
        style = [],
        geometry_type = feature.getGeometry().getType(),
        white = [255, 255, 255, 1],
        blue = [0, 153, 255, 1],
        width = 3;
        
    style['LineString'] = [
        new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: white, width: width + 2
            })
        }),
        new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: blue, width: width
            })
        })
    ],
    style['Polygon'] = [
        new ol.style.Style({
            fill: new ol.style.Fill({ color: [255, 255, 255, 0.5] })
        }),
        new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: white, width: 3.5
            })
        }),
        new ol.style.Style({
            stroke: new ol.style.Stroke({
                color: blue, width: 2.5
            })
        })
    ],
    style['Point'] = [
        new ol.style.Style({
            image: new ol.style.Circle({
                radius: width * 2,
                fill: new ol.style.Fill({color: blue}),
                stroke: new ol.style.Stroke({
                    color: white, width: width / 2
                })
            })
        })
    ];
    
    return style[geometry_type];
}

//var highlight;
var highlightedFeatures = [];
var highlightStyleCache = {};
var hoverFeatureOverlay;

function initCaveFeaturesHighlighting()
{
      hoverFeatureOverlay = new ol.layer.Vector({
        source: new ol.source.Vector(),
        map: map,
        style: function(feature, resolution) {
          var text = resolution < 5000 ? feature.get('name') : '';
          if (!highlightStyleCache[text]) {
            highlightStyleCache[text] = new ol.style.Style({
              stroke: new ol.style.Stroke({
                color: 'rgba(50,50,50,0.5)', // '#555',
                width: 1
              }),
              fill: new ol.style.Fill({
                color: 'rgba(255,0,0,0.1)' // 'rgba(255,0,0,0.1)'
              }),
              text: new ol.style.Text({
                font: '25px Calibri,sans-serif',
                text: text,
                fill: new ol.style.Fill({
                  color: '#000'
                }),
                stroke: new ol.style.Stroke({
                  color: '#500',
                  width: 2
                })
              })
            });
          }
          return highlightStyleCache[text];
        }
      });
	  
	  /*
	  hoverFeatureOverlay.getSource().on('change', function(event)
	  {
			console.log("change " + event);
			//console.log(event);
			//console.log(hoverFeatureOverlay.getSource().getFeatures());
			_geo_file_layer.changed();
	  });
	  */
}

function deleteFeature(feature)
{		
	var feature_id = feature.getProperties().id;
	
	var feature_delete_data = {
		feature_id: feature_id,
		//delete_all: delete_all
	};
	
	//var view_name = map_views[map_view_id].mapview_name;
	
    $.ajax({
                url: 'data/deleteFeature.php', // point to server-side PHP script 
                dataType: 'text',  // what to expect back from the PHP script, if anything
				
				data: JSON.stringify(feature_delete_data), // JSON.stringify({ Markers: markers })
				contentType: "application/json; charset=utf-8",
				//dataType: "json",
				
                cache: false,
                contentType: false,
                processData: false,
                //data: view_data,
                type: 'post',
                success: function(php_script_response){					
					if (php_script_response.indexOf("201") >= 0) // if (php_script_response == "201")
					{
						caveFeaturesDrawSource.removeFeature(feature);
						showNotification("Feature <b>" + feature.get('name') + "</b> was deleted.", { from: "top", align: "right" });						
						reloadMapFeatures();
					}
					else
						alert(php_script_response); // display response from the PHP script, if any
                }
     });
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

//-- implementation of Object.prototype functions generates errors like  "error SyntaxError: Failed to execute setRequestHeader' on 'XMLHttpRequest' " ?

/*Object.prototype.getName = function() { 
   var funcNameRegex = /function (.{1,})\(/;
   var results = (funcNameRegex).exec((this).constructor.toString());
   return (results && results.length > 1) ? results[1] : "";
};*/

/*
Object.prototype.inCollection = function() {
    for(var i=0; i<arguments.length; i++)
       if(arguments[i] == this) return true;
    return false;
}
*/

