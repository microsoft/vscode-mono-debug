# vscode-mono-debug
A simple VS Code debug adapter for mono

## Building

Building and using VS Code mono-debug requires a basic POSIX-like environment, a Bash-like
shell, and an installed Mono framework.

First, clone the mono-debug project:

	$ git clone https://github.com/Microsoft/vscode-mono-debug

To build the extension vsix, run:

	$ cd vscode-mono-debug
	$ make
