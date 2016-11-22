<?php
	session_start(); //-- ?
    require_once 'auth.php';
	
	require_once 'config.php';    
?>
<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<style>
#topmenu {
    list-style-type: none;
    margin: 0;
    padding: 0;
    overflow: hidden;
    background-color: #BBBBBB;
}

#topmenu li {
    float: left;
}

#buttonRight {
    float: right;
}

#topmenu li a {
    display: block;
    color: white;
    text-align: center;
    padding: 14px 16px;
    text-decoration: none;
}

#topmenu a:hover:not(.active) {
    background-color: #555;
}

#topmenu .active {
background-color:#4CAF50;

}

#topmenu li p {
    //display: block;
    color: green;
    //text-align: center;
    //padding: 14px 16px;
    //text-decoration: none;
}

</style>

  <style type="text/css">
    html, body, #mapdiv {
        width:100%; height:100%; margin:0;
    }
  </style>
  
      <style>
      .mapdiv {
        height: 400px;
        width: 100%;
      }

html           { width:100%; height:100%; margin:0; }
body           { width:100%; height:100%; margin:0; font-family:sans-serif; }
#map           { width:100%; height:100%; margin:0; }
#toolbox       { position:absolute; top:48px; right:8px; padding:3px; border-radius:4px; color:#fff; background: rgba(255, 255, 255, 0.4); z-index:100; }
#layerswitcher { margin:0; padding:10px; border-radius:4px; background:rgba(0, 60, 136, 0.5); list-style-type:none; } 
	  
	  #slider-id {
    width: 292px;
    margin: 10px;
}
    </style>

	<style>
	#measurementsBox { position:absolute; bottom:8px; right:8px; padding:3px; border-radius:4px; color:#fff; background: rgba(255, 255, 255, 0.4); z-index:100; background:rgba(0, 60, 136, 0.5);}
	
	#pointInfoBox { position:absolute; bottom:8px; left:8px; padding:3px; border-radius:4px; color:#fff; background: rgba(255, 255, 255, 0.4); z-index:100; background:rgba(200, 60, 136, 0.5);}
	
      .spg_tooltip {
        position: relative;
        background: rgba(0, 0, 0, 0.5);
        border-radius: 4px;
        color: white;
        padding: 4px 8px;
        opacity: 0.7;
        white-space: nowrap;
      }
      .spg_tooltip-measure {
        opacity: 1;
        font-weight: bold;
      }
      .spg_tooltip-static {
        background-color: #ffcc33;
        color: black;
        border: 1px solid white;
      }
      .spg_tooltip-measure:before,
      .spg_tooltip-static:before {
        border-top: 6px solid rgba(0, 0, 0, 0.5);
        border-right: 6px solid transparent;
        border-left: 6px solid transparent;
        content: "";
        position: absolute;
        bottom: -6px;
        margin-left: -7px;
        left: 50%;
      }
      .spg_tooltip-static:before {
        border-top-color: #ffcc33;
      }
  </style>

  <style>
	/* 
	   fix for navbar described in http://getbootstrap.com/components/#navbar-fixed-top
	   used when nav has class navbar-fixed-top
	*/
	body { padding-top: 50px; }
	
	/*	
	for smaller nav bar uncomment this, set body -> padding-top the same value as navbar -> height
	
	.navbar-nav > li > a, .navbar-brand {
		padding-top:4px !important; 
		padding-bottom:0 !important;
		height: 28px;
	}
	.navbar {min-height:28px !important; 
			height: 28px;}
		*/
		
		        .bootstrap-select {
            width: 100% !important;
        }
  </style>







  <title>Speo GIS</title>
<?php
$onlineMode = !false;
if ($onlineMode) :
?>
  <!--<script src="http://www.openlayers.org/api/OpenLayers.js"></script>-->
 <!--<script src='http://maps.google.com/maps?file=api&amp;v=3&amp;key=ABQIAAAAjpkAC9ePGem0lIq5XcMiuhR_wWLPFku8Ix9i2SXYRVK3e45q1BQUd_beF8dtzKET_EteAjPdGDwqpQ'></script>-->
 <script src='http://maps.google.com/maps/api/js?v=3&amp;sensor=false'></script>

 <!-- <script src="https://code.jquery.com/jquery-1.11.2.min.js"></script>
<link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.4/css/bootstrap.min.css">
<script src="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.4/js/bootstrap.min.js"></script>
<link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/ol3/3.5.0/ol.css" type="text/css">
<script src="https://cdnjs.cloudflare.com/ajax/libs/ol3/3.5.0/ol.js"></script>
-->

