<?php
    include_once 'db_common.php';

	$cave_id = $_GET["cave_id"];
	
	if (empty($cave_id))
		throw new Error("empty cave_id");

	$caveEntrances = CaveEntrancesQuery::create()->findByCaveid($cave_id);

	
	$point = PointsQuery::create()->findPK($caveEntrance->getCaveId());
	$caveEntrance->setVirtualColumn('CaveName', $cave->getName());


	echo $caveEntrances->toJSON();
?>