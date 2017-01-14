<?php
	exit;	//                    ?????????????????????????
	@session_start(); //-- ?
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
	if (strpos($_SERVER['PHP_SELF'], 'team_members.php')) 
		$currentPage = "team_members";
	else
	if (strpos($_SERVER['PHP_SELF'], 'feature_types.php')) 
		$currentPage = "feature_types";
	else
	if (strpos($_SERVER['PHP_SELF'], 'trip_reports.php')) 
		$currentPage = "feature_types";
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
#layerswitcher_header { margin:0; padding:10px; border-radius:4px; background:rgba(0, 60, 136, 0.5); list-style-type:none; } 
	  
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

  <title>Silex GIS</title>
<?php
$onlineMode = false;
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
	<link rel="stylesheet" href="http://openlayers.org/en/v3.16.0/css/ol.css" type="text/css">    

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
	
	<link rel="stylesheet" href="/speogis/scripts/lightbox-master/dist/ekko-lightbox.min.css"/>
	<script type="text/javascript" src="/speogis/scripts/lightbox-master/dist/ekko-lightbox.js"></script>
	
	<script type="text/javascript" src="/speogis/scripts/typeahead.bundle.js"></script>
	<link rel="stylesheet" href="/speogis/scripts/search_typeahead.css"/>
	
	<script type="text/javascript" src="/speogis/scripts/handlebars-v4.0.5.js"></script>
	<!--<link rel="stylesheet" href="/speogis/scripts/_typeahead_normalize.min.css">-->
	
	<link href="//cdn.jsdelivr.net/openlayers.geocoder/latest/ol3-geocoder.min.css" rel="stylesheet">
	<script src="//cdn.jsdelivr.net/openlayers.geocoder/latest/ol3-geocoder.js"></script>	
	
	<link rel="stylesheet" type="text/css" href="https://cdn.datatables.net/v/bs/dt-1.10.12/af-2.1.2/b-1.2.1/b-colvis-1.2.1/cr-1.3.2/fh-3.1.2/r-2.1.0/datatables.css"/> 
	<script type="text/javascript" src="https://cdn.datatables.net/v/bs/dt-1.10.12/af-2.1.2/b-1.2.1/b-colvis-1.2.1/cr-1.3.2/fh-3.1.2/r-2.1.0/datatables.js"></script>	
	<link rel="stylesheet" type="text/css" href="https://cdn.datatables.net/1.10.12/css/jquery.dataTables.min.css"/> 										
	<link rel="stylesheet" type="text/css" href="https://cdn.datatables.net/1.10.12/js/dataTables.bootstrap4.min.js"/>

<!--
https://cdn.datatables.net/1.10.12/js/jquery.dataTables.min.js
https://cdn.datatables.net/1.10.12/js/dataTables.bootstrap4.min.js	
-->
<!--<script src="//ajax.googleapis.com/ajax/libs/jquery/1.11.0/jquery.min.js"></script>-->
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/vendor/jquery.ui.widget.js"></script>
<script src="//blueimp.github.io/JavaScript-Templates/js/tmpl.min.js"></script>
<script src="//blueimp.github.io/JavaScript-Load-Image/js/load-image.all.min.js"></script>
<script src="//blueimp.github.io/JavaScript-Canvas-to-Blob/js/canvas-to-blob.min.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.iframe-transport.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload.js"></script>	

<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-process.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-image.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-audio.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-video.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-validate.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-ui.js"></script>

<!--<script src="//code.jquery.com/qunit/qunit-1.15.0.js"></script>-->

