<?php
$file = fopen("ip.txt","a");
$ip=$_SERVER['REMOTE_ADDR'];

// now try it
$ua=getBrowser();
$yourbrowser= "Your browser: " . $ua['name'] . " " . $ua['version'] . " on " .$ua['platform'] . " reports: <br >" . $ua['userAgent'];


fwrite($file, date('m/d/Y h:i:s a', time())."  ".$ip." ".$yourbrowser."\r\n");
fclose($file);

function getBrowser() 
{ 
    $u_agent = $_SERVER['HTTP_USER_AGENT']; 
    $bname = 'Unknown';
    $platform = 'Unknown';
    $version= "";

    //First get the platform?
    if (preg_match('/linux/i', $u_agent)) {
        $platform = 'linux';
    }
    elseif (preg_match('/macintosh|mac os x/i', $u_agent)) {
        $platform = 'mac';
    }
    elseif (preg_match('/windows|win32/i', $u_agent)) {
        $platform = 'windows';
    }
    
    // Next get the name of the useragent yes seperately and for good reason
    if(preg_match('/MSIE/i',$u_agent) && !preg_match('/Opera/i',$u_agent)) 
    { 
        $bname = 'Internet Explorer'; 
        $ub = "MSIE"; 
    } 
    elseif(preg_match('/Firefox/i',$u_agent)) 
    { 
        $bname = 'Mozilla Firefox'; 
        $ub = "Firefox"; 
    } 
    elseif(preg_match('/Chrome/i',$u_agent)) 
    { 
        $bname = 'Google Chrome'; 
        $ub = "Chrome"; 
    } 
    elseif(preg_match('/Safari/i',$u_agent)) 
    { 
        $bname = 'Apple Safari'; 
        $ub = "Safari"; 
    } 
    elseif(preg_match('/Opera/i',$u_agent)) 
    { 
        $bname = 'Opera'; 
        $ub = "Opera"; 
    } 
    elseif(preg_match('/Netscape/i',$u_agent)) 
    { 
        $bname = 'Netscape'; 
        $ub = "Netscape"; 
    } 
    
    // finally get the correct version number
    $known = array('Version', $ub, 'other');
    $pattern = '#(?<browser>' . join('|', $known) .
    ')[/ ]+(?<version>[0-9.|a-zA-Z.]*)#';
    if (!preg_match_all($pattern, $u_agent, $matches)) {
        // we have no matching number just continue
    }
    
    // see how many we have
    $i = count($matches['browser']);
    if ($i != 1) {
        //we will have two since we are not using 'other' argument yet
        //see if version is before or after the name
        if (strripos($u_agent,"Version") < strripos($u_agent,$ub)){
            $version= $matches['version'][0];
        }
        else {
            $version= $matches['version'][1];
        }
    }
    else {
        $version= $matches['version'][0];
    }
    
    // check if we have a number
    if ($version==null || $version=="") {$version="?";}
    
    return array(
        'userAgent' => $u_agent,
        'name'      => $bname,
        'version'   => $version,
        'platform'  => $platform,
        'pattern'    => $pattern
    );
} 


?><?php	
    include_once 'header2.php';
?>

<!DOCTYPE html>

<html>


  <script>
  var map;
  var layers = [];
  var map_overlay_geo_comana;
  var user_mouse_interaction_type = 0;
 
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
      };
	  
    function init() {
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


// google
/*
	  var gmap = new google.maps.Map(document.getElementById('mapdiv'), {
  disableDefaultUI: true,
  keyboardShortcuts: false,
  draggable: false,
  disableDoubleClickZoom: true,
  scrollwheel: false,
  streetViewControl: false
});

var view = new ol.View({
  // make sure the view doesn't go beyond the 22 zoom levels of Google Maps
  maxZoom: 21
});
view.on('change:center', function() {
  var center = ol.proj.transform(view.getCenter(), 'EPSG:3857', 'EPSG:4326');
  gmap.setCenter(new google.maps.LatLng(center[1], center[0]));
});
view.on('change:resolution', function() {
  gmap.setZoom(view.getZoom());
});
*/

/////////////////////////
/////////////////////////
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
			 //projection: googleMercator,
             //displayProjection: epsg3857,
			 //displayProjection: wgs84,
			 //center: new OpenLayers.LonLat(45.669245, 25.416870).transform(new ol.proj.Projection('EPSG:4326'), new ol.proj.Projection('EPSG:3857')),
			  interactions: ol.interaction.defaults().extend([dragAndDropInteraction]),
			 zoom: 5,
			  controls: ol.control.defaults({
				attributionOptions: /** @type {olx.control.AttributionOptions} */ ({
				  collapsible: false
				})
			  }).extend([
				scaleLineControl
			  ]),
			  //layers: [],
			  //units: ol.control.ScaleLineUnits.METRIC
       } );      
 
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

