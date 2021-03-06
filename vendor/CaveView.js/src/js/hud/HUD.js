
import {
	MATERIAL_LINE,
	SHADING_CURSOR, SHADING_DEPTH, SHADING_DEPTH_CURSOR, SHADING_HEIGHT, SHADING_INCLINATION, SHADING_LENGTH,
} from '../core/constants';

import { Viewer } from '../viewer/Viewer';

import { AHI } from './AHI';
import { AngleScale } from './AngleScale';
import { Compass } from './Compass';
import { LinearScale } from './LinearScale';
import { CursorScale } from './CursorScale';
import { ProgressDial } from './ProgressDial';
import { ScaleBar } from './ScaleBar';
import { HudObject } from './HudObject';

import { Materials } from '../materials/Materials';

import {
	Scene, Group,
	AmbientLight, DirectionalLight,
	OrthographicCamera
} from '../../../../three.js/src/Three';

// THREE objects

var renderer;
var camera;
var scene;

var hScale = 0;

var attitudeGroup;

var linearScale = null;
var angleScale  = null;
var cursorScale = null;
var scaleBar    = null;

var compass;
var ahi;
var progressDial;

// DOM objects

var container;

// viewer state

var controls;
var isVisible = true;

function init ( domId, viewRenderer ) {

	container = document.getElementById( domId );
	renderer = viewRenderer;

	var hHeight = container.clientHeight / 2;
	var hWidth  = container.clientWidth / 2;

	// create GL scene and camera for overlay
	camera = new OrthographicCamera( -hWidth, hWidth, hHeight, -hHeight, 1, 1000 );
	camera.position.z = 600;

	scene = new Scene();

	// group to simplyfy resize handling
	attitudeGroup = new Group();
	attitudeGroup.position.set( hWidth, -hHeight, 0 );

	scene.add( attitudeGroup );

	var aLight = new AmbientLight( 0x888888 );
	var dLight = new DirectionalLight( 0xFFFFFF );

	dLight.position.set( -1, 1, 1 );

	scene.add( aLight );
	scene.add( dLight );

	compass      = new Compass( container );
	ahi          = new AHI( container );
	progressDial = new ProgressDial();

	attitudeGroup.add( compass );
	attitudeGroup.add( ahi );
	attitudeGroup.add( progressDial );

	window.addEventListener( 'resize', resize );

	Viewer.addEventListener( 'newCave', caveChanged );
	Viewer.addEventListener( 'change', viewChanged );

	controls = Viewer.getControls();

	controls.addEventListener( 'change', update );

}

function setVisibility ( visible ) {

	compass.setVisibility( visible );
	ahi.setVisibility( visible );
	progressDial.setVisibility( visible );

	if ( scaleBar ) scaleBar.setVisibility( visible );

	isVisible = visible;

	// reset correct disposition of colour keys etc.
	if ( linearScale ) {

		if ( visible ) {

			viewChanged ( { type: 'change', name: 'shadingMode' } );

		} else {

			linearScale.setVisibility( false );
			cursorScale.setVisibility( false );
			angleScale.setVisibility( false );

		}

	}

	Viewer.renderView();

}

function getVisibility() {

	return isVisible;

}

function getProgressDial() {

	return progressDial;

}

function setScale( scale ) {

	hScale = scale;

}

function resize () {

	var hWidth  = container.clientWidth / 2;
	var hHeight = container.clientHeight / 2;

	// adjust cameras to new aspect ratio etc.
	camera.left   = -hWidth;
	camera.right  =  hWidth;
	camera.top    =  hHeight;
	camera.bottom = -hHeight;

	camera.updateProjectionMatrix();

	attitudeGroup.position.set( hWidth, -hHeight, 0 );

	newScales();

	setVisibility ( isVisible ); // set correct visibility of elements

}

function update () {

	// update HUD components

	var currentCamera = controls.object;

	compass.set( currentCamera );
	ahi.set( currentCamera );
	updateScaleBar( currentCamera );

}

function renderHUD () {

	// render on screen
	renderer.clearDepth();
	renderer.render( scene, camera );

}

