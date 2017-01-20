/*!
 * Ajax Bootstrap Select
 *
 * Extends existing [Bootstrap Select] implementations by adding the ability to search via AJAX requests as you type. Originally for CROSCON.
 *
 * @version 1.3.6
 * @author Adam Heim - https://github.com/truckingsim
 * @link https://github.com/truckingsim/Ajax-Bootstrap-Select
 * @copyright 2016 Adam Heim
 * @license Released under the MIT license.
 *
 * Contributors:
 *   Mark Carver - https://github.com/markcarver
 *
 * Last build: 2016-06-09 10:00:54 AM EDT
 */
!(function ($) {
 /*!
     * Spanish translation for the "es-ES" and "es" language codes.
     * Diomedes Domínguez <diomedes.domimnguez@gmail.com>
     */
    $.fn.ajaxSelectPicker.locale["es-ES"] = {
        /**
         * @member $.fn.ajaxSelectPicker.locale
         * @cfg {String} currentlySelected = 'Currently Selected'
         * @markdown
         * El texto que se utilizará para la etiqueta del grupo de opciones cuando se conservan las opciones seleccionadas.
         */
        currentlySelected: "Seleccionado",

        /**
         * @member $.fn.ajaxSelectPicker.locale
         * @cfg {String} emptyTitle = 'Select and begin typing'
         * @markdown
         * El texto que se utilizará como título para el elemento de selección cuando no hay elementos para mostrar.
         */
        emptyTitle: "Seleccione y comience a escribir",

        /**
         * @member $.fn.ajaxSelectPicker.locale
         * @cfg {String} errorText = ''Unable to retrieve results'
         * @markdown
         * El texto que se utilizan en el contenedor de estado cuando una solicitud devuelve con un error.
         */
        errorText: "No se puede recuperar resultados",

        /**
         * @member $.fn.ajaxSelectPicker.locale
         * @cfg {String} searchPlaceholder = 'Search...'
         * @markdown
         * El texto que se utilizará para el atributo marcador de posición de entrada de búsqueda.
         */
        searchPlaceholder: "Buscar...",

        /**
         * @member $.fn.ajaxSelectPicker.locale
         * @cfg {String} statusInitialized = 'Start typing a search query'
         * @markdown
         * El texto utilizado en el contenedor de estado cuando se inicializa.
         */
        statusInitialized: "Empieza a escribir una consulta de búsqueda",

        /**
         * @member $.fn.ajaxSelectPicker.locale
         * @cfg {String} statusNoResults = 'No Results'
         * @markdown
         * El texto utilizado en el contenedor de estado cuando la solicitud no devolvió resultados.
         */
        statusNoResults: "Sin Resultados",

        /**
         * @member $.fn.ajaxSelectPicker.locale
         * @cfg {String} statusSearching = 'Searching...'
         * @markdown
         * El texto que se utilizan en el contenedor de estado cuando se está iniciando una solicitud.
         */
        statusSearching: "Buscando..."
    };
    $.fn.ajaxSelectPicker.locale.es = $.fn.ajaxSelectPicker.locale["es-ES"];
})(jQuery);

