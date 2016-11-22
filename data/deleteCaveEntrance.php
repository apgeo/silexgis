<?php
	include_once 'db_common.php';		
	require_once 'db_utilities.php';

	$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	@file_put_contents(basename(__FILE__, '.php')."input_data.log", $submitData); // file_get_contents('php://input')
	
	// http://stackoverflow.com/questions/12987737/propelorm-with-mysql-spatial-data-type
	
	$mvDeleteData = json_decode($submitData);
	//var_dump($mvDeleteData);
	
	//$feature->importFrom('JSON', $submitData);	
	
	//$feature_id = $featureData->feature_id; //-- might be empty if insert used instead of edit
	$_user_id = $_SESSION["id_user"];
	
	/* //-- if (!GPSData::user_has_modify_right($_user_id, $feature_id))
	{	
		echo "Access not granted";
		exit;
	}
	*/
	//if (empty($mapViewData->mapview_name) || ctype_space($mapViewData->mapview_name))  // might need trim($featureData->feature_name) if not done on client side?
	//	raise_error("MapView name is empty.");

		
	//$mvDeleteData->map_view_id;
	//$mvDeleteData->delete_all;
	
	if (!empty($mvDeleteData->map_view_id))
	{
		$mapView = MapViewsQuery::create()->findById($mvDeleteData->map_view_id); //-- filter by user id		
		$mapView->delete();
	}
	else
		if (!empty($mvDeleteData->delete_all))
		{
			$mapViews = MapViewsQuery::create()->find();			
			$mapViews->delete();
		}
		else
			throw new Exception("nor id, nor delete_all parameter was specified");
		
	echo "201";	
?>