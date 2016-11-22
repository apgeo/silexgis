<?php
$root = realpath($_SERVER["DOCUMENT_ROOT"])."/speogis";
include_once("$root/geoPHP/geoPHP.inc");

class DbUtils
{    

static function x($z)    {    }		

static function point_to_json($lat, $long) 
// function wkb_to_json($wkb)	
{			
// open layers wants the returned objects to have the coordinates as long, lat -> so custom formatting needs to be done before feeding geoPHP		
// note that this is probably the wrong format to use in geoPHP or other libraries because for processing of the custom long lat order		
//$geom = geoPHP::load("POINT($lat $long)"); 
// $geom = geoPHP::load($wkb,'wkb');		 
$geom = geoPHP::load("POINT($long $lat)"); 
// $geom = geoPHP::load($wkb,'wkb');		
//$geom_coord_order = 1;		
return $geom->out('json');	
}	

static function wkt_to_json($wkt)	
{			
if (empty($wkt) || $wkt == "NULL")			return "";					
// open layers wants the returned objects to have the coordinates as long, lat -> so custom formatting needs to be done before feeding geoPHP		
// note that this is probably the wrong format to use in geoPHP or other libraries because for processing of the custom long lat order		
//$geom = geoPHP::load("POINT($lat $long)"); 
// $geom = geoPHP::load($wkb,'wkb');		
//var_dump($wkb);				
$geom = geoPHP::load($wkt,'wkt');		
//$geom_coord_order = 1;		
return $geom->out('json');	
}	

static function wkt_to_wkb($wkt)	
{			
if (empty($wkt) || $wkt == "NULL")			
	return null;					
// open layers wants the returned objects to have the coordinates as long, lat -> so custom formatting needs to be done before feeding geoPHP		
// note that this is probably the wrong format to use in geoPHP or other libraries because for processing of the custom long lat order		
//$geom = geoPHP::load("POINT($lat $long)"); 
// $geom = geoPHP::load($wkb,'wkb');		
//var_dump($wkb);				
$geom = geoPHP::load($wkt,'wkt');		
//$geom_coord_order = 1;				
return $geom->out('wkb');	
}	

static function parse_wkb($wkb)	
{		
//var_dump($wkb);		
if (empty($wkb) || $wkb == "NULL")			
	return null;						

$geometry = unpack('Lpadding/corder/Lgtype/dlatitude/dlongitude', $wkb);								
if ($geometry['gtype'] == 1) {				
$latitude = $geometry['latitude'];				
$longitude = $geometry['longitude'];															
return (object) [				'latitude' => $latitude,				'longitude' => $longitude				];			
}			else				throw new Exception("not supported geometry type for wkb parsing");	
}	
static function json_to_wkt($json) {		
$geom = geoPHP::load($json,'json');		
return $geom->out('wkt');	
}	
// https://www.w3.org/Protocols/rfc2616/rfc2616-sec10.html	
// https://en.wikipedia.org/wiki/List_of_HTTP_status_codes	

static function raise_error($errorText)
{		
	echo "400"; 
	//?		
	echo "\r\n".$errorText;
	exit;	
}

static function _getMessage($code) 	
{        
	switch ($code) {             case UPLOAD_ERR_INI_SIZE:                 $message = "The uploaded file exceeds the upload_max_filesize directive in php.ini";                 break;             case UPLOAD_ERR_FORM_SIZE:                 $message = "The uploaded file exceeds the MAX_FILE_SIZE directive that was specified in the HTML form";                 break;             case UPLOAD_ERR_PARTIAL:                 $message = "The uploaded file was only partially uploaded";                 break;             case UPLOAD_ERR_NO_FILE:                 $message = "No file was uploaded";                 break;             case UPLOAD_ERR_NO_TMP_DIR:                 $message = "Missing a temporary folder";                 break;             case UPLOAD_ERR_CANT_WRITE:                 $message = "Failed to write file to disk";                 break;             case UPLOAD_ERR_EXTENSION:                 $message = "File upload stopped by extension";                 break;             default:                 $message = "Unknown upload error";                 break;         }         return $message; 	}	
/*	static function ____getFilePath($file_name, $file_path)	{				$file_path = $file_path . $file_name;				return $file_path;	}*/		
static function get_last_inserted_id()	
{			
	$con = Propel::getConnection("speogis");				
	$get_id_query = "SELECT last_insert_id() as last_insert_id";		
	$get_id_stmt = $con->prepare($get_id_query);		
	$gires = $get_id_stmt->execute();		
	$last_insert_id_obj = $get_id_stmt->fetch(PDO::FETCH_OBJ);				
	
	return $last_insert_id_obj->last_insert_id;
	//var_dump($last_insert_id_obj->last_insert_id);
}



