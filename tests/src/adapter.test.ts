/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

"use strict";

import assert = require('assert');
import * as Path from 'path';
import {DebugClient} from './DebugClient';
import {DebugProtocol} from 'vscode-debugprotocol';

suite('Node Debug Adapter', () => {

	const DEBUG_ADAPTER = './bin/Debug/monoDebug.exe';

	const PROJECT_ROOT = Path.join(__dirname, '../../');
	const PROGRAM = Path.join(PROJECT_ROOT, 'tests/data/simple/Program.exe');
	const SOURCE = Path.join(PROJECT_ROOT, 'tests/data/simple/Program.cs');

	const BREAKPOINT_LINE = 2;

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
				adapterID: 'mock',
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

			return Promise.all([
				dc.configurationSequence(),
				dc.launch({ program: PROGRAM }),
				dc.waitForEvent('terminated')
			]);
		});
	});

	suite('setBreakpoints', () => {

		const BREAKPOINT_LINE = 10;

		test('should stop on a breakpoint', () => {
			return dc.hitBreakpoint({ program: PROGRAM, }, SOURCE, BREAKPOINT_LINE);
		});
	});

});