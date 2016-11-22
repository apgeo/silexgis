<?php
	include_once 'db_common.php';		
	require_once 'GPSData.php';
	
	include_once('../geoPHP/geoPHP.inc');
	
	require_once('FileSaver.php');
	
	$submitData = $_POST["form_data"];
	$fileData = json_decode($submitData);
	
	//$submitData = file_get_contents('php://input'); // $HTTP_RAW_POST_DATA
	//@file_put_contents(basename(__FILE__, '.php')."input_data.log", $submitData); // file_get_contents('php://input')
		
	//$fileData = json_decode($submitData);
	//$feature->importFrom('JSON', $submitData);	
		
	$_user_id = $_SESSION["id_user"];
		
	/* //-- if (!GPSData::user_has_modify_right($_user_id, $feature_id))
	{	
		echo "Access not granted";
		exit;
	}
	*/
	if (empty($fileData->cave_id) || ctype_space($fileData->cave_id))  // might need trim($featureData->feature_name) if not done on client side?
		raise_error("Cave id is empty.");
	
	if (empty($_FILES['file']['name']) || ctype_space($_FILES['file']['name']))  // might need trim($featureData->feature_name) if not done on client side?
		raise_error("No file specified.");


	$file_id = $fileData->file_id; //-- might be empty if insert used instead of edit
	$cave_id = $fileData->cave_id;
	
	try 
	{
	// get the PDO connection object from Propel
	$con = Propel::getConnection(Speogis::DATABASE_NAME);
	
	$con->beginTransaction();
	
	$file = null;
	
	if (!empty(0))
	{
		//$feature = FeaturesQuery::create()->findPK($feature_id);				
	}
	else
	{
		$file = new Files();
	}
		
	 
	$file_path = saveFile();
	
	$md5_hash = md5_file($file_path);
	$file_size = filesize($file_path);
	$file_name = basename($file_path);
	$file_mime = mime_content_type($file_path); //-- or maybe better get it from $_FILES['file']
	
	$file->setFilename($file_name)
		->setUserid($_user_id)
		->setAddtime(time())
		->setFiletype(null)
		->setSize($file_size)
		->setMd5hash($md5_hash)
		->setMimetype();

	$file->save(); // or ->update ?
	
	$file_id = $file->getId();
	
	
	$geo_object_type = "cave";
	
	$gtf = new GeoobjectsToFiles();
	
	$gtf->setFileid($file_id)
		->setGeoobjectid($cave_id)
		->setGeoobjecttype($geo_object_type)
	
	$gtf->save(); // or ->update ?
	
	$con->commit();	
	echo "201";		
	}
	catch (Exception $e) 
	{
		$con->rollback();
		throw $e;
	}	
?>