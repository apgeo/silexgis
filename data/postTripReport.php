<?php
	include_once 'db_common.php';	
	require_once 'GPSData.php';

	include_once('../geoPHP/geoPHP.inc');
	require_once 'db_utilities.php';
	
	$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
		
	$tripReportData = json_decode($submitData);
	//$cave->importFrom('JSON', $submitData);
	//var_dump($tripReportData);
	
	$_user_id = $_SESSION["id_user"];

	/*
	if (//!DbUtils::checkParameter(@$tripReportData->trip_log_id, "int", "Trip report id is empty.") 
		// || !DbUtils::checkParameter(@$tripReportData->details, "string", "Cave entrance type is empty.")
		)
		;//exit;
	*/
	$trip_log_id = null;
	
	if (!empty($tripReportData->trip_log_id))
		$trip_log_id = $tripReportData->trip_log_id; //-- might be empty if insert used instead of edit
	
	//$cave_id = $tripReportData->cave_entrance_cave_id;
		
	/*if (!GPSData::user_has_modify_right($_user_id, $cave_id))
	{	
		echo "Access not granted";
		exit;
	}
	*/
	
	$tripLog = null;
	
	if (!empty($trip_log_id))
	{
		$tripLog = TripLogsQuery::create()->findPK($trip_log_id);
	}
	else
	{		
		$tripLog = new TripLogs();
	}
		
	
		$tripLog->setAddtime(time());
		$tripLog->setTripstarttime($tripReportData->tripreport_start_time);
		$tripLog->setTripendtime($tripReportData->tripreport_end_time);
		$tripLog->setDetails($tripReportData->tripreport_details);
		$tripLog->setTargetzone($tripReportData->tripreport_place);
		
		if (!empty($tripReportData->temporary))
			$tripLog->setTemporary(1);
		else
			$tripLog->setTemporary(false);
				
		$tripLog->save();
		$trip_log_id = $tripLog->getId();
		
		// clear the previous members
		$tripLogMembers = TripLogsToTeamMembersQuery::create()->findByIdtriplog($trip_log_id);
		$tripLogMembers->delete();
		
		$tripreport_member_ids = @split(',', $tripReportData->tripreport_members);
		
		
		foreach ($tripreport_member_ids as $tmId)
		{
			$tripLogToTeamMember = new TripLogsToTeamMembers();
			
			$tripLogToTeamMember->setIdtriplog($trip_log_id);
			$tripLogToTeamMember->setIdteammember($tmId);
			
			$tripLogToTeamMember->save();
		}
	
		$trip_log_result = (object) [
			'Id' => $trip_log_id,
		];
	
	// clean temporary not finished
	//-- must find a better solution
	//$tempTripLogs = TripLogsQuery::create()->findByTemporary(1);
	//$tempTripLogs->delete();
	
	echo json_encode($trip_log_result);
	//echo "trip_log_id=".$trip_log_id;
	//echo "201";	
?>