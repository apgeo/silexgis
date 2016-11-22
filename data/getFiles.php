<?php
    include_once 'db_common.php';
	include_once 'db_utilities.php';	
	include_once '../db_interface.php';
	require_once 'GPSData.php';
	
	$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	@file_put_contents(basename(__FILE__, '.php')."input_data.log", $submitData); // file_get_contents('php://input')	
	
	$getFilesData = json_decode($submitData);
	
	$cave_id = $getFilesData->cave_id; //-- might be empty if insert used instead of edit
	
	$_user_id = $_SESSION["id_user"];
	
	$conid = DBCon::open_connection();
			
	GPSData::$ConId = DBCon::get_connection_id();
	
	$files = GPSData::get_files($_user_id, $cave_id);
	
	echo json_encode($files);
?>