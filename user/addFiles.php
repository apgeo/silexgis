<form action="./addFiles.php" method="post" enctype="multipart/form-data">
    Select file to upload:
    <input type="file" name="multipleFile[]" id="fileToUpload" multiple>
    <input type="submit" value="Upload file" name="submit">
</form>

<?php
    include_once '../header.php';
    include_once '../data/db_common.php';	
	//var_dump($_FILES);
	
	if ($_FILES['multipleFile']) 
	{
		$file_array = reArrayFiles($_FILES['multipleFile']);

		// if (!empty($_FILES['file']) && ($_FILES['file']['error'] != UPLOAD_ERR_OK)) { 
		//-- make error check 
		$_user_id = $_SESSION["id_user"];
		
		$user_directory = "";	
		 
		// set in php.ini post_max_size , upload_max_filesize ... http://php.net/manual/ro/ini.core.php#ini.post-max-size

		foreach ($file_array as $uploadedFile)
		{	
			$file_path = '../uploads/' . $user_directory . $uploadedFile['name'];
			move_uploaded_file($uploadedFile['tmp_name'], $file_path);
		
			print 'File Name: ' . $uploadedFile['name'].' Type: ' . $uploadedFile['type']." size ". $uploadedFile['size']."<br/>";
			
			$md5_hash = md5_file($file_path);
			
			$file = new Files();
			
			$file->setFilename($uploadedFile['name']);
			$file->setUserid($_user_id);
			$file->setAddtime(time());
			$file->setFiletype("");
			$file->setSize($uploadedFile['size']);
			$file->setMd5hash($md5_hash);
			$file->setMimetype($uploadedFile['type']);
			
			$file->save();
			
			//$file_id = get_last_inserted_id();
			
			//$feature = FeaturesQuery::create()->findPK($feature_id);
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