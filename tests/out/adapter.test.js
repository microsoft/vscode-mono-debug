/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
"use strict";
var Path = require('path');
var DebugClient_1 = require('./DebugClient');
suite('Node Debug Adapter', function () {
    var PROJECT_ROOT = Path.join(__dirname, '../../');
    var DATA_ROOT = Path.join(PROJECT_ROOT, 'tests/data/');
    var DEBUG_ADAPTER = Path.join(PROJECT_ROOT, 'bin/Release/mono-debug.exe');
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
            var PROGRAM = Path.join(DATA_ROOT, 'simple/Program.exe');
            return Promise.all([
                dc.configurationSequence(),
                dc.launch({ program: PROGRAM }),
                dc.waitForEvent('terminated')
            ]);
        });
        test('should stop on debugger statement', function () {
            var PROGRAM = Path.join(DATA_ROOT, 'simple_break/Program.exe');
            var DEBUGGER_LINE = 10;
            return Promise.all([
                dc.configurationSequence(),
                dc.launch({ program: PROGRAM }),
                dc.assertStoppedLocation('step', DEBUGGER_LINE)
            ]);
        });
    });
    suite('setBreakpoints', function () {
        var PROGRAM = Path.join(DATA_ROOT, 'simple/Program.exe');
        var SOURCE = Path.join(DATA_ROOT, 'simple/Program.cs');
        var BREAKPOINT_LINE = 10;
        test('should stop on a breakpoint', function () {
            return dc.hitBreakpoint({ program: PROGRAM }, { path: SOURCE, line: BREAKPOINT_LINE });
        });
    });
    suite('output events', function () {
        var PROGRAM = Path.join(DATA_ROOT, 'output/Output.exe');
        test('stdout and stderr events should be complete and in correct order', function () {
            return Promise.all([
                dc.configurationSequence(),
                dc.launch({ program: PROGRAM }),
                dc.assertOutput('stdout', "Hello stdout 0\nHello stdout 1\nHello stdout 2\n"),
                dc.assertOutput('stderr', "Hello stderr 0\nHello stderr 1\nHello stderr 2\n")
            ]);
        });
    });
});
//# sourceMappingURL=data:application/json;base64,eyJ2ZXJzaW9uIjozLCJmaWxlIjoiYWRhcHRlci50ZXN0LmpzIiwic291cmNlUm9vdCI6Ii4uL3NyYy8iLCJzb3VyY2VzIjpbImFkYXB0ZXIudGVzdC50cyJdLCJuYW1lcyI6W10sIm1hcHBpbmdzIjoiQUFBQTs7O2dHQUdnRztBQUVoRyxZQUFZLENBQUM7QUFHYixJQUFZLElBQUksV0FBTSxNQUFNLENBQUMsQ0FBQTtBQUM3Qiw0QkFBMEIsZUFBZSxDQUFDLENBQUE7QUFHMUMsS0FBSyxDQUFDLG9CQUFvQixFQUFFO0lBRTNCLElBQU0sWUFBWSxHQUFHLElBQUksQ0FBQyxJQUFJLENBQUMsU0FBUyxFQUFFLFFBQVEsQ0FBQyxDQUFDO0lBQ3BELElBQU0sU0FBUyxHQUFHLElBQUksQ0FBQyxJQUFJLENBQUMsWUFBWSxFQUFFLGFBQWEsQ0FBQyxDQUFDO0lBRXpELElBQU0sYUFBYSxHQUFHLElBQUksQ0FBQyxJQUFJLENBQUMsWUFBWSxFQUFFLDRCQUE0QixDQUFDLENBQUM7SUFHNUUsSUFBSSxFQUFlLENBQUM7SUFFcEIsS0FBSyxDQUFDLFVBQUEsSUFBSTtRQUNULEVBQUUsR0FBRyxJQUFJLHlCQUFXLENBQUMsTUFBTSxFQUFFLGFBQWEsRUFBRSxNQUFNLENBQUMsQ0FBQztRQUNwRCxFQUFFLENBQUMsS0FBSyxDQUFDLElBQUksQ0FBQyxDQUFDO0lBQ2hCLENBQUMsQ0FBQyxDQUFDO0lBRUgsUUFBUSxDQUFDLFVBQUEsSUFBSTtRQUNaLEVBQUUsQ0FBQyxJQUFJLENBQUMsSUFBSSxDQUFDLENBQUM7SUFDZixDQUFDLENBQUMsQ0FBQztJQUVILEtBQUssQ0FBQyxPQUFPLEVBQUU7UUFFZCxJQUFJLENBQUMsc0NBQXNDLEVBQUUsVUFBQSxJQUFJO1lBQ2hELEVBQUUsQ0FBQyxJQUFJLENBQUMsaUJBQWlCLENBQUMsQ0FBQyxJQUFJLENBQUM7Z0JBQy9CLElBQUksQ0FBQyxJQUFJLEtBQUssQ0FBQywwQ0FBMEMsQ0FBQyxDQUFDLENBQUM7WUFDN0QsQ0FBQyxDQUFDLENBQUMsS0FBSyxDQUFDO2dCQUNS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