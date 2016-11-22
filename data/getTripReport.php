<?php
	include_once 'db_common.php';
	
	$trip_log_id = $_GET["trip_log_id"];
	
	$tripLog = TripLogsQuery::create()->findPK($trip_log_id);

	$tripLogMembers = TripLogsToTeamMembersQuery::create()->findByIdtriplog($trip_log_id);
	
	$tripLogMembers_ids = "";
	$tripLogMembers_array = array();
	
	foreach ($tripLogMembers as $tlM)
	{
		$tripLogMembers_ids = $tripLogMembers_ids . $tlM->getIdteammember() . ",";
		$tripLogMembers_array[] = $tlM->getIdteammember();
	}
	
	//$tripLog->Members = $tripLogMembers_ids;
	$tripLog->setVirtualColumn('MemberIds', $tripLogMembers_ids);
	$tripLog->setVirtualColumn('Members', $tripLogMembers_array);
	
	echo $tripLog->toJSON();
?>