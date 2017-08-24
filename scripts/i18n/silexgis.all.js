!(function ($) {

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
				data_submenu:
				{
					points: "Points",
					exploration_points: "Exploration points",
					geofiles: "Geofiles",
					files: "Files",
					georeferenced_maps: "Georeferenced maps",
					caves: "Caves"
				},
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
					georeferenced_map: "Georeferenced map"
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
					export_map: "Export",
					show_3d_models: "Show 3d models"
				},
				about: "About",
				search_features_label: "Search features",
			},
			user_menu:
			{
				user: "User",
				edit_settings: "Settings",
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
					opt_geodesic_measurement: "Geodesic measurement",
					continue_line_message: "Click to continue the line",
					continue_polygon_message: "Click to continue the polygon",
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
					cave_features: "cave features",
					cave_zones: "cave zones",
					cave_features: "cave features",
					surface_features: "surface features",
					pictures_layer: "Pictures",
					drawings: "Drawings",
					measurements_layer: "Measurements",
					geo_files: "Geo files",
					bing_labels: "Labels",
					bing_aerial: "Aerial",
					bing_road: "Road",
				}
			},
			map_context_menu:
			{
				show_cave_details: "Cave details",
				edit_cave_details: "Edit cave details",
				delete_cave: "Detele cave",
				edit_cave_entrance_details: "Edit cave entrance details",
				delete_cave_entrance: "Delete cave entrance",				
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
				area_m2: "Area (m²)",
				volume_m3: "Volume (m³)",
				ramification_index: "Ramification index",
				discovery_date: "Discovery date",
				discoverers: "Discoverer(s)",
				elevation: "Elevation",
				entrance: "Entrance",
				view_on_map: "map",			
				
				tab_identification: "Identification",
				tab_geology: "Geology",
				tab_location: "Location",
				tab_topometry: "Topometry",
				tab_other: "Other",
				tab_entrances: "Entrances",
				tab_files: "Files",
				tab_pictures: "Pictures"
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
				cave_search_control_placeholder: "Search cave"
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
			about_form:
			{
				title: "About SilexGIS",
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

			add_file:
			{
				page_title: "Add file",
				description_text: "Select file",
				choose_file: "Choose file",
				upload_file: "Upload file"
			},
			
			add_multiple_files:
			{
				page_title: "Add files",
				description_text: "Select file",
				choose_file: "Choose files",
				upload_file: "Upload files"
			},
			
			add_geofile:
			{
				page_title: "Add file with geographic data",
				description_text: "Select file (GPX, KML, GeoJSON, Shapefile)",
				choose_file: "Choose file",
				upload_file: "Upload file"
			},
			
			add_multiple_geofiles:
			{
				page_title: "Add files with geographic data",
				description_text: "Select file (GPX, KML, GeoJSON, Shapefile)",
				choose_file: "Choose files",
				upload_file: "Upload files"
			},
			
			feature_types_page:
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

			geofiles:
			{
				page_title: "Geo files",
				add_geofile: "Add geofile",
				add_multiple_geofiles: "Add multiple geofiles",
				col_enabled: "Enabled",
				col_size: "Size",
				col_user: "User",
				col_name: "Name",
				col_time: "Time",
				col_type: "Type",
				col_original_style: "Original style",
				// col_map_location: "Location",
				// col_view: "Details",
				
				// details: "Details",
				
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

			exploration_points:
			{
				page_title: "Exploration points",
				page_description: "Exploration points are added on the map (using the exploration point feature/object type)",
				col_lat: "Latitude",
				col_long: "Longitude",
				col_alt: "Altitude",
				col_name: "Name",
				col_time: "Time",
				col_type: "Type",
				col_map_location: "Location",
				col_view: "Details",
				col_description: "Description",
				show_point: "show point",
				details: "Details",
			},
			
			georeferenced_maps:
			{
				title_new_georeferenced_map_form: "Add georeferenced map",
				title: "Map title",
				description: "Description",
				boundaries: "Boundaries",
				boundary_north: "North",
				boundary_west: "West",
				boundary_south: "South",
				boundary_east: "East"
			},

			georeferenced_maps_page:
			{
				page_title: "Georeferenced maps",
				description_text: "Georeferenced map explanation",
				btn_add_georeferenced_image: "Add georeferenced image"
			},

			caves:
			{
				page_title: "Caves",
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
				
				back: "Back",
				
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
				select: "select",
				move: "move",
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
				approximated_gallery: "approx. gallery",
				gallery_line: "gallery line",
				chimney: "chimney",
				lake: "lake",
				water_flow: "water flow",
				bivouac: "bivouac",
				desobstruction: "desobstruction",
				flag: "flag", 
				red_flag: "red flag",
				exploration_point: "exploration point",
				
				cave_features_group_label: "Cave features",
				surface_features_group_label: "Surface features"				
			},

			measurement_units:
			{
				metre_singular: "metre",
				metres: "metres",
				metre_short: "m",

				kilometre_singular: "kilometres",
				kilometres: "kilometres",
				kilometre_short: "km",

				litre_singular: "litre",
				litres: "litres",
				litre_short: "l",

				year_singular: "year",
				years: "years",
				years_short: "yr.",
			},
			misc:
			{
				datatables_lang_identifier: "english"
			},
			caveview_page:
			{
				instructions: "Mouse: left button down - rotate, right button down pan, mouse wheel - zoom",
				caveview_credits: "This module is powered by "
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
				data_submenu:
				{
					points: "Puncte",
					exploration_points: "Obiective explorare",
					geofiles: "Fisiere geografice",
					files: "Fisiere",
					georeferenced_maps: "Harti georeferentiate",
					caves: "Peșteri"
				},				
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
					georeferenced_map: "Harta georeferentiata"
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
					export_map: "Exporta ca imagine",
					show_3d_models: "Modele 3d"
				},
				about: "Despre",
				search_features_label: "Cauta locuri",
			},
			user_menu:
			{
				user: "Utilizator",
				edit_settings: "Setari",
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
					opt_geodesic_measurement: "Distanta geodezica", // geografica / geodezica ? model simplu al suprafetei fara MDE
					continue_line_message: "Click pentru a continua masurarea distantei",
					continue_polygon_message: "Click pentru a continua masurarea ariei",
				},
				map_views:
				{
					title: "Perspective harta",
				},
				features_panel:
				{					
					title: "Unelte", // "Forme de relief", obiect geografic
					cave_features_editing_checkbox: "Editare obiecte peșteră",
				},
				layer_switcher:
				{
					title: "Strate harta",
					opacity_bar_checkbox: "ascunde control opacitate",
					cave_zones: "zone peșteră",
					cave_features: "puncte peșteră",
					surface_features: "forme suprafata",
					pictures_layer: "Fotografii",
					drawings: "schite",
					measurements_layer: "masuratori",
					geo_files: "fisiere geo",
					bing_labels: "Etichete",
					bing_aerial: "Aerial",
					bing_road: "Drumuri",
				}
			},
			map_context_menu:
			{
				show_cave_details: "Detalii peșteră",
				edit_cave_details: "Editează peșteră",
				delete_cave: "Șterge peșteră",
				edit_cave_entrance_details: "Editează intrarea peșterii",
				delete_cave_entrance: "Șterge intrarea peșterii",				
			},

			cave_edit_form:
			{
				title_new: "Peșteră nouă",
				title_edit: "Editeaza peșteră",
				name: "Nume",
				other_toponyms: "Alte toponime",
				identification_code: "Cod de ifentificare",
				type: "Tip",
				description: "Descriere",
				rock_type: "Tip de rocă",
				rock_age: "Vârsta rocii",
				cave_age: "Vârsta peșterii ?",
				region: "Regiune",
				catchement_basin: "Bazin hidragrafic", // drainage?
				valley: "Vale",
				tributary_river: "Afluent",
				closest_address: "Adresă apropiată",
				land_registry_number: "Număr cadastru",
				show_cave: "Turistică",
				show_cave_length: "Lungime turistică(vizitabilă)",
				website: "Site web",
				depth: "Adâncime",
				positive_depth: "Denivelare pozitivă",
				negative_depth: "Denivelare negativă",
				potential_depth: "Denivelare potențială",
				surveyed_length: "Dezvoltare cartată",
				real_extension: "Extensie reală",
				projected_extension: "Extensie proiectată",
				exploration_status: "Stare explorare",
				protection_class: "Clasă de protecție",
				area_m2: "Arie (m²)",
				volume_m3: "Volum (m³)",
				ramification_index: "Index de ramificare",
				discovery_date: "Dată descoperire",
				discoverers: "Descoperitor(i)",
				elevation: "Altitudine",
				entrance: "Intrare",
				view_on_map: "pe hartă",

				tab_identification: "Identificare",
				tab_geology: "Geologie",
				tab_location: "Localizare",
				tab_topometry: "Topometrie",
				tab_other: "Altele",
				tab_entrances: "Intrări",
				tab_files: "Fișiere",
				tab_pictures: "Poze"				
			},
			feature_edit_form:
			{
				title_new: "Formă de relief nouă",
				title_edit: "Editează forma de relief",
				name: "Nume",
				description: "Descriere",
			},
			cave_details_form:
			{
				title: "Cave details",				
			},
			picture_edit_form:
			{
				title_new: "Poză nouă",
				title_edit: "Editează poză",
				description: "Descriere",
			},
			cave_entrance_edit_form:
			{
				title_new: "Intrare peșteră nouă",
				title_edit: "Editeaza intrare peșteră",
				name: "Nume",
				description: "Descriere",
				type: "Tip",
				cave: "Peșteră",
				cave_search_control_placeholder: "Caută peșteră"
			},
			cave_feature_edit_form:
			{
				title_new: "Parte de peșteră nouă",
				title_edit: "Editeaza parte de peșteră",
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
			about_form:
			{
				title: "Despre SilexGIS",
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
			
			add_file:
			{
				page_title: "Adauga fisier",
				description_text: "Selecteaza fisierul",
				choose_file: "Selecteaza fisier",
				upload_file: "Incarca fisier"
			},

			add_multiple_files:
			{
				page_title: "Adauga fisiere",
				description_text: "Selecteaza fisierele",
				choose_file: "Selecteaza fisiere",
				upload_file: "Incarca fisiere"
			},

			add_geofile:
			{
				page_title: "Adauga fisier cu date geografice",
				description_text: "Selecteaza fisierul (GPX, KML, GeoJSON, Shapefile)",
				choose_file: "Selecteaza fisier",
				upload_file: "Incarca fisier"
			},

			add_multiple_geofiles:
			{
				page_title: "Adauga fisiere cu date geografice",
				description_text: "Selecteaza fisierele (GPX, KML, GeoJSON, Shapefile)",
				choose_file: "Selecteaza fisiere",
				upload_file: "Incarca fisiere"
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
			
			feature_types_page:
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

			geofiles:
			{
				page_title: "Fisiere cu date geografice",
				add_geofile: "Adauga fisier",
				add_multiple_geofiles: "Adauga fisiere multiple",

				col_enabled: "Activat",
				col_size: "Dimensiune",
				col_user: "Utilizator",
				col_name: "Nume",
				col_time: "Data",
				col_type: "Tip",
				col_original_style: "Stil original",
				// col_map_location: "Location",
				// col_view: "Details",
				
				// details: "Details",

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
			
			exploration_points:
			{
				page_title: "Obiective explorare",
				page_description: "Obiective de explorare se adauga pe harta folosind tipul de obiect 'obiective de explorare'",
				col_lat: "Latitudine",
				col_long: "Longitudine",
				col_alt: "Altitudine",
				col_name: "Nume",
				col_time: "Data/timp",
				col_type: "Tip",
				col_map_location: "Localizare",
				col_view: "Detalii",
				col_description: "Descriere",				
				show_point: "mergi acolo",
				details: "Detalii",
			},

			georeferenced_maps:
			{
				title_new_georeferenced_map_form: "Adauga harta georeferentiata",
				title: "Titlul hartii",
				description: "Descriere",
				boundaries: "Limite",
				boundary_north: "Nord",
				boundary_west: "Vest",
				boundary_south: "Sud",
				boundary_east: "Est"
			},
			
			georeferenced_maps_page:
			{
				page_title: "Harti georeferentiate",
				description_text: "Harti georeferentiate sunt fisiere imagine cu harti care au asociate si coordonate geografice pentru punctele de pe harta pentru a putea fi suprapuse geografic peste alte straturi cu harti.",
				btn_add_georeferenced_image: "Adauga imagine georeferentiata"
			},

			caves:
			{
				page_title: "Peșteri",
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
				browse: "Deschide",
				
				start_upload: "Incarca",
				end_upload: "Opreste", // Stop, Termina
				error: "Eroare",
				processing: "Proceseaza...",
				
				add_time: "Data adaugarii",				
				
				back: "Inapoi",
				
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
				select: "selectează",
				move: "mută",
				cave: "peșteră",
				cave_entrance: "intrare peșteră",
				sinkhole: "dolină",
				construction: "construcție",
				lake: "lac",
				detritus: "grohotiș",
				karren: "lapiezuri",
				peak: "vârf",
				canyon: "canion",
				spring: "izvor",
				sink: "ponor",
				portal: "portal",
				well: "puț (supr.)",
				walls: "pereți",
				pot: "aven",
				fault: "falie",
				fracture_line: "fisură",
				dry_valley: "vale seacă",
				valley: "vale",
				gallery: "galerie",
				pitch: "puț",
				gallery_area: "zonă galerii",
				approximated_gallery: "galerie approx.",
				gallery_line: "galerie(linie)",
				chimney: "horn",
				lake: "lac",
				water_flow: "pârâu permanent",
				bivouac: "bivuac",
				desobstruction: "dezobstrucție",
				flag: "steag", 
				red_flag: "steag roșu",
				exploration_point: "ob. explorare",
				
				
				cave_features_group_label: "Peșteră",
				surface_features_group_label: "Suprafață"
			},

			measurement_units:
			{
				metre_singular: "metru",
				metres: "metri",
				metre_short: "m",

				kilometre_singular: "kilometru",
				kilometres: "kilometri",
				kilometre_short: "km",

				litre_singular: "litru",
				litres: "litri",
				litre_short: "l",

				year_singular: "an",
				years: "ani",
				years_short: "ani",
			},
			misc:
			{
				datatables_lang_identifier: "romanian"
			},
			caveview_page:
			{
				instructions: "rotatie - butonul mouse stanga; miscare perspectiva - butonul mouse dreapta; marire/micsorare - rotita mouse",
				caveview_credits: "Modulul 3D foloseste biblioteca "
			}
	}
};