<!--<script src="http://api.maps.yahoo.com/ajaxymap?v=3.0&appid=euzuro-openlayers"></script>-->

<!--<script type="text/javascript" src="//dev.virtualearth.net/REST/v1/Imagery/Metadata/Road?key=AqTGBsziZHIJYYxgivLBf0hVdrAk9mWO5cQcb8Yux8sW5M8c8opEC2lZqKR1ZZXf&amp;jsonp=_callback_OpenLayers_Layer_Bing_23&amp;include=ImageryProviders" id="_callback_OpenLayers_Layer_Bing_23"></script>
<script type="text/javascript" src="//dev.virtualearth.net/REST/v1/Imagery/Metadata/AerialWithLabels?key=AqTGBsziZHIJYYxgivLBf0hVdrAk9mWO5cQcb8Yux8sW5M8c8opEC2lZqKR1ZZXf&amp;jsonp=_callback_OpenLayers_Layer_Bing_25&amp;include=ImageryProviders" id="_callback_OpenLayers_Layer_Bing_25"></script>
<script type="text/javascript" src="//dev.virtualearth.net/REST/v1/Imagery/Metadata/Aerial?key=AqTGBsziZHIJYYxgivLBf0hVdrAk9mWO5cQcb8Yux8sW5M8c8opEC2lZqKR1ZZXf&amp;jsonp=_callback_OpenLayers_Layer_Bing_27&amp;include=ImageryProviders" id="_callback_OpenLayers_Layer_Bing_27"></script>
-->
    <script src="http://ecn.dev.virtualearth.net/mapcontrol/mapcontrol.ashx?v=6.2&mkt=en-us"></script>
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol.js" type="text/javascript"></script>-->
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol-debug.js" type="text/javascript"></script>-->
	
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol-debug.js" type="text/javascript"></script>-->
	<!--<script src="http://openlayers.org/en/v3.2.0/build/ol-debug.js" type="text/javascript"></script>-->
	
	<script src="http://openlayers.org/en/v3.15.0/build/ol-debug.js" type="text/javascript"></script>
	
	<link rel="stylesheet" href="http://openlayers.org/en/v3.12.1/css/ol.css" type="text/css">    
	

	<script type="text/javascript" src="http://ajax.googleapis.com/ajax/libs/jquery/1.11.3/jquery.min.js"></script>
    
	<script type="text/javascript" src="http://ajax.googleapis.com/ajax/libs/jqueryui/1.11.4/jquery-ui.min.js"></script>	
	<link rel="stylesheet" href="http://ajax.googleapis.com/ajax/libs/jqueryui/1.11.4/themes/ui-lightness/jquery-ui.css" type="text/css">

	<script src="/speogis/scripts/ol3-popup-master/src/ol3-popup.js"></script>
	<link rel="stylesheet" href="/speogis/scripts/ol3-popup-master/src/ol3-popup.css" />

	<!-- Bootstrap Only -->
	<!-- Latest compiled and minified CSS -->	
	<link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.6/css/bootstrap.min.css" integrity="sha384-1q8mTJOASx8j1Au+a5WDVnPi2lkFfwwEAa8hDDdjZlpLegxhjVME1fgjWPGmkzs7" crossorigin="anonymous">

	<!-- Optional theme -->
	<link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.6/css/bootstrap-theme.min.css" integrity="sha384-fLW2N01lMqjakBkx3l/M9EahuwpSfeNvV63J5ezn3uZzapT0u7EYsXMjQV+0En5r" crossorigin="anonymous">

	<!-- Latest compiled and minified JavaScript -->
	<script src="https://maxcdn.bootstrapcdn.com/bootstrap/3.3.6/js/bootstrap.min.js" integrity="sha384-0mSbJDEHialfmuBBQP6A4Qrprq5OVfW37PRR3j5ELqxss1yVqOtnepnHVP9aJ7xS" crossorigin="anonymous"></script>
	

	<!-- React Bootstrap-->
	<!-- Latest compiled and minified CSS -->
<!--	<link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/latest/css/bootstrap.min.css">

	<!-- Optional theme -->