<link rel="stylesheet" href="/speogis/scripts/jQuery-File-Upload-9.12.5/css/jquery.fileupload.css">
<link rel="stylesheet" href="/speogis/scripts/jQuery-File-Upload-9.12.5/css/jquery.fileupload-ui.css">
<!-- CSS adjustments for browsers with JavaScript disabled -->
<noscript><link rel="stylesheet" href="/speogis/scripts/jQuery-File-Upload-9.12.5/css/jquery.fileupload-noscript.css"></noscript>
<noscript><link rel="stylesheet" href="/speogis/scripts/jQuery-File-Upload-9.12.5/css/jquery.fileupload-ui-noscript.css"></noscript>


	<!-- Control -->
	<link rel="stylesheet" href="/speogis/scripts/ol3-ext-gh-pages/control/layerswitchercontrol.css" />
	<script type="text/javascript" src="/speogis/scripts/ol3-ext-gh-pages/control/layerswitchercontrol.js"></script>

	<link rel="stylesheet" href="/speogis/scripts/ol3_ext_style.css" />

	<link rel="stylesheet" href="/speogis/scripts/ol3-ext-gh-pages/control/permalinkcontrol.css" />
	<script type="text/javascript" src="/speogis/scripts/ol3-ext-gh-pages/control/permalinkcontrol.js"></script>
	
	<link rel="stylesheet" href="/speogis/scripts/main_map.css" />
	
	<link href="//cdn.jsdelivr.net/openlayers.contextmenu/latest/ol3-contextmenu.min.css" rel="stylesheet">
	<script src="//cdn.jsdelivr.net/openlayers.contextmenu/latest/ol3-contextmenu.js"></script>

	<!--<script src="/speogis/scripts/bootstrap-notify-master/bootstrap-notify.min.js"></script>-->
	<script src="/speogis/scripts/bootstrap-notify-master/bootstrap-notify.js"></script>
	
	<script src="/speogis/scripts/i18n/silexgis.en-EN.js"></script>
	
	<link href="/speogis/scripts/bootstrap-spinedit/js/bootstrap-spinedit.css" rel="stylesheet">
	<script src="/speogis/scripts/bootstrap-spinedit/js/bootstrap-spinedit.js"></script>
	
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
	
	<link rel="stylesheet" href="/speogis/scripts/lightbox-master/dist/ekko-lightbox.min.css"/>
	<script type="text/javascript" src="/speogis/scripts/lightbox-master/dist/ekko-lightbox.js"></script>
	
	<script type="text/javascript" src="/speogis/scripts/typeahead.bundle.js"></script>
	<link rel="stylesheet" href="/speogis/scripts/search_typeahead.css"/>
	
	<script type="text/javascript" src="/speogis/scripts/handlebars-v4.0.5.js"></script>
	<!--<link rel="stylesheet" href="/speogis/scripts/_typeahead_normalize.min.css">-->
	
	<link href="/speogis/scripts/ol3-geocoder.min.css" rel="stylesheet">
	<script src="/speogis/scripts/ol3-geocoder.js"></script>	
	
	<link rel="stylesheet" type="text/css" href="/speogis/scripts/datatables.css"/> 
	<script type="text/javascript" src="/speogis/scripts/datatables.js"></script>	
	<link rel="stylesheet" type="text/css" href="/speogis/scripts/jquery.dataTables.min.css"/> 										
	<link rel="stylesheet" type="text/css" href="/speogis/scripts/dataTables.bootstrap4.min.js"/>

<!--
https://cdn.datatables.net/1.10.12/js/jquery.dataTables.min.js
https://cdn.datatables.net/1.10.12/js/dataTables.bootstrap4.min.js	
-->
<!--<script src="//ajax.googleapis.com/ajax/libs/jquery/1.11.0/jquery.min.js"></script>-->
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/vendor/jquery.ui.widget.js"></script>
<script src="//blueimp.github.io/JavaScript-Templates/js/tmpl.min.js"></script>
<script src="//blueimp.github.io/JavaScript-Load-Image/js/load-image.all.min.js"></script>
<script src="//blueimp.github.io/JavaScript-Canvas-to-Blob/js/canvas-to-blob.min.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.iframe-transport.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload.js"></script>	

<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-process.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-image.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-audio.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-video.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-validate.js"></script>
<script src="/speogis/scripts/jQuery-File-Upload-9.12.5/js/jquery.fileupload-ui.js"></script>

<!--<script src="//code.jquery.com/qunit/qunit-1.15.0.js"></script>-->

