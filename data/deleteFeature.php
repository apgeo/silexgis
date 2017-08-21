<?php
	include_once 'db_common.php';		
	require_once 'db_utilities.php';

	$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	
	$deleteFeatureData = json_decode($submitData);
	
	$_user_id = $_SESSION["id_user"];
	
	/* //-- if (!GPSData::user_has_modify_right($_user_id, $feature_id))
	{	
		echo "Access not granted";
		exit;
	}
	*/
	//if (empty($mapViewData->mapview_name) || ctype_space($mapViewData->mapview_name))  // might need trim($featureData->feature_name) if not done on client side?
	//	raise_error("MapView name is empty.");
	
	if (!empty($deleteFeatureData->feature_id))
	{
		$feature = FeaturesQuery::create()->findById($deleteFeatureData->feature_id); //-- filter by user id
		$feature->delete();
		
		//-- probably in certain cases should also delete associated point (point_id fk)		
	}
		else
			throw new Exception("no id parameter was specified");
		
	echo "201";	
?>