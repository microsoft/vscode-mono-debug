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
var ProtocolClient_1 = require('./ProtocolClient');
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
            if (_this._supportsConfigurationDoneRequest) {
                return _this.configurationDoneRequest();
            }
            else {
                // if debug adapter doesn't support the configurationDoneRequest we has to send the setExceptionBreakpointsRequest.
                return _this.setExceptionBreakpointsRequest({ filters: ['all'] });
            }
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
    /*
     * Returns a promise that will resolve if a 'stopped' event was received within 3000ms
     * and the event's reason and line number was asserted.
     * The promise will be rejected if a timeout occurs, the assertions fail, or if the 'stackTrace' request fails.
     */
    DebugClient.prototype.assertStoppedLocation = function (reason, line) {
        var _this = this;
        return this.waitForEvent('stopped').then(function (event) {
            assert.equal(event.body.reason, reason);
            return _this.stacktraceRequest({
                threadId: event.body.threadId
            });
        }).then(function (response) {
            assert.equal(response.body.stackFrames[0].line, line);
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
                    if (output === expected) {
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
    DebugClient.prototype.hitBreakpoint = function (launchArgs, program, line) {
        var _this = this;
        return Promise.all([
            this.waitForEvent('initialized').then(function (event) {
                return _this.setBreakpointsRequest({
                    lines: [line],
                    breakpoints: [{ line: line }],
                    source: { path: program }
                });
            }).then(function (response) {
                var bp = response.body.breakpoints[0];
                assert.equal(bp.verified, true);
                assert.equal(bp.line, line);
                if (_this._supportsConfigurationDoneRequest) {
                    return _this.configurationDoneRequest();
                }
                else {
                    // if debug adapter doesn't support the configurationDoneRequest we have to send the setExceptionBreakpointsRequest.
                    return _this.setExceptionBreakpointsRequest({ filters: ['all'] });
                }
            }),
            this.launch(launchArgs),
            this.assertStoppedLocation('breakpoint', line)
        ]);
    };
    return DebugClient;
})(ProtocolClient_1.ProtocolClient);
exports.DebugClient = DebugClient;
//# sourceMappingURL=data:application/json;base64,eyJ2ZXJzaW9uIjozLCJmaWxlIjoiRGVidWdDbGllbnQuanMiLCJzb3VyY2VSb290IjoiLi4vc3JjLyIsInNvdXJjZXMiOlsiRGVidWdDbGllbnQudHMiXSwibmFtZXMiOlsiRGVidWdDbGllbnQiLCJEZWJ1Z0NsaWVudC5jb25zdHJ1Y3RvciIsIkRlYnVnQ2xpZW50LnN0YXJ0IiwiRGVidWdDbGllbnQuc3RvcCIsIkRlYnVnQ2xpZW50LmluaXRpYWxpemVSZXF1ZXN0IiwiRGVidWdDbGllbnQuY29uZmlndXJhdGlvbkRvbmVSZXF1ZXN0IiwiRGVidWdDbGllbnQubGF1bmNoUmVxdWVzdCIsIkRlYnVnQ2xpZW50LmF0dGFjaFJlcXVlc3QiLCJEZWJ1Z0NsaWVudC5kaXNjb25uZWN0UmVxdWVzdCIsIkRlYnVnQ2xpZW50LnNldEJyZWFrcG9pbnRzUmVxdWVzdCIsIkRlYnVnQ2xpZW50LnNldEV4Y2VwdGlvbkJyZWFrcG9pbnRzUmVxdWVzdCIsIkRlYnVnQ2xpZW50LmNvbnRpbnVlUmVxdWVzdCIsIkRlYnVnQ2xpZW50Lm5leHRSZXF1ZXN0IiwiRGVidWdDbGllbnQuc3RlcEluUmVxdWVzdCIsIkRlYnVnQ2xpZW50LnN0ZXBPdXRSZXF1ZXN0IiwiRGVidWdDbGllbnQucGF1c2VSZXF1ZXN0IiwiRGVidWdDbGllbnQuc3RhY2t0cmFjZVJlcXVlc3QiLCJEZWJ1Z0NsaWVudC5zY29wZXNSZXF1ZXN0IiwiRGVidWdDbGllbnQudmFyaWFibGVzUmVxdWVzdCIsIkRlYnVnQ2xpZW50LnNvdXJjZVJlcXVlc3QiLCJEZWJ1Z0NsaWVudC50aHJlYWRzUmVxdWVzdCIsIkRlYnVnQ2xpZW50LmV2YWx1YXRlUmVxdWVzdCIsIkRlYnVnQ2xpZW50LndhaXRGb3JFdmVudCIsIkRlYnVnQ2xpZW50LmNvbmZpZ3VyYXRpb25TZXF1ZW5jZSIsIkRlYnVnQ2xpZW50LmxhdW5jaCIsIkRlYnVnQ2xpZW50LmFzc2VydFN0b3BwZWRMb2NhdGlvbiIsIkRlYnVnQ2xpZW50LmFzc2VydE91dHB1dCIsIkRlYnVnQ2xpZW50LmhpdEJy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