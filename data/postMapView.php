<?php
	include_once 'db_common.php';		
	require_once 'db_utilities.php';

	$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	@file_put_contents(basename(__FILE__, '.php')."input_data.log", $submitData); // file_get_contents('php://input')
	
	// http://stackoverflow.com/questions/12987737/propelorm-with-mysql-spatial-data-type
	
	$mapViewData = json_decode($submitData);
	//var_dump($mapViewData);
	
	//$feature->importFrom('JSON', $submitData);	
	
	//$feature_id = $featureData->feature_id; //-- might be empty if insert used instead of edit
	$_user_id = $_SESSION["id_user"];
	
	/* //-- if (!GPSData::user_has_modify_right($_user_id, $feature_id))
	{	
		echo "Access not granted";
		exit;
	}
	*/
	if (empty($mapViewData->mapview_name) || ctype_space($mapViewData->mapview_name))  // might need trim($featureData->feature_name) if not done on client side?
		raise_error("MapView name is empty.");

		
	$center_lat = $mapViewData->center_geometry_coordinates[1];
	$center_lon = $mapViewData->center_geometry_coordinates[0];
	
	$center_geometry_wkt = "POINT ($center_lat $center_lon)";
	
	//var_dump($center_geometry_wkt);
	
	$center_geometry_wkb = DbUtils::wkt_to_wkb($center_geometry_wkt);
	
	//var_dump($center_geometry_wkb);
	
	$properties = json_encode($mapViewData->properties);
	
	/*
	$mapView = new MapViews();
	
	$mapView->setName($mapViewData->mapview_name);
	$mapView->setProperties($properties);
	$mapView->setCentergeometry((object)$center_geometry_wkb);
	

	$mapView->save(); // or ->update ?
	*/	
		$con = Propel::getConnection("speogis"); // BookPeer::DATABASE_NAME
		
		
        $query = "INSERT INTO `map_views` 	(`name`, 	`properties`, 	`center_geometry`	)
			VALUES	('{$mapViewData->mapview_name}', 	'$properties', 	GEOMFROMTEXT('$center_geometry_wkt'));";

		//var_dump($query);
		$stmt = $con->prepare($query);
		$res = $stmt->execute();
		
	echo "201";	
?>