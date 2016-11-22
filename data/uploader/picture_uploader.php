<?php
/*
 * jQuery File Upload Plugin PHP Example
 * https://github.com/blueimp/jQuery-File-Upload
 *
 * Copyright 2010, Sebastian Tschan
 * https://blueimp.net
 *
 * Licensed under the MIT license:
 * http://www.opensource.org/licenses/MIT
 */

//error_reporting(E_ALL | E_STRICT);
//require('UploadHandler.php');
//$upload_handler = new UploadHandler();

	include_once '../db_common.php';		
	require_once '../GPSData.php';
	
	include_once('../../geoPHP/geoPHP.inc');
	
	require_once('../FileSaver.php');	
	require_once '../db_utilities.php';

	define('ROOTPATH', $_SERVER['DOCUMENT_ROOT']."/speogis"); //-- not flexible
	
	global $user_directory;
	global $upload_dir;
	global $thumbnails_dir;
	
	$user_directory = "";
	$upload_dir = ROOTPATH.'/uploads/pictures/' . $user_directory;
	$thumbnails_dir = $upload_dir."/thumbnail";
	
$options = array(
    'delete_type' => 'POST',
    'db_host' => 'localhost',
    'db_user' => 'root',
    'db_pass' => '',
    'db_name' => 'example',
    'db_table' => 'xfilesx',
	
	'user_dirs' => true,
	//'download_via_php' => true
	'download_via_php' => true,
	'upload_dir' => $upload_dir,
	//'upload_url' => 'http://localhost/speogis/uploads/pictures'
);

//error_reporting(E_ALL | E_STRICT);
require('UploadHandler.php');

class CustomUploadHandler extends UploadHandler {
/*
    protected function initialize() {	
		/*$this->db = new mysqli(
            $this->options['db_host'],
            $this->options['db_user'],
            $this->options['db_pass'],
            $this->options['db_name']
        );
        parent::initialize();
        $this->db->close();
		
	
	$con = Propel::getConnection("speogis"); // "Speogis::DATABASE_NAME
	//echo "initialize()";
    }
*/
/*
    protected function handle_form_data($file, $index) {
		//echo "handle_form_data($file, $index)";
        $file->title = @$_REQUEST['title'][$index];
        $file->description = @$_REQUEST['description'][$index];
    }
*/
	
    protected function handle_file_upload($uploaded_file, $name, $size, $type, $error,
            $index = null, $content_range = null) {
		//echo "handle_file_upload($uploaded_file, $name, $size, $type, $error, $index = null, $content_range = null)";
		
        $fileObject = parent::handle_file_upload(
            $uploaded_file, $name, $size, $type, $error, $index, $content_range
        );
		
        if (empty($file->error)) 
		{
			/*
            $sql = 'INSERT INTO `'.$this->options['db_table']
                .'` (`name`, `size`, `type`, `title`, `description`)'
                .' VALUES (?, ?, ?, ?, ?)';
            $query = $this->db->prepare($sql);
            $query->bind_param(
                'sisss',
                $file->name,
                $file->size,
                $file->type,
                $file->title,
                $file->description
            );
            $query->execute();
            $file->id = $this->db->insert_id;
			*/
			
	//$file_id = $fileData->file_id; //-- might be empty if insert used instead of edit
	
	//$cave_id = $fileData->cave_id;
	//$cave_id = $_REQUEST['upload_file_cave_id'];
	$_user_id = $_SESSION["id_user"];
	
	try 
	{
	// get the PDO connection object from Propel
	$con = Propel::getConnection("speogis");
	
	//$con->beginTransaction();
	
	$file = null;
	
	/*if (!empty(0))
	{
		//$feature = FeaturesQuery::create()->findPK($feature_id);				
	}
	else*/
	{
		$file = new Files();
	}
	//$file_name = savePictureFile();
	//var_dump($this);
	//$file_path = saveFile2();
	
	/*define('ROOTPATH', $_SERVER['DOCUMENT_ROOT']."/speogis"); //-- not flexible
	$user_directory = "";
	$upload_dir = ROOTPATH.'/uploader/' . 'pictures/' . $user_directory;*/
	global $user_directory;
	global $upload_dir;
	global $thumbnails_dir;

	$file_path = $upload_dir . $name;
	$thumbnail_file_name = $name;//$thumbnails_dir = 
	
	$picture_geo_loc_data = GeoUtils::read_picture_geolocation_data($file_path);
	
	//var_dump($picture_geo_loc_data);exit;
	GPSData::add_picture($_user_id, $name, $thumbnail_file_name, $picture_geo_loc_data);
	
	//var_dump($_FILES);
	
	//var_dump($name);
	//var_dump($uploaded_file);
	
	/*
	$md5_hash = md5_file($file_path);
	$file_size = filesize($file_path);
	$file_name = basename($file_path);	
	$file_mime = null;
	
	if(!function_exists('mime_content_type'))
		$file_mime = DbUtils::mime_content_type_ex($file_path);  //-- PHP module needs to be enabled or ?
	else
		$file_mime = mime_content_type($file_path); //-- or maybe better get it from $_FILES['file']
	
	
	$file->setFilename($file_name)
		->setUserid($_user_id)
		->setAddtime(time())
		->setFiletype(null)
		->setSize($file_size)
		->setMd5hash($md5_hash)
		->setMimetype($file_mime);

	$file->save(); // or ->update ?
	
	$file_id = $file->getId();
	
	
	$geo_object_type = "cave";
	
	$gtf = new GeoobjectsToFiles();
	
	$gtf->setFileid($file_id)
		->setGeoobjectid($cave_id)
		->setGeoobjecttype($geo_object_type);
	
	$gtf->save(); // or ->update ?
	*/
	//exit;
	//$con->commit();	
	//echo "201";		
	}
	catch (Exception $e) 
	{
		//$con->rollback();
		throw $e;
	}	
			
        }
        return $fileObject;
    }
	
/*
    protected function set_additional_file_properties($file) {
		echo "set_additional_file_properties()";
		
        parent::set_additional_file_properties($file);
        if ($_SERVER['REQUEST_METHOD'] === 'GET') {
            $sql = 'SELECT `id`, `type`, `title`, `description` FROM `'
                .$this->options['db_table'].'` WHERE `name`=?';
            $query = $this->db->prepare($sql);
            $query->bind_param('s', $file->name);
            $query->execute();
            $query->bind_result(
                $id,
                $type,
                $title,
                $description
            );
            while ($query->fetch()) {
                $file->id = $id;
                $file->type = $type;
                $file->title = $title;
                $file->description = $description;
            }
        }
    }
*/
/*
    public function delete($print_response = true) {
        $response = parent::delete(false);
        foreach ($response as $name => $deleted) {
            if ($deleted) {
                $sql = 'DELETE FROM `'
                    .$this->options['db_table'].'` WHERE `name`=?';
                $query = $this->db->prepare($sql);
                $query->bind_param('s', $name);
                $query->execute();
            }
        } 
        return $this->generate_response($response, $print_response);
    }
*/

	protected function get_user_id() {
        @session_start();
		//echo "get_user_id()";
		$_user_id = $_SESSION["id_user"];		
        //return session_id();
    }	
	
	protected function get_file_objects($iteration_method = 'get_file_object') 
	{
		//var_dump(parent::get_file_objects($iteration_method));
		
		
		
		return parent::get_file_objects( $iteration_method  );
	}
}

$upload_handler = new CustomUploadHandler($options);

//echo "start";
//$upload_handler = new UploadHandler();
//?>