/*	Copyright (c) 2015 Jean-Marc VIGLINO, 
	released under the CeCILL-B license (French BSD license)
	(http://www.cecill.info/licences/Licence_CeCILL-B_V1-en.txt).
	
	@classdesc
	ol.source.DBPedia is a DBPedia layer source that load DBPedia located content in a vector layer.
	
	olx.source.DBPedia: olx.source.Vector
	{	url: {string} Url for DBPedia SPARQL 
	}

	@require jQuery
	
	Inherits from:
	<ol.source.Vector>
*/

/**
* @constructor ol.source.DBPedia
* @extends {ol.source.Vector}
* @param {olx.source.DBPedia=} options
* @todo 
*/
ol.source.DBPedia = function(opt_options)
{	var options = opt_options || {};
	var self = this; 

	//var 
	this._features_indexed = {};
	options.loader = this._loaderFn;
	
	/** Url for DBPedia SPARQL */
	this._url = options.url || "http://fr.dbpedia.org/sparql";

	/** Max resolution to load features  */
	this._maxResolution = options.maxResolution || 100;
	
	/** Result language */
	this._lang = options.lang || "fr";

	/** Query limit */
	this._limit = options.limit || 1000;
	
	/** Default attribution */
	if (!options.attributions) options.attributions = [ new ol.Attribution({ html:"&copy; <a href='http://dbpedia.org/'>DBpedia</a> CC-by-SA" }) ];

	//if (options.onPicturesLoad) 
	this._onPicturesLoad = options.onPicturesLoad;
	
	// Bbox strategy : reload at each move
    if (!options.strategy) options.strategy = ol.loadingstrategy.bbox;

	ol.source.Vector.call (this, options);
};
ol.inherits (ol.source.DBPedia, ol.source.Vector);


/** Decode RDF attributes and choose to add feature to the layer
* @param {feature} the feature
* @param {attributes} RDF attributes
* @param {lastfeature} last feature added (null if none)
* @return {boolean} true: add the feature to the layer
* @API stable
*/

/*
ol.source.DBPedia.prototype.readFeature = function (feature, attributes, lastfeature)
{	// Copy RDF attributes values
	for (var i in attributes) feature.set (i, attributes[i].value);

	// Prevent same feature with different type duplication
	if (lastfeature && lastfeature.get("subject") == attributes.subject.value)
	{	// Kepp dbpedia.org type ?
		// if (bindings[i].type.match ("dbpedia.org") lastfeature.get("type") = bindings[i].type.value;
		// Concat types
		lastfeature.set("type", lastfeature.get("type") +"\n"+ attributes.type.value);
		return false;
	}
	else 
	{	return true;
	}
};
*/

/** Set RDF query subject, default: select label, thumbnail, abstract and type
* @API stable
*/
/*
ol.source.DBPedia.prototype.querySubject = function ()
{	return "?subject rdfs:label ?label. "
		+ "OPTIONAL {?subject dbpedia-owl:thumbnail ?thumbnail}."
		+ "OPTIONAL {?subject dbpedia-owl:abstract ?abstract} . "
		+ "OPTIONAL {?subject rdf:type ?type}";
}
*/
/** Set RDF query filter, default: select language
* @API stable
*/
/*
ol.source.DBPedia.prototype.queryFilter = function ()
{	return	 "lang(?label) = '"+this._lang+"' "
		+ "&& lang(?abstract) = '"+this._lang+"'"
	// Filter on type 
	//+ "&& regex (?type, 'Monument|Sculpture|Museum', 'i')"
}
*/