function caveChanged ( /* event */ ) {

	newScales();

	viewChanged ( { type: 'change', name: 'shadingMode' } );

}


function newScales () {

	if ( linearScale ) scene.remove( linearScale );

	linearScale = new LinearScale( container, Viewer );

	scene.add( linearScale );


	if ( cursorScale ) scene.remove( cursorScale );

	cursorScale = new CursorScale( container );

	scene.add( cursorScale );


	if ( angleScale ) scene.remove( angleScale );

	angleScale = new AngleScale( container );

	scene.add( angleScale );

	if ( scaleBar ) {

		scene.remove( scaleBar );
		scaleBar = null;

	}

	updateScaleBar( controls.object );

}

function viewChanged ( event ) {

	if ( event.name !== 'shadingMode' || ! isVisible ) return;

	// hide all - and only make required elements visible

	var useAngleScale = false;
	var useLinearScale = false;
	var useCursorScale = false;

	switch ( Viewer.shadingMode ) {

	case SHADING_HEIGHT:

		useLinearScale = true;

		linearScale.setRange( Viewer.minHeight, Viewer.maxHeight, 'Height above Datum' ).setMaterial( Materials.getHeightMaterial( MATERIAL_LINE ) );

		break;

	case SHADING_DEPTH:

		useLinearScale = true;

		linearScale.setRange( Viewer.maxHeight - Viewer.minHeight, 0, 'Depth below surface' ).setMaterial( Materials.getHeightMaterial( MATERIAL_LINE ) );

		break;

	case SHADING_CURSOR:

		useCursorScale = true;

		cursorScale.setRange( Viewer.minHeight, Viewer.maxHeight, 'Height' );

		cursorChanged();

		break;

	case SHADING_DEPTH_CURSOR:

		useCursorScale = true;

		cursorScale.setRange( Viewer.maxHeight - Viewer.minHeight, 0, 'Depth' );

		cursorChanged();

		break;

	case SHADING_LENGTH:

		useLinearScale = true;

		linearScale.setRange( Viewer.minLegLength, Viewer.maxLegLength, 'Leg length' ).setMaterial( Materials.getHeightMaterial( MATERIAL_LINE, true ) ).setVisibility( true );

		break;

	case SHADING_INCLINATION:

		useAngleScale = true;

		break;

	}

	angleScale.setVisibility( useAngleScale );
	linearScale.setVisibility( useLinearScale );
	cursorScale.setVisibility( useCursorScale );

	if ( useCursorScale ) {

		Viewer.addEventListener( 'cursorChange', cursorChanged );

	} else {

		Viewer.removeEventListener( 'cursorChange', cursorChanged );

	}

	Viewer.renderView();

}

function cursorChanged ( /* event */ ) {

	var cursorHeight = Viewer.cursorHeight;
	var range = Viewer.maxHeight - Viewer.minHeight;
	var scaledHeight = 0;

	if ( Viewer.shadingMode === SHADING_CURSOR ) {

		scaledHeight = ( Viewer.cursorHeight + range / 2 ) / range;

	} else {

		scaledHeight = 1 - cursorHeight / range;

	}

	scaledHeight = Math.max( Math.min( scaledHeight, 1 ), 0 );

	cursorScale.setCursor( scaledHeight, Math.round( cursorHeight ) );

}

function updateScaleBar ( camera ) {

	if ( camera instanceof OrthographicCamera ) {

		if ( scaleBar === null ) {

			scaleBar = new ScaleBar( container, hScale, ( HudObject.stdWidth + HudObject.stdMargin ) * 4 );
			scene.add( scaleBar );

		}

		if ( isVisible !== scaleBar.visible ) scaleBar.setVisibility( isVisible );

		scaleBar.setScale( camera.zoom );

	} else {

		if ( scaleBar !== null && scaleBar.visible ) scaleBar.setVisibility( false );

	}

}

export var HUD = {
	init:               init,
	renderHUD:          renderHUD,
	update:             update,
	setVisibility:		setVisibility,
	getVisibility:		getVisibility,
	getProgressDial:    getProgressDial,
	setScale:           setScale
};

// EOF