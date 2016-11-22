<?php
    include_once 'header.php';
	include_once 'main_page_logger.php';
?>



<script type="text/javascript" src="/speogis/scripts/main_map.js"></script>
<!--<body onload="init();">-->

<div class="ui-layout-center">	
	<div id="mapdiv" class="mapdiv" >
	  <div id="toolbox">
		<div onclick="toggleMapLayerSelector();" > Background >></div>
	<ul id="layerswitcher" onchange="switchLayer();" >
		<li><label><input type="radio" name="layer" value="0" checked=""> OSM</label></li>
		<li><label><input type="radio" name="layer" value="1"> MapQuest Satellitar</label></li>
		<li><label><input type="radio" name="layer" value="2"> MapQuest Hybrid</label></li>
		<li><label><input type="radio" name="layer" value="3"> MapQuest OSM</label></li>
		<li><label><input type="radio" name="layer" value="4"> ArcGIS</label></li>
		<li><label><input type="radio" name="layer" value="5"> Bing Topo</label></li>
		<li><label><input type="radio" name="layer" value="6"> Bing Aerial</label></li>
		<li><label><input type="radio" name="layer" value="7"> Bing labelled</label></li>
		<li><label><input type="radio" name="layer" value="8"> OpenLayers local</label></li>		
		<!--<li><label><input type="checkbox" name="layer" value="99"> Harta gelogica 1970</label></li>		-->
	</ul>
	</div>

	<div id="measurementsBox">	
	<form class="form-inline">
	  <label><input type="checkbox" onchange="" id="measurementCheckBox" value="99"/>Distances </label>
      <label>Type:&nbsp;</label>
        <select id="measure_type">
          <option value="length">Distance</option>
          <option value="area">Area</option>
        </select>
        <label class="checkbox">
          <input type="checkbox" id="geodesic"/>
          Geodesic measurement
		  
        </label>
		<!--
		<button type="button" onclick="selectDrawFeature('Point');" >Point</button>
		<button type="button" onclick="selectDrawFeature('LineString');" >Line</button>
		<button type="button" onclick="selectDrawFeature('Polygon');" >Polygon</button>
		<button type="button" onclick="selectDrawFeature('MultiPolygon');" >MP</button>
		-->
		
		<!--<button type="button" onclick="enableDrawNewCave();" >Add new cave</button>
		-->
		<!--<button type="button" onclick="newCave();" > Adauga pestera</button>-->
		<!--<button type="button" onclick="newCave(1);" > Editeaza test</button>-->
		</form>		
</div>

	</div>
	</div>
<!--<div class="ui-layout-north">North</div>-->
<div class="ui-layout-south">
	<div>
	
		<label><input type="checkbox" onchange="" id="hgPersaniCentruCheckBox" value="false"/><a href='./assets/layer_images/persani_comana_geologica.jpg' target='_blank' >Gelogic Map 1970 - persani centru</a><label>
		<!--<a href='./assets/layer_images/persani_comana_geologica.jpg' target='_blank' >geologica persani centru</a>-->
		
		<div id="slider-id"><div class="ui-slider-handle">X</div></div>
		
	</div>

</div>

<div class="ui-layout-east">
<div id="drawControlBox" >
control box<br/>

</div>

</div>
<!--<div class="ui-layout-west">West</div>-->

</div>


		

<!--
  <div div id="toolbox">
	<ul id="layerswitcher" onchange="switchLayer();" >
		<li><label><input type="radio" name="layer" value="0" checked=""> OSM</label></li>
		<li><label><input type="radio" name="layer" value="1"> MapQuest Satellitar</label></li>
		<li><label><input type="radio" name="layer" value="2"> MapQuest Hybrid</label></li>
		<li><label><input type="radio" name="layer" value="3"> MapQuest OSM</label></li>
		<li><label><input type="radio" name="layer" value="4"> ArcGIS</label></li>
		<li><label><input type="radio" name="layer" value="5"> Bing Topo</label></li>
		<li><label><input type="radio" name="layer" value="6"> Bing Aerial</label></li>
		<li><label><input type="radio" name="layer" value="7"> Bing labelled</label></li>
		<li><label><input type="radio" name="layer" value="8"> OpenLayers local</label></li>		
		<!--<li><label><input type="checkbox" name="layer" value="99"> Harta gelogica 1970</label></li>		-->
	</ul>
	</div>
-->

