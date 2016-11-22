<?php
	//session_start();
    require_once '../auth.php';

	require_once 'db_common.php';		

	$cave_id = $_GET["cave_id"];
	
	$cave = CavesQuery::create()->findPK($cave_id);

	echo $cave->toJSON();
?>