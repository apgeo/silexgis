<?php
	include_once 'db_common.php';
	
	$picture_id = $_GET["picture_id"];
	
	$image = ImagesQuery::create()->findPK($picture_id);

	echo $image->toJSON();
?>