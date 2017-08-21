<?php
    include_once 'db_common.php';

	$_user_id = $_SESSION["id_user"];
	

	// $submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	// $searchDetails = json_decode($submitData);
	// $searchDetails->cave_id

	//$caves = CavesQuery::create()->findByUserid($_user_id);

	$cave_id = $_GET["cave_id"];
	
	if (!empty($cave_id))
		$caves = CavesQuery::create()->findById($cave_id); // findPK
	else
		$caves = CavesQuery::create()->find();
	
	echo $caves->toJSON();
?>