function rtrim(str, maxLength) {
  return str.substr(0, maxLength);
}

var displayDetailsWindow = function(pixel) {
  
  var features = [];
  map.forEachFeatureAtPixel(pixel, function(feature, layer) {
    features.push(feature);
  });
  
  if (features.length > 0)
  {
	var selectedFeature = features[0];
  
	if (selectedFeature.get('elevation') == undefined)
		return;
		
	var popupHtml = "";
	//var coordinates = selectedFeature.getGeometry().getCoordinates();
	
	coord = ol.proj.transform(selectedFeature.getGeometry().getCoordinates(), 'EPSG:3857', 'EPSG:4326');

	popupHtml += "<b>" + selectedFeature.get('gpx_name') + "</b>" + "<br/>" + "<br/>";
	popupHtml += "<i>" + rtrim(coord[0]+"", 8) + ", " + rtrim(coord[1]+"", 8) + "</i>" + "<br/>";
	popupHtml += "Alt: " + selectedFeature.get('elevation') + "m" + "<br/>" + "<br/>";	
	popupHtml += selectedFeature.get('gpx_time') + "<br/>";
	popupHtml += "tip " + selectedFeature.get('_id_point_type') + "<br/>";
	
	//popupHtml += "<br/>" + "<br/>"+ "<br/>"
//"<iframe src='test_gps_upload.html'></iframe>";
//"<form action='./saveGPSFileData.php' method='post' enctype='multipart/form-data'> Select file to upload:    <input type='file' name='file' id='fileToUpload'>    <input type='submit' value='Upload GPS file' name='submit'>	</form>";
	
	//document.getElementById('info').innerHTML = info.join(', ') || '&nbsp';
	featureDetailsPopup.show(selectedFeature.getGeometry().getCoordinates(), popupHtml); // '<div><h2>Coordinates</h2><p>' + prettyCoord + '</p></div>'
	
	//console.info(selectedFeature);
  }
}