<link rel="stylesheet" href="/speogis/scripts/jQuery-File-Upload-9.12.5/css/jquery.fileupload.css">
<link rel="stylesheet" href="/speogis/scripts/jQuery-File-Upload-9.12.5/css/jquery.fileupload-ui.css">
<!-- CSS adjustments for browsers with JavaScript disabled -->
<noscript><link rel="stylesheet" href="/speogis/scripts/jQuery-File-Upload-9.12.5/css/jquery.fileupload-noscript.css"></noscript>
<noscript><link rel="stylesheet" href="/speogis/scripts/jQuery-File-Upload-9.12.5/css/jquery.fileupload-ui-noscript.css"></noscript>


	<!-- Control -->
	<link rel="stylesheet" href="/speogis/scripts/ol3-ext-gh-pages/control/layerswitchercontrol.css" />
	<script type="text/javascript" src="/speogis/scripts/ol3-ext-gh-pages/control/layerswitchercontrol.js"></script>

	<link rel="stylesheet" href="/speogis/scripts/ol3_ext_style.css" />

	<link rel="stylesheet" href="/speogis/scripts/ol3-ext-gh-pages/control/permalinkcontrol.css" />
	<script type="text/javascript" src="/speogis/scripts/ol3-ext-gh-pages/control/permalinkcontrol.js"></script>
	
	<link rel="stylesheet" href="/speogis/scripts/main_map.css" />
	
	<link href="/speogis/scripts/ol3-contextmenu.min.css" rel="stylesheet">
	<script src="/speogis/scripts/ol3-contextmenu.js"></script>

	<!--<script src="/speogis/scripts/bootstrap-notify-master/bootstrap-notify.min.js"></script>-->
	<script src="/speogis/scripts/bootstrap-notify-master/bootstrap-notify.js"></script>
	
	<script src="/speogis/scripts/i18n/silexgis.en-EN.js"></script>

	<link href="/speogis/scripts/bootstrap-spinedit/css/bootstrap-spinedit.css" rel="stylesheet">
	<script src="/speogis/scripts/bootstrap-spinedit/js/bootstrap-spinedit.js"></script>

	<!-- Latest compiled and minified CSS --
    <link rel="stylesheet" href="//www.fuelcdn.com/fuelux/3.13.0/css/fuelux.min.css">
    <!-- Latest compiled and minified JavaScript --
    <script src="//www.fuelcdn.com/fuelux/3.13.0/js/fuelux.min.js"></script>
	-->

	<link href="/speogis/scripts/bootstrap-touchspin/src/jquery.bootstrap-touchspin.css" rel="stylesheet">
	<script src="/speogis/scripts/bootstrap-touchspin/src/jquery.bootstrap-touchspin.js"></script>

<link href="//cdn.rawgit.com/Eonasdan/bootstrap-datetimepicker/e8bddc60e73c1ec2475f827be36e1957af72e2ea/build/css/bootstrap-datetimepicker.css" rel="stylesheet">	
<script src="//cdnjs.cloudflare.com/ajax/libs/moment.js/2.9.0/moment-with-locales.js"></script>
<script src="//cdn.rawgit.com/Eonasdan/bootstrap-datetimepicker/e8bddc60e73c1ec2475f827be36e1957af72e2ea/src/js/bootstrap-datetimepicker.js"></script>

