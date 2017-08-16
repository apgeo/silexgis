<?php
/*
 * jQuery File Upload Plugin PHP Example for S3
 * https://github.com/blueimp/jQuery-File-Upload
 *
 * Copyright 2012, Roberto Colonello
 * http://www.parsec.it
 *
 * Licensed under the MIT license:
 * http://www.opensource.org/licenses/MIT
 */

error_reporting(E_ALL | E_STRICT);

require('awssdk.php');
$bucket = "YOUR BUCKET NAME";
$subFolder = "";  // leave blank for upload into the bucket directly


header('Pragma: no-cache');
header('Cache-Control: no-store, no-cache, must-revalidate');
header('Content-Disposition: inline; filename="files.json"');
header('X-Content-Type-Options: nosniff');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: OPTIONS, HEAD, GET, POST, PUT, DELETE');
header('Access-Control-Allow-Headers: X-File-Name, X-File-Type, X-File-Size');


switch ($_SERVER['REQUEST_METHOD']) {
    case 'OPTIONS':
        break;
    case 'HEAD':
    case 'GET':
       $files=array("files"=>getListOfContents($bucket, $subFolder));
        echo json_encode($files);
        break;
    case 'POST':
        if (isset($_REQUEST['_method']) && $_REQUEST['_method'] === 'DELETE') {
            $files=array("files"=>deleteObject($bucket, $subFolder));
			echo json_encode($files);
        } else {
			$files=array("files"=>uploadFiles($bucket, $subFolder));
        	echo json_encode($files);
        }
        break;
    case 'DELETE':
         echo json_encode(deleteFiles($bucket, $subFolder));
        break;
    default:
        header('HTTP/1.1 405 Method Not Allowed');
}