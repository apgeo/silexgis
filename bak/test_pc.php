<?php
	//session_start();
    require_once 'auth.php';

	require_once 'vendor/Propel/runtime/lib/Propel.php';
	Propel::init("db/propel_1_6_pr/build/conf/speogis-conf.php");
	set_include_path("db/propel_1_6_pr/build/classes" . PATH_SEPARATOR . get_include_path());

	//$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	
	$cave = new Caves(); 
	
	//$caveData = json_decode($submitData);
	
	//$cave->importFrom('JSON', $submitData);
	
	$cave->setName("testc");
	$cave->setDescription("desc");
	
	$cave->save();
	
	//print_r($caveData);
	
	echo "201";
	
	print_r($cave->toJSON());
	
	file_put_contents(basename(__FILE__, '.php')."input_data.log", file_get_contents('php://input'));
	
?>