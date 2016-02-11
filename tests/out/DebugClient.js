/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
"use strict";
var __extends = (this && this.__extends) || function (d, b) {
    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
    function __() { this.constructor = d; }
    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
};
var cp = require('child_process');
var assert = require('assert');
var net = require('net');
var protocolClient_1 = require('./protocolClient');
var DebugClient = (function (_super) {
    __extends(DebugClient, _super);
    /**
     * Creates a DebugClient object that provides a promise-based API to write
     * debug adapter tests.
     * A simple mocha example for setting and hitting a breakpoint in line 15 of a program 'test.js' looks like this:
     *
     * var dc;
     * setup(done => {
     *     dc = new DebugClient('node', './out/node/nodeDebug.js', 'node');
     *     dc.start(done);
     * });
     * teardown(done => {
     *     dc.stop(done);
     * });
     * test('should stop on a breakpoint', () => {
     *     return dc.hitBreakpoint({ program: "test.js" }, "test.js", 15);
     * });
     */
    function DebugClient(runtime, executable, debugType) {
        _super.call(this);
        this._runtime = runtime;
        this._executable = executable;
        this._enableStderr = false;
        this._debugType = debugType;
        this._supportsConfigurationDoneRequest = false;
    }
    // ---- life cycle --------------------------------------------------------------------------------------------------------
    /**
     * Starts a new debug adapter and sets up communication via stdin/stdout.
     * If a port number is specified the adapter is not launched but a connection to
     * a debug adapter running in server mode is established. This is useful for debugging
     * the adapter while running tests. For this reason all timeouts are disabled in server mode.
     */
    DebugClient.prototype.start = function (done, port) {
        var _this = this;
        if (typeof port === "number") {
            this._socket = net.createConnection(port, '127.0.0.1', function () {
                _this.connect(_this._socket, _this._socket);
                done();
            });
        }
        else {
            this._adapterProcess = cp.spawn(this._runtime, [this._executable], {
                stdio: [
                    'pipe',
                    'pipe',
                    'pipe' // stderr
                ],
            });
            var sanitize = function (s) { return s.toString().replace(/\r?\n$/mg, ''); };
            this._adapterProcess.stderr.on('data', function (data) {
                if (_this._enableStderr) {
                    console.log(sanitize(data));
                }
            });
            this._adapterProcess.on('error', function (err) {
                console.log(err);
            });
            this._adapterProcess.on('exit', function (code, signal) {
                if (code) {
                }
            });
            this.connect(this._adapterProcess.stdout, this._adapterProcess.stdin);
            done();
        }
    };
    /**
     * Shutdown the debug adapter (or disconnect if in server mode).
     */
    DebugClient.prototype.stop = function (done) {
        if (this._adapterProcess) {
            this._adapterProcess.kill();
            this._adapterProcess = null;
        }
        if (this._socket) {
            this._socket.end();
            this._socket = null;
        }
        done();
    };
    // ---- protocol requests -------------------------------------------------------------------------------------------------
    DebugClient.prototype.initializeRequest = function (args) {
        if (!args) {
            args = {
                adapterID: this._debugType,
                linesStartAt1: true,
                columnsStartAt1: true,
                pathFormat: 'path'
            };
        }
        return this.send('initialize', args);
    };
    DebugClient.prototype.configurationDoneRequest = function (args) {
        return this.send('configurationDone', args);
    };
    DebugClient.prototype.launchRequest = function (args) {
        return this.send('launch', args);
    };
    DebugClient.prototype.attachRequest = function (args) {
        return this.send('attach', args);
    };
    DebugClient.prototype.disconnectRequest = function (args) {
        return this.send('disconnect', args);
    };
    DebugClient.prototype.setBreakpointsRequest = function (args) {
        return this.send('setBreakpoints', args);
    };
    DebugClient.prototype.setFunctionBreakpointsRequest = function (args) {
        return this.send('setFunctionBreakpoints', args);
    };
    DebugClient.prototype.setExceptionBreakpointsRequest = function (args) {
        return this.send('setExceptionBreakpoints', args);
    };
    DebugClient.prototype.continueRequest = function (args) {
        return this.send('continue', args);
    };
    DebugClient.prototype.nextRequest = function (args) {
        return this.send('next', args);
    };
    DebugClient.prototype.stepInRequest = function (args) {
        return this.send('stepIn', args);
    };
    DebugClient.prototype.stepOutRequest = function (args) {
        return this.send('stepOut', args);
    };
    DebugClient.prototype.pauseRequest = function (args) {
        return this.send('pause', args);
    };
    DebugClient.prototype.stacktraceRequest = function (args) {
        return this.send('stackTrace', args);
    };
    DebugClient.prototype.scopesRequest = function (args) {
        return this.send('scopes', args);
    };
    DebugClient.prototype.variablesRequest = function (args) {
        return this.send('variables', args);
    };
    DebugClient.prototype.sourceRequest = function (args) {
        return this.send('source', args);
    };
    DebugClient.prototype.threadsRequest = function () {
        return this.send('threads');
    };
    DebugClient.prototype.evaluateRequest = function (args) {
        return this.send('evaluate', args);
    };
    // ---- convenience methods -----------------------------------------------------------------------------------------------
    /*
     * Returns a promise that will resolve if an event with a specific type was received within the given timeout.
     * The promise will be rejected if a timeout occurs.
     */
    DebugClient.prototype.waitForEvent = function (eventType, timeout) {
        var _this = this;
        if (timeout === void 0) { timeout = 3000; }
        return new Promise(function (resolve, reject) {
            _this.on(eventType, function (event) {
                resolve(event);
            });
            if (!_this._socket) {
                setTimeout(function () {
                    reject(new Error("no event '" + eventType + "' received after " + timeout + " ms"));
                }, timeout);
            }
        });
    };
    /*
     * Returns a promise that will resolve if an 'initialized' event was received within 3000ms
     * and a subsequent 'configurationDone' request was successfully executed.
     * The promise will be rejected if a timeout occurs or if the 'configurationDone' request fails.
     */
    DebugClient.prototype.configurationSequence = function () {
        var _this = this;
        return this.waitForEvent('initialized').then(function (event) {
            return _this.configurationDone();
        });
    };
    /**
     * Returns a promise that will resolve if a 'initialize' and a 'launch' request were successful.
     */
    DebugClient.prototype.launch = function (args) {
        var _this = this;
        return this.initializeRequest().then(function (response) {
            if (response.body && response.body.supportsConfigurationDoneRequest) {
                _this._supportsConfigurationDoneRequest = true;
            }
            return _this.launchRequest(args);
        });
    };
    DebugClient.prototype.configurationDone = function () {
        if (this._supportsConfigurationDoneRequest) {
            return this.configurationDoneRequest();
        }
        else {
            // if debug adapter doesn't support the configurationDoneRequest we have to send the setExceptionBreakpointsRequest.
            return this.setExceptionBreakpointsRequest({ filters: ['all'] });
        }
    };
    /*
     * Returns a promise that will resolve if a 'stopped' event was received within 3000ms
     * and the event's reason and line number was asserted.
     * The promise will be rejected if a timeout occurs, the assertions fail, or if the 'stackTrace' request fails.
     */
    DebugClient.prototype.assertStoppedLocation = function (reason, expected) {
        var _this = this;
        return this.waitForEvent('stopped').then(function (event) {
            assert.equal(event.body.reason, reason);
            return _this.stacktraceRequest({
                threadId: event.body.threadId
            });
        }).then(function (response) {
            var frame = response.body.stackFrames[0];
            if (typeof expected.path === 'string') {
                assert.equal(frame.source.path, expected.path, "stopped location: path mismatch");
            }
            if (typeof expected.line === 'number') {
                assert.equal(frame.line, expected.line, "stopped location: line mismatch");
            }
            if (typeof expected.column === 'number') {
                assert.equal(frame.column, expected.column, "stopped location: column mismatch");
            }
            return response;
        });
    };
    /*
     * Returns a promise that will resolve if enough output events with the given category have been received
     * and the concatenated data match the expected data.
     * The promise will be rejected as soon as the received data cannot match the expected data or if a timeout occurs.
     */
    DebugClient.prototype.assertOutput = function (category, expected, timeout) {
        var _this = this;
        if (timeout === void 0) { timeout = 3000; }
        return new Promise(function (resolve, reject) {
            var output = '';
            _this.on('output', function (event) {
                var e = event;
                if (e.body.category === category) {
                    output += e.body.output;
                    if (output.indexOf(expected) === 0) {
                        resolve(event);
                    }
                    else if (expected.indexOf(output) !== 0) {
                        var sanitize = function (s) { return s.toString().replace(/\r/mg, '\\r').replace(/\n/mg, '\\n'); };
                        reject(new Error("received data '" + sanitize(output) + "' is not a prefix of the expected data '" + sanitize(expected) + "'"));
                    }
                }
            });
            if (!_this._socket) {
                setTimeout(function () {
                    reject(new Error("not enough output data received after " + timeout + " ms"));
                }, timeout);
            }
        });
    };
    // ---- scenarios ---------------------------------------------------------------------------------------------------------
    /**
     * Returns a promise that will resolve if a configurable breakpoint has been hit within 3000ms
     * and the event's reason and line number was asserted.
     * The promise will be rejected if a timeout occurs, the assertions fail, or if the requests fails.
     */
    DebugClient.prototype.hitBreakpoint = function (launchArgs, location, expected) {
        var _this = this;
        return Promise.all([
            this.waitForEvent('initialized').then(function (event) {
                return _this.setBreakpointsRequest({
                    lines: [location.line],
                    breakpoints: [{ line: location.line, column: location.column }],
                    source: { path: location.path }
                });
            }).then(function (response) {
                var bp = response.body.breakpoints[0];
                var verified = (typeof location.verified === 'boolean') ? location.verified : true;
                assert.equal(bp.verified, verified, "breakpoint verification mismatch: verified");
                if (bp.source && bp.source.path) {
                    assert.equal(bp.source.path, location.path, "breakpoint verification mismatch: path");
                }
                if (typeof bp.line === 'number') {
                    assert.equal(bp.line, location.line, "breakpoint verification mismatch: line");
                }
                if (typeof location.column === 'number' && typeof bp.column === 'number') {
                    assert.equal(bp.column, location.column, "breakpoint verification mismatch: column");
                }
                return _this.configurationDone();
            }),
            this.launch(launchArgs),
            this.assertStoppedLocation('breakpoint', expected || location)
        ]);
    };
    return DebugClient;
})(protocolClient_1.ProtocolClient);
exports.DebugClient = DebugClient;
//# sourceMappingURL=data:application/json;base64,eyJ2ZXJzaW9uIjozLCJmaWxlIjoiRGVidWdDbGllbnQuanMiLCJzb3VyY2VSb290IjoiLi4vc3JjLyIsInNvdXJjZXMiOlsiRGVidWdDbGllbnQudHMiXSwibmFtZXMiOlsiRGVidWdDbGllbnQiLCJEZWJ1Z0NsaWVudC5jb25zdHJ1Y3RvciIsIkRlYnVnQ2xpZW50LnN0YXJ0IiwiRGVidWdDbGllbnQuc3RvcCIsIkRlYnVnQ2xpZW50LmluaXRpYWxpemVSZXF1ZXN0IiwiRGVidWdDbGllbnQuY29uZmlndXJhdGlvbkRvbmVSZXF1ZXN0IiwiRGVidWdDbGllbnQubGF1bmNoUmVxdWVzdCIsIkRlYnVnQ2xpZW50LmF0dGFjaFJlcXVlc3QiLCJEZWJ1Z0NsaWVudC5kaXNjb25uZWN0UmVxdWVzdCIsIkRlYnVnQ2xpZW50LnNldEJyZWFrcG9pbnRzUmVxdWVzdCIsIkRlYnVnQ2xpZW50LnNldEZ1bmN0aW9uQnJlYWtwb2ludHNSZXF1ZXN0IiwiRGVidWdDbGllbnQuc2V0RXhjZXB0aW9uQnJlYWtwb2ludHNSZXF1ZXN0IiwiRGVidWdDbGllbnQuY29udGludWVSZXF1ZXN0IiwiRGVidWdDbGllbnQubmV4dFJlcXVlc3QiLCJEZWJ1Z0NsaWVudC5zdGVwSW5SZXF1ZXN0IiwiRGVidWdDbGllbnQuc3RlcE91dFJlcXVlc3QiLCJEZWJ1Z0NsaWVudC5wYXVzZVJlcXVlc3QiLCJEZWJ1Z0NsaWVudC5zdGFja3RyYWNlUmVxdWVzdCIsIkRlYnVnQ2xpZW50LnNjb3Blc1JlcXVlc3QiLCJEZWJ1Z0NsaWVudC52YXJpYWJsZXNSZXF1ZXN0IiwiRGVidWdDbGllbnQuc291cmNlUmVxdWVzdCIsIkRlYnVnQ2xpZW50LnRocmVhZHNSZXF1ZXN0IiwiRGVidWdDbGllbnQuZXZhbHVhdGVSZXF1ZXN0IiwiRGVidWdDbGllbnQud2FpdEZvckV2ZW50IiwiRGVidWdDbGllbnQuY29uZmlndXJhdGlvblNlcXVlbmNlIiwiRGVidWdDbGllbnQubGF1bmNoIiwiRGVidWdDbGllbnQuY29uZmlndXJhdGlvbkRvbmUiLCJEZWJ1Z0NsaWVudC5hc3NlcnRTdG9wcGVkTG9jYXRpb24iLCJEZWJ1Z0NsaWVudC5hc3NlcnRPdXRwdXQiLCJEZWJ1Z0NsaWVudC5oaXRCcmVha3BvaW50Il0sIm1hcHBpbmdzIjoiQUFBQTs7O2dHQUdnRztBQUVoRyxZQUFZLENBQUM7Ozs7OztBQUViLElBQU8sRUFBRSxXQUFXLGVBQWUsQ0FBQyxDQUFDO0FBQ3JDLElBQU8sTUFBTSxXQUFXLFFBQVEsQ0FBQyxDQUFDO0FBQ2xDLElBQU8sR0FBRyxXQUFXLEtBQUssQ0FBQyxDQUFDO0FBRTVCLCtCQUE2QixrQkFBa0IsQ0FBQyxDQUFBO0FBR2hEO0lBQWlDQSwrQkFBY0E7SUFXOUNBOzs7Ozs7Ozs7Ozs7Ozs7O09BZ0JHQTtJQUNIQSxxQkFBWUEsT0FBZUEsRUFBRUEsVUFBa0JBLEVBQUVBLFNBQWlCQTtRQUNqRUMsaUJBQU9BLENBQUNBO1FBQ1JBLElBQUlBLENBQUNBLFFBQVFBLEdBQUdBLE9BQU9BLENBQUNBO1FBQ3hCQSxJQUFJQSxDQUFDQSxXQUFXQSxHQUFHQSxVQUFVQSxDQUFDQTtRQUM5QkEsSUFBSUEsQ0FBQ0EsYUFBYUEsR0FBR0EsS0FBS0EsQ0FBQ0E7UUFDM0JBLElBQUlBLENBQUNBLFVBQVVBLEdBQUdBLFNBQVNBLENBQUNBO1FBQzVCQSxJQUFJQSxDQUFDQSxpQ0FBaUNBLEdBQUdBLEtBQUtBLENBQUNBO0lBQ2hEQSxDQUFDQTtJQUVERCwySEFBMkhBO0lBRTNIQTs7Ozs7T0FLR0E7SUFDSUEsMkJBQUtBLEdBQVpBLFVBQWFBLElBQUlBLEVBQUVBLElBQWFBO1FBQWhDRSxpQkFtQ0NBO1FBakNBQSxFQUFFQSxDQUFDQSxDQUFDQSxPQUFPQSxJQUFJQSxLQUFLQSxRQUFRQSxDQUFDQSxDQUFDQSxDQUFDQTtZQUM5QkEsSUFBSUEsQ0FBQ0EsT0FBT0EsR0FBR0EsR0FBR0EsQ0FBQ0EsZ0JBQWdCQSxDQUFDQSxJQUFJQSxFQUFFQSxXQUFXQSxFQUFFQTtnQkFDdERBLEtBQUlBLENBQUNBLE9BQU9BLENBQUNBLEtBQUlBLENBQUNBLE9BQU9B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