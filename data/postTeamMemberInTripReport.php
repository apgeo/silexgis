<?php
	include_once 'db_common.php';	
	require_once 'GPSData.php';

	include_once('../geoPHP/geoPHP.inc');
	require_once 'db_utilities.php';
		
	$memberTripReportData = json_decode($submitData);
	//$cave->importFrom('JSON', $submitData);	
	
	
	$_user_id = $_SESSION["id_user"];

	if (!DbUtils::checkParameter(@$memberTripReportData->id_team_member, "int", "id_team_member is empty.")
		 || !DbUtils::checkParameter(@$memberTripReportData->id_trip_log, "int", "id_trip_log is empty.")
		)
		;//exit;
	
	$id_team_member = $memberTripReportData->id_team_member;
	$id_trip_log = $memberTripReportData->id_trip_log;
		
	//$cave_id = $tripReportData->cave_entrance_cave_id;
		
	/*if (!GPSData::user_has_modify_right($_user_id, $cave_id))
	{	
		echo "Access not granted";
		exit;
	}
	*/
	
	$tripLogToTeamMember = null;
	
	//if (!empty($trip_log_id))
		$tripLogToTeamMember = TripLogsToTeamMembersQuery::create()
						->findByIdteammember($id_team_member)
						->findByIdtriplog($id_trip_log);
	
	if ($tripLogToTeamMember)
	{
		echo "201";
		exit;
	}	
	else
	{		
		$tripLogToTeamMember = new TripLogs();
	}
			
	$tripLogToTeamMember->setIdteammember($id_team_member);
	$tripLogToTeamMember->setIdtriplog($id_trip_log);
		
	$tripLogToTeamMember->save();
		
	echo "201";
?>