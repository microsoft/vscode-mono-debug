
MONO_DEBUG_RELEASE = "./bin/Release/mono-debug.exe"
MONO_DEBUG_DEBUG = "./bin/Debug/mono-debug.exe"

all: vsix
	@echo "vsix created"

vsix: $MONO_DEBUG_RELEASE
	./node_modules/.bin/vsce package

publish: $MONO_DEBUG_RELEASE
	./node_modules/.bin/vsce publish

build: $MONO_DEBUG_RELEASE tests
	node_modules/.bin/tsc -p ./src/typescript
	@echo "build finished"

debug: $MONO_DEBUG_DEBUG tests
	node_modules/.bin/tsc -p ./src/typescript
	@echo "build finished"

$MONO_DEBUG_RELEASE:
	xbuild /p:Configuration=Release mono-debug.sln

$MONO_DEBUG_DEBUG:
	xbuild /p:Configuration=Debug mono-debug.sln

tests:
	cd testdata/simple; make
	cd testdata/output; make
	cd testdata/simple_break; make

clean:
	git clean -xfd