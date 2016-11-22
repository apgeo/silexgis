<?php
	//session_start();
    require_once '../auth.php';

	require_once '../vendor/Propel/runtime/lib/Propel.php';
	Propel::init("../db/propel_1_6_pr/build/conf/speogis-conf.php");
	set_include_path("../db/propel_1_6_pr/build/classes" . PATH_SEPARATOR . get_include_path());

	require_once '../GPSData.php';

	$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	@file_put_contents(basename(__FILE__, '.php')."_input_data.log", $submitData); // file_get_contents('php://input')
		
	$text = $_POST["q"];
	
	if (empty($text)) // alternate JSON request
	{
	$searchDetails = json_decode($submitData);
	//$cave->importFrom('JSON', $submitData);	
	
	$text = $searchDetails->text;
	}
	
	$_user_id = $_SESSION["id_user"];

	/*if (!GPSData::user_has_modify_right($_user_id, -1))
	{	
		echo "Access not granted";
		exit;
	}*/

	if (empty($text) || ctype_space($text))  // might need trim($caveData->cave_name) if not done on client side?
		raise_error("Search text is empty.");
	
	$results = array();
	
	$caves = CavesQuery::create()->filterByName("%".$text."%")->find();
	
	foreach ($caves as $cave)
	{
		//-- should filter by main cave entrance and use ->find() instead of ->findOne()
		$caveEntrance = CaveEntrancesQuery::create()->filterByCaveid($cave->getId())->findOne(); 
		
		$mainEntrance = null;
		$main_entrance_point_db_id = null;
		
		if (!empty($caveEntrance))
		{
			$mainEntrance = $caveEntrance;			
			$main_entrance_db_id = $mainEntrance->getId();
			
			$main_entrance_point_db_id = $mainEntrance->getPointid();
			//$caveEntrance = PointsQuery::create()->filterByCaveid($cave->getId())->findOne(); 
		}
		
		$cave_result = (object) [
			'id' => $cave->getId(),
			'name' => $cave->getName(),
			'res_type' => 'cave',
			'point_db_id' => $main_entrance_point_db_id
			//main_entrance_point_db_id
			//'point_id'
		];
		
		$results[] = $cave_result;
	};
	
	$features = FeaturesQuery::create()->filterByName("%".$text."%")->find();

	foreach ($features as $feature)
	{	
		//-- should filter by main cave entrance and use ->find() instead of ->findOne()
		//$caveEntrance = FeaturesQuery::create()->filterByFeatureid($cave->getId())->findOne(); 
				
		$cave_result = (object) [
			'id' => "f_"+$feature->getId(),
			'name' => $feature->getName(),
			'res_type' => 'feature',
			'point_db_id' => $feature->getPointid()
			//main_entrance_point_db_id
			//'point_id'
		];
		
		$results[] = $cave_result;
	};
	
	
	echo json_encode($results);
	//echo "201";
	
	// https://www.w3.org/Protocols/rfc2616/rfc2616-sec10.html
	// https://en.wikipedia.org/wiki/List_of_HTTP_status_codes
	function raise_error($errorText)
	{
		echo "400"; //?
		echo "\r\n".$errorText;
		exit;
	}
	//file_put_contents(basename(__FILE__, '.php')."input_data.log", $submitData); // file_get_contents('php://input')	
?>