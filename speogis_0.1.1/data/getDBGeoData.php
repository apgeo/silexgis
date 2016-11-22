<?php
	//
	// https://github.com/bmcbride/PHP-Database-GeoJSON
	//
	
	include_once('../geoPHP/geoPHP.inc');

	include_once '../db_interface.php';
    include_once '../config.php';

	include_once '../GPSData.php';
	require_once '../auth.php';
	
	function point_to_json($lat, $long) // function wkb_to_json($wkb)
	{	
		// open layers wants the returned objects to have the coordinates as long, lat -> so custom formatting needs to be done before feeding geoPHP
		// note that this is probably the wrong format to use in geoPHP or other libraries because for processing of the custom long lat order
		//$geom = geoPHP::load("POINT($lat $long)"); // $geom = geoPHP::load($wkb,'wkb');
		 $geom = geoPHP::load("POINT($long $lat)"); // $geom = geoPHP::load($wkb,'wkb');
		//$geom_coord_order = 1;
		return $geom->out('json');
	}

	function wkb_to_json($wkb)
	{	
		if (empty($wkb) || $wkb == "NULL")
			return "";
			
		// open layers wants the returned objects to have the coordinates as long, lat -> so custom formatting needs to be done before feeding geoPHP
		// note that this is probably the wrong format to use in geoPHP or other libraries because for processing of the custom long lat order
		//$geom = geoPHP::load("POINT($lat $long)"); // $geom = geoPHP::load($wkb,'wkb');
		//var_dump($wkb);
		
		$geom = geoPHP::load($wkb,'wkt');
		//$geom_coord_order = 1;
		return $geom->out('json');
	}
	
	$conid = DBCon::open_connection();    
			
	GPSData::$ConId = DBCon::get_connection_id();
			
	$user_id = 1;

/*
* If bbox variable is set, only return records that are within the bounding box
* bbox should be a string in the form of 'southwest_lng,southwest_lat,northeast_lng,northeast_lat'
* Leaflet: map.getBounds().pad(0.05).toBBoxString()
*/
$sql;

if (isset($_GET['bbox']) || isset($_POST['bbox'])) {
    $bbox = explode(',', $_GET['bbox']);
    $sql = $sql . ' WHERE x <= ' . $bbox[2] . ' AND x >= ' . $bbox[0] . ' AND y <= ' . $bbox[3] . ' AND y >= ' . $bbox[1];
}


//$points = GPSData::get_gps_points($user_id);
$features = GPSData::get_features($user_id);
$cave_entrances = GPSData::get_cave_entrances($user_id);

$x = array_merge($features, $cave_entrances);

//$features + $cave_entrances
# Build GeoJSON feature collection array
$geojson = array(
   'type'      => 'FeatureCollection',
   'features'  => array()
);

# Loop through rows to build feature arrays


foreach ($x as $row)
{
//print_r($row);
    $properties = $row;
    # Remove wkb and geometry fields from properties
    unset($properties['wkb']);
    unset($properties['coords']);
	unset($properties['sg']);
	unset($properties['spatial_geometry']);
	
    $feature = array(
         'type' => 'Feature',
         'geometry' => json_decode(wkb_to_json($row['sg'])),
		 // 'geometry' => json_decode(point_to_json($row["lat_from_point"], $row["long_from_point"])), // wkb_to_json($row['wkb'])
         'properties' => $properties
    );
    # Add feature arrays to feature collection array
    array_push($geojson['features'], $feature);
}

header('Content-type: application/json');
echo json_encode($geojson, JSON_NUMERIC_CHECK);
//$conn = NULL;				
			DBCon::close_connection();    
	
?>