/** Loader function used to load features.
* @private
*/
ol.source.DBPedia.prototype._loaderFn = function(extent, resolution, projection) 
{	
	//console.log("ol.source.DBPedia.prototype._loaderFn(extent, resolution, projection)");
	//console.log(extent);
	//console.log(resolution);
	
	if (resolution > this._maxResolution) return;
	var self = this;
	
	var bbox = ol.proj.transformExtent(extent, projection, "EPSG:4326");
	//console.log(bbox);
	console.log(bbox[1] + ', ' + bbox[0] + '\r\n' + bbox[3] + ', ' + bbox[2]);
	// SPARQL request: for more info @see http://fr.dbpedia.org/
	/*
	query =	"PREFIX geo: <http://www.w3.org/2003/01/geo/wgs84_pos#> "
				+ "SELECT DISTINCT * WHERE { "
				+ "?subject geo:lat ?lat . "
				+ "?subject geo:long ?long . "
				+ this.querySubject()+" . "
				+ "FILTER("+this.queryFilter()+") . "
				// Filter bbox
				+ "FILTER(xsd:float(?lat) <= " + bbox[3] + " && " + bbox[1] + " <= xsd:float(?lat) "
				+ "&& xsd:float(?long) <= " + bbox[2] + " && " + bbox[0] + " <= xsd:float(?long) "
				+ ") . "
				+ "} LIMIT "+this._limit;
	*/

	$.ajax(
	{	//url: this._url,
		//dataType: 'jsonp', 
		url: 'data/getPictures.php',
		dataType: 'json', // dataType: 'jsonp',
		
		//data: { query: query, format:"json" },
		success: function(data) 
		{	
			var transform = ol.proj.getTransform('EPSG:4326', 'EPSG:3857');
			var features = [];
			
			data.features.forEach(function(item) {
			
				//return;
			
			
				var feature = new ol.Feature(); // var feature = new ol.Feature(item);
				//feature.set('url', item.properties.url);
				//feature.set('thumbUrl', item.properties.thumbUrl);
				var coordinate = transform([parseFloat(item.geometry.coordinates[1]), parseFloat(item.geometry.coordinates[0])]);
				var geometry = new ol.geom.Point(coordinate);
				feature.setGeometry(geometry);
				//pictureSource.addFeature(feature);
				
				feature.set('description', item.properties.description);
				feature.set('lat', item.geometry.coordinates[0]); // coordinate[0]
				feature.set('long', item.geometry.coordinates[1]); // coordinate[1]
				//feature.set('label', "z");
				//feature.set('thumbnail', item.properties.thumbUrl);
				feature.set('thumbnail', './' + item.properties.thumbUrl);
				//feature.set('abstract', "");
				feature.set('type', "");
				feature.set('url', './' + item.properties.url);
				feature.set('image_id', item.properties.image_id);
				//feature.set('', "");

				
				//if (self._features_indexed == unde)
				//	self._features_indexed = {};
					
				if (self._features_indexed[feature.get("thumbnail")] == undefined)
				{
					self._features_indexed[feature.get("thumbnail")] = 1;
					features.push(feature);
				}
				
				//features.push(feature);
				//console.log(feature.get("thumbnail") + " " + coordinate[0] + " " + coordinate[1]);
			});
	  	  
			self.addFeatures(features);
			
			//-- maybe instead of this callback some event from the ol.source.Vector base could be used
			self._onPicturesLoad(features);
		/*
			var bindings = data.results.bindings;
			var features = [];
			var att, pt, feature, lastfeature = null;
			for ( var i in bindings )
			{	att = bindings[i];
				pt = [Number(bindings[i].long.value), Number(bindings[i].lat.value)];
				feature = new ol.Feature(new ol.geom.Point(ol.proj.transform (pt,"EPSG:4326",projection)));
				if (self.readFeature(feature, att, lastfeature))
				{	features.push(feature);
					lastfeature = feature;
				}
			}
			self.addFeatures(features);
			*/
		},
		error:  function(jqXHR, textStatus, errorThrown )
		{
			//onFailure(textStatus); //-- show error code returned
			console.error(errorThrown);
			console.error("Error loading feature types: " + textStatus + " " + errorThrown);
			//alert(errMsg);
		}			
	});
				
	/*
    function successHandler(data) {
      var transform = ol.proj.getTransform('EPSG:4326', 'EPSG:3857');
      data.features.forEach(function(item) {
        var feature = new ol.Feature(); // var feature = new ol.Feature(item);
        feature.set('url', item.properties.url);
		feature.set('thumbUrl', item.properties.thumbUrl);
        var coordinate = transform([parseFloat(item.geometry.coordinates[1]), parseFloat(item.geometry.coordinates[0])]);
        var geometry = new ol.geom.Point(coordinate);
        feature.setGeometry(geometry);
		
		feature.set('subject', "x");
		feature.set('lat', coordinate[1]);
		feature.set('long', coordinate[0]);
		feature.set('label', "z");
		feature.set('thumbnail', item.properties.thumbUrl);
		feature.set('abstract', "");
		feature.set('type', "");
		//feature.set('', "");

        pictureSource.addFeature(feature);
      });
    }
*/

	if (false)
	// Ajax request to get the tile
	$.ajax(
	{	url: this._url,
		dataType: 'jsonp', 
		data: { query: query, format:"json" },
		success: function(data) 
		{	var bindings = data.results.bindings;
			var features = [];
			var att, pt, feature, lastfeature = null;
			for ( var i in bindings )
			{	att = bindings[i];
				pt = [Number(bindings[i].long.value), Number(bindings[i].lat.value)];
				feature = new ol.Feature(new ol.geom.Point(ol.proj.transform (pt,"EPSG:4326",projection)));
				if (self.readFeature(feature, att, lastfeature))
				{	features.push(feature);
					lastfeature = feature;
				}
			}
			self.addFeatures(features);
    }});
};