<!--	<link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/latest/css/bootstrap-theme.min.css">	

	<script src="https://cdnjs.cloudflare.com/ajax/libs/react/<react-version>/react.min.js"></script>
	<script src="https://cdnjs.cloudflare.com/ajax/libs/react/<react-version>/react-dom.min.js"></script>
	<script src="https://cdnjs.cloudflare.com/ajax/libs/react-bootstrap/<version>/react-bootstrap.min.js"></script>	-->
	
	<!--
	<script src="/speogis/scripts/bootstrap-select-1.10.0/dist/js/bootstrap-select.js"></script>
	<link rel="stylesheet" href="/speogis/scripts/bootstrap-select-1.10.0/dist/css/bootstrap-select.css">
	-->
	
	<script type="text/javascript" src="//cdnjs.cloudflare.com/ajax/libs/bootstrap-select/1.6.3/js/bootstrap-select.min.js"></script>
	<script type="text/javascript" src="/speogis/scripts/Ajax-Bootstrap-Select-master/dist/js/ajax-bootstrap-select.js"></script>
    <link rel="stylesheet" href="/speogis/scripts/Ajax-Bootstrap-Select-master/dist/css/ajax-bootstrap-select.css"/>

<?php
	else :
?>
	<script> alert('offline resource mode'); </script>
<!-- //============================================================================================================================================= 
	 //============================================================================================================================================= 
		Offline
	-->
    <script src="http://ecn.dev.virtualearth.net/mapcontrol/mapcontrol.ashx?v=6.2&mkt=en-us"></script>
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol.js" type="text/javascript"></script>-->
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol-debug.js" type="text/javascript"></script>-->
	
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol-debug.js" type="text/javascript"></script>-->
	<!--<script src="http://openlayers.org/en/v3.2.0/build/ol-debug.js" type="text/javascript"></script>-->
	
	<script src="/speogis/temp_offline_scripts/ol-debug.js" type="text/javascript"></script>
	
	<link rel="stylesheet" href="/speogis/temp_offline_scripts/ol.css" type="text/css">    
	

	<script type="text/javascript" src="/speogis/temp_offline_scripts/jquery.min.js"></script>
    
	<script type="text/javascript" src="/speogis/temp_offline_scripts/jquery-ui.min.js"></script>	
	<link rel="stylesheet" href="/speogis/temp_offline_scripts/jquery-ui.css" type="text/css">

	<script src="/speogis/scripts/ol3-popup-master/src/ol3-popup.js"></script>
	<link rel="stylesheet" href="/speogis/scripts/ol3-popup-master/src/ol3-popup.css" />

	<!-- Bootstrap Only -->
	<!-- Latest compiled and minified CSS -->	
	<link rel="stylesheet" href="/speogis/temp_offline_scripts//bootstrap.min.css" integrity="sha384-1q8mTJOASx8j1Au+a5WDVnPi2lkFfwwEAa8hDDdjZlpLegxhjVME1fgjWPGmkzs7" crossorigin="anonymous">

	<!-- Optional theme -->
	<link rel="stylesheet" href="/speogis/temp_offline_scripts/bootstrap-theme.min.css" integrity="sha384-fLW2N01lMqjakBkx3l/M9EahuwpSfeNvV63J5ezn3uZzapT0u7EYsXMjQV+0En5r" crossorigin="anonymous">

	<!-- Latest compiled and minified JavaScript -->
	<script src="/speogis/temp_offline_scripts/bootstrap.min.js" integrity="sha384-0mSbJDEHialfmuBBQP6A4Qrprq5OVfW37PRR3j5ELqxss1yVqOtnepnHVP9aJ7xS" crossorigin="anonymous"></script>
	

	<!-- React Bootstrap-->
	<!-- Latest compiled and minified CSS -->
<!--	<link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/latest/css/bootstrap.min.css">

	<!-- Optional theme -->
<!--	<link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/latest/css/bootstrap-theme.min.css">	

	<script src="https://cdnjs.cloudflare.com/ajax/libs/react/<react-version>/react.min.js"></script>
	<script src="https://cdnjs.cloudflare.com/ajax/libs/react/<react-version>/react-dom.min.js"></script>
	<script src="https://cdnjs.cloudflare.com/ajax/libs/react-bootstrap/<version>/react-bootstrap.min.js"></script>	-->
	
	<script src="/speogis/scripts/bootstrap-select-1.10.0/dist/js/bootstrap-select.js"></script>
	<link rel="stylesheet" href="/speogis/scripts/bootstrap-select-1.10.0/dist/css/bootstrap-select.css">

<!--
	 =============================================================================================================================================
	 =============================================================================================================================================
		end Offline
-->

<?php
	endif;
?>
  <meta name="description" content="Silex GIS" />

  
</head>
<body>

