$(document).ready(function() {
	
	//initGeoreferencedMapForm();
	
	initGeoreferencedMapUploadControl();
	
	/*$('#add_georeferenced_image').on('click', function(event) {
		addGeoreferencedMap();
	});*/
  });

function openUploadGeoreferencedMapForm() // cave_id, cave_entrance_id, feature_id
{			
	$("#georeferencedMapForm")[0].reset();
		
	$('#georeferencedMapModalTitleLabel').text("Add new georeferenced map");

	
	/*	
	$('#saveCave').on('click', function(e) {
		//e.preventDefault(); // To prevent following the link (optional)		
		//onSaveCave(this);
		//$(this).submit();
	});
	*/		

	$('#georeferencedMapForm').off('submit');
	
	$('#georeferencedMapForm').on('submit', function(e) {
			e.preventDefault();

			var formInputRegularData = $(this).serializeObject();
			//var formData = new FormData($(this));
			formData = new FormData($(this));
			formData.append( 'form_data', JSON.stringify(formInputRegularData));
			formData.append( 'file', $( '#georeferencedMapUploadControl' )[0].files[0] );
			//formData.append( 'file', $( '#file' )[0].files[0] );
			
			/*formData.append('picture_coords_lat', $('#picture_coords_lat').val());
			formData.append('picture_coords_lon', $('#picture_coords_lon').val());
			formData.append('picture_id', $('#picture_id').val());
			formData.append('picture_existing_point_id', $('#picture_existing_point_id').val());
			*/
			
		  //var serializedFormData = JSON.stringify(formData);
		  
		$.ajax({
			url: url_base + 'data/postGeoreferencedMap.php',
			data: formData,
			processData: false,
			contentType: false,
			type: 'POST',
			success: function(php_script_response){    
								if (php_script_response.indexOf("201") >= 0) // if (php_script_response == "201")
									{
										console.log('close georeferenced map form');
										$('#georeferencedMapModal').modal('toggle');
										
										showNotification("Picture <b>" + formData.georeferenced_map_title +"</b> was added.");
										
										//-- dirty way to see if we're on map page or on georeferenced images page
										if (typeof reloadMapFeatures === "function")
											reloadMapFeatures();
										else
											location.reload();
									}
								else
									alert(php_script_response); // display response from the PHP script, if any
			}
		});
	});
	
	$('#georeferencedMapModal').modal();
/*
		  postDataAsync("data/postPicture.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#pictureModal').modal('toggle');
				reloadMapFeatures();				
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
*/		
	//fillCaveEntries();
	
	/*if (picture_id)
	{
		$.getJSON("data/getPicture.php?picture_id=" + picture_id, function( data ) {
			
			$('#pictureModal').modal();
			
			$('#picture_id').val(data.Id);
			$('#picture_name').val(data.Name);				
			$('#picture_description').val(data.Description);
			$('#picture_type_id').val(data.Feature_type_id);

			$('#point_coords_lat').val("");
			$('#point_coords_lon').val("");
			$('#point_string').val("");
			//$('#picture_existing_point_id').val("");
			//_caveFormServerData = data;
			//$('#featureModal').modal();
		});
	}*/
}

function addGeoreferencedMap()
{
	openUploadGeoreferencedMapForm(undefined, undefined);	
}

/*function deletePictures(picture_id)
{
	var question_text = "";
	
	question_text = "Doriti sa stergeti poza?";
	var q_res = confirm(String.format(question_text));
		
	if (q_res)
	{		
		var picture_delete_data = {
			picture_id: picture_id,		
		};
	
	//var view_name = map_views[map_view_id].mapview_name;
	
    $.ajax({
                url: 'data/deletePictures.php', // point to server-side PHP script 
                dataType: 'text',  // what to expect back from the PHP script, if anything
				
				data: JSON.stringify(picture_delete_data), // JSON.stringify({ Markers: markers })
				contentType: "application/json; charset=utf-8",
				//dataType: "json",
				
                cache: false,
                contentType: false,
                processData: false,
                //data: view_data,
                type: 'post',
                success: function(php_script_response){					
					if (php_script_response.indexOf("201") >= 0) // if (php_script_response == "201")
					{
						closePictureBox();
						showNotification("Picture <b>" + "" + "</b> was deleted.", { from: "bottom", align: "right" });
						//refreshViewList();//alert("saved");
						//-- must delete the thumbnails from map and from slider
					}
					else
						alert(php_script_response); // display response from the PHP script, if any
                }
     });			 	 	 
	 }
}
*/

