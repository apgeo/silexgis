<?php
    include_once 'header.php';
	include_once 'main_page_logger.php';
	
	//echo "zzzz $application_url_root";
?>
    <script type="text/javascript" src="<?=$application_url_root ?>/scripts/main_map.js"></script>
    <script type="text/javascript" src="<?=$application_url_root ?>/scripts/trip_report.js"></script>
    <!--<body onload="init();">-->

    <style>
        .pictureThumbnailListContainer {
            width: 420px;
        }

        ul.pictureThumbnailListContainer {
            list-style: none outside none;
            padding-left: 0;
            margin-bottom: 0;
        }

        li.pictureThumbnailListContainer {
            display: block;
            float: left;
            margin-right: 6px;
            cursor: pointer;
        }

        img.pictureThumbnailListContainer {
            display: block;
            height: auto;
            /*height: auto;*/
            max-width: 100%;
        }
    </style>

    <div class="ui-layout-center">
        <div id="mapdiv" class="mapdiv">
            <div id="toolbox">
                <div id="layerswitcher_header" onclick="toggleMapLayerSelector();"> B</div>
                <ul id="layerswitcher" onchange="switchLayer();">
                    <li><label><input type="radio" name="layer" value="0" checked="">OSM</label></li>
                    <!--<li><label><input type="radio" name="layer" value="1"> MapQuest Satellitar</label></li>
				<li><label><input type="radio" name="layer" value="2"> MapQuest Hybrid</label></li>
				<li><label><input type="radio" name="layer" value="3"> MapQuest OSM</label></li>
				-->
                    <li><label><input type="radio" name="layer" value="1"> ESRI</label></li>
                    <li><label><input type="radio" name="layer" value="2"> *{main_map.body.layer_switcher.bing_road}*</label></li>
                    <li><label><input type="radio" name="layer" value="3"> *{main_map.body.layer_switcher.bing_aerial}*</label></li>
                    <li><label><input type="radio" name="layer" value="4"> *{main_map.body.layer_switcher.bing_labels}*</label></li>
                    <!--<li><label><input type="radio" name="layer" value="5"> x1</label></li>
				<li><label><input type="radio" name="layer" value="6"> x2</label></li>
				<li><label><input type="radio" name="layer" value="7"> x3</label></li>
				<li><label><input type="radio" name="layer" value="8"> x4</label></li>-->
                    <!--<li><label><input type="radio" name="layer" value="8"> OpenLayers local</label></li>		-->
                    <!--<li><label><input type="checkbox" name="layer" value="99"> Harta gelogica 1970</label></li>		-->
                </ul>
            </div>

            <div id="measurementsBox">
                <form class="form-inline">
                    <label><input type="checkbox" onchange="" id="measurementCheckBox" value="99"/>*{main_map.body.measurements_box.distances}*</label>
                    <label>*{main_map.body.measurements_box.type}*:&nbsp;</label>
                    <label>
			<select id="measure_type" >
			  <option value="length">*{main_map.body.measurements_box.opt_distance}*</option>
			  <option value="area">*{main_map.body.measurements_box.opt_area}*</option>
			</select>		
			</label>
                    <br/>
                    <!-- <label><input  type="checkbox" id="geodesic" checked="true" />*{main_map.body.measurements_box.opt_geodesic_measurement}*</label> -->
                    <!-- <label class="checkbox"-->
                </form>
            </div>

            <div id="search_popup" class="ol-popup">
                <a href="#" id="search-popup-closer" class="ol-popup-closer"></a>
                <div id="search-popup-content"></div>
            </div>
        </div>
    </div>
    <!--<div class="ui-layout-north">North</div>-->
    <div class="ui-layout-south">
        <div class="pictureThumbnailListContainer">
            <ul id="mapPicturesLightSlider">
            </ul>
        </div>
        <!--
	<div>
		<label><input type="checkbox" onchange="" id="hgPersaniCentruCheckBox" value="false"/><a href='./assets/layer_images/persani_comana_geologica.jpg' target='_blank' >Harta geologica 1970 - persani centru</a></label>
		<div id="slider-id"><div class="ui-slider-handle">X</div></div>
	</div>
