# VS Code Mono Debug

A simple VS Code debugger extension for the Mono VM. It is based on the [SDB](https://github.com/mono/sdb) command line debugger.

**Please note that this extension only works on OS X and linux because the underlying [SDB](https://github.com/mono/sdb) command line debugger does not support Windows.**

## Installing Mono

On Linux or OS X, the Mono debugging support of VS Code requires [Mono](http://www.mono-project.com/) version 3.12 or later.

You can either download the latest Mono version for Linux or OS X at [Mono project](http://www.mono-project.com/download/) or you can use your package manager.

* On OS X: `brew install mono`
* On Linux: `sudo apt-get install mono-complete`

## Enable Mono debugging

To enable debugging of Mono based C# (and F#) programs, you have to pass the `-debug` option to the compiler:

```
mcs -debug Program.cs
```

If you want to attach the VS Code debugger to a Mono program, pass these additional arguments to the Mono runtime:

```
mono --debug --debugger-agent=transport=dt_socket,server=y,address=127.0.0.1:55555 Program.exe
```

The corresponding attach `launch.json` configuration looks like this:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Attach to Mono",
            "request": "attach",
            "type": "mono",
            "address": "localhost",
            "port": 55555
        }
    ]
}
```

## Building the mono-debug extension

[![build status](https://travis-ci.org/Microsoft/vscode-mono-debug.svg?branch=master)](https://travis-ci.org/Microsoft/vscode-mono-debug)

Building and using VS Code mono-debug requires a basic POSIX-like environment, a Bash-like
shell, and an installed Mono framework.

First, clone the mono-debug project:

```
$ git clone https://github.com/Microsoft/vscode-mono-debug
```

To build the extension vsix, run:

```
$ cd vscode-mono-debug
$ npm install
$ make
```