var localizedText = {
    'en': {
		map_popup:
		{
			edit_cave_entrance: "Edit cave entrance",
			entrance_details: "Entrance details",
			edit_cave: "Edit cave",
			edit_cave_entrance: "Edit cave entrance",
			meters_abbreviation: "m",
        },
		main_map:
		{
			menu:
			{
				map: "Map",
				data: "Data",
				files: "Files",				
				users: "Users",				
				reports: "Reports",
				add: "Add",
				add_submenu:
				{
					picture: "Picture",
					pictures: "Pictures",
					view: "View",
					trip_report: "Trip report",
				},
				config: "Config",
				config_submenu:
				{
					feature_types: "Feature types",
					team_members: "Team members",
				},							
				
				draw: "Draw",
				draw_submenu:
				{
					point: "Point",
					line: "Line",
					polygon: "Polygon",
				},
				tools: "Tools",
				tools_submenu:
				{
					export_map: "Export"
				},
				search_features_label: "Search features",
			},
			user_menu:
			{
				user: "User",
				log_out: "Log out"
			},
			body:
			{
				measurements_box: 
				{
					distances: "Distances",
					type: "Type",
					opt_distance: "Distance",
					opt_area: "Area",
					opt_geodesic_measurement: "Geodesic measurement"
				},
				map_views:
				{
					title: "Map views",
				},
				features_panel:
				{					
					title: "Features",
					cave_features_editing_checkbox: "Cave features editing",
				},
				layer_switcher:
				{
					title: "Map layers",
					opacity_bar_checkbox: "hide opacity bar",
				}
			},
			cave_edit_form:
			{
				title_new: "New cave",
				title_edit: "Edit cave",
				name: "Name",
				other_toponyms: "Other toponyms",
				identification_code: "Identification code",
				type: "Type",
				description: "Description",
				rock_type: "Rock type",
				rock_age: "Rock age",
				cave_age: "Cave age",
				region: "Region",
				catchement_basin: "Catchement basin", // drainage?
				valley: "Valley",
				tributary_river: "Tributary river",
				closest_address: "Closest address",
				land_registry_number: "Land registry number",
				show_cave: "Show cave",
				show_cave_length: "Show cave length",
				website: "Website",
				depth: "Depth",
				positive_depth: "Positive depth",
				negative_depth: "Negative depth",
				potential_depth: "Potential depth",
				surveyed_length: "Surveyed length",
				real_extension: "Real extension",
				projected_extension: "Projected extension",
				exploration_status: "Exploration status",
				protection_class: "Protection class",
				volume_cm: "Volume (cm)",
				ramification_index: "Ramification index",
				discovery_date: "Discovery date",
				discoverers: "Discoverer(s)",
			},
			feature_edit_form:
			{
				title_new: "New feature",
				title_edit: "Edit feature",
				name: "Name",
				description: "Description",
			},
			cave_details_form:
			{
				title: "Cave details",				
			},
			picture_edit_form:
			{
				title_new: "New picture",
				title_edit: "Edit picture",
				description: "Description",
			},
			cave_entrance_edit_form:
			{
				title_new: "New cave entrance",
				title_edit: "Edit cave entrance",
				name: "Name",
				description: "Description",
				type: "Type",
				cave: "Cave",
			},
			cave_feature_edit_form:
			{
				title_new: "New cave feature",
				title_edit: "Edit cave feature",
				name: "Name",
				description: "Description",
			},
			upload_files_form: //-- should be moved outside as function is generic
			{
				title: "Upload files",
				//title_edit: "",				
			},
			upload_pictures_form: //-- should be moved outside as function is generic
			{
				title: "Upload pictures",
				//title_edit: "",
				add_pictures: "Add pictures",
			},
		},
			trip_reports:
			{
				page_title: "Trip reports",
				title_new_trip_report_form: "New trip report",
				title_edit_trip_report_form: "Edit trip report",
				btn_add_report: "Add report",
				col_place: "Place / area",
				col_start : "Start",
				col_end : "End",
				col_details : "Details",
				col_participants : "Participants",
				col_added_on : "Added on",
				col_edit : "Edit",
				col_delete : "Delete",
				edit_link : "edit",
				pages_label : "?",
				
				trip_edit_form:
				{
					place: "Add places",
					start : "Start time",
					end : "End time",
					details : "Details",
					participants : "Participants",
				}
			},

			users:
			{
				page_title: "Users",
				col_user: "User",
				col_password: "Password",
				col_email: "Email",
				col_admin_level: "Admin level",
				col_language: "Language",
				col_last_log_in_time: "Last log in time",
			},
			
			feature_types:
			{
				page_title: "Feature types",
				col_name: "Name",
				col_symbol: "Symbol",
				col_type: "Type",
				col_details: "Details",
			},
			
			team_members:
			{
				page_title: "Feature types",
				col_first_name: "First name",
				col_last_name: "Last name",
				col_nickname: "Nickname",
				col_email: "Email",
				col_notes: "Notes",
				col_trip_count: "Trip count",
				col_trip_percentage: "Percentage",
			},
			
			files:
			{
				page_title: "Files",
				add_file: "Add file",
				add_multiple_files: "Add multiple files",				
			},
			
			gps_points:
			{
				page_title: "GPS points",
				col_lat: "Latitude",
				col_long: "Longitude",
				col_alt: "Altitude",
				col_name: "Name",
				col_time: "Time",
				col_type: "Type",
				col_map_location: "Location",
				col_view: "Details",
				show_point: "show point",
				details: "Details",
			},
			
			configuration:
			{
				page_title: "Configuration",
			},
			
			generic:
			{
				save: "Save",
				close: "Close",
				del: "Delete",
				remove: "Remove",
				add: "Add",
				add_files : "Add files",
				cancel: "Cancel",
				browse: "Browse",
				
				start_upload: "Start upload",
				end_upload: "End upload",
				error: "Error",
				processing: "Processing",				
				
				add_time: "Add time",
				
				file_table:
				{
					name: "Name",
					size: "Size",
					add_time: "Add time",
					category: "Category",
				}				
			},
			
			feature_types:
			{
				cave: "cave",
				cave_entrance: "cave entrance",
				sinkhole: "sinkhole",
				construction: "construction",
				lake: "lake",
				detritus: "detritus",
				karren: "karren",
				peak: "peak",
				canyon: "canyon",
				spring: "spring",
				sink: "sink",
				portal: "portal",
				well: "well",
				walls: "walls",
				pot: "pot",
				fault: "fault",
				fracture_line: "fracture line",
				dry_valley: "dry valley",
				gallery: "gallery",
				pitch: "pitch",
				gallery_area: "gallery area",
				
				cave_features_group_label: "Cave features",
				surface_features_group_label: "Surface features"				
			}
    },
    'ro': {
        /**
         * @member $.fn.ajaxSelectPicker.locale
         * @cfg {String} currentlySelected = 'Currently Selected'
         * @markdown
         * El texto que se utilizará para la etiqueta del grupo de opciones cuando se conservan las opciones seleccionadas.
         */	
		map_popup:
		{
			edit_cave_entrance: "Editeaza intrarea",
			entrance_details: "Entrance details",
			edit_cave: "Edit cave",
			edit_cave_entrance: "Edit cave entrance",
			meters_abbreviation: "m",
        },
		main_map:
		{
			menu:
			{
				map: "Harta",
				data: "Date",
				files: "Fisiere",
				users: "Utilizatori",				
				reports: "Rapoarte",
				add: "Adauga",
				add_submenu:
				{
					picture: "Poza",
					pictures: "Poze",
					view: "Detalii harta",
					trip_report: "Raport de tura",
				},
				config: "Configuratie",
				config_submenu:
				{
					feature_types: "Tipuri de obiecte",
					team_members: "Membri de echipa",
				},							
				
				draw: "Deseneaza",
				draw_submenu:
				{
					point: "Punct",
					line: "Linie",
					polygon: "Poligon",
				},
				tools: "Unelte",
				tools_submenu:
				{
					export_map: "Exporta ca imagine"
				},
				search_features_label: "Cauta locuri",
			},
			user_menu:
			{
				user: "Utilizator",
				log_out: "Iesire"
			},
			body:
			{
				measurements_box: 
				{
					distances: "Calc. distanta",
					type: "tip",
					opt_distance: "Distanta",
					opt_area: "Arie",
					opt_geodesic_measurement: "Distanta geodezica" // geografica / geodezica ? model simplu al suprafetei fara MDE
				},
				map_views:
				{
					title: "Perspective harta",
				},
				features_panel:
				{					
					title: "Forme de relief", // obiect geografic
					cave_features_editing_checkbox: "Editare obiecte pestera",
				},
				layer_switcher:
				{
					title: "Strate harta",
					opacity_bar_checkbox: "ascunde control opacitate",
				}
			},
			cave_edit_form:
			{
				title_new: "Pestera noua",
				title_edit: "Editeaza pestera",
				name: "Nume",
				other_toponyms: "Alte toponime",
				identification_code: "Cod de ifentificare",
				type: "Tip",
				description: "Descriere",
				rock_type: "Tip de roca",
				rock_age: "Varsta rocii",
				cave_age: "Varsta pesterii ?",
				region: "Regiune",
				catchement_basin: "Bazin hidragrafic", // drainage?
				valley: "Vale",
				tributary_river: "Afluent",
				closest_address: "Adresa aproiata",
				land_registry_number: "Numar cadastru",
				show_cave: "Turistica",
				show_cave_length: "Lungime turistica(vizitabila)",
				website: "Site web",
				depth: "Adancime",
				positive_depth: "Denivelare pozitiva",
				negative_depth: "Denivelare negativa",
				potential_depth: "Denivelare potentiala",
				surveyed_length: "Dezvoltare cartata",
				real_extension: "Extensie reala",
				projected_extension: "Extensie proiectata",
				exploration_status: "Stare explorare",
				protection_class: "Clasa de protectie",
				volume_cm: "Volum (cm)",
				ramification_index: "Index de ramificare",
				discovery_date: "Data descoperire",
				discoverers: "Descoperitor(i)",
			},
			feature_edit_form:
			{
				title_new: "Forma de relief noua",
				title_edit: "Editeaza forma de relief",
				name: "Nume",
				description: "Descriere",
			},
			cave_details_form:
			{
				title: "Cave details",				
			},
			picture_edit_form:
			{
				title_new: "Poza noua",
				title_edit: "Editeaza poza",
				description: "Descriere",
			},
			cave_entrance_edit_form:
			{
				title_new: "Intrare pestera noua",
				title_edit: "Editeaza intrare pestera",
				name: "Nume",
				description: "Descriere",
				type: "Tip",
				cave: "Pestera",
			},
			cave_feature_edit_form:
			{
				title_new: "Parte de pestera noua",
				title_edit: "Editeaza parte de pestera",
				name: "Nume",
				description: "Descriere",
			},
			upload_files_form: //-- should be moved outside as function is generic
			{
				title: "Incarca fisiere",
				//title_edit: "",				
			},
			upload_pictures_form: //-- should be moved outside as function is generic
			{
				title: "Incarca poze",
				//title_edit: "",
				add_pictures: "Adauga poze",
			},			
		},
			trip_reports:
			{
				page_title: "Rapoarte de tura",
				title_new_trip_report_form: "Raport now",
				title_edit_trip_report_form: "Editeaza raport",
				btn_add_report: "Adauga raport",
				col_place: "Loc",
				col_start : "Inceput",
				col_end : "Sfarsit",
				col_details : "Detalii",
				col_participants : "Participanti",
				col_added_on : "Adaugat la",
				col_edit : "Editeaza",
				col_delete : "Sterge",
				edit_link : "editeaza",
				pages_label : "?",
				
				trip_edit_form:
				{
					place: "Adauga loc(uri)",
					start : "Inceput",
					end : "Sfarsit",
					details : "Detalii",
					participants : "Participanti",					
				}
			},
			
			users:
			{
				page_title: "Utilizatori",
				col_user: "User",
				col_password: "Parola",
				col_email: "Email",
				col_admin_level: "Nivel administrator",
				col_language: "Limba",
				col_last_log_in_time: "Ultima autentificare",
			},
			
			feature_types:
			{
				page_title: "Tipuri de geoobiecte",
				col_name: "Nume",
				col_symbol: "Simbol",
				col_type: "Tip",
				col_details: "Detalii",				
			},
			
			team_members:
			{
				page_title: "Feature types",
				col_first_name: "Prenume",
				col_last_name: "Nume",
				col_nickname: "Porecla",
				col_email: "Email",
				col_notes: "Detalii",
				col_trip_count: "Ture",
				col_trip_percentage: "Procent",
			},
			
			files:
			{
				page_title: "Fisiere",
				add_file: "Adauga fisier",
				add_multiple_files: "Adauga fisiere multiple",
			},
			
			gps_points:
			{
				page_title: "Puncte GPS",
				col_lat: "Latitudine",
				col_long: "Longitudine",
				col_alt: "Altitudine",
				col_name: "Nume",
				col_time: "Data/timp",
				col_type: "Tip",
				col_map_location: "Localizare",
				col_view: "Detalii",
				show_point: "mergi acolo",
				details: "Detalii",
			},
			
			configuration:
			{
				page_title: "Configuratie",
			},
			
			generic:
			{				
				save: "Salveaza",
				close: "Inchide",
				del: "Sterge",
				remove: "Sterge",
				add: "Adauga",
				add_files : "Adauga fisiere",
				cancel: "Anuleaza",
				browse: "Descide",
				
				start_upload: "Incarca",
				end_upload: "Opreste", // Stop, Termina
				error: "Eroare",
				processing: "Proceseaza...",
				
				add_time: "Data adaugarii",				
				
				file_table:
				{
					name: "Nume",
					size: "Marime",
					add_time: "Adaugat la",
					category: "Categorie",
				}
			},
			
			feature_types:
			{
				cave: "pestera",
				cave_entrance: "intrare pestera",
				sinkhole: "dolina",
				construction: "constructie",
				lake: "lac",
				detritus: "grohotis",
				karren: "lapiezuri",
				peak: "varf",
				canyon: "canion",
				spring: "izvor",
				sink: "ponor",
				portal: "portal",
				well: "fantana",
				walls: "walls",
				pot: "aven",
				fault: "falie",
				fracture_line: "fisura",
				dry_valley: "vale seaca",
				valley: "vale",
				gallery: "galerie",
				pitch: "put",
				gallery_area: "zona galerii",
				
				cave_features_group_label: "Pestera",
				surface_features_group_label: "Suprafata"
			}
			
	}
};
