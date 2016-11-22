<?php
    include_once 'db_common.php';
	//include_once 'db_utilities.php';
	//include_once '../db_interface.php';
	
	//$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	
	//$cave_id = $getTeamMembersData->cave_id; //-- might be empty if insert used instead of edit
	
	//$_user_id = $_SESSION["id_user"];
	
	$teamMembers = TeamMembersQuery::create()->find();

	echo $teamMembers->toJSON();

?>