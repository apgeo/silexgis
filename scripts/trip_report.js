var url_base = 'http://' + window.location.host + "/speogis/";

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
			$('#tripreport_start_time').val(data.Tripstarttime);
			$('#tripreport_end_time').val(data.Tripendtime);				
			$('#tripreport_details').val(data.Details);
			$('#tripreport_place').val(data.Targetzone);
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

	$('#addFilesToTripReport').on('click', function(event) {
		event.preventDefault(); // To prevent following the link (optional)		
		addTripFiles();
		
		//onSaveCave(this);
		//$(this).submit();
	});
	
	$('#tripReportForm').on('submit', function(e) {
          event.preventDefault();

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
	
	$('#tripreport_start_time').datetimepicker({
		//defaultDate: "11/1/2013",
        /*disabledDates: [
							moment("12/25/2013"),
							new Date(2013, 11 - 1, 21),
							"11/22/2013 00:53"
						]
						*/
	});	

	$('#tripreport_end_time').datetimepicker({
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
	$('#upload_files_cave_id').val("");	
	
	$('#uploadFilesModalTitleLabel').text("Edit '" + "" +"'");
	
	$('#fileupload_target_type').val("trip_report");
	$('#fileupload_target_object_id').val(trip_log_id);
/*	
	$('#uploadFilesForm').on('submit', function(e) {
          event.preventDefault();

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
  //suggestion: Handlebars.compile('<div><strong>{{value}}</strong> – {{year}}</div>')
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
  //gotoDbElement(suggestion); // id
  // gotoMapElement(suggestion.point_db_id); // id
});
	}