var displayFeatureInfo = function(pixel) {
  var features = [];
  map.forEachFeatureAtPixel(pixel, function(feature, layer) {
    features.push(feature);
  });
  
  if (document.getElementById('info') != null) //--
  if (user_mouse_interaction_type == 0)
		
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

map.on('pointermove', function(evt) {
  if (evt.dragging) {
    return;
  }
  var pixel = map.getEventPixel(evt.originalEvent);
  displayFeatureInfo(pixel);
});

map.on('click', function(evt) {
  //displayFeatureInfo(evt.pixel);
  displayDetailsWindow(evt.pixel);
  //console.info(evt);
});

map.on('doubleclick', function(evt) {
  //displayFeatureInfo(evt.pixel);
  console.info(evt);
});
		//map.controls.addControl(new ol.control.ScaleLine());
		//map.addControls(new ol.control.Attribution());
       
	   /*map.addControls(new ol.control.LayerSwitcher(),
                        new ol.control.ScaleLine(),
                        new ol.control.Permalink(),                    
                        new ol.control.Attribution(),
						//new ol.control.Navigation()
						//new ol.control.PanZoomBar()
						]);
       */
       
       // OSM("Mapnik"/*, {projection: epsg4326}*/)); // "EPSG:3857"
	   // -- map.addLayer(mapQuestOpenLayer = new ol.layer.Tile({source: new ol.source.OSM()}));
       
	   /*map.addLayer(new ol.layer.
       .Tile({
  source: new ol.source.OSM({
    attributions: [
      new ol.Attribution({
        html: 'All maps &copy; ' +
            '<a href="http://www.openseamap.org/">OpenSeaMap</a>'
      }),
      ol.source.OSM.ATTRIBUTION
    ],
    crossOrigin: null,
    url: 'http://tiles.openseamap.org/seamark/{z}/{x}/{y}.png'
  })
});
	   */
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
	
	   /*
       var mapQuestOpenLayer;
        map.addLayer(mapQuestOpenLayer = new ol.layer.OSM("MapQuest Open",											
                                           // ["http://otile1.mqcdn.com/tiles/1.0.0/osm/${z}/${x}/${y}.png",
                                            // "http://otile2.mqcdn.com/tiles/1.0.0/osm/${z}/${x}/${y}.png",
                                            // "http://otile3.mqcdn.com/tiles/1.0.0/osm/${z}/${x}/${y}.png",
                                            // "http://otile4.mqcdn.com/tiles/1.0.0/osm/${z}/${x}/${y}.png"],
                                            // { // projection: epsg3857, sphericalMercator: false,
											// attribution: "&copy; <a href='http://www.openstreetmap.org/'>OpenStreetMap</a> and contributors, under an <a href='http://www.openstreetmap.org/copyright' title='ODbL'>open license</a>. Tiles Courtesy of <a href='http://www.mapquest.com/'>MapQuest</a> <img src='http://developer.mapquest.com/content/osm/mq_logo.png'>" }));
       
                                            
                                            
       map.addLayer(new ol.layer.OSM("Humanitarian Style",
                                           ["http://a.tile.openstreetmap.fr/hot/${z}/${x}/${y}.png",
                                            "http://b.tile.openstreetmap.fr/hot/${z}/${x}/${y}.png",
                                            "http://c.tile.openstreetmap.fr/hot/${z}/${x}/${y}.png"],
                                            {attribution: "&copy; <a href='http://www.openstreetmap.org/'>OpenStreetMap</a> and contributors, under an <a href='http://www.openstreetmap.org/copyright' title='ODbL'>open license</a>. Humanitarian style by <a href='http://hot.openstreetmap.org'>H.O.T.</a>",
                                            "tileOptions": { "crossOriginKeyword": null }}));
       
       
       map.addLayer(new ol.layer.OSM("Stamen watercolour",                                                   
                                           ["http://tile.stamen.com/watercolor/${z}/${x}/${y}.png"],
                                            {attribution: "&copy; <a href='http://www.openstreetmap.org/'>OpenStreetMap</a> and contributors, under an <a href='http://www.openstreetmap.org/copyright' title='ODbL'>open license</a>. Watercolour style by <a href='http://stamen.com'>Stamen Design</a>",
                                            "tileOptions": { "crossOriginKeyword": null }}));
      
       map.addLayer(new ol.layer.OSM("Stamen toner",                                                   
                                           ["http://tile.stamen.com/toner/${z}/${x}/${y}.png"],
                                            {attribution: "&copy; <a href='http://www.openstreetmap.org/'>OpenStreetMap</a> and contributors, under an <a href='http://www.openstreetmap.org/copyright' title='ODbL'>open license</a>. Toner style by <a href='http://stamen.com'>Stamen Design</a>",
                                            "tileOptions": { "crossOriginKeyword": null }}));
                                            
                                            
       map.addLayer(new ol.layer.OSM("CartoDB positron",                                                   
                                           ["http://a.basemaps.cartocdn.com/light_all/${z}/${x}/${y}.png",
                                            "http://b.basemaps.cartocdn.com/light_all/${z}/${x}/${y}.png",
                                            "http://c.basemaps.cartocdn.com/light_all/${z}/${x}/${y}.png",
                                            "http://d.basemaps.cartocdn.com/light_all/${z}/${x}/${y}.png"],
                                            {attribution: "&copy; <a href='http://www.openstreetmap.org/copyright'>OpenStreetMap</a> contributors, &copy; <a href='http://cartodb.com/attributions'>CartoDB</a>" }));
       map.addLayer(new ol.layer.OSM("CartoDB dark matter",                                                   
                                           ["http://a.basemaps.cartocdn.com/dark_all/${z}/${x}/${y}.png",
                                            "http://b.basemaps.cartocdn.com/dark_all/${z}/${x}/${y}.png",
                                            "http://c.basemaps.cartocdn.com/dark_all/${z}/${x}/${y}.png",
                                            "http://d.basemaps.cartocdn.com/dark_all/${z}/${x}/${y}.png"],
                                            {attribution: "&copy; <a href='http://www.openstreetmap.org/copyright'>OpenStreetMap</a> contributors, &copy; <a href='http://cartodb.com/attributions'>CartoDB</a>" }));
       map.addLayer(new ol.layer.OSM("CartoDB positron (no labels)",                                                   
                                           ["http://a.basemaps.cartocdn.com/light_nolabels/${z}/${x}/${y}.png",
                                            "http://b.basemaps.cartocdn.com/light_nolabels/${z}/${x}/${y}.png",
                                            "http://c.basemaps.cartocdn.com/light_nolabels/${z}/${x}/${y}.png",
                                            "http://d.basemaps.cartocdn.com/light_nolabels/${z}/${x}/${y}.png"],
                                            {sphericalMercator: false, attribution: "&copy; <a href='http://www.openstreetmap.org/copyright'>OpenStreetMap</a> contributors, &copy; <a href='http://cartodb.com/attributions'>CartoDB</a>" }));
		
		map.addLayer(new ol.layer.Tile({
        source: new ol.source.MapQuest({layer: 'sat'})
      }));
	  
		 googlelayer = new ol.layer.Google.v3("Google street", {sphericalMercator: true});
		
		var sphericalMercatorEnabled = false;
	   googlelayer = new ol.layer.Google("Google Satellite", {type: google.maps.MapTypeId.SATELLITE, numZoomLevels: 14, sphericalMercator: sphericalMercatorEnabled});
	   map.addLayer(googlelayer);

	      var gphy = new ol.layer.Google(
        "Google Physical",
        {type: google.maps.MapTypeId.TERRAIN, visibility: false, sphericalMercator: sphericalMercatorEnabled}
    );
    var gmap = new ol.layer.Google(
        "Google Streets", // the default
        {numZoomLevels: 20, visibility: false, sphericalMercator: sphericalMercatorEnabled}
    );
    var ghyb = new ol.layer.Google(
        "Google Hybrid",
        {type: google.maps.MapTypeId.HYBRID, numZoomLevels: 5, visibility: false, sphericalMercator: sphericalMercatorEnabled} // numZoomLevels: 22
    );

    map.addLayers([gsat, gphy, gmap, ghyb]);
	
       map.addLayer(new ol.layer.OSM("CartoDB dark matter (no labels)",
                                           ["http://a.basemaps.cartocdn.com/dark_nolabels/${z}/${x}/${y}.png",
                                            "http://b.basemaps.cartocdn.com/dark_nolabels/${z}/${x}/${y}.png",
                                            "http://c.basemaps.cartocdn.com/dark_nolabels/${z}/${x}/${y}.png",
                                            "http://d.basemaps.cartocdn.com/dark_nolabels/${z}/${x}/${y}.png"],
                                            {attribution: "&copy; <a href='http://www.openstreetmap.org/copyright'>OpenStreetMap</a> contributors, &copy; <a href='http://cartodb.com/attributions'>CartoDB</a>" })
											//, { isBaseLayer: true }
											);
		
           //Vector Layer
    var vectorLayer = new ol.layer.Vector("Simple Geometry", {
        preFeatureInsert: function(feature) {
           feature.geometry.transform(new ol.proj.Projection("EPSG:4326"),new ol.proj.Projection("EPSG:900913"))
    }
    });
    map.addLayer(vectorLayer);  
    */
	//yahooLayer = new ol.layer.Yahoo("Yahoo");
    //        map.addLayer(yahooLayer);
			


        // API key for http://openlayers.org. Please get your own at
        // http://bingmapsportal.com/ and use that instead.
        //var apiKey = "AqTGBsziZHIJYYxgivLBf0hVdrAk9mWO5cQcb8Yux8sW5M8c8opEC2lZqKR1ZZXf";
		//var apiKey = "";
        //var map;
        
            //map.addControl(new ol.control.LayerSwitcher());

            // var road = new ol.layer.Bing({
                // name: "Road",
                // key: apiKey,
                // type: "Road"
            // });
            // var hybrid = new ol.layer.Bing({
                // name: "Hybrid",
                // key: apiKey,
                // type: "AerialWithLabels"
            // });
            // var aerial = new ol.layer.Bing({
                // name: "Aerial",
                // key: apiKey,
                // type: "Aerial"
            // });

            // map.addLayers([road, hybrid, aerial]);

			// var shaded = new ol.layer.VirtualEarth("Shaded", {
                // type: VEMapStyle.Shaded
            // });
            // var hybrid = new ol.layer.VirtualEarth("Hybrid", {
                // type: VEMapStyle.Hybrid
            // });
            // var aerial = new ol.layer.VirtualEarth("Aerial", {
                // type: VEMapStyle.Aerial
            // });

            // map.addLayers([shaded, hybrid, aerial]);
            //map.setCenter(new OpenLayers.LonLat(-110, 45), 3);

    			
       //map.setBaseLayer(mapQuestOpenLayer);
	   //map.setBaseLayer(map_layer_arcgis);
	   map_layer_osm.setVisible(false);
	   map_layer_arcgis.setVisible(true);
	   //var layers = this.map.getLayers();
       //layers.insertAt(0, base_layer);
	   
	   //map.setBaseLayer(googlelayer);
	   
	       //Point
    
	/*var point = new OpenLayers.Geometry.Point(25.416870, 45.669245);
    //point.transform(new ol.proj.Projection("EPSG:4326"), new ol.proj.Projection("EPSG:900913"));
    var pointFeature = new OpenLayers.Feature.Vector(point);
    vectorLayer.addFeatures([pointFeature]);
	
	   var fromProjection = new ol.proj.Projection("EPSG:4326"); // transform from WGS 1984
       var toProjection = new ol.proj.Projection("EPSG:900913"); // to Spherical Mercator Projection
	  */ 
	  

	   startLon = 25.36640167236328;//25.416870;
	   startLat = 45.89311462575596;//45.669245;
	   // 25.416870, 45.669245
	   
	   //var position       = new OpenLayers.LonLat(startLon, startLat).transform(fromProjection, toProjection);
       var zoom           = 12;
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
	   
	   // map.setCenter(new OpenLayers.LonLat(10.2, 48.9).transform(
        // new ol.proj.Projection("EPSG:4326"),
        // map.getProjectionObject()
    // ), 5);
	   
	   // map.zoomToMaxExtent();
       //if (!map.getCenter()) map.zoomToMaxExtent();		
	   
	   //--initEditing();
    //}

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
      var continuePolygonMsg = 'Click pentru a continua poligonul';


      /**
       * Message to show when the user is drawing a line.
       * @type {string}
       */
      var continueLineMsg = 'Click pentru a continua linia';


      /**
       * Handle pointer move.
       * @param {ol.MapBrowserEvent} evt The event.
       */
      var pointerMoveHandler = function(evt) {
        if (evt.dragging || (user_mouse_interaction_type != 1)) {
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
            length += wgs84Sphere.haversineDistance(c1, c2);
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

      function addInteraction() {									  
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
              measureTooltipElement.className = 'tooltip tooltip-static';
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
        helpTooltipElement.className = 'tooltip hidden';
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
        measureTooltipElement.className = 'tooltip tooltip-measure';
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
		user_mouse_interaction_type = $(this).is(":checked") ? 1 : 0;
		
		map.removeInteraction(draw);
		
		if (user_mouse_interaction_type == 1)        
			addInteraction();				

    }
	);	
	
	  }	
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
				unknownFeatures.push(features[index]);
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
			
		var q_res = confirm(String.format("Datele au fost afisate pe harta ({0}).\r\nDoriti sa le adaugati si in baza de date?", dataDescriptionText));
		
		if (q_res)
		{
			console.info(features);					
			saveGPSFileData(event.file);
		}
	}

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
					if (php_script_response == "201")
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

		///////////////////////////
		// database geojson layer with points
		var db_features_source = new ol.source.Vector({
      url: 'data/getDBGeoData.php',
      format: new ol.format.GeoJSON({

         defaultDataProjection :'EPSG:4326', 
         projection: 'EPSG:3857'	
      })
	});
		
	 var db_features_layer = new ol.layer.Vector({
   source: db_features_source ,
   name: 'database features',
   //style: style_1()
           //style: caveStyle
		   /*function(feature) {
          return geoStyle[feature.getGeometry().getType()];
        }*/

	});
	
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
		
		var defaultStyle = geoStyle[feature.getGeometry().getType()];
        //console.log(feature.getProperties());
		var _point_type = feature.get('_id_point_type');
		
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
	
	
	/*
	var map = new ol.Map({
	  target: 'map',
	  controls: ol.control.defaults().extend([ new ol.control.ScaleLine({ units:'metric' }) ]),
	  layers: layers,
	  view: new ol.View({
		center: ol.proj.transform([0, 10], 'EPSG:4326', 'EPSG:3857'),
		zoom: 3
	  })
	});
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

$("#hgPersaniCentruCheckBox").prop('checked', map_overlay_geo_comana.getVisible());

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



initMeasurement();
loadGeoFiles();
	}		
	
	function loadGeoFiles()
	{
	
	var geoFiles = [];
	/*["ovCurrent.gpx", 
	"Waypoints_18-OCT-15.gpx", 
	"persani_ov_adi.gpx",
	"Waypoints_17-OCT-15.gpx",
	"garminCurrent.gpx"];
	*/
	
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