    static function mime_content_type_ex($filename) {

        $mime_types = array(

            'txt' => 'text/plain',
            'htm' => 'text/html',
            'html' => 'text/html',
            'php' => 'text/html',
            'css' => 'text/css',
            'js' => 'application/javascript',
            'json' => 'application/json',
            'xml' => 'application/xml',
            'swf' => 'application/x-shockwave-flash',
            'flv' => 'video/x-flv',

            // images
            'png' => 'image/png',
            'jpe' => 'image/jpeg',
            'jpeg' => 'image/jpeg',
            'jpg' => 'image/jpeg',
            'gif' => 'image/gif',
            'bmp' => 'image/bmp',
            'ico' => 'image/vnd.microsoft.icon',
            'tiff' => 'image/tiff',
            'tif' => 'image/tiff',
            'svg' => 'image/svg+xml',
            'svgz' => 'image/svg+xml',

            // archives
            'zip' => 'application/zip',
            'rar' => 'application/x-rar-compressed',
            'exe' => 'application/x-msdownload',
            'msi' => 'application/x-msdownload',
            'cab' => 'application/vnd.ms-cab-compressed',

            // audio/video
            'mp3' => 'audio/mpeg',
            'qt' => 'video/quicktime',
            'mov' => 'video/quicktime',

            // adobe
            'pdf' => 'application/pdf',
            'psd' => 'image/vnd.adobe.photoshop',
            'ai' => 'application/postscript',
            'eps' => 'application/postscript',
            'ps' => 'application/postscript',

            // ms office
            'doc' => 'application/msword',
            'rtf' => 'application/rtf',
            'xls' => 'application/vnd.ms-excel',
            'ppt' => 'application/vnd.ms-powerpoint',

            // open office
            'odt' => 'application/vnd.oasis.opendocument.text',
            'ods' => 'application/vnd.oasis.opendocument.spreadsheet',
        );

        $ext = strtolower(array_pop(explode('.',$filename)));
        if (array_key_exists($ext, $mime_types)) {
            return $mime_types[$ext];
        }
        else
		if (function_exists('finfo_open')) {
            $finfo = finfo_open(FILEINFO_MIME);
            $mimetype = finfo_file($finfo, $filename);
            finfo_close($finfo);
            return $mimetype;
        }
        else {
            return 'application/octet-stream';
        }
    }
	
	static function checkParameter($parameter, $type, $error_message)
	{	
		if ($type === "string")
		{
			if (empty($parameter) || ctype_space($parameter))  // might need trim($caveData->cave_name) if not done on client side?
			{
				self::raise_error($error_message); // DbUtils::
				return false;
			}
		}
		else
		if ($type === "int")
		{
			if (empty($parameter))  // might need trim($caveData->cave_name) if not done on client side?
			{
				self::raise_error($error_message); // DbUtils::
				return false;
			}
		}
		else
			throw new Exception("Parameter type is not supported.");		
			
		return true;
	}	
}

class GeoUtils
{

    static function read_picture_geolocation_data($picture_file_path) 
	{	
		$exif_data = exif_read_data($picture_file_path, 0, TRUE);
		
		//var_dump($exif_data);
		
		$lon = GeoUtils::getGps($exif_data["GPS"]["GPSLongitude"], $exif_data["GPS"]['GPSLongitudeRef']);
		$lat = GeoUtils::getGps($exif_data["GPS"]["GPSLatitude"], $exif_data["GPS"]['GPSLatitudeRef']);
		//$lon = GeoUtils::getGps($exif_data["GPSLongitude"]);
		//$lat = GeoUtils::getGps($exif_data["GPSLatitude"]);
		//$lon = self::getGps($exif_data["GPSLongitude"], $exif_data['GPSLongitudeRef']);
		//$lat = getGps($exif["GPSLatitude"], $exif['GPSLatitudeRef']);
		//var_dump($lat, $lon);
		
		return [$lat, $lon];
		//GeoUtils::getGps();
	}

////////////
// http://stackoverflow.com/a/2526412/1225421
	
//Pass in GPS.GPSLatitude or GPS.GPSLongitude or something in that format
//-- add elevation extraction
static function getGps__v1($exifCoord)
{
  $degrees = count($exifCoord) > 0 ? self::gps2Num($exifCoord[0]) : 0;
  $minutes = count($exifCoord) > 1 ? self::gps2Num($exifCoord[1]) : 0;
  $seconds = count($exifCoord) > 2 ? self::gps2Num($exifCoord[2]) : 0;

  //normalize
  $minutes += 60 * ($degrees - floor($degrees));
  $degrees = floor($degrees);

  $seconds += 60 * ($minutes - floor($minutes));
  $minutes = floor($minutes);

  //extra normalization, probably not necessary unless you get weird data
  if($seconds >= 60)
  {
    $minutes += floor($seconds/60.0);
    $seconds -= 60*floor($seconds/60.0);
  }

  if($minutes >= 60)
  {
    $degrees += floor($minutes/60.0);
    $minutes -= 60*floor($minutes/60.0);
  }

  return array('degrees' => $degrees, 'minutes' => $minutes, 'seconds' => $seconds);
}

static function gps2Num($coordPart)
{
  $parts = explode('/', $coordPart);

  if(count($parts) <= 0)// jic
    return 0;
  if(count($parts) == 1)
    return $parts[0];

  return floatval($parts[0]) / floatval($parts[1]);
}	

function getGps($exifCoord, $hemi) {

	$degrees = count($exifCoord) > 0 ? self::gps2Num($exifCoord[0]) : 0;
	$minutes = count($exifCoord) > 1 ? self::gps2Num($exifCoord[1]) : 0;
	$seconds = count($exifCoord) > 2 ? self::gps2Num($exifCoord[2]) : 0;

    $flip = ($hemi == 'W' or $hemi == 'S') ? -1 : 1;

    return $flip * ($degrees + $minutes / 60 + $seconds / 3600);

}

// end
//////////////////
}
?>