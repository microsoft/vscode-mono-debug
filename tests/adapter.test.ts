/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

"use strict";

import assert = require('assert');
import * as Path from 'path';
import {DebugClient} from './debugClient';
import {DebugProtocol} from 'vscode-debugprotocol';

suite('Node Debug Adapter', () => {

	const PROJECT_ROOT = Path.join(__dirname, '../../');
	const DATA_ROOT = Path.join(PROJECT_ROOT, 'tests/data/');

	const DEBUG_ADAPTER = Path.join(PROJECT_ROOT, 'bin/Release/mono-debug.exe');


	let dc: DebugClient;

	setup(done => {
		dc = new DebugClient('mono', DEBUG_ADAPTER, 'mono');
		dc.start(done);
	});

	teardown(done => {
		dc.stop(done);
	});

	suite('basic', () => {

		test('unknown request should produce error', done => {
			dc.send('illegal_request').then(() => {
				done(new Error("does not report error on unknown request"));
			}).catch(() => {
				done();
			});
		});
	});

	suite('initialize', () => {

		test('should produce error for invalid \'pathFormat\'', done => {
			dc.initializeRequest({
				adapterID: 'mono',
				linesStartAt1: true,
				columnsStartAt1: true,
				pathFormat: 'url'
			}).then(response => {
				done(new Error("does not report error on invalid 'pathFormat' attribute"));
			}).catch(err => {
				// error expected
				done();
			});
		});
	});

	suite('launch', () => {

		test('should run program to the end', () => {

			const PROGRAM = Path.join(DATA_ROOT, 'simple/Program.exe');

			return Promise.all([
				dc.configurationSequence(),
				dc.launch({ program: PROGRAM }),
				dc.waitForEvent('terminated')
			]);
		});

		test('should stop on debugger statement', () => {

			const PROGRAM = Path.join(DATA_ROOT, 'simple_break/Program.exe');
			const DEBUGGER_LINE = 10;

			return Promise.all([
				dc.configurationSequence(),
				dc.launch({ program: PROGRAM }),
				dc.assertStoppedLocation('step', DEBUGGER_LINE)
			]);
		});
	});

	suite('setBreakpoints', () => {

		const PROGRAM = Path.join(DATA_ROOT, 'simple/Program.exe');
		const SOURCE = Path.join(DATA_ROOT, 'simple/Program.cs');
		const BREAKPOINT_LINE = 10;

		test('should stop on a breakpoint', () => {
			return dc.hitBreakpoint({ program: PROGRAM }, { path: SOURCE, line: BREAKPOINT_LINE } );
		});
	});

	suite('output events', () => {

		const PROGRAM = Path.join(DATA_ROOT, 'output/Output.exe');

		test('stdout and stderr events should be complete and in correct order', () => {
			return Promise.all([
				dc.configurationSequence(),
				dc.launch({ program: PROGRAM }),
				dc.assertOutput('stdout', "Hello stdout 0\nHello stdout 1\nHello stdout 2\n"),
				dc.assertOutput('stderr', "Hello stderr 0\nHello stderr 1\nHello stderr 2\n")
			]);
		});
	});
});