<?php
	//session_start();
    require_once '../auth.php';

	require_once '../vendor/Propel/runtime/lib/Propel.php';
	Propel::init("../db/propel_1_6_pr/build/conf/speogis-conf.php");
	set_include_path("../db/propel_1_6_pr/build/classes" . PATH_SEPARATOR . get_include_path());

	$cave_id = $_GET["cave_id"];
	
	$cave = CavesQuery::create()->findPK($cave_id);

	echo $cave->toJSON();
?>