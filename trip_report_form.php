<?php
?>
<!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	New Trip Report form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->

<div class="modal fade" id="tripReportModal" tabindex="-1" role="dialog" aria-labelledby="tripReportModalTitleLabel">
  <div class="modal-dialog" role="document">
    <div class="modal-content">
      <div class="modal-header">
        <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
        <h4 class="modal-title" id="tripReportModalTitleLabel">*{trip_reports.title_new_trip_report_form}*</h4>
		<h5><i><div id="trip_report_label"></div></i></h5>
      </div>
      <div class="modal-body">	  
        <form id="tripReportForm" role="form" enctype="multipart/form-data" > <!-- class="form-inline" -->										   
		
		 <input type="hidden" id="trip_log_id" name="trip_log_id" >
		 <!--<input type="hidden" id="picture_id" name="picture_id" >
		 <input type="hidden" id="point_coords_lat" name="point_coords_lat" >
		 <input type="hidden" id="point_coords_lon" name="point_coords_lon" >
		 <input type="hidden" id="picture_existing_point_id" name="picture_existing_point_id" >
		 
		 
		 <input type="hidden" id="point_string" name="point_string" >-->
		 
          <!--<div class="form-group row">
            <label for="feature_name" class="col-sm-2 control-label">Name:</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="picture_name" name="picture_name" >				
			</div>
          </div>
			-->
          <div class="form-group row">
            <label for="tripreport_place" class="col-sm-2 control-label">*{trip_reports.trip_edit_form.place}*:</label>
			<div class="input-group" class="col-sm-10">
				<input class="Typeahead-hint" type="text" tabindex="-1" readonly>
				<input type="text" class="Typeahead-input" id="tripreport_place" name="tripreport_place" placeholder="Search features" >
				<div class="Typeahead-menu"></div>
			</div>
          </div>
		  
		<div id="featuresListBox" >
			
		</div>

		  <input type="hidden" id="tripreport_features" name="tripreport_features" >
		  <!--
			<div class="input-group" id="searchFeatureControlContainer" >
				<input class="Typeahead-hint" type="text" tabindex="-1" readonly>
				<input class="Typeahead-input" type="text" id="searchFeatureControl" placeholder="Search features" >
				<div class="Typeahead-menu"></div>
			</div>		
			-->
			<!-- class="typeahead" -->
          <div class="form-group row">
            <label for="tripreport_details" class="col-sm-2 control-label">*{trip_reports.trip_edit_form.details}*:</label>
			<div class="col-sm-10">
				<textarea class="form-control" id="tripreport_details" name="tripreport_details" ></textarea>
			</div>
          </div>

		  <div class="form-group row">
			<label for="tripreport_members" class="col-sm-2 control-label">*{trip_reports.trip_edit_form.participants}*:</label>
			<div class="col-sm-10">
			<!--<div class="membersTagsControl">-->
				<input class="form-control" type="text" id="tripreport_members" name="tripreport_members" />
				<!--<textarea class="form-control" id="tripreport_members" name="tripreport_members" ></textarea>-->
			</div>
		  </div>
		  
		  <!--<div class="form-group row">
			<label for="tripreport_summary" class="col-sm-2 control-label">Summary:</label>
			<div class="col-sm-10">			
				<input class="form-control" type="text" id="tripreport_summary" name="tripreport_summary" />				
			</div>
		  </div>
		  -->

		  	<!--
				<div class="col-sm-10">
					<textarea class="form-control" id="tripreport_start_time" name="tripreport_start_time" ></textarea>
				</div>	
			-->

    <div class="form-group row">
		<label for="tripreport_start_time" class="col-sm-2 control-label">*{trip_reports.trip_edit_form.start}*:</label>
			<div class='input-group date col-sm-10' id='tripreportStartTimeControl'>
                    <input type='text' class="form-control" id='tripreport_start_time' name="tripreport_start_time" />
                    <span class="input-group-addon">
                        <span class="glyphicon glyphicon-calendar"></span>
                    </span>
            </div>
    </div>

    <div class="form-group row">
		<label for="tripreport_end_time" class="col-sm-2 control-label">*{trip_reports.trip_edit_form.end}*:</label>
			<div class='input-group date col-sm-10' id='tripreportEndTimeControl'>
                    <input type='text' class="form-control" id='tripreport_end_time' name="tripreport_end_time" />
                    <span class="input-group-addon">
                        <span class="glyphicon glyphicon-calendar"></span>
                    </span>
            </div>
    </div>
	
			<table id="trip_report_files_table" class="display" cellspacing="0" width="100%">
				<thead>
					<tr>
						<th>*{generic.file_table.name}*</th>
						<th>*{generic.file_table.size}*</th>
						<th>*{generic.file_table.add_time}*</th>
						<th>*{generic.file_table.category}*</th>
					</tr>
				</thead>
			</table>
		  
		  <button class="btn btn-primary" id="addFilesToTripReport" >*{generic.add_files}*</button>
		  
		  <div class="modal-footer">
			<button type="button" class="btn btn-default" data-dismiss="modal">*{generic.close}*</button>
			<button type="submit" class="btn btn-primary" id="saveTripReport" >*{generic.save}*</button>
		  </div>
	  
	    </form>
      </div>

    </div>
  </div>
