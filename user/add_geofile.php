<?php
    include_once("grid_common.php");	
?>
<form action="<?=WEBROOT?>/data/saveGPSFileData.php" method="post" enctype="multipart/form-data">
    <b><h3>*{add_geofile.page_title}*</h3></b>
	<br/>
	*{add_geofile.description_text}*
	<br/>
		
		<!-- for localization: http://stackoverflow.com/questions/686905/labeling-file-upload-button -->
		<input type="file" name="file" id="fileUploadControl">
		
		<br/><br/>
		
		<input type="submit" value="*{add_geofile.upload_file}*" name="submit">	
		
		<br/><br/>
		<a href="<?=WEBROOT?>/user/geofiles.php">*{generic.back}*</a>
</form>