(function(){

// Style cache
var styleCache = {};

/** Reset the cache (when fonts are loaded
*/
ol.style.clearDBPediaStyleCache = function()
{	styleCache = {};
}

/** Get a default style function for dbpedia
* options.glyph {string|function|undefined} a glyph name or a function that takes a feature and return a glyph
* options.radius {number} radius of the symbol, default 8
* options.fill {ol.style.Fill} style for fill, default navy
* options.stroke {ol.style.stroke} style for stroke, default 2px white
* options.prefix {string} a prefix if many style used for the same type
*
* @require ol.style.FontSymbol and FontAwesome defs are required for dbPediaStyleFunction()
*/
ol.style.dbPediaStyleFunction = function(options)
{	if (!options) options={};
	// Get font function using dbPedia type
	var getFont;
	switch (typeof(options.glyph))
	{	case "function": getFont = options.glyph; break;
		case "string": getFont = function(){ return options.glyph; }; break;
		default:
		{	getFont = function (f)
			{	var type = f.get("type");
				if (type)
				{	if (type.match("/Museum")) return "fa-camera";
					else if (type.match("/Monument")) return "fa-building";
					else if (type.match("/Sculpture")) return "fa-android";
					else if (type.match("/Religious")) return "fa-institution";
					else if (type.match("/Castle")) return "fa-key";
					else if (type.match("Water")) return "fa-tint";
					else if (type.match("Island")) return "fa-leaf";
					else if (type.match("/Event")) return "fa-heart";
					else if (type.match("/Artwork")) return "fa-asterisk";
					else if (type.match("/Stadium")) return "fa-futbol-o";
					else if (type.match("/Place")) return "fa-street-view";
				}
				return "fa-star";
			}
			break;
		}
	}
	// Default values
	var radius = options.radius || 8;
	var fill = options.fill || new ol.style.Fill({ color:"navy"});
	var stroke = options.stroke || new ol.style.Stroke({ color: "#fff", width: 2 });
	var prefix = options.prefix ? options.prefix+"_" : "";
	// Vector style function
	return function (feature, resolution)
	{	var glyph = getFont(feature);
		var k = prefix + glyph;
		var style = styleCache[k];
		if (!style)
		{	styleCache[k] = style = new ol.style.Style
			({	image: new ol.style.FontSymbol(
						{	glyph: glyph, 
							radius: radius, 
							fill: fill,
							stroke: stroke
						})
			});
		}
		return [style];
	}
};

})();
