#!/bin/bash

git submodule update --init --recursive

(cd sdb; make)

xbuild /p:Configuration=Release