<link rel="stylesheet" href="/speogis/scripts/bootstrap-tagsinput-master/dist/bootstrap-tagsinput.css">
	<script src="/speogis/scripts/bootstrap-tagsinput-master/dist/bootstrap-tagsinput.js"></script>
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
      <a class="navbar-brand" href="index.php">SilexGIS</a>
    </div>

    <!-- Collect the nav links, forms, and other content for toggling -->
    <div class="collapse navbar-collapse" id="bs-example-navbar-collapse-1">
      <ul class="nav navbar-nav">
        <li <?php if ($currentPage == 'index') echo "class='active'"; ?> ><a href="/speogis/index.php">*{main_map.menu.map}*<span class="sr-only">(current)</span></a></li>
        <li <?php if ($currentPage == 'points') echo "class='active'"; ?> ><a href="/speogis/user/points.php">*{main_map.menu.data}*</a></li>
		<li <?php if ($currentPage == 'files') echo "class='active'"; ?> ><a href="/speogis/user/files.php">Files</a></li>
		<li <?php if ($currentPage == 'users') echo "class='active'"; ?> ><a href="/speogis/user/users.php">Users</a></li>		
		<li <?php if ($currentPage == 'trip_reports') echo "class='active'"; ?> ><a href="/speogis/user/trip_reports.php">Reports</a></li>		

        <li role="separator" class="divider"></li>
		
		<li class="dropdown">
          <a href="#" class="dropdown-toggle" data-toggle="dropdown" role="button" aria-haspopup="true" aria-expanded="false">Add<span class="caret"></span></a>
          <ul class="dropdown-menu">
            <!--<li><a href="#" onclick="enableDrawNewCave();" >Cave</a></li>-->
			<li><a href="#" onclick="enableDrawNewPicture();" >Picture</a></li>
			<li><a href="#" onclick="addPictures();" >Pictures</a></li>
			<li><a href="#" onclick="addView();" >View</a></li>
			<li><a href="#" onclick="addTripReport();" >Trip report</a></li>

			<!--
			<li role="separator" class="divider"></li>

			<!--
            <li><a href="#">Surface features</a></li>			
			
				<li><a href="#" onclick="enableDrawNewFeature('sinkhole');" >Sinkhole</a></li>				
				<li><a href="#" onclick="" >Portal</a></li>
				<li><a href="#" onclick="" >...</a></li>
			-->
			
			
          </ul>
        </li>

		<li role="separator" class="divider"></li>
		
		<li class="dropdown">
			<a href="#" class="dropdown-toggle" data-toggle="dropdown" role="button" aria-haspopup="true" aria-expanded="false">Config<span class="caret"></span></a>
			<ul class="dropdown-menu">
				<li <?php if ($currentPage == 'feature_types') echo "class='active'"; ?> ><a href="/speogis/user/feature_types.php">Feature types<span class="sr-only">(current)</span></a></li>          
				<li <?php if ($currentPage == 'team_members') echo "class='active'"; ?> ><a href="/speogis/user/team_members.php">Team members<span class="sr-only">(current)</span></a></li>          
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

		<li class="dropdown">
          <a href="#" class="dropdown-toggle" data-toggle="dropdown" role="button" aria-haspopup="true" aria-expanded="false">Tools <span class="caret"></span></a>
          <ul class="dropdown-menu">
            <li><a id="export_map" href="#" onclick="" download="map.png" >Export</a></li>
			<!--<li><a href="#" onclick="exportMap" download="map.png" >Export</a></li>-->
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
			<!--<div class="input-group">				
				<select id="search_control" class="selectpicker with-ajax" data-live-search="true" data-abs-preserve-selected="false" ></select>
				// data-showSubtext="false"
			</div>
			-->
			<div class="input-group" id="searchFeatureControlContainer" >
				<input class="Typeahead-hint" type="text" tabindex="-1" readonly>
				<input class="Typeahead-input" type="text" id="searchFeatureControl" placeholder="Search features" > <!-- class="typeahead" -->
				<div class="Typeahead-menu"></div>
			</div>		
			
			<!-- from view-source:http://twitter.github.io/typeahead.js/ -->
		</form>
<!--		
		<form action="https://twitter.com/search" method="get">
            <input type="hidden" name="mode" value="users">
            <div class="Typeahead Typeahead--twitterUsers">
              <div class="u-posRelative">
                <input class="Typeahead-hint" type="text" tabindex="-1" readonly>
                <input class="Typeahead-input" id="searchFeatureControlxx" type="text" name="q" placeholder="Search Twitter users...">
                <img class="Typeahead-spinner" src="img/spinner.gif">
              </div>
              <div class="Typeahead-menu"></div>
            </div>
            <button class="u-hidden" type="submit">blah</button>
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