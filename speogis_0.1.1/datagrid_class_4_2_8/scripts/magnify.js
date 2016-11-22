<!--

var offsetfrommouse=[15,15]; //image x,y offsets from cursor position in pixels. Enter 0,0 for no offset
if (document.getElementById || document.all){
	document.write('<div id="trailimageid">');
	document.write('</div>');
}

function showtrail(imagename,title,description,showthumb,height,filetype, width){
	if (height > 0){
		currentimageheight = height;
	}

	document.onmousemove=followmouse;

	cameraHTML = '';

	
	newHTML = '<div style="padding: 5px; background-color: #FFF; border: 1px solid #888;">';
	if(title != "") newHTML = newHTML + '<h2>' + title + '</h2>';
	if(description != "") newHTML = newHTML + description.replace(/\[[^\]]*\]/g, '') + '<br/>';

	if (showthumb > 0){
		newHTML = newHTML + '<div align="center" style="padding: 8px 2px 2px 2px;">';
		if(filetype == 8) { // Video
			newHTML = newHTML +	'<object width="380" height="285" classid="clsid:D27CDB6E-AE6D-11cf-96B8-444553540000" codebase="http://download.macromedia.com/pub/shockwave/cabs/flash/swflash.cab#version=6,0,0,0">';
			newHTML = newHTML + '<param name="movie" value="video_loupe.swf">';
			newHTML = newHTML + '<param name="quality" value="best">';
			newHTML = newHTML + '<param name="loop" value="true">';

			newHTML = newHTML + '<param name="FlashVars" value="videoLocation=' + imagename + '&bufferPercent=25">';
			newHTML = newHTML + '<EMBED SRC="video_loupe.swf" LOOP="true" QUALITY="best" FlashVars="videoLocation=' + imagename + '&bufferPercent=25" WIDTH="380" HEIGHT="285">';
			newHTML = newHTML + '</object></div>';
		} else {
			newHTML = newHTML + '<img src="' + imagename + '"';
			if ( filetype == 1 && height > 0 && width > 0 ){
				newHTML = newHTML + ' height="' + height + '" width="' + width + '"';
			}
			newHTML = newHTML + ' border="0"/></div>';
		}
	}

	newHTML = newHTML + '</div>';
	gettrailobjnostyle().innerHTML = newHTML;
	gettrailobj().display="inline";
}

function hidetrail(){
	gettrailobj().innerHTML = " ";
	gettrailobj().display="none"
	document.onmousemove=""
	gettrailobj().left="-500px"

}

function followmouse(e){

	var xcoord=offsetfrommouse[0]
	var ycoord=offsetfrommouse[1]

	var docwidth=document.all? truebody().scrollLeft+truebody().clientWidth : pageXOffset+window.innerWidth-15
	var docheight=document.all? Math.min(truebody().scrollHeight, truebody().clientHeight) : Math.min(window.innerHeight)

	//if (document.all){
	//	gettrailobjnostyle().innerHTML = 'A = ' + truebody().scrollHeight + '<br>B = ' + truebody().clientHeight;
	//} else {
	//	gettrailobjnostyle().innerHTML = 'C = ' + document.body.offsetHeight + '<br>D = ' + window.innerHeight;
	//}

	if (typeof e != "undefined"){
		if (docwidth - e.pageX < 380){
			xcoord = e.pageX - xcoord - 400; // Move to the left side of the cursor
		} else {
			xcoord += e.pageX;
		}
		if (docheight - e.pageY < (currentimageheight + 110)){
			// truebody().scrollTop is always zero in Safari 3.1, so we us documnet.body.scrollTop instead
			if ( document.body ){
				scrollTop = Math.max(truebody().scrollTop, document.body.scrollTop);
			} else {
				scrollTop = truebody().scrollTop;
			}
			ycoord += e.pageY - Math.max(0,(110 + currentimageheight + e.pageY - docheight - scrollTop));
		} else {
			ycoord += e.pageY;
		}

	} else if (typeof window.event != "undefined"){
		if (docwidth - event.clientX < 380){
			xcoord = event.clientX + truebody().scrollLeft - xcoord - 400; // Move to the left side of the cursor
		} else {
			xcoord += truebody().scrollLeft+event.clientX
		}
		if (docheight - event.clientY < (currentimageheight + 110)){
			ycoord += event.clientY + truebody().scrollTop - Math.max(0,(110 + currentimageheight + event.clientY - docheight));
		} else {
			ycoord += truebody().scrollTop + event.clientY;
		}
	}

	if(ycoord < 0) { ycoord = ycoord*-1; }
	gettrailobj().left=xcoord+"px"
	gettrailobj().top=ycoord+"px"

}

function gettrailobj(){
  if (document.getElementById)
  return document.getElementById("trailimageid").style
  else if (document.all)
  return document.all.trailimagid.style
}

function gettrailobjnostyle(){
  if (document.getElementById)
  return document.getElementById("trailimageid")
  else if (document.all)
  return document.all.trailimagid
}

function truebody(){
  return (!window.opera && document.compatMode && document.compatMode!="BackCompat")? document.documentElement : document.body
}  

//-->