<!-- doc: http://getbootstrap.com/components/#navbar-fixed-top -->
<nav class="navbar navbar-default navbar-fixed-top" > <!--role="navigation" --> 
  <div class="container-fluid">
    <!-- Brand and toggle get grouped for better mobile display -->
    <div class="navbar-header">
      <button type="button" class="navbar-toggle collapsed" data-toggle="collapse" data-target="#bs-example-navbar-collapse-1" aria-expanded="false">
        <span class="sr-only">Toggle navigation</span>
        <span class="icon-bar"></span>
        <span class="icon-bar"></span>
        <span class="icon-bar"></span>
      </button>
      <a class="navbar-brand" href="#">SilexGIS</a>
    </div>

    <!-- Collect the nav links, forms, and other content for toggling -->
    <div class="collapse navbar-collapse" id="bs-example-navbar-collapse-1">
      <ul class="nav navbar-nav">
        <li class="active"><a href="/speogis/index.php">Harta<span class="sr-only">(current)</span></a></li>
        <li><a href="/speogis/user/points.php">Date</a></li>
		<li><a href="/speogis/user/users.php">Utilizatori</a></li>
		<!--
        <li class="dropdown">
          <a href="#" class="dropdown-toggle" data-toggle="dropdown" role="button" aria-haspopup="true" aria-expanded="false">Dropdown <span class="caret"></span></a>
          <ul class="dropdown-menu">
            <li><a href="#">Action</a></li>
            <li><a href="#">Another action</a></li>
            <li><a href="#">Something else here</a></li>
            <li role="separator" class="divider"></li>
            <li><a href="#">Separated link</a></li>
            <li role="separator" class="divider"></li>
            <li><a href="#">One more separated link</a></li>
          </ul>
        </li>
		-->
      </ul>
	  
	  
		<form class="navbar-form navbar-left" role="search">
        <div class="input-group">		
            <select id="search_control" class="selectpicker with-ajax" multiple data-live-search="true"></select>
        </div>
		</form>

	  <!--
      <form class="navbar-form navbar-left" role="search">
        <div class="input-group">
		  <!--<input type="text" id="search_control" class="selectpicker form-control" placeholder="Search" name="search_control"> ->
		  <select id="search_control" class="selectpicker form-control" placeholder="Search" name="search_control" data-live-search="true" >
		  </select>
          <div class="input-group-btn">
            <button class="btn btn-default" ><i class="glyphicon glyphicon-search"></i></button>
			<!--<button class="btn btn-default" type="submit"><i class="glyphicon glyphicon-search"></i></button> ->
          </div>
 ->
		  <!-- <input type="text" id="search_control" class="selectpicker form-control" placeholder="Search"> ->
        </div>
        <!--<button type="submit" class="btn btn-default">Submit</button>-->
		
		<!--
		<div class="input-group">
          <input type="text" class="form-control" placeholder="Search" name="q">

          <div class="input-group-btn">
            <button class="btn btn-default" type="submit"><i class="glyphicon glyphicon-search"></i></button>
          </div>
        </div>
		->
		
      </form>
	  -->
	  
      <ul class="nav navbar-nav navbar-right">
        <!--<li><a href="#">Link</a></li>-->
        <li class="dropdown">
          <a href="#" class="dropdown-toggle" data-toggle="dropdown" role="button" aria-haspopup="true" aria-expanded="false">User <span class="caret"></span></a>
          <ul class="dropdown-menu">
            <li><a href="/speogis/logout.php">Afara</a></li>
            <!--
			<li><a href="#">Another action</a></li>
            <li><a href="#">Something else here</a></li>
            <li role="separator" class="divider"></li>
            <li><a href="#">Separated link</a></li>
			-->
          </ul>
        </li>
      </ul>
    </div><!-- /.navbar-collapse -->
  </div><!-- /.container-fluid -->
</nav>
<!--
<ul id='topmenu' >

  <li><p style='logo'>Silex GIS &nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp</p></li>
  <li> </li>
  <li> </li>
  <li><a <?php if (strpos($_SERVER['PHP_SELF'], 'index.php')) echo "class='active'"; ?> href="/speogis/index.php">Harta</a></li>
  <li><a <?php if (strpos($_SERVER['PHP_SELF'], 'points.php')) echo "class='active'"; ?> href="/speogis/user/points.php">Date</a></li>
  <li><a <?php if (strpos($_SERVER['PHP_SELF'], 'users.php')) echo "class='active'"; ?> href="/speogis/user/users.php">Utilizatori</a></li>  
  <li id='buttonRight' style='float: right;' ><a <?php if (strpos($_SERVER['PHP_SELF'], 'users.php')) echo "class='active'"; ?> href="/speogis/logout.php">Afara</a></li>  
