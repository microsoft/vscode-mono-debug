/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
"use strict";
var Path = require('path');
var DebugClient_1 = require('./DebugClient');
suite('Node Debug Adapter', function () {
    var DEBUG_ADAPTER = './bin/Debug/monoDebug.exe';
    var PROJECT_ROOT = Path.join(__dirname, '../../');
    var PROGRAM = Path.join(PROJECT_ROOT, 'tests/data/simple/Program.exe');
    var SOURCE = Path.join(PROJECT_ROOT, 'tests/data/simple/Program.cs');
    var BREAKPOINT_LINE = 2;
    var dc;
    setup(function (done) {
        dc = new DebugClient_1.DebugClient('mono', DEBUG_ADAPTER, 'mono');
        dc.start(done);
    });
    teardown(function (done) {
        dc.stop(done);
    });
    suite('basic', function () {
        test('unknown request should produce error', function (done) {
            dc.send('illegal_request').then(function () {
                done(new Error("does not report error on unknown request"));
            }).catch(function () {
                done();
            });
        });
    });
    suite('initialize', function () {
        test('should produce error for invalid \'pathFormat\'', function (done) {
            dc.initializeRequest({
                adapterID: 'mock',
                linesStartAt1: true,
                columnsStartAt1: true,
                pathFormat: 'url'
            }).then(function (response) {
                done(new Error("does not report error on invalid 'pathFormat' attribute"));
            }).catch(function (err) {
                // error expected
                done();
            });
        });
    });
    suite('launch', function () {
        test('should run program to the end', function () {
            return Promise.all([
                dc.configurationSequence(),
                dc.launch({ program: PROGRAM }),
                dc.waitForEvent('terminated')
            ]);
        });
    });
    suite('setBreakpoints', function () {
        var BREAKPOINT_LINE = 10;
        test('should stop on a breakpoint', function () {
            return dc.hitBreakpoint({ program: PROGRAM, }, SOURCE, BREAKPOINT_LINE);
        });
    });
});
//# sourceMappingURL=adapter.test.js.map