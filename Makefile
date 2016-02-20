
MONO_DEBUG_RELEASE = "./bin/Release/mono-debug.exe"
MONO_DEBUG_DEBUG = "./bin/Debug/mono-debug.exe"
SDB_EXE = "./sdb/bin/sdb.exe"
SDB_MAKE = "./sdb/Makefile"

all: vsix
	@echo "vsix created"

vsix: $MONO_DEBUG_RELEASE
	vsce package

$SDB_MAKE:
	git submodule update --init --recursive

$SDB_EXE: $SDB_MAKE
	cd sdb; make -f Makefile

build: $MONO_DEBUG_RELEASE
	tsc -p ./tests
	@echo "build finished"

debug: $MONO_DEBUG_DEBUG
	tsc -p ./tests
	@echo "build finished"

$MONO_DEBUG_RELEASE: $SDB_EXE
	xbuild /p:Configuration=Release mono-debug.sln

$MONO_DEBUG_DEBUG: $SDB_EXE
	xbuild /p:Configuration=Debug mono-debug.sln


clean:
	rm -rf sdb
	mkdir sdb
	git clean -xfd