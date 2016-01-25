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
                // console.log('exit');
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
                    // if debug adapter doesn't support the configurationDoneRequest we has to send the setExceptionBreakpointsRequest.
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
//# sourceMappingURL=DebugClient.js.map