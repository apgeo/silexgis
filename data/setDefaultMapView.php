<?php
	include_once 'db_common.php';		
	require_once 'db_utilities.php';

	$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	@file_put_contents(basename(__FILE__, '.php')."input_data.log", $submitData); // file_get_contents('php://input')
	
	// http://stackoverflow.com/questions/12987737/propelorm-with-mysql-spatial-data-type
	
	$mapViewData = json_decode($submitData);	
	
	//$feature->importFrom('JSON', $submitData);	
	
	//$feature_id = $featureData->feature_id; //-- might be empty if insert used instead of edit
	$_user_id = $_SESSION["id_user"];
	
	/* //-- if (!GPSData::user_has_modify_right($_user_id, $feature_id))
	{	
		echo "Access not granted";
		exit;
	}
	*/
	if (empty($mapViewData->mapview_id))  // might need trim($featureData->feature_name) if not done on client side?
	// || ctype_space($mapViewData->mapview_id)
		DbUtils::raise_error("MapView id is empty.");

		
	$mapview_id = $mapViewData->mapview_id;

	
	$allMapViews = MapViewsQuery::create()->find();
	
	foreach ($allMapViews as $mapView)
	{
		$mapView->setIsDefault(false);
		$mapView->save();
	}
	//$allMapViews->setIsDefault(false);
	//$allMapViews->save();
	//var_dump($allMapViews);
	//$allMapViews->update(array("IsDefault" => false));
	
	
	$mapView = MapViewsQuery::create()->findPK($mapview_id);
		
	$mapView->setIsDefault(true);	

	$mapView->save();
	
	echo "201";	
?>