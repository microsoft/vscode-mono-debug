# VS Code Mono Debug

A simple VS Code debug extension for the Mono VM. It is based on the [SDB](https://github.com/mono/sdb) command line debugger.

Mono Debug was already bundled with VS Code up to version 0.10.9. Starting with VS Code 0.10.10 you will have to install this extension for debugging Mono programs.

**Please note that this extension only works on OS X and linux because the underlying [SDB](https://github.com/mono/sdb) command line debugger does not support Windows.**


## Building

[![build status](https://travis-ci.org/Microsoft/vscode-mono-debug.svg?branch=master)](https://travis-ci.org/Microsoft/vscode-mono-debug)

Building and using VS Code mono-debug requires a basic POSIX-like environment, a Bash-like
shell, and an installed Mono framework.

First, clone the mono-debug project:

	$ git clone https://github.com/Microsoft/vscode-mono-debug

To build the extension vsix, run:

	$ cd vscode-mono-debug
	$ make
