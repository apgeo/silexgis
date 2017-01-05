<?php
/*	include_once('geoPHP/geoPHP.inc');

	include_once 'db_interface.php';
    include_once 'config.php';

	include_once 'GPSData.php';
*/	
	//include_once 'UploadException.php';
	
	include_once 'db_common.php';
	
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

	function savePictureFile()
	{
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
		$root = realpath($_SERVER["DOCUMENT_ROOT"])."/speogis";
		
		$user_directory = "";
		$upload_dir = $root.'/uploads/' . 'pictures/' . $user_directory;
		$thumbs_upload_dir = $root.'/uploads/' . 'pictures/' . "thumbnail/" . $user_directory;
		//var_dump($_FILES);
		
		$file_path = $upload_dir . $_FILES['file']['name'];
        move_uploaded_file($_FILES['file']['tmp_name'], $file_path);
		
		//makeThumbnails($upload_dir, $_FILES['file']['name'], 0);
		// $thumbnail_file_name = "thumb".$_FILES['file']['name'];
		$thumbnail_file_name = "thumb".$_FILES['file']['name'];
		$thumbnail_file_path = $thumbs_upload_dir . $thumbnail_file_name;
		
		//echo "$file_path, $thumbnail_file_path";
		createThumbnail2($file_path, $thumbnail_file_path, 90, 63, $background=!false);
		
		
		// set in php.ini post_max_size , upload_max_filesize ... http://php.net/manual/ro/ini.core.php#ini.post-max-size
		
		/*
		require_once 'GPSFileDataAdder.php';
		
		if (@GPSFileDataAdder::saveGPSFileData($file_path))
			echo "201";
		else
			echo "500";
		*/
		
		return $_FILES['file']['name'];
    }
	}
	
	function getPictureURL($file_name)
	{
		$user_directory = "";
		$file_url = '/uploads/' . 'pictures/'. $user_directory . $file_name;
		
		return $file_url;
	}	

	function getPictureThumbURL($file_name)
	{
		$user_directory = "";
		$file_url = '/uploads/' . 'pictures/thumbnail/'. $user_directory . $file_name;
		
		return $file_url;
	}	

	// from http://stackoverflow.com/questions/11376315/creating-a-thumbnail-from-an-uploaded-image
	function makeThumbnails($updir, $img, $id)
{
    $thumbnail_width = 134;
    $thumbnail_height = 189;
    $thumb_beforeword = "thumb";
    $arr_image_details = getimagesize("$updir" . "$img"); // pass id to thumb name
	// $arr_image_details = getimagesize("$updir" . $id . '_' . "$img"); // pass id to thumb name
    $original_width = $arr_image_details[0];
    $original_height = $arr_image_details[1];
    if ($original_width > $original_height) {
        $new_width = $thumbnail_width;
        $new_height = intval($original_height * $new_width / $original_width);
    } else {
        $new_height = $thumbnail_height;
        $new_width = intval($original_width * $new_height / $original_height);
    }
    $dest_x = intval(($thumbnail_width - $new_width) / 2);
    $dest_y = intval(($thumbnail_height - $new_height) / 2);
    if ($arr_image_details[2] == 1) {
        $imgt = "ImageGIF";
        $imgcreatefrom = "ImageCreateFromGIF";
    }
    if ($arr_image_details[2] == 2) {
        $imgt = "ImageJPEG";
        $imgcreatefrom = "ImageCreateFromJPEG";
    }
    if ($arr_image_details[2] == 3) {
        $imgt = "ImagePNG";
        $imgcreatefrom = "ImageCreateFromPNG";
    }
    if ($imgt) {
        $old_image = $imgcreatefrom("$updir" . "$img");
		// $imgt($new_image, "$updir" . $id . '_' . "$thumb_beforeword" . "$img");
        $new_image = imagecreatetruecolor($thumbnail_width, $thumbnail_height);
        imagecopyresized($new_image, $old_image, $dest_x, $dest_y, 0, 0, $new_width, $new_height, $original_width, $original_height);
        $imgt($new_image, "$updir" . "$thumb_beforeword" . "$img");
		// $imgt($new_image, "$updir" . $id . '_' . "$thumb_beforeword" . "$img");
		
		//echo "$imgt $new_image, $old_image, $dest_x, $dest_y, 0, 0, $new_width, $new_height, $original_width, $original_height";
    }
}

