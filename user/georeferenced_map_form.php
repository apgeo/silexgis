<!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	New Georeferenced Map form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->

<div class="modal fade" id="georeferencedMapModal" tabindex="-1" role="dialog" aria-labelledby="georeferencedMapModalTitleLabel">
  <div class="modal-dialog" role="document">
    <div class="modal-content">
      <div class="modal-header">
        <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
        <h4 class="modal-title" id="georeferencedMapModalTitleLabel">*{georeferenced_maps.title_new_georeferenced_map_form}*</h4>
		<h5><i><div id="trip_report_label"></div></i></h5>
      </div>
      <div class="modal-body">	  
        <form id="georeferencedMapForm" role="form" enctype="multipart/form-data" > <!-- class="form-inline" -->										   				 
		 <!--<input type="hidden" id="picture_id" name="picture_id" >-->
		 <!--<input type="hidden" id="point_coords_lat" name="point_coords_lat" >
		 <input type="hidden" id="point_coords_lon" name="point_coords_lon" >
		 <input type="hidden" id="picture_existing_point_id" name="picture_existing_point_id" >-->

          <div class="form-group row">
            <label for="georeferenced_map_title" class="col-sm-2 control-label">*{georeferenced_maps.title}*</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="georeferenced_map_title" name="georeferenced_map_title" />
			</div>
          </div>
		 
          <div class="form-group row">
            <label for="georeferenced_map_description" class="col-sm-2 control-label">*{georeferenced_maps.description}*</label>
			<div class="col-sm-10">
				<textarea class="form-control" id="georeferenced_map_description" name="georeferenced_map_description" ></textarea>
			</div>
          </div>		  
		 
		 <div>
		  *{georeferenced_maps.boundaries}*
          <div class="form-group row">
            <label for="georeferenced_map_boundary_north" class="col-sm-2 control-label">*{georeferenced_maps.boundary_north}*</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="georeferenced_map_boundary_north" name="georeferenced_map_boundary_north" />
			</div>
          </div>

          <div class="form-group row">
            <label for="georeferenced_map_boundary_west" class="col-sm-2 control-label">*{georeferenced_maps.boundary_west}*</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="georeferenced_map_boundary_west" name="georeferenced_map_boundary_west" />
			</div>
          </div>

          <div class="form-group row">
            <label for="georeferenced_map_boundary_south" class="col-sm-2 control-label">*{georeferenced_maps.boundary_south}*</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="georeferenced_map_boundary_south" name="georeferenced_map_boundary_south" />
			</div>
          </div>		  		 

          <div class="form-group row">
            <label for="georeferenced_map_boundary_east" class="col-sm-2 control-label">*{georeferenced_maps.boundary_east}*</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="georeferenced_map_boundary_east" name="georeferenced_map_boundary_east" />
			</div>
          </div>
		
		</div> 
		 
		  <!--<label class="btn btn-default btn-file">*{generic.browse}*<input type="file" style="display: none;" id="file" name="file" ></label>-->
		  <input id="georeferencedMapUploadControl" name="file" type="file" class="file" data-preview-file-type="image">
		  <!--<input id="fileupload" type="file" name="files[]" multiple    data-url="/path/to/upload/handler.json"    data-sequential-uploads="true"    data-form-data='{"script": "true"}'>		  -->
		  <!-- data-preview-file-type="text" -->
		<!--<div class="fileupload fileupload-new" data-provides="fileupload">
		  <div class="fileupload-preview thumbnail" style="width: 200px; height: 150px;"></div>
		  <div>
			<span class="btn btn-file"><span class="fileupload-new">Select image</span><span class="fileupload-exists">Change</span><input type="file" /></span>
			<a href="#" class="btn fileupload-exists" data-dismiss="fileupload">Remove</a>
		  </div>
		</div>
		-->
		  <div class="modal-footer">
			<button type="button" class="btn btn-default" data-dismiss="modal">*{generic.close}*</button>
			<button type="submit" class="btn btn-primary" id="savePicture" >*{generic.save}*</button>
		  </div>
	  
	    </form>
				
      </div>

    </div>
  </div>
</div>

<!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	end Georeferenced Map form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->




