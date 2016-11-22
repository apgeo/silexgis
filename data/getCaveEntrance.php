<?php	
    require_once '../auth.php';
	require_once 'db_common.php';		
	
	$cave_entrance_id = $_GET["cave_entrance_id"];
	
	$caveEntrance = CaveEntrancesQuery::create()->findPK($cave_entrance_id);

	echo $caveEntrance->toJSON();
?>