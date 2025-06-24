> **Fork Notice**  
> This repository is an *unmodified* fork of [microsoft/vscode-mono-debug](https://github.com/microsoft/vscode-mono-debug).  
> All authorship, copyright, and ownership remain with the original authors.  
> Modifications are limited to packaging and publication on the OpenVSX Marketplace.

# VS Code Mono Debug

A simple VS Code debugger extension for the Mono VM. Its implementation was inspired by the [SDB](https://github.com/mono/sdb) command line debugger.

![Mono Debug](images/mono-debug.png)

## Installing Mono

You can either download the latest Mono version for Linux, macOS, or Windows at the [Mono project website](https://www.mono-project.com/download/) or you can use your package manager.

* On OS X: `brew install mono`
* On Ubuntu, Debian, Raspbian: `sudo apt-get install mono-complete`
* On CentOS: `yum install mono-complete`
* On Fedora: `dnf install mono-complete`

## Enable Mono debugging

To enable debugging of Mono based C# (and F#) programs, you have to pass the `-debug` option to the compiler:

```bash
csc -debug Program.cs
```

If you want to attach the VS Code debugger to a Mono program, pass these additional arguments to the Mono runtime:

```bash
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

[![build status](https://github.com/microsoft/vscode-mono-debug/workflows/Extension%20CI/badge.svg)](https://github.com/microsoft/vscode-mono-debug/actions)

Building and using VS Code mono-debug requires a basic POSIX-like environment, a Bash-like
shell, and an installed Mono framework.

First, clone the mono-debug project:

```bash
$ git clone --recursive https://github.com/microsoft/vscode-mono-debug
```

To build the extension vsix, run:

```bash
$ cd vscode-mono-debug
$ npm install
$ make
```