</div>
<!--
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ	
	End New Trip Report form
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ
-->

<!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	Begin Upload files form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->
<div class="modal fade modal-child" id="uploadFilesModal" tabindex="-1" role="dialog" aria-labelledby="uploadFilesModalTitleLabel" data-modal-parent="#tripReportModal">
  <div class="modal-dialog" role="document">
    <div class="modal-content">
      <div class="modal-header">
        <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
        <h4 class="modal-title" id="uploadFilesModalTitleLabel">*{generic.add_files}*</h4>
		<h5><i><div id="upload_files_x_label"></div></i></h5>
      </div>
      <div class="modal-body">	  
        <!--<form id="uploadFilesForm" role="form" >
		-->
		
    <form class="fileupload" id="fileupload_cave" action="../data/uploader/index.php" method="POST" enctype="multipart/form-data"> <!-- id="fileupload" -->
	<!--action="./data/uploader/index.php" -->
	<!--<form class="fileupload" id="fileupload_cave" action="./data/uploader/index.php" method="POST" enctype="multipart/form-data"> <!-- id="fileupload" -->
	
        <!-- The fileupload-buttonbar contains buttons to add/delete files and start/cancel the upload -->
       <div class="row fileupload-buttonbar">
            <div class="col-lg-7">
				<input type="hidden" id="fileupload_target_type" name="fileupload_target_type" >
				<input type="hidden" id="fileupload_target_object_id" name="fileupload_target_object_id" >
                <!-- The fileinput-button span is used to style the file input field as button -->
                <span class="btn btn-success fileinput-button">
                    <i class="icon-plus icon-white"></i>
                    <span>*{generic.add_files}*</span>
                    <input type="file" name="files[]" multiple>
                </span>
                <button type="submit" class="btn btn-primary start">
                    <i class="icon-upload icon-white"></i>
                    <span>*{generic.start_upload}*</span>
                </button>
                <button type="reset" class="btn btn-warning cancel">
                    <i class="icon-ban-circle icon-white"></i>
                    <span>*{generic.end_upload}*</span>
                </button>
                <button type="button" class="btn btn-danger delete">
                    <i class="icon-trash icon-white"></i>
                    <span>*{generic.del}*</span>
                </button>
                <input type="checkbox" class="toggle">
                <!-- The global file processing state -->
                <span class="fileupload-process"></span>
            </div>
            <!-- The global progress state -->
            <div class="col-lg-5 fileupload-progress">
                <!-- The global progress bar -->
                <div class="progress progress-striped active" role="progressbar" aria-valuemin="0" aria-valuemax="100">
                    <div class="progress-bar progress-bar-success" style="width:0%;"></div>
                </div>
                <!-- The extended global progress state -->
                <div class="progress-extended">&nbsp;</div>
            </div>
        </div>
        <!-- The table listing the files available for upload/download -->
        <table role="presentation" class="table table-striped"><tbody class="files"></tbody></table>

		<input type="hidden" id="upload_file_cave_id" name="upload_file_cave_id" >