<!--<div id="measurementsBox">	
	<form class="form-inline">
	  <label><input type="checkbox" onchange="" id="measurementCheckBox" value="99"/>Distances </label>
      <label>Type:&nbsp;</label>
        <select id="measure_type">
          <option value="length">Distance</option>
          <option value="area">Area</option>
        </select>
        <label class="checkbox">
          <input type="checkbox" id="geodesic"/>
          Geodesic measurement
		  
        </label>
		<!--
		<button type="button" onclick="selectDrawFeature('Point');" >Point</button>
		<button type="button" onclick="selectDrawFeature('LineString');" >Line</button>
		<button type="button" onclick="selectDrawFeature('Polygon');" >Polygon</button>
		<button type="button" onclick="selectDrawFeature('MultiPolygon');" >MP</button>
		-->
		
		<!--<button type="button" onclick="enableDrawNewCave();" >Add new cave</button>
		-->
		<!--<button type="button" onclick="newCave();" > Adauga pestera</button> ->
		<!--<button type="button" onclick="newCave(1);" > Editeaza test</button> ->
		</form>		
</div>
-->

	<div id="pointInfoBox" >
          <div id="info" <!--class="alert alert-success"-->>
            
          </div>
        </div>		

<!-- 
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
	New cave form
	XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX	
-->

<div class="modal fade" id="caveModal" tabindex="-1" role="dialog" aria-labelledby="caveModalTitleLabel">
  <div class="modal-dialog" role="document">
    <div class="modal-content">
      <div class="modal-header">
        <button type="button" class="close" data-dismiss="modal" aria-label="Close"><span aria-hidden="true">&times;</span></button>
        <h4 class="modal-title" id="caveModalTitleLabel">New cave</h4>
		<h5><i><div id="cave_coords_label"></div></i></h5>
      </div>
      <div class="modal-body">	  
        <form id="caveForm" role="form" > <!-- class="form-inline" -->
		 
		 <input type="hidden" id="cave_id" name="cave_id" >
		 <input type="hidden" id="cave_coords_lat" name="cave_coords_lat" >
		 <input type="hidden" id="cave_coords_lon" name="cave_coords_lon" >
		 <input type="hidden" id="entrance_existing_point_id" name="entrance_existing_point_id" >
		 
          <div class="form-group row">
            <label for="cave_name" class="col-sm-2 control-label">Name:</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="cave_name" name="cave_name" >
			</div>
          </div>

          <div class="form-group row">
            <label for="cave_identifier" class="col-sm-2 control-label">Identification #:</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="cave_identifier" name="cave_identifier" ></textarea>
			</div>
          </div>		  

          <div class="form-group row">
            <label for="cave_description" class="col-sm-2 control-label">Description:</label>
			<div class="col-sm-10">
				<textarea class="form-control" id="cave_description" name="cave_description" ></textarea>
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
		<label for="cave_type" class="col-sm-2 control-label">Type:</label>	
		<div class="col-sm-10">
			<select id="cave_type" class="selectpicker form-control" name="cave_type" data-hide-disabled="true" data-max-options="1">
			</select> <!-- multiple data-size="5"  -->
		</div>
	</div>
      <div class="modal-footer">
        <button type="button" class="btn btn-default" data-dismiss="modal">Close</button>
        <button type="submit" class="btn btn-primary" id="saveCave" >Save</button>
      </div>
	  
	        </form>
      </div>

    </div>
  </div>
</div>
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
        <h4 class="modal-title" id="featureModalTitleLabel">New feature</h4>
		<h5><i><div id="feature_coords_label"></div></i></h5>
      </div>
      <div class="modal-body">	  
        <form id="featureForm" role="form" > <!-- class="form-inline" -->
		 
		 <input type="hidden" id="feature_id" name="feature_id" >
		 <!--<input type="hidden" id="feature_coords_lat" name="feature_coords_lat" >
		 <input type="hidden" id="feature_coords_lon" name="feature_coords_lon" >-->
		 <input type="hidden" id="feature_existing_point_id" name="feature_existing_point_id" >
		 <input type="hidden" id="feature_type_id" name="feature_type_id" >
		 
		 <input type="hidden" id="feature_string" name="feature_string" >
		 
          <div class="form-group row">
            <label for="feature_name" class="col-sm-2 control-label">Name:</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="feature_name" name="feature_name" >
			</div>
          </div>

          <div class="form-group row">
            <label for="feature_description" class="col-sm-2 control-label">Description:</label>
			<div class="col-sm-10">
				<textarea class="form-control" id="feature_description" name="feature_description" ></textarea>
			</div>
          </div>		  

		  <div class="modal-footer">
			<button type="button" class="btn btn-default" data-dismiss="modal">Close</button>
			<button type="submit" class="btn btn-primary" id="saveFeature" >Save</button>
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

</body>
</html>