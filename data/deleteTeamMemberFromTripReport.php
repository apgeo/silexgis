<?php
	include_once 'db_common.php';		
	require_once 'db_utilities.php';
	
	$memberTripReportData = json_decode($submitData);
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
	if (!DbUtils::checkParameter(@$memberTripReportData->id_trip_log_to_member, "int", "id_trip_log_to_team_member is empty.") 		 
		)
		;//exit;
		
	//$mvDeleteData->map_view_id;
	//$mvDeleteData->delete_all;
	
	if (!empty($memberTripReportData->$id_trip_log_to_member))
	{
		$tripLogToTeamMember = TripLogsToTeamMembersQuery::create()->findById($memberTripReportData->$id_trip_log_to_member); //-- filter by user id		
		$tripLogToTeamMember->delete();
	}
	/*else
		if (!empty($mvDeleteData->delete_all))
		{
			$mapViews = MapViewsQuery::create()->find();			
			$mapViews->delete();
		}*/
		else
			throw new Exception("nor id, nor delete_all parameter was specified");
		
	echo "201";	
?>