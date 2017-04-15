<?php
    include_once("grid_common.php");
	//var_dump($_FILES);

	require_once ROOTPATH.'/data/GPSFileDataAdder.php';
	
	if (@$_FILES['multipleFile']) 
	{
		$file_array = reArrayFiles($_FILES['multipleFile']);

		// if (!empty($_FILES['file']) && ($_FILES['file']['error'] != UPLOAD_ERR_OK)) { 
		//-- make error check 
		$_user_id = $_SESSION["id_user"];
		
		$user_directory = "";	
		 
		// set in php.ini post_max_size , upload_max_filesize ... http://php.net/manual/ro/ini.core.php#ini.post-max-size

		foreach ($file_array as $uploadedFile)
		{	
			/*if (!empty($_FILES['file']) && ($_FILES['file']['error'] != UPLOAD_ERR_OK)) 
			{ 
				//uploading successfully done 
				echo 'Error: ' . $_FILES['file']['error'] . '['._getMessage($_FILES['file']['error']).']'.'<br>';		
			}
			else */
			{
				$user_directory = "";
				$file_path = ROOTPATH.'/uploads/' . $user_directory . $uploadedFile['name'];
				move_uploaded_file($uploadedFile['tmp_name'], $file_path);
				// set in php.ini post_max_size , upload_max_filesize ... http://php.net/manual/ro/ini.core.php#ini.post-max-size
				
				if (@GPSFileDataAdder::saveGPSFileData($file_path, $_user_id))
					echo "Added file: '{$uploadedFile['name']}'";
				else
					echo "Error adding file '{$uploadedFile['name']}'";
			}
			
			echo "<br/><br/>";
		}
		
		echo "<br/><br/>";
		$added_file_count = count($file_array);
		echo "$added_file_count file(s) were added";		
	}

	function reArrayFiles(&$file_post) {

		$file_ary = array();
		$file_count = count($file_post['name']);
		$file_keys = array_keys($file_post);

		for ($i=0; $i<$file_count; $i++) {
			foreach ($file_keys as $key) {
				$file_ary[$i][$key] = $file_post[$key][$i];
			}
		}

		return $file_ary;
	}

	function get_last_inserted_id()
	{	
		$con = Propel::getConnection("speogis");
		
		$get_id_query = "SELECT last_insert_id() as last_insert_id";
		$get_id_stmt = $con->prepare($get_id_query);
		$gires = $get_id_stmt->execute();
		$last_insert_id_obj = $get_id_stmt->fetch(PDO::FETCH_OBJ);
		
		return $last_insert_id_obj->last_insert_id;
		//var_dump($last_insert_id_obj->last_insert_id);
	}
?>

<form action="<?=WEBROOT?>/user/add_multiple_geofiles.php" method="post" enctype="multipart/form-data">
	<b><h3>*{add_multiple_geofiles.page_title}*</h3></b>
	<br/>
	*{add_multiple_geofiles.description_text}*
	<br/>
		
	<!-- for localization: http://stackoverflow.com/questions/686905/labeling-file-upload-button -->
    
	<input type="file" name="multipleFile[]" id="fileUploadControl" multiple>
    <br/><br/>
		
	<input type="submit" value="*{add_multiple_geofiles.upload_file}*" name="submit">	
	
	<br/><br/>
	<a href="<?=WEBROOT?>/user/geofiles.php">*{generic.back}*</a>
</form>