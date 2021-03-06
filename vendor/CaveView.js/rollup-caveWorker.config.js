import * as fs from 'fs';

function glsl () {
	return {
		transform ( code, id ) {
			if ( !/\.glsl$/.test( id ) ) return;

			return 'export default ' + JSON.stringify(
				code
					.replace( /[ \t]*\/\/.*\n/g, '' )
					.replace( /[ \t]*\/\*[\s\S]*?\*\//g, '' )
					.replace( /\n{2,}/g, '\n' )
			) + ';';
		}
	};
}

export default {
	treeshake: true,
	entry: 'src/js/workers/caveWorker.js',
	dest: 'build/CaveView/js/workers/caveWorker.js',
	format: 'umd',
	plugins: [
		glsl()
	]
};
