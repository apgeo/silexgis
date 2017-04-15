<?php
    include_once 'db_common.php';

	
	$georeferencedMaps = GeoreferencedMapsQuery::create()
	->addDescendingOrderByColumn(GeoreferencedMapsPeer::ID)
	->find();

	$results = array();
	
	$user_directory = "";
	$upload_dir = ROOTPATH.'/uploads/' . 'pictures/' . $user_directory;
	
	foreach($georeferencedMaps as $grmap) 
		if ($grmap->getEnabled())
		{
			$image = ImagesQuery::create()->findPK($grmap->getImageid());
			
			if (!empty($image))
			{
				//-- store image size in the image table on image insert
				$fullFilePath = $upload_dir . $image->getFilepath();			
				$size = getimagesize($fullFilePath);
				
				$results[] = (object) [
					'id' => $grmap->getId(),
					'title' => $grmap->getTitle(),
					'description' => $grmap->getDescription(),
					'boundary_north' => $grmap->getBoundarynorth(),
					'boundary_west' => $grmap->getBoundarywest(),
					'boundary_south' => $grmap->getBoundarysouth(),
					'boundary_east' => $grmap->getBoundaryeast(),
					'file_path' => $image->getFilepath(),
					'thumb_file_path' => $image->getThumbfilepath(),
					'width' => $size[0],
					'height' => $size[1],
				];
			}
		}
		
	//echo $caveTypes->toJSON();
	echo json_encode($results);		
?>