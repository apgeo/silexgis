// Qgis2threejs Project
project = new Q3D.Project({crs:"EPSG:4326",wgs84Center:{lat:45.22654,lon:23.8158815},proj:"+proj=longlat +datum=WGS84 +no_defs",title:"x52",baseExtent:[23.812533575,45.2227411552,23.819229425,45.2303388448],rotation:0,zShift:0.0,width:100.0,zExaggeration:5.5});

// Layer 0
lyr = project.addLayer(new Q3D.DEMLayer({q:1,shading:true,type:"dem",name:"Flat Plane"}));
bl = lyr.addBlock({frame:true,m:0,height:2,width:2,plane:{width:100.0,offsetX:0,offsetY:0,height:113.468634686},sides:true}, false);
bl.data = [0,0,0,0];
lyr.stats = {max:0,min:0};
lyr.m[0] = {c:"0",type:0,ds:1};

// Layer 1
lyr = project.addLayer(new Q3D.LineLayer({q:1,objType:"Pipe",type:"line",name:"2046-81_ponor_suspendat, tracks"}));
lyr.a = ["name","symbol","number","comment","description","source","url","url name"];
