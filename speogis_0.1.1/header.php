<?php
	session_start(); //-- ?
    require_once 'auth.php';
	
	require_once 'config.php';    
	
	$currentPage = "";
	
	if (strpos($_SERVER['PHP_SELF'], 'index.php')) 
		$currentPage = "index";
	else
	if (strpos($_SERVER['PHP_SELF'], 'users.php')) 
		$currentPage = "users";
	else
	if (strpos($_SERVER['PHP_SELF'], 'points.php')) 
		$currentPage = "points";
	else
		$currentPage = "?";		
?>
<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">

  <style type="text/css">
  
    html, body, #mapdiv {
        width:100%; height:100%; margin:0;
		background-color: lightblue;				
		/*margin: 0 auto;*/
    }
  </style>
  
      <style>
      .mapdiv {        
        width: 100%;
		background-color: lightgreen;	
		/*margin: 0 auto;*/
		width: 100%;		
      }

html           { width:100%; height:100%; margin:0; }
body           { width:100%; height:100%; margin:0; font-family:sans-serif; }
/*#map           { width:100%; height:100%; margin:0; }*/
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
		
		.search_glyph_color {
			color: green;
		}
  </style>

<style>
	html,body{height:100%}

.simple {
    height: 100%;
    width: 100%
}
.simple div {
    overflow: auto
}
.vsplitbar {
    width: 5px;
    background: #cab
}
.vsplitbar:hover{
    background: #eab
}
</style>

  <title>Speo GIS</title>
<?php
$onlineMode = !false;
if ($onlineMode) :
?>
  <!--<script src="http://www.openlayers.org/api/OpenLayers.js"></script>-->
 <!--<script src='http://maps.google.com/maps?file=api&amp;v=3&amp;key=ABQIAAAAjpkAC9ePGem0lIq5XcMiuhR_wWLPFku8Ix9i2SXYRVK3e45q1BQUd_beF8dtzKET_EteAjPdGDwqpQ'></script>-->
<!-- <script src='http://maps.google.com/maps/api/js?v=3&amp;sensor=false'></script>-->

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
    
	<!-- xx <script src="http://ecn.dev.virtualearth.net/mapcontrol/mapcontrol.ashx?v=6.2&mkt=en-us"></script>-->
	
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol.js" type="text/javascript"></script>-->
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol-debug.js" type="text/javascript"></script>-->
	
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol-debug.js" type="text/javascript"></script>-->
	<!--<script src="http://openlayers.org/en/v3.2.0/build/ol-debug.js" type="text/javascript"></script>-->
	
	<link rel="stylesheet" href="/speogis/scripts/layout/layout-default-latest.css">
	
	<!--<script src="http://openlayers.org/en/v3.15.0/build/ol-debug.js" type="text/javascript"></script>-->	
	<!--<link rel="stylesheet" href="http://openlayers.org/en/v3.12.1/css/ol.css" type="text/css">    -->
	
	<script src="http://openlayers.org/en/v3.16.0/build/ol-debug.js" type="text/javascript"></script>
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
	<link rel="stylesheet" href="//cdnjs.cloudflare.com/ajax/libs/bootstrap-select/1.6.3/css/bootstrap-select.min.css"/>	
	<script type="text/javascript" src="//cdnjs.cloudflare.com/ajax/libs/bootstrap-select/1.6.3/js/bootstrap-select.min.js"></script>
	
	<link rel="stylesheet" href="/speogis/scripts/Ajax-Bootstrap-Select-master/dist/css/ajax-bootstrap-select.css"/>
	<script type="text/javascript" src="/speogis/scripts/Ajax-Bootstrap-Select-master/dist/js/ajax-bootstrap-select.js"></script>
	
	
	<!--<script type="text/javascript" src="/speogis/scripts/splitter.js"></script>-->

	<script src="/speogis/scripts/layout/jquery.layout-latest.js"></script>
	

<?php
	else :
?>
	<script> 
		//alert('offline resource mode, warning: old version, not updated from online mode'); 
		console.log('offline resource mode, warning: old version, not updated from online mode');
	</script>