<!--
<label class="title">
    <span>Title:</span><br>
    <input name="title[]" class="form-control">
</label>
<label class="description">
    <span>Description:</span><br>
    <input name="description[]" class="form-control">
</label>

<p class="title"><strong>{%=file.title||''%}</strong></p>
<p class="description">{%=file.description||''%}</p>
-->
		
    </form>
<!-- The template to display files available for upload -->
<script id="template-upload" type="text/x-tmpl">
{% for (var i=0, file; file=o.files[i]; i++) { %}
    <tr class="template-upload">
        <td>
            <span class="preview"></span>
        </td>
        <td>
            <p class="name">{%=file.name%}</p>
            <strong class="error text-danger"></strong>
        </td>
        <td>
            <p class="size">*{generic.processing}*</p>
            <div class="progress progress-striped active" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0"><div class="progress-bar progress-bar-success" style="width:0%;"></div></div>
        </td>
        <td>
            {% if (!i && !o.options.autoUpload) { %}
                <button class="btn btn-primary start" disabled>
                    <i class="glyphicon glyphicon-upload"></i>
                    <span>*{generic.start_upload}*</span>
                </button>
            {% } %}
            {% if (!i) { %}
                <button class="btn btn-warning cancel">
                    <i class="glyphicon glyphicon-ban-circle"></i>
                    <span>*{generic.cancel}*</span>
                </button>
            {% } %}
        </td>
    </tr>
{% } %}
</script>
<!-- The template to display files available for download -->

<script id="template-download" type="text/x-tmpl">
{% for (var i=0, file; file=o.files[i]; i++) { %}
    <tr class="template-download">
        <td>
            <span class="preview">
                {% if (file.thumbnailUrl) { %}
                    <a href="{%=file.url%}" title="{%=file.name%}" download="{%=file.name%}" data-gallery><img src="{%=file.thumbnailUrl%}"></a>
                {% } %}
            </span>
        </td>
        <td>
            <p class="name">
                {% if (file.url) { %}
                    <a href="{%=file.url%}" title="{%=file.name%}" download="{%=file.name%}" {%=file.thumbnailUrl?'data-gallery':''%}>{%=file.name%}</a>
                {% } else { %}
                    <span>{%=file.name%}</span>
                {% } %}
            </p>
            {% if (file.error) { %}
                <div><span class="label label-danger">*{generic.error}*</span> {%=file.error%}</div>
            {% } %}
        </td>
        <td>
            <span class="size">{%=o.formatFileSize(file.size)%}</span>
        </td>
        <td>
            {% if (file.deleteUrl) { %}
                <button class="btn btn-danger delete" data-type="{%=file.deleteType%}" data-url="{%=file.deleteUrl%}"{% if (file.deleteWithCredentials) { %} data-xhr-fields='{"withCredentials":true}'{% } %}>
                    <i class="glyphicon glyphicon-trash"></i>
                    <span>*{generic.del}*</span>
                </button>
                <input type="checkbox" name="delete" value="1" class="toggle">
            {% } else { %}
                <button class="btn btn-warning cancel">
                    <i class="glyphicon glyphicon-ban-circle"></i>
                    <span>*{generic.cancel}*</span>
                </button>
            {% } %}
        </td>
    </tr>
{% } %}
</script>
		 
	<div class="modal-footer">
        <button type="button" class="btn btn-default" data-dismiss="modal">*{generic.close}*</button>
        <button type="submit" class="btn btn-primary" id="saveFileUpload" >*{generic.save}*</button>
      </div>	  
	  
	  
	        <!--</form>
			-->
      </div>

    </div>
  </div>
</div>

<!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	end Upload files form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->