-->
        <!--<a href='./assets/layer_images/persani_comana_geologica.jpg' target='_blank' >geologica persani centru</a>-->
    </div>
    <div class="ui-layout-east">
        <b>*{main_map.body.map_views.title}*:</b><br/>
        <div id="mapViewsControlBox"></div>
        <br/>
        <div><input type="checkbox" id="caveFeaturesEditingCheckBox" value="false" />*{main_map.body.features_panel.cave_features_editing_checkbox}*</div>
        <!--<div id="drawControlBox" >
	<b>Features:</b><br/>

	</div>
	-->
        <b>*{main_map.body.features_panel.title}*:</b>
        <div id="drawFeaturesTreeControl">
            <ul id="drawFeaturesTreeControl_root">
            </ul>
        </div>
    </div>
    <div class="ui-layout-west">
        <div class="layerSwitcher"><b>*{main_map.body.layer_switcher.title}*</b></div>
        <div class="options">
            <!--<h2>Options:</h2>-->
            <!--<input id="dils" type="checkbox" checked="checked" onchange="displayInLayerSwitcher(this.checked);"/>
		<label for="dils">display "Pirate Map" in LayerSwitcher (zoom out to make it visible).</label>
		<br/>-->
            <input id="opb" type="checkbox" onchange="$('body').toggleClass('hideOpacity');" />
            <label for="opb">*{main_map.body.layer_switcher.opacity_bar_checkbox}*</label>
        </div>
    </div>

    <div id="pointInfoBox">
        <div id="info" <!--class="alert alert-success" -->>

        </div>
    </div>

    <!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	New cave form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->

    <div class="modal fade" id="caveModal" tabindex="-1" role="dialog" aria-labelledby="caveModalTitleLabel">
        <div class="modal-dialog modal-lg" role="document">
            <div class="modal-content">
                <div class="modal-header">
                    <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
                    <h4 class="modal-title" id="caveModalTitleLabel">*{main_map.cave_edit_form.title_new}*</h4>
                    <h5><i><div id="cave_coords_label"></div></i></h5>
                </div>
                <div class="modal-body">
                    <form id="caveForm" role="form">
                        <!-- class="form-inline" -->

                        <input type="hidden" id="cave_id" name="cave_id">
                        <!-- <input type="hidden" id="cave_coords_lat" name="cave_coords_lat">
                        <input type="hidden" id="cave_coords_lon" name="cave_coords_lon"> -->
                        <input type="hidden" id="entrance_existing_point_id" name="entrance_existing_point_id">

                        <input type="hidden" id="ce_feature_string" name="ce_feature_string">

                        <ul class="nav nav-tabs" role="tablist">
                            <li role="presentation" class="active"><a href="#main" aria-controls="main" role="tab" data-toggle="tab">*{main_map.cave_edit_form.tab_identification}*</a></li>
                            <li role="presentation"><a href="#geology" aria-controls="geology" role="tab" data-toggle="tab">*{main_map.cave_edit_form.tab_geology}*</a></li>
                            <li role="presentation"><a href="#location" aria-controls="location" role="tab" data-toggle="tab">*{main_map.cave_edit_form.tab_location}*</a></li>
                            <li role="presentation"><a href="#topometry" aria-controls="topometry" role="tab" data-toggle="tab">*{main_map.cave_edit_form.tab_topometry}*</a></li>
                            <li role="presentation"><a href="#other" aria-controls="other" role="tab" data-toggle="tab">*{main_map.cave_edit_form.tab_other}*</a></li>
                            <li role="presentation"><a href="#entrances" aria-controls="entrances" role="tab" data-toggle="tab">*{main_map.cave_edit_form.tab_entrances}*</a></li>
                            <li role="presentation"><a href="#files" aria-controls="files" role="tab" data-toggle="tab">*{main_map.cave_edit_form.tab_files}*</a></li>
                            <li role="presentation"><a href="#pictures" aria-controls="pictures" role="tab" data-toggle="tab">*{main_map.cave_edit_form.tab_pictures}*</a></li>
                        </ul>

                        <!-- Tab panes -->
                        <div class="tab-content">
                            <div role="tabpanel" class="tab-pane active" id="main">

                                <br/>

                                <div class="form-group row">
                                    <label for="cave_coords_lat" class="col-sm-2 control-label">Lat:</label>
                                    <div class="col-sm-4">
                                        <input type='number' size='9' step="any" class="form-control" id="cave_coords_lat" name="cave_coords_lat">
                                    </div>
                                    <label for="cave_coords_lon" class="col-sm-2 control-label">Long:</label>
                                    <div class="col-sm-4">
                                        <input type='number' size='9' step="any" class="form-control" id="cave_coords_lon" name="cave_coords_lon">
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_name" class="col-sm-4 control-label">*{main_map.cave_edit_form.name}*:</label>
                                    <div class="col-sm-8">
                                        <input type="text" class="form-control" id="cf_name" name="cf_name">
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_other_toponyms" class="col-sm-4 control-label">*{main_map.cave_edit_form.other_toponyms}*:</label>
                                    <div class="col-sm-8">
                                        <textarea class="form-control" id="cf_other_toponyms" name="cf_other_toponyms"></textarea>
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_identification_code" class="col-sm-4 control-label">*{main_map.cave_edit_form.identification_code}*:</label>
                                    <div class="col-sm-8">
                                        <input type="text" class="form-control" id="cf_identification_code" name="cf_identification_code"></textarea>
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_cave_type" class="col-sm-4 control-label">*{main_map.cave_edit_form.type}*:</label>
                                    <div class="col-sm-8">
                                        <select id="cf_cave_type" class="selectpicker form-control" name="cf_cave_type" data-hide-disabled="true" data-max-options="1">
			                            </select>
                                        <!-- multiple data-size="5"  -->
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_description" class="col-sm-4 control-label">*{main_map.cave_edit_form.description}*:</label>
                                    <div class="col-sm-8">
                                        <textarea class="form-control" id="cf_description" name="cf_description"></textarea>
                                    </div>
                                </div>

                            </div>


                            <div role="tabpanel" class="tab-pane" id="geology">
                                <br/>

                                <div class="form-group row">
                                    <label for="cf_rock_type_id" class="col-sm-4 control-label">*{main_map.cave_edit_form.rock_type}*:</label>
                                    <div class="col-sm-4">
                                        <select id="cf_rock_type_id" class="selectpicker form-control" name="cf_rock_type_id" data-hide-disabled="true" data-max-options="1">
                                            <option value="unknown" selected>unknown</option>                        
                                            <option value="limestone">limestone</option>
                                            <option value="gypsum">gypsum</option>
                                            <option value="dolomite">dolomite</option>
                                            <option value="crystalline_schist">crystalline_schist</option>
                                            <option value="ice">ice</option>
                                            <option value="lava">lava</option>
                                            <option value="chalk">chalk</option>
                                            <option value="other">other</option>                
                                        </select>
                                        <!-- multiple data-size="5"  -->
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_rock_age" class="col-sm-4 control-label">*{main_map.cave_edit_form.rock_age}*:</label>
                                    <div class="col-sm-4">
                                        <!--<textarea class="form-control" id="cf_rock_age" name="cf_rock_age" ></textarea>-->
                                        <input class="form-control" id="cf_rock_age" name="cf_rock_age" type='number' size='5' />
                                    </div>
                                    <label class="col-sm-4 control-label">*{measurement_units.years_short}*</label>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_cave_age" class="col-sm-4 control-label">*{main_map.cave_edit_form.cave_age}*:</label>
                                    <div class="col-sm-4">
                                        <input class="form-control" id="cf_cave_age" name="cf_cave_age" type='number' size='5' />
                                    </div>
                                    <label class="col-sm-4 control-label">*{measurement_units.years_short}*</label>
                                </div>
                            </div>


                            <div role="tabpanel" class="tab-pane" id="location">
                                <br/>

                                <div class="form-group row">
                                    <label for="cf_region" class="col-sm-4 control-label">*{main_map.cave_edit_form.region}*:</label>
                                    <div class="col-sm-8">
                                        <textarea class="form-control" id="cf_region" name="cf_region"></textarea>
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_hydrographic_basin" class="col-sm-4 control-label">*{main_map.cave_edit_form.catchement_basin}*:</label>
                                    <div class="col-sm-8">
                                        <textarea class="form-control" id="cf_hydrographic_basin" name="cf_hydrographic_basin"></textarea>
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_valley" class="col-sm-4 control-label">*{main_map.cave_edit_form.valley}*:</label>
                                    <div class="col-sm-8">
                                        <textarea class="form-control" id="cf_valley" name="cf_valley"></textarea>
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_tributary_river" class="col-sm-4 control-label">*{main_map.cave_edit_form.tributary_river}*:</label>
                                    <div class="col-sm-8">
                                        <textarea class="form-control" id="cf_tributary_river" name="cf_tributary_river"></textarea>
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_closest_address" class="col-sm-4 control-label">*{main_map.cave_edit_form.closest_address}*:</label>
                                    <div class="col-sm-8">
                                        <textarea class="form-control" id="cf_closest_address" name="cf_closest_address"></textarea>
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_land_registry_number" class="col-sm-4 control-label">*{main_map.cave_edit_form.land_registry_number}*:</label>
                                    <div class="col-sm-8">
                                        <textarea class="form-control" id="cf_land_registry_number" name="cf_land_registry_number"></textarea>
                                    </div>
                                </div>

                            </div>


                            <div role="tabpanel" class="tab-pane" id="topometry">
                                <br/>

                                <div class="form-group row">
                                    <label for="cf_depth" class="col-sm-4 control-label">*{main_map.cave_edit_form.depth}*:</label>
                                    <div class="col-sm-4">
                                        <input class="form-control" id="cf_depth" name="cf_depth" type='number' size='5' />
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_positive_depth" class="col-sm-4 control-label">*{main_map.cave_edit_form.positive_depth}*:</label>
                                    <div class="col-sm-4">
                                        <input class="form-control" id="cf_positive_depth" name="cf_positive_depth" type='number' size='5' />
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_negative_depth" class="col-sm-4 control-label">*{main_map.cave_edit_form.negative_depth}*:</label>
                                    <div class="col-sm-4">
                                        <input class="form-control" id="cf_negative_depth" name="cf_negative_depth" type='number' size='5' />
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_potential_depth" class="col-sm-4 control-label">*{main_map.cave_edit_form.potential_depth}*:</label>
                                    <div class="col-sm-4">
                                        <input class="form-control" id="cf_potential_depth" name="cf_potential_depth" type='number' size='5' />
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_surveyed_length" class="col-sm-4 control-label">*{main_map.cave_edit_form.surveyed_length}*:</label>
                                    <div class="col-sm-4">
                                        <input class="form-control" id="cf_surveyed_length" name="cf_surveyed_length" type='number' size='5' />
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_real_extension" class="col-sm-4 control-label">*{main_map.cave_edit_form.real_extension}*:</label>
                                    <div class="col-sm-4">
                                        <input class="form-control" id="cf_real_extension" name="cf_real_extension" type='number' size='5' />
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_projected_extension" class="col-sm-4 control-label">*{main_map.cave_edit_form.projected_extension}*:</label>
                                    <div class="col-sm-4">
                                        <input class="form-control" id="cf_projected_extension" name="cf_projected_extension" type='number' size='5' />
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_area" class="col-sm-4 control-label">*{main_map.cave_edit_form.area_m2}*:</label>
                                    <div class="col-sm-4">
                                        <input class="form-control" id="cf_area" name="cf_area" type='number' size='5' />
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_volume" class="col-sm-4 control-label">*{main_map.cave_edit_form.volume_m3}*:</label>
                                    <div class="col-sm-4">
                                        <input class="form-control" id="cf_volume" name="cf_volume" type='number' size='5' />
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_ramification_index" class="col-sm-4 control-label">*{main_map.cave_edit_form.ramification_index}*:</label>
                                    <div class="col-sm-4">
                                        <input class="form-control" id="cf_ramification_index" name="cf_ramification_index" type='number' size='5' />
                                    </div>
                                </div>
                            </div>


                            <div role="tabpanel" class="tab-pane" id="other">
                                <br/>

                                <div class="form-group row">
                                    <label for="cf_discovery_date" class="col-sm-4 control-label">*{main_map.cave_edit_form.discovery_date}*:</label>
                                    <div class="col-sm-8">
                                        <input class="form-control" id="cf_discovery_date" name="cf_discovery_date" />
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_discoverer" class="col-sm-4 control-label">*{main_map.cave_edit_form.discoverers}*:</label>
                                    <div class="col-sm-8">
                                        <textarea class="form-control" id="cf_discoverer" name="cf_discoverer"></textarea>
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_exploration_status" class="col-sm-4 control-label">*{main_map.cave_edit_form.exploration_status}*:</label>
                                    <div class="col-sm-8">
                                        <!-- <textarea class="form-control" id="cf_exploration_status" name="cf_exploration_status" ></textarea> -->
                                        <select id="cf_exploration_status" class="selectpicker form-control" name="cf_exploration_status" data-hide-disabled="true"
                                            data-max-options="1">
                                            <option value="Unknown" selected>Unknown</option>
                                            <option value="Not explored">Not explored</option>
                                            <option value="Partially explored">Partially explored</option>
                                            <option value="Exploration finished">Exploration finished</option>
                                        </select>
                                        <!-- multiple data-size="5"  -->
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_is_show_cave" class="col-sm-4 control-label">*{main_map.cave_edit_form.show_cave}*:</label>
                                    <div class="col-xs-1">
                                        <!-- <div id="checkbox-btn-group" class="checkbox-btn-group" data-toggle="buttons">                     -->
                                        <input class="form-control" id="cf_is_show_cave" name="cf_is_show_cave" type="checkbox" autocomplete="off" />

                                        <!-- </div> -->
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_show_cave_length" class="col-sm-4 control-label">*{main_map.cave_edit_form.show_cave_length}*:</label>
                                    <div class="col-sm-3">
                                        <input class="form-control" id="cf_show_cave_length" name="cf_show_cave_length" type='number' size='5' />
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_website" class="col-sm-4 control-label">*{main_map.cave_edit_form.website}*:</label>
                                    <div class="col-sm-8">
                                        <textarea class="form-control" id="cf_website" name="cf_website"></textarea>
                                    </div>
                                </div>

                                <div class="form-group row">
                                    <label for="cf_protection_class" class="col-sm-4 control-label">*{main_map.cave_edit_form.protection_class}*:</label>
                                    <div class="col-sm-8">
                                        <textarea class="form-control" id="cf_protection_class" name="cf_protection_class"></textarea>
                                    </div>
                                </div>
                            </div>

                            <div role="tabpanel" class="tab-pane" id="entrances">
                                <br/>

                                <table id="cave_entrances_table" class="display" cellspacing="0" width="100%">
                                    <thead>
                                        <tr>
                                            <th>Id</th>
                                            <th>*{main_map.cave_edit_form.entrance}*</th>
                                            <th>*{main_map.cave_edit_form.elevation}*</th>
                                            <th>*{main_map.cave_edit_form.view_on_map}*</th>
                                        </tr>
                                    </thead>
                                </table>

                                <!-- end tab panel -->
                            </div>


                            <!-- 
                                    XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
                                    Cave edit -> upload files tab
                                    XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
                                -->

                            <div role="tabpanel" class="tab-pane" id="files">
                                <br/>

                                <table id="cave_edit_files_table" class="display" cellspacing="0" width="100%">
                                    <thead>
                                        <tr>
                                            <th>*{generic.file_table.name}*</th>
                                            <th>*{generic.file_table.size}*</th>
                                            <th>*{generic.file_table.add_time}*</th>
                                            <th>*{generic.file_table.category}*</th>
                                        </tr>
                                    </thead>
                                </table>

                                <!-- <button class="btn" id="addFilesToCave">*{generic.add_files}*</button> -->
                                <!-- //-- link instead of button because, for undetermined cause, the button event gets called on enter in form instead of the default button -->
                                <a href="#" class="btn btn-info" role="button" id="addFilesToCave" >*{generic.add_files}*</a>


                                <!-- 
                                            XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
                                        -->

                            </div>

                            <div role="tabpanel" class="tab-pane" id="pictures">
                                <br/>

                                <!-- <button class="btn" id="addPicturesToCave">*{generic.add_files}*</button> -->
                                <a href="#" class="btn btn-info" role="button" id="addPicturesToCave" >*{generic.add_files}*</a>
                            </div>
                        </div>

                        <div class="modal-footer">
                            <button type="button" class="btn btn-default" data-dismiss="modal">*{generic.close}*</button>
                            <button type="submit" class="btn btn-primary" id="saveCave">*{generic.save}*</button>
                        </div>
                    </form>
                </div>

            </div>
        </div>
    </div>


