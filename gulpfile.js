/*---------------------------------------------------------
 * Copyright (C) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------*/

var gulp = require('gulp');
var path = require('path');
var azure = require('gulp-azure-storage');
var git = require('git-rev-sync');
var del = require('del');
var runSequence = require('run-sequence');
var vzip = require('gulp-vinyl-zip');
var tsb = require('gulp-tsb');


var MONO_BOM = [
	'./bin/Release/ICSharpCode.NRefactory.CSharp.dll',
	'./bin/Release/ICSharpCode.NRefactory.dll',
	'./bin/Release/Mono.Cecil.dll',
	'./bin/Release/Mono.Cecil.Mdb.dll',
	'./bin/Release/Mono.Debugger.Soft.dll',
	'./bin/Release/Mono.Debugging.dll',
	'./bin/Release/Mono.Debugging.Soft.dll',
	'./bin/Release/Newtonsoft.Json.dll',
	'./bin/Release/mono-debug.exe',
	'./bin/Release/TerminalHelper.scpt'
];
var MONO_BOM2 = [
	'./package.json',
	'./LICENSE.txt',
	'./ThirdPartyNotices.txt'
];


var extensionDest = 'extension';
var extensionBin = path.join(extensionDest, 'bin', 'Release');
var uploadDest = 'upload/' + git.short();

gulp.task('default', function(callback) {
	runSequence('build', 'internal-compile', callback);
});

gulp.task('build', function(callback) {
	runSequence('clean', 'internal-bin-copy', 'internal-package-copy', callback);
});

gulp.task('zip', function(callback) {
	runSequence('build', 'internal-zip', callback);
});

gulp.task('upload', function(callback) {
	runSequence('zip', 'internal-upload', callback);
});

gulp.task('clean', function() {
	return del(['extension/**', 'upload/**']);
});

//---- internal

gulp.task('internal-bin-copy', function() {
	return gulp.src(MONO_BOM).pipe(gulp.dest(extensionBin));
});

gulp.task('internal-package-copy', function() {
	return gulp.src(MONO_BOM2).pipe(gulp.dest(extensionDest));
});

gulp.task('internal-zip', function(callback) {
	return gulp.src('extension/**/*')
		.pipe(vzip.dest(uploadDest + '/mono-debug.zip'));
});

gulp.task('internal-upload', function() {
	return gulp.src('upload/**/*')
		.pipe(azure.upload({
			account:    process.env.AZURE_STORAGE_ACCOUNT,
			key:        process.env.AZURE_STORAGE_ACCESS_KEY,
			container:  'debuggers'
		}));
});

//---- tests

var compilation = tsb.create(path.join(__dirname, 'tests/tsconfig.json'), true);

var sources = [
	'tests/**/*.ts'
];
var outDest = 'tests/out';

gulp.task('internal-compile', function() {
	return gulp.src(sources, { base: '.' })
		.pipe(compilation())
		.pipe(gulp.dest(outDest));
});
