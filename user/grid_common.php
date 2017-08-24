<?php
	//session_start();    
	require_once dirname(__DIR__).'/config.php';
	//$root = realpath($_SERVER["DOCUMENT_ROOT"])."/silexgis/demo/";
	$root = ROOTPATH;
	
	require_once "$root/auth.php";
	//echo "<header__>";
	include_once "$root/header.php";
	
  define ("DATAGRID_DIR", "../datagrid_class_4_2_8/");
  define ("PEAR_DIR", "../datagrid_class_4_2_8/pear/");
  
  require_once(DATAGRID_DIR.'datagrid.class.php');
  require_once(PEAR_DIR.'PEAR.php');
  require_once(PEAR_DIR.'DB.php');

  require_once("$root/config.php");
  
  //include_once "$root/header.php"; // include_once 'db_interface.php'; require_once('utilities.php'); 
  //require_once('utilities.php'); include_once 'db_interface.php'; include_once 'data_interface.php'; require_once 'languages.php'; 
	//echo "$root/header.php"; exit;
	//GG 
	//echo "</header__>";
	//echo "</body></html>";
	//exit;
	//require_once "$root/vendor/Propel/runtime/lib/Propel.php";
	//Propel::init("$root/db/propel_1_6_pr/build/conf/speogis-conf.php");
	//set_include_path("$root/db/propel_1_6_pr/build/classes" . PATH_SEPARATOR . get_include_path());
	
	//<!--<script>
	//$(document).ready(function() {
	//});
	//</script>-->
?>
<script type="text/javascript" src="<?=WEBROOT ?>scripts/user_common.js"></script>

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