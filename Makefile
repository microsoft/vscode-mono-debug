
MONO_DEBUG = "bin/Release/mono-debug.exe"

all: vsix
	echo "done"

sdb/dep:
	git submodule update --init --recursive

sdb/bin: sdb/dep
	cd sdb; make -f Makefile

$MONO_DEBUG: sdb/bin
	xbuild /p:Configuration=Release

vsix: $MONO_DEBUG
	vsce package

clean:
	git clean -xfd