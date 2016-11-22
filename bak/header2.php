<?php
	session_start();
    require_once 'auth.php';
	require_once 'config.php';
    
    //echo "<a href='view_emails.php' >Email addresses</a> | 
    //      <a href='scan.php' >Manual check</a>";
    
    //echo "<a href='login.php?out=1' >Log out</a><br/>"
?>

<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">

	<!--<script type="text/javascript" src="/speogis/scripts/jquery-1.12.3.js"></script>
	<!--<script type="text/javascript" src="/speogis/scripts/jquery-ui.min.js"></script>	-->
	<link rel="stylesheet" href="/speogis/scripts/jquery-ui.css" type="text/css">

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
	
      .tooltip {
        position: relative;
        background: rgba(0, 0, 0, 0.5);
        border-radius: 4px;
        color: white;
        padding: 4px 8px;
        opacity: 0.7;
        white-space: nowrap;
      }
      .tooltip-measure {
        opacity: 1;
        font-weight: bold;
      }
      .tooltip-static {
        background-color: #ffcc33;
        color: black;
        border: 1px solid white;
      }
      .tooltip-measure:before,
      .tooltip-static:before {
        border-top: 6px solid rgba(0, 0, 0, 0.5);
        border-right: 6px solid transparent;
        border-left: 6px solid transparent;
        content: "";
        position: absolute;
        bottom: -6px;
        margin-left: -7px;
        left: 50%;
      }
      .tooltip-static:before {
        border-top-color: #ffcc33;
      }    </style>









  <title>SilexGIS</title></br/>

  	<script type="text/javascript" src="/speogis/scripts/jquery-ui.js"></script>	
	<link rel="stylesheet" href="/speogis/scripts/jquery-ui.css" type="text/css">

  <!--<script src="http://www.openlayers.org/api/OpenLayers.js"></script>-->
 <!--<script src='http://maps.google.com/maps?file=api&amp;v=3&amp;key=ABQIAAAAjpkAC9ePGem0lIq5XcMiuhR_wWLPFku8Ix9i2SXYRVK3e45q1BQUd_beF8dtzKET_EteAjPdGDwqpQ'></script>-->
 <!--<script src='http://maps.google.com/maps/api/js?v=3&amp;sensor=false'></script>-->

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
<!--
    <script src="http://ecn.dev.virtualearth.net/mapcontrol/mapcontrol.ashx?v=6.2&mkt=en-us"></script>
	-->
	<!--<script src="http://openlayers.org/en/v3.12.1/build/ol.js" type="text/javascript"></script>-->
	<script src="/speogis/scripts/ol_v3.15.1-dist/ol-debug.js" type="text/javascript"></script>	
	<link rel="stylesheet" href="/speogis/scripts/ol_v3.15.1-dist/ol.css" type="text/css">    
	

	<!--<script type="text/javascript" src="http://ajax.googleapis.com/ajax/libs/jquery/1.11.3/jquery.min.js"></script>-->
    
	<script src="./ol3-popup-master/src/ol3-popup.js"></script>
	<link rel="stylesheet" href="./ol3-popup-master/src/ol3-popup.css" />
	
  <meta name="description" content="" />

  
</head>
<body>

<ul id='topmenu' >

  <li><p style='logo'>Silex GIS &nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp&nbsp</p></li>
  <li> </li>
  <li> </li>
  <li><a <?php if (strpos($_SERVER['PHP_SELF'], 'index.php')) echo "class='active'"; ?> href="/speogis/index.php">Harta</a></li>
  <li><a <?php if (strpos($_SERVER['PHP_SELF'], 'points.php')) echo "class='active'"; ?> href="/speogis/user/points.php">Date</a></li>
  <li><a <?php if (strpos($_SERVER['PHP_SELF'], 'users.php')) echo "class='active'"; ?> href="/speogis/user/users.php">Utilizatori</a></li>  
  <li id='buttonRight' style='float: right;' ><a <?php if (strpos($_SERVER['PHP_SELF'], 'users.php')) echo "class='active'"; ?> href="/speogis/logout.php">Afara</a></li>  
</ul>