</ul>
-->




<script type="text/javascript" src="/speogis/scripts/main_map.js"></script>
<!--<body onload="init();">-->
<br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/><br/>
  <!--<span id="header">z</div>-->
  <div id="mapdiv" class="mapdiv" ></div>
  <div div id="toolbox">
	<ul id="layerswitcher" onchange="switchLayer();" >
		<li><label><input type="radio" name="layer" value="0" checked=""> OSM</label></li>
		<li><label><input type="radio" name="layer" value="1"> MapQuest Satelitar</label></li>
		<li><label><input type="radio" name="layer" value="2"> MapQuest Hibrid</label></li>
		<li><label><input type="radio" name="layer" value="3"> MapQuest OSM</label></li>
		<li><label><input type="radio" name="layer" value="4"> ArcGIS</label></li>
		<li><label><input type="radio" name="layer" value="5"> Bing Topo</label></li>
		<li><label><input type="radio" name="layer" value="6"> Bing Aerial</label></li>
		<li><label><input type="radio" name="layer" value="7"> Bing Etichetat</label></li>		
		<!--<li><label><input type="checkbox" name="layer" value="99"> Harta gelogica 1970</label></li>		-->
	</ul>
	</div>

<div id="measurementsBox">	
	<div>
	
		<label><input type="checkbox" onchange="" id="hgPersaniCentruCheckBox" value="99"/>Harta gelogica 1970<label>
		<a href='./assets/layer_images/persani_comana_geologica.jpg' target='_blank' >geologica persani centru</a><div id="slider-id"><div class="ui-slider-handle">geo</div></div>
		
	</div>

	<form class="form-inline">
	  <label><input type="checkbox" onchange="" id="measurementCheckBox" value="99"/>Distante </label>
      <label>Tip:&nbsp;</label>
        <select id="measure_type">
          <option value="length">Distanta</option>
          <option value="area">Arie</option>
        </select>
        <label class="checkbox">
          <input type="checkbox" id="geodesic"/>
          masuratori geodezice
		  
        </label>
		<button type="button" onclick="selectDrawFeature('Point');" >Point</button>		
		<button type="button" onclick="selectDrawFeature('LineString');" >Line</button>
		<button type="button" onclick="selectDrawFeature('Polygon');" >Polygon</button>
		<button type="button" onclick="selectDrawFeature('MultiPolygon');" >MP</button>
		
		<button type="button" onclick="enableDrawNewCave();" > Adauga pestera</button>
		<!--<button type="button" onclick="newCave();" > Adauga pestera</button>-->
		<button type="button" onclick="newCave(1);" > Editeaza test</button>
		</form>		
</div>

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
        <h4 class="modal-title" id="caveModalTitleLabel">Pestera noua</h4>		
		<h5><i><div id="cave_coords_label"></div></i></h5>
      </div>
      <div class="modal-body">	  
        <form id="caveForm" role="form" > <!-- class="form-inline" -->
		 
		 <input type="hidden" id="cave_id" name="cave_id" >
		 <input type="hidden" id="cave_coords_lat" name="cave_coords_lat" >
		 <input type="hidden" id="cave_coords_lon" name="cave_coords_lon" >
		 <input type="hidden" id="entrance_existing_point_id" name="entrance_existing_point_id" >
		 
          <div class="form-group row">
            <label for="cave_name" class="col-sm-2 control-label">Nume:</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="cave_name" name="cave_name" >
			</div>
          </div>

          <div class="form-group row">
            <label for="cave_identifier" class="col-sm-2 control-label">Nr. identificare:</label>
			<div class="col-sm-10">
				<input type="text" class="form-control" id="cave_identifier" name="cave_identifier" ></textarea>
			</div>
          </div>		  

          <div class="form-group row">
            <label for="cave_description" class="col-sm-2 control-label">Descriere:</label>
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
		<label for="cave_type" class="col-sm-2 control-label">Tip:</label>	 
		<div class="col-sm-10">
			<select id="cave_type" class="selectpicker form-control" name="cave_type" data-hide-disabled="true" data-max-options="1">
			</select> <!-- multiple data-size="5"  -->
		</div>
	</div>
      <div class="modal-footer">
        <button type="button" class="btn btn-default" data-dismiss="modal">Inchide</button>
        <button type="submit" class="btn btn-primary" id="saveCave" >Salveaza</button>
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

</body>
</html>