/*Object.prototype.getName = function() { 
   var funcNameRegex = /function (.{1,})\(/;
   var results = (funcNameRegex).exec((this).constructor.toString());
   return (results && results.length > 1) ? results[1] : "";
};*/
$(document).ready(function() {
  // Handler for .ready() called.
  init();
});
  </script>
  
<!--<body onload="init();">-->
<?php 
//echo "zxc";
?>    

  <!--<span id="header">z</div>-->
  <div id="mapdiv" class="mapdiv" ></div>
  <div div id="toolbox">
	<ul id="layerswitcher" onchange="switchLayer();" >
		<li><label><input type="radio" name="layer" value="0" checked=""> OSM</label></li>
		<li><label><input type="radio" name="layer" value="1"> MapQuest Satelitar</label></li>
		<li><label><input type="radio" name="layer" value="2"> MapQuest Hibrid</label></li>
		<li><label><input type="radio" name="layer" value="3"> MapQuest OSM</label></li>
		<li><label><input type="radio" name="layer" value="4"> ArcGIS</label></li>
		<li><label><input type="radio" name="layer" value="5"> Bing Topo</label></li>
		<li><label><input type="radio" name="layer" value="6"> Bing Aerial</label></li>
		<li><label><input type="radio" name="layer" value="7"> Bing Etichetat</label></li>		
		<!--<li><label><input type="checkbox" name="layer" value="99"> Harta gelogica 1970</label></li>		-->
	</ul>
	</div>

