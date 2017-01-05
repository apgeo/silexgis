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
			}
		}
	} // ... etc.
};