<!-- 
                                    XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
                                    Cave edit -> upload files modal
                                    XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->
    <div class="modal fade modal-child" id="caveUploadFilesModal" tabindex="-1" role="dialog" aria-labelledby="caveUploadFilesModalTitleLabel"
        data-modal-parent="#caveModal">
        <div class="modal-dialog" role="document">
            <div class="modal-content">
                <div class="modal-header">
                    <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
                    <h4 class="modal-title" id="caveUploadFilesModalTitleLabel">*{generic.add_files}*</h4>
                    <!-- <h5><i><div id="upload_files_x_label"></div></i></h5> -->
                </div>
                <div class="modal-body">
                    <!--<form id="uploadFilesForm" role="form" >-->

                    <form class="fileupload" id="cave_files_fileupload_form" role="form" action="<?=WEBROOT?>/data/uploader/index.php" method="POST"
                        enctype="multipart/form-data">
                        <!-- id="fileupload" -->
                        <!--action="./data/uploader/index.php" -->
                        <input type="hidden" id="upload_cave_files_cave_id" name="upload_cave_files_cave_id">

                        <!-- The fileupload-buttonbar contains buttons to add/delete files and start/cancel the upload -->
                        <div class="row fileupload-buttonbar">
                            <div class="col-lg-7">
                                <input type="hidden" id="fileupload_target_type" name="fileupload_target_type">
                                <input type="hidden" id="fileupload_target_object_id" name="fileupload_target_object_id">
                                <!-- The fileinput-button span is used to style the file input field as button -->
                                <span class="btn btn-success fileinput-button">
                                                                <i class="icon-plus icon-white"></i>
                                                                <span>*{generic.add_files}*</span>
                                <input type="file" name="files[]" multiple>
                                </span>
                                <button type="submit" class="btn start"> <!-- btn-primary -->
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
                        <table role="presentation" class="table table-striped">
                            <tbody class="files"></tbody>
                        </table>

                        <input type="hidden" id="upload_file_cave_id" name="upload_file_cave_id">
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
                                    <div class="progress progress-striped active" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0">
                                        <div class="progress-bar progress-bar-success" style="width:0%;"></div>
                                    </div>
                                </td>
                                <td>
                                    {% if (!i && !o.options.autoUpload) { %}
                                    <button class="btn btn-primary start" disabled>
                                                                <i class="glyphicon glyphicon-upload"></i>
                                                                <span id="start_upload_button" >Start</span>
                                                                <!-- *{generic.start_upload}* -->
                                                            </button> {% } %} {% if (!i)
                                    { %}
                                    <button class="btn btn-warning cancel">
                                                                <i class="glyphicon glyphicon-ban-circle"></i>
                                                                <span id="cancel_upload_button">Cancel</span>
                                                                <!-- *{generic.cancel}* -->
                                                            </button> {% } %}
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
                                        <a href="{%=file.url%}" title="{%=file.name%}" download="{%=file.name%}" {%=file.thumbnailUrl? 'data-gallery': ''%}>{%=file.name%}</a>                                        {% } else { %}
                                        <span>{%=file.name%}</span> {% } %}
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
                                    <button class="btn btn-danger delete" data-type="{%=file.deleteType%}" data-url="{%=file.deleteUrl%}" {% if (file.deleteWithCredentials)
                                        { %} data-xhr-fields='{"withCredentials":true}' {% } %}>
                                                                    <i class="glyphicon glyphicon-trash"></i>
                                                                    <span>*{generic.del}*</span>
                                                                </button>
                                    <input type="checkbox" name="delete" value="1" class="toggle"> {% } else { %}
                                    <button class="btn btn-warning cancel">
                                                                <i class="glyphicon glyphicon-ban-circle"></i>
                                                                <span>*{generic.cancel}*</span>
                                                            </button> {% } %}
                                </td>
                            </tr>
                            {% } %}
                        </script>
                </div>

            </div>
        </div>
    </div>

    <!--
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ	
    -->

    <!--
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ	
	End New cave form
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ
-->


