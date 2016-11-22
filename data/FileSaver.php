<?php
	include_once 'db_common.php';
	require_once 'db_utilities.php';
	
	function saveFile()
	{
    //if ( 0 < $_FILES['file']['error'] ) {
	// Use 
	if (!empty($_FILES['file']) && ($_FILES['file']['error'] != UPLOAD_ERR_OK)) 
	{ 
	//uploading successfully done 
	//} else { 
	//throw new UploadException($_FILES['file']['error']); 
	//	new UploadException._getMessage($_FILES['file']['error']); 
	//} 
	//      echo 'Error: ' . $_FILES['file']['error'] . '['.((new UploadException())._getMessage($_FILES['file']['error'])).']'.'<br>';
       echo 'Error: ' . $_FILES['file']['error'] . '['.DbUtils::_getMessage($_FILES['file']['error']).']'.'<br>';
		//print_r($_FILES['file']);
    }
    else 			
	{
		$user_directory = "";
		$upload_dir = '../uploads/' . 'files/' . $user_directory;
		//var_dump($_FILES);
		
		$file_path = $upload_dir . $_FILES['file']['name'];
        move_uploaded_file($_FILES['file']['tmp_name'], $file_path);
		
		// set in php.ini post_max_size , upload_max_filesize ... http://php.net/manual/ro/ini.core.php#ini.post-max-size
		
		/*
		require_once 'GPSFileDataAdder.php';
		
		if (@GPSFileDataAdder::saveGPSFileData($file_path))
			echo "201";
		else
			echo "500";
		*/
		
		return $file_path;//$_FILES['file']['name'];
    }
	}	
	
	function saveFile2()
	{
    //if ( 0 < $_FILES['file']['error'] ) {
	// Use 
	//var_dump($_FILES);
	if (!empty($_FILES['files']) && ($_FILES['files']['error'][0] != UPLOAD_ERR_OK)) 
	{ 
	//uploading successfully done 
	//} else { 
	//throw new UploadException($_FILES['file']['error']); 
	//	new UploadException._getMessage($_FILES['file']['error']); 
	//} 
	//      echo 'Error: ' . $_FILES['file']['error'] . '['.((new UploadException())._getMessage($_FILES['file']['error'])).']'.'<br>';
       echo 'Error: ' . $_FILES['files']['error'][0] . '['.DbUtils::_getMessage($_FILES['files']['error'][0]).']'.'<br>';
		//print_r($_FILES['file']);
    }
    else 			
	{
		//define('ROOTPATH', dirname(__FILE__));
		define('ROOTPATH', $_SERVER['DOCUMENT_ROOT']."/speogis"); //-- not flexible
		
		$user_directory = "";
		$upload_dir = ROOTPATH.'/uploads/' . 'files/' . $user_directory;
		// $upload_dir = '../uploads/' . 'files/' . $user_directory;
		
		//var_dump($_FILES);
		
		var_dump($_FILES['files']['error'][0]);
		var_dump($_FILES['files']['tmp_name'][0]);				
		
		$index = 0;
		
		for ($index = 0; $index < count($_FILES); $index++)
		{
		
		$file_path = $upload_dir . $_FILES['files']['name'][0];
		
		var_dump($_FILES['files']['tmp_name'][0]);
		var_dump($file_path);
        move_uploaded_file($_FILES['files']['tmp_name'][0], $file_path);
		
		// set in php.ini post_max_size , upload_max_filesize ... http://php.net/manual/ro/ini.core.php#ini.post-max-size
		
		/*
		require_once 'GPSFileDataAdder.php';
		
		if (@GPSFileDataAdder::saveGPSFileData($file_path))
			echo "201";
		else
			echo "500";
		*/
		}
		
		return $file_path;//$_FILES['file']['name'];
    }
	}		
?>