<div id="measurementsBox">	
	<div>
	
		<label><input type="checkbox" onchange="" id="hgPersaniCentruCheckBox" value="99"/>Harta gelogica 1970<label>
		<a href='./assets/layer_images/persani_comana_geologica.jpg' target='_blank' >geologica persani centru</a><div id="slider-id"><div class="ui-slider-handle">geo</div></div>
		
	</div>
	
		<!--
	<form class="form-inline">
      <label>Geometry type &nbsp;</label>
      <select id="geometry_type">
        <option value="Point">Point</option>
        <option value="LineString">LineString</option>
        <option value="Polygon">Polygon</option>
      </select>
	  
    </form>  
	-->


	<form class="form-inline">
	  <label><input type="checkbox" onchange="" id="measurementCheckBox" value="99"/>Distante </label>
      <label>Tip:&nbsp;</label>
        <select id="measure_type">
          <option value="length">Distanta</option>
          <option value="area">Arie</option>
        </select>
        <label class="checkbox">
          <input type="checkbox" id="geodesic"/>
          masuratori geodezice
        </label>
		</form>		
</div>

	<div id="pointInfoBox" >
          <div id="info" <!--class="alert alert-success"-->>
            
          </div>
        </div>		

<!--
span4 offset4

<img src="./res/banda-rosie.png" />-->
</body>
</html>