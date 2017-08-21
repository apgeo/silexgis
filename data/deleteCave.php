<?php
	include_once 'db_common.php';		
	require_once 'db_utilities.php';

	$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	
	$deleteCaveData = json_decode($submitData);
	
	$_user_id = $_SESSION["id_user"];
	
	/* //-- if (!GPSData::user_has_modify_right($_user_id, $feature_id))
	{	
		echo "Access not granted";
		exit;
	}
	*/
	//if (empty($mapViewData->mapview_name) || ctype_space($mapViewData->mapview_name))  // might need trim($featureData->feature_name) if not done on client side?
	//	raise_error("MapView name is empty.");
	
	if (!empty($deleteCaveData->cave_id))
	{
		$cave = CavesQuery::create()->findById($deleteCaveData->cave_id); //-- filter by user id
		$cave->delete();
		
		//-- probably in certain cases should also delete associated points (point_id fk) and cave entrances, maybe mark pictures and other files as orphaned (or give user choice to delete them)
	}
	else
		throw new Exception("no id parameter was specified");
	
	echo "201";	
?>