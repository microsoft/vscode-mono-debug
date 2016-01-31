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
                return _this.configurationDone();
            }),
            this.launch(launchArgs),
            this.assertStoppedLocation('breakpoint', line)
        ]);
    };
    return DebugClient;
})(ProtocolClient_1.ProtocolClient);
exports.DebugClient = DebugClient;
//# sourceMappingURL=data:application/json;base64,eyJ2ZXJzaW9uIjozLCJmaWxlIjoiRGVidWdDbGllbnQuanMiLCJzb3VyY2VSb290IjoiLi4vc3JjLyIsInNvdXJjZXMiOlsiRGVidWdDbGllbnQudHMiXSwibmFtZXMiOlsiRGVidWdDbGllbnQiLCJEZWJ1Z0NsaWVudC5jb25zdHJ1Y3RvciIsIkRlYnVnQ2xpZW50LnN0YXJ0IiwiRGVidWdDbGllbnQuc3RvcCIsIkRlYnVnQ2xpZW50LmluaXRpYWxpemVSZXF1ZXN0IiwiRGVidWdDbGllbnQuY29uZmlndXJhdGlvbkRvbmVSZXF1ZXN0IiwiRGVidWdDbGllbnQubGF1bmNoUmVxdWVzdCIsIkRlYnVnQ2xpZW50LmF0dGFjaFJlcXVlc3QiLCJEZWJ1Z0NsaWVudC5kaXNjb25uZWN0UmVxdWVzdCIsIkRlYnVnQ2xpZW50LnNldEJyZWFrcG9pbnRzUmVxdWVzdCIsIkRlYnVnQ2xpZW50LnNldEV4Y2VwdGlvbkJyZWFrcG9pbnRzUmVxdWVzdCIsIkRlYnVnQ2xpZW50LmNvbnRpbnVlUmVxdWVzdCIsIkRlYnVnQ2xpZW50Lm5leHRSZXF1ZXN0IiwiRGVidWdDbGllbnQuc3RlcEluUmVxdWVzdCIsIkRlYnVnQ2xpZW50LnN0ZXBPdXRSZXF1ZXN0IiwiRGVidWdDbGllbnQucGF1c2VSZXF1ZXN0IiwiRGVidWdDbGllbnQuc3RhY2t0cmFjZVJlcXVlc3QiLCJEZWJ1Z0NsaWVudC5zY29wZXNSZXF1ZXN0IiwiRGVidWdDbGllbnQudmFyaWFibGVzUmVxdWVzdCIsIkRlYnVnQ2xpZW50LnNvdXJjZVJlcXVlc3QiLCJEZWJ1Z0NsaWVudC50aHJlYWRzUmVxdWVzdCIsIkRlYnVnQ2xpZW50LmV2YWx1YXRlUmVxdWVzdCIsIkRlYnVnQ2xpZW50LndhaXRGb3JFdmVudCIsIkRlYnVnQ2xpZW50LmNvbmZpZ3VyYXRpb25TZXF1ZW5jZSIsIkRlYnVnQ2xpZW50LmxhdW5jaCIsIkRlYnVnQ2xpZW50LmNvbmZpZ3VyYXRpb25Eb25lIiwiRGVidWdDbGllbnQuYXNzZXJ0U3RvcHBlZExvY2F0aW9uIiwiRGVidWdDbGllbnQu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