
MONO_DEBUG_RELEASE = "./bin/Release/net45/mono-debug.exe"
MONO_DEBUG_DEBUG = "./bin/Debug/net45/mono-debug.exe"

all: vsix
	@echo "vsix created"

vsix: $MONO_DEBUG_DEBUG
	./node_modules/.bin/vsce package

publish: $MONO_DEBUG_RELEASE
	./node_modules/.bin/vsce publish

build: $MONO_DEBUG_RELEASE
	node_modules/.bin/tsc -p ./src/typescript
	@echo "build finished"

debug: $MONO_DEBUG_DEBUG
	node_modules/.bin/tsc -p ./src/typescript
	@echo "build finished"

$MONO_DEBUG_RELEASE:
	msbuild /p:Configuration=Debug mono-debug.sln

$MONO_DEBUG_DEBUG:
	msbuild /p:Configuration=Debug mono-debug.sln

tests:
	cd testdata/simple; make
	cd testdata/output; make
	cd testdata/simple_break; make
	cd testdata/fsharp; make

clean:
	git clean -xfd