function initGeoreferencedMapUploadControl()
{
	/*$('#fileupload').fileupload({
		//formData: {example: 'test'}
		url: 'speogis/data/uploader'
	}).on('fileuploadsubmit', function (e, data) {
		data.formData = data.context.find(':input').serializeArray();
	});
	
	$('#fileupload').bind('fileuploadsubmit', function (e, data) {
		// The example input, doesn't have to be part of the upload form:
		var input = $('#input');
		data.formData = {example: input.val()};
		if (!data.formData.example) {
		  data.context.find('button').prop('disabled', false);
		  input.focus();
		  return false;
		}
	});
	*/
    //'use strict';

    // Initialize the jQuery File Upload widget:
  
  /*$('#pictureUploader').fileupload({
        // Uncomment the following to send cross-domain cookies:
        //xhrFields: {withCredentials: true},
        url: 'data/uploader/picture_uploader.php'
		//url: 'data/uploader'
    });
	*/
	
	/*
    // Enable iframe cross-domain access via redirect option:
    $('#fileupload').fileupload(
        'option',
        'redirect',
        window.location.href.replace(
            /\/[^\/]*$/,
            '/cors/result.html?%s'
        )
    );
	*/
	
	/*
    if (window.location.hostname === 'blueimp.github.io') {
        // Demo settings:
        $('#fileupload').fileupload('option', {
            url: 'speogis/data/uploader',
            // Enable image resizing, except for Android and Opera,
            // which actually support image resizing, but fail to
            // send Blob objects via XHR requests:
            disableImageResize: /Android(?!.*Chrome)|Opera/
                .test(window.navigator.userAgent),
            maxFileSize: 999000,
            acceptFileTypes: /(\.|\/)(gif|jpe?g|png)$/i
        });
        // Upload server status check for browsers with CORS support:
        if ($.support.cors) {
            $.ajax({
                url: '//jquery-file-upload.appspot.com/',
                type: 'HEAD'
            }).fail(function () {
                $('<div class="alert alert-danger"/>')
                    .text('Upload server currently unavailable - ' +
                            new Date())
                    .appendTo('#fileupload');
            });
        }
    } else 
	*/
	{
        // Load existing files:
		console.log('#georeferencedMapUploadControl');
		//console.log($('#georeferencedMapUploadControl').fileupload('option', 'url'));
		
        //$('.fileupload').addClass('fileupload-processing');

        //..$('#pictureUploader').addClass('fileupload-processing');
        
		/*$.ajax({
            // Uncomment the following to send cross-domain cookies:
            //xhrFields: {withCredentials: true},
			url: url_base + "/data/postGeoreferencedMap.php",
            //url: $('#georeferencedMapUploadControl').fileupload('option', 'url'),
            dataType: 'json',
            context: $('#georeferencedMapUploadControl')[0]
        }).always(function () {
            $(this).removeClass('fileupload-processing');
        }).done(function (result) {
            $(this).fileupload('option', 'done')
                .call(this, $.Event('done'), {result: result});
        });
		*/
		// https://github.com/blueimp/jQuery-File-Upload/wiki/API
		
		// $('#georeferencedMapUploadControl').fileupload({
		$('.fileupload').fileupload({
			url: url_base + "/data/postGeoreferencedMap.php",
			//sequentialUploads: true
		});
    }	
	
	// $('#fileupload').fileupload('destroy');
}