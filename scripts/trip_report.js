  //var document_onkeyup = undefined;
  
  //var _geofiles = undefined;
  var _team_members = undefined;
  //var url_base = 'http://' + window.location.host + "/speogis/";
  //var current_url_path = window.location.host;// + "/speogis";//window.location.pathname;
  
$(document).ready(function() {
	//console.log("x1 34");
	
	//localize_static_html();
	//localize_static_html(); // again?
	//document.getElementsByTagName("html")[0].style.visibility = "visible";	
	
	initTripReportForm();
	//return;
	initTripFormUploadControls();
	initTripReportUploadControl();
	initPicturesUploadControl();
	
	//initFeatureSearchControl();
	initTripFeatureSearchControl();

	initCaveFilesTable();
  });

//////////////////////////
// Begin Trip Report form

function addTripReport()
{
	openTripReportForm(undefined);
	
	//if (feature_id == undefined)
	//$('#tripReportModal').modal();
}

function openTripReportForm(trip_log_id) // cave_id, cave_entrance_id, feature_id
{			
	//$('#upload_xx_cave_id').val("");
	
	//$('#uploadFilesForm').off('submit');
	//$("#uploadFilesForm")[0].reset();
	
	if (trip_log_id)
		$('#tripReportModalTitleLabel').text("Edit '" + "" +"'");

	if (trip_log_id)
	{
		$.getJSON(url_base + "data/getTripReport.php?trip_log_id=" + trip_log_id, function( data ) {
			
			//$('#tripReportModal').modal();
			
			$('#trip_log_id').val(data.Id);
			
			//-- keep only one
			//$('#tripreportStartTimeControl').datetimepicker().val(data.TripStartTime);
			//$('#tripreportEndTimeControl').datetimepicker().val(data.TripEndTime);

			//$('#tripreportStartTimeControl').data("DateTimePicker").date(data.TripStartTime);
			//$('#tripreportEndTimeControl').data("DateTimePicker").date(data.TripEndTime);
			
			$('#tripreport_start_time').val(data.TripStartTime);
			$('#tripreport_end_time').val(data.TripEndTime);
			
			$('#tripreport_details').val(data.Details);
			$('#tripreport_place').val(data.TargetZone);
			
			//$('#tripreport_members').val(data.Members);
			
			//$('#tripreport_members').tagsinput('items');
			
			
			$('#tripreport_members').tagsinput('removeAll');
			
			data.Members.forEach(function(team_member) {
				$('#tripreport_members').tagsinput('add', _team_members[team_member]);
			});
			
			$('#tripreport_members').tagsinput('refresh');
			//$('#').val(data.Feature_type_id);
									
			$('#tripReportModal').modal();
		});
	}
	else
	{
		$('#trip_log_id').val("");
		$('#tripreport_start_time').val("");
		$('#tripreport_end_time').val("");				
		$('#tripreport_details').val("");
		$('#tripreport_place').val("");
		$('#tripreport_members').val("");
		$('#tripreport_members').tagsinput('removeAll');
		$('#tripreport_features').val("");
		
          var formData = $('#tripReportForm').serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  formData.temporary = 1;
		  
		  postDataAsync(url_base + "data/postTripReport.php", formData, 
			function(response) 
			{ 
		$('#trip_log_id').val(response.Id);
		// $('#tripreport_start_time').val("");
		// $('#tripreport_end_time').val("");				
		// $('#tripreport_details').val("");
		// $('#tripreport_members').val("");
		// $('#tripreport_members').tagsinput('removeAll');
		
		$('#tripReportModal').modal();
			
				//console.log('close');
				//$('#tripReportModal').modal('toggle'); 
				
				showNotification("temp Trip report");
				//reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/ 
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
	
		// $.getJSON("data/getTripReport.php?trip_log_id=" + trip_log_id, function( data ) {				

		// // $('#trip_log_id').val("");
		// // $('#tripreport_start_time').val("");
		// // $('#tripreport_end_time').val("");				
		// // $('#tripreport_details').val("");
		// // $('#tripreport_members').val("");
		// // $('#tripreport_members').tagsinput('removeAll');
		
		// // $('#tripReportModal').modal();
		// });
		
	}
	
	if (trip_log_id)
	{
		refreshTripReportFilesTable(trip_log_id);
		getFeatureList(trip_log_id);
	
		/*$('#uploadFilesModal').on('hidden.bs.modal', function () {
			//-- this is not executed, maybe ovewritten by the other event
			refreshTripReportFilesTable(trip_log_id);
		});
		*/
	}
	
	$('#trip_report_files_table').DataTable().clear();
	$('#trip_report_files_table').DataTable().draw();	
}

function initTripReportForm()
{
	$('#trip_report_files_table').DataTable( {
        //"ajax": '../ajax/data/arrays.txt'
    } );

	$('#addFilesToTripReport').on('click', function(e) {
		e.preventDefault(); // To prevent following the link (optional)		
		addTripFiles();
		
		//onSaveCave(this);
		//$(this).submit();
	});
	
	$('#tripReportForm').on('submit', function(e) {
          e.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync(url_base + "data/postTripReport.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#tripReportModal').modal('toggle'); 
				
				showNotification("Trip report <b>" + "" + "</b> was saved.");
				location.reload();
				//reloadMapFeatures();
				/* //-- $("caveModal").modal('hide');*/ 
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
		
	//fillCaveEntries();	
	
	$('#tripreportStartTimeControl').datetimepicker({
		//defaultDate: "11/1/2013",
        /*disabledDates: [
							moment("12/25/2013"),
							new Date(2013, 11 - 1, 21),
							"11/22/2013 00:53"
						]
						*/
	});	

	$('#tripreportEndTimeControl').datetimepicker({
		//defaultDate: "11/1/2013",
        /*disabledDates: [
							moment("12/25/2013"),
							new Date(2013, 11 - 1, 21),
							"11/22/2013 00:53"
						]*/
	});	

/*var tm2 = new Bloodhound({
  datumTokenizer: Bloodhound.tokenizers.obj.whitespace('name'),
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  prefetch: {
    url: 'data/getTeamMembers.php',
    filter: function(list) {
      return $.map(list, function(cityname) {
        return { name: cityname }; });
    }
  }
});
tm2.initialize();
*/
var url = url_base + 'data/getTeamMembers.php';
//var url = 'http://localhost/speogis/data/getTeamMembers.php';
$.getJSON(url, function( data ) 
{	
var items = [];
_team_members = [];

$.each( data, function( key, val ) {
		
		//val.short_name = val.FirstName + " " + val.LastName;
		val.short_name = val.FirstName;
		
		if (val.LastName && val.LastName.length > 0)
			val.short_name += " " + val.LastName.substr(0, 1) + ".";
			
		items.push(val);
		_team_members[val.Id] = val;
	});
	
	
var teamMembers = new Bloodhound({
  datumTokenizer: function (item) 
  {
	//return [item.FirstName + " " + item.LastName];
	return [item.FirstName, item.LastName, item.FirstName + " " + item.LastName, item.short_name];
  }, //Bloodhound.tokenizers.obj.whitespace('FirstName'),
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  //queryTokenizer: Bloodhound.tokenizers.obj.whitespace('FirstName'),
  //prefetch: 'data/getTeamMembers.php',
  
  // prefetch: {
    // url: 'data/getTeamMembers.php',
    // /*filter: function(list) {
      // return $.map(list, function(cityname) {
        // return { name: cityname }; });
    // }*/
	// },
  identify: function(obj) { console.log("identify" + obj); return obj.Id; },
  dupDetector: function(a, b) { return a.Id === b.Id; },
  //prefetch: 'data/getSearchFeatures.php',
  /*remote: {
    url: 'data/getTeamMembers.php',
    //wildcard: '%QUERY'
  },*/
  transform: function(data)
  {
	console.log('p: ' + data);
  },
  local: items
  	/*local: function(query) {
		var items = [];
      $.getJSON('data/getTeamMembers.php', function( data ) {
	  
	$.each( data, function( key, val ) {
		items.push(val);
	});
	
	
	});
	return items;
    }
	*/
});

teamMembers.initialize();

/**
 * Typeahead
 */
/*var elt = $('.example_typeahead > > input');
elt.tagsinput({
  typeaheadjs: {
    name: 'citynames',
    displayKey: 'name',
    valueKey: 'name',
    source: tm2.ttAdapter()
  }
});
*/

/**
 * Objects as tags
 */
elt = $('#tripreport_members');
elt.tagsinput({
  itemValue: 'Id',
  //itemText: 'FirstName',
  itemText: 'short_name',
  typeaheadjs: {
/*
  hint: $('.Typeahead-hint'),
  menu: $('.Typeahead-menu'),
  //hint: true,
  highlight: true,
  minLength: 1,
  classNames: {
      open: 'is-open',
      empty: 'is-empty',
      cursor: 'is-active',
      suggestion: 'Typeahead-suggestion',
      selectable: 'Typeahead-selectable'
    },
*/  
    name: 'teamMembers',
    //displayKey: 'FirstName',
	displayKey: 'short_name',
	confirmKeys: [13, 44, 188],
    source: teamMembers.ttAdapter(),
	//limit: 9
    /*source: function(query) {
      return $.getJSON('data/getTeamMembers.php');
    }*/	
  }
});

//elt.tagsinput('add', { "Id": 21 , "FirstName": "Amsterdam" });
//elt.tagsinput('add', { "Id": 32 , "FirstName": "xx" });

// elt.tagsinput('add', { "value": 1 , "text": "Amsterdam"   , "continent": "Europe"    });
// elt.tagsinput('add', { "value": 2 , "text": "xx"   , "continent": "zz"    });

	
	// $('#membersTagsControl').tagsinput({
	/*
	$('#tripreport_members').tagsinput({
	  itemValue: 'Id',
	  itemText: 'FirstName',
	  typeahead: {
		source: function(query) {
		  return $.getJSON('data/getTeamMembers.php');
		}
	  }
	});	
	
var cities = new Bloodhound({
  datumTokenizer: Bloodhound.tokenizers.obj.whitespace('text'),
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  prefetch: 'assets/cities.json'
});
cities.initialize();

var elt = $('input');
elt.tagsinput({
  itemValue: 'value',
  itemText: 'text',
  typeaheadjs: {
    name: 'cities',
    displayKey: 'text',
    source: cities.ttAdapter()
  }
});
elt.tagsinput('add', { "value": 1 , "text": "Amsterdam"   , "continent": "Europe"    });
elt.tagsinput('add', { "value": 4 , "text": "Washington"  , "continent": "America"   });
elt.tagsinput('add', { "value": 7 , "text": "Sydney"      , "continent": "Australia" });
elt.tagsinput('add', { "value": 10, "text": "Beijing"     , "continent": "Asia"      });
elt.tagsinput('add', { "value": 13, "text": "Cairo"       , "continent": "Africa"    });	
*/

// HACK: overrule hardcoded display inline-block of typeahead.js
	$(".twitter-typeahead").css('display', 'inline');
	
    });
	
	$('.modal-child').on('show.bs.modal', function () {
        var modalParent = $(this).attr('data-modal-parent');
        //$(modalParent).css('opacity', 0);
		$(modalParent).hide();
    });
	
    $('.modal-child').on('hidden.bs.modal', function () {
        var modalParent = $(this).attr('data-modal-parent');
        //$(modalParent).css('opacity', 1);
		$(modalParent).show();
		
		var trip_log_id = $('#trip_log_id').val();
		refreshTripReportFilesTable(trip_log_id);
    });	
}

function openAddFilesToTripReportForm()
{
	openUploadFilesForm(undefined, undefined);
}

function addTripFiles()
{
	var trip_log_id = $('#trip_log_id').val();
	openUploadTripFilesForm(trip_log_id);
}

function openUploadTripFilesForm(trip_log_id) // cave_id, cave_entrance_id, feature_id
{			
	//localize_static_html(); //-- could do something wrong to mangle html
	$('#upload_files_cave_id').val("");	
	
	$('#uploadFilesModalTitleLabel').text("Edit new trip report");
	// $('#uploadFilesModalTitleLabel').text("Edit new trip report '" + "" +"'");
	
	$('#fileupload_target_type').val("trip_report");
	$('#fileupload_target_object_id').val(trip_log_id);
	
	/*$('#fileupload_cave').on('click', function (event) 
		{
			event.preventDefault();
			$('#caveModal').modal('toggle'); 
		});
	*/
	/*$('#saveFileUpload').on('click', function () {
		//$('#uploadFilesModal').modal();
	});
	*/

	// workaround for localization problem
	
	$('#start_upload_button').text(_t().generic.start_upload);
	$('#cancel_upload_button').text(_t().generic.cancel);
	
	
	$('#fileupload_cave').off('submit');
	$('#fileupload_cave').on('submit', function(e) {
		e.preventDefault();
		$('#uploadFilesModal').modal('toggle'); 
	});
	
	/*
	$('#uploadFilesForm').on('submit', function(e) {
          e.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postCave.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#caveModal').modal('toggle'); 
				last_added_cave_id = undefined; //-- need to return the cave_id from postCave.php or to load load the last added cave in fillCavePicker()
				
				showNotification("Cave <b>" + formData.cave_name + "</b> was saved.");
				reloadMapFeatures();
				//$("caveModal").modal('hide');
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
*/		
	//fillCaveEntries();
	
	if (trip_log_id)
	{
		$('#uploadFilesModal').modal();
		/*$.getJSON("data/getCave.php?cave_id=" + cave_id, function( data ) {
			
			$('#caveModal').modal();
			
			$('#cave_id').val(data.Id);
			$('#cave_name').val(data.Name);				
			$('#cave_description').val(data.Description);
			$('#cave_type').val(data.Typeid);
			$('#cave_identifier').val(data.Locationidentifier);

			$('#cave_type').selectpicker('refresh');
			
			//_caveFormServerData = data;
			//$('#caveModal').modal();
		});		
		*/
	}
}


/*function refreshTripReportFilesTable(trip_log_id)
{
	var form_data = JSON.stringify( { trip_log_id: trip_log_id } );
	
    $.ajax({
                url: url_base + 'data/getTripReportFiles.php?trip_log_id=' + trip_log_id, // point to server-side PHP script 
                dataType: 'json', // dataType: 'jsonp', //dataType: 'text',  
                cache: false,
                contentType: false,
                processData: false,
                data: form_data,
                type: 'post',
                success: function(data){
							var dataSet = [];

							var file_directory = url_base + 'data/uploader/files/';
							
							data.forEach(function(item) {
								var file_url = file_directory + item.file_name;
								var file_name_html = "<a href='" + file_url + "' target='_blank' >" + item.file_name + "</a>";
								dataSet.push([file_name_html, item.size, item.add_time, 0 //item.object_type
								]);
							});
							  
							$('#trip_report_files_table').DataTable().clear();
							$('#trip_report_files_table').DataTable()
								.rows.add(dataSet)
								.draw();
                },
				error:  function(jqXHR, textStatus, errorThrown )
				{
					//onFailure(textStatus); //-- show error code returned
					console.warn(errorThrown);
					console.warn("Error loading trip report files: " + textStatus + " " + errorThrown);
					//console.error(errorThrown);
					//console.error("Error loading trip report files: " + textStatus + " " + errorThrown);
					//alert(errMsg);
				}				
     });	
}
*/
// End Trip Report form
///////////////////////

function initTripFeatureSearchControl()
{
  var engine, remoteHost, template, empty;
  
  remoteHost = '';
  template = Handlebars.compile($("#result-template").html());
  empty = Handlebars.compile($("#empty-template").html());
  
var featuresDataSource = new Bloodhound({
  datumTokenizer: Bloodhound.tokenizers.obj.whitespace('name'),  
  queryTokenizer: Bloodhound.tokenizers.whitespace,
  //datumTokenizer: Bloodhound.tokenizers.obj.whitespace('name', 'name'),  
  
  identify: function(obj) { console.log("identify" + obj); return obj.id; },
  dupDetector: function(a, b) { return a.id === b.id; },
  prefetch: url_base + 'data/getSearchFeatures.php',
  remote: {
    //url: remoteHost + 'data/getSearchFeatures.php?q=%QUERY',
	url: remoteHost + url_base + 'data/getSearchFeatures.php?q=%QUERY',
    wildcard: '%QUERY'
  },
  transform: function(data)
  {
	console.log('p: ' + data);
  }
});

featuresDataSource.initialize();

$('#tripreport_place').typeahead({
 //$('#searchFeatureControl .typeahead').typeahead({
  hint: $('.Typeahead-hint_2'),
  menu: $('.Typeahead-menu_2'),
  //hint: true,
  highlight: true,
  minLength: 1,
  classNames: {
      open: 'is-open',
      empty: 'is-empty',
      cursor: 'is-active',
      suggestion: 'Typeahead-suggestion',
      selectable: 'Typeahead-selectable'
    },
  //display: 'name',  
  //limit: 9
  //suggestion: Handlebars.compile('<div><strong>{{value}}</strong> � {{year}}</div>')
  /*
  filter: function (parsedResponse) {
            // parsedResponse is the array returned from your backend
            console.log(parsedResponse);

            // do whatever processing you need here
            return parsedResponse;
        }
		*/
},
{
  name: 'features',
  source: featuresDataSource, //substringMatcher(states)
  displayKey: 'name',
    templates: {
      suggestion: template,
      empty: empty,
	  //header: '<h3 class="league-name">Teams</h3>'
    },
})
/*.on('typeahead:asyncrequest', function() {
    $('.Typeahead-spinner').show();
  })
  .on('typeahead:asynccancel typeahead:asyncreceive', function() {
    $('.Typeahead-spinner').hide();
  })*/;	

$('#tripreport_place').bind('typeahead:select', function(ev, suggestion) {
  console.log('Selection: ');
  console.log(suggestion);
  
  addFeatureToTripReport(suggestion);
  
  //-- workaround for afterSelect which is not working; maybe it is not implemented in this version
  		setTimeout( function() 
		{ 			
			console.log('after select timeout: ');
			$('#tripreport_place').val("");
		}, 500);

  //gotoDbElement(suggestion); // id
  // gotoMapElement(suggestion.point_db_id); // id
});

$('#tripreport_place').on('typeahead:afterSelect', function(ev, suggestion) {
  console.log('after select: ');
  $('#tripreport_place').val("");
}).on('typeahead:autocompleted', function (e, datum) {
  console.log('after select: ');  
  $('#tripreport_place').val("");
});

	}


function addFeatureToTripReport(feature)
{
	var gokey = feature.id + "_" + feature.res_type;
	
	geoobjects[gokey] =
	{
		id: feature.id,
		name: feature.name,
	};
	
	refreshFeatureList();
}
	
// duplicated from main_map	
	
function openUploadPicturesForm(geoobject_id, geoobject_type) // cave_id, cave_entrance_id, feature_id
{				
	$('#upload_xx_cave_id').val("");
	
	//$('#uploadFilesForm').off('submit');
	//$("#uploadFilesForm")[0].reset();
	
	if (geoobject_id)	
		$('#uploadPicturesModalTitleLabel').text("Edit '" + "" +"'");

/*	
	$('#uploadFilesForm').on('submit', function(e) {
          e.preventDefault();

          var formData = $(this).serializeObject();
		  //var serializedFormData = JSON.stringify(formData);
		  
		  postDataAsync("data/postCave.php", formData, 
			function(x) 
			{ 
				console.log('close');
				$('#caveModal').modal('toggle'); 
				last_added_cave_id = undefined; //-- need to return the cave_id from postCave.php or to load load the last added cave in fillCavePicker()
				
				showNotification("Cave <b>" + formData.cave_name + "</b> was saved.");
				reloadMapFeatures();
				//$("caveModal").modal('hide');
			}, 
			function(err) 
			{ 
				console.log('error');
				alert(err);
			}
		  ); // { cave: formData }
		  
		  //console.log(formData);
		  //console.log(JSON.stringify($(this).serializeObject()));          
        });
*/		
	//fillCaveEntries();
	
	//if (geoobject_id)
	{
		$('#uploadPicturesModal').modal();
		/*$.getJSON("data/getCave.php?cave_id=" + cave_id, function( data ) {
			
			$('#caveModal').modal();
			
			$('#cave_id').val(data.Id);
			$('#cave_name').val(data.Name);				
			$('#cave_description').val(data.Description);
			$('#cave_type').val(data.Typeid);
			$('#cave_identifier').val(data.Locationidentifier);

			$('#cave_type').selectpicker('refresh');
			
			//_caveFormServerData = data;
			//$('#caveModal').modal();
		});		
		*/
	}
}

function addPictures()
{
	openUploadPicturesForm(undefined, undefined);
}

function initPicturesUploadControl()
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
		console.log('#fileupload_cave');
		console.log($('#fileupload_cave').fileupload('option', 'url'));

        //..$('#pictureUploader').addClass('fileupload-processing');
        $.ajax({
            // Uncomment the following to send cross-domain cookies:
            //xhrFields: {withCredentials: true},
            url: $('#fileupload_cave').fileupload('option', 'url'),
            dataType: 'json',
            context: $('#fileupload_cave')[0]
        }).always(function () {
            $(this).removeClass('fileupload-processing');
        }).done(function (result) {
            $(this).fileupload('option', 'done')
                .call(this, $.Event('done'), {result: result});
        });
    }	
	
	// $('#fileupload').fileupload('destroy');
}


function openAddFilesToTripReportForm()
{
	openUploadFilesForm(undefined, undefined);
}

function refreshTripReportFilesTable(trip_log_id)
{
	var form_data = JSON.stringify( { trip_log_id: trip_log_id } );
	
    $.ajax({
                url: url_base + 'data/getTripReportFiles.php?trip_log_id=' + trip_log_id, // point to server-side PHP script 
                dataType: 'json', // dataType: 'jsonp', //dataType: 'text',  
                cache: false,
                contentType: false,
                processData: false,
                data: form_data,
                type: 'post',
                success: function(data){
							var dataSet = [];

							var file_directory = url_base + 'data/uploader/files/';
							
							data.forEach(function(item) {
								var file_url = file_directory + item.file_name;
								var file_name_html = "<a href='" + file_url + "' target='_blank' >" + item.file_name + "</a>";
								dataSet.push([file_name_html, item.size, item.add_time, 0/*item.object_type*/]);
							});
							  
							/*$('#cave_files_table').DataTable({
								//"ajax": '../ajax/data/arrays.txt'
								data: dataSet,
							});*/
							$('#trip_report_files_table').DataTable().clear();
							$('#trip_report_files_table').DataTable()
								.rows.add(dataSet)
								.draw();
                },
				error:  function(jqXHR, textStatus, errorThrown )
				{
					//onFailure(textStatus); //-- show error code returned
					console.warn(errorThrown);
					console.warn("Error loading trip report files: " + textStatus + " " + errorThrown);
					//console.error(errorThrown);
					//console.error("Error loading trip report files: " + textStatus + " " + errorThrown);
					//alert(errMsg);
				}				
     });	
}



function initTripFormUploadControls()
{
	 $('#fileupload_cave').fileupload({
		// $('.fileupload').fileupload({

        // Uncomment the following to send cross-domain cookies:
        //xhrFields: {withCredentials: true},
        //url: 'data/uploader/'
		//url: 'data/uploader'
    });
}


function initTripReportUploadControl()
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
    /*$('#fileupload').fileupload({
        // Uncomment the following to send cross-domain cookies:
        //xhrFields: {withCredentials: true},
        url: 'data/uploader/'
		//url: 'data/uploader'
    });
	*/
	
	 /*$('.fileupload').fileupload({
        // Uncomment the following to send cross-domain cookies:
        //xhrFields: {withCredentials: true},
        //url: 'data/uploader/'
		//url: 'data/uploader'
    });*/


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

		
		// $('.fileupload').addClass('fileupload-processing');		
		$('#fileupload_cave').addClass('fileupload-processing');		

        $.ajax({
            // Uncomment the following to send cross-domain cookies:
            //xhrFields: {withCredentials: true},
            url: $('#fileupload_cave').fileupload('option', 'url'),
            dataType: 'json',
            context: $('#fileupload_cave')[0]
        }).always(function () {
            $(this).removeClass('fileupload-processing');
        }).done(function (result) {
            $(this).fileupload('option', 'done')
                .call(this, $.Event('done'), {result: result});
        });
    }	
	
	// $('#fileupload').fileupload('destroy');
	
	$('#fileupload_cave').on('submit', function(e) {
          e.preventDefault();
		  $('#fileupload_cave').modal('toggle'); 
	});
	
}


function initCaveFilesTable()
{
	//$(document).ready(function() { //});
	$('#').DataTable({
		//"ajax": '../ajax/data/arrays.txt'
	});	
}

//////////////////////////
// Feature list

function refreshFeatureList()
{
			var html_content = "";
			
			html_content += "";
			
			var feature_ids = "";
			
			for (var f_id in geoobjects) {
				//if (data.hasOwnProperty(property)) 
				{
					var feature_id = geoobjects[f_id].id;
					var feature_name = geoobjects[f_id].name;
					var geoobject_type = geoobjects[f_id].res_type; //geoobject_type;
					
					var home_icon_name = "house.png";
										
					var control = ("<span class='map_view_item' ><img src='" + __pointer_icons_base_url + "map.png' height='18'/>&nbsp;&nbsp;" + feature_name + "</span>" +
					 "<div class='map_view_item_operations' ><span onclick='setDefaultView(" + f_id + ");' class='map_view_item' ><img src='" + __pointer_icons_base_url + home_icon_name + "' height='16'/></span>" +
					 " <span onclick=\"deleteFeatures('" + f_id + "');\" class='map_view_item' ><img src='" + __pointer_icons_base_url + "round-delete-button.png' height='16'/></span></div>" + "<br/>");

/*
					var control = ("<button onclick='showView(" + map_view_id + ");' style='background-color:transparent; border-color:transparent;' ><img src='" + pointer_icons_base_url + "map.png' height='24'/>" + map_view_name + "</button>" +
					 " <button onclick='setDefaultView(" + map_view_id + ");' style='background-color:transparent; border-color:transparent;' ><img src='" + pointer_icons_base_url + home_icon_name + "' height='16'/></button>" +
					 " <button onclick='deleteViews(" + map_view_id + ");' style='background-color:transparent; border-color:transparent;' ><img src='" + pointer_icons_base_url + "round-delete-button.png' height='16'/></button> " + "<br/>");
*/					 
					html_content += control;
					//control.appendTo($("#mapViewsControlBox"));
										
					feature_ids += f_id + ","; // get_geoobject_key(geoobjects[f_id])
				}
			}

			//html_content += "<br/>";
			
			//var add_map_view_control = ("<button onclick='addView();' style='background-color:transparent; border-color:transparent;' >Add</button>"); // Add map view
			//html_content += add_map_view_control;
			
			var delete_all_control = ("<button onclick='deleteFeatures(undefined, true);' style='background-color:transparent; border-color:transparent;' >Delete all</button><br/>"); // Delete all map views
			html_content += delete_all_control;
			
			$('#tripreport_features').val("");
			//html_content += "<br/>";
			//$("#mapViewsControlBox").append($(html_content));
			$("#featuresListBox").html("");
			$("<div>" + html_content + "</div>").appendTo($("#featuresListBox"));
			
			$('#tripreport_features').val(feature_ids);
}

var geoobjects = {};//= [];
const __pointer_icons_base_url = url_base + 'assets/img/';

function getFeatureList(trip_log_id)
{
	$("#featuresListBox").empty();
	$('#tripreport_features').val("");	
	
	var form_data = JSON.stringify( { trip_log_id: trip_log_id } );
	
	$.ajax({
		//type: "GET",
		type: 'post',
		url: url_base + "data/getTripReportFeatures.php", //"/webservices/PodcastService.asmx/CreateMarkers",
		// The key needs to match your method's input parameter (case-sensitive).
		//data: JSON.stringify(data), // JSON.stringify({ Markers: markers })
		data: form_data,
		contentType: "application/json; charset=utf-8",
		dataType: "json",		
		success: function(data) {			
			geoobjects = {};
			
			for (var property in data) {
				if (data.hasOwnProperty(property))
				{
					//featureTypes.push(data[property]);
					//geoobjects[data[property].id] = data[property];
					data[property].id = data[property].geoobject_id;
					data[property].name = data[property].geoobject_name;
					data[property].res_type = data[property].geoobject_type;
					
					geoobjects[get_geoobject_key(data[property])] = data[property];					
				}
			};
			
			refreshFeatureList();
		},
		//failure: function(errMsg) {
			//$onFailure(errMsg);
			//},
		error:  function(jqXHR, textStatus, errorThrown )
		{
			//onFailure(textStatus); //-- show error code returned
			console.error(errorThrown);
			console.error("Error loading feature types: " + textStatus + " " + errorThrown);
			//alert(errMsg);
		}
	});
}

function deleteFeatures(feature_id, delete_all = false)
{
	if (delete_all)
		geoobjects = {};
	else
		delete geoobjects[feature_id];

	refreshFeatureList();
		
	/*
	var question_text = "";
	
	if (delete_all)
		question_text = "Doriti sa stergeti toate punctele de vizualizare?";
	else
		question_text = "Doriti sa stergeti punctul de vizualizare?";
		
		
	var q_res = confirm(String.format(question_text));
		
	if (q_res)
	{

	
	var feature_delete_data = {
		feature_id: feature_id,
		delete_all: delete_all
	};
	
	var feature_name = map_features[map_view_id].feature_name;
	
    $.ajax({
                url: 'data/deleteTripFeature.php', // point to server-side PHP script 
                dataType: 'text',  // what to expect back from the PHP script, if anything
				
				data: JSON.stringify(view_delete_data), // JSON.stringify({ Markers: markers })
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
						showNotification("Map view <b>" + view_name + "</b> was deleted.", { from: "bottom", align: "right" });
						refreshViewList();//alert("saved");
					}
					else
						alert(php_script_response); // display response from the PHP script, if any
                }
     });			 	 	 
	 }*/
}

function get_geoobject_key(geoobject)
{
	return geoobject.geoobject_id + "_" + geoobject.geoobject_type; //res_type;
}
// end feature list
////////////////////