<!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	New feature form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->

    <div class="modal fade" id="featureModal" tabindex="-1" role="dialog" aria-labelledby="featureModalTitleLabel">
        <div class="modal-dialog" role="document">
            <div class="modal-content">
                <div class="modal-header">
                    <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
                    <h4 class="modal-title" id="featureModalTitleLabel">*{main_map.feature_edit_form.title_new}*</h4>
                    <h5><i><div id="feature_coords_label"></div></i></h5>
                </div>
                <div class="modal-body">
                    <form id="featureForm" role="form" >
                        
                        <input type="hidden" id="feature_id" name="feature_id">
                        <!-- <input type="hidden" id="feature_coords_lat" name="feature_coords_lat" > -->
		                <!-- <input type="hidden" id="feature_coords_lon" name="feature_coords_lon" > -->
                            

                        <input type="hidden" id="feature_existing_point_id" name="feature_existing_point_id">
                        <input type="hidden" id="feature_type_id" name="feature_type_id">

                        <input type="hidden" id="feature_string" name="feature_string">

                        <div class="form-group row">
                            <label for="feature_name" class="col-sm-4 control-label">*{main_map.feature_edit_form.name}*:</label>
                            <div class="col-sm-10">
                                <input type="text" class="form-control focusedInput" id="feature_name" name="feature_name">
                            </div>
                        </div>

                        <div class="form-group row">
                            <label for="feature_description" class="col-sm-4 control-label">*{main_map.feature_edit_form.description}*:</label>
                            <div class="col-sm-10">
                                <textarea class="form-control" id="feature_description" name="feature_description"></textarea>
                            </div>
                        </div>

                        <div class="modal-footer">
                            <button type="button" class="btn btn-default" data-dismiss="modal">*{generic.close}*</button>
                            <button type="submit" class="btn btn-primary" id="saveFeature">*{generic.save}*</button>
                        </div>

                    </form>
                </div>

            </div>
        </div>
    </div>
    <!--
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ	
	End New feature form
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ
-->


    <!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	Cave details form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->

    <div class="modal fade" id="caveDetailsModal" tabindex="-1" role="dialog" aria-labelledby="caveDetailsModalTitleLabel">
        <div class="modal-dialog" role="document">
            <div class="modal-content">
                <div class="modal-header">
                    <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
                    <h4 class="modal-title" id="caveDetailsModalTitleLabel">*{main_map.cave_details_form.title}*</h4>
                    <!--<h5><i><div id="picture_coords_label"></div></i></h5>-->
                </div>
                <div class="modal-body">
                    <!--<form id="caveDetailsForm" role="form" enctype="multipart/form-data" >
		 
		 <input type="hidden" id="cd_cave_id" name="cave_id" >		 
		 
          <div class="form-group row">
            <label class="col-sm-2 control-label">Description:</label>
          </div>		  

			

	    </form>
		-->
                    <!--<form id="fileupload1" action="speogis\data\uploader" method="POST" enctype="multipart/form-data">
    <input type="hidden" name="example1" value="test">
    <div class="row">
        <label>Example: <input type="text" name="example2"></label>
    </div>
    
	</form>
	-->

                    <ul class="nav nav-tabs" role="tablist">
                        <li role="presentation" class="active"><a href="#main" aria-controls="main" role="tab" data-toggle="tab">Identification</a></li>
                        <li role="presentation"><a href="#geology" aria-controls="geology" role="tab" data-toggle="tab">Geology</a></li>
                        <li role="presentation"><a href="#location" aria-controls="location" role="tab" data-toggle="tab">Location</a></li>
                        <li role="presentation"><a href="#topometry" aria-controls="topometry" role="tab" data-toggle="tab">Topometry</a></li>
                        <li role="presentation"><a href="#other" aria-controls="other" role="tab" data-toggle="tab">Other</a></li>
                    </ul>

                    <!-- Tab panes -->
                    <div class="tab-content">
                        <div role="tabpanel" class="tab-pane active" id="main">

                            <br/>

                            <div class="form-group row">
                                <label for="cf_other_toponyms" class="col-sm-4 control-label">*{main_map.cave_edit_form.other_toponyms}*:</label>
                                <div class="col-sm-8">
                                    <span id="cf_other_toponyms" name="cf_other_toponyms"></span>
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_identification_code" class="col-sm-4 control-label">*{main_map.cave_edit_form.identification_code}*:</label>
                                <div class="col-sm-8">
                                    <span type="text" id="cf_identification_code" name="cf_identification_code"></span>
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_cave_type" class="col-sm-4 control-label">*{main_map.cave_edit_form.type}*:</label>
                                <div class="col-sm-8">
                                    <select id="cf_cave_type" class="selectpicker form-control" name="cf_cave_type" data-hide-disabled="true" data-max-options="1">
			</select>
                                    <!-- multiple data-size="5"  -->
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_description" class="col-sm-4 control-label">*{main_map.cave_edit_form.description}*:</label>
                                <div class="col-sm-8">
                                    <span id="cf_description" name="cf_description"></span>
                                </div>
                            </div>

                        </div>


                        <div role="tabpanel" class="tab-pane" id="geology">
                            <br/>

                            <div class="form-group row">
                                <label for="cf_rock_type_id" class="col-sm-4 control-label">*{main_map.cave_edit_form.rock_type}*:</label>
                                <div class="col-sm-4">
                                    <select id="cd_rock_type_id" class="selectpicker form-control" name="cd_rock_type_id" data-hide-disabled="true" data-max-options="1">                    
                        <option value="unknown" selected>unknown</option>                        
                        <option value="limestone">limestone</option>
                        <option value="gypsum">gypsum</option>
                        <option value="dolomite">dolomite</option>
                        <option value="crystalline_schist">crystalline_schist</option>
                        <option value="ice">ice</option>
                        <option value="lava">lava</option>
                        <option value="chalk">chalk</option>
                        <option value="other">other</option>
                    </select>
                                    <!-- multiple data-size="5"  -->
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_rock_age" class="col-sm-4 control-label">*{main_map.cave_edit_form.rock_age}*:</label>
                                <div class="col-sm-4">
                                    <!--<textarea class="form-control" id="cf_rock_age" name="cf_rock_age" ></textarea>-->
                                    <input class="form-control" id="cf_rock_age" name="cf_rock_age" type='number' size='5' />
                                </div>
                                <label class="col-sm-4 control-label">*{measurement_units.years_short}*</label>
                            </div>

                            <div class="form-group row">
                                <label for="cf_cave_age" class="col-sm-4 control-label">*{main_map.cave_edit_form.cave_age}*:</label>
                                <div class="col-sm-4">
                                    <input class="form-control" id="cf_cave_age" name="cf_cave_age" type='number' size='5' />
                                </div>
                                <label class="col-sm-4 control-label">*{measurement_units.years_short}*</label>
                            </div>
                        </div>


                        <div role="tabpanel" class="tab-pane" id="location">
                            <br/>

                            <div class="form-group row">
                                <label for="cf_region" class="col-sm-4 control-label">*{main_map.cave_edit_form.region}*:</label>
                                <div class="col-sm-8">
                                    <textarea class="form-control" id="cf_region" name="cf_region"></textarea>
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_hydrographic_basin" class="col-sm-4 control-label">*{main_map.cave_edit_form.catchement_basin}*:</label>
                                <div class="col-sm-8">
                                    <textarea class="form-control" id="cf_hydrographic_basin" name="cf_hydrographic_basin"></textarea>
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_valley" class="col-sm-4 control-label">*{main_map.cave_edit_form.valley}*:</label>
                                <div class="col-sm-8">
                                    <textarea class="form-control" id="cf_valley" name="cf_valley"></textarea>
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_tributary_river" class="col-sm-4 control-label">*{main_map.cave_edit_form.tributary_river}*:</label>
                                <div class="col-sm-8">
                                    <textarea class="form-control" id="cf_tributary_river" name="cf_tributary_river"></textarea>
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_closest_address" class="col-sm-4 control-label">*{main_map.cave_edit_form.closest_address}*:</label>
                                <div class="col-sm-8">
                                    <textarea class="form-control" id="cf_closest_address" name="cf_closest_address"></textarea>
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_land_registry_number" class="col-sm-4 control-label">*{main_map.cave_edit_form.land_registry_number}*:</label>
                                <div class="col-sm-8">
                                    <textarea class="form-control" id="cf_land_registry_number" name="cf_land_registry_number"></textarea>
                                </div>
                            </div>

                        </div>


                        <div role="tabpanel" class="tab-pane" id="topometry">
                            <br/>

                            <div class="form-group row">
                                <label for="cf_depth" class="col-sm-4 control-label">*{main_map.cave_edit_form.depth}*:</label>
                                <div class="col-sm-4">
                                    <input class="form-control" id="cf_depth" name="cf_depth" type='number' size='5' />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_positive_depth" class="col-sm-4 control-label">*{main_map.cave_edit_form.positive_depth}*:</label>
                                <div class="col-sm-4">
                                    <input class="form-control" id="cf_positive_depth" name="cf_positive_depth" type='number' size='5' />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_negative_depth" class="col-sm-4 control-label">*{main_map.cave_edit_form.negative_depth}*:</label>
                                <div class="col-sm-4">
                                    <input class="form-control" id="cf_negative_depth" name="cf_negative_depth" type='number' size='5' />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_potential_depth" class="col-sm-4 control-label">*{main_map.cave_edit_form.potential_depth}*:</label>
                                <div class="col-sm-4">
                                    <input class="form-control" id="cf_potential_depth" name="cf_potential_depth" type='number' size='5' />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_surveyed_length" class="col-sm-4 control-label">*{main_map.cave_edit_form.surveyed_length}*:</label>
                                <div class="col-sm-4">
                                    <input class="form-control" id="cf_surveyed_length" name="cf_surveyed_length" type='number' size='5' />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_real_extension" class="col-sm-4 control-label">*{main_map.cave_edit_form.real_extension}*:</label>
                                <div class="col-sm-4">
                                    <input class="form-control" id="cf_real_extension" name="cf_real_extension" type='number' size='5' />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_projected_extension" class="col-sm-4 control-label">*{main_map.cave_edit_form.projected_extension}*:</label>
                                <div class="col-sm-4">
                                    <input class="form-control" id="cf_projected_extension" name="cf_projected_extension" type='number' size='5' />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_area" class="col-sm-4 control-label">*{main_map.cave_edit_form.area_m2}*:</label>
                                <div class="col-sm-4">
                                    <input class="form-control" id="cf_area" name="cf_area" type='number' size='5' />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_volume" class="col-sm-4 control-label">*{main_map.cave_edit_form.volume_m3}*:</label>
                                <div class="col-sm-4">
                                    <input class="form-control" id="cf_volume" name="cf_volume" type='number' size='5' />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_ramification_index" class="col-sm-4 control-label">*{main_map.cave_edit_form.ramification_index}*:</label>
                                <div class="col-sm-4">
                                    <input class="form-control" id="cf_ramification_index" name="cf_ramification_index" type='number' size='5' />
                                </div>
                            </div>
                        </div>


                        <div role="tabpanel" class="tab-pane" id="other">
                            <br/>

                            <div class="form-group row">
                                <label for="cf_discovery_date" class="col-sm-4 control-label">*{main_map.cave_edit_form.discovery_date}*:</label>
                                <div class="col-sm-8">
                                    <input class="form-control" id="cf_discovery_date" name="cf_discovery_date" />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_discoverer" class="col-sm-4 control-label">*{main_map.cave_edit_form.discoverers}*:</label>
                                <div class="col-sm-8">
                                    <textarea class="form-control" id="cf_discoverer" name="cf_discoverer"></textarea>
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_exploration_status" class="col-sm-4 control-label">*{main_map.cave_edit_form.exploration_status}*:</label>
                                <div class="col-sm-8">
                                    <textarea class="form-control" id="cf_exploration_status" name="cf_exploration_status"></textarea>
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_is_show_cave" class="col-sm-4 control-label">*{main_map.cave_edit_form.show_cave}*:</label>
                                <div class="col-xs-1">
                                    <input class="form-control" id="cf_is_show_cave" name="cf_is_show_cave" autocomplete="off" type='checkbox' />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_show_cave_length" class="col-sm-4 control-label">*{main_map.cave_edit_form.show_cave_length}*:</label>
                                <div class="col-sm-3">
                                    <input class="form-control" id="cf_show_cave_length" name="cf_show_cave_length" type='number' size='5' />
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_website" class="col-sm-4 control-label">*{main_map.cave_edit_form.website}*:</label>
                                <div class="col-sm-8">
                                    <textarea class="form-control" id="cf_website" name="cf_website"></textarea>
                                </div>
                            </div>

                            <div class="form-group row">
                                <label for="cf_protection_class" class="col-sm-4 control-label">*{main_map.cave_edit_form.protection_class}*:</label>
                                <div class="col-sm-8">
                                    <textarea class="form-control" id="cf_protection_class" name="cf_protection_class"></textarea>
                                </div>
                            </div>

                        </div>
                    </div>




                    <!-- <table id="cave_files_table" class="display" cellspacing="0" width="100%">
        <thead>
            <tr>
				<th>*{generic.file_table.name}*</th>
				<th>*{generic.file_table.size}*</th>
				<th>*{generic.file_table.add_time}*</th>
				<th>*{generic.file_table.category}*</th>
            </tr>
        </thead>
    </table>
	
				<button type="button" class="btn btn-success" id="caveDetailsAddFilesButton">
                    <i class="icon-plus icon-white"></i>
                    <span>*{generic.add_files}*</span>
                </button> -->


                    <!--	
    <form id="fileupload" action="\data\uploader" method="POST" enctype="multipart/form-data">
        <!-- The fileupload-buttonbar contains buttons to add/delete files and start/cancel the upload --
       <div class="row fileupload-buttonbar">
            <div class="col-lg-7">
                <!-- The fileinput-button span is used to style the file input field as button --
                <span class="btn btn-success fileinput-button">
                    <i class="icon-plus icon-white"></i>
                    <span>Add files...</span>
                    <input type="file" name="files[]" multiple>
                </span>
                <button type="submit" class="btn btn-primary start">
                    <i class="icon-upload icon-white"></i>
                    <span>Start upload</span>
                </button>
                <button type="reset" class="btn btn-warning cancel">
                    <i class="icon-ban-circle icon-white"></i>
                    <span>Cancel upload</span>
                </button>
                <button type="button" class="btn btn-danger delete">
                    <i class="icon-trash icon-white"></i>
                    <span>Delete</span>
                </button>
                <input type="checkbox" class="toggle">
                <!-- The global file processing state --
                <span class="fileupload-process"></span>
            </div>
            <!-- The global progress state --
            <div class="col-lg-5 fileupload-progress">
                <!-- The global progress bar --
                <div class="progress progress-striped active" role="progressbar" aria-valuemin="0" aria-valuemax="100">
                    <div class="progress-bar progress-bar-success" style="width:0%;"></div>
                </div>
                <!-- The extended global progress state --
                <div class="progress-extended">&nbsp;</div>
            </div>
        </div>
        <!-- The table listing the files available for upload/download --
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
                    <!-- The template to display files available for upload --
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
            <p class="size">Processing...</p>
            <div class="progress progress-striped active" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0"><div class="progress-bar progress-bar-success" style="width:0%;"></div></div>
        </td>
        <td>
            {% if (!i && !o.options.autoUpload) { %}
                <button class="btn btn-primary start" disabled>
                    <i class="glyphicon glyphicon-upload"></i>
                    <span>Start</span>
                </button>
            {% } %}
            {% if (!i) { %}
                <button class="btn btn-warning cancel">
                    <i class="glyphicon glyphicon-ban-circle"></i>
                    <span>Cancel</span>
                </button>
            {% } %}
        </td>
    </tr>
{% } %}
</script>
<!-- The template to display files available for download --

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
                <div><span class="label label-danger">Error</span> {%=file.error%}</div>
            {% } %}
        </td>
        <td>
            <span class="size">{%=o.formatFileSize(file.size)%}</span>
        </td>
        <td>
            {% if (file.deleteUrl) { %}
                <button class="btn btn-danger delete" data-type="{%=file.deleteType%}" data-url="{%=file.deleteUrl%}"{% if (file.deleteWithCredentials) { %} data-xhr-fields='{"withCredentials":true}'{% } %}>
                    <i class="glyphicon glyphicon-trash"></i>
                    <span>Delete</span>
                </button>
                <input type="checkbox" name="delete" value="1" class="toggle">
            {% } else { %}
                <button class="btn btn-warning cancel">
                    <i class="glyphicon glyphicon-ban-circle"></i>
                    <span>Cancel</span>
                </button>
            {% } %}
        </td>
    </tr>
{% } %}
</script>
-->
                </div>

            </div>
        </div>
    </div>
    <!--
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ	
	Cave details form
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ
-->


    <!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	New picture form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->

    <div class="modal fade" id="pictureModal" tabindex="-1" role="dialog" aria-labelledby="pictureModalTitleLabel">
        <div class="modal-dialog" role="document">
            <div class="modal-content">
                <div class="modal-header">
                    <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
                    <h4 class="modal-title" id="pictureModalTitleLabel">*{main_map.picture_edit_form.title_new}*</h4>
                    <h5><i><div id="picture_coords_label"></div></i></h5>
                </div>
                <div class="modal-body">
                    <form id="pictureForm" role="form" enctype="multipart/form-data" >
                        <!-- class="form-inline" -->

                        <input type="hidden" id="picture_id" name="picture_id">
                        <input type="hidden" id="point_coords_lat" name="point_coords_lat">
                        <input type="hidden" id="point_coords_lon" name="point_coords_lon">
                        <input type="hidden" id="picture_existing_point_id" name="picture_existing_point_id">


                        <input type="hidden" id="point_string" name="point_string">

                        <!--<div class="form-group row">
            <label for="feature_name" class="col-sm-2 control-label">Name:</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="picture_name" name="picture_name" >				
			</div>
          </div>
			-->
                        <div class="form-group row">
                            <label for="picture_description" class="col-sm-2 control-label">*{main_map.picture_edit_form.description}*</label>
                            <div class="col-sm-10">
                                <textarea class="form-control" id="picture_description" name="picture_description"></textarea>
                            </div>
                        </div>

                        <!--<label class="btn btn-default btn-file">*{generic.browse}*<input type="file" style="display: none;" id="file" name="file" ></label>-->
                        <input id="pictureUploadControl" name="file" type="file" class="filex" data-preview-file-type="text">
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
                            <button type="submit" class="btn btn-primary" id="savePicture">*{generic.save}*</button>
                        </div>

                    </form>
                </div>

            </div>
        </div>
    </div>
    <!--
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ	
	End New picture form
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ
-->


    <!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	New cave entrance form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->

    <div class="modal fade" id="caveEntranceModal" tabindex="-1" role="dialog" aria-labelledby="caveEntranceModalTitleLabel">
        <div class="modal-dialog" role="document">
            <div class="modal-content">
                <div class="modal-header">
                    <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
                    <h4 class="modal-title" id="caveEntranceModalTitleLabel">*{main_map.cave_entrance_edit_form.title_new}*</h4>
                    <h5><i><div id="cave_entrance_coords_label"></div></i></h5>
                </div>
                <div class="modal-body">
                    <form id="caveEntranceForm" role="form">
                        <!-- class="form-inline" -->

                        <!--<input type="hidden" id="cave_entrance_cave_id" name="cave_entrance_cave_id" >-->
                        <!-- <input type="hidden" id="cave_entrance_coords_lat" name="cave_coords_lat" >
		 <input type="hidden" id="cave_entrance_coords_lon" name="cave_coords_lon" > -->
                        <input type="hidden" id="cave_entrance_existing_point_id" name="cave_entrance_existing_point_id">
                        <input type="hidden" id="cave_entrance_id" name="cave_entrance_id">

                        <input type="hidden" id="cave_entrance_feature_string" name="cave_entrance_feature_string">

                        <div class="form-group row">
                            <label for="cave_entrance_coords_lat" class="col-sm-2 control-label">Lat:</label>
                            <div class="col-sm-4">
                                <input type='number' size='9' step="any" class="form-control" id="cave_entrance_coords_lat" name="cave_entrance_coords_lat">
                            </div>
                            <label for="cave_entrance_coords_lon" class="col-sm-2 control-label">Long:</label>
                            <div class="col-sm-4">
                                <input type='number' size='9' step="any" class="form-control" id="cave_entrance_coords_lon" name="cave_entrance_coords_lon">
                            </div>
                        </div>

                        <div class="form-group row">
                            <label for="cave_entrance_name" class="col-sm-2 control-label">*{main_map.cave_entrance_edit_form.name}*:</label>
                            <div class="col-sm-10">
                                <input type="text" class="form-control" id="cave_entrance_name" name="cave_entrance_name">
                            </div>
                        </div>

                        <div class="form-group row">
                            <label for="cave_entrance_description" class="col-sm-2 control-label">*{main_map.cave_entrance_edit_form.description}*:</label>
                            <div class="col-sm-10">
                                <textarea class="form-control" id="cave_entrance_description" name="cave_entrance_description"></textarea>
                            </div>
                        </div>

                        <!--		
		<div class="dropdown">
		  <button id="cave_type_btn" type="button" data-toggle="dropdown" aria-haspopup="true" aria-expanded="false">
			Tip
			<span class="caret"></span>
		  </button>
		  <ul id="caveTypes" class="dropdown-menu" aria-labelledby="cave_type_btn">
		  </ul>
		</div>		  
		-->
                        <div class="form-group row">
                            <label for="cave_entrance_type" class="col-sm-2 control-label">*{main_map.cave_entrance_edit_form.type}*:</label>
                            <div class="col-sm-10">
                                <select id="cave_entrance_type" class="selectpicker form-control" name="cave_entrance_type" data-hide-disabled="true" data-max-options="1">
			</select>
                                <!-- multiple data-size="5"  -->
                            </div>
                        </div>

                        <!-- <div class="form-group row">	
		<label for="cave_entrance_cave_idz" class="col-sm-2 control-label">*{main_map.cave_entrance_edit_form.cave}*:</label>	
		<div class="col-sm-10">
			<select id="cave_entrance_cave_idz" class="selectpicker form-control" name="cave_entrance_cave_idz" data-hide-disabled="true" data-max-options="1">
			</select>
		</div>
	</div> -->

                        <input type="hidden" id="cave_entrance_cave_id" name="cave_entrance_cave_id">

                        <div class="form-group row">
                            <label for="cave_entrance_cave_control" class="col-sm-2 control-label">*{main_map.cave_entrance_edit_form.cave}*:</label>
                            <div class="input-group" class="col-sm-10">
                                <input class="Typeahead-hint" type="text" tabindex="-1" readonly>
                                <input type="text" class="Typeahead-input" id="cave_entrance_cave_control" name="cave_entrance_cave_control" placeholder="*{main_map.cave_entrance_edit_form.cave_search_control_placeholder}*">
                                <div class="Typeahead-menu"></div>
                            </div>
                        </div>


                        <div class="modal-footer">
                            <button type="button" class="btn btn-default" data-dismiss="modal">*{generic.close}*</button>
                            <button type="submit" class="btn btn-primary" id="saveCaveEntrance">*{generic.save}*</button>
                        </div>
                    </form>
                </div>

            </div>
        </div>
    </div>
    <!--
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ	
	End New cave entrance form
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ
-->




    <!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	Begin Upload pictures form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->
    <div class="modal fade" id="uploadPicturesModal" tabindex="-1" role="dialog" aria-labelledby="uploadFilesModalTitleLabel">
        <div class="modal-dialog" role="document">
            <div class="modal-content">
                <div class="modal-header">
                    <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
                    <h4 class="modal-title" id="uploadPicturesModalTitleLabel">*{main_map.upload_pictures_form.title}*</h4>
                    <h5><i><div id="upload_files_z_label"></div></i></h5>
                </div>
                <div class="modal-body">
                    <!--<form id="uploadPicturesForm" role="form" >
		-->

                    <!--<input type="hidden" id="upload_files_cave_id" name="upload_files_cave_id" >-->

                    <form class="fileupload" id="pictureUploader" action="<?=WEBROOT ?>/data/uploader/picture_uploader.php" method="POST" enctype="multipart/form-data">
                        <!-- id="pictureUploader" -->
                        <!-- The fileupload-buttonbar contains buttons to add/delete files and start/cancel the upload -->
                        <div class="row fileupload-buttonbar">
                            <div class="col-lg-7">
                                <!-- The fileinput-button span is used to style the file input field as button -->
                                <span class="btn btn-success fileinput-button">
                    <i class="icon-plus icon-white"></i>
                    <span>*{main_map.upload_pictures_form.add_pictures}*</span>
                                <input type="file" name="files[]" multiple>
                                </span>
                                <button type="submit" class="btn btn-primary start">
                    <i class="icon-upload icon-white"></i>
                    <span>*{generic.start_upload}*</span>
                </button>
                                <button type="reset" class="btn btn-warning cancel">
                    <i class="icon-ban-circle icon-white"></i>
                    <span>*{generic.cancel}*</span>
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
                        <table role="presentation" class="table table-striped">
                            <tbody class="files"></tbody>
                        </table>

                        <input type="hidden" id="upload_feature_id" name="upload_feature_id">
                        <!--<input type="hidden" id="upload_cave_id" name="upload_cave_id" >-->
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
                    <script id="template-upload-picture" type="text/x-tmpl">
                        {% for (var i=0, file; file=o.files[i]; i++) { %}
                        <tr class="template-upload-picture">
                            <td>
                                <span class="preview"></span>
                            </td>
                            <td>
                                <p class="name">{%=file.name%}</p>
                                <strong class="error text-danger"></strong>
                            </td>
                            <td>
                                <p class="size">*{generic.processing}*</p>
                                <div class="progress progress-striped active" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0">
                                    <div class="progress-bar progress-bar-success" style="width:0%;"></div>
                                </div>
                            </td>
                            <td>
                                {% if (!i && !o.options.autoUpload) { %}
                                <button class="btn btn-primary start" disabled>
                    <i class="glyphicon glyphicon-upload"></i>
                    <span>*{generic.start_upload}*</span>
                </button> {% } %} {% if (!i) { %}
                                <button class="btn btn-warning cancel">
                    <i class="glyphicon glyphicon-ban-circle"></i>
                    <span>*{generic.cancel}*</span>
                </button> {% } %}
                            </td>
                        </tr>
                        {% } %}
                    </script>
                    <!-- The template to display files available for download -->

                    <script id="template-download-picture" type="text/x-tmpl">
                        {% for (var i=0, file; file=o.files[i]; i++) { %}
                        <tr class="template-download-picture">
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
                                    <a href="{%=file.url%}" title="{%=file.name%}" download="{%=file.name%}" {%=file.thumbnailUrl? 'data-gallery': ''%}>{%=file.name%}</a>                                    {% } else { %}
                                    <span>{%=file.name%}</span> {% } %}
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
                                <button class="btn btn-danger delete" data-type="{%=file.deleteType%}" data-url="{%=file.deleteUrl%}" {% if (file.deleteWithCredentials)
                                    { %} data-xhr-fields='{"withCredentials":true}' {% } %}>
                    <i class="glyphicon glyphicon-trash"></i>
                    <span>*{generic.del}*</span>
                </button>
                                <input type="checkbox" name="delete" value="1" class="toggle"> {% } else { %}
                                <button class="btn btn-warning cancel">
                    <i class="glyphicon glyphicon-ban-circle"></i>
                    <span>*{generic.cancel}*</span>
                </button> {% } %}
                            </td>
                        </tr>
                        {% } %}
                    </script>

                    <div class="modal-footer">
                        <button type="button" class="btn btn-default" data-dismiss="modal">*{generic.close}*</button>
                        <button type="submit" class="btn btn-primary" id="savePictureFileUpload">*{generic.save}*</button>
                    </div>


                    <!--</form>
			-->
                </div>

            </div>
        </div>
    </div>

    <!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	end Upload pictures form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->


    <!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	New cave feature form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->

    <div class="modal fade" id="caveFeatureModal" tabindex="-1" role="dialog" aria-labelledby="caveFeatureModalTitleLabel">
        <div class="modal-dialog" role="document">
            <div class="modal-content">
                <div class="modal-header">
                    <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
                    <h4 class="modal-title" id="caveFeatureModalTitleLabel">*{main_map.cave_feature_edit_form.title_new}*</h4>
                    <h5><i><div id="cave_feature_coords_label"></div></i></h5>
                </div>
                <div class="modal-body">
                    <form id="caveFeatureForm" role="form">
                        <!-- class="form-inline" -->

                        <input type="hidden" id="cave_feature_id" name="cave_feature_id">
                        <!--<input type="hidden" id="cave_feature_coords_lat" name="cave_feature_coords_lat" >
		 <input type="hidden" id="cave_feature_coords_lon" name="cave_feature_coords_lon" >-->
                        <input type="hidden" id="cave_feature_existing_point_id" name="cave_feature_existing_point_id">
                        <input type="hidden" id="cave_feature_type_id" name="cave_feature_type_id">

                        <input type="hidden" id="cave_feature_string" name="cave_feature_string">

                        <div class="form-group row">
                            <label for="cave_feature_name" class="col-sm-2 control-label">*{main_map.cave_feature_edit_form.name}*:</label>
                            <div class="col-sm-10">
                                <input type="text" class="form-control focusedInput" id="cave_feature_name" name="cave_feature_name">
                            </div>
                        </div>

                        <div class="form-group row">
                            <label for="cave_feature_description" class="col-sm-2 control-label">*{main_map.cave_feature_edit_form.description}*:</label>
                            <div class="col-sm-10">
                                <textarea class="form-control" id="cave_feature_description" name="cave_feature_description"></textarea>
                            </div>
                        </div>

                        <div class="modal-footer">
                            <button type="button" class="btn btn-default" data-dismiss="modal">*{generic.close}*</button>
                            <button type="submit" class="btn btn-primary" id="saveCaveFeature">*{generic.save}*</button>
                        </div>

                    </form>
                </div>

            </div>
        </div>
    </div>

    <!--
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ	
	End New cave feature form
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ
-->

