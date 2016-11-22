<?php
/*	include_once('geoPHP/geoPHP.inc');

	include_once 'db_interface.php';
    include_once 'config.php';

	include_once 'GPSData.php';
*/	
	//include_once 'UploadException.php';
	
	require_once '../auth.php';
	
	function _getMessage($code) 
	{
        switch ($code) { 
            case UPLOAD_ERR_INI_SIZE: 
                $message = "The uploaded file exceeds the upload_max_filesize directive in php.ini"; 
                break; 
            case UPLOAD_ERR_FORM_SIZE: 
                $message = "The uploaded file exceeds the MAX_FILE_SIZE directive that was specified in the HTML form"; 
                break; 
            case UPLOAD_ERR_PARTIAL: 
                $message = "The uploaded file was only partially uploaded"; 
                break; 
            case UPLOAD_ERR_NO_FILE: 
                $message = "No file was uploaded"; 
                break; 
            case UPLOAD_ERR_NO_TMP_DIR: 
                $message = "Missing a temporary folder"; 
                break; 
            case UPLOAD_ERR_CANT_WRITE: 
                $message = "Failed to write file to disk"; 
                break; 
            case UPLOAD_ERR_EXTENSION: 
                $message = "File upload stopped by extension"; 
                break; 

            default: 
                $message = "Unknown upload error"; 
                break; 
        } 
        return $message; 
	}

    //if ( 0 < $_FILES['file']['error'] ) {
	// Use 
if (!empty($_FILES['file']) && ($_FILES['file']['error'] != UPLOAD_ERR_OK)) { 
//uploading successfully done 
//} else { 
//throw new UploadException($_FILES['file']['error']); 
//	new UploadException._getMessage($_FILES['file']['error']); 
//} 
//      echo 'Error: ' . $_FILES['file']['error'] . '['.((new UploadException())._getMessage($_FILES['file']['error'])).']'.'<br>';
        echo 'Error: ' . $_FILES['file']['error'] . '['._getMessage($_FILES['file']['error']).']'.'<br>';
		//print_r($_FILES['file']);
    }
    else 
	{
		$user_directory = "";
		$file_path = '../uploads/' . $user_directory . $_FILES['file']['name'];
        move_uploaded_file($_FILES['file']['tmp_name'], $file_path);
		// set in php.ini post_max_size , upload_max_filesize ... http://php.net/manual/ro/ini.core.php#ini.post-max-size
		
		require_once 'GPSFileDataAdder.php';
		
		if (@GPSFileDataAdder::saveGPSFileData($file_path))
			echo "201";
		else
			echo "500";
    }
?>