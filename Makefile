
MONO_DEBUG = "./bin/Release/mono-debug.exe"
SDB_EXE = "./sdb/bin/sdb.exe"
SDB_MAKE = "./sdb/Makefile"

all: vsix
	echo "done"

vsix: $MONO_DEBUG
	vsce package

$SDB_MAKE:
	git submodule update --init --recursive

$SDB_EXE: $SDB_MAKE
	cd sdb; make -f Makefile

$MONO_DEBUG: $SDB_EXE
	xbuild /p:Configuration=Release


clean:
	rm -rf sdb
	mkdir sdb
	git clean -xfd