<!-- //-- about form duplicated in grid_common.php -->

<!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	About form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->

<div class="modal fade" id="aboutModal" tabindex="-1" role="dialog" aria-labelledby="aboutModalTitleLabel">
        <div class="modal-dialog" role="document">
            <div class="modal-content">
                <div class="modal-header">
                    <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
                    <h4 class="modal-title" id="aboutModalTitleLabel">*{main_map.about_form.title}*</h4>
                    <!-- <h5><i><div id="feature_coords_label"></div></i></h5> -->
                </div>
                <div class="modal-body">
                    <!-- <form id="aboutForm" role="form" > -->
                        <a href="http://www.speosilex.ro/silexgis/" target="_blank" >Website SilexGIS</a>
                        <br/>
                        <br/>
                        <a href="https://github.com/apgeo/silexgis" target="_blank" >SilexGIS pe GitHub</a>
                        <br/>
                        <br/>
                        <a href="http://www.speosilex.ro/" target="_blank" >Silex Brașov</a>

                        <div class="modal-footer">
                            <button type="button" class="btn btn-default" data-dismiss="modal">*{generic.close}*</button>                            
                        </div>

                    <!-- </form> -->
                </div>

            </div>
        </div>
    </div>
    <!--
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ	
	End About feature form
	ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ
-->
    <?php
    include_once ROOTPATH.'/user/trip_report_form.php';
	include_once ROOTPATH.'/user/georeferenced_map_form.php';
?>

        <!--
                            <a href="http://41.media.tumblr.com/f37ac708134914c471073e4c0b47328d/tumblr_mrn3dc10Wa1r1thfzo8_1280.jpg" data-toggle="lightbox" data-title="A random title" data-footer="A custom footer text">
                                xx
                            </a>
-->
        <a href="#" data-toggle="lightbox" data-title="custom title" data-footer="custom text" id="thumbPictureHolder" style="visibility:hidden">
                                
                            </a>

        </body>

        </html>