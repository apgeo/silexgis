<?php
	//
	// https://github.com/bmcbride/PHP-Database-GeoJSON
	//
	
	include_once('../geoPHP/geoPHP.inc');

	//include_once 'db_interface.php';
    include_once '../config.php';

	include_once 'GPSData.php';
	require_once '../auth.php';
	
	require_once('PictureFileSaver.php');
	require_once 'db_utilities.php';
		
	$conid = DBCon::open_connection();    
			
	GPSData::$ConId = DBCon::get_connection_id();
			
	$user_id = 1;

/*
* If bbox variable is set, only return records that are within the bounding box
* bbox should be a string in the form of 'southwest_lng,southwest_lat,northeast_lng,northeast_lat'
* Leaflet: map.getBounds().pad(0.05).toBBoxString()
*/
$sql; $bbox;
/*
if (isset($_GET['bbox']) || isset($_POST['bbox'])) {
    $bbox = explode(',', $_GET['bbox']);
    //$sql = $sql . ' WHERE x <= ' . $bbox[2] . ' AND x >= ' . $bbox[0] . ' AND y <= ' . $bbox[3] . ' AND y >= ' . $bbox[1];
}
*/
$bbox = explode(',', $_GET['bbox']);

//$points = GPSData::get_gps_points($user_id);
//$features = GPSData::get_features($user_id);
$pictures = GPSData::get_pictures($user_id, $bbox);

//$x = array_merge($features, $cave_entrances);

//$features + $cave_entrances
# Build GeoJSON feature collection array
$geojson = array(
   'type'      => 'FeatureCollection',
   'features'  => array()
);

# Loop through rows to build feature arrays


foreach ($pictures as $row)
{
//print_r($row);
    $properties = $row;
    # Remove wkb and geometry fields from properties	
    unset($properties['wkb']);
    //unset($properties['coords']);
	unset($properties['sg']);
	unset($properties['spatial_geometry']);
	
	$properties["url"] = getPictureURL($properties["file_path"]);
	$properties["thumbUrl"] = getPictureThumbURL($properties["thumb_file_path"]);
	
    $feature = array(
         'type' => 'Feature',
         'geometry' => json_decode(DbUtils::wkt_to_json($row['sg'])),
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