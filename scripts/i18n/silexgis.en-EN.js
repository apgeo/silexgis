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
				
			},
			user_menu:
			{
				user: "User",
				log_out: "Log out"
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
				
			},
			user_menu:
			{
				user: "Utilizator",
				log_out: "Iesire"
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
				col_notes: "Note",
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
				error: "Eroare",
				processing: "Proceseaza...",
				
				start_upload: "Incarca",
				end_upload: "Opreste", // Stop, Termina
				
				add_time: "Data adaugarii",				
				
				file_table:
				{
					name: "Nume",
					size: "Marime",
					add_time: "Adaugat la",
					category: "Categorie",
				}
			}				
	}
};