<!-- //============================================================================================================================================= 
	 //============================================================================================================================================= 
		Offline
	-->
    <!--  <script src="http://ecn.dev.virtualearth.net/mapcontrol/mapcontrol.ashx?v=6.2&mkt=en-us"></script>
	
	
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol.js" type="text/javascript"></script>-->
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol-debug.js" type="text/javascript"></script>-->
	
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol-debug.js" type="text/javascript"></script>-->
	<!--<script src="http://openlayers.org/en/v3.2.0/build/ol-debug.js" type="text/javascript"></script>-->
	
	<link rel="stylesheet" href="/speogis/scripts/layout/layout-default-latest.css">
	
	
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

	<link rel="stylesheet" href="/speogis/scripts/bootstrap-select.min.css"/>	
	<script type="text/javascript" src="/speogis/scripts/bootstrap-select.min.js"></script>
		
	<link rel="stylesheet" href="/speogis/scripts/bootstrap-select-1.10.0/dist/css/bootstrap-select.css">
	<script src="/speogis/scripts/bootstrap-select-1.10.0/dist/js/bootstrap-select.js"></script>

	
	<link rel="stylesheet" href="/speogis/scripts/Ajax-Bootstrap-Select-master/dist/css/ajax-bootstrap-select.css"/>
	<script type="text/javascript" src="/speogis/scripts/Ajax-Bootstrap-Select-master/dist/js/ajax-bootstrap-select.js"></script>

	
	<script src="/speogis/scripts/layout/jquery.layout-latest.js"></script>
	

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
        <li <?php if ($currentPage == 'index') echo "class='active'"; ?> ><a href="/speogis/index.php">Map<span class="sr-only">(current)</span></a></li>
        <li <?php if ($currentPage == 'points') echo "class='active'"; ?> ><a href="/speogis/user/points.php">Data</a></li>
		<li <?php if ($currentPage == 'users') echo "class='active'"; ?> ><a href="/speogis/user/users.php">Users</a></li>

        <li role="separator" class="divider"></li>
		
		<li class="dropdown">
          <a href="#" class="dropdown-toggle" data-toggle="dropdown" role="button" aria-haspopup="true" aria-expanded="false">Add<span class="caret"></span></a>
          <ul class="dropdown-menu">
            <li><a href="#" onclick="enableDrawNewCave();" >Cave</a></li>

			<li role="separator" class="divider"></li>

            <li><a href="#">Surface features</a></li>			
			
				<li><a href="#" onclick="enableDrawNewFeature('sinkhole');" >Sinkhole</a></li>				
				<li><a href="#" onclick="" >Portal</a></li>
				<li><a href="#" onclick="" >...</a></li>
			
			
			
          </ul>
        </li>

		<li role="separator" class="divider"></li>
		
		<li class="dropdown">
			<a href="#" class="dropdown-toggle" data-toggle="dropdown" role="button" aria-haspopup="true" aria-expanded="false">Config<span class="caret"></span></a>
			<ul class="dropdown-menu">
				<li <?php if ($currentPage == '?') echo "class='active'"; ?> ><a href="/speogis/user/feature_types.php">Feature types<span class="sr-only">(current)</span></a></li>          
			</ul>
        </li>
		
		<li role="separator" class="divider"></li>
		
		<li class="dropdown">
          <a href="#" class="dropdown-toggle" data-toggle="dropdown" role="button" aria-haspopup="true" aria-expanded="false">Draw <span class="caret"></span></a>
          <ul class="dropdown-menu">
            <li><a href="#" onclick="selectDrawFeature('Point');" >Point</a></li>
            <li><a href="#" onclick="selectDrawFeature('LineString');" >Line</a></li>
            <li><a href="#" onclick="selectDrawFeature('Polygon');" >Polygon</a></li>            
          </ul>
        </li>
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
	  
	  <?php if ($currentPage == 'index') : ?>
		<form class="navbar-form navbar-left" role="search">
			<div class="input-group">
				<select id="search_control" class="selectpicker with-ajax" data-live-search="true" data-abs-preserve-selected="false" ></select>
				<!-- data-showSubtext="false" -->
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
	  <?php else : ?>
	  
	  <?php endif; ?>
	  
      <ul class="nav navbar-nav navbar-right">
        <!--<li><a href="#">Link</a></li>-->
        <li class="dropdown">
          <a href="#" class="dropdown-toggle" data-toggle="dropdown" role="button" aria-haspopup="true" aria-expanded="false">User <span class="caret"></span></a>
          <ul class="dropdown-menu">
            <li><a href="/speogis/logout.php">Log out</a></li>
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
<?php ?>