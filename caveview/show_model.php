<?php
	@session_start();    
	header('Content-Type: text/html; charset=utf-8');

	require_once("../user/grid_common.php");

	require_once '../config.php';
	require_once '../auth.php';
	
	include_once ROOTPATH.'/data/db_common.php';
	
	include_once ROOTPATH.'/utilities.php';

	// $cave_id = @$_GET["cave_id"];
	$file_id = $_GET["file_id"];

	if (empty($file_id) || ctype_space($file_id))
		Utils::raise_error("file_id is empty");
	
	$file = null;
	$allFiles = null;

	if ($file_id == "all")
	{
		$con = Propel::getConnection("speogis"); // BookPeer::DATABASE_NAME

		$query = "SELECT files.* from `files`
			WHERE files.file_name LIKE '%.lox%' OR files.file_name LIKE '%.3d%'"; 
		// :p1 => '%| novel |%', :p2 => '%| russian |%'

		$stmt = $con->prepare($query);
		$res = $stmt->execute(); 		
		$allFiles = $stmt->fetchAll(PDO::FETCH_OBJ);//var_dump($res);		
		// print_r($allFiles);
		// $allFiles = FilesQuery::create()
		// 	->filterByFileName(array("%.lox%", "%.3d%"), Criteria::CONTAINS_SOME)
		// 	// ->filterByFilename("%.3d%")
		// 	->find();
	}
	else
		$file = FilesQuery::create()->findPK($file_id);

	// $caves = CavesQuery::create()->findById($cave_id); // findPK
	// else
	// $caves = CavesQuery::create()->find();	
	// echo $allFiles->toJson();
	// exit;
?>

<!-- //-- head section is redeclared after header.php -->
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xml:lang="en-gb" lang="en-gb" dir="ltr">
<head>
	<title>CaveView 3d cave viewer</title>
	<meta http-equiv="content-type" content="text/html; charset=utf-8" />
	<meta name="viewport" content="width=device-width, initial-scale=1.0">
	<link type="text/css" href="<?=WEBROOT ?>vendor/CaveView.js/build/CaveView/css/caveview.css" rel="stylesheet"/>
	<link type="text/css" href="<?=WEBROOT ?>caveview/css/silexgis_caveview.css" rel="stylesheet"/>
</head>
<body onload="onload();" >
<br/>
<br/>
<script type="text/javascript" src="http://nls.tileserver.com/api.js"></script>
<script type="text/javascript" src="<?=WEBROOT ?>vendor/CaveView.js/build/CaveView/lib/proj4-src.js" ></script>

<script type="text/javascript" src="<?=WEBROOT ?>vendor/CaveView.js/build/CaveView/lib/BingProvider.js" ></script>
<script type="text/javascript" src="<?=WEBROOT ?>vendor/CaveView.js/build/CaveView/lib/OSMProvider.js" ></script>
<script type="text/javascript" src="<?=WEBROOT ?>vendor/CaveView.js/build/CaveView/lib/NLSProvider.js" ></script>

<script type="text/javascript" src="<?=WEBROOT ?>vendor/CaveView.js/build/CaveView/js/CaveView.js" ></script>

<script type="text/javascript" >

function onload () 
{
	var url_base = 'http://' + window.location.host + '<?=$application_url_root?>' + "/";
	var file_directory = url_base + 'data/uploader/files/';

	<?php
	
	// $js_array = json_encode($php_array);
	if (!empty($file))
	{
	?>

	var file_name = "<?= $file->getFileName() ?>";
	var file_url = file_directory + file_name;

	<?php
	}
	else
		if (!empty($allFiles))
			//var_dump($allFiles)
				echo "var files = ". json_encode($allFiles) . ";\n"; // $allFiles->toJSON()
			else
				throw new Exception("php: input file(s) could not be loaded");
	?>
	
	CV.UI.init( "scene", { 
	terrainDirectory: file_directory,
	surveyDirectory: "", //"http://localhost/speogis/vendor/CaveView.js/build/surveys/", 
	home: "" // "http://localhost/speogis/vendor/CaveView.js/build/" 
	} );

	CV.Viewer.addOverlay( 'OSM', new OSMProvider() );
	CV.Viewer.addOverlay( 'NLS', new NLSProvider() ); // eslint-disable-line no-undef
	CV.Viewer.addOverlay( 'Bing Aerial',  new BingProvider( 'Aerial' ) );
	CV.Viewer.addOverlay( 'Bing OS', new BingProvider( 'OrdnanceSurvey' ) );

	if (file_url)
	{
		CV.UI.loadCave(file_url);
	}
	else
	if (files)
	{
		var file_paths = [];

		for (var key in files)
			if(files.hasOwnProperty(key))
			{
				var file = files[key];
				var url_base = 'http://' + window.location.host + '<?=$application_url_root?>' + "/";
				
				var file_url = file_directory + file.file_name; //FileName;

				file_paths.push(file_url);
			}

		CV.UI.loadCaveList(file_paths);
			// [ 				
			// "BlueWater.lox", 
			// "2046-81_ponorul_suspendat_surf.lox",
			// "Cheddar.lox", 
			// "P8_Master.3d", 
			// "all.3d", 
			// "Giants_Oxlow_Maskhill_System.3d", 
			// "Castleton_Master.3d", 
			
			//"Peak_Master_NoSurface.3d", 
			// "Peak_Master.3d", 
			// "loser.3d" 
			// ] 
	}
	else
		throw "input file(s) could not be loaded";

}

</script>
<div id="scene"></div>
<p>*{caveview_page.instructions}*</p>
*{caveview_page.caveview_credits}* <a rel="license" href="https://aardgoose.github.io/CaveView.js/">CaveView</a> (<a rel="license" href="https://github.com/aardgoose/CaveView.js">GitHub project</a>)
</body>
</html>