// from http://stackoverflow.com/questions/11376315/creating-a-thumbnail-from-an-uploaded-image
function createThumbnail($filepath, $thumbpath, $thumbnail_width, $thumbnail_height, $background=false) {
    list($original_width, $original_height, $original_type) = getimagesize($filepath);
    if ($original_width > $original_height) {
        $new_width = $thumbnail_width;
        $new_height = intval($original_height * $new_width / $original_width);
    } else {
        $new_height = $thumbnail_height;
        $new_width = intval($original_width * $new_height / $original_height);
    }
    $dest_x = intval(($thumbnail_width - $new_width) / 2);
    $dest_y = intval(($thumbnail_height - $new_height) / 2);

    if ($original_type === 1) {
        $imgt = "ImageGIF";
        $imgcreatefrom = "ImageCreateFromGIF";
    } else if ($original_type === 2) {
        $imgt = "ImageJPEG";
        $imgcreatefrom = "ImageCreateFromJPEG";
    } else if ($original_type === 3) {
        $imgt = "ImagePNG";
        $imgcreatefrom = "ImageCreateFromPNG";
    } else {
        return false;
    }

    $old_image = $imgcreatefrom($filepath);
    $new_image = imagecreatetruecolor($thumbnail_width, $thumbnail_height); // creates new image, but with a black background

    // figuring out the color for the background
    if(is_array($background) && count($background) === 3) {
      list($red, $green, $blue) = $background;
      $color = imagecolorallocate($new_image, $red, $green, $blue);
      imagefill($new_image, 0, 0, $color);
    // apply transparent background only if is a png image
    } else if($background === 'transparent' && $original_type === 3) {
      imagesavealpha($new_image, TRUE);
      $color = imagecolorallocatealpha($new_image, 0, 0, 0, 127);
      imagefill($new_image, 0, 0, $color);
    }

    imagecopyresampled($new_image, $old_image, $dest_x, $dest_y, 0, 0, $new_width, $new_height, $original_width, $original_height);
    $imgt($new_image, $thumbpath);
    return file_exists($thumbpath);
}

// from https://gist.github.com/pedroppinheiro/7a039da05fd9a1bc4182
/**
 * This code is an improvement over Alex's code that can be found here -> http://stackoverflow.com/a/11376379
 * 
 * This funtion creates a thumbnail with size $thumbnail_width x $thumbnail_height.
 * It supports JPG, PNG and GIF formats. The final thumbnail tries to keep the image proportion.
 * 
 * Warnings and/or notices will also be thrown if anything fails.
 * 
 * Example of usage:
 * 
 * <code>
 * require_once 'create_thumbnail.php';
 * 
 * $success = createThumbnail(__DIR__.DIRECTORY_SEPARATOR.'image.jpg', __DIR__.DIRECTORY_SEPARATOR.'image_thumb.jpg', 60, 60, array(255,255,255)); // creates a thumbnail called image_thumb.jpg with 60x60 in size and with a white background
 * 
 * echo $success ? 'thumbnail was created' : 'something went wrong';
 * </code>
 * 
 * @author Pedro Pinheiro (https://github.com/pedroppinheiro).
 * @param string $filepath The image's complete path. Example: C:\xampp\htdocs\project\image.jpg
 * @param string $thumbpath The path to create the thumbnail + name of the thumbnail. Example: C:\xampp\htdocs\project\image_thumbnail.jpg
 * @param int $thumbnail_width Width of the thumbnail. Only integers allowed.
 * @param int $thumbnail_height Height of the thumbnail. Only integers allowed.
 * @param int[int] | 'transparent' An array containing the values of red, green, and blue to be used as the image's background color, or use the string 'transparent' to define the background as transparent (only applicable to png images). This parameter is optional, so if no value is provided, then the default background will be black.
 * @return boolean Returns true if the thumbnail was created successfully, false otherwise.
 */
function createThumbnail2($filepath, $thumbpath, $thumbnail_width, $thumbnail_height, $background=false) {
    list($original_width, $original_height, $original_type) = getimagesize($filepath);
    if ($original_width > $original_height) {
        $new_width = $thumbnail_width;
        $new_height = intval($original_height * $new_width / $original_width);
    } else {
        $new_height = $thumbnail_height;
        $new_width = intval($original_width * $new_height / $original_height);
    }
    $dest_x = 0;//intval(($thumbnail_width - $new_width) / 2);
    $dest_y = 0;//intval(($thumbnail_height - $new_height) / 2);
    if ($original_type === 1) {
        $imgt = "ImageGIF";
        $imgcreatefrom = "ImageCreateFromGIF";
    } else if ($original_type === 2) {
        $imgt = "ImageJPEG";
        $imgcreatefrom = "ImageCreateFromJPEG";
    } else if ($original_type === 3) {
        $imgt = "ImagePNG";
        $imgcreatefrom = "ImageCreateFromPNG";
    } else {
        return false;
    }
    $old_image = $imgcreatefrom($filepath);
    $new_image = imagecreatetruecolor($thumbnail_width < $new_width ? $thumbnail_width : $new_width, $new_height < $thumbnail_height ? $new_height : $thumbnail_height); // creates new image, but with a black background
    // figuring out the color for the background
    if(is_array($background) && count($background) === 3) {
      list($red, $green, $blue) = $background;
      $color = imagecolorallocate($new_image, $red, $green, $blue);
      imagefill($new_image, 0, 0, $color);
    // apply transparent background only if is a png image
    } else if($background === 'transparent' && $original_type === 3) {
      imagesavealpha($new_image, TRUE);
      $color = imagecolorallocatealpha($new_image, 0, 0, 0, 127);
      imagefill($new_image, 0, 0, $color);
    }
    imagecopyresampled($new_image, $old_image, $dest_x, $dest_y, 0, 0, $new_width, $new_height, $original_width, $original_height);
    $imgt($new_image, $thumbpath);
    return file_exists($thumbpath);
}
?>